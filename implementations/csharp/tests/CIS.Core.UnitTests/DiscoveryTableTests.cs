using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class DiscoveryTableTests
{
    public static IEnumerable<object[]> Cases => ReadCases().Select(c => new object[] { c!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public void DiscoveredFactsMatchThePortableTable(string id)
    {
        JsonObject row = ReadCases().Single(c => c!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject input = row["inputs"]!.AsObject();
        JsonObject app = input["application"]!.AsObject();
        Assembly? SelectAssembly(string? value) => value switch
        {
            "application" => typeof(DiscoveryTableTests).Assembly,
            "core" => typeof(ApplicationIdentityDiscovery).Assembly,
            _ => null
        };
        var assembly = new AssemblyMetadataStub
        {
            EntryAssembly = SelectAssembly(Text(app, "entryAssembly")),
            Name = Text(app, "assemblyName"),
            InformationalVersion = Text(app, "information"),
            Version = Text(app, "version")
        };
        ApplicationIdentity application = ApplicationIdentityDiscovery.Discover(new ApplicationIdentityInput
        {
            ApplicationName = Text(app, "name"),
            BuildVersion = Text(app, "build"),
            ApplicationAssembly = SelectAssembly(Text(app, "explicitAssembly")),
            CommitId = Text(app, "commit"),
            BuildId = Text(app, "buildId")
        }, assembly);
        JsonObject facts = input["runtime"]!.AsObject();
        var source = new RuntimeMetadataStub
        {
            Platform = Text(facts, "platform") switch
            {
                "windows" => OSPlatform.Windows,
                "linux" => OSPlatform.Linux,
                "macos" => OSPlatform.OSX,
                { } other => OSPlatform.Create(other),
                _ => null
            },
            ProcessStartedAt = Text(facts, "start") is { } start ? DateTimeOffset.Parse(start, CultureInfo.InvariantCulture) : null,
            HostName = Text(facts, "host"),
            OSDescription = Text(facts, "description"),
            OSArchitecture = ArchitectureValue(Text(facts, "osArch")),
            ProcessArchitecture = ArchitectureValue(Text(facts, "processArch")),
            RuntimeDescription = Text(facts, "framework"),
            ProcessId = facts["pid"]?.GetValue<int>()
        };
        RuntimeMetadata runtime = RuntimeMetadataDiscovery.Discover(source);
        JsonObject service = input["service"]!.AsObject();
        var variables = new EnvironmentVariableStub();
        variables.Values["CORE_SERVICE_VERSION"] = Text(service, "core");
        variables.Values["DD_VERSION"] = Text(service, "dd");
        ApplicationVersionSelection versions = ApplicationVersionSelector.Select(application, Text(service, "setup"), variables);
        JsonObject initializationInput = input["initialization"]!.AsObject();
        var clock = new InitializationTimeProvider(DateTimeOffset.Parse(Text(initializationInput, "time")!, CultureInfo.InvariantCulture));
        InitializationIdentity initialization = new InitializationIdentityFactory(clock, () => Guid.Parse(Text(initializationInput, "id")!)).Create();
        JsonNode actual = JsonSerializer.SerializeToNode(new { Application = application, Versions = versions, Runtime = runtime, Initialization = initialization })!;

        Assert.IsTrue(JsonNode.DeepEquals(row["expected"], actual), $"{id}: {actual.ToJsonString()}");
        Assert.AreEqual(8, source.Reads.Count);
        Assert.AreEqual(1, clock.Reads);
        Assembly? selected = SelectAssembly(Text(app, "explicitAssembly")) ?? SelectAssembly(Text(app, "entryAssembly"));
        Assert.IsTrue(assembly.ObservedAssemblies.All(a => ReferenceEquals(a, selected)));
    }

    private static string? Text(JsonObject node, string key) => node[key]?.GetValue<string>();
    private static Architecture? ArchitectureValue(string? value) => value is null ? null : Enum.Parse<Architecture>(value);

    private static JsonArray ReadCases()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "logging", "discovery-cases.json");
            if (File.Exists(path))
            {
                return JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray();
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot find the portable discovery table.");
    }
}
