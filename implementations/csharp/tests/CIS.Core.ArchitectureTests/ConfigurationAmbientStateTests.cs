using System.Reflection;
using System.Reflection.Emit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ConfigurationAmbientStateTests
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    private static readonly string[] ForbiddenCalls = ["System.Console.get_IsOutputRedirected", "System.Console.get_IsErrorRedirected", "System.Console.get_IsInputRedirected", "System.Diagnostics.Debugger.get_IsAttached", "System.Environment.GetEnvironmentVariables", "System.Environment.SetEnvironmentVariable", "System.AppDomain.add_UnhandledException", "System.AppDomain.add_FirstChanceException", "System.Threading.Tasks.TaskScheduler.add_UnobservedTaskException"];

    [TestMethod]
    public void ConfigurationHelpersDoNotQueryAmbientPresentationOrDumpOrMutateVariables()
    {
        Assembly core = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "CIS.Core.dll"));
        int checkedCalls = 0;
        foreach (Type type in core.GetTypes().Where(type => IsStartupType(type)))
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
        foreach (Type type in core.GetTypes().Where(type => IsStartupType(type)))
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

    [TestMethod]
    public void ActualFacadeHasOnlyPrivatePublicationStateAndATrivialExplicitStaticConstructor()
    {
        Type facade = typeof(CIS.EnvironmentOptions);
        Assert.AreEqual((TypeAttributes)0, facade.Attributes & TypeAttributes.BeforeFieldInit);
        FieldInfo[] fields = facade.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.AreEqual(3, fields.Length);
        Assert.IsTrue(fields.All(field => field.IsPrivate));
        Assert.AreEqual(1, fields.Count(field => field.FieldType == typeof(object) && field.IsInitOnly));
        Assert.AreEqual(1, fields.Count(field => field.FieldType == typeof(CIS.EnvironmentSnapshot) && !field.IsInitOnly));
        Assert.AreEqual(1, fields.Count(field => field.FieldType == typeof(bool) && !field.IsInitOnly));
        MethodBase[] calls = Calls(facade.TypeInitializer!).ToArray();
        Assert.AreEqual(1, calls.Length);
        Assert.AreEqual(typeof(object), calls[0].DeclaringType);
        Assert.AreEqual(".ctor", calls[0].Name);
        Assert.IsNull(facade.GetMethod("Reset", BindingFlags.Public | BindingFlags.Static));
        Assert.IsNull(typeof(CIS.EnvironmentOptions).Assembly.GetType("CIS.EnviormentsOptions"));
    }

    [TestMethod]
    [DataRow("Initialize")]
    [DataRow("Current")]
    [DataRow("IsInitialized")]
    public void PublicationReadsAndWritesUseTheIdenticalPrivateMonitorGate(string member)
    {
        Type facade = typeof(CIS.EnvironmentOptions);
        MethodBase method = member == "Initialize" ? facade.GetMethod(member)! : facade.GetProperty(member)!.GetMethod!;
        var instructions = Instructions(method).ToArray();
        var enter = instructions.Single(item => item.Member is MethodBase call && call.DeclaringType == typeof(Monitor) && call.Name == "Enter");
        var exit = instructions.Single(item => item.Member is MethodBase call && call.DeclaringType == typeof(Monitor) && call.Name == "Exit");
        var gate = instructions.Single(item => item.Code == OpCodes.Ldsfld && item.Member is FieldInfo field && field.Name == "Gate");
        Assert.IsTrue(gate.Offset < enter.Offset);
        Assert.AreEqual(facade, gate.Member!.DeclaringType);
        var accesses = instructions.Where(item => item.Member is FieldInfo field && field.Name == "current").ToArray();
        Assert.IsTrue(accesses.Length > 0);
        Assert.IsTrue(accesses.All(item => item.Offset > enter.Offset && item.Offset < exit.Offset));
    }

    [TestMethod]
    public void InitializationGuardIsSetBeforeConstructionAndClearedInsideFinally()
    {
        MethodInfo method = typeof(CIS.EnvironmentOptions).GetMethod("Initialize")!;
        var instructions = Instructions(method).ToArray();
        int create = instructions.Single(item => item.Member is MethodBase call && call.DeclaringType?.FullName == "CIS.Configuration.EnvironmentSnapshotFactory" && call.Name == "Create").Offset;
        int publish = instructions.Single(item => item.Code == OpCodes.Stsfld && item.Member is FieldInfo field && field.Name == "current").Offset;
        var stores = instructions.Where(item => item.Code == OpCodes.Stsfld && item.Member is FieldInfo field && field.Name == "initializing").ToArray();
        Assert.AreEqual(2, stores.Length);
        Assert.IsTrue(stores[0].Offset < create && create < publish && publish < stores[1].Offset);
        Assert.IsTrue(method.GetMethodBody()!.ExceptionHandlingClauses.Cast<ExceptionHandlingClause>().Any(clause => clause.Flags == ExceptionHandlingClauseOptions.Finally && clause.HandlerOffset <= stores[1].Offset && stores[1].Offset < clause.HandlerOffset + clause.HandlerLength));
    }

    [TestMethod]
    public void CompiledGateInspectorDistinguishesNeverInvokedUngatedFieldAccess()
    {
        MethodInfo probe = typeof(ConfigurationAmbientStateTests).GetMethod(nameof(UngatedFieldProbe), BindingFlags.NonPublic | BindingFlags.Static)!;
        var instructions = Instructions(probe).ToArray();
        Assert.IsTrue(instructions.Any(item => item.Code == OpCodes.Ldsfld && item.Member is FieldInfo));
        Assert.IsFalse(instructions.Any(item => item.Member is MethodBase call && call.DeclaringType == typeof(Monitor)));
    }

    private static readonly object UngatedValue = new();
    // Only its compiled IL is inspected.
    private static object UngatedFieldProbe() => UngatedValue;

    private static bool IsStartupType(Type type) =>
        type.Namespace is "CIS.Configuration" or "CIS.Logging.Configuration" ||
        type == typeof(CIS.EnvironmentOptions) || type == typeof(CIS.EnvironmentSetup) || type == typeof(CIS.EnvironmentSnapshot) ||
        type == typeof(CIS.Logging.LoggingSetup) || type == typeof(CIS.Logging.LoggingOptions) || type == typeof(CIS.Logging.EventMetadata);

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
        AppDomain.CurrentDomain.UnhandledException += static (_, _) => { };
        AppDomain.CurrentDomain.FirstChanceException += static (_, _) => { };
        TaskScheduler.UnobservedTaskException += static (_, _) => { };
        return Console.IsOutputRedirected || Console.IsErrorRedirected || Console.IsInputRedirected || System.Diagnostics.Debugger.IsAttached || Environment.GetEnvironmentVariables().Count > 0;
    }

    internal static IEnumerable<MethodBase> Calls(MethodBase method) => Instructions(method).Select(item => item.Member).OfType<MethodBase>();

    private static IEnumerable<(int Offset, OpCode Code, MemberInfo? Member)> Instructions(MethodBase method)
    {
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (int offset = 0; offset < bytes.Length;)
        {
            int instructionOffset = offset;
            byte first = bytes[offset++];
            short value = first == 0xfe ? (short)(0xfe00 | bytes[offset++]) : first;
            OpCode code = Codes[value];
            MemberInfo? member = null;
            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField)
            {
                int token = BitConverter.ToInt32(bytes, offset);
                member = method.Module.ResolveMember(token, method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
            }
            yield return (instructionOffset, code, member);
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
