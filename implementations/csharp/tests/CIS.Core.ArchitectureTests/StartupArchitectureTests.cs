using System.Reflection;
using System.Reflection.Emit;
using CIS.Heartbeat;
using CIS.Startup;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class StartupArchitectureTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly IReadOnlyDictionary<short, OpCode> Codes = typeof(OpCodes).GetFields()
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    [TestMethod]
    public void CoordinatorAndItsGeneratedWorkCannotReferenceHeartbeatOrConfiguration()
        => AssertIndependent(typeof(StartupCoordinator));

    [TestMethod]
    [DataRow(nameof(ConstructorDependency))]
    [DataRow(nameof(FieldDependency))]
    [DataRow(nameof(GenericSignatureDependency))]
    [DataRow(nameof(BodyOnlyDependency))]
    [DataRow(nameof(AsyncBodyOnlyDependency))]
    [DataRow(nameof(TypeTokenDependency))]
    [DataRow(nameof(ConfigurationFieldDependency))]
    public void BoundaryRejectsRealSignatureFieldAndBodyDependencies(string fixture)
    {
        Type source = typeof(StartupArchitectureTests).GetNestedType(fixture, BindingFlags.NonPublic)!;
        Assert.ThrowsExactly<InvalidOperationException>(() => AssertIndependent(source));
    }

    // Use the same compiled-metadata/IL pattern as the existing Healthcheck and
    // Type Manager boundaries. Hosting is deliberately outside this coordinator boundary.
    private static void AssertIndependent(Type root)
    {
        foreach (Type source in SelfAndNested(root))
        {
            foreach ((Type dependency, string site) in Dependencies(source))
            {
                foreach (Type target in ContainedTypes(dependency, new HashSet<Type>()))
                {
                    if (target.Namespace == "CIS.Heartbeat"
                        || target.Namespace?.StartsWith("CIS.Heartbeat.", StringComparison.Ordinal) == true
                        || target.Namespace == "CIS.Configuration"
                        || target.Namespace?.StartsWith("CIS.Configuration.", StringComparison.Ordinal) == true)
                    {
                        throw new InvalidOperationException("Startup coordinator depends on an optional runtime module: " + site + " -> " + target.FullName);
                    }
                }
            }
        }
    }

    private static IEnumerable<Type> SelfAndNested(Type source)
    {
        yield return source;
        foreach (Type nested in source.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (Type type in SelfAndNested(nested)) { yield return type; }
        }
    }

    private static IEnumerable<(Type Type, string Site)> Dependencies(Type source)
    {
        string site = source.FullName!;
        if (source.BaseType is not null) { yield return (source.BaseType, site); }
        foreach (Type dependency in source.GetInterfaces().Concat(source.GetGenericArguments())) { yield return (dependency, site); }
        foreach (FieldInfo field in source.GetFields(Declared)) { yield return (field.FieldType, site + "." + field.Name); }
        MethodBase[] methods = source.GetMethods(Declared).Cast<MethodBase>().Concat(source.GetConstructors(Declared))
            .Concat(source.TypeInitializer is null ? [] : new[] { source.TypeInitializer }).Distinct().ToArray();
        foreach (MethodBase method in methods)
        {
            string methodSite = site + "." + method.Name;
            foreach (Type dependency in MemberTypes(method)) { yield return (dependency, methodSite); }
            MethodBody? body = method.GetMethodBody();
            if (body is null) { continue; }
            foreach (LocalVariableInfo local in body.LocalVariables) { yield return (local.LocalType, methodSite); }
            foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
            {
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause && clause.CatchType is not null)
                { yield return (clause.CatchType, methodSite); }
            }
            foreach (MemberInfo member in BodyMembers(method, body))
            {
                foreach (Type dependency in MemberTypes(member)) { yield return (dependency, methodSite); }
            }
        }
    }

    private static IEnumerable<Type> MemberTypes(MemberInfo member)
    {
        if (member is Type type) { yield return type; yield break; }
        if (member.DeclaringType is not null) { yield return member.DeclaringType; }
        if (member is FieldInfo field) { yield return field.FieldType; }
        if (member is MethodBase method)
        {
            foreach (ParameterInfo parameter in method.GetParameters()) { yield return parameter.ParameterType; }
            if (method is MethodInfo info) { yield return info.ReturnType; }
            if (method.IsGenericMethod)
            {
                foreach (Type argument in method.GetGenericArguments()) { yield return argument; }
            }
        }
    }

    private static IEnumerable<Type> ContainedTypes(Type type, HashSet<Type> seen)
    {
        if (!seen.Add(type)) { yield break; }
        yield return type;
        IEnumerable<Type> inner = type.HasElementType ? [type.GetElementType()!] : type.IsGenericParameter
            ? type.GetGenericParameterConstraints() : type.IsGenericType ? type.GetGenericArguments() : [];
        if (type.IsFunctionPointer) { inner = type.GetFunctionPointerParameterTypes().Append(type.GetFunctionPointerReturnType()); }
        foreach (Type argument in inner)
        {
            foreach (Type contained in ContainedTypes(argument, seen)) { yield return contained; }
        }
    }

    private static IEnumerable<MemberInfo> BodyMembers(MethodBase method, MethodBody body)
    {
        byte[] bytes = body.GetILAsByteArray() ?? [];
        for (int offset = 0; offset < bytes.Length;)
        {
            short key = bytes[offset++] == 0xfe ? unchecked((short)(0xfe00 | bytes[offset++])) : bytes[offset - 1];
            OpCode code = Codes[key];
            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                yield return method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset), method.DeclaringType?.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : null)!;
            }
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }
    }

    // These are inspected without construction or invocation. Body-only probes
    // have neutral object signatures, so a metadata-signature-only guard misses them.
    private sealed class ConstructorDependency
    {
        public ConstructorDependency(HeartbeatRuntime runtime) { GC.KeepAlive(runtime); }
    }
    private sealed class FieldDependency
    {
        internal HeartbeatRuntime? Runtime { get; set; }
    }
    private sealed class ConfigurationFieldDependency
    {
        internal global::CIS.Configuration.ConfigurationRuntime? Runtime { get; set; }
    }
    private static class GenericSignatureDependency
    {
        internal static IEnumerable<HeartbeatRuntime> Read() => Array.Empty<HeartbeatRuntime>();
    }
    private static class BodyOnlyDependency
    {
        internal static void Stop(object runtime) => ((HeartbeatRuntime)runtime).BeginStopping();
    }
    private static class AsyncBodyOnlyDependency
    {
        internal static async Task StopAsync(object runtime)
        {
            await Task.Yield();
            ((HeartbeatRuntime)runtime).BeginStopping();
        }
    }
    private static class TypeTokenDependency
    {
        internal static Type Read() => typeof(HeartbeatRuntime);
    }
}
