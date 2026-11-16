namespace CIS.Heartbeat;

/// <summary>Performs recurring asynchronous work using the dependencies of its actual lifetime owner.</summary>
/// <remarks>
/// Each returned task must include all child work and cleanup that can use the subscriber or its dependencies.
/// Heartbeat prevents overlap for a registered instance through actual completion, including error handling.
/// Detached work and independent direct calls are outside that guarantee.
/// </remarks>
public interface IHeartbeatSubscriber
{
    /// <summary>Performs one invocation, returning true for successful work and false for an ordinary negative result.</summary>
    /// <param name="cancellationToken">The execution token, independent of unregister and shutdown wait cancellation.</param>
    /// <returns>A nonnull task representing the entire invocation and its subscriber-owned cleanup.</returns>
    /// <remarks>A false result does not invoke error handling. Application shutdown does not automatically cancel this token.</remarks>
    Task<bool> OnElapsedAsync(CancellationToken cancellationToken);

    /// <summary>Handles an exception escaping the condition or callback while the owner remains usable.</summary>
    /// <param name="exception">The originating exception; successful handling does not replace the failed execution result.</param>
    /// <returns>A nonnull task representing all recovery work and its cleanup.</returns>
    /// <remarks>Heartbeat awaits this task within the same execution. Handler faults are secondary and do not recursively invoke this method.</remarks>
    Task OnErrorAsync(Exception exception);
}
