namespace CIS.Coordination;

/// <summary>Owns bounded local acquisitions, conservative deadlines and independent renewal of exact authority grants.</summary>
/// <remarks>Direct consumers must drain their business tasks before releasing a lease. Stop joins actual provider requests
/// and cancellation callbacks; an uncooperative provider or callback can keep that join incomplete.</remarks>
public sealed partial class CoordinationRuntime : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly ICoordinationProvider provider;
    private readonly CoordinationOptions options;
    private readonly TimeProvider time;
    private readonly Dictionary<Guid, Entry> entries = [];
    private readonly CancellationTokenSource admissionClosed = new();
    private CoordinationRuntimeState state;
    private Task? startTask;
    private Task? stopTask;
    private ITimer? timer;
    private int acquisitionRequests;
    private int renewalRequests;
    private int notifications;
    private bool? authorityAvailable;
    private ICoordinationRuntimeObserver? observer;

    /// <summary>Copies validated finite settings without starting timers or contacting the authority.</summary>
    public CoordinationRuntime(ICoordinationProvider provider, CoordinationOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        if (provider.AuthorityId == Guid.Empty)
        {
            throw new ArgumentException("Select a nonempty installed coordination authority.", nameof(provider));
        }

        this.provider = provider;
        this.options = options.CopyValidated();
        time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Validates the authority once and starts one bounded scheduler; repeated calls join the same startup.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (state is CoordinationRuntimeState.Stopping or CoordinationRuntimeState.Stopped)
            {
                throw new InvalidOperationException("Coordination admission has already closed.");
            }

            if (startTask is null)
            {
                state = CoordinationRuntimeState.Starting;
                startTask = Task.Run(() => StartCoreAsync(cancellationToken), CancellationToken.None);
            }

            return startTask;
        }
    }

    /// <summary>Closes acquisition immediately and begins finite graceful drain for every published handle.</summary>
    public void BeginStopping()
    {
        List<(Entry Entry, TransitionNotice? Notice)> changed = [];
        lock (gate)
        {
            if (state is CoordinationRuntimeState.Stopping or CoordinationRuntimeState.Stopped)
            {
                return;
            }

            state = CoordinationRuntimeState.Stopping;
            foreach (Entry entry in entries.Values)
            {
                if (BeginDrainingLocked(entry, options.MaximumGracefulDrain))
                {
                    changed.Add((entry, CaptureTransitionLocked(entry)));
                }
            }
        }

        // This source is only used by runtime-owned waits, never arbitrary consumer callbacks.
        admissionClosed.Cancel();
        foreach ((Entry entry, TransitionNotice? notice) in changed)
        {
            DispatchTransition(entry, notice);
        }
    }

    /// <summary>Begins finite drain, then joins actual owned requests and terminal notifications without abandoning cleanup.</summary>
    public Task StopAsync()
    {
        BeginStopping();
        lock (gate)
        {
            stopTask ??= Task.Run(StopCoreAsync, CancellationToken.None);
            return stopTask;
        }
    }

    /// <summary>Joins the same actual runtime stop barrier.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>Reads one bounded detached diagnostic page without authority I/O or retained history.</summary>
    public CoordinationRuntimeSnapshot GetSnapshot(int skip = 0, int take = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, options.MaximumSnapshotPageSize);
        lock (gate)
        {
            CoordinationLeaseSnapshot[] page = entries.Values.Skip(skip).Take(take).Select(SnapshotLocked).ToArray();
            int owned = entries.Values.Count(entry => entry.State is CoordinationLeaseState.Owned or CoordinationLeaseState.Draining);
            return new(state, entries.Count, acquisitionRequests, renewalRequests, notifications, owned, authorityAvailable,
                entries.Count, Array.AsReadOnly(page));
        }
    }

    internal void SetObserver(ICoordinationRuntimeObserver value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            if (observer is not null)
            {
                throw new InvalidOperationException("Coordination instrumentation is already attached.");
            }

            observer = value;
        }
    }

    internal async Task<ManagedCoordinationResult> AcquireAuthorizedAsync(CoordinationAcquisition acquisition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        cancellationToken.ThrowIfCancellationRequested();
        Entry entry;
        lock (gate)
        {
            if (state != CoordinationRuntimeState.Running)
            {
                return new(CoordinationAcquireOutcome.Unavailable, reasonCode: "coordination.admission.closed");
            }

            if (entries.Count == options.MaximumManagedLeases)
            {
                return new(CoordinationAcquireOutcome.CapacityExceeded, reasonCode: "coordination.capacity.managed");
            }

            if (entries.ContainsKey(acquisition.AcquisitionId))
            {
                throw new ArgumentException("Every acquisition attempt requires a fresh identifier.", nameof(acquisition));
            }

            entry = new(acquisition);
            entries.Add(acquisition.AcquisitionId, entry);
        }

        try
        {
            PhysicalReply<CoordinationAcquireResult> reply = await RequestAsync(entry, CoordinationRuntimeOperation.Acquire,
                token => provider.AcquireAsync(new(acquisition, options.LeaseDuration), token), options.RequestTimeout,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (reply.CapacityExceeded)
            {
                return new(CoordinationAcquireOutcome.CapacityExceeded, reasonCode: "coordination.capacity.requests");
            }

            if (reply.Result is { } result)
            {
                if (result.Outcome == CoordinationAcquireOutcome.Acquired)
                {
                    ManagedCoordinationResult? adopted = TryAdopt(entry, result.Grant!, result.RemainingValidity, reply.Started,
                        cancellationToken);
                    if (adopted is not null)
                    {
                        return adopted;
                    }

                    await CleanupKnownGrantAsync(entry, result.Grant!).ConfigureAwait(false);
                    return new(CoordinationAcquireOutcome.Indeterminate, reasonCode: "coordination.ack.expired");
                }

                if (result.Outcome != CoordinationAcquireOutcome.Indeterminate)
                {
                    return new(result.Outcome, reasonCode: result.ReasonCode, observedOwner: result.ObservedOwner);
                }
            }

            return await RecoverAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CoordinationGrant? orphan;
            lock (gate)
            {
                if (entry.Lease is null)
                {
                    entry.Active = false;
                }

                orphan = entry.Lease is null ? entry.KnownGrant : null;
            }

            if (orphan is not null)
            {
                await CleanupKnownGrantAsync(entry, orphan).ConfigureAwait(false);
            }

            EndCoordinator(entry);
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await provider.ValidateAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                authorityAvailable = true;
                if (state == CoordinationRuntimeState.Starting)
                {
                    TimeSpan interval = new[] { options.RenewalRetryDelay, options.RenewalInterval,
                        options.RecoveryObservationDelay, TimeSpan.FromMilliseconds(100) }.Min();
                    timer = time.CreateTimer(static owner => ((CoordinationRuntime)owner!).Pump(), this, interval, interval);
                    state = CoordinationRuntimeState.Running;
                }
            }
        }
        catch
        {
            lock (gate)
            {
                authorityAvailable = false;
            }

            throw;
        }
    }

    private async Task StopCoreAsync()
    {
        Task? startup;
        Task[] barriers;
        lock (gate)
        {
            startup = startTask;
            barriers = entries.Values.Select(entry => entry.Settled.Task).ToArray();
        }

        if (startup is not null)
        {
            try
            {
                await startup.ConfigureAwait(false);
            }
            catch
            {
                // Startup's original caller retains the validation failure. Stop still joins actual settlement.
            }
        }

        await Task.WhenAll(barriers).ConfigureAwait(false);
        ITimer? ownedTimer;
        lock (gate)
        {
            ownedTimer = timer;
            timer = null;
            state = CoordinationRuntimeState.Stopped;
        }

        if (ownedTimer is not null)
        {
            await ownedTimer.DisposeAsync().ConfigureAwait(false);
        }

        admissionClosed.Dispose();
    }

    private async Task<ManagedCoordinationResult> RecoverAsync(Entry entry, CancellationToken caller)
    {
        long started = time.GetTimestamp();
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, admissionClosed.Token);
        while (Eligible(entry, caller) && Remaining(started, options.AcquisitionRecoveryTimeout) > TimeSpan.Zero)
        {
            TimeSpan remaining = Remaining(started, options.AcquisitionRecoveryTimeout);
            PhysicalReply<CoordinationObservation> reply = await RequestAsync(entry, CoordinationRuntimeOperation.Resolve,
                token => provider.ResolveAsync(entry.Acquisition, token), Min(options.RequestTimeout, remaining), caller).ConfigureAwait(false);
            caller.ThrowIfCancellationRequested();
            if (reply.Result is { Outcome: CoordinationObservationOutcome.FoundLive } found)
            {
                if (Remaining(started, options.AcquisitionRecoveryTimeout) > TimeSpan.Zero)
                {
                    ManagedCoordinationResult? adopted = TryAdopt(entry, found.Grant!, found.RemainingValidity, reply.Started, caller);
                    if (adopted is not null)
                    {
                        return adopted;
                    }
                }

                await CleanupKnownGrantAsync(entry, found.Grant!).ConfigureAwait(false);
                break;
            }

            remaining = Remaining(started, options.AcquisitionRecoveryTimeout);
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            try
            {
                await Task.Delay(Min(options.RecoveryObservationDelay, remaining), time, waitCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!caller.IsCancellationRequested)
            {
                break;
            }
        }

        caller.ThrowIfCancellationRequested();
        return new(CoordinationAcquireOutcome.Indeterminate, reasonCode: "coordination.acquisition.abandoned");
    }

    private ManagedCoordinationResult? TryAdopt(Entry entry, CoordinationGrant grant, TimeSpan remainingValidity,
        long requestStarted, CancellationToken caller)
    {
        bool adopted;
        TransitionNotice? notice = null;
        lock (gate)
        {
            TimeSpan budget = Min(remainingValidity, options.LeaseDuration) - options.SafetyMargin;
            adopted = state == CoordinationRuntimeState.Running && !caller.IsCancellationRequested && entry.Active
                && grant.AuthorityId == provider.AuthorityId && grant.Acquisition == entry.Acquisition
                && Remaining(requestStarted, budget) > TimeSpan.Zero;
            if (adopted)
            {
                entry.Grant = grant;
                entry.State = CoordinationLeaseState.Owned;
                entry.Deadline = new(requestStarted, budget);
                entry.NextRenewal = new(time.GetTimestamp(), options.RenewalInterval);
                entry.Lease = new(this, entry);
                notice = CaptureTransitionLocked(entry);
            }
        }

        if (!adopted)
        {
            return null;
        }

        DispatchTransition(entry, notice);
        return new(CoordinationAcquireOutcome.Acquired, entry.Lease);
    }

    private bool Eligible(Entry entry, CancellationToken caller)
    {
        lock (gate)
        {
            return state == CoordinationRuntimeState.Running && entry.Active && !caller.IsCancellationRequested;
        }
    }

    private TimeSpan Remaining(long started, TimeSpan budget) => budget - time.GetElapsedTime(started, time.GetTimestamp());

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first <= second ? first : second;

    private CoordinationLeaseSnapshot SnapshotLocked(Entry entry) => new(entry.Acquisition, entry.State, entry.Grant,
        entry.LossReason, entry.Requests, entry.Notifications);

    private void RemoveSettledLocked(Entry entry)
    {
        if (!entry.Active && entry.Coordinators == 0 && entry.Requests == 0 && entry.Notifications == 0)
        {
            entries.Remove(entry.Acquisition.AcquisitionId);
            if (!entry.SourceDisposed)
            {
                entry.SourceDisposed = true;
                entry.OwnershipLost.Dispose();
            }

            entry.Settled.TrySetResult();
        }
    }

    private readonly record struct Deadline(long Started, TimeSpan Budget);

    private sealed class Entry
    {
        internal Entry(CoordinationAcquisition acquisition)
        {
            Acquisition = acquisition;
            OwnershipToken = OwnershipLost.Token;
        }

        internal readonly CoordinationAcquisition Acquisition;
        internal readonly CancellationTokenSource OwnershipLost = new();
        internal readonly CancellationToken OwnershipToken;
        internal readonly TaskCompletionSource<CoordinationLeaseTermination> Termination = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Active = true;
        internal int Coordinators = 1;
        internal int Requests;
        internal int Notifications;
        internal CoordinationLeaseState State = CoordinationLeaseState.Acquiring;
        internal CoordinationLossReason? LossReason;
        internal CoordinationGrant? Grant;
        internal CoordinationGrant? KnownGrant;
        internal ManagedLease? Lease;
        internal Deadline Deadline;
        internal Deadline NextRenewal;
        internal Deadline? DrainDeadline;
        internal bool RenewalInFlight;
        internal bool RenewalCoordinator;
        internal int RenewalRequests;
        internal bool CleanupScheduled;
        internal bool SourceDisposed;
        internal Task<CoordinationMutationResult>? ReleaseTask;
    }
}
