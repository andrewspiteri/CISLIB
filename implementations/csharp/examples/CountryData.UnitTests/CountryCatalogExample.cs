using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Examples.CountryData;
using CIS.Metrics;
using Microsoft.Extensions.Hosting;

namespace CIS.Examples.CountryData.UnitTests;

// Test-owned dependencies for the real public catalog. No host, HTTP or exporter starts.
internal sealed class CountryCatalogExample : IAsyncDisposable
{
    internal const string InitialData = """
        {"error":false,"data":[{"name":"Malta","Iso2":"mt","Iso3":"mlt"}]}
        """;

    private readonly ExampleLifetime lifetime = new();

    internal ScriptedRestClient Rest { get; } = new(capacity: 16);
    internal ManualHeartbeat Heartbeat { get; } = new(capacity: 8);
    internal RecordingHealthMonitorFactory Health { get; } = new(reportCapacity: 16);
    internal RecordingLogger<CountryCatalog> Logger { get; } = new(capacity: 16);
    internal ManualTimeProvider Clock { get; } = new(DateTimeOffset.UnixEpoch);
    internal CountryCatalog Catalog
    {
        get;
    }
    internal RecordingHealthMonitor Monitor => Health.Monitors.Single();

    internal CountryCatalogExample(CountrySettings? settings = null)
    {
        settings ??= new CountrySettings
        {
            SourceUrl = "https://provider.invalid/countries",
            RetryPolicyName = null
        };

        Catalog = new CountryCatalog(Rest, settings, new InitializedSettingsReader(),
            Heartbeat, Health, DisabledMetrics.Instance, lifetime, Logger, Clock, []);
    }

    internal async Task StartAsync()
    {
        Rest.EnqueueJsonValue(InitialData);
        await Catalog.StartAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Catalog.DisposeAsync();
        }
        finally
        {
            try
            {
                await Heartbeat.DisposeAsync();
            }
            finally
            {
                lifetime.Dispose();
            }
        }
    }

    private sealed class InitializedSettingsReader : IConfigurationReader
    {
        public TResult Read<TConfiguration, TResult>(TConfiguration configuration,
            Func<TConfiguration, TResult> reader)
            where TConfiguration : class, IConfigurationObject
        {
            return reader(configuration);
        }
    }

    private sealed class ExampleLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            stopping.Cancel();
        }

        public void Dispose()
        {
            stopping.Dispose();
        }
    }
}
