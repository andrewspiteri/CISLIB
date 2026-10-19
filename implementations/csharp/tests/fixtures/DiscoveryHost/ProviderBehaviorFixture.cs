using System.Collections;
using System.Net;
using System.Numerics;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderBehaviorFixture
{
    internal static readonly string[] Modes = ["filtered", "no-outputs", "stopped", "disposed", "all-faulted", "partial-fault", "full", "template-tail", "float", "duplicates", "root-shape", "rejection", "capture-failure", "formatter-failure"];
    private sealed class State(bool fail = false) : IEnumerable<KeyValuePair<string, object?>>
    {
        internal int Calls;
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        { Calls++; if (fail) { throw new InvalidOperationException("Controlled capture failure."); } return new[] { new KeyValuePair<string, object?>("value", 7) }.AsEnumerable().GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class SingleUse : IEnumerable
    {
        private int calls;
        public IEnumerator GetEnumerator()
        { if (++calls != 1) { throw new InvalidOperationException("Single-use argument enumerated again."); } return new object[] { 1 }.GetEnumerator(); }
    }
    private sealed class Bomb
    { public override string ToString() => throw new InvalidOperationException("Omitted value formatted."); }
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    private static async Task Join(Task task, bool expectedFault)
    { try { await task.WaitAsync(Guard); } catch (IOException) when (expectedFault) { Require(task.IsFaulted); } }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string? root = null)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal)); bool controlled = mode is "no-outputs" or "all-faulted" or "partial-fault" or "full";
        bool physical = root is not null && !controlled; bool fault = mode is "all-faulted" or "partial-fault";
        using var console = new MemoryStream(); using var file = new MemoryStream(); var entered = Signal(); var release = Signal(); int ioAttempts = 0;
        string? path = physical ? Path.Combine(root!, "logs Ω", "active.log") : null;
        var options = new LoggingOptions { ConsoleLevel = mode == "no-outputs" ? CoreLogLevel.None : mode == "filtered" ? CoreLogLevel.Warning : CoreLogLevel.Information, FileLevel = mode == "partial-fault" ? CoreLogLevel.Information : CoreLogLevel.None, FilePath = "controlled.jsonl", ConsoleFormat = LogOutputFormat.JsonLines, FileFormat = LogOutputFormat.JsonLines, MaxEntryBytes = 16384, QueueCapacity = mode == "full" ? 1 : 64, ShutdownTimeoutMilliseconds = 5000 };
        CoreLogRuntime runtime;
        if (physical)
        {
            LoggingOptions global = EnvironmentOptions.Logging; global.ConsoleLevel = CoreLogLevel.None; global.FileLevel = options.ConsoleLevel;
            global.FilePath = path; global.FileFormat = LogOutputFormat.JsonLines; global.MaxEntryBytes = 16384; global.QueueCapacity = 64; global.ShutdownTimeoutMilliseconds = 5000;
            runtime = CoreLogRuntime.Create();
        }
        else if (controlled)
        {
            runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, mode == "no-outputs" ? null : async item =>
            {
                if (++ioAttempts == 1) { entered.TrySetResult(); if (mode == "full") { await release.Task; } }
                if (fault) { throw new IOException("Controlled writer failure."); }
                await console.WriteAsync(item.Event.JsonLine);
            }, mode == "partial-fault" ? item => file.WriteAsync(item.Event.JsonLine) : null,
                consoleFlush: mode == "no-outputs" ? null : () => ValueTask.CompletedTask, fileFlush: mode == "partial-fault" ? () => ValueTask.CompletedTask : null);
        }
        else { runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), options, acquireConsole: _ => console); }
        using var provider = new CoreLoggerProvider(runtime); ILogger logger = provider.CreateLogger("Provider.Behavior"); var state = new State(); int formatterCalls = 0, rejected = 0; bool exceptionIdentity = false;
        bool enabledBefore = logger.IsEnabled(LogLevel.Information), enabledAtProbe = enabledBefore;
        void Log(object value, string name = "entry", string? message = "rendered") => logger.Log(LogLevel.Information, new EventId(17, name), value, null, (received, error) =>
        { formatterCalls++; Require(ReferenceEquals(received, value) && error is null); return message!; });
        try
        {
            switch (mode)
            {
                case "filtered":
                    Log(state); logger.Log(LogLevel.None, new EventId(17), state, null, (_, _) => { formatterCalls++; throw new InvalidOperationException("None formatter ran."); }); break;
                case "no-outputs": Log(state); break;
                case "stopped": await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard); enabledAtProbe = logger.IsEnabled(LogLevel.Information); Log(state); break;
                case "disposed": provider.Dispose(); enabledAtProbe = logger.IsEnabled(LogLevel.Information); Log(state); Require(!runtime.GetDiagnostics().AdmissionClosed); break;
                case "all-faulted":
                case "partial-fault":
                    Log(state, "first"); await Join(runtime.QueuedOutputs!.Console!.Completion, fault);
                    enabledAtProbe = logger.IsEnabled(LogLevel.Information); Log(state, "second"); break;
                case "full":
                    Log(state, "first"); await entered.Task.WaitAsync(Guard); Log(state, "second");
                    enabledAtProbe = logger.IsEnabled(LogLevel.Information); Log(state, "third"); release.TrySetResult(); break;
                case "template-tail":
                    logger.LogInformation(new EventId(17, "framework"), "Items {Items} Count {Count}", Enumerable.Repeat(0, 2000).ToArray(), 7);
                    Log(new KeyValuePair<string, object?>[] { new("Items", Enumerable.Repeat(0, 2000).ToArray()), new("bad\0key", new Bomb()), new("{OriginalFormat}", "bounded {Items}") }, "skipped-tail"); break;
                case "float": Log(new Dictionary<string, object?> { ["number"] = 0.1f }); break;
                case "duplicates":
                    Log(new KeyValuePair<string, object?>[] { new("nested", new KeyValuePair<string, object?>[] { new("same", 1), new("same", 2) }), new("{OriginalFormat}", "first"), new("{OriginalFormat}", "last") }); break;
                case "root-shape": Log(new Dictionary<string, int> { ["ignored"] = 7 }); break;
                case "rejection":
                    object[] values = [CoreLogLevel.Error, TimeSpan.Zero, new DateTime(2026, 10, 14), new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Local), new Uri("https://example.test"), IPAddress.Loopback, (nint)1, (nuint)1, (Int128)1, (UInt128)1, BigInteger.One, new Hashtable { [1] = "bad" }];
                    foreach (object value in values) { Log(new Dictionary<string, object?> { ["bad"] = value }); rejected++; }
                    Log(new Dictionary<string, object?> { ["{OriginalFormat}"] = "\ud800" }); rejected++;
                    Log(state, "bad\0name"); rejected++; Log(state, new string('x', 256)); rejected++; Log(state, "\ud800"); rejected++;
                    Log(state, message: null); rejected++; Log(state, message: "\ud800"); rejected++;
                    var once = new SingleUse(); logger.LogInformation("Single {Items}", once); rejected++;
                    Log(state, "after-rejections"); break;
                case "capture-failure": var failing = new State(fail: true); Log(failing); rejected++; Require(failing.Calls == 1); Log(state, "after-capture"); break;
                case "formatter-failure":
                    var expected = new InvalidOperationException("Original formatter failure.");
                    try { logger.Log(LogLevel.Information, new EventId(17), state, null, (_, _) => { formatterCalls++; throw expected; }); }
                    catch (InvalidOperationException error) { exceptionIdentity = ReferenceEquals(error, expected); }
                    Require(exceptionIdentity == !physical && runtime.GetDiagnostics().InvalidEventCount == (physical ? 1 : 0) && state.Calls == 0);
                    if (physical) { rejected++; }
                    Log(state, "after-formatter"); break;
            }
            release.TrySetResult(); LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard);
            await Join(runtime.QueuedOutputs!.Completion, fault); bool leaseReleased = false;
            byte[] bytes = physical ? File.ReadAllBytes(path!) : console.ToArray();
            if (physical) { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); leaseReleased = true; }
            JsonObject report = Common(mode); report["physical"] = physical; report["leaseReleased"] = leaseReleased;
            report["bytes"] = Convert.ToBase64String(bytes); report["fileBytes"] = Convert.ToBase64String(file.ToArray()); report["formatterCalls"] = formatterCalls;
            report["stateCalls"] = state.Calls; report["rejected"] = rejected; report["exceptionIdentity"] = exceptionIdentity;
            report["formatterFailureContained"] = mode == "formatter-failure" && physical && !exceptionIdentity;
            report["enabledBefore"] = enabledBefore; report["enabledAtProbe"] = enabledAtProbe; report["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount);
            report["truncated"] = Number(runtime.GetDiagnostics().TruncatedEventCount); report["outputs"] = Diagnostics(runtime); report["result"] = StopJoinFixture.Json(stop); return report;
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync().AsTask().WaitAsync(Guard); await Join(runtime.QueuedOutputs!.Completion, fault); }
    }
}
