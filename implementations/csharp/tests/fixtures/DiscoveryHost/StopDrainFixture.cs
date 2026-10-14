using System.Text.Json.Nodes;
using CIS.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopDrainFixture
{
    internal static readonly string[] Modes = ["normal-dual", "queued-only", "blocked-write", "single-write", "cancel-write", "blocked-flush", "cancel-flush", "blocked-callback"];
    internal static int Run(string mode, string root)
    {
        Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString());
        return 0;
    }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal));
        bool normal = mode == "normal-dual", queued = mode == "queued-only", flushing = mode is "blocked-flush" or "cancel-flush";
        bool cooperative = mode is "cancel-write" or "cancel-flush", callback = mode == "blocked-callback";
        var clock = new FileTimerCoordinationFixture.Clock(); using var stream = new HeldStream(!normal && !queued && !flushing, flushing, cooperative, callback);
        var startWorker = Signal();
        Func<Func<Task>, Task>? launch = queued ? work => Task.Run(async () => { await startWorker.Task; await work(); }) : null;
        string path = Path.Combine(root, "owned.jsonl");
        var options = new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.Information,
            ConsoleFormat = LogOutputFormat.JsonLines,
            FileLevel = normal ? CoreLogLevel.Information : CoreLogLevel.None,
            FilePath = normal ? path : null,
            QueueCapacity = 3,
            MaxEntryBytes = 16384,
            ShutdownTimeoutMilliseconds = 10
        };
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, clock, _ => stream, launch);
        stream.CallbackProbe = () => runtime.GetDiagnostics().AdmissionClosed;
        try
        {
            CoreLogger logger = runtime.CreateLogger("Drain");
            int count = flushing || mode == "single-write" ? 1 : 3;
            Accepted(logger.Write(CoreLogLevel.Information, Data("one")));
            if (!queued && !normal) { await stream.FirstWrite.Task.WaitAsync(Guard); }
            for (int i = 1; i < count; i++) { Accepted(logger.Write(CoreLogLevel.Information, Data(i == 1 ? "two" : "three"))); }
            if (flushing) { await UntilAsync(() => runtime.QueuedOutputs!.Console!.CaptureAccounting().Written == 1); }
            Task<LogStopResult> stop = runtime.StopAsync(CancellationToken.None);
            if (flushing) { await stream.FirstFlush.Task.WaitAsync(Guard); }
            if (!normal)
            {
                clock.Advance(TimeSpan.FromMilliseconds(10) - TimeSpan.FromTicks(1)); Require(!stop.IsCompleted);
                clock.Advance(TimeSpan.FromTicks(1));
            }
            LogStopResult result = await stop.WaitAsync(Guard); JsonObject frozen = Result(result);
            bool pendingBeforeRelease = !runtime.QueuedOutputs!.Completion.IsCompleted;
            bool cancellationRequested = stream.Token.CanBeCanceled && stream.Token.IsCancellationRequested;
            if (callback) { await stream.CallbackEntered.Task.WaitAsync(Guard); Require(stream.CallbackGateFree); }
            int registeredAfterDeadline = runtime.QueuedOutputs.Console!.RegisteredFlushCount;
            if (!normal && !flushing)
            {
                Require(registeredAfterDeadline == (mode == "single-write" ? 1 : 0));
                Require(runtime.QueuedOutputs.Console.CaptureAccounting().Abandoned == (queued ? 3 : mode == "single-write" ? 0 : 2));
            }
            Require(ReferenceEquals(stop, runtime.StopAsync(CancellationToken.None)));
            stream.ReleaseAll(); startWorker.TrySetResult();
            try { await runtime.QueuedOutputs.Completion.WaitAsync(Guard); }
            catch (OperationCanceledException) when (cooperative) { }
            await UntilAsync(() => clock.Active == 0);
            Require(Result(result).ToJsonString() == frozen.ToJsonString());
            Require(ReferenceEquals(result, runtime.GetDiagnostics().LastStopResult));
            JsonObject report = Common(mode);
            report["result"] = frozen; report["live"] = Diagnostics(runtime);
            report["stream"] = stream.Report(); report["timers"] = clock.Report();
            report["pendingBeforeRelease"] = pendingBeforeRelease; report["cancellationRequested"] = cancellationRequested;
            report["registeredAfterDeadline"] = registeredAfterDeadline;
            report["registeredAfterIo"] = runtime.QueuedOutputs.Console.RegisteredFlushCount;
            report["immutable"] = true; report["sameTask"] = ReferenceEquals(stop, runtime.StopAsync(CancellationToken.None));
            report["sameResult"] = ReferenceEquals(result, await runtime.StopAsync(CancellationToken.None));
            report["workersSettled"] = runtime.QueuedOutputs.Completion.IsCompleted;
            report["fileBytes"] = normal ? Convert.ToBase64String(File.ReadAllBytes(path)) : null;
            return report;
        }
        finally
        {
            stream.ReleaseAll(); startWorker.TrySetResult();
            try { await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); }
            catch (OperationCanceledException) when (cooperative) { }
        }
    }
    private static JsonObject Result(LogStopResult result) => new()
    {
        ["completion"] = result.Completion.ToString(),
        ["succeeded"] = result.Succeeded,
        ["loss"] = result.LossObserved,
        ["captured"] = result.BoundaryCapturedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        ["observed"] = result.ObservedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        ["invalid"] = Number(result.InvalidEventCount),
        ["truncated"] = Number(result.TruncatedEventCount),
        ["outputs"] = new JsonArray(result.Outputs.Select(o => (JsonNode)new JsonObject
        {
            ["name"] = o.OutputName,
            ["boundary"] = Number(o.AcceptedSequenceBoundary),
            ["written"] = Number(o.Written),
            ["failed"] = Number(o.Failed),
            ["abandoned"] = Number(o.Abandoned),
            ["pending"] = Number(o.Pending),
            ["flush"] = o.WriterFlushStatus.ToString(),
            ["filtered"] = Number(o.Rejections.Filtered),
            ["full"] = Number(o.Rejections.QueueFull),
            ["unavailable"] = Number(o.Rejections.Unavailable)
        }).ToArray())
    };
    internal sealed class HeldStream(bool holdWrite, bool holdFlush, bool cooperative, bool holdCallback) : MemoryStream
    {
        internal readonly TaskCompletionSource FirstWrite = Signal(), FirstFlush = Signal(), CallbackEntered = Signal();
        private readonly TaskCompletionSource release = Signal(), releaseCallback = Signal();
        internal CancellationToken Token { get; private set; }
        internal Func<bool>? CallbackProbe;
        internal bool CallbackGateFree;
        private int writes, written, flushes, flushed, callbacks, disposed;
        internal void ReleaseAll() { releaseCallback.TrySetResult(); release.TrySetResult(); }
        private CancellationTokenRegistration Observe(CancellationToken token)
        {
            Token = token;
            return token.Register(() =>
            {
                Interlocked.Increment(ref callbacks);
                if (holdCallback)
                {
                    CallbackGateFree = CallbackProbe!(); CallbackEntered.TrySetResult();
                    Require(releaseCallback.Task.Wait(Guard));
                }
            });
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref writes); using var registration = Observe(cancellationToken); FirstWrite.TrySetResult();
            if (holdWrite)
            {
                if (cooperative) { await release.Task.WaitAsync(cancellationToken); }
                else { await release.Task; }
            }
            await base.WriteAsync(buffer, CancellationToken.None); Interlocked.Increment(ref written);
        }
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref flushes); using var registration = Observe(cancellationToken); FirstFlush.TrySetResult();
            if (holdFlush)
            {
                if (cooperative) { await release.Task.WaitAsync(cancellationToken); }
                else { await release.Task; }
            }
            Interlocked.Increment(ref flushed);
        }
        internal JsonObject Report() => new()
        {
            ["writes"] = writes,
            ["written"] = written,
            ["flushes"] = flushes,
            ["flushed"] = flushed,
            ["callbacks"] = callbacks,
            ["disposed"] = disposed,
            ["callbackGateFree"] = CallbackGateFree,
            ["bytes"] = Convert.ToBase64String(ToArray())
        };
        protected override void Dispose(bool disposing) { if (disposing) { Interlocked.Increment(ref disposed); } base.Dispose(disposing); }
    }
}
