namespace CIS.Logging.Configuration;

internal enum ConfigurationTokenState
{
    Absent,
    Valid,
    Invalid
}

// Absence and invalid input stay distinct so a later resolver can enforce precedence.
internal static class SeverityTokenParser
{
    internal static ConfigurationTokenState Parse(string? token, out CoreLogLevel? severity)
    {
        severity = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return ConfigurationTokenState.Absent;
        }

        severity = token.Trim().ToUpperInvariant() switch
        {
            "TRACE" or "TRC" => CoreLogLevel.Trace,
            "DEBUG" or "DBG" => CoreLogLevel.Debug,
            "INFORMATION" or "INF" or "INFO" => CoreLogLevel.Information,
            "WARNING" or "WRN" or "WARN" => CoreLogLevel.Warning,
            "ERROR" or "ERR" => CoreLogLevel.Error,
            "CRITICAL" or "CRT" or "CRIT" => CoreLogLevel.Critical,
            "NONE" => CoreLogLevel.None,
            _ => null
        };

        return severity.HasValue ? ConfigurationTokenState.Valid : ConfigurationTokenState.Invalid;
    }
}
