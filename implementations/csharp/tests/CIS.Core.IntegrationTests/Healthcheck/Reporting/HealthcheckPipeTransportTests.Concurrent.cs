using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan ConcurrentDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ConcurrentObservation RunConcurrentScenario(ConcurrentControl? control = null)
    {
        var facts = new ConcurrentFacts();
        var serviceClock = control?.UnknownSourceError is { } error ? new ConcurrentServiceClock(error) : null;
        (HealthcheckService service, IConditionMonitor monitor) = serviceClock is null
            ? Create(startup: control?.StartupCompleted ?? true)
            : CreateConcurrentControlService(serviceClock);
        string pipe = TransportTestSupport.NewPipeName();
        var source = new ConcurrentSource(service, facts, serviceClock);
        var serverClock = new ConcurrentClock(facts, facts.ServerTimers, control?.BeforeServerTimerDispose);
        var callerClock = new ConcurrentClock(facts, facts.CallerTimers);
        var callers = new ConcurrentCallers();
        var status = new ConcurrentStatus(facts);
        var records = new List<ConcurrentTaskRecord>();
        HealthPipeServer? server = null; Task? body = null; Exception? primary = null;
        try
        {
            control?.ObserveFacts?.Invoke(facts);
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 2, PipeRequestTimeoutMs = 5000 }, serverClock);
            body = RunConcurrentBodyAsync(server, pipe, source, monitor, callerClock, callers, facts);
            JoinConcurrent(body);
            RunConcurrentOriginalTail(server, facts, status, records, control);
        }
        catch (Exception actual) { primary = actual; }
        return ReleaseConcurrentOwners(server, service, monitor, source, serverClock, callerClock, serviceClock, callers, facts, status, records, body, primary);
    }

    private static async Task RunConcurrentBodyAsync(HealthPipeServer server, string pipe, ConcurrentSource source, IConditionMonitor monitor,
        ConcurrentClock callerClock, ConcurrentCallers callers, ConcurrentFacts facts)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        Task<JsonObject>[] actual = Enumerable.Range(0, 24).Select(_ => ConcurrentRequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}", callerClock, facts)).ToArray();
        callers.Requests.AddRange(actual);
        JsonObject[] replies = await Task.WhenAll(actual);
        foreach (JsonObject reply in replies) { TransportTestSupport.ValidateSnapshotEnvelope(reply, "health", true, true); }
        Assert.AreEqual(24, source.Captures);
        Volatile.Write(ref facts.originalResponseAssertionsReturned, 1);
        GC.KeepAlive(monitor);
    }

    // Literal helper body, with the explicit SAME15s public TimeProvider CTS overload
    // and read-only weak ownership observations. The shared helper is unchanged.
    private static async Task<JsonObject> ConcurrentRequestAsync(string pipe, string json, ConcurrentClock clock, ConcurrentFacts facts, bool fragmented = false)
    {
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit, clock);
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        facts.RecordClient(client, timeout);
        await client.ConnectAsync(timeout.Token);
        await TransportTestSupport.WriteFrameAsync(client, Encoding.UTF8.GetBytes(json), fragmented, cancellation: timeout.Token);
        byte[] payload = await TransportTestSupport.ReadFrameAsync(client, timeout.Token);
        Assert.AreNotEqual((byte)0xef, payload[0], "No UTF-8 BOM.");
        return JsonNode.Parse(payload)!.AsObject();
    }

    private static void RunConcurrentOriginalTail(HealthPipeServer server, ConcurrentFacts facts, ConcurrentStatus status,
        List<ConcurrentTaskRecord> records, ConcurrentControl? control)
    {
        Volatile.Write(ref facts.firstStopInvoked, 1);
        Task first = server.StopAsync(); records.Add(ObserveConcurrentTask(first, status, 0));
        if (!ObserveConcurrentCompletion(first))
        {
            Volatile.Write(ref facts.firstStopObservedPending, 1);
            control?.AfterFirstPendingObservation?.Invoke();
            return;
        }
        first.GetAwaiter().GetResult(); Volatile.Write(ref facts.firstStopObservedSuccess, 1);
        Volatile.Write(ref facts.secondStopInvoked, 1);
        Task second = server.StopAsync(); records.Add(ObserveConcurrentTask(second, status, 1));
        if (!ObserveConcurrentCompletion(second)) { Volatile.Write(ref facts.secondStopObservedPending, 1); return; }
        second.GetAwaiter().GetResult(); Volatile.Write(ref facts.secondStopObservedSuccess, 1);
        Volatile.Write(ref facts.startAssertionInvoked, 1);
        Task assertion = RunConcurrentStartAssertionAsync(server, facts, status, records, control);
        records.Add(ObserveConcurrentTask(assertion, status, 2));
        if (!ObserveConcurrentCompletion(assertion)) { Volatile.Write(ref facts.startAssertionObservedPending, 1); return; }
        try { assertion.GetAwaiter().GetResult(); Volatile.Write(ref facts.originalTailReturned, 1); }
        catch (AssertFailedException actual) when (ReferenceEquals(actual, facts.OriginalTerminalAssertion?.SourceException))
        {
            // The exact original assertion is released only after real cleanup.
        }
    }

    private static async Task RunConcurrentStartAssertionAsync(HealthPipeServer server, ConcurrentFacts facts, ConcurrentStatus status,
        List<ConcurrentTaskRecord> records, ConcurrentControl? control)
    {
        try { await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ConcurrentActualStart(server, facts, status, records, control)); }
        catch (AssertFailedException actual) { facts.RecordOriginalTerminalAssertion(actual); throw; }
    }

    private static Task ConcurrentActualStart(HealthPipeServer server, ConcurrentFacts facts, ConcurrentStatus status,
        List<ConcurrentTaskRecord> records, ConcurrentControl? control)
    {
        Exception? expected = null;
        try
        {
            try
            {
                Task actual = server.StartAsync();
                // A returned promise is not the original synchronous terminal guard.
                records.Add(ObserveConcurrentTask(actual, status, 5));
                return actual;
            }
            catch (Exception actual)
            {
                MethodInfo site = typeof(HealthPipeServer).GetMethod(nameof(HealthPipeServer.StartAsync))!;
                if (actual.GetType() == typeof(InvalidOperationException) && actual.Message == "The Healthcheck pipe server is stopped."
                    && actual.TargetSite == site && actual.InnerException is null && actual.Data.Count == 0)
                {
                    expected = actual; facts.RecordOriginalStartGuard(actual);
                    control?.AfterActualStartGuard?.Invoke();
                }
                throw;
            }
        }
        catch (Exception actual)
        {
            if (!ReferenceEquals(actual, expected)) { facts.RecordError(actual); }
            throw;
        }
    }

    private static bool ObserveConcurrentCompletion(Task actual)
    {
        var elapsed = Stopwatch.StartNew();
        while (!actual.IsCompleted && elapsed.Elapsed < ConcurrentDeadline) { Thread.Sleep(1); }
        return actual.IsCompleted;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ConcurrentObservation ReleaseConcurrentOwners(HealthPipeServer? server, HealthcheckService service, IConditionMonitor monitor,
        ConcurrentSource source, ConcurrentClock serverClock, ConcurrentClock callerClock, ConcurrentServiceClock? serviceClock,
        ConcurrentCallers callers, ConcurrentFacts facts, ConcurrentStatus status, List<ConcurrentTaskRecord> records, Task? body, Exception? primary)
    {
        // BOTH ancillary APIs are really invoked on every exit, only after the
        // reached original tail. Never delay Dispose to make graph release easier.
        Task? stop = null, dispose = null;
        if (server is not null)
        {
            Volatile.Write(ref facts.cleanupStopInvoked, 1); stop = BeginConcurrentStop(server);
            Volatile.Write(ref facts.cleanupDisposeInvoked, 1); dispose = BeginConcurrentDispose(server);
        }
        if (stop is not null) { records.Add(ObserveConcurrentTask(stop, status, 3)); }
        if (dispose is not null) { records.Add(ObserveConcurrentTask(dispose, status, 4)); }
        var holds = new List<Exception>();
        if (body is not null)
        {
            try { JoinConcurrent(body); Volatile.Write(ref facts.bodyJoined, 1); }
            catch (Exception actual) when (ReferenceEquals(actual, primary)) { Volatile.Write(ref facts.bodyJoined, 1); }
            catch (Exception actual) { holds.Add(actual); }
        }
        foreach (Task<JsonObject> actual in callers.Requests)
        {
            try { JoinConcurrent(actual); Interlocked.Increment(ref facts.actualClientTasksJoined); }
            catch (Exception error) { holds.Add(error); }
        }
        try { callerClock.JoinActualDisposals(); } catch (Exception error) { holds.Add(error); }
        try { serverClock.JoinActualDisposals(); } catch (Exception error) { holds.Add(error); }
        try { JoinConcurrent(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); } catch (Exception error) { holds.Add(error); }
        try { JoinConcurrent(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); } catch (Exception error) { holds.Add(error); }
        GC.KeepAlive(monitor);
        holds.AddRange(facts.Errors()); holds.AddRange(status.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && body is not null && callers.Requests.Count == 24
            && !stop!.IsFaulted && !stop.IsCanceled && !dispose!.IsFaulted && !dispose.IsCanceled;
        if (eligible)
        {
            try { RequireConcurrentPhase(facts); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(serverClock), new(callerClock), new(callers),
                .. facts.WeakClients(), .. serverClock.WeakTimerOwners(), .. callerClock.WeakTimerOwners()];
            if (serviceClock is not null) { graph = [.. graph, new(serviceClock)]; }
            return new(null, graph, facts, status, [.. records]);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stop!, dispose!);
            try { JoinConcurrent(all); } catch (Exception error) { holds.Add(all.Exception ?? error); }
        }
        foreach (ConcurrentTaskRecord record in records)
        {
            try { if (record.Observer.TryGetTarget(out Task? actual)) { JoinConcurrent(actual); } }
            catch (Exception error) { holds.Add(error); }
        }
        holds.AddRange(facts.Errors()); holds.AddRange(status.Errors());
        if (facts.OriginalTerminalAssertion is { } terminal) { holds.Add(terminal.SourceException); }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original concurrent body, actual terminal phase or real cleanup failed.", DistinctConcurrent(holds));
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, status, [.. records]);
    }

    private static void ReviewConcurrentScenario(ConcurrentObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            Volatile.Write(ref observed.Facts.ownerReviewStarted, 1);
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < ConcurrentDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < ConcurrentDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive)) { throw new InvalidOperationException("Actual concurrent client/source/System timer/state/worker ownership remains live; elapsed time grants no settlement."); }
            Volatile.Write(ref observed.Facts.actualOwnersDead, 1);
            RequireConcurrentPhase(observed.Facts);
            foreach (ConcurrentTaskRecord record in observed.Records) { SettleConcurrentObserver(record, observed.Status); }
            Exception[] errors = DistinctConcurrent(observed.Facts.Errors().Concat(observed.Status.Errors()));
            if (errors.Length > 0)
            {
                if (observed.Facts.OriginalTerminalAssertion is { } terminal) { errors = [.. errors, terminal.SourceException]; }
                throw new AggregateException("Actual concurrent producer, contract or resource failures remain unconditional holds.", errors);
            }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length == 0) { return; }
        ConcurrentFacts facts = observed.Facts;
        if (Volatile.Read(ref facts.firstStopObservedPending) == 1)
        {
            if (observed.Status.Success(0)) { throw new InvalidOperationException("Actual first Stop completed late, but original second Stop and Start rejection were honestly unreached before Dispose."); }
            Assert.IsTrue(observed.Status.Completed(0), "The actual original FIRST Stop must publish completion after all24 real requests, both timer inventories, state and workers settle.");
            throw new InvalidOperationException("A first-Stop completion without a coherent observed successful outcome remains a hold.");
        }
        if (Volatile.Read(ref facts.firstStopObservedSuccess) != 1 || Volatile.Read(ref facts.secondStopObservedSuccess) != 1
            || Volatile.Read(ref facts.secondStopObservedPending) != 0 || Volatile.Read(ref facts.startAssertionObservedPending) != 0
            || Volatile.Read(ref facts.startAssertionInvoked) != 1)
        { throw new InvalidOperationException("Original concurrent terminal phases were not actually completed in order before Dispose; late outcomes cannot reconstruct them."); }
        if (!observed.Status.Success(0) || !observed.Status.Success(1) || !observed.Status.Success(3) || !observed.Status.Success(4))
        { throw new InvalidOperationException("Actual original Stops and independently invoked cleanup Stop/Dispose must really succeed before a healthy result or original terminal assertion is released."); }
        if (facts.OriginalTerminalAssertion is { } original) { original.Throw(); }
        if (!observed.Status.Success(2) || Volatile.Read(ref facts.originalTailReturned) != 1 || facts.OriginalStartGuard is null)
        { throw new InvalidOperationException("The original actual Start rejection assertion must really execute and return after both real Stops."); }
        Volatile.Write(ref facts.stageReviewSucceeded, 1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SettleConcurrentObserver(ConcurrentTaskRecord record, ConcurrentStatus status)
    {
        if (record.Observer.TryGetTarget(out Task? observer))
        {
            bool antecedentCompleted = record.Actual.TryGetTarget(out Task? actual) && actual.IsCompleted;
            if (antecedentCompleted || observer.Status != TaskStatus.WaitingForActivation || status.Completed(record.Role))
            {
                JoinConcurrent(observer);
            }
            else if (record.Actual.TryGetTarget(out actual) && actual.IsCompleted)
            {
                // An actual completion racing the first read must be observed.
                JoinConcurrent(observer);
            }
        }
        if (record.Actual.TryGetTarget(out Task? completed) && completed.IsCompleted && !status.Completed(record.Role))
        { throw new InvalidOperationException("An actual concurrent terminal promise completed without a settled matching observer."); }
    }

    private static void RequireConcurrentPhase(ConcurrentFacts facts)
    {
        Exception[] errors = facts.Errors(); if (errors.Length > 0) { throw new AggregateException("Actual concurrent unknown source/clock/callback errors cannot be hidden by an original assertion.", errors); }
        if (Volatile.Read(ref facts.captures) != 24 || Volatile.Read(ref facts.returns) != 24 || Volatile.Read(ref facts.originalResponseAssertionsReturned) != 1
            || Volatile.Read(ref facts.bodyJoined) != 1 || Volatile.Read(ref facts.actualClientTasksJoined) != 24 || facts.WeakClients().Length != 48
            || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1 || Volatile.Read(ref facts.firstStopInvoked) != 1)
        { throw new InvalidOperationException("All24 original concurrent IO/source/assertion/client scopes and real state cleanup must join before any owner candidate."); }
        foreach (ConcurrentTimerFacts timers in new[] { facts.ServerTimers, facts.CallerTimers })
        {
            if (Volatile.Read(ref timers.created) != 24 || Volatile.Read(ref timers.creationEntries) != 24 || Volatile.Read(ref timers.creationReturns) != 24
                || Volatile.Read(ref timers.disposeEntries) != 24 || Volatile.Read(ref timers.disposeReturns) != 24 || Volatile.Read(ref timers.actualDrainsJoined) != 24
                || Volatile.Read(ref timers.callbacksEntered) != Volatile.Read(ref timers.callbacksReturned))
            { throw new InvalidOperationException("Each separate actual24 server/client System timer inventory and every entered callback/DisposeAsync drain must settle."); }
        }
    }

    private static void JoinConcurrent(Task actual) => actual.WaitAsync(ConcurrentDeadline).GetAwaiter().GetResult();
    private static Task BeginConcurrentStop(HealthPipeServer server) { try { return server.StopAsync(); } catch (Exception error) { return Task.FromException(error); } }
    private static Task BeginConcurrentDispose(HealthPipeServer server) { try { return server.DisposeAsync().AsTask(); } catch (Exception error) { return Task.FromException(error); } }
    private static Exception[] DistinctConcurrent(IEnumerable<Exception> errors) => errors.SelectMany(error => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [error]).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
    private static ConcurrentTaskRecord ObserveConcurrentTask(Task actual, ConcurrentStatus status, int role)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var value = ((ConcurrentStatus Status, int Role))state!; value.Status.Record(value.Role, completed);
        }, (status, role), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(new(actual), new(observer), role);
    }
    private sealed record ConcurrentObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph, ConcurrentFacts Facts, ConcurrentStatus Status, ConcurrentTaskRecord[] Records);
    private sealed record ConcurrentTaskRecord(WeakReference<Task> Actual, WeakReference<Task> Observer, int Role);
    private sealed class ConcurrentCallers { internal List<Task<JsonObject>> Requests { get; } = []; }
    private sealed class ConcurrentControl
    {
        internal bool StartupCompleted { get; init; } = true;
        internal Exception? UnknownSourceError { get; init; }
        internal Action<int>? BeforeServerTimerDispose { get; init; }
        internal Action? AfterFirstPendingObservation { get; init; }
        internal Action? AfterActualStartGuard { get; init; }
        internal Action<object>? ObserveFacts { get; init; }
    }
    private sealed class ConcurrentTimerFacts
    {
        internal int creationEntries, creationReturns, created, disposeEntries, disposeReturns, actualDrainsJoined, callbacksEntered, callbacksReturned;
    }
    private sealed class ConcurrentFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private readonly List<WeakReference> clients = [];
        private ExceptionDispatchInfo? terminalAssertion;
        private Exception? startGuard;
        internal ConcurrentTimerFacts ServerTimers { get; } = new();
        internal ConcurrentTimerFacts CallerTimers { get; } = new();
        internal int captures, returns, originalResponseAssertionsReturned, bodyJoined, actualClientTasksJoined, monitorJoined, serviceJoined;
        internal int firstStopInvoked, firstStopObservedSuccess, firstStopObservedPending, secondStopInvoked, secondStopObservedSuccess, secondStopObservedPending;
        internal int startAssertionInvoked, startAssertionObservedPending, originalTailReturned, ownerReviewStarted, actualOwnersDead, stageReviewSucceeded;
        internal int cleanupStopInvoked, cleanupDisposeInvoked;
        internal ExceptionDispatchInfo? OriginalTerminalAssertion { get { lock (gate) { return terminalAssertion; } } }
        internal Exception? OriginalStartGuard { get { lock (gate) { return startGuard; } } }
        internal void RecordOriginalTerminalAssertion(AssertFailedException error) { lock (gate) { if (terminalAssertion is not null) { errors.Add(error); } else { terminalAssertion = ExceptionDispatchInfo.Capture(error); } } }
        internal void RecordOriginalStartGuard(Exception error) { lock (gate) { if (startGuard is not null) { errors.Add(error); } else { startGuard = error; } } }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        internal void RecordClient(NamedPipeClientStream client, CancellationTokenSource timeout) { lock (gate) { clients.Add(new(client)); clients.Add(new(timeout)); } }
        internal WeakReference[] WeakClients() { lock (gate) { return [.. clients]; } }
    }
    private sealed class ConcurrentStatus(ConcurrentFacts facts)
    {
        private readonly object gate = new();
        private readonly bool[] complete = new bool[6], success = new bool[6];
        private readonly List<Exception> errors = [];
        internal bool Completed(int role) { lock (gate) { return complete[role]; } }
        internal bool Success(int role) { lock (gate) { return success[role]; } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        internal void Record(int role, Task actual)
        {
            lock (gate)
            {
                complete[role] = true;
                if (actual.Exception is { } fault)
                {
                    foreach (Exception error in fault.Flatten().InnerExceptions)
                    {
                        if (!(role == 2 && ReferenceEquals(error, facts.OriginalTerminalAssertion?.SourceException))) { errors.Add(error); }
                    }
                }
                else if (actual.IsCanceled) { errors.Add(new TaskCanceledException("An actual uncanceled concurrent terminal operation canceled.")); }
                else { success[role] = true; }
            }
        }
    }
    private sealed class ConcurrentSource(HealthcheckService service, ConcurrentFacts facts, ConcurrentServiceClock? serviceClock) : IHealthSnapshotSource
    {
        internal int Captures => Volatile.Read(ref facts.captures);
        public HealthSnapshot CaptureSnapshot()
        {
            int capture = Interlocked.Increment(ref facts.captures);
            try { if (capture == 1) { serviceClock?.Arm(); } return service.CaptureSnapshot(); }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private static (HealthcheckService Service, IConditionMonitor Monitor) CreateConcurrentControlService(ConcurrentServiceClock clock)
    {
        var service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "pipe-fixture", InstanceId = Guid.NewGuid() }, clock);
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false, Name = "critical" });
        service.MarkStartupCompleted(); return (service, monitor);
    }
    private sealed class ConcurrentServiceClock(Exception error) : TimeProvider
    {
        private int armed;
        internal void Arm() => Volatile.Write(ref armed, 1);
        public override DateTimeOffset GetUtcNow() { if (Interlocked.Exchange(ref armed, 0) == 1) { throw error; } return TimeProvider.System.GetUtcNow(); }
    }
}
