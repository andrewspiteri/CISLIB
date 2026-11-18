using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Healthcheck;

namespace CIS.CommonTesting;

/// <summary>The actual manually invoked phase result, without production scheduling or policy evaluation.</summary>
public enum ManualHeartbeatOutcome
{
    /// <summary>The callback returned true.</summary>
    Succeeded,
    /// <summary>The callback returned false or a user phase failed.</summary>
    Failed,
    /// <summary>The condition returned false and no callback ran.</summary>
    Skipped,
    /// <summary>A matching, canceled execution token ended a user phase.</summary>
    ExpectedCancellation
}

/// <summary>One completed manual invocation. No result history is retained by the helper.</summary>
/// <param name="Outcome">The actual result.</param>
/// <param name="CallbackResult">The returned callback boolean, if invoked successfully.</param>
/// <param name="Error">The original unexpected phase error.</param>
/// <param name="ErrorHandlingError">A separate error from awaited OnErrorAsync.</param>
/// <param name="ReportingError">A separate public monitor reporting error.</param>
public sealed record ManualHeartbeatResult(ManualHeartbeatOutcome Outcome, bool? CallbackResult = null,
    Exception? Error = null, Exception? ErrorHandlingError = null, Exception? ReportingError = null);

/// <summary>Inspects one active owner-bound registration; snapshots retain their own caller-owned references.</summary>
public sealed class ManualHeartbeatRegistration
{
    internal readonly ManualHeartbeat.Owner Owner;
    internal readonly Func<CancellationToken, Task<bool>>? Condition;
    internal readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task? Active;
    internal bool Initialized;
    internal bool Closing;
    internal bool Retiring;
    internal bool OwnsMonitor;
    internal IHealthMonitor? CurrentMonitor;

    internal ManualHeartbeatRegistration(ManualHeartbeat.Owner owner, IHeartbeatSubscriber subscriber,
        HeartbeatRegistrationOptions options, HeartbeatHealthcheckOptions? monitoring, Func<CancellationToken, Task<bool>>? condition)
    {
        Owner = owner;
        Subscriber = subscriber;
        Options = options;
        Monitoring = monitoring;
        Condition = condition;
    }
    /// <summary>Gets the exact subscriber, compared by reference.</summary>
    public IHeartbeatSubscriber Subscriber
    {
        get;
    }
    /// <summary>Gets copied immutable schedule inputs; no timer uses them.</summary>
    public HeartbeatRegistrationOptions Options
    {
        get;
    }
    /// <summary>Gets the selected public monitoring inputs.</summary>
    public HeartbeatHealthcheckOptions? Monitoring
    {
        get;
    }
    /// <summary>Gets the supplied or publicly created monitor.</summary>
    public IHealthMonitor? Monitor => Volatile.Read(ref CurrentMonitor);
    /// <summary>Gets whether admission for this registration has closed.</summary>
    public bool IsClosing => Volatile.Read(ref Closing);
}

