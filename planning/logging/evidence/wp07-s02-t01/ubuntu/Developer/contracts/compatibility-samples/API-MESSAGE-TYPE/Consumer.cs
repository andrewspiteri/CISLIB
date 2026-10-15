// Compile-only consumer shape; never execute against the reference-only specification.
#nullable enable
using CIS;
using CIS.Configuration;
using CIS.Logging;

internal static class ConstructionNames
{
    internal static LogWriteResult Use()
    {
        EnvironmentOptions.EnvironmentKind = EnvironmentKind.Console;
        EnvironmentOptions.Logging.MaxEntryBytes = 65536;
        CoreLogRuntime runtime = CoreLogRuntime.Create();
        CoreLogger logger = runtime.CreateLogger(component: "Orders");
        var data = new LogEventData { EventName = "order.created", Message = "Created" };
        var issue = new ConfigurationIssue(code: ConfigurationIssueCodes.InvalidToken, key: "CORE_LOG_LEVEL", explanation: "A token is invalid.");
        var exception = new CoreConfigurationException(issues: new[] { issue });
        return logger.Write(level: CoreLogLevel.Information, data: data);
    }
}
