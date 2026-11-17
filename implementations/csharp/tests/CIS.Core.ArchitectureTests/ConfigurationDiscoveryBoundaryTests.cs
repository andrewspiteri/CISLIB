using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ConfigurationDiscoveryBoundaryTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly IReadOnlyDictionary<short, OpCode> Codes = typeof(OpCodes).GetFields()
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    [TestMethod]
    public void ConfigurationUsesPreparedTypesWithoutEnumeratingOrLoadingAssemblies()
    {
        Type[] roots = typeof(IConfigurationObject).Assembly.GetTypes()
            .Where(type => type.DeclaringType is null && (type.Namespace == "CIS.Configuration"
                || type.Namespace?.StartsWith("CIS.Configuration.", StringComparison.Ordinal) == true)).ToArray();
        Assert.IsTrue(roots.Length > 0, "Inspect the actual production Configuration namespace.");
        foreach (Type root in roots) { Inspect(root); }
    }

    [TestMethod]
    [DataRow(nameof(EnumerationFixture))]
    [DataRow(nameof(AssemblyLoadFixture))]
    [DataRow(nameof(LoadedAssembliesFixture))]
    [DataRow(nameof(AsyncContextLoadFixture))]
    public void InspectorRejectsActualDiscoveryAndLoadingCallsIncludingGeneratedAsyncWork(string fixture)
    {
        Type source = typeof(ConfigurationDiscoveryBoundaryTests).GetNestedType(fixture, BindingFlags.NonPublic)!;
        Assert.ThrowsExactly<InvalidOperationException>(() => Inspect(source));
    }

    private static void Inspect(Type source)
    {
        MethodBase[] methods = source.GetMethods(Declared).Cast<MethodBase>().Concat(source.GetConstructors(Declared))
            .Concat(source.TypeInitializer is null ? [] : new[] { source.TypeInitializer }).Distinct().ToArray();
        foreach (MethodBase method in methods)
        {
            foreach (MethodBase target in Calls(method))
            {
                Type? owner = target.DeclaringType;
                bool forbidden = owner == typeof(Assembly)
                    && (target.Name is "GetTypes" or "GetExportedTypes" or "get_DefinedTypes" or "get_ExportedTypes"
                        || target.Name.StartsWith("Load", StringComparison.Ordinal))
                    || owner == typeof(AppDomain) && target.Name == nameof(AppDomain.GetAssemblies)
                    || owner == typeof(AssemblyLoadContext) && target.Name.StartsWith("LoadFromAssembly", StringComparison.Ordinal);
                if (forbidden)
                {
                    throw new InvalidOperationException("Configuration must query its prepared Type Manager: "
                        + source.FullName + "." + method.Name + " -> " + owner!.FullName + "." + target.Name);
                }
            }
        }
        foreach (Type nested in source.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) { Inspect(nested); }
    }

    // Same local IL/OpCodes pattern as the Type Manager and Startup boundaries.
    // Property/constructor reflection on known POCOs and Assembly.GetName remain allowed.
    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (int offset = 0; offset < bytes.Length;)
        {
            short key = bytes[offset++] == 0xfe ? unchecked((short)(0xfe00 | bytes[offset++])) : bytes[offset - 1];
            OpCode code = Codes[key];
            if (code.OperandType == OperandType.InlineMethod)
            {
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset), method.DeclaringType?.GetGenericArguments(),
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

    // These methods are inspected without invocation, loading or enumeration.
    private static class EnumerationFixture
    {
        internal static Type[] Read(Assembly assembly) => assembly.GetTypes();
    }
    private static class AssemblyLoadFixture
    {
        internal static Assembly Read(string name) => Assembly.Load(name);
    }
    private static class LoadedAssembliesFixture
    {
        internal static Assembly[] Read() => AppDomain.CurrentDomain.GetAssemblies();
    }
    private static class AsyncContextLoadFixture
    {
        internal static async Task<Assembly> ReadAsync(AssemblyLoadContext context, AssemblyName name)
        {
            await Task.Yield();
            return context.LoadFromAssemblyName(name);
        }
    }
}
