using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderOwnershipFixture
{
    private sealed class Tracker { internal int Disposals; }
    private sealed class HostScopes(Tracker tracker) : IExternalScopeProvider, IDisposable
    {
        internal readonly LoggerExternalScopeProvider Inner = new();
        internal int Calls;
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
        { Calls++; Inner.ForEachScope(callback, state); }
        public IDisposable Push(object? state) => Inner.Push(state);
        public void Dispose() => tracker.Disposals++;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterTemporarySource(CoreLoggerProvider provider, Tracker tracker)
    {
        var source = new HostScopes(tracker);
        provider.SetScopeProvider(source);
        return new WeakReference(source);
    }
    private static void Collect()
    { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }

    private static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        Require(ProviderOwnershipExpectations.Modes.Contains(mode, StringComparer.Ordinal));
        string path = Path.Combine(root, "logs \u03a9", "active.log");
        LoggingOptions options = EnvironmentOptions.Logging;
        options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = CoreLogLevel.Information;
        options.FilePath = path; options.FileFormat = LogOutputFormat.JsonLines;
        options.QueueCapacity = 64; options.MaxEntryBytes = 16384; options.ShutdownTimeoutMilliseconds = 5000;
        await using CoreLogRuntime runtime = CoreLogRuntime.Create();
        using var provider = new CoreLoggerProvider(runtime);
        ILogger logger = provider.CreateLogger("Provider.Ownership");
        CoreLogger direct = runtime.CreateLogger("Direct.Ownership");
        var tracker = new Tracker(); var host = new HostScopes(tracker);
        int formatters = 0, guards = 0, lifoErrors = 0;
        bool runtimeLive = false, leaseHeld = false, hostHandleSurvived = false;
        bool? sourceRetainedBefore = null, sourceReleasedAfter = null;
        void Log(ILogger target, string name) => target.Log(LogLevel.Information, new EventId(17, name), "state", null, (_, _) => { formatters++; return "rendered"; });
        void Write(string name) => Accepted(direct.Write(CoreLogLevel.Information, new LogEventData { EventName = name, Message = "direct" }));
        void DisposeProvider()
        {
            provider.Dispose(); provider.Dispose();
            Require(!logger.IsEnabled(LogLevel.Information));
            logger.Log(LogLevel.Information, default, new object(), null, (_, _) => { formatters++; throw new InvalidOperationException("Disposed formatter ran."); });
            try { provider.CreateLogger("Never"); } catch (ObjectDisposedException) { guards++; }
            try { provider.SetScopeProvider(host); } catch (ObjectDisposedException) { guards++; }
            try { logger.BeginScope(new OperationContextData()); } catch (ObjectDisposedException) { guards++; }
            Require(guards == 3);
            runtimeLive = !runtime.GetDiagnostics().AdmissionClosed && direct.IsEnabled(CoreLogLevel.Information);
            Require(runtimeLive);
            try { using var probe = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { leaseHeld = true; }
            Require(leaseHeld);
        }
        switch (mode)
        {
            case "shared":
                provider.SetScopeProvider(host);
                using (IDisposable borrowed = host.Push(new OperationContextData { OperationId = "host.operation" }))
                {
                    using (logger.BeginScope(new OperationContextData { OperationId = "owned.operation" }))
                    { Log(logger, "before"); DisposeProvider(); Write("direct.scoped"); }
                    int active = 0; host.Inner.ForEachScope((_, _) => active++, 0);
                    hostHandleSurvived = active == 1; Require(hostHandleSurvived);
                    using var other = new CoreLoggerProvider(runtime);
                    Log(other.CreateLogger("Other.Ownership"), "other");
                }
                break;
            case "resources":
                WeakReference weak = RegisterTemporarySource(provider, tracker);
                Collect(); sourceRetainedBefore = weak.IsAlive; Require(sourceRetainedBefore.Value);
                DisposeProvider(); Collect(); sourceReleasedAfter = !weak.IsAlive; Require(sourceReleasedAfter.Value);
                Write("after.release"); GC.KeepAlive(provider); GC.KeepAlive(logger);
                break;
            case "scope-handles":
                IDisposable outer = logger.BeginScope(new OperationContextData { OperationId = "outer" })!;
                IDisposable inner = logger.BeginScope(new OperationContextData { OperationId = "inner" })!;
                try
                {
                    DisposeProvider(); Write("inner");
                    try { outer.Dispose(); } catch (InvalidOperationException) { lifoErrors++; }
                    Require(lifoErrors == 1); inner.Dispose(); Write("outer");
                    outer.Dispose(); outer.Dispose(); Write("after");
                }
                finally { inner.Dispose(); outer.Dispose(); }
                break;
            case "in-flight":
                provider.SetScopeProvider(host);
                using (IDisposable borrowed = host.Push(new OperationContextData { OperationId = "in.flight" }))
                {
                    var entered = Signal(); var release = Signal();
                    Task work = Task.Run(() => logger.Log(LogLevel.Information, new EventId(17, "in.flight"), "state", null, (_, _) =>
                    { Interlocked.Increment(ref formatters); entered.TrySetResult(); release.Task.WaitAsync(Guard).GetAwaiter().GetResult(); return "rendered"; }));
                    try
                    {
                        await entered.Task.WaitAsync(Guard); DisposeProvider();
                        int active = 0; host.Inner.ForEachScope((_, _) => active++, 0);
                        hostHandleSurvived = active == 1; Require(hostHandleSurvived);
                        release.TrySetResult(); await work.WaitAsync(Guard);
                    }
                    finally { release.TrySetResult(); await work.WaitAsync(Guard); }
                }
                Write("after.flight"); break;
        }
        Require(tracker.Disposals == 0);
        LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard);
        Require(stop.Succeeded && !stop.LossObserved);
        bool leaseReleased = false;
        using (var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { leaseReleased = true; }
        JsonObject report = Common(mode);
        report["runtimeLiveAfterProviderDispose"] = runtimeLive; report["leaseHeldAfterProviderDispose"] = leaseHeld;
        report["hostHandleSurvived"] = hostHandleSurvived; report["hostDisposals"] = tracker.Disposals; report["hostCaptureCalls"] = host.Calls;
        report["sourceRetainedBefore"] = sourceRetainedBefore; report["sourceReleasedAfter"] = sourceReleasedAfter;
        report["formatterCalls"] = formatters; report["disposedGuards"] = guards; report["lifoErrors"] = lifoErrors;
        report["leaseReleasedAfterCoreStop"] = leaseReleased; report["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount);
        report["truncated"] = Number(runtime.GetDiagnostics().TruncatedEventCount); report["bytes"] = Convert.ToBase64String(File.ReadAllBytes(path));
        report["result"] = StopJoinFixture.Json(stop); return report;
    }
}
