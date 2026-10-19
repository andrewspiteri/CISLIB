using System.Reflection;
using System.Reflection.Emit;
using CIS.Logging;
using CIS.Logging.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class DiagnosticsArchitectureTests
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);
    [TestMethod]
    public void DiagnosticIlDoesNotRouteDoIoQueryCallerSourcesOrCreateTasks() => Inspect(typeof(CoreLogRuntime).GetMethod(nameof(CoreLogRuntime.GetDiagnostics))!);
    [TestMethod]
    [DataRow(nameof(RouteProbe))]
    [DataRow(nameof(IoProbe))]
    [DataRow(nameof(TaskProbe))]
    [DataRow(nameof(EnvironmentProbe))]
    [DataRow(nameof(ClockProbe))]
    [DataRow(nameof(QueueProbe))]
    [DataRow(nameof(ThreadPoolProbe))]
    [DataRow(nameof(ThreadProbe))]
    [DataRow(nameof(TimerProbe))]
    [DataRow(nameof(CombinedTaskProbe))]
    [DataRow(nameof(RuntimeCompletionProbe))]
    public void InspectorRejectsActualForbiddenDiagnosticCallSites(string name) => Assert.ThrowsExactly<InvalidDataException>(() => Inspect(typeof(DiagnosticsArchitectureTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!));
    private static LogWriteResult RouteProbe(CoreLogger logger) => logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "probe" });
    private static void IoProbe() => File.WriteAllText("not-executed", "not-executed");
    private static Task TaskProbe() => Task.Run(() => { });
    private static string? EnvironmentProbe() => Environment.GetEnvironmentVariable("CIS_TEST_NOT_READ");
    private static DateTimeOffset ClockProbe(TimeProvider caller) => caller.GetUtcNow();
    private static void QueueProbe(OutputEventQueue queue) => queue.Complete();
    private static bool ThreadPoolProbe() => ThreadPool.QueueUserWorkItem(_ => { });
    private static Thread ThreadProbe() => new Thread(() => { });
    private static Timer TimerProbe() => new Timer(_ => { }, null, 0, 1);
    private static Task CombinedTaskProbe(Task first, Task second) => Task.WhenAll(first, second);
    private static Task RuntimeCompletionProbe(CoreLogRuntime runtime) => runtime.Completion;
    private static bool AllowedShippingTarget(MethodBase target)
    {
        string name = target.DeclaringType?.FullName ?? "";
        return name switch
        {
            "CIS.Logging.CoreLogRuntime" => target.Name is "GetDiagnostics" or "CaptureAccounting" or "get_IsAdmissionClosed" or "get_InvalidEventCount" or "get_TruncatedEventCount" or "AddLoss" || target.Name.StartsWith("<GetDiagnostics>b__", StringComparison.Ordinal),
            "CIS.Logging.Runtime.OutputQueues" => target.Name is "get_Console" or "get_File" or "get_Entries",
            "CIS.Logging.Runtime.OutputQueues+<>c" => target.Name.StartsWith("<get_Console>b__", StringComparison.Ordinal) || target.Name.StartsWith("<get_File>b__", StringComparison.Ordinal),
            "CIS.Logging.Runtime.IncomingEventQueue" => target.Name is "Capture" or "get_Completion" or "get_IsClosed",
            "CIS.Logging.Runtime.IncomingRingBuffer" => target.Name is "Capture" or "get_ActivePublishers" or "get_IsClosed",
            "CIS.Logging.Runtime.AtomicLogCounter" => target.Name is "get_Value" or "get_IsSaturated",
            "CIS.Logging.Runtime.IncomingRingSnapshot" => target.IsConstructor || target.Name.StartsWith("get_", StringComparison.Ordinal),
            "CIS.Logging.CoreLogRuntime+Output" => target.Name is "get_CallerFiltered" or "get_CallerUnavailable" or "get_CallerFilteredCounter" or "get_CallerUnavailableCounter" or "get_Name",
            "CIS.Logging.Runtime.IncomingFlushBoundary" => target.Name is "get_Observation" or "get_Target" or "get_QueueFull",
            "CIS.Logging.Runtime.IncomingAccounting" or "CIS.Logging.LogIncomingSnapshot" => target.IsConstructor || target.Name.StartsWith("get_", StringComparison.Ordinal),
            "CIS.Logging.Runtime.OutputEventQueue" => target.Name == "CaptureAccounting",
            "CIS.Logging.Runtime.RuntimeAccounting" or "CIS.Logging.Runtime.OutputAccounting" or "CIS.Logging.CoreLogDiagnostics" or "CIS.Logging.LogOutputDiagnostics" => target.IsConstructor || target.Name.StartsWith("get_", StringComparison.Ordinal) || name == "CIS.Logging.Runtime.OutputAccounting" && target.Name == "SaturatingSum",
            "CIS.Logging.CoreLogRuntime+<>c" => target.Name.StartsWith("<GetDiagnostics>b__", StringComparison.Ordinal),
            _ => false
        };
    }
    private static void Inspect(MethodBase root)
    {
        var pending = new Stack<MethodBase>(); pending.Push(root); var visited = new HashSet<MethodBase>();
        while (pending.TryPop(out MethodBase? method))
        {
            if (!visited.Add(method)) { continue; }
            byte[]? body = method.GetMethodBody()?.GetILAsByteArray(); if (body is null) { continue; }
            for (int offset = 0; offset < body.Length;)
            {
                short key = body[offset++] == 0xFE ? unchecked((short)(0xFE00 | body[offset++])) : body[offset - 1]; OpCode code = Codes[key];
                if (code.OperandType == OperandType.InlineMethod)
                {
                    MethodBase target = method.Module.ResolveMethod(BitConverter.ToInt32(body, offset), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null)!;
                    string name = target.DeclaringType?.FullName ?? "";
                    if (name is "System.Console" or "System.Environment" or "System.TimeProvider" or "CIS.EnvironmentOptions" or "CIS.Logging.LoggingOptions" or "CIS.Logging.CoreLogger" or "System.Diagnostics.Trace" or "System.Diagnostics.Debug" or "System.Diagnostics.Activity" or "System.Threading.ThreadPool" or "System.Threading.Thread" or "System.Threading.Timer"
                        || name.StartsWith("System.IO.", StringComparison.Ordinal) || name.StartsWith("System.Threading.Tasks.", StringComparison.Ordinal) && !(name == "System.Threading.Tasks.Task" && target.Name == "get_IsCompleted")
                        || name.StartsWith("System.Threading.Channels.", StringComparison.Ordinal) && target.Name is not ("get_Reader" or "get_Count")
                        || target.Module.Assembly == typeof(CoreLogRuntime).Assembly && !AllowedShippingTarget(target))
                    { throw new InvalidDataException("Diagnostics call a forbidden route/source/I/O/task: " + name + "." + target.Name); }
                    if (target.Module.Assembly == typeof(CoreLogRuntime).Assembly) { pending.Push(target); }
                }
                offset += code.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(body, offset),
                    _ => 4
                };
            }
        }
    }
}
