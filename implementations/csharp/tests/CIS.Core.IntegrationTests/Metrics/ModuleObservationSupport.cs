using CIS.Configuration;
using CIS.Configuration.Hosting;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.Metrics.OpenTelemetry;
using CIS.Tests.IntegrationTests.Startup;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.IntegrationTests.Metrics;

// Small shared setup over the existing actual StartupHost. The writer borrows no
// stream, and this helper creates no alternate provider, scheduler or host runner.
internal static class ModuleObservationSupport
{
    internal static CoreLogRuntime QuietLogging(QuietWriter writer)
    {
        LoggingOptions settings = EnvironmentOptions.Logging;
        CoreLogLevel? console = settings.ConsoleLevel, file = settings.FileLevel;
        string? filePath = settings.FilePath;
        try
        {
            settings.ConsoleLevel = settings.FileLevel = CoreLogLevel.None;
            return CoreLogRuntime.Create([new LogWriterRegistration("metrics-observation", writer)]);
        }
        finally { settings.ConsoleLevel = console; settings.FileLevel = file; settings.FilePath = filePath; }
    }

    internal static StartupHost Host(ITypeManager manager, ConfigurationRuntime configuration,
        CoreLoggerProvider provider, bool defaultCache)
        => new(services =>
        {
            services.AddSingleton(manager);
            services.AddCisConfiguration(configuration);
            // Exercise the existing borrowing provider, without another runtime DI alias.
            services.AddSingleton<ILoggerProvider>(provider);
            services.Configure<LoggerFilterOptions>(options => { options.Rules.Clear(); options.MinLevel = LogLevel.None; });
            services.AddCisMetrics(options => { if (!defaultCache) { options.ScrapeCacheDuration = TimeSpan.Zero; } });
        }, configureApp: app => { app.UseRouting(); app.UseCisMetrics(); });

    internal static double Sum(string text, string name)
        => CollectionText.Samples(text).Where(sample => sample.StartsWith(name + "{", StringComparison.Ordinal))
            .Sum(CollectionText.Value);

    internal static async Task StopLoggingAsync(CoreLogRuntime runtime)
    {
        await runtime.StopAsync(CancellationToken.None).WaitAsync(StartupHost.Deadline);
        await runtime.Completion.WaitAsync(StartupHost.Deadline);
        await runtime.DisposeAsync();
    }
}

internal sealed class QuietWriter : ILogWriter
{
    private int writes;
    internal int Writes => Volatile.Read(ref writes);
    public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
    { Interlocked.Increment(ref writes); return ValueTask.CompletedTask; }
    public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
