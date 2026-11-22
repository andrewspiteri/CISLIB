namespace CIS.Coordination;

/// <summary>The managed engine lifecycle, independent of any business callback's completion.</summary>
public enum CoordinationRuntimeState
{
    /// <summary>Constructed without authority I/O.</summary>
    Created,
    /// <summary>Validating the selected authority.</summary>
    Starting,
    /// <summary>Accepting authorized acquisitions.</summary>
    Running,
    /// <summary>Admission is closed and actual owned work is being joined.</summary>
    Stopping,
    /// <summary>All runtime-owned physical requests and notifications settled.</summary>
    Stopped
}

/// <summary>A detached local ownership observation. It never authorizes protected effects.</summary>
/// <param name="Acquisition">The exact bounded detached acquisition.</param>
/// <param name="State">Current local state, including unpublished acquisition.</param>
/// <param name="Grant">The last confirmed grant, if any.</param>
/// <param name="LossReason">The stable terminal loss reason, if any.</param>
/// <param name="UnfinishedRequests">Actual physical provider requests still running.</param>
/// <param name="UnfinishedNotifications">Actual terminal cancellation notifications still running.</param>
public sealed record CoordinationLeaseSnapshot(CoordinationAcquisition Acquisition, CoordinationLeaseState State,
    CoordinationGrant? Grant, CoordinationLossReason? LossReason, int UnfinishedRequests, int UnfinishedNotifications);

/// <summary>A bounded passive page; collection performs no authority I/O and retains no history.</summary>
/// <param name="State">Current runtime lifecycle.</param>
/// <param name="ManagedReservations">Live or unresolved attempts/handles still charged.</param>
/// <param name="AcquisitionRequests">Actual acquire/resolve/release requests still running.</param>
/// <param name="RenewalRequests">Actual independent renewal requests still running.</param>
/// <param name="UnfinishedNotifications">Actual terminal cancellation callbacks still running.</param>
/// <param name="OwnedLeases">All current Owned or Draining handles, independent of paging.</param>
/// <param name="AuthorityAvailable">Last completed availability observation, or unknown before startup.</param>
/// <param name="TotalEntries">Current total entry count before paging.</param>
/// <param name="Entries">Detached bounded current entries.</param>
public sealed record CoordinationRuntimeSnapshot(CoordinationRuntimeState State, int ManagedReservations,
    int AcquisitionRequests, int RenewalRequests, int UnfinishedNotifications, int OwnedLeases, bool? AuthorityAvailable,
    int TotalEntries, IReadOnlyList<CoordinationLeaseSnapshot> Entries);

internal enum CoordinationRuntimeOperation
{
    Acquire,
    Renew,
    Release,
    Resolve
}

internal enum CoordinationRuntimeOperationOutcome
{
    Succeeded,
    Busy,
    Refused,
    Unavailable,
    Indeterminate,
    NotObserved,
    Cancelled,
    Faulted
}

// Only trusted library instrumentation attaches here. It is invoked outside scheduler/state gates.
internal interface ICoordinationRuntimeObserver
{
    ICoordinationRuntimeObserver CaptureOperationObserver() => this;

    void OperationCompleted(CoordinationRuntimeOperation operation, CoordinationRuntimeOperationOutcome outcome, TimeSpan duration);

    void Transition(CoordinationLeaseSnapshot snapshot);
}
