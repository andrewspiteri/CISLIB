using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConfigurationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> SharedCases => ConfigurationProcessChecks.Cases("configuration-resolution-cases.json", 26).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(SharedCases))]
    public async Task ChildConfigurationMatchesSharedInputsWithoutChangingHostLabel(string id)
    {
        string inputs = ConfigurationProcessChecks.Find("configuration-resolution-cases.json");
        JsonObject row = ConfigurationProcessChecks.Cases("configuration-resolution-cases.json", 26).Single(row => (string?)row!["id"] == id)!.AsObject();
        JsonObject input = row["inputs"]!.AsObject();
        var environment = new Dictionary<string, string?>();
        foreach (JsonNode? setting in ConfigurationReadExpectations.Catalog()["settings"]!.AsArray())
        {
            environment[(string)setting!["key"]!] = null;
            foreach (JsonNode? alias in setting["aliases"]?.AsArray() ?? []) { environment[(string)alias!] = null; }
        }
        foreach ((string name, JsonNode? value) in input["environment"]!.AsObject()) { environment[name] = (string?)value; }
        string? hostLabel = (string?)input["host"]!["environmentName"];
        environment["ASPNETCORE_ENVIRONMENT"] = hostLabel;
        environment["CORE_PASSWORD"] = "unknown-child-fixture-sentinel";
        using var directory = new TemporaryDirectory();
        (JsonObject result, _) = await ConfigurationProcessChecks.RunAsync(["configuration", inputs, id], directory.Path, TestContext, inputs, environment);
        Assert.AreEqual(id, (string?)result["caseId"]);
        JsonObject configuration = result["configuration"]!.AsObject();
        JsonObject expected = row["expected"]!.AsObject();
        if (expected["hasParseErrors"]!.GetValue<bool>())
        {
            Assert.AreEqual("Invalid", (string?)configuration["status"]);
            Assert.IsFalse(configuration.ContainsKey("values"));
            Assert.IsNull(configuration["filePath"]);
            Assert.IsNull(configuration["workingDirectory"]);
            CollectionAssert.AreEqual(expected["settings"]!.AsArray().Where(setting => (string?)setting!["state"] == "InvalidToken").Select(setting => "InvalidToken:" + (string)setting!["key"]!).ToArray(),
                ConfigurationProcessChecks.Issues(configuration["issues"]!));
        }
        else
        {
            Assert.AreEqual("Valid", (string?)configuration["status"]);
            Assert.AreEqual(0, configuration["issues"]!.AsArray().Count);
            Assert.AreEqual((string?)expected["buildVersion"], (string?)configuration["values"]!["BuildVersion"]);
            foreach (JsonNode? setting in expected["settings"]!.AsArray())
            {
                string key = (string)setting!["key"]!;
                string property = (string)ConfigurationReadExpectations.Catalog()["settings"]!.AsArray().Single(row => (string?)row!["key"] == key)!["property"]!;
                JsonNode? wanted = setting["value"];
                if (key == "CORE_LOG_FILE_PATH" && wanted is not null) { wanted = JsonValue.Create(Path.GetFullPath((string)wanted!, directory.Path)); }
                Assert.IsTrue(JsonNode.DeepEquals(wanted, configuration["values"]![property]), id + ":" + key);
            }
            Assert.IsTrue(JsonNode.DeepEquals(expected["consoleEnabled"], configuration["consoleEnabled"]));
            Assert.IsTrue(JsonNode.DeepEquals(expected["fileEnabled"], configuration["fileEnabled"]));
        }
        Assert.IsTrue(JsonNode.DeepEquals(input["host"], result["hostBefore"]));
        Assert.IsTrue(JsonNode.DeepEquals(result["hostBefore"], result["hostAfter"]));
        Assert.AreEqual(hostLabel, (string?)result["frameworkVariableBefore"]);
        Assert.AreEqual(hostLabel, (string?)result["frameworkVariableAfter"]);
        string[] reads = result["observed"]!["recognizedReads"]!.AsArray().Select(node => (string)node!).ToArray();
        CollectionAssert.AreEqual(ConfigurationReadExpectations.Expected(input), reads);
        Assert.AreEqual(reads.Length, reads.Distinct(StringComparer.Ordinal).Count());
        Assert.IsFalse(reads.Contains("ASPNETCORE_ENVIRONMENT", StringComparer.Ordinal));
        Assert.IsFalse(reads.Contains("CORE_PASSWORD", StringComparer.Ordinal));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(directory.Path).Length, "Selection created filesystem outputs.");
    }
}
