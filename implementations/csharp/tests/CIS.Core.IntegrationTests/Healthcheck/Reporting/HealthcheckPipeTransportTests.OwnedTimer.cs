using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan OwnedTimerDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OwnedTimerObservation RunOwnedTimerScenario(HealthcheckService? observedService = null, IConditionMonitor? observedMonitor = null, OwnedTimerControl? control = null)
    {
        if ((observedService is null) != (observedMonitor is null)) { throw new ArgumentException("Both actual control owners must be supplied together."); }
        var (service, monitor) = observedService is null ? Create() : (observedService, observedMonitor!);
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new OwnedTimerFacts();
        var source = new OwnedTimerSource(service, facts);
        var clock = new OwnedTimerClock(facts, control?.BeforeActualTimerDispose);
        var callers = new OwnedTimerCallers();
        Task? body = null, contract = null;
        HealthPipeServer? server = null;
        Exception? primary = null;
        try
        {
            control?.ObserveFacts?.Invoke(facts);
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 }, clock);
            body = RunOwnedTimerBodyAsync(server, pipe, monitor, callers);
            JoinOwnedTimer(body);
            contract = RunOwnedTimerContractAsync(server, clock, facts, control);
        }
        catch (Exception error) { primary = error; }
        return ReleaseOwnedTimerOwners(server, service, monitor, source, clock, facts, body, callers, contract, primary);
    }

    private static async Task RunOwnedTimerBodyAsync(HealthPipeServer server, string pipe, IConditionMonitor monitor, OwnedTimerCallers callers)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        Task<JsonObject> response = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        callers.Requests.Add(response);
        TransportTestSupport.ValidateSnapshotEnvelope(await response, "health", true, true);
        GC.KeepAlive(monitor);
    }

    private static async Task RunOwnedTimerContractAsync(HealthPipeServer server, OwnedTimerClock clock, OwnedTimerFacts facts, OwnedTimerControl? control)
    {
        try
        {
            control?.BeforeFirstStop?.Invoke();
            Volatile.Write(ref facts.firstStopInvoked, 1);
            // Only the deadline supervisor moved outside this real async task.
            // Await the actual original uncanceled Stop promise, never a fake signal.
            await server.StopAsync();
            Volatile.Write(ref facts.firstStopSucceeded, 1);
            int created = clock.CreatedTimers;
            Volatile.Write(ref facts.createdObservedAfterOriginalStop, created);
            try { Assert.AreEqual(1, created); }
            catch (AssertFailedException error) { facts.RegisterOriginalTerminalAssertion(error, "created"); throw; }
            int disposed = clock.DisposedTimers;
            Volatile.Write(ref facts.disposedObservedAfterOriginalStop, disposed);
            try { Assert.AreEqual(control?.ExpectedDisposedTimers ?? 1, disposed, "A settled request must release its actual TimeProvider timer before stopping completes."); }
            catch (AssertFailedException error) { facts.RegisterOriginalTerminalAssertion(error, "disposed"); throw; }
            Volatile.Write(ref facts.originalTimerAssertionsReturned, 1);
        }
        catch (Exception error) { facts.RecordContractError(error); throw; }
    }

    private sealed class OwnedTimerCallers
    {
        internal List<Task<JsonObject>> Requests { get; } = [];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OwnedTimerObservation ReleaseOwnedTimerOwners(HealthPipeServer? server, HealthcheckService service, IConditionMonitor monitor,
        OwnedTimerSource source, OwnedTimerClock clock, OwnedTimerFacts facts, Task? body, OwnedTimerCallers callers, Task? contract, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginOwnedTimerStop(server);
        Task? disposing = server is null ? null : BeginOwnedTimerDispose(server);
        var status = new OwnedTimerStatus();
        var observers = new List<WeakReference<Task>>();
        if (contract is not null) { observers.Add(ObserveOwnedTimerContract(contract, facts, status)); }
        if (stopping is not null) { observers.Add(ObserveOwnedTimerTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveOwnedTimerTerminal(disposing, status, true)); }
        var holds = new List<Exception>();
        foreach (Task? actual in new Task?[] { body }.Concat(callers.Requests))
        {
            if (actual is not null)
            {
                try
                {
                    JoinOwnedTimer(actual);
                    if (ReferenceEquals(actual, body)) { Volatile.Write(ref facts.bodyJoined, 1); }
                    else { Interlocked.Increment(ref facts.clientTasksJoined); }
                }
                catch (Exception error) when (ReferenceEquals(actual, body) && ReferenceEquals(error, primary))
                {
                    // Only duplicate observation of the same completed original body.
                    Volatile.Write(ref facts.bodyJoined, 1);
                }
                catch (Exception error) { holds.Add(error); }
            }
        }
        try { clock.JoinStartedActualDisposals(); }
        catch (Exception error) { holds.Add(error); }
        try { JoinOwnedTimer(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
        catch (Exception error) { holds.Add(error); }
        try { JoinOwnedTimer(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
        catch (Exception error) { holds.Add(error); }
        GC.KeepAlive(monitor);
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && body is not null && contract is not null && callers.Requests.Count == 1
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            try { RequireOwnedTimerPhase(facts); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // The original manual timer is callback-free and owns no native timer.
            // Real client/source/state work has joined. The actual RunSlot owns
            // Server until IO, budget Dispose and its finally truly return.
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(clock), new(callers), .. clock.WeakTimerOwners()];
            return new(null, graph, facts, status, [.. observers]);
        }
        if (server is not null)
        {
            Task[] actual = contract is null ? [stopping!, disposing!] : [stopping!, disposing!, contract];
            Task all = Task.WhenAll(actual);
            try { JoinOwnedTimer(all); }
            catch (Exception error)
            {
                foreach (Exception failure in all.Exception?.Flatten().InnerExceptions ?? [error])
                {
                    if (!facts.IsOriginalTerminalAssertion(failure)) { holds.Add(failure); }
                }
            }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actualObserver)) { JoinOwnedTimer(actualObserver); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        holds.AddRange(facts.Errors());
        if (facts.OriginalTerminalAssertion is { } original) { holds.Add(original.SourceException); }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original owned-timer body, terminal contract or real cleanup did not settle.", holds.Distinct<Exception>(ReferenceEqualityComparer.Instance));
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, []);
    }

    private static void ReviewOwnedTimerScenario(OwnedTimerObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < OwnedTimerDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < OwnedTimerDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive)) { throw new InvalidOperationException("Actual owned-timer/client/source/worker ownership remains live; elapsed time cannot certify settlement."); }
            RequireOwnedTimerPhase(observed.Facts);
            foreach (WeakReference<Task> observer in observed.Observers)
            {
                if (observer.TryGetTarget(out Task? actual)) { JoinOwnedTimer(actual); }
            }
            Exception[] unknown = observed.Facts.Errors().Concat(observed.Status!.Errors()).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (unknown.Length > 0)
            {
                if (observed.Facts.OriginalTerminalAssertion is { } original) { unknown = [.. unknown, original.SourceException]; }
                throw new AggregateException("An actual owned-timer contract/resource operation failed.", unknown);
            }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            // Owner death cannot complete an operation. Test the FIRST original
            // awaited Stop before claiming its later timer assertions executed.
            Assert.AreEqual(1, Volatile.Read(ref observed.Facts.firstStopInvoked), "The actual original first Stop must be invoked.");
            Assert.AreEqual(1, Volatile.Read(ref observed.Facts.firstStopSucceeded), "The actual original first Stop must publish success after its real request, manual timer and worker settle.");
            if (observed.Facts.OriginalTerminalAssertion is { } original)
            {
                if (!observed.Status!.StopCompleted || !observed.Status.DisposeCompleted)
                {
                    throw new InvalidOperationException("The original terminal assertion cannot be released while actual cleanup operations remain unfinished.");
                }
                original.Throw();
            }
            Assert.IsTrue(observed.Status!.ContractCompleted, "The actual original post-Stop timer assertions must return successfully.");
            Assert.AreEqual(1, Volatile.Read(ref observed.Facts.originalTimerAssertionsReturned), "Both original timer assertions must actually execute.");
            Assert.IsTrue(observed.Status.StopCompleted, "Actual independently initiated cleanup Stop must publish success.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual independently initiated cleanup Dispose must publish success.");
        }
    }

    private static void RequireOwnedTimerPhase(OwnedTimerFacts facts)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("Actual unknown source/contract/manual API failures remain resource holds.", errors); }
        if (Volatile.Read(ref facts.captures) != 1 || Volatile.Read(ref facts.returns) != 1 || Volatile.Read(ref facts.actualTimerObjects) != 1
            || Volatile.Read(ref facts.manualCallsEntered) != Volatile.Read(ref facts.manualCallsReturned)
            || Volatile.Read(ref facts.bodyJoined) != 1 || Volatile.Read(ref facts.clientTasksJoined) != 1
            || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1)
        {
            throw new InvalidOperationException("The real original request/source, every entered original manual API and real state disposal must settle; unknown phases remain holds.");
        }
        // Do not assert DisposedTimers==1 here: that is the literal original
        // post-Stop contract assertion, not a substitute resource-completion flag.
    }

    private static void JoinOwnedTimer(Task actual) => actual.WaitAsync(OwnedTimerDeadline).GetAwaiter().GetResult();
    private static Task BeginOwnedTimerStop(HealthPipeServer server) { try { return server.StopAsync(); } catch (Exception error) { return Task.FromException(error); } }
    private static Task BeginOwnedTimerDispose(HealthPipeServer server) { try { return server.DisposeAsync().AsTask(); } catch (Exception error) { return Task.FromException(error); } }
    private static WeakReference<Task> ObserveOwnedTimerContract(Task actual, OwnedTimerFacts facts, OwnedTimerStatus status)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((OwnedTimerFacts Facts, OwnedTimerStatus Status))state!;
            if (completed.Exception is { } error) { foreach (Exception cause in error.Flatten().InnerExceptions) { observed.Facts.RecordContractError(cause); } }
            else if (completed.IsCanceled) { observed.Facts.RecordError(new TaskCanceledException("The actual uncanceled original contract task canceled.")); }
            else { observed.Status.RecordContractSuccess(); }
        }, (facts, status), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private static WeakReference<Task> ObserveOwnedTimerTerminal(Task actual, OwnedTimerStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((OwnedTimerStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("An actual uncanceled cleanup operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record OwnedTimerObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph, OwnedTimerFacts Facts, OwnedTimerStatus? Status, WeakReference<Task>[] Observers);
    private sealed class OwnedTimerStatus
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private bool stop, dispose, contract;
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal bool ContractCompleted { get { lock (gate) { return contract; } } }
        internal void RecordSuccess(bool disposal) { lock (gate) { if (disposal) { dispose = true; } else { stop = true; } } }
        internal void RecordContractSuccess() { lock (gate) { contract = true; } }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class OwnedTimerFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private ExceptionDispatchInfo? originalTerminalAssertion;
        private string? originalSite;
        internal int captures, returns, actualTimerObjects, manualCallsEntered, manualCallsReturned;
        internal int firstStopInvoked, firstStopSucceeded, originalTimerAssertionsReturned, bodyJoined, clientTasksJoined, monitorJoined, serviceJoined;
        internal int createdObservedAfterOriginalStop = -1, disposedObservedAfterOriginalStop = -1;
        internal ExceptionDispatchInfo? OriginalTerminalAssertion { get { lock (gate) { return originalTerminalAssertion; } } }
        internal void RecordError(Exception error) { lock (gate) { if (!errors.Any(actual => ReferenceEquals(actual, error))) { errors.Add(error); } } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        internal void RegisterOriginalTerminalAssertion(AssertFailedException error, string site)
        {
            lock (gate)
            {
                if (Volatile.Read(ref firstStopSucceeded) != 1 || site is not ("created" or "disposed") || originalTerminalAssertion is not null)
                {
                    errors.Add(new InvalidOperationException("An unproven terminal assertion cannot be registered as an original leaf.", error)); return;
                }
                originalTerminalAssertion = ExceptionDispatchInfo.Capture(error); originalSite = site;
            }
        }
        internal bool IsOriginalTerminalAssertion(Exception error)
        {
            lock (gate) { return Volatile.Read(ref firstStopSucceeded) == 1 && originalSite is "created" or "disposed" && ReferenceEquals(originalTerminalAssertion?.SourceException, error); }
        }
        internal void RecordContractError(Exception error) { if (!IsOriginalTerminalAssertion(error)) { RecordError(error); } }
    }
    private sealed class OwnedTimerControl
    {
        internal Action? BeforeActualTimerDispose { get; init; }
        internal Action? BeforeFirstStop { get; init; }
        internal Action<object>? ObserveFacts { get; init; }
        internal int ExpectedDisposedTimers { get; init; } = 1;
    }
    private sealed class OwnedTimerSource(HealthcheckService service, OwnedTimerFacts facts) : IHealthSnapshotSource
    {
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref facts.captures);
            try { return service.CaptureSnapshot(); }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private sealed class OwnedTimerClock(OwnedTimerFacts facts, Action? beforeActualDispose) : TimeProvider
    {
        private readonly object gate = new();
        private readonly OwnedTimerActualClock actual = new();
        private readonly List<ObservedManualTimer> timers = [];
        internal int CreatedTimers => actual.CreatedTimers;
        internal int DisposedTimers => actual.DisposedTimers;
        public override long TimestampFrequency => actual.TimestampFrequency;
        public override long GetTimestamp() => actual.GetTimestamp();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                ITimer original = actual.CreateTimer(callback, state, dueTime, period);
                var observed = new ObservedManualTimer(original, facts, beforeActualDispose);
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref facts.actualTimerObjects);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinStartedActualDisposals()
        {
            ObservedManualTimer[] snapshot; lock (gate) { snapshot = [.. timers]; }
            foreach (ObservedManualTimer timer in snapshot) { timer.JoinStartedActualCalls(); }
            lock (gate) { if (timers.Count != snapshot.Length) { throw new InvalidOperationException("Actual manual timer publication raced cleanup."); } }
        }
        internal WeakReference[] WeakTimerOwners()
        {
            lock (gate) { return [new(actual), .. timers.SelectMany(timer => new[] { new WeakReference(timer), new WeakReference(timer.Actual) })]; }
        }
        private sealed class ObservedManualTimer(ITimer actual, OwnedTimerFacts facts, Action? beforeActualDispose) : ITimer
        {
            private readonly object gate = new();
            private readonly List<Task> actualCalls = [];
            internal ITimer Actual => actual;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                try { return actual.Change(dueTime, period); }
                catch (Exception error) { facts.RecordError(error); throw; }
            }
            private TaskCompletionSource EnterActualCall()
            {
                var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (gate) { actualCalls.Add(returned.Task); }
                Interlocked.Increment(ref facts.manualCallsEntered); return returned;
            }
            public void Dispose()
            {
                TaskCompletionSource returned = EnterActualCall();
                try { beforeActualDispose?.Invoke(); actual.Dispose(); returned.TrySetResult(); }
                catch (Exception error) { facts.RecordError(error); returned.TrySetException(error); throw; }
                finally { Interlocked.Increment(ref facts.manualCallsReturned); }
            }
            public async ValueTask DisposeAsync()
            {
                TaskCompletionSource returned = EnterActualCall();
                try { beforeActualDispose?.Invoke(); await actual.DisposeAsync(); returned.TrySetResult(); }
                catch (Exception error) { facts.RecordError(error); returned.TrySetException(error); throw; }
                finally { Interlocked.Increment(ref facts.manualCallsReturned); }
            }
            internal void JoinStartedActualCalls()
            {
                Task[] snapshot; lock (gate) { snapshot = [.. actualCalls]; }
                foreach (Task returned in snapshot) { JoinOwnedTimer(returned); }
            }
        }
    }
}
