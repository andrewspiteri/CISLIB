using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Examples.CountryData;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.IntegrationTests.CountryData;

// Only application reporting is under test here. The public manual helpers do
// not establish production scheduling, health policy, HTTP, retry or drain proof.
internal sealed class CountryDiagnosticFixture : IAsyncDisposable
{
    internal const string Malta = "{\"error\":false,\"data\":[{\"name\":\"Malta\",\"Iso2\":\"MT\",\"Iso3\":\"MLT\"}]}";
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    private readonly ServiceProvider metrics;
    internal ScriptedRestClient Rest { get; } = new(capacity: 32);
    internal ManualHeartbeat Heartbeat { get; } = new();
    internal RecordingHealthMonitorFactory Health { get; } = new(reportCapacity: 32);
    internal ControlledCountryLogger Logger { get; } = new();
    internal DiagnosticClock Clock { get; } = new();
    internal ControlledLifetime Lifetime { get; } = new();
    internal CountryCatalog Catalog
    {
        get;
    }
    internal RecordingHealthMonitor Monitor => Health.Monitors.Single();

    internal CountryDiagnosticFixture()
    {
        // Reuse the existing public disabled Metrics registration, without an
        // exporter/provider or a second implementation of its handle contracts.
        var services = new ServiceCollection();
        services.AddCisMetrics(options => options.Enabled = false);
        metrics = services.BuildServiceProvider();
        var settings = new CountrySettings
        {
            SourceUrl = "https://provider.invalid/countries",
            RetryPolicyName = null
        };
        Catalog = new(Rest, settings, new DirectReader(), Heartbeat, Health,
            metrics.GetRequiredService<CIS.Metrics.IMetrics>(), Lifetime, Logger, Clock, []);
    }

    internal async Task StartAsync()
    {
        Rest.EnqueueJsonValue(Malta);
        await Catalog.StartAsync(CancellationToken.None).WaitAsync(Deadline);
    }

    internal Task<ManualHeartbeatResult> SucceedAsync()
    {
        Rest.EnqueueJsonValue(Malta);
        return Heartbeat.TriggerAsync(Catalog);
    }

    internal Task<ManualHeartbeatResult> FailAsync()
    {
        Rest.EnqueueFailure(RestFailureKind.Business, "country.provider_rejected", "The provider rejected this read.", 200);
        return Heartbeat.TriggerAsync(Catalog);
    }

    internal void ClearLogs()
    {
        Logger.Recording.Clear();
        Logger.ResetWriteAttempts();
    }

    public async ValueTask DisposeAsync()
    {
        // Fault controls apply only to hot-path diagnostics, not startup/stop
        // milestone logging or ownership cleanup.
        Logger.ThrowEnabled = false;
        Logger.ThrowWrite = false;
        Clock.ThrowTimestamp = false;
        Clock.ThrowFrequency = false;
        await Catalog.DisposeAsync().AsTask().WaitAsync(Deadline);
        await Heartbeat.DisposeAsync().AsTask().WaitAsync(Deadline);
        metrics.Dispose();
        Lifetime.Dispose();
    }

    private sealed class DirectReader : IConfigurationReader
    {
        public TResult Read<TConfiguration, TResult>(TConfiguration configuration, Func<TConfiguration, TResult> reader)
            where TConfiguration : class, IConfigurationObject
        {
            return reader(configuration);
        }
    }
}

internal sealed class DiagnosticClock : TimeProvider
{
    private readonly ManualTimeProvider manual = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private int timestampReads;

    internal bool ThrowTimestamp
    {
        get; set;
    }
    internal bool ThrowFrequency
    {
        get; set;
    }
    internal int TimestampReads => Volatile.Read(ref timestampReads);

    public override long TimestampFrequency
    {
        get
        {
            if (ThrowFrequency)
            {
                throw new InvalidOperationException("The reporting frequency is unavailable.");
            }
            return manual.TimestampFrequency;
        }
    }

    public override long GetTimestamp()
    {
        Interlocked.Increment(ref timestampReads);
        if (ThrowTimestamp)
        {
            throw new InvalidOperationException("The reporting timestamp is unavailable.");
        }
        return manual.GetTimestamp();
    }

    public override DateTimeOffset GetUtcNow()
    {
        return manual.GetUtcNow();
    }

    public override System.Threading.ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        return manual.CreateTimer(callback, state, dueTime, period);
    }

    internal void Advance(TimeSpan elapsed)
    {
        manual.Advance(elapsed);
    }
    internal void SetUtcNow(DateTimeOffset value)
    {
        manual.SetUtcNow(value);
    }
}

internal sealed class ControlledCountryLogger : ILogger<CountryCatalog>
{
    private int writeAttempts;

    internal RecordingLogger<CountryCatalog> Recording { get; } = new(capacity: 32);
    internal bool ThrowEnabled
    {
        get; set;
    }
    internal bool ThrowWrite
    {
        get; set;
    }
    internal int WriteAttempts => Volatile.Read(ref writeAttempts);
    internal void ResetWriteAttempts()
    {
        Interlocked.Exchange(ref writeAttempts, 0);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return Recording.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        if (ThrowEnabled)
        {
            throw new InvalidOperationException("The reporting logger cannot query its level.");
        }
        return Recording.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Interlocked.Increment(ref writeAttempts);
        if (ThrowWrite)
        {
            throw new InvalidOperationException("The reporting logger cannot write.");
        }
        Recording.Log(logLevel, eventId, state, exception, formatter);
    }
}

internal sealed class ControlledLifetime : IHostApplicationLifetime, IDisposable
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
