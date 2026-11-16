using System.Numerics;
using System.Runtime.CompilerServices;

namespace CIS.Heartbeat;

/// <summary>The application-owned shared Heartbeat engine. Construction and registration are inactive until hosting signals readiness.</summary>
/// <remarks>Use the hosting adapter and actual owner boundaries. This infrastructure object never disposes subscribers or replaces their scopes.</remarks>
public sealed class HeartbeatRuntime : IAsyncDisposable
{
    internal readonly object Gate = new();
    internal readonly TimeProvider Clock;
    private readonly long frequency;
    private readonly int capacity;
    private readonly IHeartbeatExecutor executor;
    private readonly ConditionalWeakTable<IHeartbeatSubscriber, HeartbeatBinding> bindings = new();
    private readonly List<HeartbeatRegistration> registrations = [];
    private readonly Queue<HeartbeatExecution> pending = new();
    private readonly HashSet<HeartbeatExecution> operations = [];
    private readonly HashSet<HeartbeatOwner> closingOwners = [];
    private readonly Queue<HeartbeatExecution> suppressionCleanup = new();
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<HeartbeatInfrastructureFault> infrastructureFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ITimer? timer;
    private bool running, activating, stopping, disposed, pumpScheduled, wakeRequested;
    private bool timerSettled = true;
    private bool suppressionScheduled;
    private long lastTimestamp;
    private int dispatched;
    private string? resourceFailure;

    /// <summary>Creates an inert engine with one validated copy of module settings.</summary>
    public HeartbeatRuntime(HeartbeatOptions? options = null)
        : this(HeartbeatOptions.Resolve(options ?? EnvironmentOptions.Heartbeat, System.Environment.GetEnvironmentVariable),
            TimeProvider.System, new HeartbeatThreadPoolExecutor()) { }

    internal HeartbeatRuntime(HeartbeatSettings settings, TimeProvider clock, IHeartbeatExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(executor);
        if (settings.MaxConcurrent is < 1 or > 16) { throw new ArgumentOutOfRangeException(nameof(settings)); }
        frequency = clock.TimestampFrequency;
        if (frequency <= 0) { throw new ArgumentException("The clock requires a positive timestamp frequency.", nameof(clock)); }
        Clock = clock;
        capacity = settings.MaxConcurrent;
        this.executor = executor;
    }

    /// <summary>Gets the first detached infrastructure fault. Subscriber failures are execution facts, not infrastructure failures.</summary>
    /// <remarks>This awaitable remains pending if no fault occurs; it retains no subscriber, scope or exception object.</remarks>
    public Task<HeartbeatInfrastructureFault> InfrastructureFailure => infrastructureFailure.Task;

    /// <summary>Creates an explicit lifetime owner forwarding to this engine.</summary>
    public HeartbeatOwner CreateOwner()
    {
        lock (Gate) { EnsureAdmission(); return new HeartbeatOwner(this); }
    }

