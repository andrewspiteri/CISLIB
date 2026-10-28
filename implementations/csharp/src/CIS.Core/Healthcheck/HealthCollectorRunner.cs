using System.Numerics;
using System.Runtime.CompilerServices;
using CIS.Healthcheck.Internal;

namespace CIS.Healthcheck;

/// <summary>Runs explicitly requested, owner-protected health evaluations with bounded concurrency.</summary>
/// <remarks>Construction and registration are inert. Owners retain their registration handles and explicitly activate periodic collection.</remarks>
public sealed class HealthCollectorRunner : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IHealthCollectionState state;
    private readonly TimeProvider clock;
    private readonly long frequency;
    private readonly int capacity;
    private readonly Guid runnerId = Guid.NewGuid();
    private readonly List<RegistrationEntry> entries = [];
    private readonly HashSet<Operation> operations = [];
    private readonly TaskCompletionSource startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource stopCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration stoppingRegistration;
    private ITimer? timer;
    private bool started, starting, stopping, disposed, pumpScheduled, wakeRequested;
    private bool timerSettled = true;
    private int cursor, registering, closing;
    private HealthDiagnostic? resourceError;

    /// <summary>Claims the service's sole collector runner without starting a dispatcher or evaluation.</summary>
    public HealthCollectorRunner(HealthcheckService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        state = ((IHealthStateAccess)service).CollectionState;
        clock = service.Clock;
        frequency = clock.TimestampFrequency;
        if (frequency <= 0) { throw new ArgumentException("The clock must have a positive timestamp frequency.", nameof(service)); }
        capacity = service.Settings.MaxConcurrentCollections;
        service.ClaimCollectorRunner();
        stoppingRegistration = state.Stopping.UnsafeRegister(static value => ((HealthCollectorRunner)value!).BeginStop(), this);
    }

    /// <summary>Enables dispatch once. A pre-canceled call leaves the runner unstarted.</summary>
    public Task StartAsync(CancellationToken waitCancellation = default)
    {
        if (waitCancellation.IsCancellationRequested) { return Task.FromCanceled(waitCancellation); }
        lock (gate)
        {
            EnsureAdmission();
            if (started) { return startCompletion.Task.WaitAsync(waitCancellation); }
            started = starting = true;
        }
        ITimer? created = null;
        bool transferred = false;
        try
        {
            created = clock.CreateTimer(static value => ((HealthCollectorRunner)value!).Wake(), this,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            bool closeTimer;
            lock (gate)
            {
                starting = false;
                closeTimer = stopping;
                if (!closeTimer) { timer = created; transferred = true; }
                timerSettled = !closeTimer;
            }
            if (closeTimer) { transferred = true; _ = DisposeTimerAsync(created); }
            startCompletion.TrySetResult();
            Wake();
        }
        catch (Exception exception)
        {
            ITimer? failedTimer = null;
            lock (gate)
            {
                starting = false;
                if (!transferred)
                {
                    if (created is null) { timerSettled = true; }
                    // A returned timer still belongs to startup if publication
                    // failed. Transfer it once to stop, or retain the existing
                    // stop barrier until its actual disposal settles.
                    else if (!stopping) { timer = created; }
                    else { timerSettled = false; failedTimer = created; }
                }
            }
            startCompletion.TrySetException(new InvalidOperationException(HealthDiagnostic.FromException(exception).Message));
            BeginStop();
            if (failedTimer is not null) { _ = DisposeTimerAsync(failedTimer); }
        }
        SignalProgress();
        return startCompletion.Task.WaitAsync(waitCancellation);
    }

    /// <summary>Attaches one inert collector to an active monitor belonging to this service.</summary>
    public IHealthCollectionRegistration Register(IHealthMonitor monitor, IHealthCollector collector, HealthCollectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(options);
        if (monitor is not IHealthStateAccess access || !ReferenceEquals(access.CollectionState, state))
        { throw new ArgumentException("The monitor must belong to this runner's service.", nameof(monitor)); }
        long intervalUnits = 0;
        if (options.Interval is TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero || interval.TotalMilliseconds > uint.MaxValue - 1)
            { throw new ArgumentException("Supply a positive, representable collection interval.", nameof(options)); }
            BigInteger units = ((BigInteger)interval.Ticks * frequency + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond;
            if (units > long.MaxValue) { throw new ArgumentException("The interval exceeds the clock's timestamp range.", nameof(options)); }
            intervalUnits = (long)units;
            _ = checked(clock.GetTimestamp() + intervalUnits);
        }
        lock (gate) { EnsureAdmission(); registering++; }
        RegistrationEntry? entry = null;
        bool published = false;
        try
        {
            var target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), options);
            entry = new RegistrationEntry(target, intervalUnits);
            var handle = new Registration(this, entry, monitor, collector);
            entry.Handle = new(handle);
            lock (gate)
            {
                if (!stopping && !state.Stopping.IsCancellationRequested)
                { entries.Add(entry); published = true; }
                else { entry.Closed = true; closing++; }
            }
            if (!published)
            {
                _ = RetireAsync(entry);
                throw new InvalidOperationException("The collector runner is stopping.");
            }
            var subscription = target.Retirement.UnsafeRegister(static value =>
            {
                var item = ((HealthCollectorRunner Runner, RegistrationEntry Entry))value!;
                item.Runner.Withdraw(item.Entry);
            }, (this, entry));
            bool alreadySettled;
            lock (gate) { entry.RetirementRegistration = subscription; alreadySettled = entry.StateSettled; }
            if (alreadySettled) { subscription.Dispose(); }
            return handle;
        }
        finally
        {
            lock (gate) { registering--; }
            SignalProgress();
            GC.KeepAlive(monitor);
        }
    }

    /// <summary>Closes admission and awaits actual evaluations, owner leases and runner resources. Cancellation affects only this wait.</summary>
    public Task StopAsync(CancellationToken waitCancellation = default)
    {
        BeginStop();
        HealthCollectionExecution.ThrowIfSelfWait(state, runnerId: runnerId);
        return stopCompletion.Task.WaitAsync(waitCancellation);
    }

    /// <summary>Performs terminal stop and releases the service-stopping subscription.</summary>
    public async ValueTask DisposeAsync()
    {
        BeginStop();
        HealthCollectionExecution.ThrowIfSelfWait(state, runnerId: runnerId);
        await stopCompletion.Task.ConfigureAwait(false);
        stoppingRegistration.Dispose();
        lock (gate) { disposed = true; }
    }

    private void EnsureAdmission()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopping || state.Stopping.IsCancellationRequested) { throw new InvalidOperationException("The collector runner is stopping."); }
    }

    private void Activate(RegistrationEntry entry)
    {
        long now = clock.GetTimestamp();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(entry.Closed, typeof(IHealthCollectionRegistration));
            EnsureAdmission();
            if (!started) { throw new InvalidOperationException("Start the collector runner first."); }
            if (entry.Target.Interval is null) { throw new InvalidOperationException("Periodic collection requires an explicit interval."); }
            if (entry.Periodic) { return; }
            _ = checked(now + entry.IntervalUnits);
            entry.Periodic = true;
            if (entry.Current is null) { entry.Due = now; }
        }
        Wake();
    }

    private Task<HealthCollectionAttempt> Collect(RegistrationEntry entry, CancellationToken waitCancellation)
    {
        if (waitCancellation.IsCancellationRequested) { return Task.FromCanceled<HealthCollectionAttempt>(waitCancellation); }
        HealthCollectionExecution.ThrowIfSelfWait(state, collectionRegistrationId: entry.Target.CollectionRegistrationId);
        Task<HealthCollectionAttempt> task;
        lock (gate)
        {
            if (entry.Closed) { return Task.FromResult(Retired()); }
            EnsureAdmission();
            if (!started) { throw new InvalidOperationException("Start the collector runner first."); }
            if (entry.Current is not null) { task = entry.Current.Completion.Task; }
            else
            {
                entry.Pending = true;
                entry.ManualCompletion ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                task = entry.ManualCompletion.Task;
            }
        }
        Wake();
        return task.WaitAsync(waitCancellation);
    }

    private void Withdraw(RegistrationEntry entry)
    {
        TaskCompletionSource<HealthCollectionAttempt>? pending;
        lock (gate)
        {
            if (entry.Closed) { return; }
            pending = CloseLocked(entry);
        }
        pending?.TrySetResult(Retired());
        _ = RetireAsync(entry);
        Wake();
    }

    private TaskCompletionSource<HealthCollectionAttempt>? CloseLocked(RegistrationEntry entry)
    {
        entry.Closed = true;
        entry.Pending = entry.Periodic = false;
        entry.Due = null;
        int index = entries.IndexOf(entry);
        if (index >= 0)
        {
            entries.RemoveAt(index);
            if (index < cursor) { cursor--; }
            if (cursor >= entries.Count) { cursor = 0; }
        }
        var pending = entry.ManualCompletion;
        entry.ManualCompletion = null;
        closing++;
        return pending;
    }

    private async Task RetireAsync(RegistrationEntry entry)
    {
        try { await state.WithdrawCollection(entry.Target).ConfigureAwait(false); }
        catch (Exception exception)
        {
            HealthDiagnostic diagnostic = HealthDiagnostic.FromException(exception);
            lock (gate) { resourceError ??= diagnostic; }
        }
        finally
        {
            lock (gate) { entry.StateSettled = true; closing--; }
            entry.RetirementRegistration.Dispose();
            SignalProgress(entry);
        }
    }

    private void BeginStop()
    {
        RegistrationEntry[] retiring;
        var pending = new List<TaskCompletionSource<HealthCollectionAttempt>>();
        ITimer? retiringTimer;
        lock (gate)
        {
            if (stopping) { return; }
            stopping = true;
            retiring = entries.ToArray();
            foreach (var entry in retiring)
            {
                var waiter = CloseLocked(entry);
                if (waiter is not null) { pending.Add(waiter); }
            }
            retiringTimer = timer;
            timer = null;
            timerSettled = retiringTimer is null && !starting;
        }
        foreach (var waiter in pending) { waiter.TrySetResult(Retired()); }
        foreach (var entry in retiring) { _ = RetireAsync(entry); }
        if (retiringTimer is not null) { _ = DisposeTimerAsync(retiringTimer); }
        SignalProgress();
    }

    private async Task DisposeTimerAsync(ITimer retiringTimer)
    {
        try { await retiringTimer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception)
        {
            HealthDiagnostic diagnostic = HealthDiagnostic.FromException(exception);
            lock (gate) { resourceError ??= diagnostic; }
        }
        finally
        {
            lock (gate) { timerSettled = true; }
            SignalProgress();
        }
    }

    private void SignalProgress(RegistrationEntry? entry = null)
    {
        bool entrySettled, runnerSettled;
        HealthDiagnostic? error;
        lock (gate)
        {
            entrySettled = entry is { Closed: true, StateSettled: true, Current: null };
            runnerSettled = stopping && !starting && !pumpScheduled && timerSettled && registering == 0 && closing == 0 && operations.Count == 0;
            error = resourceError;
        }
        if (entrySettled) { entry!.Disposal.TrySetResult(); }
        if (runnerSettled)
        {
            if (error is null) { stopCompletion.TrySetResult(); }
            else { stopCompletion.TrySetException(new InvalidOperationException(error.Message)); }
        }
    }

    private void Wake()
    {
        lock (gate)
        {
            if (!started || stopping) { return; }
            if (pumpScheduled) { wakeRequested = true; return; }
            pumpScheduled = true;
        }
        // Never capture the requesting component's arbitrary ExecutionContext in the singleton dispatcher.
        ThreadPool.UnsafeQueueUserWorkItem(static runner => runner.Pump(), this, preferLocal: false);
    }

    private void Pump()
    {
        try
        {
            while (true)
            {
                long now = clock.GetTimestamp();
                var dead = new List<RegistrationEntry>();
                var abandoned = new List<TaskCompletionSource<HealthCollectionAttempt>>();
                Operation? operation = null;
                ITimer? currentTimer;
                TimeSpan delay = Timeout.InfiniteTimeSpan;
                lock (gate)
                {
                    wakeRequested = false;
                    if (stopping) { pumpScheduled = false; break; }
                    operation = SelectOperationLocked(now, dead, abandoned, out delay);
                    currentTimer = timer;
                }
                foreach (var waiter in abandoned) { waiter.TrySetResult(Retired()); }
                foreach (var entry in dead) { _ = RetireAsync(entry); }
                if (operation is not null) { AcquireAndPost(operation); continue; }
                try { currentTimer?.Change(delay, Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException)
                {
                    lock (gate) { if (!stopping) { throw; } }
                }
                lock (gate)
                {
                    if (wakeRequested && !stopping) { continue; }
                    // Release dispatcher ownership in the same decision that
                    // observes no further wake. A later requester queues its
                    // own dispatcher; this one never clears that new ownership.
                    pumpScheduled = false;
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            HealthDiagnostic diagnostic = HealthDiagnostic.FromException(exception);
            lock (gate) { resourceError ??= diagnostic; }
            BeginStop();
            // BeginStop has closed admission, so no new dispatcher can own this
            // flag while the faulted dispatcher releases it.
            lock (gate) { pumpScheduled = false; }
        }
        SignalProgress();
    }

    // End the transient strong-handle snapshot before calling any owner, timer
    // or external clock. Only the chosen operation retains its actual owner.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Operation? SelectOperationLocked(long now, List<RegistrationEntry> dead,
        List<TaskCompletionSource<HealthCollectionAttempt>> abandoned, out TimeSpan delay)
    {
        delay = Timeout.InfiniteTimeSpan;
        List<(RegistrationEntry Entry, Registration Handle)> live = new(entries.Count);
        for (int index = 0; index < entries.Count;)
        {
            RegistrationEntry entry = entries[index];
            if (!entry.Handle.TryGetTarget(out Registration? handle))
            {
                var pending = CloseLocked(entry);
                if (pending is not null) { abandoned.Add(pending); }
                dead.Add(entry);
            }
            else
            {
                if (entry.Periodic && entry.Current is null && entry.Due <= now) { entry.Pending = true; }
                live.Add((entry, handle));
                index++;
            }
        }
        if (operations.Count >= capacity) { return null; }
        // The linear sweep preserves entry order and adjusts cursor on every
        // removal. Selection does not mutate either ordered collection.
        for (int examined = 0; examined < live.Count; examined++)
        {
            int index = (cursor + examined) % live.Count;
            var (entry, handle) = live[index];
            if (!entry.Pending || entry.Current is not null) { continue; }
            var operation = new Operation(entry, handle, entry.ManualCompletion ?? new(TaskCreationOptions.RunContinuationsAsynchronously));
            entry.ManualCompletion = null;
            entry.Pending = false;
            entry.Due = null;
            entry.Current = operation;
            operations.Add(operation);
            cursor = (index + 1) % live.Count;
            return operation;
        }
        // Every due live entry was marked pending and would have been selected.
        // With capacity available and no selection, only future due times remain.
        long? next = entries.Where(entry => entry.Periodic && entry.Current is null).Select(entry => entry.Due).Min();
        if (next.HasValue) { delay = TimerDelay((BigInteger)next.Value - now); }
        return null;
    }

    private TimeSpan TimerDelay(BigInteger units)
    {
        BigInteger ticks = (units * TimeSpan.TicksPerSecond + frequency - 1) / frequency;
        long bounded = (long)BigInteger.Min(ticks, (BigInteger)(uint.MaxValue - 1) * TimeSpan.TicksPerMillisecond);
        return TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond, bounded));
    }

    private void AcquireAndPost(Operation operation)
    {
        try
        {
            var reserved = state.TryReserve(operation.Entry.Target);
            operation.Reservation = reserved.Reservation;
            if (reserved.Disposition != HealthCollectionReservationDisposition.Reserved)
            {
                operation.Joined = reserved.Disposition == HealthCollectionReservationDisposition.Existing;
                operation.Attempt = Retired();
                Post(operation);
                return;
            }
            using (HealthCollectionExecution.Enter(state, operation.Entry.Target.MonitorId,
                operation.Entry.Target.CollectionRegistrationId, runnerId))
            {
                bool acquired = operation.Handle!.Collector.TryAcquire(out operation.Lease);
                if (!acquired && operation.Lease is null) { operation.Reason = HealthCollectionCompletionReason.OwnerUnavailable; }
                else if (!acquired || operation.Lease is null)
                {
                    operation.Reason = HealthCollectionCompletionReason.LeaseAcquisitionFault;
                    operation.AcquisitionError = new("Healthcheck.LeaseProtocol", "The owner returned an inconsistent acquisition result and lease.");
                }
            }
        }
        catch (Exception exception)
        {
            operation.Reason = HealthCollectionCompletionReason.LeaseAcquisitionFault;
            operation.AcquisitionError = HealthDiagnostic.FromException(exception, "Healthcheck.LeaseAcquisition");
        }
        Post(operation);
    }

    private void Post(Operation operation) => ThreadPool.UnsafeQueueUserWorkItem(
        static item =>
        {
            // Publish the actual execution task only after its cleanup and
            // execution scope have exited. A bookkeeping fault must fault the
            // caller while retaining the operation's unsettled ownership.
            _ = item.Runner.ExecuteAsync(item.Operation).ContinueWith(
                static (completed, value) => ((Operation)value!).Completion.TrySetFromTask(completed),
                item.Operation, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }, (Runner: this, Operation: operation), preferLocal: false);

    private async Task<HealthCollectionAttempt> ExecuteAsync(Operation operation)
    {
        var target = operation.Entry.Target;
        using (HealthCollectionExecution.Enter(state, target.MonitorId, target.CollectionRegistrationId, runnerId))
        {
            if (operation.Joined && operation.Reservation is not null)
            {
                while (true)
                {
                    try
                    {
                        await state.WaitForSettlementAsync(operation.Reservation).ConfigureAwait(false);
                        break;
                    }
                    catch (ThreadInterruptedException)
                    {
                        // Joining retains the runner's slot until the existing
                        // state-owned operation actually settles too.
                    }
                }
            }
            HealthDiagnostic? cleanupError = null;
            try
            {
                if (!operation.Joined && operation.Reservation is not null && operation.Reason == HealthCollectionCompletionReason.Settled)
                {
                    if (!state.TryEnter(operation.Reservation)) { operation.Reason = HealthCollectionCompletionReason.RetiredBeforeInvocation; }
                    else
                    {
                        try
                        {
                            Task<HealthCollectionResult>? evaluation = operation.Lease!.CollectAsync(operation.Reservation.LifetimeCancellation);
                            if (evaluation is null) { throw new InvalidOperationException("The owner returned no evaluation task."); }
                            HealthCollectionResult result = await evaluation.ConfigureAwait(false);
                            operation.Attempt = state.CommitResult(operation.Reservation, result);
                        }
                        catch (OperationCanceledException) when (operation.Reservation.LifetimeCancellation.IsCancellationRequested) { }
                        catch (Exception exception)
                        {
                            operation.Attempt = state.CommitEvaluationFailure(operation.Reservation,
                                HealthDiagnostic.FromException(exception, "Healthcheck.CollectionEvaluation"));
                        }
                    }
                }
            }
            catch (Exception exception) { cleanupError = HealthDiagnostic.FromException(exception, "Healthcheck.CollectionIntegration"); }
            finally
            {
                if (operation.Lease is not null)
                {
                    try { await operation.Lease.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception exception) { cleanupError = HealthDiagnostic.FromException(exception, "Healthcheck.LeaseCleanup"); }
                }
                if (operation.Reservation is not null && !operation.Joined)
                {
                    DateTimeOffset completedAt;
                    try { completedAt = clock.GetUtcNow(); }
                    catch (Exception exception)
                    {
                        completedAt = DateTimeOffset.UtcNow;
                        cleanupError ??= HealthDiagnostic.FromException(exception, "Healthcheck.CollectionClock");
                    }
                    while (true)
                    {
                        try
                        {
                            operation.Attempt = state.Complete(operation.Reservation,
                                new(operation.Reason, completedAt, operation.AcquisitionError, cleanupError));
                            await state.WaitForSettlementAsync(operation.Reservation).ConfigureAwait(false);
                            break;
                        }
                        catch (ThreadInterruptedException)
                        {
                            // Completion is idempotent. Interruption while waiting
                            // for the coordinator gate does not release ownership
                            // of its actual operation or the runner's capacity.
                        }
                    }
                }
                bool retire = false;
                long now = 0;
                try { now = clock.GetTimestamp(); }
                catch (Exception exception)
                {
                    retire = true;
                    var previous = operation.Attempt ?? Retired();
                    operation.Attempt = new(previous.Disposition, previous.EvaluationFailed,
                        previous.Diagnostic ?? HealthDiagnostic.FromException(exception, "Healthcheck.CollectionClock"));
                }
                lock (gate)
                {
                    if (!retire && operation.Entry.Periodic && !operation.Entry.Closed)
                    {
                        try { operation.Entry.Due = checked(now + operation.Entry.IntervalUnits); }
                        catch (OverflowException) { retire = true; }
                    }
                }
                if (operation.Attempt?.Disposition == HealthCollectionDisposition.OwnerUnavailable) { retire = true; }
                if (retire) { Withdraw(operation.Entry); }
            }
        }
        // End the component's execution scope before making settled ownership
        // visible to this or any concurrent progress publisher.
        operation.Handle = null;
        operation.Lease = null;
        lock (gate)
        {
            operation.Entry.Current = null;
            operations.Remove(operation);
        }
        SignalProgress(operation.Entry);
        Wake();
        return operation.Attempt ?? Retired();
    }

    private static HealthCollectionAttempt Retired() => new(HealthCollectionDisposition.Retired, false, null);

    private sealed class RegistrationEntry(HealthCollectionTarget target, long intervalUnits)
    {
        internal readonly HealthCollectionTarget Target = target;
        internal readonly long IntervalUnits = intervalUnits;
        internal WeakReference<Registration> Handle = null!;
        internal bool Closed, StateSettled, Periodic, Pending;
        internal long? Due;
        internal Operation? Current;
        internal CancellationTokenRegistration RetirementRegistration;
        internal TaskCompletionSource<HealthCollectionAttempt>? ManualCompletion;
        internal readonly TaskCompletionSource Disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Operation(RegistrationEntry entry, Registration handle, TaskCompletionSource<HealthCollectionAttempt> completion)
    {
        internal readonly RegistrationEntry Entry = entry;
        internal readonly TaskCompletionSource<HealthCollectionAttempt> Completion = completion;
        internal Registration? Handle = handle;
        internal HealthCollectionReservation? Reservation;
        internal IHealthCollectionLease? Lease;
        internal HealthCollectionCompletionReason Reason;
        internal HealthDiagnostic? AcquisitionError;
        internal HealthCollectionAttempt? Attempt;
        internal bool Joined;
    }

    private sealed class Registration(HealthCollectorRunner runner, RegistrationEntry entry, IHealthMonitor monitor,
        IHealthCollector collector) : IHealthCollectionRegistration
    {
        internal IHealthCollector Collector { get; } = collector;
        // Strong ownership belongs only to the caller's handle and to an actually reserved operation.
        private readonly IHealthMonitor retainedMonitor = monitor;
        public Guid MonitorId => entry.Target.MonitorId;
        public void ActivatePeriodic()
        {
            try { runner.Activate(entry); }
            finally { GC.KeepAlive(this); }
        }
        public Task<HealthCollectionAttempt> CollectAsync(CancellationToken waitCancellation = default)
        {
            try { return runner.Collect(entry, waitCancellation); }
            finally { GC.KeepAlive(this); }
        }
        public void Withdraw()
        {
            try { runner.Withdraw(entry); }
            finally { GC.KeepAlive(this); }
        }
        public Task WaitForDisposalAsync(CancellationToken waitCancellation = default)
        {
            HealthCollectionExecution.ThrowIfSelfWait(runner.state, collectionRegistrationId: entry.Target.CollectionRegistrationId);
            return entry.Disposal.Task.WaitAsync(waitCancellation);
        }
        public ValueTask DisposeAsync()
        {
            Withdraw();
            HealthCollectionExecution.ThrowIfSelfWait(runner.state, collectionRegistrationId: entry.Target.CollectionRegistrationId);
            GC.KeepAlive(retainedMonitor);
            return new(entry.Disposal.Task);
        }
    }
}
