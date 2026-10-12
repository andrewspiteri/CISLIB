using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class DiscoveryProcessTests
{
    private const string ApplicationBuild = "9.8.7-rc.2+sha.Application";
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("native", ApplicationBuild)]
    [DataRow("assembly-version", "23.0.0.0")]
    [DataRow("unavailable", "unknown")]
    public async Task ChildProjectionUsesTheApplicationAndVisibleRuntimeFacts(string scenario, string expectedBuild)
    {
        JsonObject result = await Run(scenario, " Deployment.First ", "Shadowed.Alias");
        JsonObject observed = result["Observed"]!.AsObject();
        JsonObject application = result["Application"]!.AsObject();
        JsonObject runtime = result["Runtime"]!.AsObject();
        JsonObject initialization = result["Initialization"]!.AsObject();
        JsonObject versions = result["Versions"]!.AsObject();

        Assert.AreEqual("CIS.Core.DiscoveryFixture", Text(application, "ApplicationName"));
        Assert.AreEqual(expectedBuild, Text(application, "BuildVersion"));
        Assert.AreEqual(expectedBuild, Text(versions, "BuildVersion"));
        Assert.AreEqual("Deployment.First", Text(versions, "ServiceVersion"));
        Assert.IsNull(application["CommitId"]);
        Assert.IsNull(application["BuildId"]);
        Assert.AreEqual(ApplicationBuild, Text(observed, "EntryInformationalVersion"));
        Assert.AreEqual("23.0.0.0", Text(observed, "EntryAssemblyVersion"));
        Assert.AreEqual("88.0.0.0", Text(observed, "EntryFileVersion"));
        Assert.AreEqual("1.0.0.0", Text(observed, "CoreAssemblyVersion"));
        Assert.AreNotEqual(ApplicationBuild, Text(observed, "CoreInformationalVersion"));
        Assert.AreEqual(Hash(FixturePath), Text(observed, "EntrySha256"));
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(FixturePath)!, "CIS.Core.dll")), Text(observed, "CoreSha256"));
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown", Text(runtime, "OperatingSystem"));
        Assert.AreEqual(Text(observed, "OperatingSystem"), Text(runtime, "OperatingSystem"));
        string id = Text(initialization, "InstanceId")!;
        Assert.IsTrue(Guid.TryParseExact(id, "D", out Guid uuid));
        Assert.AreNotEqual(Guid.Empty, uuid);
        Assert.AreEqual(uuid.ToString("D"), id);
        DateTimeOffset initialized = Time(initialization, "InitializedAtUtc");
        Assert.AreEqual(TimeSpan.Zero, initialized.Offset);
        Assert.IsTrue(initialized >= Time(observed, "BeforeInitializationUtc"));
        Assert.IsTrue(initialized <= Time(observed, "AfterInitializationUtc"));

        if (scenario == "unavailable")
        {
            foreach (string field in new[] { "ProcessStartedAtUtc", "HostName", "OperatingSystemDescription", "OSArchitecture", "ProcessArchitecture", "RuntimeDescription", "ProcessId" })
            {
                Assert.IsNull(runtime[field], field);
            }
        }
        else
        {
            foreach (string field in new[] { "HostName", "OSArchitecture", "ProcessArchitecture", "RuntimeDescription" })
            {
                Assert.AreEqual(Text(observed, field), Text(runtime, field), field);
            }

            Assert.AreEqual(Text(observed, "OSDescription"), Text(runtime, "OperatingSystemDescription"));
            Assert.AreNotEqual("collector-only.example", Text(runtime, "HostName"));
            Assert.AreEqual(observed["ProcessId"]!.GetValue<int>(), runtime["ProcessId"]!.GetValue<int>());
            Assert.AreEqual(Time(observed, "ProcessStartedAtUtc"), Time(runtime, "ProcessStartedAtUtc"));
            Assert.AreEqual(TimeSpan.Zero, Time(runtime, "ProcessStartedAtUtc").Offset);
            Assert.IsTrue(Time(runtime, "ProcessStartedAtUtc") <= initialized);
        }
    }

    [TestMethod]
    public async Task ChangingChildTelemetryLabelsKeepsBuildFactsAndParentEnvironmentStable()
    {
        string[] keys = ["CORE_SERVICE_VERSION", "DD_VERSION", "DD_AGENT_HOST"];
        string?[] before = keys.Select(Environment.GetEnvironmentVariable).ToArray();
        JsonObject first = await Run("native", "Deployment.First", null);
        JsonObject second = await Run("native", "Deployment.Second", null);

        Assert.IsTrue(JsonNode.DeepEquals(first["Application"], second["Application"]));
        Assert.AreEqual(ApplicationBuild, Text(first["Versions"]!.AsObject(), "BuildVersion"));
        Assert.AreEqual(ApplicationBuild, Text(second["Versions"]!.AsObject(), "BuildVersion"));
        Assert.AreEqual("Deployment.First", Text(first["Versions"]!.AsObject(), "ServiceVersion"));
        Assert.AreEqual("Deployment.Second", Text(second["Versions"]!.AsObject(), "ServiceVersion"));
        Assert.AreNotEqual(Text(first["Initialization"]!.AsObject(), "InstanceId"), Text(second["Initialization"]!.AsObject(), "InstanceId"));
        CollectionAssert.AreEqual(before, keys.Select(Environment.GetEnvironmentVariable).ToArray());
    }

    [TestMethod]
    [DataRow(null, " Alias.Label ", "Alias.Label", "DD_VERSION")]
    [DataRow(" \t", "Alias.Label", "Alias.Label", "DD_VERSION")]
    [DataRow(" ", "\t", ApplicationBuild, "BuildVersion")]
    public async Task ChildVersionSelectionUsesOnlyTheFirstNonblankVersionSource(string? core, string? alias, string expected, string expectedSource)
    {
        JsonObject result = await Run("native", core, alias);
        JsonObject versions = result["Versions"]!.AsObject();
        Assert.AreEqual(ApplicationBuild, Text(versions, "BuildVersion"));
        Assert.AreEqual(expected, Text(versions, "ServiceVersion"));
        Assert.IsTrue(expectedSource is "DD_VERSION" or "BuildVersion");
    }

    private async Task<JsonObject> Run(string scenario, string? core, string? alias)
    {
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", [FixturePath, scenario], AppContext.BaseDirectory, TimeSpan.FromSeconds(15), new Dictionary<string, string?>
        {
            ["CORE_SERVICE_VERSION"] = core,
            ["DD_VERSION"] = alias,
            ["DD_AGENT_HOST"] = "collector-only.example",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        });
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.AreEqual(string.Empty, result.StandardError);
        TestContext.WriteLine(result.StandardOutput);
        return JsonNode.Parse(result.StandardOutput)!.AsObject();
    }

    private static string? Text(JsonObject node, string key) => node[key]?.GetValue<string>();
    private static DateTimeOffset Time(JsonObject node, string key) => DateTimeOffset.Parse(Text(node, key)!, CultureInfo.InvariantCulture);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
