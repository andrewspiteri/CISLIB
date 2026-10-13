using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;
namespace CIS.Tests.DiscoveryFixture;

internal static class FileTimerQualificationFixture
{
    internal static readonly string[] Modes = ["stopped-rearm", "faulted-rearm"];
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode).GetAwaiter().GetResult().ToJsonString()); return 0; }
    private static JsonObject Observe(CoreLogRuntime runtime)
    {
        LogOutputDiagnostics output = runtime.GetDiagnostics().Outputs.Single();
        return new JsonObject
        {
            ["state"] = output.State.ToString(),
            ["fault"] = output.LastFaultCode,
            ["accepted"] = Number(output.Accepted),
            ["written"] = Number(output.Written),
            ["failed"] = Number(output.Failed),
            ["abandoned"] = Number(output.Abandoned),
            ["pending"] = Number(output.Pending),
            ["unavailable"] = Number(output.Unavailable)
        };
    }
    internal static async Task<JsonObject> ExecuteAsync(string mode)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal));
        bool fault = mode == "faulted-rearm";
        var clock = new FileTimerCoordinationFixture.Clock();
        using var writer = new Writer(holdFirstWrite: false, failFirstFlush: fault);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.Information, FilePath = "controlled", QueueCapacity = 3, MaxEntryBytes = 16384 },
            null, writer.WriteAsync, clock, fileFlush: writer.FlushAsync);
        OutputEventQueue queue = runtime.QueuedOutputs!.File!; Task worker = queue.Completion, shared = runtime.QueuedOutputs.Completion;
        try
        {
            queue.StartFileFlushTimer(clock, Interval);
            Accepted(runtime.CreateLogger("TimerQualification").Write(CoreLogLevel.Information, Data("one")));
            await UntilAsync(() => queue.CaptureAccounting().Written == 1);
            OutputFlushRegistration actual;
            lock (Field(queue, "admissionGate")!)
            {
                clock.Advance(Interval); actual = Registrations(queue).Single();
                Require(actual.AcceptedSequenceBoundary == 1 && !actual.WriterFlushCompletion.IsCompleted);
            }
            Require(await actual.WriterFlushCompletion.WaitAsync(Guard) == !fault);
            if (!fault) { runtime.CloseAdmission(); }
            try { await shared.WaitAsync(Guard); }
            catch (IOException) when (fault) { }
            Require(worker.IsCompleted && shared.IsCompleted && worker.IsFaulted == fault);
            Task disposal = (Task)Field(queue, "fileFlushTimerDisposal")!;
            Require(disposal.IsCompletedSuccessfully && Field(queue, "fileFlushTimer") is null && clock.Active == 0);
            JsonObject before = Observe(runtime), beforeTimers = clock.Report();
            Require(before["state"]!.GetValue<string>() == (fault ? "Faulted" : "Stopped"));
            var probes = new JsonArray();
            for (int index = 0; index < 64; index++)
            {
                queue.StartFileFlushTimer(clock, Interval);
                Require(clock.Active == 0 && clock.Created == index + 2 && clock.Disposed == clock.Created);
                Require(ReferenceEquals(worker, queue.Completion) && ReferenceEquals(shared, runtime.QueuedOutputs.Completion));
                Require(ReferenceEquals(disposal, Field(queue, "fileFlushTimerDisposal")) && Field(queue, "fileFlushTimer") is null);
                clock.Advance(TimeSpan.FromMinutes(1));
                Require(clock.PeriodicFires == 1 && writer.Writes == 1 && writer.Flushes == 1 && queue.RegisteredFlushCount == 0);
                probes.Add(new JsonObject { ["created"] = clock.Created, ["disposed"] = clock.Disposed, ["active"] = clock.Active, ["fires"] = clock.PeriodicFires, ["controls"] = queue.RegisteredFlushCount });
            }
            JsonObject after = Observe(runtime); Require(before.ToJsonString() == after.ToJsonString());
            LogWriteResult late = runtime.CreateLogger("TimerQualification").Write(CoreLogLevel.Information, Data("late"));
            Require(late.Outcome == (fault ? LogWriteOutcome.Rejected : LogWriteOutcome.Stopped));
            Require(late.Outputs.Single().Status == (fault ? LogOutputWriteStatus.Faulted : LogOutputWriteStatus.Stopped));
            JsonObject report = Common(mode); report["before"] = before; report["after"] = after; report["afterLate"] = Observe(runtime);
            report["beforeTimers"] = beforeTimers; report["timers"] = clock.Report(); report["probes"] = probes;
            report["writer"] = writer.Report(); report["lateOutcome"] = late.Outcome.ToString(); report["lateOutput"] = late.Outputs.Single().Status.ToString();
            report["workerFaulted"] = worker.IsFaulted; report["sharedFaulted"] = shared.IsFaulted;
            report["sameWorker"] = ReferenceEquals(worker, queue.Completion); report["sameSharedCompletion"] = ReferenceEquals(shared, runtime.QueuedOutputs.Completion);
            report["sameDisposal"] = ReferenceEquals(disposal, Field(queue, "fileFlushTimerDisposal")); report["finalControls"] = queue.RegisteredFlushCount;
            return report;
        }
        finally { await Cleanup(runtime, writer); }
    }
}
