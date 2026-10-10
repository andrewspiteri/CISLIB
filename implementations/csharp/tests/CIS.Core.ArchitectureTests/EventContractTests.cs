using System.Text.Json;
using CIS.Tests.ArchitectureTests.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class EventContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly JsonDocument Schema = Read("contracts/logging/v1/event.schema.json");
    private static readonly JsonDocument Contract = Read("contracts/logging/v1/event-contract.json");
    private static readonly JsonDocument Records = Read("conformance/logging/event-record-cases.json");
    private static readonly JsonDocument Values = Read("conformance/logging/event-value-cases.json");

    public static IEnumerable<object[]> RecordCases => Records.RootElement.GetProperty("cases").EnumerateArray().Select(item => new object[] { item.GetProperty("id").GetString()! });
    public static IEnumerable<object[]> ValueCases => Values.RootElement.GetProperty("cases").EnumerateArray().Select(item => new object[] { item.GetProperty("id").GetString()! });

    [TestMethod]
    [DynamicData(nameof(RecordCases))]
    public void EveryRecordFixtureMatchesItsExpectedValidity(string id)
    {
        JsonElement fixture = Find(Records, id);
        var validator = new EventRecordValidator(Schema.RootElement, Contract.RootElement);
        Assert.AreEqual(fixture.GetProperty("expectedValid").GetBoolean(), validator.Accepts(fixture), id);
    }

    [TestMethod]
    [DynamicData(nameof(ValueCases))]
    public void EveryValueFixtureMatchesItsExplicitRepresentation(string id)
    {
        JsonElement fixture = Find(Values, id);
        var validator = new ValueFixtureValidator(Contract.RootElement);
        if (!fixture.GetProperty("expectedValid").GetBoolean())
        {
            Assert.ThrowsExactly<InvalidDataException>(() => validator.Normalize(fixture.GetProperty("input")), id);
            return;
        }

        JsonElement observed = validator.Normalize(fixture.GetProperty("input"));
        Assert.IsTrue(JsonElement.DeepEquals(fixture.GetProperty("expectedJson"), observed), id + ": " + observed.GetRawText());
    }

    [TestMethod]
    public void AllGuideBoundsAndProjectionFieldsAreDeclared()
    {
        var expected = new Dictionary<string, int>
        {
            ["recordUtf8BytesIncludingLf"] = 65536,
            ["attributeDepthIncludingRoot"] = 8,
            ["attributeNodesIncludingRoot"] = 1024,
            ["eventNameScalars"] = 255,
            ["componentScalars"] = 255,
            ["attributeKeyScalars"] = 255,
            ["operationIdScalars"] = 255,
            ["messageScalars"] = 8192,
            ["templateScalars"] = 8192,
            ["exceptionNodes"] = 16,
            ["exceptionDepthIncludingRoot"] = 4,
            ["exceptionMessageScalars"] = 4096,
            ["exceptionStackScalars"] = 16384,
            ["operationScopeLevels"] = 32,
            ["applicationNameScalars"] = 255,
            ["environmentNameScalars"] = 255,
            ["versionAndBuildIdentifierScalars"] = 512
        };
        foreach ((string name, int value) in expected)
        {
            Assert.AreEqual(value, Contract.RootElement.GetProperty("bounds").GetProperty(name).GetInt32(), name);
        }

        string[] resource = Contract.RootElement.GetProperty("resourceProjection").EnumerateObject().Select(item => item.Value.GetString()!).Order().ToArray();
        CollectionAssert.AreEqual(Schema.RootElement.GetProperty("properties").GetProperty("resource").GetProperty("properties").EnumerateObject().Select(item => item.Name).Order().ToArray(), resource);
        Assert.AreEqual(14, resource.Length);
        foreach (JsonDocument catalog in new[] { Records, Values })
        {
            JsonElement[] cases = catalog.RootElement.GetProperty("cases").EnumerateArray().ToArray();
            Assert.AreEqual(catalog.RootElement.GetProperty("caseCount").GetInt32(), cases.Length);
            Assert.AreEqual(cases.Length, cases.Select(item => item.GetProperty("id").GetString()).Distinct().Count());
            Assert.IsTrue(cases.Any(item => item.GetProperty("expectedValid").GetBoolean()));
            Assert.IsTrue(cases.Any(item => !item.GetProperty("expectedValid").GetBoolean()));
        }

        using JsonDocument configuration = Read("contracts/logging/v1/configuration.json");
        JsonElement limit = configuration.RootElement.GetProperty("settings").EnumerateArray().Single(item => item.GetProperty("key").GetString() == "CORE_LOG_MAX_ENTRY_BYTES");
        Assert.AreEqual(65536, limit.GetProperty("default").GetInt32());
        Assert.AreEqual("Logging.MaxEntryBytes", limit.GetProperty("property").GetString());
    }

    [TestMethod]
    public void CaptureExpectationsRemainExplicitAndPending()
    {
        using JsonDocument expectations = Read("conformance/logging/event-capture-expectations.json");
        Assert.IsTrue(expectations.RootElement.GetProperty("scope").GetString()!.Contains("not results", StringComparison.Ordinal));
        Assert.AreEqual(expectations.RootElement.GetProperty("caseCount").GetInt32(), expectations.RootElement.GetProperty("cases").GetArrayLength());
        foreach (string required in new[] { "CAPTURE-NESTED-OWNERSHIP", "CAPTURE-NESTED-OVERRIDES", "CAPTURE-SCOPE-32", "CAPTURE-SCOPE-33", "CAPTURE-EXCEPTION-DEPTH-CLIP", "CAPTURE-SIZE-TRUNCATION", "CAPTURE-FORWARDED-PRODUCER" })
        {
            Assert.IsTrue(expectations.RootElement.GetProperty("cases").EnumerateArray().Any(item => item.GetProperty("id").GetString() == required), required);
        }

        Assert.AreEqual("Truncate", Contract.RootElement.GetProperty("oversize").GetProperty("policy").GetString());
    }

    [TestMethod]
    public void ValidatorRejectsUnsupportedSchemaKeywords()
    {
        using JsonDocument unsupported = JsonDocument.Parse("{\"type\":\"object\",\"unevaluatedProperties\":false}");
        Assert.ThrowsExactly<NotSupportedException>(() => new FixtureSchemaValidator(unsupported.RootElement));
    }

    private static JsonElement Find(JsonDocument catalog, string id) => catalog.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);

    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, path)));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "implementations", "csharp", "CisLib.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run the contract checks from the CisLib checkout.");
    }
}
