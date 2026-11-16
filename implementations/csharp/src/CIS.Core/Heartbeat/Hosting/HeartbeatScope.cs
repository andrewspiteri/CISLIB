using Microsoft.Extensions.DependencyInjection;

namespace CIS.Heartbeat.Hosting;

/// <summary>Owns an actual Microsoft DI scope and delays destructive scope disposal until its Heartbeat owner has drained.</summary>
/// <remarks>Resolve the subscriber and its dependencies from Services, and register it through Service. Plain DI scope disposal cannot supply this barrier.</remarks>
public sealed class HeartbeatScope : IAsyncDisposable
{
    private readonly HeartbeatOwner owner;
    private readonly AsyncServiceScope scope;
    private readonly TaskCompletionSource disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int disposalStarted;

    /// <summary>Creates a new actual lifetime boundary; it does not inherit instances from an unrelated DI scope.</summary>
    public HeartbeatScope(HeartbeatRuntime runtime, IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        owner = runtime.CreateOwner();
        try { scope = scopeFactory.CreateAsyncScope(); }
        catch
        {
            // No registration or hold exists yet, so closing this empty owner is synchronous.
            owner.CloseAsync().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>Gets the provider owning the actual subscriber and dependency instances.</summary>
    public IServiceProvider Services => scope.ServiceProvider;
    /// <summary>Gets the registration facade bound to this scope's lifetime owner.</summary>
    public IHeartbeatService Service => owner.Service;

    /// <summary>Closes and drains Heartbeat usage before initiating actual scope disposal; repeated calls join one operation.</summary>
    public ValueTask DisposeAsync()
    {
        owner.Runtime.CloseOwner(owner);
        HeartbeatExecutionContext.ThrowIfWaitingOn(owner.Runtime, ownerId: owner.Id);
        if (Interlocked.CompareExchange(ref disposalStarted, 1, 0) == 0) { _ = DisposeCoreAsync(); }
        return new(disposal.Task);
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await owner.CloseAsync().ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
            await owner.DisposeAsync().ConfigureAwait(false);
            disposal.TrySetResult();
        }
        catch (Exception error) { disposal.TrySetException(error); }
    }
}
