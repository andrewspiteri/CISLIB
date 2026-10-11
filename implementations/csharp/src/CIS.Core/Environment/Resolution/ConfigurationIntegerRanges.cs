namespace CIS.Configuration;

internal readonly record struct ConfigurationIntegerRange(string Key, long Minimum, long Maximum);

internal static class ConfigurationIntegerRanges
{
    public static IReadOnlyList<ConfigurationIntegerRange> All { get; } = Array.AsReadOnly<ConfigurationIntegerRange>(
    [
        new("CORE_LOG_QUEUE_CAPACITY", 1, 65536),
        new("CORE_LOG_FLUSH_INTERVAL_MS", 10, 60000),
        new("CORE_LOG_SHUTDOWN_TIMEOUT_MS", 1, 300000),
        new("CORE_LOG_FILE_MAX_BYTES", 65536, 1073741824),
        new("CORE_LOG_FILE_RETAINED_FILES", 1, 1000),
        new("CORE_LOG_SCHEMA_VERSION", 1, 1),
        new("CORE_LOG_MAX_ENTRY_BYTES", 16384, 16777216)
    ]);
}
