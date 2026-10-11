using CIS.Logging;
using CIS.Logging.Configuration;

namespace CIS.Configuration;

internal sealed class StartupInputCapture
{
    private StartupInputCapture(ApplicationIdentityInput identity, EnvironmentConfigurationInput configuration)
    {
        Identity = identity;
        Configuration = configuration;
    }

    public ApplicationIdentityInput Identity { get; }
    public EnvironmentConfigurationInput Configuration { get; }

    public static StartupInputCapture Capture(EnvironmentSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        string? applicationName = setup.ApplicationName;
        LoggingSetup? logging = setup.Logging;
        var identity = new ApplicationIdentityInput
        {
            ApplicationAssembly = setup.ApplicationAssembly,
            ApplicationName = applicationName,
            BuildVersion = setup.BuildVersion,
            CommitId = setup.CommitId,
            BuildId = setup.BuildId
        };
        var configuration = new EnvironmentConfigurationInput
        {
            ApplicationName = applicationName,
            ServiceVersion = setup.ServiceVersion,
            EnvironmentName = setup.EnvironmentName,
            EnvironmentKind = (ConfigurationEnvironmentKind)setup.EnvironmentKind,
            Logging = new LoggingConfigurationInput
            {
                Level = (ConfigurationSeverity?)logging?.Level,
                ConsoleFormat = (ConfigurationOutputFormat?)logging?.ConsoleFormat,
                ConsoleLevel = (ConfigurationSeverity?)logging?.ConsoleLevel,
                ConsoleStream = (ConfigurationConsoleStream?)logging?.ConsoleStream,
                HumanDetail = (ConfigurationHumanDetail?)logging?.HumanDetail,
                FilePath = logging?.FilePath,
                FileFormat = (ConfigurationOutputFormat?)logging?.FileFormat,
                FileLevel = (ConfigurationSeverity?)logging?.FileLevel,
                QueueCapacity = logging?.QueueCapacity,
                FlushIntervalMilliseconds = logging?.FlushIntervalMilliseconds,
                ShutdownTimeoutMilliseconds = logging?.ShutdownTimeoutMilliseconds,
                FileMaxBytes = logging?.FileMaxBytes,
                FileRetainedFiles = logging?.FileRetainedFiles,
                SchemaVersion = logging?.SchemaVersion,
                MaxEntryBytes = logging?.MaxEntryBytes
            }
        };
        return new StartupInputCapture(identity, configuration);
    }
}
