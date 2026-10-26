using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan TotalBudgetDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TotalBudgetObservation RunTotalBudgetScenario(HealthcheckService? observedService = null, IConditionMonitor? observedMonitor = null)
    {
        if ((observedService is null) != (observedMonitor is null)) { throw new ArgumentException("Both actual control owners must be supplied together."); }
        var (service, monitor) = observedService is null ? Create() : (observedService, observedMonitor!);
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new TotalBudgetFacts();
        var clock = new TotalBudgetClock(facts);
        var source = new TotalBudgetSource(service, clock, facts);
        var callers = new TotalBudgetCallers();
        HealthPipeServer? server = null;
        Task? body = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1, PipeRequestTimeoutMs = 1000 }, clock);
            body = RunTotalBudgetBodyAsync(server, pipe, source, clock, monitor, callers, facts);
            JoinTotalBudget(body);
        }
        catch (Exception error) { primary = error; }
        return ReleaseTotalBudgetOwners(server, service, monitor, source, clock, facts, body, callers, primary);
    }

    private static async Task RunTotalBudgetBodyAsync(HealthPipeServer server, string pipe, TotalBudgetSource source,
        TotalBudgetClock clock, IConditionMonitor monitor, TotalBudgetCallers callers, TotalBudgetFacts facts)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
        await using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            facts.RecordActualClient(client);
            await client.ConnectAsync(timeout.Token);
            await clock.TimerCreated.Task.WaitAsync(TransportTestSupport.Limit);
            // 600ms belongs to request reception; the source consumes another 600ms.
            // The timer deliberately cannot fire, requiring an elapsed-time deadline check.
            clock.Advance(600);
            Volatile.Write(ref facts.requestAdvanceBeforeWrite, clock.GetTimestamp());
            await TransportTestSupport.WriteFrameAsync(client, Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), cancellation: timeout.Token);
            try
            {
                await TransportTestSupport.ReadFrameAsync(client, timeout.Token);
                Volatile.Write(ref facts.firstCompleteReplyReturned, 1);
                Assert.Fail("An expired total budget cannot commit any late complete reply.");
            }
            catch (IOException) { Interlocked.Increment(ref facts.firstReadIoExceptions); }
        }
        Volatile.Write(ref facts.rawScopeReturned, 1);
        Assert.AreEqual(1, source.Captures);
        Volatile.Write(ref facts.beforeRecoveryCaptures, source.Captures);
        Task<JsonObject> recovery = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        callers.Requests.Add(recovery);
        TransportTestSupport.ValidateSnapshotEnvelope(await recovery, "health", true, true);
        Assert.AreEqual(2, source.Captures);
        GC.KeepAlive(monitor);
    }

    private sealed class TotalBudgetCallers
    {
        internal List<Task<JsonObject>> Requests { get; } = [];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TotalBudgetObservation ReleaseTotalBudgetOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, TotalBudgetSource source, TotalBudgetClock clock,
        TotalBudgetFacts facts, Task? body, TotalBudgetCallers callers, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginTotalBudgetStop(server);
        Task? disposing = server is null ? null : BeginTotalBudgetDispose(server);
        var status = new TotalBudgetStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveTotalBudgetTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveTotalBudgetTerminal(disposing, status, true)); }
        var holds = new List<Exception>();
        try
        {
            // These are the original real helper tasks, including their frame IO,
            // await-using client disposal and caller CTS disposal. No IO catch is added.
            foreach (Task? actual in new Task?[] { body }.Concat(callers.Requests))
            {
                if (actual is not null)
                {
                    try
                    {
                        JoinTotalBudget(actual);
                        if (ReferenceEquals(actual, body)) { Volatile.Write(ref facts.bodyJoined, 1); Volatile.Write(ref facts.originalBodySucceeded, actual.IsCompletedSuccessfully ? 1 : 0); }
                        else { Interlocked.Increment(ref facts.clientTasksJoined); }
                    }
                    catch (Exception error) when (ReferenceEquals(actual, body) && ReferenceEquals(error, primary))
                    {
                        // The same actual body task already completed, including its
                        // original await-using disposals, with this exact saved error.
                        // Preserve it as the body failure, not a second cleanup fault.
                        Volatile.Write(ref facts.bodyJoined, 1);
                    }
                    catch (Exception error) { holds.Add(error); }
                }
            }
            try { clock.RequireActualStubDisposals(); }
            catch (Exception error) { holds.Add(error); }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinTotalBudget(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            try { JoinTotalBudget(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            GC.KeepAlive(monitor);
        }
        // Keep actual swallowed source/callback causes visible even when the
        // original response assertion also failed. They remain resource holds.
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && body is not null && callers.Requests.Count == 1
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            try { RequireTotalBudgetPhase(facts); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // Exact remaining-decrement leaves only: the real BeginStop still
            // cancels admission and disposes every published stream. Every actual
            // accepting/handling RunSlot still owns Server until IO and its finally
            // return. The one original worker belong to this whole weak
            // graph; the original one-slot population remains unchanged.
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(clock), new(callers), .. facts.WeakClients(), .. clock.WeakTimerOwners()];
            return new(null, graph, facts, status, [.. observers]);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinTotalBudget(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinTotalBudget(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original request-plus-synchronous-snapshot/recovery body or its actual resource cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, []);
    }

    private static void ReviewTotalBudgetScenario(TotalBudgetObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < TotalBudgetDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < TotalBudgetDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual accepting/request/source/manual-clock/stub-timer ownership remains live; elapsed time cannot certify settlement.");
            }
            RequireTotalBudgetPhase(observed.Facts);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { JoinTotalBudget(actual); }
            }
            Exception[] errors = observed.Status!.Errors();
            if (errors.Length > 0) { throw new AggregateException("An actual uncanceled terminal operation failed.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual terminal Stop must publish success after the real request-plus-synchronous-snapshot/recovery exchanges, manual timers and all queued workers settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must publish success after the real request-plus-synchronous-snapshot/recovery exchanges, manual timers and all queued workers settle.");
        }
    }

    private static void RequireTotalBudgetPhase(TotalBudgetFacts facts)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("An actual unknown producer or manual-clock/stub failure remains a resource hold, even when the original expired-body assertions passed.", errors); }
        if (Volatile.Read(ref facts.captures) != 2 || Volatile.Read(ref facts.returns) != 2
            || Volatile.Read(ref facts.beforeRecoveryCaptures) != 1 || Volatile.Read(ref facts.rawScopeReturned) != 1
            || Volatile.Read(ref facts.firstReadIoExceptions) != 1 || Volatile.Read(ref facts.firstCompleteReplyReturned) != 0
            || Volatile.Read(ref facts.createdTimers) != 2 || Volatile.Read(ref facts.actualStubDisposeReturns) != 2
            || Volatile.Read(ref facts.stubAsyncDisposeReturns) != 0 || Volatile.Read(ref facts.advanceCalls) != 2
            || Volatile.Read(ref facts.finalTimestamp) != 1200 || Volatile.Read(ref facts.requestAdvanceBeforeWrite) != 600
            || Volatile.Read(ref facts.firstCaptureEntryTimestamp) != 600 || Volatile.Read(ref facts.recoveryCaptureEntryTimestamp) != 1200
            || Volatile.Read(ref facts.clientTasksJoined) != 1 || Volatile.Read(ref facts.bodyJoined) != 1
            || Volatile.Read(ref facts.originalBodySucceeded) != 1 || facts.WeakClients().Length != 1
            || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1)
        {
            throw new InvalidOperationException("The original TimerCreated/request600-before-write/source600-before-capture order, expired raw scope and healthy recovery, two actual source/manual timer returns and state disposals must all settle; unknown phases remain holds.");
        }
    }
    private static void JoinTotalBudget(Task actual) => actual.WaitAsync(TotalBudgetDeadline).GetAwaiter().GetResult();
    private static T JoinTotalBudget<T>(Task<T> actual) => actual.WaitAsync(TotalBudgetDeadline).GetAwaiter().GetResult();
    private static Task BeginTotalBudgetStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static Task BeginTotalBudgetDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveTotalBudgetTerminal(Task actual, TotalBudgetStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((TotalBudgetStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record TotalBudgetObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        TotalBudgetFacts Facts, TotalBudgetStatus? Status, WeakReference<Task>[] Observers);
    private sealed class TotalBudgetStatus
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private bool stop, dispose;
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal void RecordSuccess(bool disposal) { lock (gate) { if (disposal) { dispose = true; } else { stop = true; } } }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class TotalBudgetFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private readonly List<WeakReference> clients = [];
        internal int captures, returns, createdTimers, actualStubDisposeReturns, stubAsyncDisposeReturns, advanceCalls;
        internal long finalTimestamp, requestAdvanceBeforeWrite = -1, firstCaptureEntryTimestamp = -1, recoveryCaptureEntryTimestamp = -1;
        internal int firstReadIoExceptions, firstCompleteReplyReturned, rawScopeReturned, beforeRecoveryCaptures = -1;
        internal int clientTasksJoined, bodyJoined, originalBodySucceeded, monitorJoined, serviceJoined;
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        internal void RecordActualClient(NamedPipeClientStream actual) { lock (gate) { clients.Add(new(actual)); } }
        internal WeakReference[] WeakClients() { lock (gate) { return [.. clients]; } }
    }
    private sealed class TotalBudgetSource(HealthcheckService service, TotalBudgetClock clock, TotalBudgetFacts facts) : IHealthSnapshotSource
    {
        internal int Captures => Volatile.Read(ref facts.captures);
        public HealthSnapshot CaptureSnapshot()
        {
            int ordinal = Interlocked.Increment(ref facts.captures);
            try
            {
                if (ordinal == 1)
                {
                    Volatile.Write(ref facts.firstCaptureEntryTimestamp, clock.GetTimestamp());
                    clock.Advance(600);
                }
                else if (ordinal == 2) { Volatile.Write(ref facts.recoveryCaptureEntryTimestamp, clock.GetTimestamp()); }
                return service.CaptureSnapshot();
            }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private sealed class TotalBudgetClock(TotalBudgetFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<TotalBudgetTimer> timers = [];
        private long timestamp;
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        internal void Advance(long milliseconds)
        {
            long actual = Interlocked.Add(ref timestamp, milliseconds);
            Interlocked.Increment(ref facts.advanceCalls);
            Volatile.Write(ref facts.finalTimestamp, actual);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // Original DelayedCallbackClock deliberately ignores callback/state.
            // This is an inert manual stub, never a System timer or callback drain.
            var timer = new TotalBudgetTimer(facts);
            lock (gate) { timers.Add(timer); }
            Interlocked.Increment(ref facts.createdTimers);
            TimerCreated.TrySetResult();
            return timer;
        }
        internal void RequireActualStubDisposals()
        {
            TotalBudgetTimer[] actual; lock (gate) { actual = [.. timers]; }
            foreach (TotalBudgetTimer timer in actual)
            {
                // Client disposal can complete before the server continuation
                // reaches budget.Dispose. Observe that actual manual API call;
                // this is not a System callback or resource drain.
                JoinTotalBudget(timer.ActualDisposeObserved.Task);
                if (Volatile.Read(ref timer.disposeCalls) != 1 || Volatile.Read(ref timer.disposeReturns) != 1
                    || Volatile.Read(ref timer.asyncDisposeCalls) != 0 || Volatile.Read(ref timer.asyncDisposeReturns) != 0)
                {
                    throw new InvalidOperationException("The actual manual stub's original synchronous Dispose API must have returned once; no System or asynchronous callback drain can be inferred.");
                }
            }
            lock (gate) { if (timers.Count != actual.Length) { throw new InvalidOperationException("Manual timer publication raced its actual disposal inventory."); } }
        }
        internal WeakReference[] WeakTimerOwners() { lock (gate) { return timers.Select(timer => new WeakReference(timer)).ToArray(); } }
        private sealed class TotalBudgetTimer(TotalBudgetFacts facts) : ITimer
        {
            internal TaskCompletionSource ActualDisposeObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int disposeCalls, disposeReturns, asyncDisposeCalls, asyncDisposeReturns;
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose()
            {
                Interlocked.Increment(ref disposeCalls);
                // Original Dispose has no resource/callback action.
                Interlocked.Increment(ref facts.actualStubDisposeReturns);
                Interlocked.Increment(ref disposeReturns);
                ActualDisposeObserved.TrySetResult();
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref asyncDisposeCalls);
                // Preserve original completed no-op; do not call Dispose here.
                Interlocked.Increment(ref facts.stubAsyncDisposeReturns);
                Interlocked.Increment(ref asyncDisposeReturns);
                return ValueTask.CompletedTask;
            }
        }
    }
}
