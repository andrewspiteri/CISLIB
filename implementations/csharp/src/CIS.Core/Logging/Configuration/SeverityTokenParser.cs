namespace CIS.Logging.Configuration;

internal enum ConfigurationSeverity
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6
}

internal enum ConfigurationTokenState
{
    Absent,
    Valid,
    Invalid
}

// Absence and invalid input stay distinct so a later resolver can enforce precedence.
internal static class SeverityTokenParser
{
    internal static ConfigurationTokenState Parse(string? token, out ConfigurationSeverity? severity)
    {
        severity = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return ConfigurationTokenState.Absent;
        }

        severity = token.Trim().ToUpperInvariant() switch
        {
            "TRACE" or "TRC" => ConfigurationSeverity.Trace,
            "DEBUG" or "DBG" => ConfigurationSeverity.Debug,
            "INFORMATION" or "INF" or "INFO" => ConfigurationSeverity.Information,
            "WARNING" or "WRN" or "WARN" => ConfigurationSeverity.Warning,
            "ERROR" or "ERR" => ConfigurationSeverity.Error,
            "CRITICAL" or "CRT" or "CRIT" => ConfigurationSeverity.Critical,
            "NONE" => ConfigurationSeverity.None,
            _ => null
        };

        return severity.HasValue ? ConfigurationTokenState.Valid : ConfigurationTokenState.Invalid;
    }
}
