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

    [TestMethod]
    public void ConfigurationValidationAndStoredGettersHaveNoOutputProbeNetworkOrWorkerCalls()
    {
        Assembly core = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "CIS.Core.dll"));
        int checkedMethods = 0;
        foreach (Type type in core.GetTypes().Where(type => type.Namespace is "CIS.Configuration" or "CIS.Logging.Configuration"))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            IEnumerable<MethodBase> methods = type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags));
            if (type.TypeInitializer is { } initializer) { methods = methods.Append(initializer).DistinctBy(method => method.MetadataToken); }
            foreach (MethodBase method in methods)
            {
                checkedMethods++;
                foreach (MethodBase called in Calls(method))
                {
                    Assert.IsFalse(IsInfrastructureCall(called), $"{type.FullName}.{method.Name} references {called.DeclaringType?.FullName}.{called.Name}");
                }
            }
        }
        Assert.IsTrue(checkedMethods > 0, "No compiled configuration methods were inspected.");
    }

    [TestMethod]
    public void CompiledInfrastructureGuardRecognizesNeverInvokedNegativeControls()
    {
        MethodInfo probe = typeof(ConfigurationAmbientStateTests).GetMethod(nameof(InfrastructureProbe), BindingFlags.NonPublic | BindingFlags.Static)!;
        string[] actual = Calls(probe).Where(IsInfrastructureCall).Select(called => called.DeclaringType?.FullName + "." + called.Name).ToArray();
        foreach (string expected in new[] { "System.IO.Directory.CreateDirectory", "System.IO.File.OpenWrite", "System.IO.File.Exists", "System.Net.Http.HttpClient..ctor", "System.Threading.Thread..ctor", "System.Threading.Thread.Start", "System.Threading.Tasks.Task.Run" })
        {
            Assert.IsTrue(actual.Contains(expected, StringComparer.Ordinal), expected);
        }
        Assert.IsFalse(Calls(typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!).Any(IsInfrastructureCall));
    }

    private static bool IsInfrastructureCall(MethodBase called)
    {
        Type? type = called.DeclaringType;
        string? name = type?.FullName;
        if (type?.Namespace?.StartsWith("System.Net", StringComparison.Ordinal) == true) { return true; }
        if (name is "System.Console" or "System.IO.File" or "System.IO.FileInfo" or "System.IO.FileStream" or "System.IO.DirectoryInfo" or "System.IO.FileSystemWatcher" or "System.IO.StreamReader" or "System.IO.StreamWriter") { return true; }
        if (name == "System.IO.Directory") { return called.Name != nameof(Directory.GetCurrentDirectory); }
        if (name is "System.Threading.Thread" or "System.Threading.ThreadPool" or "System.Threading.Timer" or "System.Threading.PeriodicTimer") { return true; }
        return name switch
        {
            "System.Threading.Tasks.Task" => called.Name is "Run" or "Start" or "Delay",
            "System.Threading.Tasks.TaskFactory" => called.Name == "StartNew",
            "System.Diagnostics.Process" => called.Name == "Start",
            "System.Threading.CancellationTokenSource" => called.Name == "CancelAfter",
            _ => false
        };
    }

    // Compiled IL is inspected only. This method is never invoked.
    private static void InfrastructureProbe()
    {
        Directory.CreateDirectory("CIS_NEVER_INVOKE_INFRASTRUCTURE_PROBE");
        using Stream output = File.OpenWrite("CIS_NEVER_INVOKE_INFRASTRUCTURE_PROBE");
        _ = File.Exists("CIS_NEVER_INVOKE_INFRASTRUCTURE_PROBE");
        using var client = new System.Net.Http.HttpClient();
        var worker = new Thread(static () => { });
        worker.Start();
        _ = Task.Run(static () => { });
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
