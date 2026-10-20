// HC00 nonshipping facade. Never execute this type as runtime evidence.
namespace CIS.Healthcheck;

/// <summary>Compile-only shape of the explicitly controlled local pipe server.</summary>
public sealed class HealthPipeServer : IAsyncDisposable
{
    public HealthPipeServer(
        IHealthSnapshotSource source,
        HealthcheckOptions options,
        TimeProvider? timeProvider = null) => throw ReferenceOnly.Unavailable();

    public Task StartAsync(CancellationToken waitCancellation = default) =>
        throw ReferenceOnly.Unavailable();

    public Task StopAsync(CancellationToken waitCancellation = default) =>
        throw ReferenceOnly.Unavailable();

    public ValueTask DisposeAsync() => throw ReferenceOnly.Unavailable();
}