/// <summary>Explicitly invokes registered work without timers, weak ownership, capacity queues or a production scheduler.</summary>
/// <remarks>One invocation per registration can run at a time. Close/unregister join actual callbacks,
/// OnError and owned-monitor retirement; they never dispose subscribers or cancel accepted work.</remarks>
public sealed class ManualHeartbeat : IHeartbeatOwnerFactory, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly List<ManualHeartbeatRegistration> registrations = [];
    private readonly HashSet<Owner> owners = [];
    private readonly AsyncLocal<ManualHeartbeatRegistration?> executing = new();
    private readonly int capacity;
    private Task? disposal;

    /// <summary>Bounds active owners and registrations. Closed registrations are released, not archived.</summary>
    public ManualHeartbeat(int capacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 65_536);
        this.capacity = capacity;
    }
    /// <summary>Gets a detached snapshot of active/retiring registrations.</summary>
    public IReadOnlyList<ManualHeartbeatRegistration> Registrations
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(registrations.ToArray());
            }
        }
    }
    /// <inheritdoc />
    public IHeartbeatOwner CreateOwner()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            if (owners.Count == capacity)
            {
                throw new InvalidOperationException("The manual owner capacity has been reached.");
            }
            var owner = new Owner(this);
            owners.Add(owner);
            return owner;
        }
    }

    private void Register(Owner owner, IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
        HeartbeatHealthcheckOptions? monitoring, Func<CancellationToken, Task<bool>>? condition)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Interval <= TimeSpan.Zero || options.Interval > HeartbeatRegistrationOptions.MaximumDuration
            || options.InitialDelay < TimeSpan.Zero || options.InitialDelay > HeartbeatRegistrationOptions.MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        if (monitoring is not null)
        {
            int selections = (monitoring.Monitor is null ? 0 : 1) + (monitoring.Availability is null ? 0 : 1)
                + (monitoring.ConsecutiveFailures is null ? 0 : 1);
            if (selections != 1 || monitoring.Monitor is not null && monitoring.Monitor is not (IAvailabilityMonitor or IConsecutiveFailureMonitor)
                || monitoring.Monitor is null && monitoring.MonitorFactory is null)
            {
                throw new ArgumentException("Select one suitable monitor or a public factory/policy.", nameof(monitoring));
            }
            if ((monitoring.Monitor is IAvailabilityMonitor || monitoring.Availability is not null)
                && !monitoring.SuccessPublishesUsableData)
            {
                throw new ArgumentException("Availability requires explicit usable-data publication.", nameof(monitoring));
            }
        }
        var copied = new HeartbeatRegistrationOptions
        {
            Name = options.Name,
            Interval = options.Interval,
            InitialDelay = options.InitialDelay
        };
        ManualHeartbeatRegistration entry;
        lock (gate)
        {
            EnsureOpen(owner);
            if (registrations.Any(item => ReferenceEquals(item.Subscriber, subscriber)))
            {
                return;
            }
            if (registrations.Count == capacity)
            {
                throw new InvalidOperationException("The manual registration capacity has been reached.");
            }
            entry = new(owner, subscriber, copied, monitoring, condition);
            registrations.Add(entry);
            owner.Count++;
        }
        try
        {
            // Retain the initializing entry before calling a supplied public factory.
            IHealthMonitor? monitor = monitoring?.Monitor;
            bool ownsMonitor = false;
            if (monitoring?.ConsecutiveFailures is { } failures)
            {
                monitor = monitoring.MonitorFactory!.CreateConsecutiveFailures(failures);
                ownsMonitor = true;
            }
            else if (monitoring?.Availability is { } availability)
            {
                monitor = monitoring.MonitorFactory!.CreateAvailability(availability);
                ownsMonitor = true;
            }
            bool retire;
            lock (gate)
            {
                entry.CurrentMonitor = monitor;
                entry.OwnsMonitor = ownsMonitor;
                entry.Initialized = true;
                retire = MarkRetiring(entry);
            }
            if (retire)
            {
                _ = RetireAsync(entry);
            }
        }
        catch
        {
            lock (gate)
            {
                registrations.Remove(entry);
                owner.Count--;
                entry.Closed.TrySetResult();
                CompleteOwner(owner);
            }
            throw;
        }
    }

    /// <summary>Starts one explicit invocation and returns its retained actual operation before any user prologue can reenter.</summary>
    public Task<ManualHeartbeatResult> TriggerAsync(IHeartbeatSubscriber subscriber, CancellationToken executionCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (executionCancellation.IsCancellationRequested)
        {
            return Task.FromCanceled<ManualHeartbeatResult>(executionCancellation);
        }
        ManualHeartbeatRegistration entry;
        var completion = new TaskCompletionSource<ManualHeartbeatResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            entry = registrations.SingleOrDefault(item => ReferenceEquals(item.Subscriber, subscriber))
                ?? throw new InvalidOperationException("The subscriber is not registered.");
            EnsureOpen(entry.Owner);
            if (entry.Closing || !entry.Initialized || entry.Active is not null)
            {
                throw new InvalidOperationException("The registration is closing, initializing or already executing.");
            }
            entry.Active = completion.Task;
        }
        _ = InvokeAsync(entry, completion, executionCancellation);
        return completion.Task;
    }

    private async Task InvokeAsync(ManualHeartbeatRegistration entry, TaskCompletionSource<ManualHeartbeatResult> completion, CancellationToken token)
    {
        ManualHeartbeatRegistration? previous = executing.Value;
        executing.Value = entry;
        var result = new ManualHeartbeatResult(ManualHeartbeatOutcome.Failed);
        try
        {
            bool admitted = entry.Condition is null || await (entry.Condition(token)
                ?? throw new InvalidOperationException("The condition returned no task.")).ConfigureAwait(false);
            if (!admitted)
            {
                result = new(ManualHeartbeatOutcome.Skipped);
            }
            else
            {
                bool value = await (entry.Subscriber.OnElapsedAsync(token)
                    ?? throw new InvalidOperationException("The callback returned no task.")).ConfigureAwait(false);
                result = new(value ? ManualHeartbeatOutcome.Succeeded : ManualHeartbeatOutcome.Failed, value);
            }
        }
        catch (OperationCanceledException error) when (token.IsCancellationRequested && error.CancellationToken == token)
        {
            result = new(ManualHeartbeatOutcome.ExpectedCancellation);
        }
        catch (Exception error)
        {
            Exception? handling = null;
            try
            {
                await (entry.Subscriber.OnErrorAsync(error)
                    ?? throw new InvalidOperationException("OnErrorAsync returned no task.")).ConfigureAwait(false);
            }
            catch (Exception secondary)
            {
                handling = secondary;
            }
            result = new(ManualHeartbeatOutcome.Failed, Error: error, ErrorHandlingError: handling);
        }
        try
        {
            Report(entry, result.Outcome);
        }
        catch (Exception error)
        {
            result = result with
            {
                ReportingError = error
            };
        }
        executing.Value = previous;
        bool retire;
        lock (gate)
        {
            completion.TrySetResult(result);
            entry.Active = null;
            retire = MarkRetiring(entry);
        }
        if (retire)
        {
            await RetireAsync(entry).ConfigureAwait(false);
        }
    }

    private static void Report(ManualHeartbeatRegistration entry, ManualHeartbeatOutcome outcome)
    {
        if (entry.Monitor is IConsecutiveFailureMonitor failures)
        {
            switch (outcome)
            {
                case ManualHeartbeatOutcome.Succeeded:
                    failures.ReportSuccess();
                    break;
                case ManualHeartbeatOutcome.Failed:
                    failures.ReportFailure();
                    break;
                case ManualHeartbeatOutcome.Skipped:
                    failures.ReportSkipped();
                    break;
                case ManualHeartbeatOutcome.ExpectedCancellation:
                    failures.ReportExpectedCancellation();
                    break;
            }
        }
        else if (entry.Monitor is IAvailabilityMonitor availability)
        {
            if (outcome == ManualHeartbeatOutcome.Succeeded && entry.Monitoring!.SuccessPublishesUsableData)
            {
                availability.ReportPublished();
            }
            else if (outcome == ManualHeartbeatOutcome.Failed)
            {
                availability.ReportRefreshFailure();
            }
        }
    }

    private Task Unregister(Owner owner, IHeartbeatSubscriber subscriber, CancellationToken waitCancellation)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ManualHeartbeatRegistration? entry;
        bool retire;
        lock (gate)
        {
            entry = registrations.SingleOrDefault(item => ReferenceEquals(item.Subscriber, subscriber) && ReferenceEquals(item.Owner, owner));
            if (entry is null)
            {
                return Task.CompletedTask;
            }
            entry.Closing = true;
            retire = MarkRetiring(entry);
        }
        if (retire)
        {
            _ = RetireAsync(entry);
        }
        if (ReferenceEquals(executing.Value, entry))
        {
            throw new InvalidOperationException("An invocation cannot wait for its own unregister drain.");
        }
        return Wait(entry.Closed.Task, waitCancellation);
    }

    private Task Close(Owner owner, CancellationToken waitCancellation = default)
    {
        List<ManualHeartbeatRegistration> retiring = [];
        lock (gate)
        {
            owner.Closing = true;
            foreach (ManualHeartbeatRegistration entry in registrations.Where(item => ReferenceEquals(item.Owner, owner)))
            {
                entry.Closing = true;
                if (MarkRetiring(entry))
                {
                    retiring.Add(entry);
                }
            }
            CompleteOwner(owner);
        }
        foreach (ManualHeartbeatRegistration entry in retiring)
        {
            _ = RetireAsync(entry);
        }
        if (ReferenceEquals(executing.Value?.Owner, owner))
        {
            throw new InvalidOperationException("An invocation cannot wait for its own owner drain.");
        }
        return Wait(owner.Completion.Task, waitCancellation);
    }

    private static Task Wait(Task actual, CancellationToken waitCancellation)
        => actual.IsCompleted ? actual : actual.WaitAsync(waitCancellation);

    private bool MarkRetiring(ManualHeartbeatRegistration entry)
    {
        if (!entry.Closing || !entry.Initialized || entry.Active is not null || entry.Retiring)
        {
            return false;
        }
        entry.Retiring = true;
        return true;
    }

    private async Task RetireAsync(ManualHeartbeatRegistration entry)
    {
        Exception? failure = null;
        try
        {
            if (entry.OwnsMonitor && entry.Monitor is { } monitor)
            {
                await monitor.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            failure = error;
        }
        lock (gate)
        {
            registrations.Remove(entry);
            entry.Owner.Count--;
            if (failure is null)
            {
                entry.Closed.TrySetResult();
            }
            else
            {
                entry.Owner.Failure ??= failure;
                entry.Closed.TrySetException(failure);
            }
            CompleteOwner(entry.Owner);
        }
    }

    private void CompleteOwner(Owner owner)
    {
        if (!owner.Closing || owner.Count != 0)
        {
            return;
        }
        owners.Remove(owner);
        if (owner.Failure is null)
        {
            owner.Completion.TrySetResult();
        }
        else
        {
            owner.Completion.TrySetException(owner.Failure);
        }
    }

    private void EnsureOpen(Owner owner)
    {
        ObjectDisposedException.ThrowIf(disposal is not null, this);
        if (owner.Closing)
        {
            throw new InvalidOperationException("The manual Heartbeat owner is closing.");
        }
    }

    /// <summary>Closes all manual owners and joins their actual accepted work; no subscriber is disposed.</summary>
    public ValueTask DisposeAsync()
    {
        if (executing.Value is not null)
        {
            throw new InvalidOperationException("An invocation cannot dispose its own manual Heartbeat.");
        }
        TaskCompletionSource? completion = null;
        Owner[] snapshot = [];
        Task operation;
        lock (gate)
        {
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
                snapshot = owners.ToArray();
            }
            operation = disposal;
        }
        if (completion is not null)
        {
            _ = DisposeCoreAsync(completion, snapshot);
        }
        return new(operation);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion, Owner[] snapshot)
    {
        try
        {
            await Task.WhenAll(snapshot.Select(owner => Close(owner))).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    internal sealed class Owner : IHeartbeatOwner
    {
        private readonly ManualHeartbeat parent;
        internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Exception? Failure;
        internal bool Closing;
        internal int Count;

        internal Owner(ManualHeartbeat parent)
        {
            this.parent = parent;
            Service = new OwnerService(parent, this);
        }

        public IHeartbeatService Service
        {
            get;
        }
        public Task CloseAsync(CancellationToken waitCancellation = default) => parent.Close(this, waitCancellation);
        public ValueTask DisposeAsync() => new(CloseAsync());
    }

    private sealed class OwnerService(ManualHeartbeat parent, Owner owner) : IHeartbeatHealthcheckService
    {
        public void Register(IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options, Func<CancellationToken, Task<bool>>? condition = null)
            => parent.Register(owner, subscriber, options, null, condition);
        public void Register(IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
            HeartbeatHealthcheckOptions monitoring, Func<CancellationToken, Task<bool>>? condition = null)
        {
            ArgumentNullException.ThrowIfNull(monitoring);
            parent.Register(owner, subscriber, options, monitoring, condition);
        }
        public Task UnregisterAsync(IHeartbeatSubscriber subscriber, CancellationToken waitCancellation = default)
            => parent.Unregister(owner, subscriber, waitCancellation);
    }
}
