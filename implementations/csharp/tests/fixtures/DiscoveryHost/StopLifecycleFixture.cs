using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopLifecycleFixture
{
    internal static readonly string[] Modes = ["normal", "normal-dual", "producer-stop-race", "pre-cancelled", "cancelled-joiners", "held-write", "held-flush", "late-failure", "kill-queued", "kill-partial", "kill-after-stop"];
    internal static readonly string[] KernelModes = ["cancelled-joiners", "held-write", "held-flush", "late-failure"];
    internal static string PathFor(string root) => Path.Combine(root, "logs Ω", "active.log");
    internal static int Run(string mode, string root)
    {
        if (mode.StartsWith("kill-", StringComparison.Ordinal)) { CrashAsync(mode, root).GetAwaiter().GetResult(); return 1; }
        JsonObject report = mode == "restart" ? RestartAsync(root).GetAwaiter().GetResult()
            : KernelModes.Contains(mode, StringComparer.Ordinal) ? KernelAsync(mode, root).GetAwaiter().GetResult() : PublicAsync(mode, root).GetAwaiter().GetResult();
        if (mode == "normal-dual") { Console.Write("application-after-stop\n"); Console.Out.Flush(); Console.Error.WriteLine(report.ToJsonString()); }
        else { Console.WriteLine(report.ToJsonString()); }
        return 0;
    }
    private static LoggingOptions Options(string root) => new()
    {
        ConsoleLevel = CoreLogLevel.None,
        FileLevel = CoreLogLevel.Information,
        FilePath = PathFor(root),
        FileFormat = LogOutputFormat.JsonLines,
        QueueCapacity = 2048,
        FileMaxBytes = 1048576,
        MaxEntryBytes = 16384,
        ShutdownTimeoutMilliseconds = 5000
    };
    private static CoreLogRuntime PublicCreate(string root, bool dual = false)
    {
        LoggingOptions source = Options(root), target = EnvironmentOptions.Logging;
        target.ConsoleFormat = LogOutputFormat.JsonLines; target.ConsoleStream = ConsoleOutputStream.StandardOutput;
        target.ConsoleLevel = dual ? CoreLogLevel.Information : source.ConsoleLevel; target.FileLevel = source.FileLevel; target.FilePath = source.FilePath;
        target.FileFormat = source.FileFormat; target.QueueCapacity = source.QueueCapacity; target.FileMaxBytes = source.FileMaxBytes;
        target.MaxEntryBytes = source.MaxEntryBytes; target.ShutdownTimeoutMilliseconds = source.ShutdownTimeoutMilliseconds;
        return CoreLogRuntime.Create();
    }
    internal static byte[] ReadLive(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray();
    }
    internal static bool LeaseAvailable(string path)
    {
        try { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private static async Task<byte[]> SeedAsync(string root)
    {
        var runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), Options(root));
        try
        {
            Accepted(runtime.CreateLogger("Seed").Write(CoreLogLevel.Information, Data("seed")));
            Require((await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard)).Succeeded);
            await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); Require(LeaseAvailable(PathFor(root)));
            return ReadLive(PathFor(root));
        }
        finally { await runtime.DisposeAsync(); }
    }
    private static void CheckStopped(CoreLogRuntime runtime)
    {
        var logger = runtime.CreateLogger("AfterStop"); int calls = 0;
        Require(logger.Write(CoreLogLevel.None, Data("none")).Outcome == LogWriteOutcome.Stopped);
        Require(logger.Write(CoreLogLevel.Information, Data("later")).Outcome == LogWriteOutcome.Stopped);
        foreach (CoreLogLevel level in new[] { CoreLogLevel.None, CoreLogLevel.Information })
        { Require(logger.WriteLazy(level, () => { calls++; throw new InvalidOperationException("Stopped lazy factory invoked."); }).Outcome == LogWriteOutcome.Stopped); }
        Require(calls == 0);
    }
    private static async Task<JsonObject> PublicAsync(string mode, string root)
    {
        Require(mode is "normal" or "normal-dual" or "producer-stop-race" or "pre-cancelled");
        byte[] seed = await SeedAsync(root); var runtime = PublicCreate(root, mode == "normal-dual"); var logger = runtime.CreateLogger("PublicLifecycle");
        var observations = new ConcurrentBag<(string Name, LogWriteResult Result)>(); var operations = new ConcurrentBag<Task<LogStopResult>>();
        TaskCompletionSource captureEntered = Signal(), releaseCapture = Signal(); int cancelled = 0; bool capturingStopped = false; Exception? primaryFailure = null;
        try
        {
            void Write(string name) => observations.Add((name, logger.Write(CoreLogLevel.Information, Data(name))));
            if (mode == "producer-stop-race")
            {
                Write("initial");
                Task<LogWriteResult> capturing = Task.Run(() => logger.WriteLazy(CoreLogLevel.Information, () =>
                { captureEntered.TrySetResult(); Require(releaseCapture.Task.Wait(Guard)); return Data("capturing"); }));
                await captureEntered.Task.WaitAsync(Guard);
                var start = Signal(); TaskCompletionSource[] ready = Enumerable.Range(0, 24).Select(_ => Signal()).ToArray();
                Task[] producers = Enumerable.Range(0, 8).Select(async p =>
                { ready[p].TrySetResult(); await start.Task; for (int i = 0; i < 64; i++) { Write($"p{p}.{i}"); } }).ToArray();
                Task[] stoppers = Enumerable.Range(0, 16).Select(async s =>
                {
                    ready[8 + s].TrySetResult(); await start.Task;
                    Task<LogStopResult> operation = s % 2 == 0 ? runtime.StopAsync(CancellationToken.None) : (Task<LogStopResult>)runtime.DisposeAsync().AsTask();
                    operations.Add(operation); await operation.WaitAsync(Guard);
                }).ToArray();
                await Task.WhenAll(ready.Select(x => x.Task)).WaitAsync(Guard); start.TrySetResult();
                await UntilAsync(() => runtime.GetDiagnostics().AdmissionClosed); releaseCapture.TrySetResult();
                LogWriteResult raced = await capturing.WaitAsync(Guard); capturingStopped = raced.Outcome == LogWriteOutcome.Stopped;
                Require(capturingStopped); observations.Add(("capturing", raced));
                await Task.WhenAll(producers.Concat(stoppers)).WaitAsync(Guard);
            }
            else
            {
                for (int i = 0; i < (mode is "normal" or "normal-dual" ? 120 : 40); i++) { Write("n" + i); }
                if (mode == "pre-cancelled")
                {
                    using var caller = new CancellationTokenSource(); caller.Cancel();
                    try { await runtime.StopAsync(caller.Token); throw new InvalidOperationException("Cancelled first waiter succeeded."); }
                    catch (OperationCanceledException error) { Require(error.CancellationToken == caller.Token); cancelled = 1; }
                    // No later stop/dispose can conceal a failure of cancelled initiation.
                    Require(runtime.GetDiagnostics().AdmissionClosed && runtime.GetDiagnostics().ShutdownStartedAtUtc is not null); CheckStopped(runtime);
                }
            }
            Task<LogStopResult> shared = runtime.StopAsync(CancellationToken.None); LogStopResult result = await shared.WaitAsync(Guard);
            Require(operations.All(x => ReferenceEquals(x, shared))); string frozen = StopJoinFixture.Json(result).ToJsonString();
            Task[] joins = Enumerable.Range(0, 64).Select(_ => runtime.DisposeAsync().AsTask()).ToArray(); Require(joins.All(x => ReferenceEquals(x, shared)));
            await Task.WhenAll(joins).WaitAsync(Guard); CheckStopped(runtime);
            Require(ReferenceEquals(result, runtime.GetDiagnostics().LastStopResult) && frozen == StopJoinFixture.Json(result).ToJsonString());
            Require(LeaseAvailable(PathFor(root)) && result.Succeeded && !result.LossObserved);
            var accepted = observations.Where(x => x.Result.Outcome == LogWriteOutcome.Accepted).OrderBy(x => x.Result.Outputs.Single(o => o.OutputName == "file").AcceptedSequence).ToArray();
            Require(observations.All(x => x.Result.Outcome is LogWriteOutcome.Accepted or LogWriteOutcome.Stopped));
            JsonObject report = Common(mode); report["kind"] = "public"; report["seed"] = Convert.ToBase64String(seed); report["bytes"] = Convert.ToBase64String(ReadLive(PathFor(root)));
            report["receipts"] = new JsonArray(accepted.Select(x => (JsonNode)new JsonObject { ["ticket"] = Number(x.Result.Outputs.Single(o => o.OutputName == "file").AcceptedSequence!.Value), ["event"] = x.Name }).ToArray());
            report["producerStopped"] = observations.Count(x => x.Name.StartsWith("p", StringComparison.Ordinal) && x.Result.Outcome == LogWriteOutcome.Stopped);
            report["producerAccepted"] = observations.Count(x => x.Name.StartsWith("p", StringComparison.Ordinal) && x.Result.Outcome == LogWriteOutcome.Accepted);
            report["attempts"] = observations.Count; report["stopped"] = observations.Count - accepted.Length; report["capturingStopped"] = capturingStopped;
            report["cancelled"] = cancelled; report["racingStoppers"] = operations.Count; report["result"] = StopJoinFixture.Json(result);
            report["sameTask"] = true; report["immutable"] = true; report["joiners"] = joins.Length; report["leaseReleased"] = true; report["stoppedChecks"] = true; return report;
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            releaseCapture.TrySetResult();
            try { await runtime.DisposeAsync().AsTask().WaitAsync(Guard); } catch (Exception) when (primaryFailure is not null) { }
        }
    }
    internal static async Task<JsonObject> KernelAsync(string mode, string root)
    {
        Require(KernelModes.Contains(mode, StringComparer.Ordinal)); byte[] seed = await SeedAsync(root);
        bool flushing = mode == "held-flush"; var clock = new Clock(); var gate = new object();
        var writer = new HeldFile(PathFor(root), flushing, mode == "late-failure", prefix: !flushing);
        var options = Options(root); options.QueueCapacity = 8; options.ShutdownTimeoutMilliseconds = 10;
        var queues = new OutputQueues(8, false, true, null, item => writer.WriteAsync(item, CancellationToken.None), gate, fileResource: writer,
            fileFlush: () => writer.FlushAsync(CancellationToken.None), fileCancellableWrite: writer.WriteAsync, fileCancellableFlush: writer.FlushAsync);
        var runtime = new CoreLogRuntime(ConsoleCompositionFixture.Metadata(), options, null, queues.File, clock, gate, queues);
        Exception? primaryFailure = null;
        try
        {
            var logger = runtime.CreateLogger("HeldLifecycle"); int admitted = flushing ? 3 : 6;
            for (int i = 0; i < admitted; i++)
            {
                Accepted(logger.Write(CoreLogLevel.Information, Data("held." + i)));
                if (i == 0 && !flushing) { await writer.Entered.Task.WaitAsync(Guard); }
            }
            Task<LogStopResult> shared = runtime.StopAsync(CancellationToken.None); await writer.Entered.Task.WaitAsync(Guard);
            int cancelled = 0;
            if (mode == "cancelled-joiners")
            {
                using var caller = new CancellationTokenSource(); Task<LogStopResult>[] waits = Enumerable.Range(0, 64).Select(_ => runtime.StopAsync(caller.Token)).ToArray(); caller.Cancel();
                foreach (Task<LogStopResult> wait in waits)
                {
                    try { await wait; throw new InvalidOperationException("Caller waiter did not cancel."); }
                    catch (OperationCanceledException error) { Require(error.CancellationToken == caller.Token); cancelled++; }
                }
            }
            int afterCallerCancellation = writer.Cancellations; bool requestedAfterCallers = writer.CancellationRequested; Require(afterCallerCancellation == 0 && !requestedAfterCallers);
            Task[] joins = Enumerable.Range(0, 64).Select(_ => runtime.DisposeAsync().AsTask()).ToArray(); Require(joins.All(x => ReferenceEquals(x, shared)));
            clock.Advance(TimeSpan.FromMilliseconds(9)); int beforeDeadline = writer.Cancellations; bool requestedBeforeDeadline = writer.CancellationRequested; Require(!requestedBeforeDeadline && beforeDeadline == 0 && !shared.IsCompleted && writer.Disposals == 0 && !LeaseAvailable(PathFor(root)));
            clock.Advance(TimeSpan.FromMilliseconds(1)); LogStopResult result = await shared.WaitAsync(Guard); await Task.WhenAll(joins).WaitAsync(Guard);
            await UntilAsync(() => writer.Cancellations == 1); string frozen = StopJoinFixture.Json(result).ToJsonString(); CheckStopped(runtime);
            bool held = writer.InFlight && writer.Disposals == 0 && !queues.Completion.IsCompleted && !LeaseAvailable(PathFor(root)); Require(held);
            byte[] pending = ReadLive(PathFor(root)); writer.Release.TrySetResult();
            try { await queues.Completion.WaitAsync(Guard); } catch (IOException) when (mode == "late-failure") { }
            Require(writer.Disposals == 1 && !writer.UnsafeDispose && LeaseAvailable(PathFor(root)));
            Require(ReferenceEquals(result, runtime.GetDiagnostics().LastStopResult) && frozen == StopJoinFixture.Json(result).ToJsonString());
            await UntilAsync(() => clock.Active == 0); Require(clock.Created == 1);
            JsonObject report = Common(mode); report["kind"] = "kernel"; report["seed"] = Convert.ToBase64String(seed); report["pendingBytes"] = Convert.ToBase64String(pending); report["bytes"] = Convert.ToBase64String(ReadLive(PathFor(root)));
            report["line"] = Convert.ToBase64String(writer.FirstLine!); report["result"] = StopJoinFixture.Json(result); report["live"] = Diagnostics(runtime);
            report["outputCancellationsBeforeDeadline"] = new JsonArray(afterCallerCancellation, beforeDeadline);
            report["outputCancellationRequestedBeforeDeadline"] = new JsonArray(requestedAfterCallers, requestedBeforeDeadline);
            report["writer"] = writer.Report(); report["timers"] = clock.Report(); report["cancelled"] = cancelled; report["heldLease"] = held;
            report["sameTask"] = true; report["immutable"] = true; report["joiners"] = joins.Length; report["leaseReleased"] = true; report["stoppedChecks"] = true; return report;
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            writer.Release.TrySetResult(); queues.Complete();
            try
            {
                try { await queues.Completion.WaitAsync(Guard); } catch (IOException) when (mode == "late-failure") { }
                await runtime.DisposeAsync().AsTask().WaitAsync(Guard);
            }
            catch (Exception) when (primaryFailure is not null) { }
        }
    }
    private static async Task CrashAsync(string mode, string root)
    {
        Require(mode is "kill-queued" or "kill-partial" or "kill-after-stop"); byte[] seed = await SeedAsync(root);
        var gate = new object(); var clock = new Clock(); var writer = new HeldFile(PathFor(root), false, false, mode != "kill-queued");
        var options = Options(root); options.QueueCapacity = 8; options.ShutdownTimeoutMilliseconds = 10;
        var queues = new OutputQueues(8, false, true, null, item => writer.WriteAsync(item, CancellationToken.None), gate, fileResource: writer,
            fileFlush: () => writer.FlushAsync(CancellationToken.None), fileCancellableWrite: writer.WriteAsync, fileCancellableFlush: writer.FlushAsync);
        var runtime = new CoreLogRuntime(ConsoleCompositionFixture.Metadata(), options, null, queues.File, clock, gate, queues);
        for (int i = 0; i < 6; i++)
        { Accepted(runtime.CreateLogger("CrashBoundary").Write(CoreLogLevel.Information, Data("held." + i))); if (i == 0) { await writer.Entered.Task.WaitAsync(Guard); } }
        LogStopResult? result = null;
        if (mode == "kill-after-stop")
        { Task<LogStopResult> shared = runtime.StopAsync(CancellationToken.None); clock.Advance(TimeSpan.FromMilliseconds(10)); result = await shared.WaitAsync(Guard); await UntilAsync(() => writer.Cancellations == 1); CheckStopped(runtime); }
        Require(writer.InFlight && writer.Disposals == 0 && !LeaseAvailable(PathFor(root)));
        JsonObject report = Common(mode); report["kind"] = "checkpoint"; report["seed"] = Convert.ToBase64String(seed); report["bytes"] = Convert.ToBase64String(ReadLive(PathFor(root))); report["line"] = Convert.ToBase64String(writer.FirstLine!);
        report["live"] = Diagnostics(runtime); report["writer"] = writer.Report(); report["heldLease"] = true; report["result"] = result is null ? null : StopJoinFixture.Json(result);
        Console.WriteLine(report.ToJsonString()); Console.Out.Flush();
        // The parent kills this exact child after validating the checkpoint. No graceful finally is executed.
        await Signal().Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
    private static async Task<JsonObject> RestartAsync(string root)
    {
        byte[] before = ReadLive(PathFor(root)); var runtime = PublicCreate(root);
        try
        {
            bool recovered = runtime.GetDiagnostics().Outputs.Single().RecoveredPartialSegment;
            Accepted(runtime.CreateLogger("Restart").Write(CoreLogLevel.Information, Data("restarted")));
            LogStopResult result = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard); Require(result.Succeeded && LeaseAvailable(PathFor(root)));
            string[] damaged = Directory.GetFiles(Path.GetDirectoryName(PathFor(root))!).Where(p => Path.GetFileName(p).Contains(".damaged", StringComparison.Ordinal)).ToArray();
            Require(damaged.Length == (recovered ? 1 : 0));
            JsonObject report = Common("restart"); report["kind"] = "restart"; report["beforeBytes"] = Convert.ToBase64String(before); report["bytes"] = Convert.ToBase64String(ReadLive(PathFor(root)));
            report["damagedBytes"] = damaged.Length == 0 ? null : Convert.ToBase64String(File.ReadAllBytes(damaged[0])); report["recovered"] = recovered; report["leaseReleased"] = true; report["result"] = StopJoinFixture.Json(result); return report;
        }
        finally { await runtime.DisposeAsync(); }
    }
    private sealed class HeldFile : IDisposable
    {
        private readonly FileOutputOwner owner; private readonly bool holdFlush, fail, prefix; private CancellationTokenRegistration cancellation; private CancellationToken ioToken;
        internal bool CancellationRequested => ioToken.IsCancellationRequested;
        internal readonly TaskCompletionSource Entered = Signal(), Release = Signal(); internal byte[]? FirstLine;
        internal int Writes, Flushes, Cancellations, Disposals; internal volatile bool InFlight; internal bool UnsafeDispose;
        internal HeldFile(string path, bool holdFlush, bool fail, bool prefix)
        { owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines); owner.ActiveFile.Position = owner.ActiveFile.Length; this.holdFlush = holdFlush; this.fail = fail; this.prefix = prefix; }
        internal async ValueTask WriteAsync(AdmittedEvent item, CancellationToken token)
        {
            InFlight = true;
            try
            {
                if (++Writes == 1)
                {
                    FirstLine = item.Event.JsonLine.ToArray(); ioToken = token; cancellation = token.Register(() => Interlocked.Increment(ref Cancellations));
                    if (!holdFlush)
                    {
                        if (prefix) { await owner.ActiveFile.WriteAsync(FirstLine.AsMemory(0, 32), CancellationToken.None); }
                        Entered.TrySetResult(); await Release.Task; if (fail) { throw new IOException("private controlled late IO failure"); }
                        await owner.ActiveFile.WriteAsync(FirstLine.AsMemory(prefix ? 32 : 0), CancellationToken.None); return;
                    }
                }
                await owner.ActiveFile.WriteAsync(item.Event.JsonLine, CancellationToken.None);
            }
            finally { InFlight = false; }
        }
        internal async ValueTask FlushAsync(CancellationToken token)
        {
            _ = token; InFlight = true;
            try { Flushes++; if (holdFlush) { Entered.TrySetResult(); await Release.Task; } await owner.ActiveFile.FlushAsync(CancellationToken.None); }
            finally { InFlight = false; }
        }
        internal JsonObject Report() => new() { ["writes"] = Writes, ["flushes"] = Flushes, ["cancellations"] = Cancellations, ["disposals"] = Disposals, ["unsafeDispose"] = UnsafeDispose, ["inFlight"] = InFlight };
        public void Dispose() { Disposals++; UnsafeDispose |= InFlight; cancellation.Dispose(); owner.Dispose(); }
    }
}
