using System.Text.Json.Nodes;
using CIS.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;
namespace CIS.Tests.DiscoveryFixture;

internal static class StopStartFixture
{
    internal static readonly string[] Modes = ["empty", "normal", "dual", "configured-default", "configured-min", "configured-max", "configured-below", "configured-above", "env", "shared", "pre-cancelled", "blocked-write", "blocked-flush", "clock-correction", "late-facts", "closed-writes"];
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode).GetAwaiter().GetResult().ToJsonString()); return 0; }
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
    internal static async Task<JsonObject> ExecuteAsync(string mode)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal));
        bool empty = mode == "empty", dual = mode == "dual", invalid = mode is "configured-below" or "configured-above";
        bool success = empty || mode is "normal" or "dual" or "closed-writes", flush = mode == "blocked-flush", cancelled = mode == "pre-cancelled";
        int milliseconds = mode == "configured-min" ? 1 : mode == "configured-max" ? 300000 : mode == "configured-default" ? 5000 : 10;
        var clock = new Clock(); using var writer = new Writer(holdFirstWrite: !success && !flush, holdFirstFlush: flush);
        using var file = new Writer(holdFirstWrite: false);
        var options = mode == "env" ? EnvironmentOptions.Logging : new LoggingOptions
        {
            ConsoleLevel = empty ? CoreLogLevel.None : CoreLogLevel.Information,
            FileLevel = dual ? CoreLogLevel.Warning : CoreLogLevel.None,
            FilePath = dual ? "controlled" : null,
            QueueCapacity = 3,
            MaxEntryBytes = 16384,
            ShutdownTimeoutMilliseconds = milliseconds
        };
        if (mode == "configured-default") { options = new LoggingOptions { ConsoleLevel = CoreLogLevel.Information, FileLevel = CoreLogLevel.None, QueueCapacity = 3, MaxEntryBytes = 16384 }; }
        if (mode == "env") { Require(options.ShutdownTimeoutMilliseconds == 250); milliseconds = options.ShutdownTimeoutMilliseconds; }
        if (invalid)
        {
            options.ShutdownTimeoutMilliseconds = mode == "configured-below" ? 0 : 300001; int acquired = 0;
            string? error = null;
            try { CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, clock, acquireConsole: _ => { acquired++; return new MemoryStream(); }); }
            catch (ArgumentOutOfRangeException caught) { error = caught.GetType().Name; }
            Require(error == "ArgumentOutOfRangeException" && acquired == 0 && clock.Created == 0);
            JsonObject bad = Common(mode); bad["error"] = error; bad["acquired"] = acquired; bad["timers"] = clock.Report(); return bad;
        }
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options,
            empty ? null : writer.WriteAsync, dual ? file.WriteAsync : null, clock,
            consoleFlush: empty ? null : writer.FlushAsync, fileFlush: dual ? file.FlushAsync : null);
        Task? lazy = null; var lazyEntered = Signal(); var lazyRelease = Signal();
        try
        {
            Require(runtime.GetDiagnostics().LastStopResult is null);
            CoreLogger logger = runtime.CreateLogger("StopStart");
            if (mode == "late-facts")
            {
                Require(logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "" }).Outcome == LogWriteOutcome.InvalidEvent);
                lazy = Task.Run(() =>
                {
                    LogWriteResult late = logger.WriteLazy(CoreLogLevel.Information, () =>
                    {
                        lazyEntered.TrySetResult(); Require(lazyRelease.Task.Wait(Guard));
                        return new LogEventData { EventName = "" };
                    });
                    Require(late.Outcome == LogWriteOutcome.InvalidEvent);
                });
                await lazyEntered.Task.WaitAsync(Guard);
            }
            if (!empty)
            {
                LogWriteResult write = logger.Write(CoreLogLevel.Information, Data("one"));
                Require(write.Outcome == LogWriteOutcome.Accepted);
                await writer.FirstWrite.Task.WaitAsync(Guard);
                if (dual) { Accepted(logger.Write(CoreLogLevel.Warning, Data("both"))); }
                if (flush) { await UntilAsync(() => runtime.QueuedOutputs!.Console!.CaptureAccounting().Written == 1); }
            }
            // Runtime creation freezes the budget before even the first stop caller.
            options.ShutdownTimeoutMilliseconds = milliseconds == 1 ? 300000 : 1;
            using var caller = new CancellationTokenSource(); if (cancelled) { caller.Cancel(); }
            Task<LogStopResult> first;
            var starters = new List<Task<LogStopResult>>();
            if (mode == "shared")
            {
                await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
                {
                    Task<LogStopResult> call = runtime.StopAsync(CancellationToken.None);
                    lock (starters) { starters.Add(call); }
                }))).WaitAsync(Guard);
                first = starters[0]; Require(starters.All(t => ReferenceEquals(t, first)));
            }
            else { first = runtime.StopAsync(cancelled ? caller.Token : CancellationToken.None); }
            Require(runtime.GetDiagnostics().AdmissionClosed);
            bool tokenPreserved = false;
            if (cancelled)
            {
                try { await first.WaitAsync(Guard); throw new InvalidOperationException("Cancelled stop succeeded."); }
                catch (OperationCanceledException error) { Require(error.CancellationToken == caller.Token); tokenPreserved = true; }
            }
            Task<LogStopResult> shared = runtime.StopAsync(CancellationToken.None);
            Require(cancelled || ReferenceEquals(first, shared));
            Task<LogStopResult>[] joins = Enumerable.Range(0, 64).Select(_ => runtime.StopAsync(CancellationToken.None)).ToArray();
            Require(joins.All(t => ReferenceEquals(t, shared)) && clock.Created == (empty ? 0 : 1));
            options.ShutdownTimeoutMilliseconds = milliseconds == 1 ? 300000 : 1;
            if (flush) { await writer.FirstFlush.Task.WaitAsync(Guard); }
            if (lazy is not null)
            {
                lazyRelease.TrySetResult(); await lazy.WaitAsync(Guard);
                Require(runtime.GetDiagnostics().InvalidEventCount == 2);
            }
            if (!success)
            {
                Require(!shared.IsCompleted && runtime.GetDiagnostics().LastStopResult is null);
                clock.ShiftUtc(-20); clock.Advance(TimeSpan.FromMilliseconds(milliseconds) - TimeSpan.FromTicks(1)); Require(!shared.IsCompleted);
                clock.ShiftUtc(40); clock.Advance(TimeSpan.FromTicks(1));
            }
            LogStopResult result = await shared.WaitAsync(Guard); JsonObject frozen = Result(result);
            Require(result.Completion == (success ? LogCompletion.Completed : LogCompletion.TimedOut));
            Require(ReferenceEquals(result, runtime.GetDiagnostics().LastStopResult));
            Require(result.InvalidEventCount == (mode == "late-facts" ? 1 : 0));
            int factories = 0;
            foreach (CoreLogLevel level in new[] { CoreLogLevel.Information, CoreLogLevel.None })
            { Require(logger.WriteLazy(level, () => { factories++; return Data("late"); }).Outcome == LogWriteOutcome.Stopped); }
            Require(factories == 0);
            writer.ReleaseAll(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard);
            Require(Result(result).ToJsonString() == frozen.ToJsonString() && ReferenceEquals(result, await runtime.StopAsync(CancellationToken.None)));
            await UntilAsync(() => clock.Active == 0);
            JsonObject report = Common(mode); report["result"] = frozen; report["writer"] = writer.Report(); report["file"] = file.Report(); report["timers"] = clock.Report();
            report["tokenPreserved"] = tokenPreserved; report["joinedCallers"] = joins.Length; report["concurrentStarters"] = starters.Count; report["configuredMilliseconds"] = milliseconds;
            report["cachedIdentity"] = ReferenceEquals(result, runtime.GetDiagnostics().LastStopResult);
            report["immutableAfterIo"] = Result(result).ToJsonString() == frozen.ToJsonString();
            report["lateFactories"] = factories; report["diagnostics"] = Diagnostics(runtime);
            report["liveInvalid"] = Number(runtime.GetDiagnostics().InvalidEventCount);
            report["finalState"] = runtime.GetDiagnostics().State.ToString(); report["sharedWorkersJoined"] = runtime.QueuedOutputs.Completion.IsCompletedSuccessfully;
            return report;
        }
        finally { lazyRelease.TrySetResult(); if (lazy is not null) { await lazy.WaitAsync(Guard); } await Cleanup(runtime, writer, file); }
    }
}
