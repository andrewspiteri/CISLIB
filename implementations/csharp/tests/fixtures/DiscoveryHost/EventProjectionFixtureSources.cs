using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

// Application-side assignments supply controlled facts. Logging owns metadata capture.
internal static class EventProjectionFixtureSources
{
    internal static EventMetadata Create(JsonObject metadata, out JsonObject suppliedFacts)
    {
        ConfigurationFixtureAdapter.Facts.ApplicationName = metadata["service"]!.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.EnvironmentName = metadata["env"]!.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.ServiceVersion = metadata["version"]!.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.BuildVersion = metadata["build_version"]!.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.EnvironmentKind = Enum.Parse<EnvironmentKind>(metadata["environment_kind"]!.GetValue<string>(), ignoreCase: true);
        ConfigurationFixtureAdapter.Facts.CommitId = metadata["commit_id"]?.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.BuildId = metadata["build_id"]?.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.InstanceId = metadata["instance_id"]!.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.InitializedAtUtc = metadata["initialized_at"]!.GetValue<DateTimeOffset>();
        ConfigurationFixtureAdapter.Facts.ProcessStartedAtUtc = metadata["process_started_at"]?.GetValue<DateTimeOffset>();
        ConfigurationFixtureAdapter.Facts.HostName = metadata["host_name"]?.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.OperatingSystem = metadata["os_family"]!.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.OperatingSystemDescription = metadata["os_description"]?.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.OSArchitecture = metadata["os_architecture"]?.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.ProcessArchitecture = metadata["process_architecture"]?.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.RuntimeDescription = metadata["runtime_description"]?.GetValue<string>();
        ConfigurationFixtureAdapter.Facts.ProcessId = metadata["process_id"]?.GetValue<int>();
        ConfigurationFixtureAdapter.Facts.Logging = new LoggingOptions { FilePath = "private/projection.jsonl", QueueCapacity = 777 };
        suppliedFacts = metadata;
        return ConfigurationFixtureAdapter.Facts.CaptureMetadata();
    }
}
