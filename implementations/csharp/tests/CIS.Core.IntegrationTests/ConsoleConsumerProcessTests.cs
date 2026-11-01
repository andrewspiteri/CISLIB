using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConsoleConsumerProcessTests
{
#if !CIS_MUTATION_TESTS
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> ReaderPositives = new(StringComparer.Ordinal);
    private sealed record CompletedContext(string Id, string Culture, string UICulture, string EnvironmentSha256);
    private sealed record CompletedObservation(string Json, string FixtureSha256, string CoreSha256, string InputsSha256);
    private static readonly ConcurrentDictionary<CompletedContext, CompletedObservation> CompletedFunctionalInputs = new();
#endif
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => ConsoleProcessChecks.Cases("consumer", 38);
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task SameCompiledConsumerUsesEnvironmentAndDrainsBothStreams(string id)
    {
#if CIS_MUTATION_TESTS
        await Run(id);
#else
        CompletedContext context = CurrentContext(id);
        (JsonObject catalog, _, JsonObject actual) = await Run(id);
        // Run completed every physical assertion and disposed its private child root.
        // Only immutable text from an ordinary functional case can supply an identity input.
        if (context == CurrentContext(id))
        {
            JsonNode observed = actual["observed"]!;
            string json = new JsonObject { ["catalog"] = catalog.DeepClone(), ["actual"] = actual.DeepClone() }.ToJsonString();
            CompletedFunctionalInputs.TryAdd(context, new(json, observed["fixtureSha256"]!.GetValue<string>(),
                observed["coreSha256"]!.GetValue<string>(), observed["inputsSha256"]!.GetValue<string>()));
        }
#endif
    }
    [TestMethod]
    [DataRow("settings", "Environment-selected settings differ.")]
    [DataRow("outcome", "Consumer outcomes/tickets differ.")]
    [DataRow("factory", "Consumer factory calls differ.")]
    [DataRow("clock", "Consumer clock calls differ.")]
    [DataRow("final", "Consumer final accounting differs.")]
    [DataRow("checkpoint", "Independent file progress differs.")]
    [DataRow("dispose", "Borrowed console disposal differs.")]
    [DataRow("application", "Borrowed application bytes differ.")]
    [DataRow("route", "Unselected consumer stream has records.")]
    [DataRow("identity", "Consumer JSONL facts differ.")]
    [DataRow("resource", "Consumer JSONL facts differ.")]
    [DataRow("bom", "Consumer entry framing/cap differs.")]
    [DataRow("records", "Consumer record count differs.")]
    public async Task ConsumerComparerRejectsSpecificCorruption(string fault, string reason)
    {
        (JsonObject catalog, JsonObject row, JsonObject actual) = await ReadPositive("CONSUMER-BLOCK-JSONL-STDERR");
        ConsoleConsumerExpectations.Damage(actual, fault);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => ConsoleConsumerExpectations.Validate(catalog, row, actual, physicalBytes: true));
        Assert.AreEqual(reason, error.Message);
    }
    [TestMethod]
    public async Task IndependentConsumersHaveUniqueInstanceIdentities()
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (object[] row in Rows)
        {
            (_, _, JsonObject actual) = await ReadIdentity((string)row[0]);
            Assert.IsTrue(identities.Add((string)actual["resource"]!["instance_id"]!), "A fresh consumer reused its instance identity.");
        }
        Assert.AreEqual(38, identities.Count);
    }

    private async Task<(JsonObject Catalog, JsonObject Row, JsonObject Actual)> ReadIdentity(string id)
    {
#if !CIS_MUTATION_TESTS
        CompletedContext context = CurrentContext(id);
        if (CompletedFunctionalInputs.TryGetValue(context, out CompletedObservation? capture))
        {
            string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
            if (capture.FixtureSha256 == Hash(host) && capture.CoreSha256 == Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll"))
                && capture.InputsSha256 == Hash(FixtureProcessChecks.Find("console-consumer-cases.json")))
            {
                JsonObject reports = JsonNode.Parse(capture.Json)!.AsObject(), catalog = reports["catalog"]!.AsObject(), actual = reports["actual"]!.AsObject();
                JsonObject row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
                ConsoleConsumerExpectations.Validate(catalog, row, actual, physicalBytes: true);
                JsonNode observed = actual["observed"]!;
                Assert.AreEqual((string?)observed["operatingSystem"], (string?)actual["resource"]!["os_family"]);
                Assert.AreEqual(observed["processId"]!.GetValue<int>(), actual["resource"]!["process_id"]!.GetValue<int>());
                TestContext.WriteLine($"Completed physical consumer identity input: caseId={id}; processId={observed["processId"]}; fixtureSha256={capture.FixtureSha256}; coreSha256={capture.CoreSha256}; inputsSha256={capture.InputsSha256}; environmentSha256={context.EnvironmentSha256}; newChild=false.");
                return (catalog, row, actual);
            }
        }
#endif
        // Missing, pending or changed-context observations always run a fresh child.
        return await Run(id);
    }

#if !CIS_MUTATION_TESTS
    private static CompletedContext CurrentContext(string id)
    {
        var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(StringComparer.Ordinal)
            .Select(key => new KeyValuePair<string, string?>(key, Environment.GetEnvironmentVariable(key))).ToArray();
        string hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(environment)));
        return new(id, CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name, hash);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
#endif

    private async Task<(JsonObject Catalog, JsonObject Row, JsonObject Actual)> ReadPositive(string id)
    {
#if CIS_MUTATION_TESTS
        return await Run(id);
#else
        string text = await ReaderPositives.GetOrAdd(id, value => new Lazy<Task<string>>(() => CapturePositive(value))).Value;
        JsonObject reports = JsonNode.Parse(text)!.AsObject(), catalog = reports["catalog"]!.AsObject(), actual = reports["actual"]!.AsObject();
        JsonObject row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        ConsoleConsumerExpectations.Validate(catalog, row, actual, physicalBytes: true);
        JsonNode observed = actual["observed"]!;
        Assert.AreEqual((string?)observed["operatingSystem"], (string?)actual["resource"]!["os_family"]);
        Assert.AreEqual(observed["processId"]!.GetValue<int>(), actual["resource"]!["process_id"]!.GetValue<int>());
        return (catalog, row, actual);
#endif
    }

#if !CIS_MUTATION_TESTS
    private async Task<string> CapturePositive(string id)
    {
        (JsonObject catalog, _, JsonObject actual) = await Run(id);
        return new JsonObject { ["catalog"] = catalog, ["actual"] = actual }.ToJsonString();
    }
#endif

    private async Task<(JsonObject Catalog, JsonObject Row, JsonObject Actual)> Run(string id)
    {
        JsonObject catalog = LogValueProcessTests.Read("console-consumer-cases.json"), row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await ConsoleProcessChecks.Capture("consumer", row, TestContext, ConsoleConsumerExpectations.EnvironmentFor(row));
        ConsoleConsumerExpectations.Validate(catalog, row, actual, physicalBytes: true);
        JsonNode observed = actual["observed"]!;
        Assert.AreEqual((string?)observed["operatingSystem"], (string?)actual["resource"]!["os_family"]);
        Assert.AreEqual(observed["processId"]!.GetValue<int>(), actual["resource"]!["process_id"]!.GetValue<int>());
        return (catalog, row, actual);
    }
}
