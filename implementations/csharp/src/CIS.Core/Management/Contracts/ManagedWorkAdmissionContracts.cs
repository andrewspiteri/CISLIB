namespace CIS.Management;

/// <summary>Local managed-execution lifecycle, independent of process liveness and reported presence.</summary>
public enum ManagedWorkAdmissionState
{
    /// <summary>No live authoritative bootstrap has completed for this process.</summary>
    AwaitingBootstrap,
    /// <summary>Current confirmation and required local prerequisites permit a new finite admission.</summary>
    Ready,
    /// <summary>Retained state remains usable for settlement, while new work is refused.</summary>
    ContinuingOnly,
    /// <summary>Current authority is being reconciled through the owning modules before admission resumes.</summary>
    Reconciling,
    /// <summary>Shutdown has permanently closed this process's new-work admission.</summary>
    Stopping
}

/// <summary>A bounded local observation, never a lease, business authorization or reusable admission permit.</summary>
/// <param name="State">The local managed-execution lifecycle.</param>
/// <param name="AdmissionAllowed">Whether this capture's freshness and prerequisites currently permit a new admission.</param>
/// <param name="Revision">A nonnegative local publication revision, unrelated to the opaque remote control revision.</param>
/// <param name="ReasonCode">An optional finite safe local reason.</param>
/// <param name="AuthorityId">The currently bound management namespace UUID, if established.</param>
/// <param name="RegistrationId">The currently bound process registration UUID, if established.</param>
/// <param name="ControlRevision">The exact opaque control revision, if established.</param>
public sealed record ManagedWorkAdmissionSnapshot(ManagedWorkAdmissionState State, bool AdmissionAllowed, long Revision,
    string? ReasonCode = null, string? AuthorityId = null, string? RegistrationId = null, string? ControlRevision = null);

/// <summary>One immutable local publication; consumers recheck the port before admitting new work.</summary>
public sealed class ManagedWorkAdmissionChangedEventArgs : EventArgs
{
    /// <summary>Captures the published observation without conferring continuing freshness or authority.</summary>
    public ManagedWorkAdmissionChangedEventArgs(ManagedWorkAdmissionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
    }

    /// <summary>Gets this publication's exact observation.</summary>
    public ManagedWorkAdmissionSnapshot Snapshot
    {
        get;
    }
}

/// <summary>The optional local new-work gate; its absence does not select managed execution.</summary>
/// <remarks>A selected implementation starts closed. It evaluates freshness and required local prerequisites on each
/// check, independently of timer scheduling. Closing it does not cancel or revoke already admitted finite work.</remarks>
public interface IManagedWorkAdmission
{
    /// <summary>Checks current permission at the caller's finite admission point; no reusable permit is returned.</summary>
    bool TryAdmit();

    /// <summary>Captures current lifecycle/freshness facts; a retained snapshot cannot authorize a later admission.</summary>
    ManagedWorkAdmissionSnapshot CaptureSnapshot();

    /// <summary>Observes local publications outside the gate; listeners must not perform blocking work or infer later freshness.</summary>
    event EventHandler<ManagedWorkAdmissionChangedEventArgs>? Changed;
}

/// <summary>Reconciles one existing module's state without publishing configuration or replacing its ownership.</summary>
public interface IManagedStateReconciler
{
    /// <summary>Reads the module's current prerequisite without I/O or work admission.</summary>
    bool IsReady
    {
        get;
    }

    /// <summary>Reconciles current authoritative facts; false retains prior usable state and a closed admission gate.</summary>
    /// <remarks>The host invokes selected reconcilers sequentially and joins actual work. Unexpected defects remain faults.</remarks>
    Task<bool> ReconcileAsync(ManagementControlConfirmation confirmation, CancellationToken cancellationToken = default);
}
