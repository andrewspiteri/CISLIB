using CIS.Heartbeat.Healthcheck;

namespace CIS.Heartbeat;

/// <summary>A cooperative lifetime boundary for subscribers and their existing dependencies.</summary>
/// <remarks>Retain this owner while recurrence is wanted. Await closure before disposing protected objects; closure does not dispose them.</remarks>
public sealed class HeartbeatOwner : IHeartbeatOwner
{
    internal readonly HeartbeatRuntime Runtime;
    internal readonly Guid Id = Guid.NewGuid();
    internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Registrations;
    internal int Holds;
    internal bool Closing;
    internal bool Disposed;

    internal HeartbeatOwner(HeartbeatRuntime runtime)
    {
        Runtime = runtime;
        Service = new OwnerService(this);
    }

    /// <summary>Gets the registration facade bound to this actual lifetime owner and the application's shared engine.</summary>
    public IHeartbeatService Service
    {
        get;
    }

    /// <summary>Closes admission and waits for accepted work and reporting retirement. Cancellation affects only this wait.</summary>
    public Task CloseAsync(CancellationToken waitCancellation = default)
    {
        Runtime.CloseOwner(this);
        HeartbeatExecutionContext.ThrowIfWaitingOn(Runtime, ownerId: Id);
        return Completion.Task.IsCompleted ? Completion.Task : Completion.Task.WaitAsync(waitCancellation);
    }

    /// <summary>Joins closure before the application's owner disposes its subscriber and dependencies.</summary>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        lock (Runtime.Gate)
        {
            Disposed = true;
        }
    }

    private sealed class OwnerService(HeartbeatOwner owner) : IHeartbeatHealthcheckService
    {
        public void Register(IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
            Func<CancellationToken, Task<bool>>? condition = null)
            => owner.Runtime.Register(owner, subscriber, options, condition);

        public void Register(IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
            HeartbeatHealthcheckOptions monitoring, Func<CancellationToken, Task<bool>>? condition = null)
            => HeartbeatHealthcheckExtensions.RegisterCore(owner, subscriber, options, monitoring, condition);

        public Task UnregisterAsync(IHeartbeatSubscriber subscriber, CancellationToken waitCancellation = default)
            => owner.Runtime.UnregisterAsync(subscriber, waitCancellation);
    }
}
