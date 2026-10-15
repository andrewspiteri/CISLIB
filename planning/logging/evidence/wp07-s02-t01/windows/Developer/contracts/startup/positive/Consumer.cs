using CIS;
using CIS.Configuration;
using CIS.Logging;

public static class Consumer
{
    public static EventMetadata Prepare()
    {
        EnvironmentOptions.EnvironmentKind = EnvironmentKind.Service;
        EnvironmentOptions.CommitId = "abc123";
        EnvironmentOptions.BuildId = "build-42";
        LoggingOptions logging = EnvironmentOptions.Logging;
        logging.Level = CoreLogLevel.Information;
        logging.ConsoleFormat = LogOutputFormat.Human;
        logging.ConsoleLevel = CoreLogLevel.Warning;
        logging.ConsoleStream = ConsoleOutputStream.StandardError;
        logging.HumanDetail = HumanOutputDetail.Verbose;
        logging.FilePath = "logs/app.jsonl";
        logging.FileFormat = LogOutputFormat.JsonLines;
        logging.FileLevel = CoreLogLevel.Debug;
        logging.MaxEntryBytes = 65536;
        logging.QueueCapacity = 1024;
        logging.FlushIntervalMilliseconds = 1000;
        logging.ShutdownTimeoutMilliseconds = 5000;
        logging.FileMaxBytes = 10485760L;
        logging.FileRetainedFiles = 10;
        logging.SchemaVersion = 1;
        logging.Prepare(System.IO.Directory.GetCurrentDirectory());
        var issue = new ConfigurationIssue(code: ConfigurationIssueCodes.InvalidToken, key: "CORE_LOG_LEVEL", explanation: "A token is invalid.");
        _ = new CoreConfigurationException(issues: new[] { issue });
        return new EventMetadata();
    }
}
