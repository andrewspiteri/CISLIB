using System.Runtime.ExceptionServices;
using CIS.Management.Wire;

namespace CIS.Management;

/// <summary>Owns the selected process's bounded local management admission state without owning business work.</summary>
/// <remarks>This gate starts closed. Freshness is checked on every admission, independently of Heartbeat scheduling.
/// Closing it never cancels an already admitted invocation, changes configuration or grants business/lease authority.</remarks>
public sealed class ManagedWorkAdmission : IManagedWorkAdmission
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly long frequency;
    private ManagedWorkAdmissionSnapshot current = new(ManagedWorkAdmissionState.AwaitingBootstrap, false, 0,
        "management.bootstrap.required");
    private Func<bool>? prerequisites;
    private string? registrationId;
    private string? authorityId;
    private string? controlRevision;
    private long generation;
    private long confirmationStarted;
    private int validForMs;
    private long lastTimestamp;
    private DateTimeOffset lastUtc;
    private bool hasClockSample;
    private bool bootstrapped;
    private bool confirmationGapObserved;

    /// <summary>Captures one clock without starting timers, reading credentials or resolving modules.</summary>
    public ManagedWorkAdmission(TimeProvider? timeProvider = null)
    {
        clock = timeProvider ?? TimeProvider.System;
        frequency = clock.TimestampFrequency;
        if (frequency <= 0)
        {
            throw new ArgumentException("Managed admission requires a measurable monotonic clock.", nameof(timeProvider));
        }
    }

    /// <inheritdoc />
    public event EventHandler<ManagedWorkAdmissionChangedEventArgs>? Changed;

    /// <inheritdoc />
    public bool TryAdmit() => CaptureSnapshot().AdmissionAllowed;

    /// <inheritdoc />
    public ManagedWorkAdmissionSnapshot CaptureSnapshot()
    {
        Func<bool>? selected;
        lock (gate)
        {
            if (current.State != ManagedWorkAdmissionState.Ready)
            {
                return current;
            }
            selected = prerequisites;
        }
        bool ready;
        try
        {
            ready = selected?.Invoke() ?? true;
        }
        catch (Exception error)
        {
            InvalidatePreserving(error, "management.prerequisite.fault");
            throw;
        }
        ManagedWorkAdmissionSnapshot? publication = null;
        ManagedWorkAdmissionSnapshot snapshot;
        try
        {
            lock (gate)
            {
                if (current.State == ManagedWorkAdmissionState.Ready)
                {
                    ClockSample now = ReadClockUnderLock();
                    if (!now.Trusted)
                    {
                        publication = CloseUnderLock("management.control.clock_gap");
                    }
                    else if (!FreshUnderLock(confirmationStarted, validForMs, now.Timestamp))
                    {
                        publication = CloseUnderLock("management.control.expired");
                    }
                    else if (!ready)
                    {
                        publication = CloseUnderLock("management.prerequisite.unavailable");
                    }
                }
                snapshot = current;
            }
        }
        catch (Exception error)
        {
            InvalidatePreserving(error, "management.control.clock_unavailable");
            throw;
        }
        Publish(publication);
        return snapshot;
    }

    /// <summary>Requires another live exchange after a host-observed resume or unmeasurable timing gap.</summary>
    /// <remarks>Prior accepted authority/revision remain observations; this cannot migrate authority or reopen stopping.</remarks>
    public void Invalidate()
    {
        ManagedWorkAdmissionSnapshot? publication;
        lock (gate)
        {
            hasClockSample = false;
            publication = CloseUnderLock("management.control.revalidation_required");
        }
        Publish(publication);
    }

    internal void CloseAfterFault()
    {
        ManagedWorkAdmissionSnapshot? publication;
        lock (gate)
        {
            hasClockSample = false;
            string reason = current.State is ManagedWorkAdmissionState.Ready or ManagedWorkAdmissionState.Reconciling
                ? "management.control.revalidation_required"
                : current.ReasonCode ?? "management.control.revalidation_required";
            // Fence outstanding work even when an earlier precise refusal has already closed admission.
            publication = CloseUnderLock(reason);
        }
        Publish(publication);
    }

    // Reporting refusal still closes RM-07 admission, but is not an explicit host/control-connectivity gap.
    internal void InvalidateReporting()
    {
        ManagedWorkAdmissionSnapshot? publication;
        lock (gate)
        {
            // Reporting refusal does not invalidate clock evidence; retain it for the next control-entry gap check.
            publication = CloseUnderLock("management.reporting.unavailable");
        }
        Publish(publication);
    }

    internal void BindPrerequisites(Func<bool> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        lock (gate)
        {
            if (prerequisites is not null)
            {
                throw new InvalidOperationException("Bind the selected managed prerequisites once.");
            }
            prerequisites = selected;
        }
    }

    internal void BindRegistration(string id)
    {
        if (!ManagementWire.IsUuid(id))
        {
            throw new ArgumentException("Select a canonical registration UUID.", nameof(id));
        }
        ManagedWorkAdmissionSnapshot? publication;
        lock (gate)
        {
            if (current.State == ManagedWorkAdmissionState.Stopping)
            {
                return;
            }
            if (registrationId != id)
            {
                // A replacement session has no usable authority window from the prior registration.
                confirmationGapObserved = true;
            }
            registrationId = id;
            publication = CloseUnderLock("management.bootstrap.registration_required");
        }
        Publish(publication);
    }

    internal ManagedControlAttempt BeginAttempt(string id, string attemptId)
    {
        if (!ManagementWire.IsUuid(id) || !ManagementWire.IsUuid(attemptId))
        {
            throw new ArgumentException("Select canonical control registration and attempt UUIDs.");
        }
        ManagedWorkAdmissionSnapshot? publication;
        ManagedWorkAdmissionSnapshot? gap = null;
        ManagedControlAttempt result;
        lock (gate)
        {
            if (current.State == ManagedWorkAdmissionState.Stopping || registrationId != id)
            {
                throw new InvalidOperationException("The managed registration is closed or has been replaced.");
            }
            // Revalidation starts at this sample, even if the previous sample exposed a resume gap.
            ClockSample started = ReadClockUnderLock();
            if (bootstrapped && !confirmationGapObserved)
            {
                string? reason = !started.Trusted ? "management.control.clock_gap"
                    : !FreshUnderLock(confirmationStarted, validForMs, started.Timestamp) ? "management.control.expired" : null;
                if (reason is not null)
                {
                    // Retain the old window's gap before Reconciling replaces it, even without an admission/status observer.
                    gap = CloseUnderLock(reason);
                }
            }
            generation = checked(generation + 1);
            result = new(generation, id, attemptId, started.Timestamp);
            publication = UpdateUnderLock(ManagedWorkAdmissionState.Reconciling, false, "management.control.checking");
        }
        Publish(gap);
        Publish(publication);
        return result;
    }

    internal bool IsCurrent(ManagedControlAttempt attempt)
    {
        lock (gate)
        {
            return CurrentUnderLock(attempt);
        }
    }

    internal void Refuse(ManagedControlAttempt attempt, string reason)
    {
        ManagedWorkAdmissionSnapshot? publication = null;
        lock (gate)
        {
            if (CurrentUnderLock(attempt))
            {
                publication = CloseUnderLock(reason);
            }
        }
        Publish(publication);
    }

    internal bool ValidateConfirmation(ManagedControlAttempt attempt, ManagementControlConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        ManagedWorkAdmissionSnapshot? publication = null;
        bool accepted = false;
        lock (gate)
        {
            if (CurrentUnderLock(attempt))
            {
                ClockSample now = ReadClockUnderLock();
                string? refusal = !ManagementWire.IsUuid(confirmation.AuthorityId)
                    || confirmation.RegistrationId != attempt.RegistrationId || confirmation.AttemptId != attempt.AttemptId
                    || !ManagementWire.IsScalarText(confirmation.ControlRevision, 256)
                    || confirmation.ValidForMs is < 1 or > ManagementWire.MaximumControlValidForMs
                    ? "management.control.invalid_confirmation"
                    : authorityId is not null && authorityId != confirmation.AuthorityId ? "management.control.authority_changed"
                    : !confirmation.AdmissionAllowed ? "management.control.refused"
                    : !now.Trusted ? "management.control.clock_gap"
                    : !FreshUnderLock(attempt.Started, confirmation.ValidForMs, now.Timestamp) ? "management.control.expired" : null;
                if (refusal is null)
                {
                    // Bind the confirmed namespace before module reconciliation can fail or be delayed.
                    authorityId ??= confirmation.AuthorityId;
                    accepted = true;
                }
                else
                {
                    publication = CloseUnderLock(refusal);
                }
            }
        }
        Publish(publication);
        return accepted;
    }

    internal bool CompleteReconciliation(ManagedControlAttempt attempt, ManagementControlConfirmation confirmation)
    {
        if (!ValidateConfirmation(attempt, confirmation))
        {
            return false;
        }
        bool ready;
        try
        {
            ready = prerequisites?.Invoke() ?? true;
        }
        catch (Exception error)
        {
            InvalidatePreserving(error, "management.prerequisite.fault");
            throw;
        }
        ManagedWorkAdmissionSnapshot? publication = null;
        bool accepted = false;
        lock (gate)
        {
            if (CurrentUnderLock(attempt))
            {
                ClockSample now = ReadClockUnderLock();
                if (!ready)
                {
                    publication = CloseUnderLock("management.reconciliation.incomplete");
                }
                else if (!now.Trusted || !FreshUnderLock(attempt.Started, confirmation.ValidForMs, now.Timestamp))
                {
                    publication = CloseUnderLock(now.Trusted ? "management.control.expired" : "management.control.clock_gap");
                }
                else
                {
                    authorityId ??= confirmation.AuthorityId;
                    controlRevision = confirmation.ControlRevision;
                    confirmationStarted = attempt.Started;
                    validForMs = confirmation.ValidForMs;
                    bootstrapped = true;
                    confirmationGapObserved = false;
                    publication = UpdateUnderLock(ManagedWorkAdmissionState.Ready, true, null);
                    accepted = true;
                }
            }
        }
        Publish(publication);
        return accepted;
    }

    internal void BeginStopping()
    {
        ManagedWorkAdmissionSnapshot? publication = null;
        lock (gate)
        {
            if (current.State != ManagedWorkAdmissionState.Stopping)
            {
                generation = checked(generation + 1);
                publication = UpdateUnderLock(ManagedWorkAdmissionState.Stopping, false, "management.stopping");
            }
        }
        Publish(publication);
    }

    private bool CurrentUnderLock(ManagedControlAttempt attempt)
        => current.State != ManagedWorkAdmissionState.Stopping && generation == attempt.Generation
            && registrationId == attempt.RegistrationId;

    private bool FreshUnderLock(long started, int milliseconds, long now)
    {
        TimeSpan elapsed = clock.GetElapsedTime(started, now);
        return elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromMilliseconds(milliseconds);
    }

    private ClockSample ReadClockUnderLock()
    {
        long timestamp = clock.GetTimestamp();
        DateTimeOffset utc = clock.GetUtcNow();
        bool trusted = clock.TimestampFrequency == frequency;
        if (hasClockSample)
        {
            TimeSpan elapsed = clock.GetElapsedTime(lastTimestamp, timestamp);
            TimeSpan utcElapsed = utc - lastUtc;
            trusted &= elapsed >= TimeSpan.Zero && utcElapsed >= TimeSpan.Zero
                && Math.Abs((utcElapsed - elapsed).TotalMilliseconds) <= 1000;
        }
        lastTimestamp = timestamp;
        lastUtc = utc;
        hasClockSample = true;
        return new(timestamp, trusted);
    }

    private ManagedWorkAdmissionSnapshot? CloseUnderLock(string reason)
    {
        if (current.State == ManagedWorkAdmissionState.Stopping)
        {
            return null;
        }
        if (reason is "management.control.expired" or "management.control.clock_gap"
            or "management.control.clock_unavailable" or "management.control.revalidation_required")
        {
            // One failed window can span several control attempts while module reconciliation is incomplete.
            confirmationGapObserved = true;
        }
        generation = checked(generation + 1);
        return UpdateUnderLock(bootstrapped ? ManagedWorkAdmissionState.ContinuingOnly : ManagedWorkAdmissionState.AwaitingBootstrap,
            false, reason);
    }

    private ManagedWorkAdmissionSnapshot? UpdateUnderLock(ManagedWorkAdmissionState state, bool allowed, string? reason)
    {
        if (current.State == state && current.AdmissionAllowed == allowed && current.ReasonCode == reason
            && current.AuthorityId == authorityId && current.RegistrationId == registrationId && current.ControlRevision == controlRevision)
        {
            return null;
        }
        current = new(state, allowed, checked(current.Revision + 1), reason, authorityId, registrationId, controlRevision);
        return current;
    }

    private void InvalidatePreserving(Exception original, string reason)
    {
        try
        {
            ManagedWorkAdmissionSnapshot? publication;
            lock (gate)
            {
                publication = CloseUnderLock(reason);
            }
            Publish(publication);
        }
        catch (Exception secondary)
        {
            throw new AggregateException("Managed admission and its failure notification faulted.", original, secondary);
        }
        ExceptionDispatchInfo.Capture(original).Throw();
    }

    private void Publish(ManagedWorkAdmissionSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            Changed?.Invoke(this, new(snapshot));
        }
    }

    private readonly record struct ClockSample(long Timestamp, bool Trusted);
}

internal readonly record struct ManagedControlAttempt(long Generation, string RegistrationId, string AttemptId, long Started);
