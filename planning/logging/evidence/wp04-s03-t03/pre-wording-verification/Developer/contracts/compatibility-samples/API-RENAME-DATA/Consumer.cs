// Compile-only consumer shape; never execute against the reference-only specification.
#nullable enable
using CIS;
using CIS.Configuration;
using CIS.Logging;

internal static class ConstructionNames
{
    internal static LogWriteResult Use()
    {
        var setup = new EnvironmentSetup
        {
            ApplicationName = "Compatibility.Consumer",
            Logging = new LoggingSetup { MaxEntryBytes = 65536 }
        };
        EnvironmentSnapshot snapshot = EnvironmentOptions.Initialize(setup: setup);
        CoreLogRuntime runtime = CoreLogRuntime.Create(environment: snapshot);
        CoreLogger logger = runtime.CreateLogger(component: "Orders");
        var data = new LogEventData { EventName = "order.created", Message = "Created" };
        var issue = new ConfigurationIssue(code: ConfigurationIssueCodes.InvalidToken, key: "CORE_LOG_LEVEL", explanation: "A token is invalid.");
        var exception = new CoreConfigurationException(issues: new[] { issue });
        return logger.Write(level: CoreLogLevel.Information, data: data);
    }
}
