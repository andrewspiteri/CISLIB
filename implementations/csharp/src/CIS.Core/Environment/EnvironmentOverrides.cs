using System.Globalization;
using CIS.Logging;
using CIS.Logging.Configuration;
using CIS.Text;

namespace CIS.Configuration;

/// <summary>Applies known environment overrides during container construction.</summary>
/// <remarks>Collects safe diagnostics for the constructor. Invalid selected overrides never select lower-priority aliases.</remarks>
internal static class EnvironmentOverrides
{
    internal static (string ApplicationName, string EnvironmentName, string ServiceVersion) Apply(
        Func<string, string?> read, string applicationName, string environmentName, string serviceVersion, LoggingOptions logging, List<ConfigurationIssue> issues)
    {
        applicationName = Identity(read, "CORE_SERVICE_NAME", "DD_SERVICE", applicationName, 255, issues);
        environmentName = Identity(read, "CORE_ENVIRONMENT", "DD_ENV", environmentName, 255, issues);
        serviceVersion = Identity(read, "CORE_SERVICE_VERSION", "DD_VERSION", serviceVersion, 512, issues);
        logging.Level = Level(Token(read, "CORE_LOG_LEVEL"), "CORE_LOG_LEVEL", logging.Level, issues);
        logging.ConsoleFormat = Format(Token(read, "CORE_LOG_CONSOLE_FORMAT"), "CORE_LOG_CONSOLE_FORMAT", logging.ConsoleFormat, issues);
        logging.ConsoleLevel = Level(Token(read, "CORE_LOG_CONSOLE_LEVEL"), "CORE_LOG_CONSOLE_LEVEL", logging.ConsoleLevel, issues);
        logging.ConsoleStream = Stream(Token(read, "CORE_LOG_CONSOLE_STREAM"), logging.ConsoleStream, issues);
        logging.HumanDetail = Detail(Token(read, "CORE_LOG_HUMAN_DETAIL"), logging.HumanDetail, issues);
        string? path = Token(read, "CORE_LOG_FILE_PATH");
        if (path is not null)
        {
            logging.FilePath = path;
        }
        logging.FileFormat = Format(Token(read, "CORE_LOG_FILE_FORMAT"), "CORE_LOG_FILE_FORMAT", logging.FileFormat, issues);
        logging.FileLevel = Level(Token(read, "CORE_LOG_FILE_LEVEL"), "CORE_LOG_FILE_LEVEL", logging.FileLevel, issues);
        logging.QueueCapacity = (int)Integer(read, "CORE_LOG_QUEUE_CAPACITY", logging.QueueCapacity, minimum: LoggingOptions.MinimumQueueCapacity, maximum: LoggingOptions.MaximumQueueCapacity, issues: issues);
        logging.FlushIntervalMilliseconds = (int)Integer(read, "CORE_LOG_FLUSH_INTERVAL_MS", logging.FlushIntervalMilliseconds, minimum: LoggingOptions.MinimumFlushIntervalMilliseconds, maximum: LoggingOptions.MaximumFlushIntervalMilliseconds, issues: issues);
        logging.ShutdownTimeoutMilliseconds = (int)Integer(read, "CORE_LOG_SHUTDOWN_TIMEOUT_MS", logging.ShutdownTimeoutMilliseconds, minimum: LoggingOptions.MinimumShutdownTimeoutMilliseconds, maximum: LoggingOptions.MaximumShutdownTimeoutMilliseconds, issues: issues);
        logging.FileMaxBytes = Integer(read, "CORE_LOG_FILE_MAX_BYTES", logging.FileMaxBytes, minimum: LoggingOptions.MinimumFileMaxBytes, maximum: LoggingOptions.MaximumFileMaxBytes, issues: issues);
        logging.FileRetainedFiles = (int)Integer(read, "CORE_LOG_FILE_RETAINED_FILES", logging.FileRetainedFiles, minimum: LoggingOptions.MinimumFileRetainedFiles, maximum: LoggingOptions.MaximumFileRetainedFiles, issues: issues);
        logging.SchemaVersion = Schema(read, logging.SchemaVersion, issues);
        logging.MaxEntryBytes = (int)Integer(read, "CORE_LOG_MAX_ENTRY_BYTES", logging.MaxEntryBytes, minimum: LoggingOptions.MinimumMaxEntryBytes, maximum: LoggingOptions.MaximumMaxEntryBytes, issues: issues);
        return (applicationName, environmentName, serviceVersion);
    }

