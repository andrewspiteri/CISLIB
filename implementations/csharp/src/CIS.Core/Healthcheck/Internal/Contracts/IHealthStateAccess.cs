namespace CIS.Healthcheck.Internal;

/// <summary>Connects Core-owned monitor handles and the service factory to their fixed state coordinator.</summary>
/// <remarks>
/// Implement explicitly on the service and its monitor handles. Both properties return stable
/// service-lifetime state identities and are read outside the state gate. The registry must not
/// retain this access object or a caller-owned monitor graph. This is not a public adapter seam.
/// </remarks>
internal interface IHealthStateAccess
{
    /// <summary>Gets the owning collection coordinator, also used for runner ownership checks.</summary>
    IHealthCollectionState CollectionState { get; }

    /// <summary>Gets the owning execution coordinator for atomic binding without a global lookup.</summary>
    IHealthExecutionState ExecutionState { get; }
}
