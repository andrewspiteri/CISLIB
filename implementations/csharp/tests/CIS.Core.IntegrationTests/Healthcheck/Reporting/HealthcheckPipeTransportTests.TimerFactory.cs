using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan TimerCreationDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunTimerCreationScenario(HealthcheckService? observedService = null, IConditionMonitor? observedMonitor = null, TimerCreationControl? control = null)
    {
        if ((observedService is null) != (observedMonitor is null)) { throw new ArgumentException("Both real external state owners must be supplied together."); }
        var (service, monitor) = observedService is null ? Create() : (observedService, observedMonitor!);
        string pipe = TransportTestSupport.NewPipeName();
        Exception? primary = null;
        var holds = new List<Exception>();
        try
        {
            TimerCreationObservation first = RunFailedTimerStage(service, monitor, pipe, control);
            control?.ObserveFirstObservation?.Invoke(first);
            ReviewTimerCreationStage(first);
            // The same-name replacement does not exist until both ORIGINAL
            // first-server terminal operations have actually published success.
            control?.AfterFirstStageSucceeded?.Invoke();
            TimerCreationObservation replacement = RunReplacementTimerStage(service, monitor, pipe, control);
            control?.ObserveReplacementObservation?.Invoke(replacement);
            ReviewTimerCreationStage(replacement);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { JoinTimerCreation(monitor.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            try { JoinTimerCreation(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            GC.KeepAlive(monitor);
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            throw new AggregateException("Actual external state cleanup remains a timer-creation scenario hold.", holds);
        }
        if (primary is not null) { ExceptionDispatchInfo.Capture(primary).Throw(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TimerCreationObservation RunFailedTimerStage(HealthcheckService service, IConditionMonitor monitor, string pipe, TimerCreationControl? control)
    {
        var facts = new TimerCreationFacts(replacement: false);
        control?.ObserveFirstFacts?.Invoke(facts);
        var originalClock = new FailingTimerClock();
        var clock = new ObservedFailingTimerClock(originalClock, facts, control?.AfterOriginalFactoryThrow);
        var source = new TimerCreationSource(service, facts);
        var callers = new TimerCreationCallers();
        HealthPipeServer? server = null;
        Task? body = null, contract = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 }, clock);
            body = RunFailedTimerBodyAsync(server, service, monitor, source, originalClock, pipe, facts);
            JoinTimerCreation(body);
            contract = RunFirstTimerContractAsync(server, facts);
        }
        catch (Exception error) { primary = error; }
        return ReleaseTimerCreationStage(server, source, clock, originalClock, null, facts, callers, body, contract, primary);
    }

    private static async Task RunFailedTimerBodyAsync(HealthPipeServer server, HealthcheckService service, IConditionMonitor monitor,
        TimerCreationSource source, FailingTimerClock clock, string pipe, TimerCreationFacts facts)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        HealthSnapshot before = service.CaptureSnapshot();
        using var cancellation = new CancellationTokenSource(TransportTestSupport.Limit);
        await using (var peer = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            facts.RecordClient(peer);
            await peer.ConnectAsync(cancellation.Token);
            await clock.Attempted.Task.WaitAsync(TransportTestSupport.Limit);
            try { Assert.AreEqual(0, await peer.ReadAsync(new byte[1], cancellation.Token), "A failed clock cannot manufacture a response."); }
            catch (IOException) { /* The actual endpoint may close with a broken-pipe error. */ }
        }
        Volatile.Write(ref facts.rawClientScopeJoined, 1);
        // Keep the original admission assertion BEFORE any caller's Stop.
        Assert.ThrowsExactly<InvalidOperationException>(() => server.StartAsync());
        Volatile.Write(ref facts.originalAdmissionAssertionReturned, 1);
        Assert.AreEqual(1, clock.Attempts);
        Assert.AreEqual(0, source.Captures);
        HealthSnapshot after = service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.AreEqual(before.HealthStatus, after.HealthStatus);
        Assert.AreEqual(before.Ready, after.Ready);
        Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);
        Volatile.Write(ref facts.originalStateAssertionsReturned, 1);
        GC.KeepAlive(monitor);
        // The actual caller CTS disposal is part of this completed body task.
    }

    private static async Task RunFirstTimerContractAsync(HealthPipeServer server, TimerCreationFacts facts)
    {
        try
        {
            Volatile.Write(ref facts.originalStopInvoked, 1);
            await server.StopAsync();
            Volatile.Write(ref facts.originalStopSucceeded, 1);
            Volatile.Write(ref facts.originalDisposeInvoked, 1);
            await server.DisposeAsync();
            Volatile.Write(ref facts.originalDisposeSucceeded, 1);
        }
        catch (Exception error) { facts.RecordError(error); throw; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TimerCreationObservation RunReplacementTimerStage(HealthcheckService service, IConditionMonitor monitor, string pipe, TimerCreationControl? control)
    {
        var facts = new TimerCreationFacts(replacement: true);
        control?.ObserveReplacementFacts?.Invoke(facts);
        var source = new TimerCreationSource(service, facts);
        var clock = new TimerCreationSystemClock(facts);
        var callers = new TimerCreationCallers();
        HealthPipeServer? server = null;
        Task? body = null, contract = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 }, clock);
            Volatile.Write(ref facts.replacementConstructed, 1);
            body = RunReplacementTimerBodyAsync(server, monitor, pipe, facts, callers);
            JoinTimerCreation(body);
            contract = RunReplacementTimerContractAsync(server, facts);
        }
        catch (Exception error) { primary = error; }
        return ReleaseTimerCreationStage(server, source, clock, null, clock, facts, callers, body, contract, primary);
    }

    private static async Task RunReplacementTimerBodyAsync(HealthPipeServer server, IConditionMonitor monitor, string pipe,
        TimerCreationFacts facts, TimerCreationCallers callers)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        Task<JsonObject> response = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        callers.Requests.Add(response);
        TransportTestSupport.ValidateSnapshotEnvelope(await response, "health", true, true);
        Volatile.Write(ref facts.originalEnvelopeAssertionReturned, 1);
        GC.KeepAlive(monitor);
    }

    private static async Task RunReplacementTimerContractAsync(HealthPipeServer server, TimerCreationFacts facts)
    {
        try
        {
            Volatile.Write(ref facts.originalStopInvoked, 1);
            await server.StopAsync();
            Volatile.Write(ref facts.originalStopSucceeded, 1);
        }
        catch (Exception error) { facts.RecordError(error); throw; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TimerCreationObservation ReleaseTimerCreationStage(HealthPipeServer? server, TimerCreationSource source, TimeProvider clock,
        FailingTimerClock? originalClock, TimerCreationSystemClock? systemClock, TimerCreationFacts facts, TimerCreationCallers callers,
        Task? body, Task? contract, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginTimerCreationStop(server);
        Task? disposing = server is null ? null : BeginTimerCreationDispose(server);
        var status = new TimerCreationStatus();
        var observers = new List<WeakReference<Task>>();
        if (contract is not null) { observers.Add(ObserveTimerCreationTask(contract, status, 0)); }
        if (stopping is not null) { observers.Add(ObserveTimerCreationTask(stopping, status, 1)); }
        if (disposing is not null) { observers.Add(ObserveTimerCreationTask(disposing, status, 2)); }
        var holds = new List<Exception>();
        foreach (Task? actual in new Task?[] { body }.Concat(callers.Requests))
        {
            if (actual is null) { continue; }
            try
            {
                JoinTimerCreation(actual);
                if (ReferenceEquals(actual, body)) { Volatile.Write(ref facts.bodyJoined, 1); }
                else { Interlocked.Increment(ref facts.clientTasksJoined); }
            }
            catch (Exception error) when (ReferenceEquals(actual, body) && ReferenceEquals(error, primary))
            {
                // Same completed body and same already-observed primary only.
                Volatile.Write(ref facts.bodyJoined, 1);
            }
            catch (Exception error) { holds.Add(error); }
        }
        if (systemClock is not null)
        {
            try { systemClock.JoinActualDisposals(); }
            catch (Exception error) { holds.Add(error); }
        }
        // Unknown source/factory/callback/contract causes survive even when the
        // original body has already produced its own assertion failure.
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && body is not null && contract is not null
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            try { RequireTimerCreationPhase(facts); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // Service/monitor are explicit external owners reused by the next
            // original stage. Only Source points to Service; it does not own the
            // Server, failing clock, peer, queued RunSlot or their terminal tasks.
            WeakReference[] graph = [new(server!), new(source), new(clock), new(callers), .. facts.WeakClients(),
                .. originalClock is null ? [] : new WeakReference[] { new(originalClock) },
                .. systemClock?.WeakTimerOwners() ?? []];
            return new(null, graph, facts, status, [.. observers]);
        }
        if (server is not null)
        {
            Task[] actual = contract is null ? [stopping!, disposing!] : [stopping!, disposing!, contract];
            Task all = Task.WhenAll(actual);
            try { JoinTimerCreation(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> reference in observers)
            {
                try { if (reference.TryGetTarget(out Task? observer)) { JoinTimerCreation(observer); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        holds.AddRange(facts.Errors());
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("Actual timer-creation/replacement body, producer or cleanup remains a runtime hold.",
                holds.SelectMany(error => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [error]).Distinct<Exception>(ReferenceEqualityComparer.Instance));
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, status, [.. observers]);
    }

    private static void ReviewTimerCreationStage(TimerCreationObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            Volatile.Write(ref observed.Facts.ownerReviewStarted, 1);
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < TimerCreationDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < TimerCreationDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive)) { throw new InvalidOperationException("Actual timer-factory/peer/source/worker ownership remains live; elapsed time cannot prove settlement."); }
            Volatile.Write(ref observed.Facts.actualOwnersDead, 1);
            RequireTimerCreationPhase(observed.Facts);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { JoinTimerCreation(actual); }
            }
            Exception[] errors = observed.Facts.Errors().Concat(observed.Status!.Errors()).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (errors.Length > 0) { throw new AggregateException("Actual terminal or resource error cannot become a timer-factory assertion.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.AreEqual(1, Volatile.Read(ref observed.Facts.originalStopInvoked), "The original Stop must actually be called.");
            Assert.AreEqual(1, Volatile.Read(ref observed.Facts.originalStopSucceeded), "The original Stop must publish success after its actual factory/client/worker work settles.");
            if (!observed.Facts.Replacement)
            {
                Assert.AreEqual(1, Volatile.Read(ref observed.Facts.originalDisposeInvoked), "Original Dispose is reached only after actual first Stop succeeds.");
                Assert.AreEqual(1, Volatile.Read(ref observed.Facts.originalDisposeSucceeded), "The original first Dispose must actually publish success before the same-name replacement exists.");
            }
            Assert.IsTrue(observed.Status!.ContractCompleted, "All actually reached original terminal contract operations must return.");
            Assert.IsTrue(observed.Status.StopCompleted, "Actual independently invoked cleanup Stop must publish success.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual independently invoked cleanup Dispose must publish success.");
            Volatile.Write(ref observed.Facts.stageReviewSucceeded, 1);
        }
    }

    private static void RequireTimerCreationPhase(TimerCreationFacts facts)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("Actual unknown timer-factory/source/callback/contract errors remain unconditional holds.", errors); }
        bool phase = facts.Replacement
            ? Volatile.Read(ref facts.replacementConstructed) == 1 && Volatile.Read(ref facts.captures) == 1 && Volatile.Read(ref facts.returns) == 1
                && Volatile.Read(ref facts.originalEnvelopeAssertionReturned) == 1 && Volatile.Read(ref facts.clientTasksJoined) == 1
                && Volatile.Read(ref facts.createdTimers) == 1 && Volatile.Read(ref facts.actualDrainsJoined) == 1
                && Volatile.Read(ref facts.callbacksEntered) == Volatile.Read(ref facts.callbacksReturned)
            : Volatile.Read(ref facts.captures) == 0 && Volatile.Read(ref facts.returns) == 0 && Volatile.Read(ref facts.rawClientScopeJoined) == 1
                && facts.WeakClients().Length == 1 && Volatile.Read(ref facts.originalAdmissionAssertionReturned) == 1
                && Volatile.Read(ref facts.originalStateAssertionsReturned) == 1 && Volatile.Read(ref facts.factoryEntries) == 1
                && Volatile.Read(ref facts.factoryReturns) == 1 && Volatile.Read(ref facts.originalThrowMatches) == 1
                && Volatile.Read(ref facts.deliveredOriginalMatches) == 1 && Volatile.Read(ref facts.innerAttemptsObserved) == 1
                && facts.OriginalFactoryFailure is not null && Volatile.Read(ref facts.createdTimers) == 0;
        if (!phase || Volatile.Read(ref facts.bodyJoined) != 1)
        { throw new InvalidOperationException("Every actual original factory/peer/admission/state or replacement source/IO/System drain phase must return before owner proof."); }
    }

    private static Task BeginTimerCreationStop(HealthPipeServer server) { try { return server.StopAsync(); } catch (Exception error) { return Task.FromException(error); } }
    private static Task BeginTimerCreationDispose(HealthPipeServer server) { try { return server.DisposeAsync().AsTask(); } catch (Exception error) { return Task.FromException(error); } }
    private static void JoinTimerCreation(Task actual) => actual.WaitAsync(TimerCreationDeadline).GetAwaiter().GetResult();
    private static WeakReference<Task> ObserveTimerCreationTask(Task actual, TimerCreationStatus status, int role)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var value = ((TimerCreationStatus Status, int Role))state!;
            if (completed.Exception is { } error) { value.Status.RecordError(error); }
            else if (completed.IsCanceled) { value.Status.RecordError(new TaskCanceledException("An actual uncanceled timer-creation terminal operation canceled.")); }
            else { value.Status.RecordSuccess(value.Role); }
        }, (status, role), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record TimerCreationObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph, TimerCreationFacts Facts, TimerCreationStatus? Status, WeakReference<Task>[] Observers);
    private sealed class TimerCreationCallers { internal List<Task<JsonObject>> Requests { get; } = []; }
    private sealed class TimerCreationControl
    {
        internal Action? AfterOriginalFactoryThrow { get; init; }
        internal Action? AfterFirstStageSucceeded { get; init; }
        internal Action<object>? ObserveFirstFacts { get; init; }
        internal Action<object>? ObserveReplacementFacts { get; init; }
        internal Action<object>? ObserveFirstObservation { get; init; }
        internal Action<object>? ObserveReplacementObservation { get; init; }
    }
    private sealed class TimerCreationStatus
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private bool contract, stop, dispose;
        internal bool ContractCompleted { get { lock (gate) { return contract; } } }
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal void RecordSuccess(int role) { lock (gate) { if (role == 0) { contract = true; } else if (role == 1) { stop = true; } else { dispose = true; } } }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class TimerCreationFacts(bool replacement)
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private readonly List<WeakReference> clients = [];
        private Exception? originalFactoryFailure;
        internal bool Replacement { get; } = replacement;
        internal int captures, returns, rawClientScopeJoined, originalAdmissionAssertionReturned, originalStateAssertionsReturned;
        internal int factoryEntries, factoryReturns, originalThrowMatches, deliveredOriginalMatches, innerAttemptsObserved;
        internal int replacementConstructed, originalEnvelopeAssertionReturned, createdTimers, actualDrainsJoined, callbacksEntered, callbacksReturned;
        internal int bodyJoined, clientTasksJoined, originalStopInvoked, originalStopSucceeded, originalDisposeInvoked, originalDisposeSucceeded;
        internal int ownerReviewStarted, actualOwnersDead, stageReviewSucceeded;
        internal Exception? OriginalFactoryFailure { get { lock (gate) { return originalFactoryFailure; } } }
        internal void RecordOriginalThrow(Exception actual, int attempts)
        {
            // Source-bound provenance inference: the unchanged inner method has
            // one exact literal throw. This is not an independent creation hook.
            MethodInfo literal = typeof(FailingTimerClock).GetMethod(nameof(FailingTimerClock.CreateTimer))!;
            bool exact = actual.GetType() == typeof(InvalidOperationException) && actual.Message == "actual-test-clock-timer-failure"
                && actual.TargetSite == literal && attempts == 1 && actual.InnerException is null && actual.Data.Count == 0;
            lock (gate)
            {
                if (!exact || originalFactoryFailure is not null) { errors.Add(actual); return; }
                originalFactoryFailure = actual;
                Interlocked.Increment(ref originalThrowMatches);
                Volatile.Write(ref innerAttemptsObserved, attempts);
            }
        }
        internal void RecordDeliveredThrow(Exception actual)
        {
            lock (gate)
            {
                if (originalFactoryFailure is not null && ReferenceEquals(actual, originalFactoryFailure)) { Interlocked.Increment(ref deliveredOriginalMatches); }
                else { errors.Add(actual); }
            }
        }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        internal void RecordClient(NamedPipeClientStream client) { lock (gate) { clients.Add(new(client)); } }
        internal WeakReference[] WeakClients() { lock (gate) { return [.. clients]; } }
    }
    private sealed class TimerCreationSource(HealthcheckService service, TimerCreationFacts facts) : IHealthSnapshotSource
    {
        internal int Captures => Volatile.Read(ref facts.captures);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref facts.captures);
            try { return service.CaptureSnapshot(); }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private sealed class ObservedFailingTimerClock(FailingTimerClock original, TimerCreationFacts facts, Action? afterOriginalThrow) : TimeProvider
    {
        public override long TimestampFrequency => original.TimestampFrequency;
        public override long GetTimestamp() => original.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => original.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref facts.factoryEntries);
            try
            {
                try { return original.CreateTimer(callback, state, dueTime, period); }
                catch (Exception actual)
                {
                    facts.RecordOriginalThrow(actual, original.Attempts);
                    afterOriginalThrow?.Invoke();
                    throw;
                }
            }
            catch (Exception actual) { facts.RecordDeliveredThrow(actual); throw; }
            finally { Interlocked.Increment(ref facts.factoryReturns); }
        }
    }
}
