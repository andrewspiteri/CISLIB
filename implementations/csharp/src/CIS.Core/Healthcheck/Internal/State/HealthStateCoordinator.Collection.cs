using System.Numerics;
using System.Runtime.CompilerServices;

namespace CIS.Healthcheck.Internal;

internal sealed partial class HealthStateCoordinator
{
    // A caller may retain a retired target and its shared drain task. Weak keys avoid a
    // service-lifetime history of completed attachments while preserving that identity.
    private readonly ConditionalWeakTable<HealthCollectionTarget, CollectionAttachment> attachments = new();

    public HealthCollectionTarget RegisterCollection(Guid monitorId, Guid collectionRegistrationId, HealthCollectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (collectionRegistrationId == Guid.Empty || (options.Interval is TimeSpan interval &&
            (interval <= TimeSpan.Zero || interval.TotalMilliseconds > uint.MaxValue - 1)))
        { throw new ArgumentException("Supply a nonempty identity and a positive, representable interval.", nameof(options)); }
        var effects = new Effects();
        try
        {
            lock (gate)
            {
                EnsureAdmission();
                SweepDead(effects);
                if (!active.TryGetValue(monitorId, out var entry))
                { throw new ArgumentException("Supply an active monitor belonging to this service.", nameof(monitorId)); }
                if (entry.Collection is not null || active.Values.Any(item => item.Collection?.CollectionRegistrationId == collectionRegistrationId))
                { throw new InvalidOperationException("The collection is already attached."); }
                if (entry.Generation == long.MaxValue) { throw new InvalidOperationException("The attachment generation is exhausted."); }
                var target = new HealthCollectionTarget(monitorId, collectionRegistrationId, ++entry.Generation, options.Interval, options.IsRequired);
                attachments.Add(target, new CollectionAttachment(entry, target));
                entry.Collection = target;
                entry.HasCollectionFacts = true;
                revision++;
                return target;
            }
        }
        finally { Dispatch(effects); }
    }

    public HealthCollectionReservationResult TryReserve(HealthCollectionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (gate)
        {
            if (!attachments.TryGetValue(target, out var attachment) || !IsCurrent(attachment) || stopping)
            { return new(HealthCollectionReservationDisposition.Retired, null); }
            if (attachment.Operation is { } existing)
            { return new(HealthCollectionReservationDisposition.Existing, existing.Reservation); }
            var operation = new CollectionOperation(this, attachment);
            var entry = attachment.Entry;
            operation.Reservation = new(Guid.NewGuid(), entry.Id, target.CollectionRegistrationId, target.Generation,
                entry.HealthRevision, entry.Measurements.ToDictionary(pair => pair.Key, pair => pair.Value.Revision, StringComparer.Ordinal),
                operation.Cancellation.Token, operation);
            attachment.Operation = operation;
            operations.Add(operation.Reservation.OperationId, operation);
            entry.PendingOperations++;
            revision++;
            return new(HealthCollectionReservationDisposition.Reserved, operation.Reservation);
        }
    }

    public bool TryEnter(HealthCollectionReservation reservation)
    {
        lock (gate)
        {
            var operation = Resolve(reservation);
            if (operation is null || operation.Entered || operation.CompletionCalled || stopping || !IsCurrent(operation.Attachment)) { return false; }
            operation.Entered = true;
            return true;
        }
    }

