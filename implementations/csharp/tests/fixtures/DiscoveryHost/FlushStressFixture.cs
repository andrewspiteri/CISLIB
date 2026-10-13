using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushStressFixture
{
    internal static readonly string[] Modes = ["repeat-write", "repeat-flush", "repeat-progress", "saturation", "cohort", "failed-flush", "concurrent", "deadline-race"];
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal));
        Require(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription == ".NET 10.0.12");
        return mode switch
        {
            "repeat-write" or "repeat-flush" or "repeat-progress" => await RepeatAsync(mode),
            "concurrent" => await ConcurrentAsync(mode, root),
            "saturation" => await SaturationAsync(mode, root),
            "cohort" or "failed-flush" => await CohortAsync(mode, root),
            "deadline-race" => await DeadlineRaceAsync(mode),
            _ => throw new ArgumentException("Unknown stress mode.", nameof(mode))
        };
    }
    private static LoggingOptions Options(string root, int capacity = 3, CoreLogLevel file = CoreLogLevel.None) => new()
    { ConsoleLevel = CoreLogLevel.Information, FileLevel = file, FilePath = file == CoreLogLevel.None ? null : Path.Combine(root, "unused.jsonl"), QueueCapacity = capacity, MaxEntryBytes = 16384 };
    private static CoreLogRuntime Runtime(Clock clock, LoggingOptions options, Writer console, Writer? file = null)
        => CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, console.WriteAsync, file is null ? null : file.WriteAsync, clock, console.FlushAsync, file is null ? null : file.FlushAsync);
    private static async Task Cancelled(Task<LogFlushResult> wait, CancellationToken token)
    {
        try { await wait.WaitAsync(Guard); throw new InvalidOperationException("Expected cancellation."); }
        catch (OperationCanceledException error) { Require(error.CancellationToken == token); }
    }
    private static void Drain(LogFlushResult result, long target, long written, long pending, string flush, bool loss, long unavailable = 0)
    {
        Require(result.Outputs.Count == 1 && result.LossObserved == loss);
        LogOutputDrainResult output = result.Outputs[0];
        Require(output.AcceptedSequenceBoundary == target && output.Written == written && output.Failed == 0 && output.Abandoned == 0 && output.Pending == pending &&
            output.WriterFlushStatus.ToString() == flush && output.Rejections.Unavailable == unavailable);
        Require(target == output.Written + output.Failed + output.Abandoned + output.Pending);
    }
    private static async Task<JsonObject> RepeatAsync(string mode)
    {
        bool flush = mode == "repeat-flush", progress = mode == "repeat-progress";
        var clock = new Clock(); using var writer = new Writer(!flush, flush); using var sharedCaller = new CancellationTokenSource();
        CoreLogRuntime runtime = Runtime(clock, Options("unused"), writer); CoreLogger logger = runtime.CreateLogger("FlushStress");
        JsonObject report = Common(mode); OutputEventQueue queue = runtime.QueuedOutputs!.Console!;
        Task? progressTask = null;
        try
        {
            if (progress)
            {
                foreach (string field in new[] { "lastAcceptedSequence", "acceptedCount", "writtenCount" })
                { typeof(OutputEventQueue).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, long.MaxValue - 1); }
                Accepted(logger.Write(CoreLogLevel.Information, Data("first"))); await writer.FirstWrite.Task.WaitAsync(Guard);
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                await Cancelled(runtime.FlushAsync(TimeSpan.FromSeconds(5), cancelled.Token), cancelled.Token);
                Require(logger.Write(CoreLogLevel.Information, Data("exhausted")).Outcome == LogWriteOutcome.Rejected);
            }
            else
            {
                if (flush)
                {
                    using var cancelled = new CancellationTokenSource();
                    Task<LogFlushResult> zero = runtime.FlushAsync(TimeSpan.FromSeconds(5), cancelled.Token);
                    await writer.FirstFlush.Task.WaitAsync(Guard); cancelled.Cancel(); await Cancelled(zero, cancelled.Token);
                }
                for (int index = 0; index < (flush ? 3 : 4); index++)
                {
                    Accepted(logger.Write(CoreLogLevel.Information, Data("record." + index)));
                    if (index == 0 && !flush) { await writer.FirstWrite.Task.WaitAsync(Guard); }
                    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                    await Cancelled(runtime.FlushAsync(TimeSpan.FromSeconds(5), cancelled.Token), cancelled.Token);
                }
                Require(logger.Write(CoreLogLevel.Information, Data("overflow")).Outcome == LogWriteOutcome.Rejected);
                Require(logger.Write(CoreLogLevel.Information, Data("")).Outcome == LogWriteOutcome.InvalidEvent);
            }
            JsonObject before = QueueGauge(queue);
            Require(before["controls"]!.GetValue<int>() == (progress ? 0 : 4));
            var counters = await RepeatCallersAsync();
            // The helper frame no longer retains its per-caller deadlines or task locals.
            for (int attempt = 0; attempt < 3; attempt++) { GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); }
            Require(clock.Samples.Count == 10 && clock.Samples.All(sample => !sample.IsAlive));
            report["weakSamples"] = clock.Samples.Count; report["collectedSamples"] = clock.Samples.Count(sample => !sample.IsAlive);
            report["before"] = before; report["repeated"] = counters; report["afterDetach"] = QueueGauge(queue);
            if (progress)
            {
                Require(progressTask is not null && !progressTask.IsCompleted && FlushCoordinationFixture.Continuations(progressTask) == 0);
                report["progressSlots"] = FlushCoordinationFixture.ContinuationSlots(progressTask);
            }
            Task<LogFlushResult> final = runtime.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Require(!final.IsCompleted); writer.ReleaseAll();
            LogFlushResult settled = await final.WaitAsync(Guard); Require(settled.Completion == LogCompletion.Completed);
            Drain(settled, progress ? long.MaxValue : flush ? 3 : 4, progress ? long.MaxValue : flush ? 3 : 4, 0,
                progress ? "Failed" : "Succeeded", true, progress ? 1 : 0);
            await Cleanup(runtime, writer);
            report["final"] = Result(settled); report["writer"] = writer.Report(); report["diagnostics"] = Diagnostics(runtime);
            report["timers"] = clock.Report(); report["finalControls"] = queue.RegisteredFlushCount;
            report["progressCleared"] = Progress(queue) is null;
            return report;
        }
        finally { await Cleanup(runtime, writer); }

        async Task<JsonObject> RepeatCallersAsync()
        {
            int peakAttached = 0, peakSlots = 0, cancelledCount = 0, timedOut = 0;
            JsonObject? first = null, last = null; int baselineCreated = clock.Created;
            clock.SampleOwners = true;
            for (int index = 0; index < 5000; index++)
            {
                using var cancellation = new CancellationTokenSource();
                bool cancel = index % 2 == 0;
                Task<LogFlushResult> wait = runtime.FlushAsync(TimeSpan.FromSeconds(5), cancel ? cancellation.Token : sharedCaller.Token);
                Task held = progress ? Progress(queue)! : Registrations(queue).Single(r => r.AcceptedSequenceBoundary == queue.LastAcceptedSequence).WriterFlushCompletion;
                progressTask ??= progress ? held : null;
                Require(!held.IsCompleted && FlushCoordinationFixture.Continuations(held) == 1 && clock.Active == 1);
                peakAttached = Math.Max(peakAttached, FlushCoordinationFixture.Continuations(held));
                peakSlots = Math.Max(peakSlots, FlushCoordinationFixture.ContinuationSlots(held));
                if (cancel) { cancellation.Cancel(); await Cancelled(wait, cancellation.Token); cancelledCount++; }
                else
                {
                    clock.Advance(TimeSpan.FromSeconds(5));
                    LogFlushResult result = await wait.WaitAsync(Guard);
                    Require(result.Completion == LogCompletion.TimedOut && !result.Succeeded);
                    Drain(result, progress ? long.MaxValue : flush ? 3 : 4, progress ? long.MaxValue - 1 : 0, progress ? 1 : flush ? 3 : 4,
                        progress ? "Failed" : "Pending", true, progress ? 1 : 0);
                    first ??= Result(result); last = Result(result); timedOut++;
                }
                Require(FlushCoordinationFixture.Continuations(held) == 0 && FlushCoordinationFixture.ContinuationSlots(held) <= 4 &&
                    clock.Active == 0 && queue.RegisteredFlushCount == (progress ? 0 : 4));
            }
            clock.SampleOwners = false;
            Require(clock.Created - baselineCreated == 5000 && cancelledCount == 2500 && timedOut == 2500 && !sharedCaller.IsCancellationRequested);
            return new JsonObject { ["iterations"] = 5000, ["cancelled"] = cancelledCount, ["timedOut"] = timedOut, ["peakAttached"] = peakAttached, ["peakSlots"] = peakSlots, ["first"] = first, ["last"] = last };
        }
    }
    private static async Task<JsonObject> ConcurrentAsync(string mode, string root)
    {
        var clock = new Clock(); using var console = new Writer(); using var file = new Writer();
        CoreLogRuntime runtime = Runtime(clock, Options(root, 256, CoreLogLevel.Warning), console, file);
        CoreLogger logger = runtime.CreateLogger("FlushStress"); var writes = new ConcurrentBag<JsonObject>();
        var waits = new ConcurrentBag<(string key, Task<LogFlushResult> wait)>(); var go = Signal();
        try
        {
            LogWriteResult first = logger.Write(CoreLogLevel.Error, Data("first")); Accepted(first); writes.Add(WriteRow("first", first));
            await console.FirstWrite.Task.WaitAsync(Guard); await file.FirstWrite.Task.WaitAsync(Guard);
            Task[] producers = Enumerable.Range(0, 16).Select(producer => Task.Run(async () =>
            {
                await go.Task;
                for (int index = 0; index < 16; index++)
                {
                    string name = "producer." + producer + "." + index;
                    LogWriteResult result = logger.Write(index % 2 == 0 ? CoreLogLevel.Warning : CoreLogLevel.Information, Data(name));
                    Accepted(result); writes.Add(WriteRow(name, result));
                    if (index % 4 == 3)
                    {
                        string key = producer + "." + index; Clock.CallerKey = key;
                        try { waits.Add((key, runtime.FlushAsync(TimeSpan.FromSeconds(10), CancellationToken.None))); }
                        finally { Clock.CallerKey = null; }
                    }
                }
            })).ToArray();
            go.TrySetResult(); await Task.WhenAll(producers).WaitAsync(Guard);
            JsonObject consoleHeld = QueueGauge(runtime.QueuedOutputs!.Console!), fileHeld = QueueGauge(runtime.QueuedOutputs.File!);
            Require(writes.Count == 257 && waits.Count == 64 && clock.Captures.Count == 64 && clock.Active == 64 &&
                consoleHeld["attached"]!.GetValue<int>() == 64 && fileHeld["attached"]!.GetValue<int>() == 0 &&
                consoleHeld["buffered"]!.GetValue<int>() == 256 && fileHeld["buffered"]!.GetValue<int>() == 128);
            long[] consoleTargets = clock.Captures.Select(c => long.Parse(c["outputs"]![0]!["boundary"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
            long[] fileTargets = clock.Captures.Select(c => long.Parse(c["outputs"]![1]!["boundary"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
            Require(consoleTargets.Length == runtime.QueuedOutputs.Console!.RegisteredFlushCount && fileTargets.Length == runtime.QueuedOutputs.File!.RegisteredFlushCount);
            Require(consoleTargets.Length <= 64 && fileTargets.Length <= 64 && consoleHeld["slots"]!.GetValue<int>() <= 256);
            console.ReleaseAll(); file.ReleaseAll();
            var observations = new JsonArray();
            foreach ((string key, Task<LogFlushResult> wait) in waits.OrderBy(w => w.key, StringComparer.Ordinal))
            {
                LogFlushResult result = await wait.WaitAsync(Guard); JsonObject initial = clock.Captures.Single(c => c["caller"]!.GetValue<string>() == key);
                Require(result.Completion == LogCompletion.Completed && result.Succeeded && !result.LossObserved);
                for (int index = 0; index < 2; index++)
                {
                    long boundary = long.Parse(initial["outputs"]![index]!["boundary"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
                    Require(result.Outputs[index].AcceptedSequenceBoundary == boundary && result.Outputs[index].Written == boundary && result.Outputs[index].Pending == 0 &&
                        result.Outputs[index].Rejections.Filtered.ToString(System.Globalization.CultureInfo.InvariantCulture) == initial["outputs"]![index]!["filtered"]!.GetValue<string>());
                }
                observations.Add(new JsonObject { ["caller"] = key, ["initial"] = initial.DeepClone(), ["final"] = Result(result) });
            }
            await Cleanup(runtime, console, file);
            JsonObject report = Common(mode); report["writes"] = new JsonArray(writes.OrderBy(w => w["name"]!.GetValue<string>(), StringComparer.Ordinal).Select(w => (JsonNode)w).ToArray());
            report["observations"] = observations; report["heldConsole"] = consoleHeld; report["heldFile"] = fileHeld;
            report["console"] = console.Report(); report["file"] = file.Report(); report["timers"] = clock.Report(); report["diagnostics"] = Diagnostics(runtime); return report;
        }
        finally { go.TrySetResult(); await Cleanup(runtime, console, file); }
    }
    private static JsonObject WriteRow(string name, LogWriteResult result) => new()
    {
        ["name"] = name,
        ["outputs"] = new JsonArray(result.Outputs.Select(output => (JsonNode)new JsonObject
        {
            ["name"] = output.OutputName,
            ["status"] = output.Status.ToString(),
            ["ticket"] = output.AcceptedSequence is { } ticket ? Number(ticket) : null
        }).ToArray())
    };

    // Remaining modes use the same actual Core paths and bounded observations.
    private static async Task<JsonObject> SaturationAsync(string mode, string root)
    {
        var clock = new Clock(); using var console = new Writer(holdSecondWrite: true); using var file = new Writer(holdFirstFlush: true);
        CoreLogRuntime runtime = Runtime(clock, Options(root, 3, CoreLogLevel.Error), console, file); CoreLogger logger = runtime.CreateLogger("FlushStress");
        var writes = new ConcurrentBag<JsonObject>();
        try
        {
            LogWriteResult first = logger.Write(CoreLogLevel.Error, Data("first")); Accepted(first); writes.Add(WriteRow("first", first));
            await console.FirstWrite.Task.WaitAsync(Guard); await file.FirstWrite.Task.WaitAsync(Guard);
            Task<LogFlushResult> early = runtime.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            var go = Signal();
            Task[] producers = Enumerable.Range(0, 64).Select(index => Task.Run(async () =>
            { await go.Task; string name = "producer." + index; writes.Add(WriteRow(name, logger.Write(CoreLogLevel.Information, Data(name)))); })).ToArray();
            go.TrySetResult(); await Task.WhenAll(producers).WaitAsync(Guard);
            Require(writes.Count(w => w["outputs"]![0]!["status"]!.GetValue<string>() == "Accepted") == 4);
            Require(logger.Write(CoreLogLevel.Information, Data("")).Outcome == LogWriteOutcome.InvalidEvent);
            Task<LogFlushResult> late = runtime.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            file.ReleaseFirst.TrySetResult(); await file.FirstFlush.Task.WaitAsync(Guard);
            clock.ShiftUtc(20); clock.Advance(TimeSpan.Zero, true); Require(!early.IsCompleted && !late.IsCompleted);
            clock.ShiftUtc(-40); clock.Advance(TimeSpan.Zero, true);
            Require(clock.Due.SequenceEqual(new[] { TimeSpan.TicksPerSecond * 5, TimeSpan.TicksPerSecond * 5 }));
            JsonObject heldConsole = QueueGauge(runtime.QueuedOutputs!.Console!), heldFile = QueueGauge(runtime.QueuedOutputs.File!);
            var dueBeforeTimeout = new JsonArray(clock.Due.Select(d => (JsonNode)JsonValue.Create(Number(d))!).ToArray());
            Require(heldConsole["controls"]!.GetValue<int>() == 2 && heldFile["controls"]!.GetValue<int>() == 1);
            clock.Advance(TimeSpan.FromSeconds(5));
            LogFlushResult firstTimeout = await early.WaitAsync(Guard), fullTimeout = await late.WaitAsync(Guard);
            Require(firstTimeout.Completion == LogCompletion.TimedOut && !firstTimeout.Succeeded && !firstTimeout.LossObserved &&
                firstTimeout.Outputs[0].AcceptedSequenceBoundary == 1 && firstTimeout.Outputs[0].Pending == 1 && firstTimeout.InvalidEventCount == 0 &&
                fullTimeout.Completion == LogCompletion.TimedOut && fullTimeout.LossObserved && fullTimeout.InvalidEventCount == 1 &&
                fullTimeout.Outputs[0].AcceptedSequenceBoundary == 4 && fullTimeout.Outputs[0].Pending == 4 && fullTimeout.Outputs[0].Rejections.QueueFull == 61);
            JsonObject detachedConsole = QueueGauge(runtime.QueuedOutputs.Console!), detachedFile = QueueGauge(runtime.QueuedOutputs.File!);
            Require(detachedConsole["controls"]!.GetValue<int>() == 2 && detachedConsole["attached"]!.GetValue<int>() == 0 &&
                detachedFile["controls"]!.GetValue<int>() == 1 && detachedFile["attached"]!.GetValue<int>() == 0);
            string frozen = Result(firstTimeout).ToJsonString() + Result(fullTimeout).ToJsonString();
            Task<LogFlushResult> joined = runtime.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            console.ReleaseFirst.TrySetResult(); await console.SecondWrite.Task.WaitAsync(Guard);
            Require(console.Order.SequenceEqual(new[] { "write.1", "flush.1" }) && !joined.IsCompleted);
            console.ReleaseSecond.TrySetResult(); file.ReleaseFlush.TrySetResult();
            LogFlushResult final = await joined.WaitAsync(Guard);
            Require(final.Completion == LogCompletion.Completed && final.Succeeded && final.LossObserved);
            await Cleanup(runtime, console, file);
            Require(frozen == Result(firstTimeout).ToJsonString() + Result(fullTimeout).ToJsonString());
            JsonObject report = Common(mode); report["early"] = Result(firstTimeout); report["late"] = Result(fullTimeout); report["final"] = Result(final);
            report["writes"] = new JsonArray(writes.OrderBy(w => w["name"]!.GetValue<string>(), StringComparer.Ordinal).Select(w => (JsonNode)w).ToArray());
            report["heldConsole"] = heldConsole; report["heldFile"] = heldFile; report["detachedConsole"] = detachedConsole; report["detachedFile"] = detachedFile;
            report["utcCorrectionsPreservedDue"] = true; report["dueBeforeTimeout"] = dueBeforeTimeout; report["immutableAfterIo"] = true;
            report["console"] = console.Report(); report["file"] = file.Report(); report["timers"] = clock.Report(); report["diagnostics"] = Diagnostics(runtime); return report;
        }
        finally { await Cleanup(runtime, console, file); }
    }
    private static async Task<JsonObject> CohortAsync(string mode, string root)
    {
        bool failure = mode == "failed-flush";
        var clock = new Clock(); using var console = new Writer(holdFirstFlush: failure, failFirstFlush: failure);
        using var file = new Writer(holdFirstFlush: failure, holdSecondWrite: failure);
        CoreLogRuntime runtime = Runtime(clock, Options(root, 3, CoreLogLevel.Information), console, file);
        CoreLogger logger = runtime.CreateLogger("FlushStress");
        var sources = Enumerable.Range(0, 64).Select(_ => new CancellationTokenSource()).ToArray();
        var waits = new List<Task<LogFlushResult>>(); var phases = new JsonArray(); var timeouts = new JsonArray(); var results = new JsonArray(); var laterAccepted = new JsonArray();
        try
        {
            Accepted(logger.Write(CoreLogLevel.Information, Data("record.0"))); await console.FirstWrite.Task.WaitAsync(Guard); await file.FirstWrite.Task.WaitAsync(Guard);
            if (!failure)
            {
                for (int index = 0; index < 4; index++)
                {
                    if (index > 0) { Accepted(logger.Write(CoreLogLevel.Information, Data("record." + index))); }
                    using var pre = new CancellationTokenSource(); pre.Cancel(); await Cancelled(runtime.FlushAsync(TimeSpan.FromSeconds(5), pre.Token), pre.Token);
                }
                Require(logger.Write(CoreLogLevel.Information, Data("overflow")).Outcome == LogWriteOutcome.Rejected);
                Require(logger.Write(CoreLogLevel.Information, Data("")).Outcome == LogWriteOutcome.InvalidEvent);
            }
            for (int index = 0; index < 64; index++)
            { waits.Add(runtime.FlushAsync(TimeSpan.FromSeconds(index is >= 32 and < 48 ? 1 : 5), sources[index].Token)); }
            phases.Add(Phase("all"));
            Require(runtime.QueuedOutputs!.Console!.RegisteredFlushCount == (failure ? 1 : 4) && runtime.QueuedOutputs.File!.RegisteredFlushCount == (failure ? 1 : 4));
            Require(phases[0]!["console"]!["attached"]!.GetValue<int>() == 64 && clock.Active == 64);
            if (failure)
            {
                console.ReleaseFirst.TrySetResult(); file.ReleaseFirst.TrySetResult();
                await console.FirstFlush.Task.WaitAsync(Guard); await file.FirstFlush.Task.WaitAsync(Guard);
                var go = Signal();
                Task<LogWriteResult>[] producers = Enumerable.Range(0, 64).Select(index => Task.Run(async () =>
                { await go.Task; return logger.Write(CoreLogLevel.Information, Data("later." + index)); })).ToArray();
                go.TrySetResult(); LogWriteResult[] writes = await Task.WhenAll(producers).WaitAsync(Guard);
                Require(writes.Count(w => w.Outcome == LogWriteOutcome.Accepted) == 3 && writes.Count(w => w.Outcome == LogWriteOutcome.Rejected) == 61);
                foreach (var item in writes.Select((w, i) => (w, i)).Where(item => item.w.Outcome == LogWriteOutcome.Accepted).OrderBy(item => item.w.Outputs[1].AcceptedSequence))
                { laterAccepted.Add(new JsonObject { ["name"] = "later." + item.i, ["ticket"] = Number(item.w.Outputs[1].AcceptedSequence!.Value) }); }
            }
            for (int index = 0; index < 32; index++) { sources[index].Cancel(); await Cancelled(waits[index], sources[index].Token); }
            clock.Advance(TimeSpan.FromSeconds(1));
            for (int index = 32; index < 48; index++)
            {
                LogFlushResult result = await waits[index].WaitAsync(Guard);
                Require(result.Completion == LogCompletion.TimedOut && !result.Succeeded && result.LossObserved == !failure);
                Require(result.Outputs.All(o => o.AcceptedSequenceBoundary == (failure ? 1 : 4) && o.Written == (failure ? 1 : 0) &&
                    o.Pending == (failure ? 0 : 4) && o.WriterFlushStatus == LogWriterFlushStatus.Pending && o.Rejections.QueueFull == (failure ? 0 : 1)));
                timeouts.Add(Result(result));
            }
            phases.Add(Phase("detached"));
            Require(phases[1]!["console"]!["attached"]!.GetValue<int>() == 16 && clock.Active == 16 &&
                phases[1]!["console"]!["controls"]!.GetValue<int>() == (failure ? 1 : 4) &&
                phases[1]!["file"]!["controls"]!.GetValue<int>() == (failure ? 1 : 4));
            string frozen = timeouts.ToJsonString();
            if (failure)
            {
                console.ReleaseFlush.TrySetResult();
                try { await runtime.QueuedOutputs.Console!.Completion.WaitAsync(Guard); throw new InvalidOperationException("Expected worker failure."); } catch (IOException) { }
            }
            else { console.ReleaseAll(); }
            await UntilAsync(() => QueueGauge(runtime.QueuedOutputs.File!)["attached"]!.GetValue<int>() == 16);
            phases.Add(Phase("file"));
            Require(phases[2]!["file"]!["controls"]!.GetValue<int>() == (failure ? 1 : 4));
            int fileCancelled = 0;
            if (!failure)
            {
                clock.ShiftUtc(20); clock.Advance(TimeSpan.Zero, true); clock.ShiftUtc(-40);
                clock.Advance(TimeSpan.FromSeconds(3), true);
                Require(clock.Due.All(due => due == TimeSpan.TicksPerSecond * 5));
                for (int index = 48; index < 56; index++) { sources[index].Cancel(); await Cancelled(waits[index], sources[index].Token); fileCancelled++; }
                phases.Add(Phase("file-detached"));
                Require(phases[3]!["file"]!["attached"]!.GetValue<int>() == 8 && clock.Active == 8);
            }
            if (failure) { file.ReleaseFlush.TrySetResult(); } else { file.ReleaseAll(); }
            for (int index = failure ? 48 : 56; index < 64; index++)
            {
                LogFlushResult result = await waits[index].WaitAsync(Guard);
                Require(result.Completion == LogCompletion.Completed && result.Succeeded == !failure && result.LossObserved);
                Require(result.Outputs.All(o => o.AcceptedSequenceBoundary == (failure ? 1 : 4) && o.Written == (failure ? 1 : 4) && o.Failed == 0 && o.Abandoned == 0 && o.Pending == 0));
                Require(result.Outputs[0].WriterFlushStatus == (failure ? LogWriterFlushStatus.Failed : LogWriterFlushStatus.Succeeded) && result.Outputs[1].WriterFlushStatus == LogWriterFlushStatus.Succeeded);
                results.Add(Result(result));
            }
            if (failure) { await file.SecondWrite.Task.WaitAsync(Guard); Require(runtime.QueuedOutputs.File!.CaptureAccounting().Pending == 3); file.ReleaseSecond.TrySetResult(); }
            await Cleanup(runtime, console, file);
            Require(frozen == timeouts.ToJsonString());
            JsonObject report = Common(mode); report["phases"] = phases; report["timeouts"] = timeouts; report["results"] = results;
            report["cancelled"] = 32 + fileCancelled; report["immutableAfterIo"] = true; report["laterAccepted"] = laterAccepted;
            report["console"] = console.Report(); report["file"] = file.Report(); report["timers"] = clock.Report(); report["diagnostics"] = Diagnostics(runtime); return report;
        }
        finally { await Cleanup(runtime, console, file); foreach (CancellationTokenSource source in sources) { source.Dispose(); } }
        JsonObject Phase(string label) => new() { ["label"] = label, ["console"] = QueueGauge(runtime.QueuedOutputs!.Console!), ["file"] = QueueGauge(runtime.QueuedOutputs.File!), ["timers"] = clock.Active, ["dueTicks"] = new JsonArray(clock.Due.Select(d => (JsonNode)JsonValue.Create(Number(d))!).ToArray()) };
    }
    private static async Task<JsonObject> DeadlineRaceAsync(string mode)
    {
        var observations = new JsonArray();
        for (int iteration = 0; iteration < 32; iteration++)
        {
            bool cancel = iteration % 2 == 0; var clock = new Clock(); using var writer = new Writer(); using var caller = new CancellationTokenSource();
            CoreLogRuntime runtime = Runtime(clock, Options("unused"), writer); CoreLogger logger = runtime.CreateLogger("FlushStress");
            using var releaseCancellation = new ManualResetEventSlim(); var enteredCancellation = Signal(); int guardExpired = 0;
            Task? tick = null; Task<LogFlushResult>? wait = null;
            try
            {
                Accepted(logger.Write(CoreLogLevel.Information, Data("race." + iteration))); await writer.FirstWrite.Task.WaitAsync(Guard);
                wait = runtime.FlushAsync(TimeSpan.FromSeconds(5), caller.Token);
                object owner = clock.Owner;
                var lifetime = (CancellationTokenSource)Field(owner, "lifetime")!;
                // Installed after the real linked-token registration: LIFO blocks Cancel before caller notification.
                using CancellationTokenRegistration hook = lifetime.Token.Register(() =>
                {
                    enteredCancellation.TrySetResult();
                    if (!releaseCancellation.Wait(Guard)) { Interlocked.Exchange(ref guardExpired, 1); }
                });
                tick = Task.Run(() => clock.Advance(TimeSpan.FromSeconds(5)));
                await enteredCancellation.Task.WaitAsync(Guard);
                // Prove the hook is ahead of linked notification before either caller cancellation or I/O is released.
                await Task.Delay(20);
                Require(!clock.DisposalStarted.Task.IsCompleted && !wait.IsCompleted);
                if (cancel) { caller.Cancel(); } else { writer.ReleaseFirst.TrySetResult(); }
                await clock.DisposalStarted.Task.WaitAsync(Guard);
                Require(!wait.IsCompleted && clock.ActiveCallbacks == 1 && clock.PendingJoins == 1 && lifetime.IsCancellationRequested);
                _ = lifetime.Token; // This throws if the owned source was disposed before callback join.
                try { await wait.WaitAsync(TimeSpan.FromMilliseconds(20)); throw new InvalidOperationException("Callback join returned early."); }
                catch (TimeoutException) { }
                JsonObject during = clock.Report();
                releaseCancellation.Set(); await tick.WaitAsync(Guard);
                JsonObject? result = null;
                if (cancel) { await Cancelled(wait, caller.Token); }
                else
                {
                    LogFlushResult timeout = await wait.WaitAsync(Guard);
                    Require(timeout.Completion == LogCompletion.TimedOut && !timeout.Succeeded && !timeout.LossObserved);
                    Drain(timeout, 1, 0, 1, "Pending", false); result = Result(timeout);
                }
                Require(guardExpired == 0);
                writer.ReleaseAll(); await Cleanup(runtime, writer);
                Require(clock.Active == 0 && clock.ActiveCallbacks == 0 && clock.PendingJoins == 0 && clock.Created == 1 && clock.Disposed == 1);
                bool disposed = false; try { _ = lifetime.Token; } catch (ObjectDisposedException) { disposed = true; }
                Require(disposed);
                observations.Add(new JsonObject
                {
                    ["iteration"] = iteration,
                    ["cancelled"] = cancel,
                    ["during"] = during,
                    ["result"] = result,
                    ["timers"] = clock.Report(),
                    ["notificationHeldBeforeCompletion"] = true,
                    ["sourceReadableDuringJoin"] = true,
                    ["sourceDisposedAfterJoin"] = disposed,
                    ["writer"] = writer.Report(),
                    ["diagnostics"] = Diagnostics(runtime)
                });
            }
            finally
            {
                releaseCancellation.Set(); caller.Cancel(); writer.ReleaseAll();
                if (tick is not null) { await tick.WaitAsync(Guard); }
                if (wait is not null) { try { await wait.WaitAsync(Guard); } catch (OperationCanceledException) { } }
                await Cleanup(runtime, writer);
            }
        }
        JsonObject report = Common(mode); report["observations"] = observations; return report;
    }
}
