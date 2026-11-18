namespace CIS.Heartbeat;

/// <summary>Creates explicit registration lifetimes on the application's selected Heartbeat service.</summary>
/// <remarks>Replacing this business-facing factory does not replace hosted scheduler startup or shutdown.</remarks>
public interface IHeartbeatOwnerFactory
{
    /// <summary>Creates an owner whose actual accepted work must settle before its dependencies are disposed.</summary>
    IHeartbeatOwner CreateOwner();
}

/// <summary>Closes registration admission and joins actual accepted work without disposing subscribers.</summary>
public interface IHeartbeatOwner : IAsyncDisposable
{
    /// <summary>Gets the service bound to this registration lifetime.</summary>
    IHeartbeatService Service
    {
        get;
    }

    /// <summary>Closes admission and joins actual work; cancellation affects only this caller's wait.</summary>
    /// <remarks>A canceled wait is not proof of settlement. Repeated close and disposal join the same lifetime.</remarks>
    Task CloseAsync(CancellationToken waitCancellation = default);
}
