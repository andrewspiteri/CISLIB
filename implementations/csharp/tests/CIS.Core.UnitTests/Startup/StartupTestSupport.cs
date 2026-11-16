using System.Collections.Concurrent;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Startup;

internal static class StartupTestSupport
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    internal static ServiceProvider BuildProvider(ServiceCollection services, bool validateOnBuild = true)
        => services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = validateOnBuild });

    internal static TaskCompletionSource Signal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static async Task<Exception> ObserveFailureAsync(Task operation)
    {
        try { await operation.WaitAsync(Deadline); }
        catch (Exception error) { return error; }
        throw new AssertFailedException("The original startup operation unexpectedly succeeded.");
    }

    internal static async Task StopAndDisposeAsync(StartupCoordinator coordinator, ServiceProvider provider)
    {
        // A failed/canceled startup task is still joined by StopAsync. Only its
        // actual barrier success permits destructive root-provider disposal.
        await coordinator.StopAsync().WaitAsync(Deadline);
        await coordinator.DisposeAsync().AsTask().WaitAsync(Deadline);
        await provider.DisposeAsync();
    }
}

internal class StartupProbe : IStartable
{
    private int calls;
    internal Func<CancellationToken, Task> Start { get; set; } = static _ => Task.CompletedTask;
    internal ConcurrentQueue<CancellationToken> Tokens { get; } = new();
    internal int Calls => Volatile.Read(ref calls);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        Tokens.Enqueue(cancellationToken);
        return Start(cancellationToken);
    }
}

internal sealed class EqualStartupProbe : StartupProbe
{
    public override bool Equals(object? obj) => obj is EqualStartupProbe;
    public override int GetHashCode() => throw new InvalidOperationException("Startup identity cannot call subscriber hash code.");
}