    public HealthCollectionAttempt CommitResult(HealthCollectionReservation reservation, HealthCollectionResult result)
    {
        DateTimeOffset now = Now();
        lock (gate)
        {
            var operation = Resolve(reservation);
            if (operation is null) { return RetiredAttempt(); }
            if (operation.Evaluation is not null) { return operation.Evaluation; }
            if (!CanCommit(operation)) { return RetiredAttempt(); }
            var entry = operation.Entry;
            bool matches = result is not null && (operation.Attachment.Target.IsRequired
                ? entry.Policy switch
                {
                    HealthPolicyKind.Condition => result.Kind == HealthCollectionResultKind.Condition,
                    HealthPolicyKind.ConsecutiveFailures => result.Kind == HealthCollectionResultKind.Execution,
                    HealthPolicyKind.NumericThreshold => result.Kind == HealthCollectionResultKind.Number,
                    _ => result.Kind is HealthCollectionResultKind.AvailabilityPublished or HealthCollectionResultKind.AvailabilityRefreshFailed
                }
                : result.Kind == HealthCollectionResultKind.Diagnostics);
            if (!matches)
            {
                return CommitFailureLocked(operation, new HealthDiagnostic("Healthcheck.CollectionResult", "The evaluation returned no result or a result incompatible with its registration."), now);
            }

            bool healthFresh = operation.Attachment.Target.IsRequired && entry.HealthRevision == reservation.HealthObservationRevision;
            if (healthFresh)
            {
                switch (result!.Kind)
                {
                    case HealthCollectionResultKind.Condition: SetCondition(entry, result.IsFlagged!.Value, result.Diagnostic, now); break;
                    case HealthCollectionResultKind.Number: SetNumber(entry, result.Value!.Value, result.Diagnostic, now); break;
                    case HealthCollectionResultKind.Execution: SetOutcome(entry, result.Outcome!.Value, result.Diagnostic, now); break;
                    case HealthCollectionResultKind.AvailabilityPublished: SetOutcome(entry, HealthExecutionOutcome.Succeeded, null, now); break;
                    case HealthCollectionResultKind.AvailabilityRefreshFailed: SetOutcome(entry, HealthExecutionOutcome.Failed, result.Diagnostic, now); break;
                }
            }
            HealthMeasurement[] eligible = result!.Measurements.Where(measurement =>
            {
                BigInteger baseline = reservation.MeasurementObservationRevisions.GetValueOrDefault(measurement.Key);
                return !entry.Measurements.TryGetValue(measurement.Key, out var current) || current.Revision == baseline;
            }).ToArray();
            bool capacityError = entry.Measurements.Count + eligible.Count(measurement => !entry.Measurements.ContainsKey(measurement.Key)) > 32;
            HealthDiagnostic? error = null;
            if (capacityError)
            {
                error = new("Healthcheck.MeasurementCapacity", "The measurement batch exceeds the monitor's 32 retained metric keys.");
                SetCollectionError(entry, error, now);
            }
            else
            {
                foreach (var measurement in eligible) { entry.Measurements[measurement.Key] = new(measurement, ++revision); }
            }
            bool committed = healthFresh || eligible.Length != 0 || (!operation.Attachment.Target.IsRequired && result.Measurements.Count == 0);
            operation.Evaluation = new(committed ? HealthCollectionDisposition.Committed : HealthCollectionDisposition.Obsolete,
                capacityError && !healthFresh, error);
            return operation.Evaluation;
        }
    }

    public HealthCollectionAttempt CommitEvaluationFailure(HealthCollectionReservation reservation, HealthDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        DateTimeOffset now = Now();
        lock (gate)
        {
            var operation = Resolve(reservation);
            if (operation is null) { return RetiredAttempt(); }
            if (operation.Evaluation is not null) { return operation.Evaluation; }
            return !CanCommit(operation) ? RetiredAttempt() : CommitFailureLocked(operation, diagnostic, now);
        }
    }

    private HealthCollectionAttempt CommitFailureLocked(CollectionOperation operation, HealthDiagnostic diagnostic, DateTimeOffset now)
    {
        var entry = operation.Entry;
        if (operation.Attachment.Target.IsRequired)
        {
            if (entry.HealthRevision != operation.Reservation.HealthObservationRevision)
            { return operation.Evaluation = new(HealthCollectionDisposition.Obsolete, true, diagnostic); }
            if (entry.Policy is HealthPolicyKind.Condition or HealthPolicyKind.NumericThreshold) { SetCondition(entry, true, diagnostic, now); }
            else { SetOutcome(entry, HealthExecutionOutcome.Failed, diagnostic, now); }
        }
        SetCollectionError(entry, diagnostic, now);
        return operation.Evaluation = new(HealthCollectionDisposition.Committed, true, diagnostic);
    }

    public HealthCollectionAttempt Complete(HealthCollectionReservation reservation, HealthCollectionCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (!Enum.IsDefined(completion.Reason)) { throw new ArgumentOutOfRangeException(nameof(completion)); }
        var effects = new Effects();
        HealthCollectionAttempt attempt;
        lock (gate)
        {
            var operation = Resolve(reservation);
            if (operation is null) { return RetiredAttempt(); }
            if (operation.Completion is not null) { return operation.Completion; }
            bool current = IsCurrent(operation.Attachment);
            if (completion.Reason == HealthCollectionCompletionReason.OwnerUnavailable && current)
            {
                WithdrawLocked(operation.Entry, effects);
                attempt = new(HealthCollectionDisposition.OwnerUnavailable, false, null);
            }
            else
            {
                attempt = operation.Evaluation ?? new(current && completion.Reason != HealthCollectionCompletionReason.RetiredBeforeInvocation
                    ? HealthCollectionDisposition.Committed : HealthCollectionDisposition.Retired, false, null);
                if (current)
                {
                    operation.Entry.LastCompleted = completion.CompletedAtUtc;
                    revision++;
                    if (completion.AcquisitionError is not null) { SetCollectionError(operation.Entry, completion.AcquisitionError, completion.CompletedAtUtc); }
                    if (completion.CleanupError is not null) { SetCollectionError(operation.Entry, completion.CleanupError, completion.CompletedAtUtc); }
                }
            }
            operation.CompletionCalled = true;
            operation.Completion = attempt;
            FinalizeOperationLocked(operation, effects);
        }
        Dispatch(effects);
        return attempt;
    }

