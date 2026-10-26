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
    private static readonly TimeSpan PendingDisposeDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PendingDisposeObservation RunPendingDisposeScenario(HealthcheckService? observedService = null,
        IConditionMonitor? observedMonitor = null, PendingDisposeControl? control = null)
    {
        if ((observedService is null) != (observedMonitor is null)) { throw new ArgumentException("Both actual control state owners are required together."); }
        var (service, monitor) = observedService is null ? Create() : (observedService, observedMonitor!);
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new PendingDisposeFacts();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim();
        var source = new PendingDisposeSource(service, entered, release, facts, control?.BeforeSourceCapture);
        var serverClock = new PendingDisposeSystemClock(facts.ServerTimer, control?.BeforeServerTimerDispose);
        var callerClock = new PendingDisposeSystemClock(facts.CallerTimer);
        HealthPipeServer? server = null;
        Task<JsonObject>? request = null;
        Task? firstDisposal = null, contract = null;
        Exception? primary = null;
        try
        {
            control?.ObserveFacts?.Invoke(facts);
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }, serverClock);
            JoinPendingDispose(TransportTestSupport.StartServerAsync(server, pipe));
            request = PendingDisposeRequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}", callerClock, facts, control);
            JoinPendingDispose(entered.Task);
            control?.AfterSourceEntered?.Invoke();
            firstDisposal = BeginPendingDispose(server);
            Volatile.Write(ref facts.firstDisposeInvoked, 1);
            try
            {
                Assert.IsFalse(firstDisposal.IsCompleted, "Disposal must await the source that has entered and whose release gate is still closed.");
                Volatile.Write(ref facts.originalPendingAssertionReturned, 1);
            }
            catch (AssertFailedException error) { facts.RegisterEarlyAssertion(error); throw; }
            Volatile.Write(ref facts.originalCallerRelease, 1);
            release.Set();
            // Await the actual first Dispose/later Stop promises. Only their
            // deadline supervisors move outside this otherwise original order.
            contract = PendingDisposeOriginalContractAsync(server, firstDisposal, request, facts);
        }
        catch (Exception error) { primary = error; }
        return ReleasePendingDisposeOwners(server, service, monitor, source, entered, release,
            serverClock, callerClock, request, firstDisposal, contract, facts, control, primary);
    }

    private static async Task PendingDisposeOriginalContractAsync(HealthPipeServer server, Task firstDisposal,
        Task<JsonObject> request, PendingDisposeFacts facts)
    {
        try
        {
            await firstDisposal;
            Volatile.Write(ref facts.firstDisposeSucceeded, 1);
            await server.StopAsync();
            Volatile.Write(ref facts.laterStopSucceeded, 1);
            try
            {
                await request;
                try { Assert.Fail("A pending reply cannot succeed after disposal closes admission."); }
                catch (AssertFailedException error) { facts.RegisterReplyAssertion(error); throw; }
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
            Volatile.Write(ref facts.originalReplyFailureAssertionReturned, 1);
        }
        catch (Exception error) { facts.RecordContractError(error); throw; }
    }

    // Literal one-row helper clone. The approved public CTS(TimeSpan,
    // TimeProvider) overload retains the original15s while exposing its actual
    // System timer separately from the server's original1000ms budget.
    private static async Task<JsonObject> PendingDisposeRequestAsync(string pipe, string json,
        PendingDisposeSystemClock clock, PendingDisposeFacts facts, PendingDisposeControl? control, bool fragmented = false)
    {
        Exception? propagating = null;
        try
        {
            using var timeout = new CancellationTokenSource(TransportTestSupport.Limit, clock);
            facts.ObserveOwner(timeout);
            control?.ObserveCallerCancellation?.Invoke(timeout);
            try
            {
                await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                facts.ObserveOwner(client);
                try
                {
                    await client.ConnectAsync(timeout.Token);
                    await TransportTestSupport.WriteFrameAsync(client, Encoding.UTF8.GetBytes(json), fragmented, cancellation: timeout.Token);
                    byte[] payload = await TransportTestSupport.ReadFrameAsync(client, timeout.Token);
                    Assert.AreNotEqual((byte)0xef, payload[0], "No UTF-8 BOM.");
                    return JsonNode.Parse(payload)!.AsObject();
                }
                catch (Exception error) { propagating = error; throw; }
            }
            catch (Exception error)
            {
                // Preserve a distinct actual client-construction/disposal fault
                // even if its type is one of the original transport catches.
                if (!ReferenceEquals(error, propagating)) { facts.RecordError(error); }
                propagating = error;
                throw;
            }
            finally { if (timeout.IsCancellationRequested) { Volatile.Write(ref facts.callerCancellationRequested, 1); } }
        }
        catch (Exception error)
        {
            if (!ReferenceEquals(error, propagating)) { facts.RecordError(error); }
            throw;
        }
        finally { Volatile.Write(ref facts.actualRequestScopeUnwound, 1); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PendingDisposeObservation ReleasePendingDisposeOwners(HealthPipeServer? server, HealthcheckService service,
        IConditionMonitor monitor, PendingDisposeSource source, TaskCompletionSource entered, ManualResetEventSlim release,
        PendingDisposeSystemClock serverClock, PendingDisposeSystemClock callerClock, Task<JsonObject>? request,
        Task? firstDisposal, Task? contract, PendingDisposeFacts facts, PendingDisposeControl? control, Exception? primary)
    {
        // These are independently initiated actual cleanup operations. Neither
        // supplies success to the original first-disposal task.
        Task? stopping = server is null ? null : BeginPendingDisposeStop(server);
        Task? disposing = server is null ? null : BeginPendingDispose(server);
        var status = new PendingDisposeStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObservePendingDisposeTask(stopping, status, facts, 0)); }
        if (disposing is not null) { observers.Add(ObservePendingDisposeTask(disposing, status, facts, 1)); }
        if (firstDisposal is not null) { observers.Add(ObservePendingDisposeTask(firstDisposal, status, facts, 2)); }
        if (contract is not null) { observers.Add(ObservePendingDisposeTask(contract, status, facts, 3)); }
        var holds = new List<Exception>();
        try { release.Set(); Volatile.Write(ref facts.cleanupReleaseCalled, 1); }
        catch (Exception error) { holds.Add(error); }
        if (request is not null)
        {
            try { _ = JoinPendingDispose(request); Volatile.Write(ref facts.actualRequestJoined, 1); }
            catch (IOException) { Volatile.Write(ref facts.actualRequestJoined, 1); }
            catch (OperationCanceledException) { Volatile.Write(ref facts.actualRequestJoined, 1); }
            catch (Exception error) { holds.Add(error); }
        }
        if (entered.Task.IsCompletedSuccessfully)
        {
            try { JoinPendingDispose(source.Returned); Volatile.Write(ref facts.actualSourceJoined, 1); }
            catch (Exception error) { holds.Add(error); }
        }
        try { serverClock.JoinActualDisposals(); }
        catch (Exception error) { holds.Add(error); }
        try { callerClock.JoinActualDisposals(); }
        catch (Exception error) { holds.Add(error); }
        // State must outlive the actual source capture. Do not repair a live
        // capture by disposing the service underneath it.
        if (Volatile.Read(ref facts.actualSourceJoined) == 1 || Volatile.Read(ref facts.sourceCaptures) == 0)
        {
            try { JoinPendingDispose(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            try { JoinPendingDispose(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
            catch (Exception error) { holds.Add(error); }
        }
        else { holds.Add(new InvalidOperationException("The actual snapshot source has not returned; its state cannot be disposed to invent settlement.")); }
        if (Volatile.Read(ref facts.actualSourceJoined) == 1 || Volatile.Read(ref facts.sourceCaptures) == 0)
        {
            try { release.Dispose(); Volatile.Write(ref facts.actualGateDisposed, 1); }
            catch (Exception error) { holds.Add(error); }
        }
        try { control?.AfterActualDrains?.Invoke(); }
        catch (Exception error) { facts.RecordError(error); }
        GC.KeepAlive(monitor);
        holds.AddRange(facts.Errors()); holds.AddRange(status.Errors());
        bool knownPrimary = primary is null || facts.IsEarlyAssertion(primary);
        bool eligible = knownPrimary && holds.Count == 0 && server is not null && request is not null && firstDisposal is not null
            && (contract is not null || facts.EarlyAssertion is not null)
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled
            && !firstDisposal.IsFaulted && !firstDisposal.IsCanceled;
        if (eligible)
        {
            try { RequirePendingDisposePhase(facts); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        WeakReference<Task>? completedContractSingleton = null;
        if (eligible && contract is not null && ReferenceEquals(contract, Task.CompletedTask))
        {
            try
            {
                RequirePendingDisposeCompletedContractSingleton(contract, firstDisposal!, facts, status);
                completedContractSingleton = new(contract);
            }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // Default8 retained. Every actual RunSlot retains Server through
            // IO, its real request budget and finally under the reviewed
            // accounting/progress family. No one-slot proof is substituted.
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(entered), new(entered.Task),
                new(release), new(source.Returned), new(serverClock), new(callerClock), new(request!), new(firstDisposal!),
                .. (contract is null || completedContractSingleton is not null ? Array.Empty<WeakReference>() : new[] { new WeakReference(contract) }),
                .. facts.WeakOwners(), .. serverClock.WeakTimerOwners(), .. callerClock.WeakTimerOwners()];
            return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), graph, facts, status, [.. observers])
            {
                CompletedContractSingleton = completedContractSingleton
            };
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(new Task?[] { stopping, disposing, firstDisposal, contract }.OfType<Task>());
            try { JoinPendingDispose(all); }
            catch (Exception error)
            {
                foreach (Exception cause in all.Exception?.Flatten().InnerExceptions ?? [error])
                { if (!facts.IsReplyAssertion(cause)) { holds.Add(cause); } }
            }
            foreach (WeakReference<Task> reference in observers)
            {
                try { if (reference.TryGetTarget(out Task? actual)) { JoinPendingDispose(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
        }
        holds.AddRange(facts.Errors()); holds.AddRange(status.Errors());
        if (facts.ReplyAssertion is { } reply) { holds.Add(reply.SourceException); }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The actual pending-disposal source, IO, state or terminal cleanup remains unresolved.", FlattenPendingDispose(holds));
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, status, []);
    }

    private static void ReviewPendingDisposeScenario(PendingDisposeObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < PendingDisposeDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < PendingDisposeDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive)) { ThrowPendingDisposeHold(observed, new InvalidOperationException("Actual default8 server/source/client/state ownership remains live; elapsed time is not settlement.")); }
            try
            {
                RequirePendingDisposePhase(observed.Facts);
                if (observed.CompletedContractSingleton is { } singleton)
                {
                    RequirePendingDisposeCompletedContractSingletonAfterGraph(singleton, observed.Facts, observed.Status);
                }
                foreach (WeakReference<Task> reference in observed.Observers) { if (reference.TryGetTarget(out Task? actual)) { JoinPendingDispose(actual); } }
                Exception[] errors = FlattenPendingDispose(observed.Facts.Errors().Concat(observed.Status.Errors()));
                if (errors.Length > 0) { throw new AggregateException("Unknown actual terminal/resource failures remain holds.", errors); }
            }
            catch (Exception error) { ThrowPendingDisposeHold(observed, error); }
            if (observed.Primary is not null)
            {
                if (!observed.Status.Stop || !observed.Status.Dispose || !observed.Status.FirstDispose) { ThrowPendingDisposeHold(observed, new InvalidOperationException("An early original assertion cannot be released before actual cleanup completion.")); }
                observed.Primary.Throw();
            }
            // A dead graph cannot publish a missing first Dispose promise.
            Assert.AreEqual(1, Volatile.Read(ref observed.Facts.firstDisposeInvoked), "The actual original first Dispose must be invoked.");
            Assert.AreEqual(1, Volatile.Read(ref observed.Facts.firstDisposeSucceeded), "The actual original first Dispose must publish success after real source, IO, timer and all8 worker ownership settles.");
            Assert.AreEqual(1, Volatile.Read(ref observed.Facts.laterStopSucceeded), "The original later Stop must actually complete after first Dispose.");
            if (observed.Facts.ReplyAssertion is { } reply)
            {
                if (!observed.Status.Stop || !observed.Status.Dispose) { ThrowPendingDisposeHold(observed, new InvalidOperationException("The original reply assertion cannot bypass actual terminal cleanup.")); }
                reply.Throw();
            }
            Assert.IsTrue(observed.Status.Contract, "The actual original terminal contract must complete successfully.");
            Assert.AreEqual(1, Volatile.Read(ref observed.Facts.originalReplyFailureAssertionReturned), "The original pending-reply failure assertion must actually execute.");
            Assert.IsTrue(observed.Status.Stop, "Actual independent cleanup Stop must complete successfully.");
            Assert.IsTrue(observed.Status.Dispose, "Actual independent cleanup Dispose must complete successfully.");
        }
        observed.Primary?.Throw();
    }

    private static void RequirePendingDisposePhase(PendingDisposeFacts facts)
    {
        Exception[] errors = facts.Errors(); if (errors.Length > 0) { throw new AggregateException("Actual unknown source, clock, callback or observer faults cannot be omitted.", errors); }
        if (Volatile.Read(ref facts.sourceCaptures) != 1 || Volatile.Read(ref facts.sourceReturns) != 1 || Volatile.Read(ref facts.sourceWaitSucceeded) != 1
            || Volatile.Read(ref facts.actualSourceJoined) != 1 || Volatile.Read(ref facts.cleanupReleaseCalled) != 1 || Volatile.Read(ref facts.actualGateDisposed) != 1
            || Volatile.Read(ref facts.actualRequestJoined) != 1 || Volatile.Read(ref facts.actualRequestScopeUnwound) != 1 || Volatile.Read(ref facts.callerCancellationRequested) != 0
            || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1 || Volatile.Read(ref facts.firstDisposeInvoked) != 1
            || (facts.EarlyAssertion is null && (Volatile.Read(ref facts.originalCallerRelease) != 1 || Volatile.Read(ref facts.originalPendingAssertionReturned) != 1)))
        { throw new InvalidOperationException("Only actual source release/return, client scopes, unexpired caller watchdog and state/gate joins permit pending-disposal ownership review."); }
        facts.ServerTimer.Require(1000); facts.CallerTimer.Require(15000);
        if (Volatile.Read(ref facts.CallerTimer.callbacksEntered) != 0) { throw new InvalidOperationException("Caller watchdog expiration cannot supply original disposal success."); }
    }

    private static Exception[] FlattenPendingDispose(IEnumerable<Exception> errors) => errors.SelectMany<Exception, Exception>(error => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { error }).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
    private static void ThrowPendingDisposeHold(PendingDisposeObservation observed, Exception error)
    {
        var errors = new List<Exception> { error }; if (observed.Primary is { } primary) { errors.Add(primary.SourceException); }
        if (observed.Facts.ReplyAssertion is { } reply) { errors.Add(reply.SourceException); }
        errors.AddRange(observed.Facts.Errors()); errors.AddRange(observed.Status.Errors());
        throw new AggregateException("Original pending-disposal assertions cannot bypass actual resource/owner holds.", FlattenPendingDispose(errors));
    }
    private static void JoinPendingDispose(Task task) => task.WaitAsync(PendingDisposeDeadline).GetAwaiter().GetResult();
    private static T JoinPendingDispose<T>(Task<T> task) => task.WaitAsync(PendingDisposeDeadline).GetAwaiter().GetResult();
    private static Task BeginPendingDispose(HealthPipeServer server) { try { return server.DisposeAsync().AsTask(); } catch (Exception error) { return Task.FromException(error); } }
    private static Task BeginPendingDisposeStop(HealthPipeServer server) { try { return server.StopAsync(); } catch (Exception error) { return Task.FromException(error); } }
    private static WeakReference<Task> ObservePendingDisposeTask(Task task, PendingDisposeStatus status, PendingDisposeFacts facts, int kind)
    {
        Task observer = task.ContinueWith(static (actual, state) =>
        {
            var observed = ((PendingDisposeStatus Status, PendingDisposeFacts Facts, int Kind))state!;
            if (actual.Exception is { } error) { foreach (Exception cause in error.Flatten().InnerExceptions) { if (!(observed.Kind == 3 && observed.Facts.IsReplyAssertion(cause))) { observed.Status.RecordError(cause); } } }
            else if (actual.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("An actual uncanceled pending-disposal terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Kind); }
        }, (status, facts, kind), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record PendingDisposeObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph, PendingDisposeFacts Facts, PendingDisposeStatus Status, WeakReference<Task>[] Observers)
    {
        internal WeakReference<Task>? CompletedContractSingleton { get; init; }
    }
    private sealed class PendingDisposeStatus
    {
        private readonly object gate = new(); private readonly List<Exception> errors = []; private bool stop, dispose, firstDispose, contract;
        internal bool Stop { get { lock (gate) { return stop; } } }
        internal bool Dispose { get { lock (gate) { return dispose; } } }
        internal bool FirstDispose { get { lock (gate) { return firstDispose; } } }
        internal bool Contract { get { lock (gate) { return contract; } } }
        internal void RecordSuccess(int kind) { lock (gate) { switch (kind) { case 0: stop = true; break; case 1: dispose = true; break; case 2: firstDispose = true; break; case 3: contract = true; break; default: throw new InvalidOperationException("Unknown actual terminal observer kind."); } } }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class PendingDisposeFacts
    {
        private readonly object gate = new(); private readonly List<Exception> errors = []; private readonly List<WeakReference> owners = [];
        private ExceptionDispatchInfo? earlyAssertion, replyAssertion;
        internal readonly PendingDisposeTimerFacts ServerTimer = new(), CallerTimer = new();
        internal int sourceCaptures, sourceReturns, sourceWaitSucceeded, firstDisposeInvoked, originalPendingAssertionReturned, originalCallerRelease;
        internal int cleanupReleaseCalled, actualGateDisposed, actualSourceJoined, actualRequestJoined, actualRequestScopeUnwound, callerCancellationRequested;
        internal int monitorJoined, serviceJoined, firstDisposeSucceeded, laterStopSucceeded, originalReplyFailureAssertionReturned;
        internal ExceptionDispatchInfo? EarlyAssertion { get { lock (gate) { return earlyAssertion; } } }
        internal ExceptionDispatchInfo? ReplyAssertion { get { lock (gate) { return replyAssertion; } } }
        internal void RecordError(Exception error) { lock (gate) { if (!errors.Any(existing => ReferenceEquals(existing, error))) { errors.Add(error); } } }
        internal Exception[] Errors() { lock (gate) { return [.. errors, .. ServerTimer.Errors(), .. CallerTimer.Errors()]; } }
        internal void ObserveOwner(object owner) { lock (gate) { owners.Add(new(owner)); } }
        internal WeakReference[] WeakOwners() { lock (gate) { return [.. owners]; } }
        internal void RegisterEarlyAssertion(AssertFailedException error) { lock (gate) { earlyAssertion = ExceptionDispatchInfo.Capture(error); } }
        internal void RegisterReplyAssertion(AssertFailedException error) { lock (gate) { replyAssertion = ExceptionDispatchInfo.Capture(error); } }
        internal bool IsEarlyAssertion(Exception error) { lock (gate) { return ReferenceEquals(earlyAssertion?.SourceException, error); } }
        internal bool IsReplyAssertion(Exception error) { lock (gate) { return ReferenceEquals(replyAssertion?.SourceException, error); } }
        internal void RecordContractError(Exception error) { if (!IsReplyAssertion(error)) { RecordError(error); } }
    }
    private sealed class PendingDisposeControl
    {
        internal Action<object>? ObserveFacts { get; init; }
        internal Action<CancellationTokenSource>? ObserveCallerCancellation { get; init; }
        internal Action? AfterSourceEntered { get; init; }
        internal Action? BeforeSourceCapture { get; init; }
        internal Action? BeforeServerTimerDispose { get; init; }
        internal Action? AfterActualDrains { get; init; }
    }
    private sealed class PendingDisposeSource(HealthcheckService service, TaskCompletionSource entered,
        ManualResetEventSlim release, PendingDisposeFacts facts, Action? beforeCapture) : IHealthSnapshotSource
    {
        private readonly TaskCompletionSource returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Returned => returned.Task;
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref facts.sourceCaptures);
            try
            {
                entered.TrySetResult();
                bool released = release.Wait(TransportTestSupport.Limit);
                if (released) { Volatile.Write(ref facts.sourceWaitSucceeded, 1); }
                // Preserve original ignored-bool behavior: always do the same
                // real capture. A false Wait is later an ownership hold.
                beforeCapture?.Invoke();
                return service.CaptureSnapshot();
            }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref facts.sourceReturns); returned.TrySetResult(); }
        }
    }
}
