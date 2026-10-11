using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EnvironmentOptionsProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("before")]
    [DataRow("null")]
    [DataRow("defaults")]
    [DataRow("explicit")]
    [DataRow("overrides")]
    [DataRow("retry")]
    public async Task ActualPublicFacadeSupportsExplicitInitializationAndStoredReadsInFreshProcesses(string scenario)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        Dictionary<string, string?> variables = RecognizedNames().ToDictionary(name => name, _ => (string?)null, StringComparer.Ordinal);
        Assert.AreEqual(21, variables.Count);
        variables["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR";
        if (scenario == "before") { variables["CORE_LOG_LEVEL"] = "invalid-must-not-be-read-by-static-constructor"; }
        if (scenario == "overrides")
        {
            variables["CORE_SERVICE_NAME"] = " Env.App "; variables["DD_SERVICE"] = "ignored-dd-app";
            variables["CORE_SERVICE_VERSION"] = " env-release "; variables["DD_VERSION"] = "ignored-dd-version";
            variables["CORE_ENVIRONMENT"] = " Env.Live "; variables["DD_ENV"] = "ignored-dd-env";
            variables["CORE_LOG_LEVEL"] = " WRN "; variables["CORE_LOG_CONSOLE_LEVEL"] = " ERR ";
            variables["CORE_LOG_FILE_LEVEL"] = " TRC "; variables["CORE_LOG_MAX_ENTRY_BYTES"] = "131072";
        }
        using var owned = new TemporaryDirectory();
        string before = Path.Combine(owned.Path, "before");
        string after = Path.Combine(owned.Path, "after");
        Directory.CreateDirectory(before); Directory.CreateDirectory(after);
        string parentDirectory = Directory.GetCurrentDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "startup-facade", scenario, after], before, TimeSpan.FromSeconds(15), variables);
        Assert.AreEqual(0, process.ExitCode, process.StandardError);
        Assert.AreEqual(string.Empty, process.StandardError);
        TestContext.WriteLine(process.StandardOutput);
        JsonNode result = JsonNode.Parse(process.StandardOutput)!;
        Assert.AreEqual(scenario, result["scenario"]!.GetValue<string>());
        bool initialized = scenario is not ("before" or "null");
        Assert.AreEqual(initialized, result["initialized"]!.GetValue<bool>());
        JsonNode observed = result["observed"]!;
        Assert.AreEqual(before, observed["beforeDirectory"]!.GetValue<string>());
        Assert.AreEqual(after, observed["afterDirectory"]!.GetValue<string>());
        Assert.IsTrue(observed["storedProjectionStable"]!.GetValue<bool>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        if (!initialized) { Assert.IsNull(result["snapshot"]); }
        else
        {
            JsonNode snapshot = result["snapshot"]!;
            bool defaults = scenario == "defaults";
            bool overrides = scenario == "overrides";
            Assert.AreEqual(overrides ? "Env.App" : defaults ? observed["fixtureName"]!.GetValue<string>() : "Setup.App", snapshot["ApplicationName"]!.GetValue<string>());
            string build = defaults ? observed["fixtureBuild"]!.GetValue<string>() : "1.2.3-rc.4+app.5";
            Assert.AreEqual(build, snapshot["BuildVersion"]!.GetValue<string>());
            Assert.AreEqual(overrides ? "env-release" : defaults ? build : "setup-release", snapshot["ServiceVersion"]!.GetValue<string>());
            Assert.AreEqual(overrides ? "Env.Live" : defaults ? "unknown" : "Setup.Live", snapshot["EnvironmentName"]!.GetValue<string>());
            Assert.AreEqual(defaults ? 0 : 2, snapshot["EnvironmentKind"]!.GetValue<int>());
            Assert.AreEqual(defaults ? null : "commit-8", snapshot["CommitId"]?.GetValue<string>());
            Assert.AreEqual(defaults ? null : "build-9", snapshot["BuildId"]?.GetValue<string>());
            Assert.IsTrue(Guid.TryParseExact(snapshot["InstanceId"]!.GetValue<string>(), "D", out Guid instance));
            Assert.AreNotEqual(Guid.Empty, instance);
            DateTimeOffset initializedAt = snapshot["InitializedAtUtc"]!.GetValue<DateTimeOffset>();
            Assert.AreEqual(TimeSpan.Zero, initializedAt.Offset);
            Assert.IsTrue(initializedAt >= observed["beforeInitialization"]!.GetValue<DateTimeOffset>() && initializedAt <= observed["afterInitialization"]!.GetValue<DateTimeOffset>());
            foreach ((string fact, string actual) in new[] { ("HostName", "hostName"), ("OperatingSystem", "operatingSystem"), ("OperatingSystemDescription", "osDescription"), ("OSArchitecture", "osArchitecture"), ("ProcessArchitecture", "processArchitecture"), ("RuntimeDescription", "runtimeDescription"), ("ProcessId", "processId"), ("ProcessStartedAtUtc", "processStartedAtUtc") })
            {
                Assert.IsTrue(JsonNode.DeepEquals(snapshot[fact], observed[actual]), fact);
            }
            JsonNode logging = snapshot["Logging"]!;
            Assert.AreEqual(defaults ? 2 : 3, logging["Level"]!.GetValue<int>());
            Assert.AreEqual(overrides ? 4 : defaults ? 2 : 6, logging["ConsoleLevel"]!.GetValue<int>());
            Assert.AreEqual(overrides ? 0 : defaults ? 2 : 1, logging["FileLevel"]!.GetValue<int>());
            Assert.AreEqual(defaults || overrides, logging["ConsoleEnabled"]!.GetValue<bool>());
            Assert.AreEqual(!defaults, logging["FileEnabled"]!.GetValue<bool>());
            Assert.AreEqual(defaults ? null : Path.Combine(before, "logs", "~app.jsonl"), logging["FilePath"]?.GetValue<string>());
            Assert.AreEqual(overrides ? 131072 : 65536, logging["MaxEntryBytes"]!.GetValue<int>());
            JsonObject metadata = snapshot["EventMetadata"]!.AsObject();
            Assert.AreEqual(17, metadata.Count);
            foreach ((string name, JsonNode? value) in metadata) { Assert.IsTrue(JsonNode.DeepEquals(value, snapshot[name]), name); }
            Assert.IsNull(metadata["Logging"]); Assert.IsNull(metadata["FilePath"]);
        }
        string[] issues = result["issuePairs"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        CollectionAssert.AreEqual(scenario == "retry" ? new[] { "InvalidPath:LoggingSetup.FilePath", "OutOfRange:LoggingSetup.QueueCapacity" } : Array.Empty<string>(), issues);
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(before).Count());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(after).Count());
        Assert.AreEqual(2, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        Assert.AreEqual(parentDirectory, Directory.GetCurrentDirectory());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static IEnumerable<string> RecognizedNames()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "contracts", "logging", "v1", "configuration.json");
            if (File.Exists(path))
            {
                JsonObject contract = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                return contract["settings"]!.AsArray().SelectMany(row => new[] { row!["key"]!.GetValue<string>() }.Concat(row["aliases"]!.AsArray().Select(alias => alias!.GetValue<string>())));
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find recognized startup variable contract.");
    }
}
