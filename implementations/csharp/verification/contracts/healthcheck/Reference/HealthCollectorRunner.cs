// HC00 nonshipping facade. Never execute this type as runtime evidence.
namespace CIS.Healthcheck;

/// <summary>Compile-only shape of the optional application-owned collector runner.</summary>
public sealed class HealthCollectorRunner : IAsyncDisposable
{
    public HealthCollectorRunner(HealthcheckService service) => throw ReferenceOnly.Unavailable();

    public Task StartAsync(CancellationToken waitCancellation = default) =>
        throw ReferenceOnly.Unavailable();

    public IHealthCollectionRegistration Register(
        IHealthMonitor monitor,
        IHealthCollector collector,
        HealthCollectorOptions options) => throw ReferenceOnly.Unavailable();

    public Task StopAsync(CancellationToken waitCancellation = default) =>
        throw ReferenceOnly.Unavailable();

    public ValueTask DisposeAsync() => throw ReferenceOnly.Unavailable();
}
