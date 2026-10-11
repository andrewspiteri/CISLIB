using System.Reflection;
using System.Reflection.Emit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ConfigurationAmbientStateTests
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    private static readonly string[] ForbiddenCalls = ["System.Console.get_IsOutputRedirected", "System.Console.get_IsErrorRedirected", "System.Console.get_IsInputRedirected", "System.Diagnostics.Debugger.get_IsAttached", "System.Environment.GetEnvironmentVariables", "System.Environment.SetEnvironmentVariable"];

    [TestMethod]
    public void ConfigurationHelpersDoNotQueryAmbientPresentationOrDumpOrMutateVariables()
    {
        Assembly core = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "CIS.Core.dll"));
        int checkedCalls = 0;
        foreach (Type type in core.GetTypes().Where(type => type.Namespace is "CIS.Configuration" or "CIS.Logging.Configuration"))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            IEnumerable<MethodBase> methods = type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags));
            if (type.TypeInitializer is { } initializer) { methods = methods.Append(initializer).DistinctBy(method => method.MetadataToken); }
            foreach (MethodBase method in methods)
            {
                foreach (MethodBase called in Calls(method))
                {
                    checkedCalls++;
                    string name = called.DeclaringType?.FullName + "." + called.Name;
                    Assert.IsFalse(ForbiddenCalls.Contains(name, StringComparer.Ordinal), $"{type.FullName}.{method.Name} references {name}");
                }
            }
        }
        Assert.IsTrue(checkedCalls > 0, "No compiled configuration calls were inspected.");
    }

    [TestMethod]
    public void CompiledCallGuardRecognizesEveryForbiddenAmbientOperation()
    {
        MethodInfo probe = typeof(ConfigurationAmbientStateTests).GetMethod(nameof(AmbientProbe), BindingFlags.NonPublic | BindingFlags.Static)!;
        string[] calls = Calls(probe).Select(called => called.DeclaringType?.FullName + "." + called.Name).ToArray();
        foreach (string name in ForbiddenCalls)
        {
            Assert.IsTrue(calls.Contains(name, StringComparer.Ordinal), name);
        }
    }

    // This method is inspected as IL only; it is never invoked by the tests.
    private static bool AmbientProbe()
    {
        Environment.SetEnvironmentVariable("CIS_NEVER_INVOKE_AMBIENT_PROBE", "not-executed");
        return Console.IsOutputRedirected || Console.IsErrorRedirected || Console.IsInputRedirected || System.Diagnostics.Debugger.IsAttached || Environment.GetEnvironmentVariables().Count > 0;
    }

    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (int offset = 0; offset < bytes.Length;)
        {
            byte first = bytes[offset++];
            short value = first == 0xfe ? (short)(0xfe00 | bytes[offset++]) : first;
            OpCode code = Codes[value];
            if (code.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(bytes, offset);
                MethodBase? called = method.Module.ResolveMethod(token, method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (called is not null) { yield return called; }
            }
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(bytes, offset)),
                _ => throw new InvalidDataException("Unknown compiled IL operand.")
            };
        }
    }
}
