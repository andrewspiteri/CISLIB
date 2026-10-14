using System.Diagnostics;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderScopeFixture
{
    internal static readonly string[] Modes = ["owned", "external", "hybrid", "extensions", "async-owned", "async-external", "bounds", "invalid", "host-incompatible", "depth", "filtered", "correlation", "replacement"];
    private sealed class ObservedScopes(Action? onCapture = null, bool fail = false) : IExternalScopeProvider
    {
        internal readonly LoggerExternalScopeProvider Inner = new(); internal int Calls;
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
        { Calls++; onCapture?.Invoke(); if (fail) { throw new InvalidOperationException("Filtered host scopes captured."); } Inner.ForEachScope(callback, state); }
        public IDisposable Push(object? state) => Inner.Push(state);
    }
    private sealed class Fields : IEnumerable<KeyValuePair<string, object?>>
    {
        internal int Visits;
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        { for (int i = 0; i < 2000; i++) { Visits++; Require(Visits <= 1030); yield return new("k" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i); } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string? root = null)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal)); using var bytes = new MemoryStream(); bool physical = root is not null;
        string? path = physical ? Path.Combine(root!, "logs Ω", "active.log") : null; CoreLogRuntime runtime;
        var options = new LoggingOptions { ConsoleLevel = mode == "filtered" ? CoreLogLevel.Warning : CoreLogLevel.Information, FileLevel = CoreLogLevel.None, ConsoleFormat = LogOutputFormat.JsonLines, QueueCapacity = 64, MaxEntryBytes = 16384, ShutdownTimeoutMilliseconds = 5000 };
        if (physical)
        {
            LoggingOptions global = EnvironmentOptions.Logging; global.ConsoleLevel = CoreLogLevel.None; global.FileLevel = options.ConsoleLevel;
            global.FilePath = path; global.FileFormat = LogOutputFormat.JsonLines; global.QueueCapacity = 64; global.MaxEntryBytes = 16384; global.ShutdownTimeoutMilliseconds = 5000; runtime = CoreLogRuntime.Create();
        }
        else { runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), options, acquireConsole: _ => bytes); }
        using var provider = new CoreLoggerProvider(runtime); ILogger logger = provider.CreateLogger("Provider.Scope"); var host = new LoggerExternalScopeProvider(); int formatters = 0, ownedErrors = 0; var fields = new Fields(); string? trace = null, span = null;
        void Log(string name = "entry", Dictionary<string, object?>? attributes = null) => logger.Log(LogLevel.Information, new EventId(17, name), attributes ?? new(), null, (_, _) => { Interlocked.Increment(ref formatters); return "rendered"; });
        var observed = new ObservedScopes(fail: mode == "filtered");
        try
        {
            switch (mode)
            {
                case "owned":
                    object?[] array = [1, true]; var input = new Dictionary<string, object?> { ["owner"] = "original", ["items"] = array };
                    using (logger.BeginScope(input)) { input["owner"] = "changed"; array[0] = 999; Log(); input.Clear(); }
                    break;
                case "external":
                    provider.SetScopeProvider(host); var attrs = new Dictionary<string, LogValue> { ["owner"] = LogValue.String("before") }; var context = new OperationContextData { OperationId = "before", Attributes = attrs };
                    using (host.Push(context)) { context.OperationId = "host.operation"; attrs["owner"] = LogValue.String("at-capture"); Log(); attrs.Clear(); context.OperationId = "after"; }
                    break;
                case "hybrid":
                    provider.SetScopeProvider(host);
                    using (logger.BeginScope(new OperationContextData { OperationId = "core.operation", Attributes = new Dictionary<string, LogValue> { ["common"] = LogValue.String("core"), ["core"] = LogValue.Boolean(true) } }))
                    using (host.Push(new OperationContextData { OperationId = "host.operation", Attributes = new Dictionary<string, LogValue> { ["common"] = LogValue.String("host"), ["host"] = LogValue.Boolean(true) } }))
                    {
                        Log("host"); Log("event", new() { ["common"] = "event" });
                        Accepted(runtime.CreateLogger("Direct").Write(CoreLogLevel.Information, new LogEventData { EventName = "direct", Message = "rendered" }));
                    }
                    break;
                case "extensions": using (logger.BeginScope("Batch {Owner}", "Ω")) { Log(); } break;
                case "async-owned":
                case "async-external":
                    if (mode == "async-external") { provider.SetScopeProvider(host); }
                    var ready = Signal(); var go = Signal(); int arrivals = 0;
                    await Task.WhenAll(Enumerable.Range(1, 2).Select(i => Task.Run(async () =>
                    {
                        var context = new OperationContextData { OperationId = "operation." + i, Attributes = new Dictionary<string, LogValue> { ["owner"] = LogValue.Integer(i) } };
                        using (mode == "async-owned" ? logger.BeginScope(context)! : host.Push(context))
                        { if (Interlocked.Increment(ref arrivals) == 2) { ready.TrySetResult(); } await go.Task.WaitAsync(Guard); await Task.Yield(); Log("operation." + i); }
                    })).Append(Task.Run(async () => { await ready.Task.WaitAsync(Guard); go.TrySetResult(); }))).WaitAsync(Guard);
                    Log("after"); break;
                case "bounds":
                    using (logger.BeginScope(fields)) { Log("fields"); }
                    object deep = 1; for (int i = 0; i < 9; i++) { deep = new object[] { deep }; }
                    provider.SetScopeProvider(host); using (host.Push(new Dictionary<string, object?> { ["deep"] = deep })) { Log("depth"); }
                    break;
                case "invalid":
                    try { logger.BeginScope(new Dictionary<string, object?> { ["bad"] = new object() }); } catch (ArgumentException) { ownedErrors++; }
                    provider.SetScopeProvider(host);
                    foreach (object bad in new object[] { new object(), new Dictionary<string, object?> { ["bad"] = "\ud800" }, new OperationContextData { OperationId = "bad\0id" } })
                    { using (host.Push(bad)) { Log("invalid"); } }
                    Log("after"); break;
                case "host-incompatible":
                    provider.SetScopeProvider(host);
                    using (host.Push("plain host text")) { Log("string-host"); }
                    using (host.Push(new Dictionary<string, string> { ["owner"] = "typed-string-map" })) { Log("string-map-host"); }
                    using (host.Push(new Dictionary<string, object?> { ["HttpMethod"] = System.Net.Http.HttpMethod.Get })) { Log("microsoft-value-host"); }
                    Log("after"); break;
                case "depth":
                    var owned = new List<IDisposable>(); var external = new List<IDisposable>();
                    try
                    {
                        for (int i = 0; i < 32; i++) { owned.Add(logger.BeginScope(new OperationContextData())!); }
                        try { logger.BeginScope(new OperationContextData()); } catch (InvalidOperationException) { ownedErrors++; }
                        provider.SetScopeProvider(host); using (host.Push(new OperationContextData())) { Log("combined-invalid"); }
                        foreach (IDisposable handle in owned.AsEnumerable().Reverse()) { handle.Dispose(); }
                        owned.Clear();
                        for (int i = 0; i < 33; i++) { external.Add(host.Push(new OperationContextData())); }
                        Log("host-invalid"); foreach (IDisposable handle in external.AsEnumerable().Reverse()) { handle.Dispose(); }
                        external.Clear(); Log("after");
                    }
                    finally { foreach (IDisposable handle in external.AsEnumerable().Reverse()) { handle.Dispose(); } foreach (IDisposable handle in owned.AsEnumerable().Reverse()) { handle.Dispose(); } }
                    break;
                case "filtered": provider.SetScopeProvider(observed); Log(); logger.Log(LogLevel.None, default, "none", null, (_, _) => { Interlocked.Increment(ref formatters); return "none"; }); break;
                case "correlation":
                    using (var original = new Activity("original").SetIdFormat(ActivityIdFormat.W3C).SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start())
                    using (var changed = new Activity("changed").SetIdFormat(ActivityIdFormat.W3C).SetParentId("00-33333333333333333333333333333333-4444444444444444-01").Start())
                    {
                        Activity.Current = original; trace = original.TraceId.ToHexString(); span = original.SpanId.ToHexString();
                        var changing = new ObservedScopes(() => Activity.Current = changed); provider.SetScopeProvider(changing);
                        using (changing.Push(new Dictionary<string, object?> { ["trace_id"] = "fake" })) { Log(); }
                        Require(changing.Calls == 1); Activity.Current = original;
                    }
                    break;
                case "replacement":
                    using (host.Push(new Dictionary<string, object?> { ["owner"] = "first" }))
                    {
                        provider.SetScopeProvider(host); Log("first"); var second = new LoggerExternalScopeProvider();
                        using (second.Push(new Dictionary<string, object?> { ["owner"] = "second" })) { provider.SetScopeProvider(second); Log("second"); }
                        provider.Dispose(); Require(!runtime.GetDiagnostics().AdmissionClosed); int stillActive = 0; host.ForEachScope((_, _) => stillActive++, 0); Require(stillActive == 1);
                        Accepted(runtime.CreateLogger("Direct").Write(CoreLogLevel.Information, new LogEventData { EventName = "direct", Message = "rendered" }));
                    }
                    break;
            }
            LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard); Require(stop.Succeeded); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard);
            byte[] written = physical ? File.ReadAllBytes(path!) : bytes.ToArray(); bool leaseReleased = false;
            if (physical) { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); leaseReleased = true; }
            JsonObject report = Common(mode); report["physical"] = physical; report["bytes"] = Convert.ToBase64String(written); report["leaseReleased"] = leaseReleased;
            report["formatterCalls"] = formatters; report["ownedErrors"] = ownedErrors; report["fieldVisits"] = fields.Visits; report["filteredHostCalls"] = observed.Calls;
            report["trace"] = trace; report["span"] = span; report["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount); report["truncated"] = Number(runtime.GetDiagnostics().TruncatedEventCount); report["result"] = StopJoinFixture.Json(stop); return report;
        }
        finally { await runtime.DisposeAsync().AsTask().WaitAsync(Guard); }
    }
}
