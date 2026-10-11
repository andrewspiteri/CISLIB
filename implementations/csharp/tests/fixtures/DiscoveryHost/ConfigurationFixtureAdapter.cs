using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging.Configuration;

namespace CIS.Tests.DiscoveryFixture;

// Nonshipping protocol adapter, compiled into the host and unit tests.
internal static class ConfigurationFixtureAdapter
{
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

    public static EnvironmentConfigurationInput Setup(JsonObject input)
    {
        JsonObject setup = input["setup"]!.AsObject();
        string? String(string key) => Text(setup, key);
        int? Integer(string key) => setup[key]?.GetValue<int>();
        long? Long(string key) => setup[key]?.GetValue<long>();
        string? hostEnvironment = input["host"] is { } host ? Text(host, "environmentName") : null;
        ConfigurationSeverity? Severity(string key) => String(key) is { } value ? Enum.Parse<ConfigurationSeverity>(value, ignoreCase: false) : null;
        ConfigurationOutputFormat? Format(string key) => String(key) switch
        {
            "human" => ConfigurationOutputFormat.Human,
            "jsonl" => ConfigurationOutputFormat.JsonLines,
            null => null,
            _ => throw new InvalidDataException("Unknown fixture format.")
        };
        return new EnvironmentConfigurationInput
        {
            ApplicationName = String("ApplicationName"),
            EnvironmentName = setup.ContainsKey("EnvironmentName") ? String("EnvironmentName") : hostEnvironment,
            ServiceVersion = String("ServiceVersion"),
            EnvironmentKind = Integer("EnvironmentKind") is { } kind ? (ConfigurationEnvironmentKind)kind : null,
            Logging = new LoggingConfigurationInput
            {
                Level = Severity("Logging.Level"),
                ConsoleFormat = Format("Logging.ConsoleFormat"),
                ConsoleLevel = Severity("Logging.ConsoleLevel"),
                ConsoleStream = String("Logging.ConsoleStream") switch
                {
                    "stderr" => ConfigurationConsoleStream.StandardError,
                    "stdout" => ConfigurationConsoleStream.StandardOutput,
                    null => null,
                    _ => throw new InvalidDataException("Unknown fixture stream.")
                },
                HumanDetail = String("Logging.HumanDetail") switch
                {
                    "normal" => ConfigurationHumanDetail.Normal,
                    "verbose" => ConfigurationHumanDetail.Verbose,
                    null => null,
                    _ => throw new InvalidDataException("Unknown fixture detail.")
                },
                FilePath = String("Logging.FilePath"),
                FileFormat = Format("Logging.FileFormat"),
                FileLevel = Severity("Logging.FileLevel"),
                QueueCapacity = Integer("Logging.QueueCapacity"),
                FlushIntervalMilliseconds = Integer("Logging.FlushIntervalMilliseconds"),
                ShutdownTimeoutMilliseconds = Integer("Logging.ShutdownTimeoutMilliseconds"),
                FileMaxBytes = Long("Logging.FileMaxBytes"),
                FileRetainedFiles = Integer("Logging.FileRetainedFiles"),
                SchemaVersion = Integer("Logging.SchemaVersion"),
                MaxEntryBytes = Integer("Logging.MaxEntryBytes")
            }
        };
    }

    public static JsonObject Project(ConfigurationResolution result) => new()
    {
        ["buildVersion"] = result.BuildVersion,
        ["settings"] = new JsonArray(
            Setting(result.ApplicationName), Setting(result.EnvironmentName), Setting(result.ServiceVersion),
            Setting(result.Level), Setting(result.ConsoleFormat), Setting(result.ConsoleLevel),
            Setting(result.ConsoleStream), Setting(result.HumanDetail), Setting(result.FilePath),
            Setting(result.FileFormat), Setting(result.FileLevel), Setting(result.QueueCapacity),
            Setting(result.FlushIntervalMilliseconds), Setting(result.ShutdownTimeoutMilliseconds),
            Setting(result.FileMaxBytes), Setting(result.FileRetainedFiles), Setting(result.SchemaVersion), Setting(result.MaxEntryBytes)),
        ["consoleEnabled"] = result.ConsoleEnabled,
        ["fileEnabled"] = result.FileEnabled,
        ["hasParseErrors"] = result.HasParseErrors
    };

    private static JsonObject Setting<T>(ParsedConfigurationSetting<T> setting) => new()
    {
        ["key"] = setting.Key,
        ["state"] = setting.State.ToString(),
        ["value"] = setting.HasValue ? JsonSerializer.SerializeToNode(CanonicalValue(setting.Value)) : null,
        ["source"] = setting.Source.ToString().ToLowerInvariant(),
        ["sourceKey"] = setting.SourceKey,
        ["inheritedFrom"] = setting.InheritedFrom
    };

    private static object? CanonicalValue<T>(T value) => value switch
    {
        ConfigurationSeverity severity => severity.ToString(),
        ConfigurationOutputFormat format => format == ConfigurationOutputFormat.Human ? "human" : "jsonl",
        ConfigurationConsoleStream stream => stream == ConfigurationConsoleStream.StandardError ? "stderr" : "stdout",
        ConfigurationHumanDetail detail => detail == ConfigurationHumanDetail.Normal ? "normal" : "verbose",
        _ => value
    };
}
