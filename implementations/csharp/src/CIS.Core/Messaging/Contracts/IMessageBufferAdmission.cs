namespace CIS.Messaging;

/// <summary>The selected module's shared encoded-byte admission boundary for optional native adapters.</summary>
/// <remarks>All module/provider working copies use the same process-wide budget. Native client overhead is separately documented.</remarks>
public interface IMessageBufferAdmission
{
    /// <summary>Reserves bytes before allocation and returns an idempotent release lease.</summary>
    /// <remarks>Retain the lease until the owned copy and its native operation actually settle. Cancellation must not release an in-use buffer.</remarks>
    ValueTask<IDisposable> AcquireAsync(int bytes, CancellationToken cancellationToken = default);
}
