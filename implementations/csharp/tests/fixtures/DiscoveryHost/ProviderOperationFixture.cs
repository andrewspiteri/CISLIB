using System.Diagnostics;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderOperationFixture
{
    internal static readonly string[] Modes = ["owned", "host", "nested-owned", "nested-host", "duplicates", "nonstring", "event", "protected", "bounds", "invalid", "host-omitted", "callback-errors"];
    private sealed class ThrowingHost : IExternalScopeProvider
    {
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state) => throw new InvalidOperationException("Controlled provider failure.");
        public IDisposable Push(object? state) => throw new InvalidOperationException("Unused.");
    }
    private sealed class ThrowingFields : IEnumerable<KeyValuePair<string, object?>>
    {
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => throw new IOException("Controlled host enumerator failure.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string? root = null)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal)); using var bytes = new MemoryStream(); bool physical = root is not null;
        string? path = physical ? Path.Combine(root!, "logs Ω", "active.log") : null; CoreLogRuntime runtime;
        if (physical)
        {
            LoggingOptions options = EnvironmentOptions.Logging; options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = CoreLogLevel.Information;
            options.FilePath = path; options.FileFormat = LogOutputFormat.JsonLines; options.QueueCapacity = 64; options.MaxEntryBytes = 16384; options.ShutdownTimeoutMilliseconds = 5000; runtime = CoreLogRuntime.Create();
        }
        else { runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions { ConsoleLevel = CoreLogLevel.Information, FileLevel = CoreLogLevel.None, ConsoleFormat = LogOutputFormat.JsonLines, QueueCapacity = 64, MaxEntryBytes = 16384 }, acquireConsole: _ => bytes); }
        using var provider = new CoreLoggerProvider(runtime); ILogger logger = provider.CreateLogger("Provider.Operation"); var host = new LoggerExternalScopeProvider(); int calls = 0, errors = 0; string? trace = null, span = null;
        void Log(string name = "entry", Dictionary<string, object?>? state = null) => logger.Log(LogLevel.Information, new EventId(17, name), state ?? new(), null, (_, _) => { calls++; return "rendered"; });
        Dictionary<string, object?> Scope(object? operationId, string common) => new() { ["operation_id"] = operationId, ["common"] = common };
        try
        {
            switch (mode)
            {
                case "owned": var input = Scope(" owned.operation ", "original"); using (logger.BeginScope(input)) { input["operation_id"] = "changed"; input["common"] = "changed"; Log(); } break;
                case "host": provider.SetScopeProvider(host); var data = Scope("before", "before"); using (host.Push(data)) { data["operation_id"] = " host.operation "; data["common"] = "at-capture"; Log(); data.Clear(); } break;
                case "nested-owned":
                    using (logger.BeginScope(Scope("outer", "outer")))
                    {
                        Log("outer"); using (logger.BeginScope(Scope("inner", "inner"))) { Log("inner"); using (logger.BeginScope(Scope("  ", "blank"))) { Log("blank", new() { ["common"] = "event", ["operation_id"] = "attribute-only" }); } }
                    }
                    Log("after"); break;
                case "nested-host":
                    provider.SetScopeProvider(host);
                    using (logger.BeginScope(new OperationContextData { OperationId = "core", Attributes = new Dictionary<string, LogValue> { ["core"] = LogValue.Boolean(true) } }))
                    using (host.Push(Scope("host.outer", "outer")))
                    { Log("outer"); using (host.Push(Scope("host.inner", "inner"))) { Log("inner"); using (host.Push(Scope("", "blank"))) { Log("blank", new() { ["common"] = "event" }); } } }
                    break;
                case "duplicates":
                    using (logger.BeginScope(new OperationContextData { OperationId = "parent" }))
                    {
                        KeyValuePair<string, object?>[][] inputs = [[new("operation_id", "one"), new("operation_id", 7)], [new("operation_id", 7), new("operation_id", "two")], [new("operation_id", "three"), new("operation_id", " ")], [new("Operation_Id", "ordinary")], [new("operation_id", null)]];
                        for (int i = 0; i < inputs.Length; i++) { using (logger.BeginScope(inputs[i])) { Log("duplicate." + i); } }
                    }
                    break;
                case "nonstring":
                    using (logger.BeginScope(new Dictionary<string, object?> { ["operation_id"] = 7 })) { Log("integer"); }
                    using (logger.BeginScope(new Dictionary<string, object?> { ["operation_id"] = LogValue.String("explicit") })) { Log("explicit-value"); }
                    break;
                case "event":
                    using (logger.BeginScope(Scope("scope", "scope")))
                    {
                        Log("provider", new() { ["common"] = "event", ["operation_id"] = "attribute-only" });
                        Accepted(runtime.CreateLogger("Direct").Write(CoreLogLevel.Information, new LogEventData { EventName = "explicit", Message = "rendered", OperationId = " event.id ", Attributes = new Dictionary<string, LogValue> { ["common"] = LogValue.String("direct") } }));
                        Accepted(runtime.CreateLogger("Direct").Write(CoreLogLevel.Information, new LogEventData { EventName = "blank", Message = "rendered", OperationId = " " }));
                    }
                    break;
                case "protected":
                    provider.SetScopeProvider(host);
                    using (var activity = new Activity("protected").SetIdFormat(ActivityIdFormat.W3C).SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start())
                    using (host.Push(new Dictionary<string, object?> { ["operation_id"] = "protected.operation", ["service"] = "scope.fake", ["env"] = "fake", ["version"] = "fake", ["component"] = "fake", ["resource"] = "fake", ["trace_id"] = "scope.fake", ["span_id"] = "fake" }))
                    { trace = activity.TraceId.ToHexString(); span = activity.SpanId.ToHexString(); Log(state: new() { ["service"] = "event.fake", ["trace_id"] = "event.fake" }); }
                    break;
                case "bounds":
                    var bounded = new[] { new KeyValuePair<string, object?>("common", "scope") }.Concat(Enumerable.Range(0, 1022).Select(i => new KeyValuePair<string, object?>("k" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i))).Append(new("operation_id", "bounded"));
                    using (logger.BeginScope(bounded)) { Log("retained", new() { ["common"] = "event" }); }
                    var late = Enumerable.Range(0, 2000).Select(i => new KeyValuePair<string, object?>("k" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i)).Append(new("operation_id", "not-visited"));
                    using (logger.BeginScope(late)) { Log("late"); }
                    break;
                case "invalid":
                    foreach (string bad in new[] { "bad\0id", "\ud800", new string('x', 256) })
                    { try { logger.BeginScope(Scope(bad, "bad")); } catch (ArgumentException) { errors++; } }
                    using (logger.BeginScope(Scope(new string('x', 255), "valid"))) { Log(); }
                    break;
                case "host-omitted":
                    provider.SetScopeProvider(host); using (host.Push(Scope("parent", "parent"))) using (host.Push(Scope("bad\0id", "bad"))) { Log(); }
                    break;
                case "callback-errors":
                    provider.SetScopeProvider(new ThrowingHost()); Log("provider-error"); provider.SetScopeProvider(host);
                    using (host.Push(new ThrowingFields())) { Log("enumerator-error"); }
                    Log("after"); break;
            }
            LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard); Require(stop.Succeeded); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard);
            byte[] written = physical ? File.ReadAllBytes(path!) : bytes.ToArray(); bool released = false; if (physical) { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); released = true; }
            JsonObject report = Common(mode); report["physical"] = physical; report["leaseReleased"] = released; report["bytes"] = Convert.ToBase64String(written); report["formatterCalls"] = calls; report["ownedErrors"] = errors;
            report["trace"] = trace; report["span"] = span; report["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount); report["truncated"] = Number(runtime.GetDiagnostics().TruncatedEventCount); report["result"] = StopJoinFixture.Json(stop); return report;
        }
        finally { await runtime.DisposeAsync().AsTask().WaitAsync(Guard); }
    }
}