    public Task WithdrawCollection(HealthCollectionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var effects = new Effects();
        Task task;
        lock (gate)
        {
            if (!attachments.TryGetValue(target, out var attachment)) { return Task.CompletedTask; }
            CloseAttachmentLocked(attachment, effects);
            task = attachment.Disposal.Task;
        }
        Dispatch(effects);
        return task;
    }

    private bool IsCurrent(CollectionAttachment attachment) => !attachment.Closed && !attachment.Entry.Withdrawn &&
        ReferenceEquals(attachment.Entry.Collection, attachment.Target) && attachment.Entry.Generation == attachment.Target.Generation;

    private bool CanCommit(CollectionOperation operation) => operation.Entered && !operation.CompletionCalled && IsCurrent(operation.Attachment);

    private CollectionOperation? Resolve(HealthCollectionReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        return reservation.StateHandle is CollectionOperation operation && ReferenceEquals(operation.Owner, this) &&
            ReferenceEquals(operation.Reservation, reservation) ? operation : null;
    }

    private static HealthCollectionAttempt RetiredAttempt() => new(HealthCollectionDisposition.Retired, false, null);

    private void SetCollectionError(HealthMonitorEntry entry, HealthDiagnostic diagnostic, DateTimeOffset at)
    { entry.CollectionError = diagnostic; entry.CollectionErrorAt = at; revision++; }

    private void CloseAttachmentLocked(CollectionAttachment attachment, Effects effects)
    {
        if (attachment.Closed) { return; }
        attachment.Closed = true;
        if (ReferenceEquals(attachment.Entry.Collection, attachment.Target))
        {
            attachment.Entry.Collection = null;
            if (attachment.Entry.Generation < long.MaxValue) { attachment.Entry.Generation++; }
            revision++;
        }
        if (attachment.Operation is { } operation) { RequestCancellation(operation, effects); }
        else { effects.Complete.Add(attachment.Disposal); }
    }

    private static void RequestCancellation(CollectionOperation operation, Effects effects)
    {
        if (operation.CancellationRequested) { return; }
        operation.CancellationRequested = true;
        effects.Cancel.Add(operation);
    }

    private void CancelOperation(CollectionOperation operation)
    {
        // User cancellation handlers and diagnostic projection are deliberately outside gate.
        HealthDiagnostic? error = null;
        DateTimeOffset? errorAt = null;
        try { operation.Cancellation.Cancel(); }
        catch (Exception exception)
        {
            error = HealthDiagnostic.FromException(exception, "Healthcheck.Cancellation");
            try { errorAt = Now(); }
            catch (Exception) { /* A failing injected clock must not strand cancellation settlement. */ }
        }
        finally
        {
            var effects = new Effects();
            lock (gate)
            {
                operation.CancellationSettled = true;
                if (error is not null && errorAt.HasValue && IsCurrent(operation.Attachment)) { SetCollectionError(operation.Entry, error, errorAt.Value); }
                FinalizeOperationLocked(operation, effects);
            }
            Dispatch(effects);
        }
    }

    private void FinalizeOperationLocked(CollectionOperation operation, Effects effects)
    {
        if (operation.Finalized || !operation.CompletionCalled || (operation.CancellationRequested && !operation.CancellationSettled)) { return; }
        operation.Finalized = true;
        operations.Remove(operation.Reservation.OperationId);
        operation.Attachment.Operation = null;
        operation.Entry.PendingOperations--;
        if (!operation.Entry.Withdrawn) { revision++; }
        if (operation.Attachment.Closed) { effects.Complete.Add(operation.Attachment.Disposal); }
        if (operation.Entry.Withdrawn && operation.Entry.PendingOperations == 0) { effects.Complete.Add(operation.Entry.Disposal); }
        effects.Dispose.Add(operation.Cancellation);
        FinishService(effects);
    }

    private sealed class CollectionAttachment(HealthMonitorEntry entry, HealthCollectionTarget target)
    {
        internal readonly HealthMonitorEntry Entry = entry;
        internal readonly HealthCollectionTarget Target = target;
        internal readonly TaskCompletionSource Disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CollectionOperation? Operation;
        internal bool Closed;
    }

    private sealed class CollectionOperation(HealthStateCoordinator owner, CollectionAttachment attachment) : IHealthCollectionOperationHandle
    {
        internal readonly HealthStateCoordinator Owner = owner;
        internal readonly CollectionAttachment Attachment = attachment;
        internal HealthMonitorEntry Entry => Attachment.Entry;
        internal readonly CancellationTokenSource Cancellation = new();
        internal HealthCollectionReservation Reservation = null!;
        internal bool Entered, CompletionCalled, CancellationRequested, CancellationSettled, Finalized;
        internal HealthCollectionAttempt? Evaluation, Completion;
    }
}
