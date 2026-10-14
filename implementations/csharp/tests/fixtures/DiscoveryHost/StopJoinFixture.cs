using System.Text.Json.Nodes;
using CIS.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopJoinFixture
{
    internal static readonly string[] Modes = ["empty", "dispose-first", "stop-first", "pre-cancelled", "cancel-later", "cancelled-joins", "deadline-joins", "after-completion-cancel"];
    internal static int Run(string mode, string root)
    {
        _ = root; // This controlled fixture needs no filesystem destination.
        Console.WriteLine(ExecuteAsync(mode).GetAwaiter().GetResult().ToJsonString()); return 0;
    }
    internal static async Task<JsonObject> ExecuteAsync(string mode)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal)); bool empty = mode == "empty", deadline = mode == "deadline-joins";
        var clock = new FileTimerCoordinationFixture.Clock(); using var stream = new StopDrainFixture.HeldStream(true, false, false, false);
        var options = new LoggingOptions { ConsoleLevel = empty ? CoreLogLevel.None : CoreLogLevel.Information, ConsoleFormat = LogOutputFormat.JsonLines, FileLevel = CoreLogLevel.None, QueueCapacity = 3, MaxEntryBytes = 16384, ShutdownTimeoutMilliseconds = 10 };
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, clock, _ => stream);
        var callers = new List<CancellationTokenSource>(); int cancelled = 0; bool tokenIdentity = true;
        JsonObject? preCancelledStart = null;
        try
        {
            if (!empty) { Accepted(runtime.CreateLogger("Join").Write(CoreLogLevel.Information, Data("one"))); await stream.FirstWrite.Task.WaitAsync(Guard); }
            Task? initialDisposal = mode is "dispose-first" or "empty" ? runtime.DisposeAsync().AsTask() : null;
            if (mode == "pre-cancelled")
            {
                var caller = new CancellationTokenSource(); callers.Add(caller); caller.Cancel();
                await Cancelled(runtime.StopAsync(caller.Token), caller.Token);
                // Observe initiation before any uncancelled stop or disposal can start it instead.
                CoreLogDiagnostics started = runtime.GetDiagnostics();
                LogWriteOutcome stoppedWrite = runtime.CreateLogger("Join").Write(CoreLogLevel.Information, Data("after-cancelled-start")).Outcome;
                preCancelledStart = new JsonObject
                {
                    ["admissionClosed"] = started.AdmissionClosed,
                    ["shutdownStarted"] = started.ShutdownStartedAtUtc is not null,
                    ["state"] = started.State.ToString(),
                    ["writeOutcome"] = stoppedWrite.ToString(),
                    ["deadlineCreated"] = clock.Created
                };
                Require(started.AdmissionClosed && started.ShutdownStartedAtUtc is not null && started.State == CoreLogState.Stopping && stoppedWrite == LogWriteOutcome.Stopped && clock.Created == 1);
            }
            Task<LogStopResult> shared = runtime.StopAsync(CancellationToken.None);
            if (mode == "cancel-later")
            {
                var caller = new CancellationTokenSource(); callers.Add(caller);
                Task<LogStopResult> wait = runtime.StopAsync(caller.Token); caller.Cancel(); await Cancelled(wait, caller.Token);
            }
            if (mode is "cancelled-joins" or "deadline-joins")
            {
                for (int i = 0; i < 64; i++)
                {
                    var caller = new CancellationTokenSource(); callers.Add(caller); Task<LogStopResult> wait = runtime.StopAsync(caller.Token);
                    caller.Cancel(); await Cancelled(wait, caller.Token);
                }
            }
            bool outputCancelledBeforeDeadline = stream.Token.IsCancellationRequested;
            Require(!outputCancelledBeforeDeadline);
            if (deadline) { clock.Advance(TimeSpan.FromMilliseconds(9)); Require(!shared.IsCompleted); }
            Task[] disposals = Enumerable.Range(0, 64).Select(_ => runtime.DisposeAsync().AsTask()).ToArray();
            Require(disposals.All(task => ReferenceEquals(task, shared)) && (initialDisposal is null || ReferenceEquals(initialDisposal, shared)));
            if (deadline) { clock.Advance(TimeSpan.FromMilliseconds(1)); }
            else { stream.ReleaseAll(); }
            LogStopResult result = await shared.WaitAsync(Guard); await Task.WhenAll(disposals).WaitAsync(Guard);
            bool pendingAtReturn = !runtime.QueuedOutputs!.Completion.IsCompleted;
            bool outputCancelledAtReturn = stream.Token.IsCancellationRequested;
            string frozen = Json(result).ToJsonString();
            if (mode is "after-completion-cancel" or "empty")
            {
                var caller = new CancellationTokenSource(); callers.Add(caller); caller.Cancel(); await Cancelled(runtime.StopAsync(caller.Token), caller.Token);
            }
            stream.ReleaseAll(); await runtime.QueuedOutputs.Completion.WaitAsync(Guard); await UntilAsync(() => clock.Active == 0);
            Require(Json(result).ToJsonString() == frozen && ReferenceEquals(result, runtime.GetDiagnostics().LastStopResult));
            Require(ReferenceEquals(shared, runtime.DisposeAsync().AsTask()) && ReferenceEquals(result, await runtime.StopAsync(CancellationToken.None)));
            JsonObject report = Common(mode); report["result"] = Json(result); report["stream"] = stream.Report(); report["live"] = Diagnostics(runtime); report["timers"] = clock.Report();
            report["preCancelledStart"] = preCancelledStart;
            report["cancelledWaiters"] = cancelled; report["tokenIdentity"] = tokenIdentity; report["disposeJoiners"] = disposals.Length;
            report["sameTask"] = true; report["sameResult"] = true; report["immutable"] = true;
            report["outputCancelledBeforeDeadline"] = outputCancelledBeforeDeadline; report["outputCancelledAtReturn"] = outputCancelledAtReturn;
            report["pendingAtReturn"] = pendingAtReturn; report["finalState"] = runtime.GetDiagnostics().State.ToString();
            return report;
        }
        finally
        {
            stream.ReleaseAll(); runtime.QueuedOutputs!.Complete(); await runtime.QueuedOutputs.Completion.WaitAsync(Guard);
            foreach (CancellationTokenSource caller in callers) { caller.Dispose(); }
        }
        async Task Cancelled(Task<LogStopResult> wait, CancellationToken token)
        {
            try { await wait; throw new InvalidOperationException("Caller wait was not cancelled."); }
            catch (OperationCanceledException error) { tokenIdentity &= error.CancellationToken == token; Require(tokenIdentity); cancelled++; }
        }
    }
    private static JsonObject Json(LogStopResult result) => new()
    {
        ["completion"] = result.Completion.ToString(),
        ["succeeded"] = result.Succeeded,
        ["loss"] = result.LossObserved,
        ["captured"] = result.BoundaryCapturedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        ["observed"] = result.ObservedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        ["outputs"] = new JsonArray(result.Outputs.Select(o => (JsonNode)new JsonObject
        {
            ["name"] = o.OutputName,
            ["boundary"] = Number(o.AcceptedSequenceBoundary),
            ["written"] = Number(o.Written),
            ["failed"] = Number(o.Failed),
            ["abandoned"] = Number(o.Abandoned),
            ["pending"] = Number(o.Pending),
            ["flush"] = o.WriterFlushStatus.ToString()
        }).ToArray())
    };
}
