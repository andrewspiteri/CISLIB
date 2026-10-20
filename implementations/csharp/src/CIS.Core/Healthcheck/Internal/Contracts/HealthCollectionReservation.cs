using System.Collections.ObjectModel;
using System.Numerics;

namespace CIS.Healthcheck.Internal;

/// <summary>Opaque state-owned operation identity. Implementations contain state bookkeeping, never owner or collector references.</summary>
internal interface IHealthCollectionOperationHandle
{
}

/// <summary>Immutable observation baselines and lifetime cancellation for one actual collection wrapper.</summary>
internal sealed class HealthCollectionReservation
{
    internal HealthCollectionReservation(
        Guid operationId,
        Guid monitorId,
        Guid collectionRegistrationId,
        long generation,
        BigInteger healthObservationRevision,
        IReadOnlyDictionary<string, BigInteger> measurementObservationRevisions,
        CancellationToken lifetimeCancellation,
        IHealthCollectionOperationHandle stateHandle)
    {
        ArgumentNullException.ThrowIfNull(measurementObservationRevisions);
        ArgumentNullException.ThrowIfNull(stateHandle);
        if (measurementObservationRevisions.Count > 32)
        {
            throw new ArgumentException("A collection reservation supports at most 32 measurement baselines.", nameof(measurementObservationRevisions));
        }
        OperationId = operationId;
        MonitorId = monitorId;
        CollectionRegistrationId = collectionRegistrationId;
        Generation = generation;
        HealthObservationRevision = healthObservationRevision;
        var revisions = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
        foreach (var revision in measurementObservationRevisions)
        {
            revisions.Add(revision.Key, revision.Value);
        }
        MeasurementObservationRevisions = new ReadOnlyDictionary<string, BigInteger>(revisions);
        LifetimeCancellation = lifetimeCancellation;
        StateHandle = stateHandle;
    }

    internal Guid OperationId { get; }
    internal Guid MonitorId { get; }
    internal Guid CollectionRegistrationId { get; }
    internal long Generation { get; }
    internal BigInteger HealthObservationRevision { get; }
    internal IReadOnlyDictionary<string, BigInteger> MeasurementObservationRevisions { get; }
    internal CancellationToken LifetimeCancellation { get; }
    internal IHealthCollectionOperationHandle StateHandle { get; }
}

/// <summary>The atomic reservation result, including joining an already tracked operation.</summary>
internal enum HealthCollectionReservationDisposition
{
    Reserved = 0,
    Existing = 1,
    Retired = 2
}

/// <summary>A reservation decision. Existing returns the same operation identity used by the runner's shared task.</summary>
internal sealed class HealthCollectionReservationResult
{
    internal HealthCollectionReservationResult(
        HealthCollectionReservationDisposition disposition,
        HealthCollectionReservation? reservation)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if ((disposition == HealthCollectionReservationDisposition.Retired) != (reservation is null))
        {
            throw new ArgumentException("A live reservation decision requires its state-issued reservation.", nameof(reservation));
        }
        Disposition = disposition;
        Reservation = reservation;
    }

    internal HealthCollectionReservationDisposition Disposition { get; }
    internal HealthCollectionReservation? Reservation { get; }
}

/// <summary>Actual wrapper completion facts; none of these reasons is a fabricated callback failure.</summary>
internal enum HealthCollectionCompletionReason
{
    Settled = 0,
    RetiredBeforeInvocation = 1,
    OwnerUnavailable = 2,
    LeaseAcquisitionFault = 3
}

/// <summary>Detached completion and cleanup facts, reported only after any acquired owner lease's disposal settles.</summary>
internal sealed class HealthCollectionCompletion
{
    internal HealthCollectionCompletion(
        HealthCollectionCompletionReason reason,
        DateTimeOffset completedAtUtc,
        HealthDiagnostic? acquisitionError = null,
        HealthDiagnostic? cleanupError = null)
    {
        Reason = reason;
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        AcquisitionError = acquisitionError;
        CleanupError = cleanupError;
    }

    internal HealthCollectionCompletionReason Reason { get; }
    internal DateTimeOffset CompletedAtUtc { get; }
    internal HealthDiagnostic? AcquisitionError { get; }
    internal HealthDiagnostic? CleanupError { get; }
}