    internal void Register(HeartbeatOwner owner, IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
        Func<CancellationToken, Task<bool>>? condition = null, Func<Guid, IHeartbeatObserver>? observerFactory = null,
        Func<ValueTask>? cleanup = null, CancellationToken executionCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(options);
        HeartbeatRegistrationOptions captured = options.ValidatedCopy();
        long interval = ToUnits(captured.Interval, nameof(options));
        long delay = ToUnits(captured.InitialDelay, nameof(options));
        HeartbeatRegistration entry;
        bool duplicate;
        lock (Gate)
        {
            EnsureAdmission();
            duplicate = bindings.TryGetValue(subscriber, out HeartbeatBinding? binding);
            if (duplicate) { entry = binding!.Registration; }
            else
            {
                ObjectDisposedException.ThrowIf(owner.Disposed, owner);
                if (owner.Closing) { throw new InvalidOperationException("The Heartbeat owner is closing."); }
                entry = new(subscriber, owner, captured, interval, delay);
                bindings.Add(subscriber, new(entry, condition, cleanup, executionCancellation));
                registrations.Add(entry);
                owner.Registrations++;
            }
        }
        if (duplicate)
        {
            // The Core binder is synchronous. Join a concurrent first registration
            // outside the gate without constructing another monitor or replacing options.
            entry.Initialization.Task.GetAwaiter().GetResult();
            return;
        }
        Exception? failure = null;
        IHeartbeatObserver? observer = null;
        try { observer = observerFactory?.Invoke(entry.Id); }
        catch (Exception error) { failure = error; }
        lock (Gate)
        {
            entry.Observer = observer;
            entry.Initialized = true;
            if (failure is not null || stopping || owner.Closing)
            {
                entry.Closing = true;
                failure ??= new InvalidOperationException("The registration owner or application closed during binding.");
            }
            else if (running)
            {
                SetOrigin(entry, ReadTimestamp(), captured.InitialDelay, delay);
                if (stopping) { entry.Closing = true; failure = new InvalidOperationException("The application stopped while capturing registration timing."); }
            }
        }
        if (failure is null)
        {
            Report(entry, new(HeartbeatEvent.Registered, entry.Id, ReadUtcNow()));
            entry.Initialization.TrySetResult();
        }
        else
        {
            entry.Initialization.TrySetException(failure);
            // Observe the shared failure even when no duplicate joined it.
            _ = entry.Initialization.Task.Exception;
        }
        if (running || entry.Closing) { Wake(); }
        if (failure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    internal Task UnregisterAsync(IHeartbeatSubscriber subscriber, CancellationToken waitCancellation)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        HeartbeatRegistration? entry;
        lock (Gate)
        {
            if (!bindings.TryGetValue(subscriber, out HeartbeatBinding? binding)) { return Task.CompletedTask; }
            entry = binding.Registration;
            entry.Closing = true;
        }
        Wake();
        HeartbeatExecutionContext.ThrowIfWaitingOn(this, registrationId: entry.Id);
        return entry.Completion.Task.IsCompleted ? entry.Completion.Task : entry.Completion.Task.WaitAsync(waitCancellation);
    }

    internal void CloseOwner(HeartbeatOwner owner)
    {
        lock (Gate)
        {
            owner.Closing = true;
            closingOwners.Add(owner);
            foreach (HeartbeatRegistration entry in registrations)
            {
                if (entry.Owner.TryGetTarget(out HeartbeatOwner? actual) && ReferenceEquals(actual, owner)) { entry.Closing = true; }
            }
            CompleteOwner(owner);
        }
        Wake();
    }

    internal void Activate()
    {
        lock (Gate)
        {
            if (running || activating || stopping || disposed) { return; }
            activating = true;
        }
        ITimer? created = null;
        Exception? failure = null;
        try
        {
            if (ExecutionContext.IsFlowSuppressed()) { created = CreateTimer(); }
            else { using (ExecutionContext.SuppressFlow()) { created = CreateTimer(); } }
        }
        catch (Exception error) { failure = error; }
        bool closeTimer;
        lock (Gate)
        {
            activating = false;
            closeTimer = stopping;
            if (!closeTimer && failure is null)
            {
                timer = created;
                running = true;
                long now = ReadTimestamp();
                foreach (HeartbeatRegistration entry in registrations.Where(item => item.Initialized && !item.Closing))
                { SetOrigin(entry, now, entry.Options.InitialDelay, entry.InitialDelayUnits); }
            }
            else if (created is not null) { timerSettled = false; }
        }
        if (closeTimer && created is not null) { _ = DisposeTimerAsync(created); }
        if (failure is not null) { FailInfrastructure("Heartbeat.TimerCreation", failure); BeginStopping(); }
        Wake();
    }

    // Startup must not publish success when activation was stopped or its timer
    // failed. This acknowledges infrastructure bookkeeping, never a user callback.
    internal bool ActivateForStartup()
    {
        Activate();
        lock (Gate)
        {
            return running && timer is not null && !activating && !stopping && !disposed
                && !infrastructureFailure.Task.IsCompleted;
        }
    }

    private ITimer CreateTimer() => Clock.CreateTimer(static value => ((HeartbeatRuntime)value!).Wake(), this,
        Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    /// <summary>Establishes terminal shutdown and awaits true settlement. Cancellation cancels only the caller's wait.</summary>
    public Task StopAsync(CancellationToken waitCancellation = default)
    {
        BeginStopping();
        HeartbeatExecutionContext.ThrowIfWaitingOn(this);
        return completion.Task.IsCompleted ? completion.Task : completion.Task.WaitAsync(waitCancellation);
    }

    /// <summary>Joins the shutdown barrier and releases scheduler resources.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (Gate) { disposed = true; }
    }

    internal void BeginStopping()
    {
        ITimer? oldTimer = null;
        lock (Gate)
        {
            if (!stopping)
            {
                stopping = true;
                running = false;
                foreach (HeartbeatRegistration entry in registrations) { entry.Closing = true; }
                oldTimer = timer;
                timer = null;
                if (oldTimer is not null) { timerSettled = false; }
            }
        }
        if (oldTimer is not null) { _ = DisposeTimerAsync(oldTimer); }
        Wake();
    }

    private async Task DisposeTimerAsync(ITimer oldTimer)
    {
        try { await oldTimer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error)
        {
            FailInfrastructure("Heartbeat.TimerDisposal", error);
            lock (Gate) { resourceFailure = "The scheduler timer's disposal could not be confirmed."; }
        }
        finally { lock (Gate) { timerSettled = true; } Wake(); }
    }

    private void Wake()
    {
        lock (Gate)
        {
            if (completion.Task.IsCompleted || disposed) { return; }
            if (pumpScheduled) { wakeRequested = true; return; }
            pumpScheduled = true;
        }
        if (!ThreadPool.UnsafeQueueUserWorkItem(static runtime => runtime.RunPump(), this, preferLocal: false))
        {
            // ThreadPool's rejected post has no wrapper that can later invoke work.
            lock (Gate) { pumpScheduled = false; resourceFailure = "The scheduling pump could not be posted."; }
            FailInfrastructure("Heartbeat.PumpDispatch", new InvalidOperationException(resourceFailure));
            lock (Gate)
            {
                stopping = true;
                running = false;
                foreach (HeartbeatRegistration entry in registrations) { entry.Closing = true; }
            }
        }
    }

    private void RunPump()
    {
        try { Pump(); }
        catch (Exception error)
        {
            FailInfrastructure("Heartbeat.SchedulingBoundary", error);
            lock (Gate) { pumpScheduled = false; resourceFailure = "Scheduler bookkeeping could not be fully confirmed."; }
            BeginStopping();
        }
    }

    private void Pump()
    {
        while (true)
        {
            var admitted = new List<HeartbeatExecution>();
            var launch = new List<HeartbeatExecution>();
            var withdrawn = new List<HeartbeatExecution>();
            var retire = new List<HeartbeatRegistration>();
            Exception? timerError = null;
            lock (Gate)
            {
                wakeRequested = false;
                long now = ReadTimestamp();
                foreach (HeartbeatRegistration entry in registrations)
                {
                    if (!entry.Initialized || entry.Retiring || entry.ReportingSettlement) { continue; }
                    if (entry.Current is null && (!entry.Target.TryGetTarget(out _) || !entry.Owner.TryGetTarget(out _)))
                    { entry.Closing = entry.Unusable = true; }
                    if (entry.Closing && entry.Current is null) { entry.Retiring = true; retire.Add(entry); continue; }
                    if (!running || entry.Current is not null || ElapsedUnits(entry.Origin, now) < entry.Delay) { continue; }
                    if (!entry.Target.TryGetTarget(out IHeartbeatSubscriber? target)
                        || !entry.Owner.TryGetTarget(out HeartbeatOwner? owner) || owner.Closing
                        || !bindings.TryGetValue(target, out HeartbeatBinding? binding))
                    { entry.Closing = entry.Unusable = entry.Retiring = true; retire.Add(entry); continue; }
                    if (entry.LastExecutionId == long.MaxValue)
                    { entry.Closing = entry.Retiring = true; retire.Add(entry); continue; }
                    var operation = new HeartbeatExecution(this, entry, binding, target, owner, ++entry.LastExecutionId, now);
                    if (stopping) { continue; }
                    owner.Holds++;
                    entry.Current = operation;
                    operations.Add(operation);
                    pending.Enqueue(operation);
                    admitted.Add(operation);
                }
                while (pending.Count != 0)
                {
                    if (stopping || pending.Peek().Registration.Unusable) { withdrawn.Add(pending.Dequeue()); continue; }
                    if (dispatched == capacity) { break; }
                    HeartbeatExecution next = pending.Peek();
                    pending.Dequeue();
                    next.HasSlot = true;
                    dispatched++;
                    launch.Add(next);
                }
                if (running && timer is not null)
                {
                    long minimum = frequency; // Weak-owner reconciliation is bounded independently of long intervals.
                    foreach (HeartbeatRegistration entry in registrations.Where(item => item.Initialized && !item.Closing && item.Current is null))
                    { minimum = Math.Min(minimum, Math.Max(1, entry.Delay - ElapsedUnits(entry.Origin, now))); }
                    try
                    {
                        if (!timer.Change(registrations.Count == 0 ? Timeout.InfiniteTimeSpan : FromUnits(minimum), Timeout.InfiniteTimeSpan))
                        { throw new InvalidOperationException("The scheduling timer rejected its next wake."); }
                    }
                    catch (Exception error) { timerError = error; }
                }
            }
            foreach (HeartbeatExecution operation in admitted) { operation.ReportAdmission(); }
            foreach (HeartbeatRegistration entry in retire) { _ = RetireAsync(entry); }
            foreach (HeartbeatExecution operation in withdrawn) { QueueSuppressionCleanup(operation); }
            foreach (HeartbeatExecution operation in launch)
            {
                bool posted;
                try { posted = executor.TryPost(() => Start(operation)); }
                catch (Exception error) { FailInfrastructure("Heartbeat.ExecutionDispatch", error); posted = false; }
                if (posted)
                {
                    operation.MarkDispatched(ReadTimestamp());
                    operation.ReportDispatch();
                    operation.PublishDispatch();
                }
                else
                {
                    FailInfrastructure("Heartbeat.ExecutionDispatch", new InvalidOperationException("The executor rejected a wrapper."));
                    BeginStopping();
                    operation.ClearDispatch();
                    operation.PublishDispatch();
                    QueueSuppressionCleanup(operation);
                }
            }
            if (timerError is not null) { FailInfrastructure("Heartbeat.TimerChange", timerError); BeginStopping(); }
            lock (Gate)
            {
                if (wakeRequested) { continue; }
                pumpScheduled = false;
                CompleteStop();
                return;
            }
        }
    }

    private void QueueSuppressionCleanup(HeartbeatExecution operation)
    {
        lock (Gate)
        {
            suppressionCleanup.Enqueue(operation);
            if (suppressionScheduled) { return; }
            suppressionScheduled = true;
        }
        // One infrastructure continuation starts already-withdrawn cleanup tasks;
        // it never dispatches a condition/callback or runs on the scheduling pump.
        if (!ThreadPool.UnsafeQueueUserWorkItem(static runtime => runtime.DrainSuppressionCleanup(), this, preferLocal: false))
        { FailInfrastructure("Heartbeat.SuppressionCleanup", new InvalidOperationException("Suppression cleanup could not be posted; ownership remains live.")); }
    }

    private void DrainSuppressionCleanup()
    {
        while (true)
        {
            HeartbeatExecution operation;
            lock (Gate)
            {
                if (suppressionCleanup.Count == 0) { suppressionScheduled = false; break; }
                operation = suppressionCleanup.Dequeue();
            }
            Start(operation);
        }
        Wake();
    }

    private void Start(HeartbeatExecution operation)
    {
        if (Interlocked.Exchange(ref operation.WrapperStarted, 1) != 0) { return; }
        Task task = operation.RunAsync();
        _ = task.ContinueWith(static (finished, value) =>
        {
            var pair = ((HeartbeatRuntime Runtime, HeartbeatExecution Operation))value!;
            if (finished.Exception is Exception error) { pair.Runtime.FailInfrastructure("Heartbeat.ExecutionBoundary", error); }
            pair.Runtime.Settle(pair.Operation);
        }, (this, operation), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal bool TryStart(HeartbeatExecution operation)
    {
        lock (Gate) { return !stopping && !operation.Registration.Unusable; }
    }

    internal void Retire(HeartbeatRegistration entry)
    {
        lock (Gate) { entry.Closing = true; }
    }

    private void Settle(HeartbeatExecution operation)
    {
        HeartbeatNotification settled;
        lock (Gate)
        {
            HeartbeatRegistration entry = operation.Registration;
            HeartbeatOwner owner = operation.Owner!;
            settled = operation.DetachSettlement(ReadTimestamp());
            // RunAsync is actually complete. Drop every transient target/error graph
            // before releasing ownership; the later settlement report is detached.
            operation.Release();
            owner.Holds--;
            if (operation.HasSlot) { dispatched--; }
            operations.Remove(operation);
            entry.Current = null;
            entry.ReportingSettlement = true;
            if (!entry.Closing && !stopping) { SetOrigin(entry, ReadTimestamp(), entry.Options.Interval, entry.IntervalUnits); }
            CompleteOwner(owner);
        }
        Report(operation.Registration, settled);
        lock (Gate) { operation.Registration.ReportingSettlement = false; }
        Wake();
    }

    private async Task RetireAsync(HeartbeatRegistration entry)
    {
        try
        {
            Report(entry, new(HeartbeatEvent.Retired, entry.Id, ReadUtcNow()));
            if (entry.Observer is not null) { await entry.Observer.DisposeAsync().ConfigureAwait(false); }
        }
        catch (Exception error) { FailInfrastructure("Heartbeat.BindingRetirement", error); }
        finally
        {
            lock (Gate)
            {
                entry.Observer = null;
                if (entry.Target.TryGetTarget(out IHeartbeatSubscriber? target)
                    && bindings.TryGetValue(target, out HeartbeatBinding? binding) && ReferenceEquals(binding.Registration, entry))
                { bindings.Remove(target); }
                registrations.Remove(entry);
                if (entry.Owner.TryGetTarget(out HeartbeatOwner? owner)) { owner.Registrations--; CompleteOwner(owner); }
                entry.Completion.TrySetResult();
            }
            Wake();
        }
    }

    internal void Report(HeartbeatRegistration entry, HeartbeatNotification notification)
    {
        try { entry.Observer?.Report(notification); }
        catch (Exception error) { FailInfrastructure("Heartbeat.Reporting", error); }
    }

    internal async ValueTask FlushAsync(HeartbeatRegistration entry)
    {
        try { if (entry.Observer is not null) { await entry.Observer.FlushAsync().ConfigureAwait(false); } }
        catch (Exception error) { FailInfrastructure("Heartbeat.Reporting", error); }
    }

    internal void FailInfrastructure(string code, Exception error)
        => infrastructureFailure.TrySetResult(new HeartbeatInfrastructureFault(code, error));

    private void EnsureAdmission()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stopping) { throw new InvalidOperationException("Heartbeat is stopping or stopped."); }
    }

    private void SetOrigin(HeartbeatRegistration entry, long timestamp, TimeSpan delay, long units)
    {
        entry.Origin = timestamp;
        entry.OriginUtc = ReadUtcNow();
        entry.EffectiveDelay = delay;
        entry.Delay = units;
    }

    private void CompleteOwner(HeartbeatOwner owner)
    {
        if (owner.Closing && owner.Registrations == 0 && owner.Holds == 0)
        { closingOwners.Remove(owner); owner.Completion.TrySetResult(); }
    }

    private void CompleteStop()
    {
        if (!stopping || activating || !timerSettled || registrations.Count != 0 || operations.Count != 0 || pumpScheduled || suppressionScheduled) { return; }
        if (resourceFailure is null) { completion.TrySetResult(); }
        else { completion.TrySetException(new InvalidOperationException(resourceFailure)); }
    }

    private long ToUnits(TimeSpan duration, string parameter)
    {
        BigInteger units = ((BigInteger)duration.Ticks * frequency + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond;
        if (units > long.MaxValue) { throw new ArgumentOutOfRangeException(parameter, "The duration exceeds this clock's supported elapsed range."); }
        return (long)units;
    }

    internal long ElapsedUnits(long origin, long now) => Math.Max(0, unchecked(now - origin));
    internal double Milliseconds(long origin, long now) => (double)ElapsedUnits(origin, now) * 1000 / frequency;
    private TimeSpan FromUnits(long units)
    {
        Int128 ticks = ((Int128)units * TimeSpan.TicksPerSecond + frequency - 1) / frequency;
        return TimeSpan.FromTicks((long)Int128.Min(HeartbeatRegistrationOptions.MaximumDuration.Ticks, ticks));
    }

    internal HeartbeatSchedulerCounts Inspect()
    {
        lock (Gate) { return new(registrations.Count, operations.Count, dispatched, pending.Count); }
    }

    internal long ReadTimestamp()
    {
        try { long value = Clock.GetTimestamp(); Interlocked.Exchange(ref lastTimestamp, value); return value; }
        catch (Exception error) { FailInfrastructure("Heartbeat.Clock", error); BeginStopping(); return Interlocked.Read(ref lastTimestamp); }
    }

    internal DateTimeOffset ReadUtcNow()
    {
        try { return Clock.GetUtcNow(); }
        catch (Exception error) { FailInfrastructure("Heartbeat.Clock", error); BeginStopping(); return DateTimeOffset.UtcNow; }
    }
}
