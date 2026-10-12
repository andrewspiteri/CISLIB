using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

// Nonshipping protocol adapter, compiled into the host and unit tests.
internal static class ConfigurationFixtureAdapter
{
    // Mutable protocol inputs are test-owned, never a writable shipping container or lifecycle API.
    internal static FixtureEnvironmentValues Facts { get; set; } = new();
    internal static void ApplyOverrides(Func<string, string?> read)
    {
        var issues = new List<ConfigurationIssue>();
        (Facts.ApplicationName, Facts.EnvironmentName, Facts.ServiceVersion) = EnvironmentOverrides.Apply(
            read, Facts.ApplicationName, Facts.EnvironmentName, Facts.ServiceVersion, Facts.Logging, issues);
        if (issues.Count != 0) { throw new CoreConfigurationException(issues); }
    }

    public static JsonObject ReadDocument(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    public static JsonObject Case(JsonObject document, string id) => document["cases"]!.AsArray().Single(row => Text(row!, "id") == id)!.AsObject();
    public static string? Text(JsonNode node, string name) => node[name]?.GetValue<string>();

    public static string FindFixture()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "logging", "configuration-resolution-cases.json");
            if (File.Exists(path)) { return path; }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find the configuration resolution fixtures.");
    }

    public static void Assign(JsonObject input)
    {
        Facts = new FixtureEnvironmentValues();
        JsonObject setup = input["setup"]!.AsObject();
        JsonObject discovery = input["discovery"]?.AsObject() ?? new JsonObject();
        string? Nonblank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        ConfigurationFixtureAdapter.Facts.ApplicationName = Nonblank(Text(setup, "ApplicationName")) ?? Nonblank(Text(discovery, "applicationName")) ?? string.Empty;
        ConfigurationFixtureAdapter.Facts.BuildVersion = Nonblank(Text(setup, "BuildVersion")) ?? Nonblank(Text(discovery, "buildVersion")) ?? "unknown";
        ConfigurationFixtureAdapter.Facts.ServiceVersion = Nonblank(Text(setup, "ServiceVersion")) ?? ConfigurationFixtureAdapter.Facts.BuildVersion;
        ConfigurationFixtureAdapter.Facts.EnvironmentName = Nonblank(Text(setup, "EnvironmentName")) ?? Nonblank(input["host"] is { } host ? Text(host, "environmentName") : null) ?? "unknown";
        ConfigurationFixtureAdapter.Facts.EnvironmentKind = setup["EnvironmentKind"] is { } kind ? (EnvironmentKind)kind.GetValue<int>() : EnvironmentKind.Unknown;
        ConfigurationFixtureAdapter.Facts.CommitId = Nonblank(Text(setup, "CommitId")) ?? Nonblank(Text(discovery, "commitId"));
        ConfigurationFixtureAdapter.Facts.BuildId = Nonblank(Text(setup, "BuildId")) ?? Nonblank(Text(discovery, "buildId"));
        ConfigurationFixtureAdapter.Facts.InstanceId = "11111111-2222-4333-8444-555555555555";
        ConfigurationFixtureAdapter.Facts.InitializedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // Each fixture starts with a complete caller-owned fact set, independent of prior tests.
        ConfigurationFixtureAdapter.Facts.ProcessStartedAtUtc = null;
        ConfigurationFixtureAdapter.Facts.HostName = null;
        ConfigurationFixtureAdapter.Facts.OperatingSystem = "unknown";
        ConfigurationFixtureAdapter.Facts.OperatingSystemDescription = null;
        ConfigurationFixtureAdapter.Facts.OSArchitecture = null;
        ConfigurationFixtureAdapter.Facts.ProcessArchitecture = null;
        ConfigurationFixtureAdapter.Facts.RuntimeDescription = null;
        ConfigurationFixtureAdapter.Facts.ProcessId = null;
        ConfigurationFixtureAdapter.Facts.Logging = new LoggingOptions();
        foreach ((string key, JsonNode? value) in setup)
        {
            if (!key.StartsWith("Logging.", StringComparison.Ordinal) || value is null) { continue; }
            PropertyInfo property = typeof(LoggingOptions).GetProperty(key[8..])!;
            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object parsed;
            if (type.IsEnum)
            {
                string token = value.GetValue<string>();
                token = token switch { "human" => "Human", "jsonl" => "JsonLines", "stderr" => "StandardError", "stdout" => "StandardOutput", "normal" => "Normal", "verbose" => "Verbose", _ => token };
                parsed = Enum.Parse(type, token);
            }
            else { parsed = value.Deserialize(type)!; }
            property.SetValue(ConfigurationFixtureAdapter.Facts.Logging, parsed);
        }
    }

    public static JsonObject Values(PreparedConfiguration snapshot)
    {
        LoggingOptions logging = snapshot.Logging;
        return new JsonObject
        {
            ["ApplicationName"] = snapshot.ApplicationName,
            ["EnvironmentName"] = snapshot.EnvironmentName,
            ["ServiceVersion"] = snapshot.ServiceVersion,
            ["BuildVersion"] = snapshot.BuildVersion,
            ["CommitId"] = snapshot.CommitId,
            ["BuildId"] = snapshot.BuildId,
            ["EnvironmentKind"] = snapshot.EnvironmentKind.ToString(),
            ["Logging.Level"] = logging.Level.ToString(),
            ["Logging.ConsoleFormat"] = logging.ConsoleFormat == LogOutputFormat.Human ? "human" : "jsonl",
            ["Logging.ConsoleLevel"] = logging.EffectiveConsoleLevel.ToString(),
            ["Logging.ConsoleStream"] = logging.ConsoleStream == ConsoleOutputStream.StandardError ? "stderr" : "stdout",
            ["Logging.HumanDetail"] = logging.HumanDetail == HumanOutputDetail.Normal ? "normal" : "verbose",
            ["Logging.FilePath"] = logging.FilePath,
            ["Logging.FileFormat"] = logging.FileFormat == LogOutputFormat.Human ? "human" : "jsonl",
            ["Logging.FileLevel"] = logging.EffectiveFileLevel.ToString(),
            ["Logging.QueueCapacity"] = logging.QueueCapacity,
            ["Logging.FlushIntervalMilliseconds"] = (int)logging.FlushInterval.TotalMilliseconds,
            ["Logging.ShutdownTimeoutMilliseconds"] = (int)logging.ShutdownTimeout.TotalMilliseconds,
            ["Logging.FileMaxBytes"] = logging.FileMaxBytes,
            ["Logging.FileRetainedFiles"] = logging.FileRetainedFiles,
            ["Logging.SchemaVersion"] = logging.SchemaVersion,
            ["Logging.MaxEntryBytes"] = logging.MaxEntryBytes
        };
    }
}

