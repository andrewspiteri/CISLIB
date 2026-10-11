using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StartupQualificationProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    public static IEnumerable<object[]> MetadataRows => Read("metadata-cases.json")["cases"]!.AsArray()
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(MetadataRows))]
    public async Task EverySharedMetadataVectorExecutesInAnIsolatedActualCoreProcess(string id)
    {
        JsonObject result = await Run(["startup-metadata", Find("conformance/logging/metadata-cases.json"), id]);
        Assert.AreEqual(id, result["caseId"]!.GetValue<string>());
        Assert.AreEqual(Hash(Find("conformance/logging/metadata-cases.json")), result["observed"]!["inputsSha256"]!.GetValue<string>());
        Assert.IsFalse(result["observed"]!["publicFacadeInitialized"]!.GetValue<bool>());
    }

    [TestMethod]
    [DataRow("STARTUP-BEFORE-PUBLICATION", "startup-facade", "before")]
    [DataRow("STARTUP-INVALID-THEN-VALID", "startup-facade", "retry")]
    [DataRow("STARTUP-ONE-PUBLICATION", "startup-facade", "explicit")]
    [DataRow("STARTUP-CONCURRENT-VALID", "startup-concurrency", "valid-16")]
    [DataRow("STARTUP-CONCURRENT-INVALID-VALID", "startup-concurrency", "mixed-16")]
    [DataRow("STARTUP-NO-RESET-OR-SIDE-EFFECTS", "startup-independent", "")]
    public async Task SharedLifecycleCasesExecuteTheirIsolatedPublicStartupPaths(string id, string mode, string scenario)
    {
        JsonObject row = Read("startup-lifecycle-cases.json")["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject expected = row["expected"]!.AsObject();
        JsonObject result = await Run(scenario.Length == 0 ? [mode] : [mode, scenario], appendAfterDirectory: true);
        if (id == "STARTUP-BEFORE-PUBLICATION")
        {
            Assert.AreEqual(expected["IsInitialized"]!.GetValue<bool>(), result["initialized"]!.GetValue<bool>());
            Assert.IsNull(result["snapshot"]);
            JsonObject errors = result["observed"]!["preInitializationErrors"]!.AsObject();
            Assert.AreEqual(9, errors.Count);
            foreach ((string getter, JsonNode? error) in errors)
            {
                Assert.AreEqual(expected["readsThrow"]!.GetValue<string>(), error!.GetValue<string>(), getter);
            }
        }
        else if (id == "STARTUP-INVALID-THEN-VALID")
        {
            Assert.IsTrue(result["initialized"]!.GetValue<bool>());
            Assert.IsTrue(result["issuePairs"]!.AsArray().Count > 0);
            Assert.AreEqual(expected["validCanSucceed"]!.GetValue<bool>(), result["initialized"]!.GetValue<bool>());
            Assert.AreEqual(expected["publishesInvalidSnapshot"]!.GetValue<bool>(), result["observed"]!["initializedAfterFailure"]!.GetValue<bool>());
            Assert.AreEqual(expected["invalidThrows"]!.GetValue<string>(), result["observed"]!["invalidException"]!.GetValue<string>());
            Assert.AreEqual(9, result["observed"]!["failureReadErrors"]!.AsObject().Count);
        }
        else if (id is "STARTUP-CONCURRENT-VALID" or "STARTUP-CONCURRENT-INVALID-VALID")
        {
            JsonArray attempts = result["attempts"]!.AsArray();
            int successes = attempts.Count(attempt => attempt!["success"]!.GetValue<bool>());
            Assert.AreEqual(expected[id == "STARTUP-CONCURRENT-VALID" ? "successfulPublishers" : "validSuccessfulPublishers"]!.GetValue<int>(), successes);
            Assert.IsTrue(result["observed"]!["storedProjectionStable"]!.GetValue<bool>());
            if (id == "STARTUP-CONCURRENT-INVALID-VALID")
            {
                Assert.AreEqual(1, result["publishedCandidate"]!.GetValue<int>() % 2);
            }
        }
        else if (id == "STARTUP-ONE-PUBLICATION")
        {
            Assert.AreEqual(expected["successfulPublishers"]!.GetValue<int>(), result["observed"]!["successfulPublishers"]!.GetValue<int>());
            Assert.AreEqual(expected["CurrentReferenceEqualsInitializeResult"]!.GetValue<bool>(), result["observed"]!["currentReferenceEqualsInitializeResult"]!.GetValue<bool>());
            Assert.AreEqual(expected["laterThrows"]!.GetValue<string>(), result["observed"]!["laterException"]!.GetValue<string>());
            Assert.AreEqual(!expected["replacesSnapshot"]!.GetValue<bool>(), result["observed"]!["storedProjectionStable"]!.GetValue<bool>());
            Assert.IsTrue(result["initialized"]!.GetValue<bool>());
            Assert.IsTrue(result["observed"]!["storedProjectionStable"]!.GetValue<bool>());
        }
        else
        {
            Assert.IsTrue(result["observed"]!["noResetApi"]!.GetValue<bool>());
            Assert.IsFalse(expected["productionResetApi"]!.GetValue<bool>());
        }
    }

    [TestMethod]
    public async Task IndependentlyInitializedApplicationProcessesHaveDifferentStableInstancesAndActualProcessMetadata()
    {
        JsonObject first = await Run(["startup-independent"], appendAfterDirectory: true);
        JsonObject second = await Run(["startup-independent"], appendAfterDirectory: true);
        Assert.AreNotEqual(first["snapshot"]!["InstanceId"]!.GetValue<string>(), second["snapshot"]!["InstanceId"]!.GetValue<string>());
        foreach (JsonObject result in new[] { first, second })
        {
            JsonNode snapshot = result["snapshot"]!;
            JsonNode observed = result["observed"]!;
            Assert.IsTrue(Guid.TryParseExact(snapshot["InstanceId"]!.GetValue<string>(), "D", out Guid id));
            Assert.AreNotEqual(Guid.Empty, id);
            Assert.IsTrue(observed["instanceStable"]!.GetValue<bool>());
            Assert.IsTrue(observed["completeSnapshotStable"]!.GetValue<bool>());
            Assert.IsTrue(observed["referenceStable"]!.GetValue<bool>());
            Assert.AreEqual(128, observed["storedReadChecks"]!.GetValue<int>());
            Assert.AreEqual("Independent.App", snapshot["ApplicationName"]!.GetValue<string>());
            Assert.AreEqual(1024, snapshot["Logging"]!["QueueCapacity"]!.GetValue<int>());
            foreach ((string fact, string observation) in new[] { ("ProcessId", "processId"), ("ProcessStartedAtUtc", "processStartedAtUtc"), ("HostName", "hostName"), ("OperatingSystem", "operatingSystem"), ("OSArchitecture", "osArchitecture"), ("ProcessArchitecture", "processArchitecture"), ("RuntimeDescription", "runtimeDescription") })
            {
                Assert.IsTrue(JsonNode.DeepEquals(snapshot[fact], observed[observation]), fact);
            }
            DateTimeOffset initialized = snapshot["InitializedAtUtc"]!.GetValue<DateTimeOffset>();
            Assert.AreEqual(TimeSpan.Zero, initialized.Offset);
            Assert.IsTrue(initialized >= observed["beforeInitialization"]!.GetValue<DateTimeOffset>());
            Assert.IsTrue(initialized <= observed["afterInitialization"]!.GetValue<DateTimeOffset>());
        }
    }

    private async Task<JsonObject> Run(string[] arguments, bool appendAfterDirectory = false)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        JsonArray settings = JsonNode.Parse(File.ReadAllText(Find("contracts/logging/v1/configuration.json")))!["settings"]!.AsArray();
        Dictionary<string, string?> variables = settings.SelectMany(row => new[] { row!["key"]!.GetValue<string>() }
            .Concat(row["aliases"]!.AsArray().Select(alias => alias!.GetValue<string>())))
            .ToDictionary(name => name, _ => (string?)null, StringComparer.Ordinal);
        Assert.AreEqual(21, variables.Count);
        variables["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR";
        using var owned = new TemporaryDirectory();
        string before = Path.Combine(owned.Path, "before");
        string after = Path.Combine(owned.Path, "after");
        Directory.CreateDirectory(before);
        Directory.CreateDirectory(after);
        string parentDirectory = Directory.GetCurrentDirectory();
        IEnumerable<string> mode = appendAfterDirectory ? arguments.Append(after) : arguments;
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host }.Concat(mode),
            before, TimeSpan.FromSeconds(20), variables);
        Assert.AreEqual(0, process.ExitCode, process.StandardError);
        Assert.AreEqual(string.Empty, process.StandardError);
        TestContext.WriteLine(process.StandardOutput);
        JsonObject result = JsonNode.Parse(process.StandardOutput)!.AsObject();
        JsonNode observed = result["observed"]!;
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(before).Count());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(after).Count());
        Assert.AreEqual(2, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        Assert.AreEqual(parentDirectory, Directory.GetCurrentDirectory());
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        return result;
    }

    private static JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText(Find("conformance/logging/" + name)))!.AsObject();

    private static string Find(string relative)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, relative);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find startup qualification input: " + relative);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
