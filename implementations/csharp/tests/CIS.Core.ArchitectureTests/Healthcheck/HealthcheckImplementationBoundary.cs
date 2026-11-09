using System.Reflection;
using System.Reflection.Emit;

namespace CIS.Tests.ArchitectureTests.Healthcheck;

// TI section 3 allows HTTP in Core, but keeps policy/state and transport ownership separate.
internal static class HealthcheckImplementationBoundary
{
    private const string Prefix = "CIS.Healthcheck.";
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly IReadOnlyDictionary<short, OpCode> Codes = typeof(OpCodes).GetFields()
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    internal static void Assert(Type source)
    {
        foreach ((Type dependency, string site) in Dependencies(source))
        {
            foreach (Type target in ContainedTypes(dependency, new HashSet<Type>()))
            {
                string space = target.Namespace ?? "";
                string reason = space.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal)
                    || space.StartsWith("CIS.Heartbeat", StringComparison.Ordinal) || space.StartsWith("CIS.Startup", StringComparison.Ordinal)
                    ? "Infrastructure dependency"
                    : !IsHttp(source) && space.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ? "HTTP dependency"
                    : IsPolicy(source) && (space.StartsWith("System.IO.Pipes", StringComparison.Ordinal)
                        || space.StartsWith("System.Net", StringComparison.Ordinal) || IsTransport(target)) ? "Policy owns transport dependency"
                    : IsTransport(source) && (IsPolicy(target) || IsOwner(target)) ? "Transport owns policy dependency" : "";
                if (reason.Length != 0) { throw new InvalidOperationException(reason + ": " + site + " -> " + target.FullName); }
            }
        }
    }

    private static bool InNamespace(Type type, string space) => type.Namespace == space
        || type.Namespace?.StartsWith(space + ".", StringComparison.Ordinal) == true;
    private static bool IsHttp(Type type) => InNamespace(type, Prefix + "AspNetCore");
    private static Type Root(Type type) => type.DeclaringType is null ? type : Root(type.DeclaringType);
    private static bool IsPolicy(Type type) => InNamespace(type, Prefix + "Internal")
        || Root(type).FullName is Prefix + "HealthcheckService" or Prefix + "HealthCollectorRunner" or Prefix + "HealthExecutionBinder";
    private static bool IsTransport(Type type) => IsHttp(type) || InNamespace(type, Prefix + "Pipes")
        || Root(type).FullName == Prefix + "HealthPipeServer";
    private static bool IsOwner(Type type) => type.Namespace == "CIS.Healthcheck" && type.Name is
        "IHealthMonitorFactory" or "IHealthMonitor" or "IConditionMonitor" or "IConsecutiveFailureMonitor" or "INumericThresholdMonitor"
        or "IAvailabilityMonitor" or "IHealthCollector" or "IHealthCollectionLease" or "IHealthCollectionRegistration" or "IHealthExecutionBinding";

    private static IEnumerable<Type> ContainedTypes(Type type, HashSet<Type> seen)
    {
        if (!seen.Add(type)) { yield break; }
        yield return type;
        IEnumerable<Type> inner = type.HasElementType ? [type.GetElementType()!] : type.IsGenericParameter
            ? type.GetGenericParameterConstraints() : type.IsGenericType ? type.GetGenericArguments() : [];
        if (type.IsFunctionPointer) { inner = type.GetFunctionPointerParameterTypes().Append(type.GetFunctionPointerReturnType()); }
        foreach (Type item in inner)
        {
            foreach (Type contained in ContainedTypes(item, seen)) { yield return contained; }
        }
    }

    private static IEnumerable<(Type Type, string Site)> Dependencies(Type type)
    {
        string site = type.FullName!;
        if (type.BaseType is not null) { yield return (type.BaseType, site); }
        foreach (Type target in type.GetInterfaces().Concat(type.GetGenericArguments())) { yield return (target, site); }
        foreach (Type target in AttributeTypes(type.GetCustomAttributesData())) { yield return (target, site); }
        foreach (FieldInfo field in type.GetFields(Declared))
        {
            foreach (Type target in MemberTypes(field)) { yield return (target, site + "." + field.Name); }
        }
        MethodBase[] methods = type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared))
            .Concat(type.TypeInitializer is null ? [] : new[] { type.TypeInitializer }).Distinct().ToArray();
        foreach (MethodBase method in methods)
        {
            string methodSite = site + "." + method.Name;
            foreach (Type target in MemberTypes(method)) { yield return (target, methodSite); }
            MethodBody? body = method.GetMethodBody();
            if (body is null) { continue; }
            foreach (LocalVariableInfo local in body.LocalVariables) { yield return (local.LocalType, methodSite); }
            foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
            {
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause && clause.CatchType is not null) { yield return (clause.CatchType, methodSite); }
            }
            foreach (MemberInfo member in BodyMembers(method, body))
            {
                foreach (Type target in MemberTypes(member)) { yield return (target, methodSite); }
            }
        }
    }

    private static IEnumerable<Type> MemberTypes(MemberInfo member)
    {
        if (member is Type type) { yield return type; yield break; }
        if (member.DeclaringType is not null) { yield return member.DeclaringType; }
        foreach (Type target in AttributeTypes(member.GetCustomAttributesData())) { yield return target; }
        if (member is FieldInfo field)
        {
            yield return field.FieldType;
            foreach (Type modifier in field.GetRequiredCustomModifiers().Concat(field.GetOptionalCustomModifiers())) { yield return modifier; }
        }
        if (member is MethodBase method)
        {
            IEnumerable<ParameterInfo> parameters = method.GetParameters();
            if (method is MethodInfo info) { parameters = parameters.Append(info.ReturnParameter); }
            foreach (ParameterInfo parameter in parameters)
            {
                yield return parameter.ParameterType;
                foreach (Type modifier in parameter.GetRequiredCustomModifiers().Concat(parameter.GetOptionalCustomModifiers())) { yield return modifier; }
                foreach (Type target in AttributeTypes(parameter.GetCustomAttributesData())) { yield return target; }
            }
            if (method.IsGenericMethod)
            {
                foreach (Type argument in method.GetGenericArguments()) { yield return argument; }
            }
        }
    }

    private static IEnumerable<Type> AttributeTypes(IEnumerable<CustomAttributeData> attributes)
    {
        foreach (CustomAttributeData attribute in attributes)
        {
            yield return attribute.AttributeType;
            foreach (CustomAttributeTypedArgument argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(item => item.TypedValue)))
            {
                foreach (Type target in AttributeArgumentTypes(argument)) { yield return target; }
            }
        }
    }

    private static IEnumerable<Type> AttributeArgumentTypes(CustomAttributeTypedArgument argument)
    {
        yield return argument.ArgumentType;
        if (argument.Value is Type type) { yield return type; }
        if (argument.Value is IEnumerable<CustomAttributeTypedArgument> items)
        {
            foreach (CustomAttributeTypedArgument item in items)
            {
                foreach (Type target in AttributeArgumentTypes(item)) { yield return target; }
            }
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
}
