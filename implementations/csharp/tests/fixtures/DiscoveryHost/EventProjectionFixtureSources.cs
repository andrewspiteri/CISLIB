using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

// Controlled portable facts feed the real validated builder; actual process identity is observed separately.
internal sealed class EventProjectionFixtureSources(JsonObject metadata) : IRuntimeMetadataSource, IEnvironmentVariableSource
{
    internal bool Blocked { get; set; }
    private void Read() { if (Blocked) { throw new InvalidOperationException("Projection queried a blocked startup source."); } }
    private string? Text(string key) { Read(); return metadata[key]?.GetValue<string>(); }
    public string? GetValue(string name) { Read(); return null; }
    public DateTimeOffset? GetProcessStartedAt() => Text("process_started_at") is { } text ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) : null;
    public string? GetHostName() => Text("host_name");
    public OSPlatform? GetOperatingSystem() => Text("os_family") switch
    {
        "windows" => OSPlatform.Windows,
        "linux" => OSPlatform.Linux,
        "macos" => OSPlatform.OSX,
        "unknown" => null,
        _ => throw new InvalidDataException("Unknown portable OS family.")
    };
    public string? GetOperatingSystemDescription() => Text("os_description");
    public Architecture? GetOSArchitecture() => ArchitectureValue("os_architecture");
    public Architecture? GetProcessArchitecture() => ArchitectureValue("process_architecture");
    public string? GetRuntimeDescription() => Text("runtime_description");
    public int? GetProcessId() { Read(); return metadata["process_id"]?.GetValue<int>(); }
    private Architecture? ArchitectureValue(string key) => Text(key) is { } text ? Enum.Parse<Architecture>(text, ignoreCase: true) : null;

    internal static EnvironmentSnapshot Create(JsonObject metadata, out EnvironmentSetup setup, out EventProjectionFixtureSources sources)
    {
        setup = new EnvironmentSetup
        {
            ApplicationName = metadata["service"]!.GetValue<string>(),
            EnvironmentName = metadata["env"]!.GetValue<string>(),
            ServiceVersion = metadata["version"]!.GetValue<string>(),
            BuildVersion = metadata["build_version"]!.GetValue<string>(),
            EnvironmentKind = Enum.Parse<EnvironmentKind>(metadata["environment_kind"]!.GetValue<string>(), ignoreCase: true),
            CommitId = metadata["commit_id"]?.GetValue<string>(),
            BuildId = metadata["build_id"]?.GetValue<string>(),
            Logging = new LoggingSetup { FilePath = "private/projection.jsonl", QueueCapacity = 777, MaxEntryBytes = 65536 }
        };
        sources = new EventProjectionFixtureSources(metadata);
        EnvironmentSnapshot snapshot = EnvironmentSnapshotFactory.Create(setup, sources, runtimeSource: sources,
            readWorkingDirectory: Directory.GetCurrentDirectory,
            initializationFactory: new InitializationIdentityFactory(new FixedClock(metadata["initialized_at"]!.GetValue<DateTimeOffset>()),
                () => Guid.Parse(metadata["instance_id"]!.GetValue<string>())));
        sources.Blocked = true;
        return snapshot;
    }
    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider { public override DateTimeOffset GetUtcNow() => instant; }
}