    private static string? Token(Func<string, string?> read, string key)
    {
        string? value = read(key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Identity(Func<string, string?> read, string key, string alias, string value, int maximum, List<ConfigurationIssue> issues)
    {
        string? selected = Token(read, key);
        string source = key;
        if (selected is null)
        {
            selected = Token(read, alias);
            source = alias;
        }
        if (selected is null)
        {
            return value;
        }
        if (!UnicodeScalarText.IsValid(selected, maximum, allowControls: false))
        {
            issues.Add(new(ConfigurationIssueCodes.InvalidIdentity, source, "Supply a bounded identity without controls or malformed Unicode."));
            return value;
        }
        return selected;
    }

    private static CoreLogLevel Level(string? token, string key, CoreLogLevel value, List<ConfigurationIssue> issues)
        => Level(token, key, (CoreLogLevel?)value, issues)!.Value;

    private static CoreLogLevel? Level(string? token, string key, CoreLogLevel? value, List<ConfigurationIssue> issues)
    {
        if (token is null)
        {
            return value;
        }
        if (SeverityTokenParser.Parse(token, out CoreLogLevel? parsed) == ConfigurationTokenState.Valid)
        {
            return parsed;
        }
        issues.Add(new(ConfigurationIssueCodes.InvalidToken, key,
            "Use Trace, Debug, Information, Warning, Error, Critical or None (aliases TRC, DBG, INF/INFO, WRN/WARN, ERR, CRT/CRIT)."));
        return value;
    }

    private static LogOutputFormat Format(string? token, string key, LogOutputFormat value, List<ConfigurationIssue> issues)
    {
        if (token is null)
        {
            return value;
        }
        switch (token.ToLowerInvariant())
        {
            case "human":
                return LogOutputFormat.Human;
            case "jsonl":
                return LogOutputFormat.JsonLines;
            default:
                InvalidToken(key, issues, "Use human or jsonl.");
                return value;
        }
    }

    private static ConsoleOutputStream Stream(string? token, ConsoleOutputStream value, List<ConfigurationIssue> issues)
    {
        if (token is null)
        {
            return value;
        }
        switch (token.ToLowerInvariant())
        {
            case "stderr":
                return ConsoleOutputStream.StandardError;
            case "stdout":
                return ConsoleOutputStream.StandardOutput;
            default:
                InvalidToken("CORE_LOG_CONSOLE_STREAM", issues, "Use stderr or stdout.");
                return value;
        }
    }

    private static HumanOutputDetail Detail(string? token, HumanOutputDetail value, List<ConfigurationIssue> issues)
    {
        if (token is null)
        {
            return value;
        }
        switch (token.ToLowerInvariant())
        {
            case "normal":
                return HumanOutputDetail.Normal;
            case "verbose":
                return HumanOutputDetail.Verbose;
            default:
                InvalidToken("CORE_LOG_HUMAN_DETAIL", issues, "Use normal or verbose.");
                return value;
        }
    }

    private static long Integer(Func<string, string?> read, string key, long value, long minimum, long maximum, List<ConfigurationIssue> issues)
    {
        string? token = Token(read, key);
        if (token is null)
        {
            return value;
        }
        return TryInteger(token, key, minimum, maximum, issues, out long parsed) ? parsed : value;
    }

    private static int Schema(Func<string, string?> read, int value, List<ConfigurationIssue> issues)
    {
        const string key = "CORE_LOG_SCHEMA_VERSION";
        string? token = Token(read, key);
        if (token is null)
        {
            return value;
        }
        if (!TryInteger(token, key, int.MinValue, int.MaxValue, issues, out long parsed))
        {
            return value;
        }
        if (parsed != 1)
        {
            issues.Add(new(ConfigurationIssueCodes.UnsupportedSchemaVersion, key, "Only schema version 1 is supported."));
            return value;
        }
        return 1;
    }

    private static bool TryInteger(string token, string key, long minimum, long maximum, List<ConfigurationIssue> issues, out long parsed)
    {
        parsed = 0;
        ReadOnlySpan<char> digits = token;
        if (digits.Length > 0 && digits[0] is '+' or '-')
        {
            digits = digits[1..];
        }
        if (digits.IsEmpty || digits.ContainsAnyExceptInRange('0', '9'))
        {
            InvalidToken(key, issues, IntegerRange(key, minimum, maximum));
            return false;
        }
        if (!long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed) || parsed < minimum || parsed > maximum)
        {
            issues.Add(new(ConfigurationIssueCodes.OutOfRange, key, IntegerRange(key, minimum, maximum)));
            return false;
        }
        return true;
    }

    private static string IntegerRange(string key, long minimum, long maximum)
        => key == "CORE_LOG_SCHEMA_VERSION" ? "Only schema version 1 is supported."
            : string.Create(CultureInfo.InvariantCulture, $"Use an invariant decimal integer from {minimum} through {maximum}.");

    private static void InvalidToken(string key, List<ConfigurationIssue> issues, string explanation)
        => issues.Add(new(ConfigurationIssueCodes.InvalidToken, key, explanation));
}
