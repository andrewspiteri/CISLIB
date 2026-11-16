namespace CIS.Heartbeat;

/// <summary>Registers existing subscriber instances with the application's shared scheduler through their actual owner.</summary>
/// <remarks>The subscriber instance is the handle. Application infrastructure owns readiness, shutdown and disposal ordering.</remarks>
public interface IHeartbeatService
{
    /// <summary>Captures a recurring schedule, or successfully leaves an existing registration unchanged.</summary>
    /// <param name="subscriber">The fully initialized instance, identified by reference rather than equality, type or name.</param>
    /// <param name="options">Schedule input copied and validated before registration changes.</param>
    /// <param name="condition">An optional asynchronous condition retained while the subscriber is alive.</param>
    /// <remarks>
    /// Valid duplicates are no-ops, including during individual unregistration. Validation still applies to duplicates,
    /// and global stopping or disposal rejects them. Registration never invokes subscriber code inline.
    /// The actual owner must retain the subscriber for its intended lifetime and drain before disposing its dependencies.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The subscriber or required options is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A duration exceeds its documented bounds.</exception>
    /// <exception cref="ArgumentException">The diagnostic name is malformed or exceeds its documented bound.</exception>
    /// <exception cref="InvalidOperationException">The application is stopping or the registration owner is closing.</exception>
    /// <exception cref="ObjectDisposedException">The application runtime or registration owner is disposed.</exception>
    void Register(IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
        Func<CancellationToken, Task<bool>>? condition = null);

    /// <summary>Closes new acceptance and waits for that registration's accepted work to actually settle.</summary>
    /// <param name="subscriber">The original instance; an unknown or already removed instance is a successful no-op.</param>
    /// <param name="waitCancellation">Cancels only this caller's pending wait, after establishing the registration cutoff.</param>
    /// <returns>The shared drain observed by this caller; completion permits sequential registration reuse.</returns>
    /// <remarks>
    /// Ordinary unregistration drains accepted queued work. Application shutdown overrides that selection by suppressing
    /// unstarted work. Error handling and cleanup remain included. This operation never disposes the subscriber or its owner.
    /// A canceled wait does not prove settlement; never await a drain containing the current execution.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The subscriber is null.</exception>
    /// <exception cref="OperationCanceledException">This caller's wait is canceled while work remains pending.</exception>
    /// <exception cref="InvalidOperationException">The operation would wait for its own execution.</exception>
    Task UnregisterAsync(IHeartbeatSubscriber subscriber, CancellationToken waitCancellation = default);
}
