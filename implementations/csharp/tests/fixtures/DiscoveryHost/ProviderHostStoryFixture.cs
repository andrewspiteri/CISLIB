using System.Diagnostics;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderHostStoryFixture
{
    private sealed class Worker;
    internal const string TypedCategory = "CIS.Tests.DiscoveryFixture.ProviderHostStoryFixture.Worker";
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    private static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        Require(ProviderHostStoryExpectations.Modes.Contains(mode, StringComparer.Ordinal));
        string path = Path.Combine(root, "logs \u03a9", "active.log");
        LoggingOptions options = EnvironmentOptions.Logging;
        options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = CoreLogLevel.Information;
        options.FilePath = path; options.FileFormat = LogOutputFormat.JsonLines;
        options.QueueCapacity = 64; options.MaxEntryBytes = 16384; options.ShutdownTimeoutMilliseconds = 5000;
        await using CoreLogRuntime runtime = CoreLogRuntime.Create();
        using var provider = new CoreLoggerProvider(runtime);
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
        { builder.ClearProviders(); builder.SetMinimumLevel(LogLevel.Warning); builder.AddFilter<CoreLoggerProvider>(null, LogLevel.Trace); });
        // ILoggerFactory.AddProvider owns provider disposal. The provider itself still borrows Core.
        factory.AddProvider(provider);
        ILogger typed = factory.CreateLogger<Worker>();
        ILogger owned = provider.CreateLogger("Owned.Host.Story");
        CoreLogger direct = runtime.CreateLogger("Direct.Host.Story");
        int formatters = 0, arrivals = 0, disposedGuards = 0;
        bool sharedLive = false, workFinishedAtStop = false, sameStopTask = false, sameStopResult = false, ownerAttention = false, formatterExceptionPreserved = false;
        string? trace = null, span = null; var phases = new List<string>();
        void Log(ILogger logger, string name, Dictionary<string, object?>? fields = null) => logger.Log(LogLevel.Information, new EventId(17, name), fields ?? new(), null, (_, _) => { Interlocked.Increment(ref formatters); return "rendered"; });
        void Write(string name) => Accepted(direct.Write(CoreLogLevel.Information, new LogEventData { EventName = name, Message = "direct" }));
        Dictionary<string, object?> Scope(string operation, int owner, string common) => new() { ["operation_id"] = operation, ["owner"] = owner, ["common"] = common, ["phase"] = "initial" };
        if (mode is "async-owned" or "async-external")
        {
            ILogger selected = mode == "async-owned" ? owned : typed;
            var ready = Signal(); var go = Signal();
            Task[] operations = Enumerable.Range(1, 2).Select(i => Task.Run(async () =>
            {
                var input = Scope("operation." + i, i, "outer");
                using (selected.BeginScope(input))
                {
                    input["phase"] = "changed";
                    if (Interlocked.Increment(ref arrivals) == 2) { ready.TrySetResult(); }
                    await go.Task.WaitAsync(Guard); await Task.Yield();
                    Log(selected, $"owner.{i}.outer");
                    using (selected.BeginScope(new Dictionary<string, object?> { ["operation_id"] = "nested." + i, ["common"] = "inner" }))
                    {
                        await Task.Yield(); Log(selected, $"owner.{i}.inner");
                        Log(selected, $"owner.{i}.event", new() { ["common"] = "event", ["operation_id"] = "ordinary-event-field" });
                    }
                    Log(selected, $"owner.{i}.restored");
                }
            })).ToArray();
            try { await ready.Task.WaitAsync(Guard); Require(arrivals == 2); go.TrySetResult(); await Task.WhenAll(operations).WaitAsync(Guard); }
            finally { go.TrySetResult(); await Task.WhenAll(operations).WaitAsync(Guard); }
            Log(selected, "after");
        }
        if (mode == "factory-precedence")
        {
            using (owned.BeginScope(new OperationContextData { OperationId = "core.outer", Attributes = new Dictionary<string, LogValue> { ["core"] = LogValue.Boolean(true), ["common"] = LogValue.String("core") } }))
            using (var activity = new Activity("factory").SetIdFormat(ActivityIdFormat.W3C).SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start())
            using (typed.BeginScope(new Dictionary<string, object?> { ["operation_id"] = "host.outer", ["common"] = "host", ["service"] = "fake", ["trace_id"] = "fake", ["span_id"] = "fake" }))
            {
                trace = activity.TraceId.ToHexString(); span = activity.SpanId.ToHexString(); Log(typed, "outer");
                using (typed.BeginScope(new Dictionary<string, object?> { ["operation_id"] = "host.inner", ["common"] = "inner" }))
                {
                    Log(typed, "inner", new() { ["common"] = "event", ["operation_id"] = "ordinary-event-field", ["env"] = "fake", ["version"] = "fake", ["component"] = "fake", ["resource"] = "fake" });
                    Accepted(direct.Write(CoreLogLevel.Information, new LogEventData { EventName = "explicit", Message = "direct", OperationId = "event.explicit" }));
                }
            }
            Log(typed, "after");
        }
        if (mode == "factory-lifetime")
        {
            using (owned.BeginScope(new OperationContextData { OperationId = "core.owner", Attributes = new Dictionary<string, LogValue> { ["owner"] = LogValue.String("application") } }))
            {
                Log(typed, "before"); factory.Dispose(); factory.Dispose();
                try { provider.CreateLogger("Never"); } catch (ObjectDisposedException) { disposedGuards++; }
                try { factory.CreateLogger("Never"); } catch (ObjectDisposedException) { disposedGuards++; }
                Require(disposedGuards == 2 && !typed.IsEnabled(LogLevel.Information));
                Log(typed, "disposed"); sharedLive = direct.IsEnabled(CoreLogLevel.Information) && !runtime.GetDiagnostics().AdmissionClosed; Require(sharedLive);
                Write("direct.live"); using var other = new CoreLoggerProvider(runtime); Log(other.CreateLogger("Other.Host.Story"), "other.live");
            }
        }
        if (mode == "factory-formatter")
        {
            var original = new InvalidOperationException("Controlled original formatter failure.");
            try { typed.Log(LogLevel.Information, new EventId(17, "never"), "state", null, (_, _) => { formatters++; throw original; }); }
            catch (AggregateException error) { formatterExceptionPreserved = error.InnerExceptions.Count == 1 && ReferenceEquals(error.InnerExceptions[0], original); }
            Require(!formatterExceptionPreserved && runtime.GetDiagnostics().InvalidEventCount == 1);
            Log(typed, "after.formatter");
        }
        if (mode is "shutdown" or "shutdown-loss")
        {
            var started = Signal(); var finish = Signal(); bool finished = false;
            Task work = Task.Run(async () =>
            {
                using (typed.BeginScope(new Dictionary<string, object?> { ["operation_id"] = "background", ["owner"] = "worker" }))
                {
                    phases.Add("work.started"); Log(typed, "work.started"); started.TrySetResult();
                    await finish.Task.WaitAsync(Guard); await Task.Yield(); Log(typed, "work.finished"); phases.Add("work.finished"); finished = true;
                }
            });
            try
            {
                await started.Task.WaitAsync(Guard); phases.Add("shutdown.requested"); finish.TrySetResult();
                await work.WaitAsync(Guard); workFinishedAtStop = finished && work.IsCompletedSuccessfully; Require(workFinishedAtStop);
                Write("owner.finishing");
                if (mode == "shutdown-loss") { Log(typed, "invalid", new() { ["bad"] = new object() }); }
            }
            finally { finish.TrySetResult(); await work.WaitAsync(Guard); }
            phases.Add("runtime.stop");
        }
        Task<LogStopResult> first = runtime.StopAsync(CancellationToken.None);
        LogStopResult stop = await first.WaitAsync(Guard);
        Task<LogStopResult> repeated = runtime.StopAsync(CancellationToken.None);
        Task disposal = runtime.DisposeAsync().AsTask();
        sameStopTask = ReferenceEquals(first, repeated) && ReferenceEquals(first, disposal);
        sameStopResult = ReferenceEquals(stop, await repeated) && ReferenceEquals(stop, runtime.GetDiagnostics().LastStopResult);
        await disposal.WaitAsync(Guard); Require(sameStopTask && sameStopResult && stop.Succeeded);
        ownerAttention = !stop.Succeeded || stop.LossObserved;
        int before = formatters; Log(typed, "stopped"); Require(formatters == before);
        factory.Dispose(); provider.Dispose();
        if (mode is "shutdown" or "shutdown-loss") { phases.Add("factory.dispose"); }
        bool leaseReleased;
        using (var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { leaseReleased = true; }
        JsonObject report = Common(mode);
        report["formatterExceptionPreserved"] = formatterExceptionPreserved;
        report["formatterFailureContained"] = mode == "factory-formatter" && !formatterExceptionPreserved;
        report["formatterCalls"] = formatters; report["arrivals"] = arrivals; report["sharedLiveAfterFactoryDispose"] = sharedLive;
        report["disposedGuards"] = disposedGuards; report["workFinishedAtStop"] = workFinishedAtStop; report["sameStopTask"] = sameStopTask;
        report["sameStopResult"] = sameStopResult; report["ownerAttention"] = ownerAttention; report["phases"] = new JsonArray(phases.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
        report["trace"] = trace; report["span"] = span; report["leaseReleased"] = leaseReleased;
        report["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount); report["truncated"] = Number(runtime.GetDiagnostics().TruncatedEventCount);
        report["bytes"] = Convert.ToBase64String(File.ReadAllBytes(path)); report["result"] = StopJoinFixture.Json(stop); return report;
    }
}
