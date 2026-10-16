using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EnvironmentOptionsProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("defaults")]
    [DataRow("explicit")]
    [DataRow("alias")]
    [DataRow("overrides")]
    [DataRow("invalid")]
    public async Task ReadonlyConstructorFactsAndLoggingCaptureWorkInFreshApplicationProcesses(string scenario)
    {
        _ = await RunCase(scenario);
    }

    [TestMethod]
    public async Task SeparateApplicationProcessesReceiveDistinctInstanceIds()
    {
        var ids = new HashSet<Guid>();
        foreach (string scenario in new[] { "defaults", "explicit", "alias", "overrides" })
        {
            Assert.IsTrue(ids.Add((await RunCase(scenario))!.Value), "Application instance ID repeated.");
        }
    }

    private async Task<Guid?> RunCase(string scenario)
    {
        using var directory = new TemporaryDirectory();
        string[] names = ["CORE_SERVICE_NAME", "DD_SERVICE", "CORE_ENVIRONMENT", "DD_ENV", "CORE_SERVICE_VERSION", "DD_VERSION", "CORE_LOG_LEVEL", "CORE_LOG_CONSOLE_FORMAT", "CORE_LOG_CONSOLE_LEVEL", "CORE_LOG_CONSOLE_STREAM", "CORE_LOG_HUMAN_DETAIL", "CORE_LOG_FILE_PATH", "CORE_LOG_FILE_FORMAT", "CORE_LOG_FILE_LEVEL", "CORE_LOG_QUEUE_CAPACITY", "CORE_LOG_FLUSH_INTERVAL_MS", "CORE_LOG_SHUTDOWN_TIMEOUT_MS", "CORE_LOG_FILE_MAX_BYTES", "CORE_LOG_FILE_RETAINED_FILES", "CORE_LOG_SCHEMA_VERSION", "CORE_LOG_MAX_ENTRY_BYTES"];
        CollectionAssert.AreEquivalent(ConfigurationReadExpectations.Expected(new JsonObject()), names);
        var variables = names.ToDictionary(name => name, _ => (string?)null, StringComparer.Ordinal);
        if (scenario == "alias")
        {
            foreach (string key in new[] { "CORE_SERVICE_NAME", "CORE_ENVIRONMENT", "CORE_SERVICE_VERSION" }) { variables[key] = " \t "; }
            variables["DD_SERVICE"] = "Alias.App";
            variables["DD_ENV"] = "Alias.Env";
            variables["DD_VERSION"] = "Alias.Release";
        }
        if (scenario is "overrides" or "invalid")
        {
            variables["CORE_SERVICE_NAME"] = scenario == "invalid" ? "invalid\nname" : "Environment.App";
            variables["DD_SERVICE"] = "Shadowed.Alias";
            variables["CORE_ENVIRONMENT"] = "Canonical.Env";
            variables["DD_ENV"] = "Shadowed.Env";
            variables["CORE_SERVICE_VERSION"] = "Canonical.Release";
            variables["DD_VERSION"] = "Shadowed.Release";
            variables["CORE_LOG_LEVEL"] = "WRN";
            variables["CORE_LOG_MAX_ENTRY_BYTES"] = "131072";
        }
        (JsonObject result, int processId) = await ConfigurationProcessChecks.RunAsync(["constructor-options", scenario], directory.Path, TestContext, environment: variables);
        Assert.AreEqual(scenario, (string?)result["scenario"]);
        Assert.AreEqual(processId, result["observedPid"]!.GetValue<int>());
        Guid? instanceId = null;
        Assert.IsFalse(result["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.IsTrue(result["readonlyFacts"]!.GetValue<bool>());
        if (scenario == "invalid")
        {
            Assert.IsNull(result["values"]);
            Assert.IsNull(result["metadata"]);
            Assert.IsTrue(result["repeatedFailure"]!.GetValue<bool>());
            Assert.IsFalse(result["invalidIdentityInterned"]!.GetValue<bool>());
            Assert.AreEqual(1, result["issues"]!.AsArray().Count);
            Assert.AreEqual("InvalidIdentity", result["issues"]![0]!["code"]!.GetValue<string>());
            Assert.AreEqual("CORE_SERVICE_NAME", result["issues"]![0]!["key"]!.GetValue<string>());
        }
        else
        {
            JsonNode values = result["values"]!;
            JsonNode metadata = result["metadata"]!;
            Assert.AreEqual(scenario == "alias" ? "Alias.App" : scenario == "overrides" ? "Environment.App" : "CIS.Core.DiscoveryFixture", values["ApplicationName"]!.GetValue<string>());
            Assert.AreEqual("9.8.7-rc.2+sha.Application", values["BuildVersion"]!.GetValue<string>());
            Assert.AreEqual(scenario == "alias" ? "Alias.Release" : scenario == "overrides" ? "Canonical.Release" : "9.8.7-rc.2+sha.Application", values["ServiceVersion"]!.GetValue<string>());
            Assert.AreEqual(scenario == "alias" ? "Alias.Env" : scenario == "overrides" ? "Canonical.Env" : "unknown", values["EnvironmentName"]!.GetValue<string>());
            Assert.AreEqual(scenario == "overrides" ? 3 : 2, values["Logging"]!["Level"]!.GetValue<int>());
            Assert.AreEqual(result["observedPid"]!.GetValue<int>(), values["ProcessId"]!.GetValue<int>());
            Assert.IsTrue(result["nameInterned"]!.GetValue<bool>());
            instanceId = Guid.Parse(values["InstanceId"]!.GetValue<string>());
            Assert.AreNotEqual(Guid.Empty, instanceId.Value);
            foreach (string name in new[] { "ApplicationName", "BuildVersion", "ServiceVersion", "EnvironmentName", "ProcessId", "InstanceId", "HostName", "RuntimeDescription", "OperatingSystem", "OSArchitecture", "ProcessArchitecture" })
            {
                Assert.IsTrue(JsonNode.DeepEquals(values[name], metadata[name]), "Metadata fact differs: " + name);
            }
            foreach (string name in new[] { "HostName", "RuntimeDescription", "OSArchitecture", "ProcessArchitecture" }) { Assert.IsFalse(string.IsNullOrWhiteSpace((string?)values[name])); }
            Assert.AreEqual(ConfigurationProcessChecks.Family, (string?)values["OperatingSystem"]);
            DateTimeOffset timestamp = values["InitializedAtUtc"]!.GetValue<DateTimeOffset>();
            Assert.AreEqual(TimeSpan.Zero, timestamp.Offset);
            Assert.IsTrue(result["before"]!.GetValue<DateTimeOffset>() <= timestamp && timestamp <= result["after"]!.GetValue<DateTimeOffset>());
            Assert.AreEqual(scenario == "overrides" ? 131072 : 65536, values["Logging"]!["MaxEntryBytes"]!.GetValue<int>());
            Assert.IsNull(metadata["Logging"]);
            Assert.AreEqual(values["ApplicationName"]!.GetValue<string>(), metadata["ApplicationName"]!.GetValue<string>());
            Assert.AreEqual(0, result["issues"]!.AsArray().Count);
            Assert.AreEqual(scenario == "explicit" ? 1 : 0, values["EnvironmentKind"]!.GetValue<int>());
            if (scenario == "explicit")
            {
                Assert.AreEqual("caller-commit", metadata["CommitId"]!.GetValue<string>());
                Assert.AreEqual("caller-build", metadata["BuildId"]!.GetValue<string>());
            }
        }
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(directory.Path).Count());
        return instanceId;
    }
}