// Test-only final observation. There is no library publication/lifecycle behind this record.
internal sealed record PreparedConfiguration(EventMetadata EventMetadata, LoggingOptions Logging)
{
    public string ApplicationName => EventMetadata.ApplicationName;
    public string BuildVersion => EventMetadata.BuildVersion;
    public string ServiceVersion => EventMetadata.ServiceVersion;
    public string EnvironmentName => EventMetadata.EnvironmentName;
    public EnvironmentKind EnvironmentKind => EventMetadata.EnvironmentKind;
    public string? CommitId => EventMetadata.CommitId;
    public string? BuildId => EventMetadata.BuildId;
}

// Literal shared fixture transport. It exercises projection validation without spoofing readonly runtime facts.
internal sealed class FixtureEnvironmentValues
{
    public string ApplicationName { get; set; } = "Fixture.App";
    public string BuildVersion { get; set; } = "fixture-build";
    public string ServiceVersion { get; set; } = "service-release";
    public string EnvironmentName { get; set; } = "unknown";
    public EnvironmentKind EnvironmentKind { get; set; } = EnvironmentKind.Unknown;
    public string InstanceId { get; set; } = "11111111-2222-4333-8444-555555555555";
    public DateTimeOffset? ProcessStartedAtUtc { get; set; }
    public DateTimeOffset InitializedAtUtc { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public string OperatingSystem { get; set; } = "unknown";
    public int? ProcessId { get; set; }
    public string? CommitId { get; set; }
    public string? BuildId { get; set; }
    public string? HostName { get; set; }
    public string? OperatingSystemDescription { get; set; }
    public string? RuntimeDescription { get; set; }
    public string? OSArchitecture { get; set; }
    public string? ProcessArchitecture { get; set; }
    public LoggingOptions Logging { get; set; } = new();
    internal EventMetadata CaptureMetadata() => new(ApplicationName, BuildVersion, ServiceVersion, EnvironmentName, EnvironmentKind, InstanceId, ProcessStartedAtUtc, InitializedAtUtc, OperatingSystem, ProcessId, CommitId, BuildId, HostName, OperatingSystemDescription, RuntimeDescription, OSArchitecture, ProcessArchitecture);
}
