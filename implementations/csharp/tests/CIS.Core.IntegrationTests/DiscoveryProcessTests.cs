using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
[DoNotParallelize]
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
        AssertProjection(result, scenario, expectedBuild, "Deployment.First");
    }

    private static void AssertProjection(JsonObject result, string scenario, string expectedBuild, string expectedService)
    {
        JsonObject observed = result["Observed"]!.AsObject();
        JsonObject application = result["Application"]!.AsObject();
        JsonObject runtime = result["Runtime"]!.AsObject();
        JsonObject initialization = result["Initialization"]!.AsObject();
        JsonObject versions = result["Versions"]!.AsObject();

        Assert.AreEqual("CIS.Core.DiscoveryFixture", Text(application, "ApplicationName"));
        Assert.AreEqual(expectedBuild, Text(application, "BuildVersion"));
        Assert.AreEqual(expectedBuild, Text(versions, "BuildVersion"));
        Assert.AreEqual(expectedService, Text(versions, "ServiceVersion"));
        Assert.AreEqual(scenario == "native" ? "static-constructor" : "controlled-internal-projection", Text(result, "FactBinding"), "Discovery fact binding differs.");
        CollectionAssert.AreEquivalent(new[] { "ApplicationName", "BuildVersion", "CommitId", "BuildId" }, application.Select(pair => pair.Key).ToArray());
        CollectionAssert.AreEquivalent(new[] { "BuildVersion", "ServiceVersion" }, versions.Select(pair => pair.Key).ToArray());
        Assert.IsNull(application["CommitId"]);
        Assert.IsNull(application["BuildId"]);
        Assert.AreEqual(ApplicationBuild, Text(observed, "EntryInformationalVersion"));
        Assert.AreEqual("23.0.0.0", Text(observed, "EntryAssemblyVersion"));
        Assert.AreEqual("88.0.0.0", Text(observed, "EntryFileVersion"));
        Assert.AreEqual("1.0.0.0", Text(observed, "CoreAssemblyVersion"));
        Assert.AreEqual("CIS.Core", Text(observed, "CoreName"), "Observed Core name differs.");
        Assert.AreEqual(".NET 10.0.12", Text(observed, "RuntimeDescription"), "Observed runtime differs.");
        Assert.AreNotEqual(ApplicationBuild, Text(observed, "CoreInformationalVersion"));
        Assert.AreEqual(Hash(FixturePath), Text(observed, "EntrySha256"));
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(FixturePath)!, "CIS.Core.dll")), Text(observed, "CoreSha256"), "Observed Core SHA-256 differs.");
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown", Text(runtime, "OperatingSystem"));
        Assert.AreEqual(Text(observed, "OperatingSystem"), Text(runtime, "OperatingSystem"));
        string id = Text(initialization, "InstanceId")!;
        Assert.IsTrue(Guid.TryParseExact(id, "D", out Guid uuid));
        Assert.AreNotEqual(Guid.Empty, uuid, "Initialization instance ID must be nonempty.");
        Assert.AreEqual(uuid.ToString("D"), id);
        DateTimeOffset initialized = Time(initialization, "InitializedAtUtc");
        Assert.AreEqual(TimeSpan.Zero, initialized.Offset);
        Assert.IsTrue(initialized >= Time(observed, "BeforeInitializationUtc"));
        Assert.IsTrue(initialized <= Time(observed, "AfterInitializationUtc"));

        if (scenario == "unavailable")
        {
            foreach (string field in new[] { "ProcessStartedAtUtc", "HostName", "OperatingSystemDescription", "OSArchitecture", "ProcessArchitecture", "RuntimeDescription", "ProcessId" })
            {
                Assert.IsTrue(runtime.ContainsKey(field), field);
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
        (string Mode, string? Core, string? Alias, string Build, string Service)[] scenarios =
        [
            ("native", " Deployment.First ", "Shadowed", ApplicationBuild, "Deployment.First"),
            ("assembly-version", "Deployment.First", null, "23.0.0.0", "Deployment.First"),
            ("unavailable", "Deployment.First", null, "unknown", "Deployment.First"),
            ("native", "Deployment.Second", null, ApplicationBuild, "Deployment.Second"),
            ("native", null, " Alias.Label ", ApplicationBuild, "Alias.Label"),
            ("native", " \t", "Alias.Label", ApplicationBuild, "Alias.Label"),
            ("native", " ", "\t", ApplicationBuild, ApplicationBuild)
        ];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        JsonNode? firstApplication = null;
        foreach (var scenario in scenarios)
        {
            JsonObject result = await Run(scenario.Mode, scenario.Core, scenario.Alias);
            AssertProjection(result, scenario.Mode, scenario.Build, scenario.Service);
            Assert.IsTrue(ids.Add(Text(result["Initialization"]!.AsObject(), "InstanceId")!), "Instance ID reused between children.");
            if (scenario.Mode == "native")
            {
                firstApplication ??= result["Application"];
                Assert.IsTrue(JsonNode.DeepEquals(firstApplication, result["Application"]), "Telemetry label changed application facts.");
            }
        }
    }

    [TestMethod]
    [DataRow(null, " Alias.Label ", "Alias.Label")]
    [DataRow(" \t", "Alias.Label", "Alias.Label")]
    [DataRow(" ", "\t", ApplicationBuild)]
    public async Task ChildVersionSelectionUsesOnlyTheFirstNonblankVersionSource(string? core, string? alias, string expected)
    {
        JsonObject result = await Run("native", core, alias);
        AssertProjection(result, "native", ApplicationBuild, expected);
    }

    [TestMethod]
    [DataRow("binding")]
    [DataRow("runtime")]
    [DataRow("core-name")]
    [DataRow("core-bytes")]
    [DataRow("instance")]
    public async Task ProjectionChecksRejectCorruptedChildObservations(string fault)
    {
        JsonObject result = await Run("native", "Deployment.First", null);
        AssertProjection(result, "native", ApplicationBuild, "Deployment.First");
        switch (fault)
        {
            case "binding": result["FactBinding"] = "controlled-internal-projection"; break;
            case "runtime": result["Observed"]!["RuntimeDescription"] = ".NET 10.0.99"; result["Runtime"]!["RuntimeDescription"] = ".NET 10.0.99"; break;
            case "core-name": result["Observed"]!["CoreName"] = "Unexpected.Core"; break;
            case "core-bytes": result["Observed"]!["CoreSha256"] = new string('0', 64); break;
            case "instance": result["Initialization"]!["InstanceId"] = Guid.Empty.ToString("D"); break;
            default: Assert.Fail("Unknown corruption control."); break;
        }
        string reason = fault switch
        {
            "binding" => "Discovery fact binding differs.",
            "runtime" => "Observed runtime differs.",
            "core-name" => "Observed Core name differs.",
            "core-bytes" => "Observed Core SHA-256 differs.",
            "instance" => "Initialization instance ID must be nonempty.",
            _ => throw new AssertFailedException("Unknown corruption control.")
        };
        StringAssert.Contains(Assert.ThrowsExactly<AssertFailedException>(() => AssertProjection(result, "native", ApplicationBuild, "Deployment.First")).Message, reason);
    }

    private async Task<JsonObject> Run(string scenario, string? core, string? alias)
    {
        Dictionary<string, string?> before = ParentEnvironment();
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        JsonObject catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "discovery-configuration.json")))!.AsObject();
        foreach (JsonNode? setting in catalog["settings"]!.AsArray())
        {
            environment[setting!["key"]!.GetValue<string>()] = null;
            foreach (JsonNode? name in setting["aliases"]?.AsArray() ?? [])
            {
                environment[name!.GetValue<string>()] = null;
            }
        }
        environment["CORE_SERVICE_VERSION"] = core;
        environment["DD_VERSION"] = alias;
        environment["DD_AGENT_HOST"] = "collector-only.example";
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", FixturePath, scenario],
                Path.GetDirectoryName(FixturePath)!, TimeSpan.FromSeconds(15), environment);
        }
        finally
        {
            Dictionary<string, string?> after = ParentEnvironment();
            Assert.IsTrue(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? value) && value == pair.Value),
                "Parent environment changed; values are intentionally not logged.");
        }
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.AreEqual(string.Empty, result.StandardError);
        TestContext.WriteLine(result.StandardOutput);
        return JsonNode.Parse(result.StandardOutput)!.AsObject();
    }

    private static Dictionary<string, string?> ParentEnvironment() => Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
        .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value, StringComparer.Ordinal);

    private static string? Text(JsonObject node, string key)
    {
        Assert.IsTrue(node.ContainsKey(key), $"Missing observation: {key}");
        return node[key]?.GetValue<string>();
    }
    [TestMethod]
    [DataRow("2026-10-17T00:00:00")]
    [DataRow("2026-10-17T00:00:00+01:00")]
    public void UtcObservationsRequireAnExplicitZeroOffset(string token)
    {
        var node = new JsonObject { ["time"] = "2026-10-17T00:00:00Z" };
        Assert.AreEqual(TimeSpan.Zero, Time(node, "time").Offset);
        node["time"] = token;
        StringAssert.Contains(Assert.ThrowsExactly<AssertFailedException>(() => Time(node, "time")).Message, "Observation must explicitly identify UTC.");
    }

    [TestMethod]
    [DataRow("2026-10-17T00:00:00Z")]
    [DataRow("2026-10-17T00:00:00.1234567+00:00")]
    public void ExplicitUtcRepresentationsRemainValid(string token) =>
        Assert.AreEqual(TimeSpan.Zero, Time(new JsonObject { ["time"] = token }, "time").Offset);

    private static DateTimeOffset Time(JsonObject node, string key)
    {
        string token = Text(node, key)!;
        Assert.IsTrue((token.EndsWith('Z') || token.EndsWith("+00:00", StringComparison.Ordinal)) &&
            DateTimeOffset.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) && parsed.Offset == TimeSpan.Zero,
            "Observation must explicitly identify UTC.");
        return DateTimeOffset.Parse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
