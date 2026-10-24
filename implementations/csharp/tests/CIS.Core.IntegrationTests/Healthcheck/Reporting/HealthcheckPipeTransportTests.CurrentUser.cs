using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.AccessControl;
using System.Security.Principal;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan CurrentUserDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CurrentUserObservation RunCurrentUserScenario(CurrentUserControl? control = null)
    {
        CurrentUserOwners owned = BeginCurrentUserScenario(control);
        if (owned.Primary is not null) { return new(owned.Primary, [], owned.Facts, null, []); }
        var holds = new List<Exception>();
        try
        {
            Volatile.Write(ref owned.Facts.ownerReviewStarted, 1);
            RequireCurrentUserOwnersDead(owned.Owners);
            Volatile.Write(ref owned.Facts.actualOwnersDead, 1);
            // All real RunSlot/late CreateTimer calls retain Server until return.
            // With actual owner death, this inventory is now final, not merely quiet.
            owned.Clock.JoinFinalActualDisposals();
            Volatile.Write(ref owned.Facts.finalInventoryJoined, 1);
            RequireCurrentUserPhase(owned.Facts);
            return new(null, [new(owned.Clock), .. owned.Clock.WeakTimerOwners()], owned.Facts, owned.Status, owned.Observers);
        }
        catch (Exception error) { holds.Add(error); }
        holds.AddRange(owned.Facts.Errors());
        return new(ExceptionDispatchInfo.Capture(new AggregateException("Actual current-user worker/clock settlement did not complete.", holds.SelectMany(error => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [error]).Distinct<Exception>(ReferenceEqualityComparer.Instance))), [], owned.Facts, null, []);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CurrentUserOwners BeginCurrentUserScenario(CurrentUserControl? control)
    {
        var (service, monitor) = Create();
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new CurrentUserFacts(); var source = new CurrentUserSource(service, facts); var clock = new CurrentUserClock(facts, control);
        HealthPipeServer? server = null; Task? body = null; Exception? primary = null;
        try
        {
            control?.ObserveFacts?.Invoke(facts);
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe }, clock);
            body = RunCurrentUserBodyAsync(server, pipe, clock, monitor, facts, control);
            JoinCurrentUser(body);
        }
        catch (Exception error) { primary = error; }
        Task? stopping = server is null ? null : BeginCurrentUserStop(server), disposing = server is null ? null : BeginCurrentUserDispose(server);
        var status = new CurrentUserStatus(); var observers = new List<WeakReference<Task>>(); var holds = new List<Exception>();
        if (stopping is not null) { observers.Add(ObserveCurrentUserTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveCurrentUserTerminal(disposing, status, true)); }
        if (body is not null)
        {
            try { JoinCurrentUser(body); Volatile.Write(ref facts.bodyJoined, 1); }
            catch (Exception error) when (ReferenceEquals(error, primary)) { Volatile.Write(ref facts.bodyJoined, 1); }
            catch (Exception error) { holds.Add(error); }
        }
        try { JoinCurrentUser(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); } catch (Exception error) { holds.Add(error); }
        try { JoinCurrentUser(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); } catch (Exception error) { holds.Add(error); }
        GC.KeepAlive(monitor);
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && body is not null && Volatile.Read(ref facts.bodyJoined) == 1
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            // Clock is deliberately retained through stage1. Any callback/CTS/BCL
            // back-reference that still roots Server makes this a real hold.
            return new(null, [new(server!), new(source), new(service), new(monitor), .. facts.WeakClients()], clock, facts, status, [.. observers]);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinCurrentUser(all); } catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> reference in observers)
            {
                try { if (reference.TryGetTarget(out Task? actual)) { JoinCurrentUser(actual); } } catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        // Only actual completed Stop/Dispose can close the failure-path inventory.
        if (stopping?.IsCompletedSuccessfully == true && disposing?.IsCompletedSuccessfully == true)
        {
            try { clock.JoinFinalActualDisposals(); } catch (Exception error) { holds.Add(error); }
        }
        holds.AddRange(facts.Errors());
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original current-user body or its actual cleanup failed.", holds.SelectMany(error => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [error]).Distinct<Exception>(ReferenceEqualityComparer.Instance));
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], clock, facts, status, []);
    }

    private static async Task RunCurrentUserBodyAsync(HealthPipeServer server, string pipe, CurrentUserClock clock, IConditionMonitor monitor, CurrentUserFacts facts, CurrentUserControl? control)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        facts.RecordClient(client);
        await client.ConnectAsync(timeout.Token);
        if (control?.WaitForActualCreation == true) { await clock.ActualCreated.Task.WaitAsync(TransportTestSupport.Limit); }
        if (control?.WaitForActualPublication == true) { await clock.Published.Task.WaitAsync(TransportTestSupport.Limit); }
        if (OperatingSystem.IsWindows())
        {
            PipeSecurity security = client.GetAccessControl();
            var owner = (SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!;
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            Assert.AreEqual(control?.ExpectedWindowsOwner ?? identity.User, owner);
            Assert.IsTrue(security.AreAccessRulesProtected);
            AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
            Assert.IsTrue(rules.Count > 0);
            foreach (PipeAccessRule rule in rules) { Assert.AreEqual(owner, rule.IdentityReference); Assert.AreEqual(AccessControlType.Allow, rule.AccessControlType); }
        }
        else
        {
            // Unix CurrentUserOnly checks peer UID on accept. Socket mode follows
            // the process umask; it is not promised to be 0600. Different-UID
            // rejection is exercised separately against real packaged processes.
            string endpoint = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipe);
            UnixFileMode mode = File.GetUnixFileMode(endpoint);
            const UnixFileMode ownerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            Assert.AreEqual(control?.ExpectedUnixOwnerMode ?? ownerReadWrite, mode & ownerReadWrite);
            Console.WriteLine($"Actual Unix pipe endpoint mode: {mode}; current-owner connection succeeded.");
        }
        Volatile.Write(ref facts.platformAssertionsReturned, 1);
        GC.KeepAlive(monitor);
    }

    private static void RequireCurrentUserOwnersDead(WeakReference[] owners)
    {
        var elapsed = Stopwatch.StartNew();
        while (owners.Any(reference => reference.IsAlive) && elapsed.Elapsed < CurrentUserDeadline)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            if (owners.Any(reference => reference.IsAlive) && elapsed.Elapsed < CurrentUserDeadline) { Thread.Sleep(1); }
        }
        if (owners.Any(reference => reference.IsAlive)) { throw new InvalidOperationException("Actual current-user Server/RunSlot/source/client owners remain live; a quiet clock cannot close their timer inventory."); }
    }
    private static void ReviewCurrentUserScenario(CurrentUserObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            RequireCurrentUserOwnersDead(observed.Graph);
            RequireCurrentUserPhase(observed.Facts);
            foreach (WeakReference<Task> reference in observed.Observers) { if (reference.TryGetTarget(out Task? actual)) { JoinCurrentUser(actual); } }
            Exception[] unknown = observed.Facts.Errors().Concat(observed.Status!.Errors()).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (unknown.Length > 0) { throw new AggregateException("An actual current-user clock/cleanup failure remains held.", unknown); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual current-user Stop must publish success after all real workers and the final actual timer inventory settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual current-user Dispose must publish success after all real workers and the final actual timer inventory settle.");
        }
    }
    private static void RequireCurrentUserPhase(CurrentUserFacts facts)
    {
        Exception[] errors = facts.Errors(); if (errors.Length > 0) { throw new AggregateException("Actual unknown current-user source/clock failures remain holds.", errors); }
        int created = Volatile.Read(ref facts.actualCreated), published = Volatile.Read(ref facts.published);
        if (Volatile.Read(ref facts.captures) != 0 || Volatile.Read(ref facts.returns) != 0 || Volatile.Read(ref facts.bodyJoined) != 1 || Volatile.Read(ref facts.platformAssertionsReturned) != 1
            || facts.WeakClients().Length != 1 || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1 || Volatile.Read(ref facts.actualOwnersDead) != 1
            || created is < 0 or > 1 || published != created || Volatile.Read(ref facts.createCallsEntered) != Volatile.Read(ref facts.createCallsReturned)
            || Volatile.Read(ref facts.actualDrainsJoined) != created || Volatile.Read(ref facts.actualDrainsCompleted) != created || Volatile.Read(ref facts.callbacksEntered) != Volatile.Read(ref facts.callbacksReturned) || Volatile.Read(ref facts.finalInventoryJoined) != 1)
        {
            throw new InvalidOperationException("Actual zero-source/platform/client/state work, owner death and every final0or1 System timer/callback drain must settle; no stable snapshot or elapsed time proves zero.");
        }
    }
    private static void JoinCurrentUser(Task actual) => actual.WaitAsync(CurrentUserDeadline).GetAwaiter().GetResult();
    private static Task BeginCurrentUserStop(HealthPipeServer server) { try { return server.StopAsync(); } catch (Exception error) { return Task.FromException(error); } }
    private static Task BeginCurrentUserDispose(HealthPipeServer server) { try { return server.DisposeAsync().AsTask(); } catch (Exception error) { return Task.FromException(error); } }
    private static WeakReference<Task> ObserveCurrentUserTerminal(Task actual, CurrentUserStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((CurrentUserStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("An actual uncanceled current-user terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record CurrentUserOwners(ExceptionDispatchInfo? Primary, WeakReference[] Owners, CurrentUserClock Clock, CurrentUserFacts Facts, CurrentUserStatus Status, WeakReference<Task>[] Observers);
    private sealed record CurrentUserObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph, CurrentUserFacts Facts, CurrentUserStatus? Status, WeakReference<Task>[] Observers);
    private sealed class CurrentUserStatus
    {
        private readonly object gate = new(); private readonly List<Exception> errors = []; private bool stop, dispose;
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal void RecordSuccess(bool disposal) { lock (gate) { if (disposal) { dispose = true; } else { stop = true; } } }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class CurrentUserFacts
    {
        private readonly object gate = new(); private readonly List<Exception> errors = []; private readonly List<WeakReference> clients = [];
        internal int captures, returns, bodyJoined, platformAssertionsReturned, monitorJoined, serviceJoined, ownerReviewStarted, actualOwnersDead, finalInventoryJoined;
        internal int createCallsEntered, createCallsReturned, actualCreated, published, actualDrainsJoined, actualDrainsCompleted, callbacksEntered, callbacksReturned;
        internal void RecordError(Exception error) { lock (gate) { if (!errors.Any(actual => ReferenceEquals(actual, error))) { errors.Add(error); } } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        internal void RecordClient(NamedPipeClientStream actual) { lock (gate) { clients.Add(new(actual)); } }
        internal WeakReference[] WeakClients() { lock (gate) { return [.. clients]; } }
    }
    private sealed class CurrentUserControl
    {
        internal Action<object>? ObserveFacts { get; init; }
        internal Action? BeforePublication { get; init; }
        internal Action? AfterPublication { get; init; }
        internal Action? AfterActualDrain { get; init; }
        internal bool WaitForActualPublication { get; init; }
        internal bool WaitForActualCreation { get; init; }
        internal SecurityIdentifier? ExpectedWindowsOwner { get; init; }
        internal UnixFileMode? ExpectedUnixOwnerMode { get; init; }
    }
    private sealed class CurrentUserSource(HealthcheckService service, CurrentUserFacts facts) : IHealthSnapshotSource
    {
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref facts.captures); try { return service.CaptureSnapshot(); }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private sealed class CurrentUserClock(CurrentUserFacts facts, CurrentUserControl? control) : TimeProvider
    {
        private readonly object gate = new(); private readonly List<CurrentUserTimer> timers = []; private readonly List<CurrentUserTimer> creating = [];
        internal TaskCompletionSource Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ActualCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency; public override long GetTimestamp() => TimeProvider.System.GetTimestamp(); public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref facts.createCallsEntered); CurrentUserTimer? observed = null;
            try
            {
                observed = new CurrentUserTimer(facts, callback, state, control?.AfterActualDrain);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { creating.Add(observed); }
                Interlocked.Increment(ref facts.actualCreated); ActualCreated.TrySetResult();
                control?.BeforePublication?.Invoke();
                lock (gate) { timers.Add(observed); creating.Remove(observed); }
                Interlocked.Increment(ref facts.published); Published.TrySetResult();
                control?.AfterPublication?.Invoke();
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); if (observed?.ActualPublished == true) { observed.Dispose(); } throw; }
            finally { Interlocked.Increment(ref facts.createCallsReturned); }
        }
        internal void JoinFinalActualDisposals()
        {
            if (Volatile.Read(ref facts.createCallsEntered) != Volatile.Read(ref facts.createCallsReturned)) { throw new InvalidOperationException("An actual current-user timer factory is still running; inventory is not final."); }
            CurrentUserTimer[] snapshot; lock (gate) { snapshot = timers.Concat(creating).Distinct().ToArray(); }
            var errors = new List<Exception>();
            foreach (CurrentUserTimer timer in snapshot)
            {
                try { timer.JoinActualAndReleaseOnlyObserverReferences(); Interlocked.Increment(ref facts.actualDrainsJoined); }
                catch (Exception error) { errors.Add(error); }
            }
            lock (gate) { if (timers.Concat(creating).Distinct().Count() != snapshot.Length) { errors.Add(new InvalidOperationException("Actual timer publication raced the closed owner inventory.")); } }
            if (errors.Count > 0) { throw new AggregateException("Actual current-user timers did not all drain.", errors); }
        }
        internal WeakReference[] WeakTimerOwners() { lock (gate) { return timers.Concat(creating).Distinct().SelectMany(timer => timer.WeakOwners()).ToArray(); } }
        private sealed class CurrentUserTimer(CurrentUserFacts facts, TimerCallback callback, object? state, Action? afterActualDrain) : ITimer
        {
            private readonly object gate = new(); private readonly TaskCompletionSource disposalRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TimerCallback? observedCallback = callback; private object? observedState = state; private ITimer? actual; private WeakReference? originalActual; private Task? actualDisposal;
            internal bool ActualPublished { get { lock (gate) { return originalActual is not null; } } }
            internal void Publish(ITimer timer) { lock (gate) { actual = timer; originalActual = new(timer); } }
            internal WeakReference[] WeakOwners() { lock (gate) { return [new(this), originalActual ?? throw new InvalidOperationException("Actual timer owner was not published.")]; } }
            internal void Invoke(object? unused)
            {
                Interlocked.Increment(ref facts.callbacksEntered);
                try { TimerCallback current; object? currentState; lock (gate) { current = observedCallback ?? throw new InvalidOperationException("Callback entered after real drain."); currentState = observedState; } current(currentState); }
                catch (Exception error) { facts.RecordError(error); throw; }
                finally { Interlocked.Increment(ref facts.callbacksReturned); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(CurrentUserTimer)); }
                try { return timer.Change(dueTime, period); } catch (Exception error) { facts.RecordError(error); throw; }
            }
            public void Dispose()
            {
                lock (gate)
                {
                    if (actualDisposal is not null) { return; }
                    try { actualDisposal = (actual ?? throw new InvalidOperationException("Actual System timer not published.")).DisposeAsync().AsTask(); }
                    catch (Exception error) { facts.RecordError(error); actualDisposal = Task.FromException(error); }
                }
                disposalRequested.TrySetResult();
            }
            public ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
            internal void JoinActualAndReleaseOnlyObserverReferences()
            {
                JoinCurrentUser(disposalRequested.Task); Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual System timer disposal task missing."); }
                JoinCurrentUser(drain); Interlocked.Increment(ref facts.actualDrainsCompleted);
                try { afterActualDrain?.Invoke(); } catch (Exception error) { facts.RecordError(error); throw; }
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}
