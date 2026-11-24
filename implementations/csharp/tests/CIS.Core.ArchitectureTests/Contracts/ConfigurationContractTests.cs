using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests.Contracts;

[TestClass]
public sealed class ConfigurationContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly JsonDocument Schema = Read("contracts/management/v1/contract.schema.json");
    private static readonly JsonDocument Wire = Read("conformance/management/v1/configuration-cases.json");
    private static readonly JsonDocument OpenApi = Read("contracts/management/v1/openapi.json");

    public static IEnumerable<object[]> WireCases => Wire.RootElement.GetProperty("cases").EnumerateArray()
        .Select(value => new object[] { value.GetProperty("id").GetString()! });

    [TestMethod]
    [DynamicData(nameof(WireCases))]
    public void IndependentlyAuthoredConfigurationVectorsMatchTheirDeclaredSchema(string id)
    {
        JsonElement fixture = Find(id);
        using JsonDocument selected = SelectSchema(fixture.GetProperty("schema").GetString()!);
        var validator = new FixtureSchemaValidator(selected.RootElement);
        using JsonDocument value = JsonDocument.Parse(fixture.GetProperty("json").GetString()!);

        Assert.AreEqual(fixture.GetProperty("schemaValid").GetBoolean(),
            validator.Accepts(value.RootElement, fixture.GetProperty("direction").GetString() == "request"), id);
    }

    [TestMethod]
    public void PortableVectorsRetainLargeGenerationsInitialZeroAndExactUnicodeDigest()
    {
        using JsonDocument snapshot = Parse("configuration-snapshot-valid");
        JsonElement value = snapshot.RootElement;
        Assert.AreEqual("9007199254740993", value.GetProperty("desired").GetProperty("generation").GetString());
        JsonElement source = value.GetProperty("commonSource");
        string payload = source.GetProperty("payloadJson").GetString()!;
        Assert.AreEqual("{\"Greeting\":\"Café — 日本語\",\"Enabled\":false}", payload);
        const string expected = "6b22cc1785a4a64b26ce415be9c4a3cfbe21aa5b7127644f87004d92192429d9";
        Assert.AreEqual(expected, source.GetProperty("sha256").GetString());
        Assert.AreEqual(expected, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));

        using JsonDocument report = Parse("configuration-statusreport-valid");
        Assert.AreEqual("0", report.RootElement.GetProperty("entries")[0].GetProperty("applied")
            .GetProperty("localRevision").GetString());
        Assert.AreEqual("2026-11-24T00:12:34.1234567Z", report.RootElement.GetProperty("observedAtUtc").GetString());

        foreach (string id in new[] { "configuration-int64-overflow-runtime-bound",
            "configuration-payload-digest-mismatch", "configuration-duplicate-decoded-payload-name",
            "configuration-payload-bom-refused", "configuration-duplicate-command-name" })
        {
            JsonElement fixture = Find(id);
            Assert.IsTrue(fixture.GetProperty("schemaValid").GetBoolean(), id);
            Assert.IsFalse(fixture.GetProperty("valid").GetBoolean(), id);
        }
    }

    [TestMethod]
    public void OpenApiDeclaresNineteenFixedAuthenticatedOperationsAndSpecificBounds()
    {
        (string Method, string Path, string Id)[] operations =
        [
            ("get", "/configuration/definitions", "getConfigurationDefinitions"),
            ("post", "/configuration/validate", "validateConfiguration"),
            ("post", "/configuration/preview", "previewConfiguration"),
            ("get", "/configuration/sources/{sourceId}", "getConfigurationSource"),
            ("post", "/configuration/sources/{sourceId}/publications", "publishConfigurationSource"),
            ("get", "/configuration/publications/{publicationId}", "getConfigurationPublication"),
            ("post", "/applications/{applicationId}/configuration/{configurationId}/releases", "createConfigurationRelease"),
            ("get", "/configuration/releases/{releaseId}", "getConfigurationRelease"),
            ("get", "/applications/{applicationId}/configuration/{configurationId}/assignments", "getConfigurationAssignments"),
            ("put", "/applications/{applicationId}/configuration/{configurationId}/assignments", "replaceConfigurationAssignments"),
            ("get", "/registrations/{registrationId}/configuration/{configurationId}", "getRegistrationConfiguration"),
            ("put", "/registrations/{registrationId}/configuration-status", "reportConfigurationStatus"),
            ("get", "/applications/{applicationId}/configuration-status", "getConfigurationStatus"),
            ("get", "/configuration/catalog", "getConfigurationCatalog"),
            ("post", "/configuration/catalog/apply", "applyConfigurationCatalog"),
            ("get", "/configuration/catalog/operations/{operationKey}", "getConfigurationCatalogOperation"),
            ("get", "/configuration/sources/{sourceId}/publication-operations/{operationKey}", "getConfigurationPublicationOperation"),
            ("get", "/applications/{applicationId}/configuration/{configurationId}/release-operations/{operationKey}", "getConfigurationReleaseOperation"),
            ("get", "/applications/{applicationId}/configuration/{configurationId}/assignment-operations/{operationKey}", "getConfigurationAssignmentOperation")
        ];

        foreach ((string method, string path, string id) in operations)
        {
            JsonElement operation = OpenApi.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
            Assert.AreEqual(id, operation.GetProperty("operationId").GetString());
            Assert.AreEqual("configuration", operation.GetProperty("x-cis-rest-operation-alias").GetString());
            Assert.AreEqual(1048576, operation.GetProperty("x-cis-max-request-bytes").GetInt32());
            foreach (JsonElement security in operation.GetProperty("security").EnumerateArray())
            {
                Assert.IsTrue(security.EnumerateObject().Any(), id);
            }
            if (path.Contains("{configurationId}", StringComparison.Ordinal))
            {
                Assert.IsTrue(operation.GetProperty("parameters").EnumerateArray().Any(parameter =>
                    parameter.GetProperty("name").GetString() == "schemaVersion"
                    && parameter.GetProperty("in").GetString() == "query"
                    && parameter.GetProperty("required").GetBoolean()), id);
            }
        }

        Assert.AreEqual(65536, Schema.RootElement.GetProperty("$defs").GetProperty("MetadataCommand")
            .GetProperty("x-cis-default-max-body-bytes").GetInt32());
        JsonElement conditional = OpenApi.RootElement.GetProperty("paths")
            .GetProperty("/registrations/{registrationId}/configuration/{configurationId}").GetProperty("get");
        Assert.IsFalse(conditional.GetProperty("responses").GetProperty("304").TryGetProperty("content", out _));
        Assert.AreEqual("read-only", OpenApi.RootElement.GetProperty("paths").GetProperty("/configuration/validate")
            .GetProperty("post").GetProperty("x-cis-effect").GetString());
    }

    [TestMethod]
    public void OriginalAssignmentResultPreservesCompleteMapAndIndependentCleanup()
    {
        using JsonDocument document = Parse("configuration-operationresult-valid");
        JsonElement value = document.RootElement;
        Assert.AreEqual("committed", value.GetProperty("originalCompletion").GetString());
        Assert.AreEqual("notCommitted", value.GetProperty("attemptCompletion").GetProperty("outcome").GetString());
        JsonElement decision = value.GetProperty("result");
        Assert.AreEqual("promote", decision.GetProperty("intent").GetString());
        Assert.AreEqual(0, decision.GetProperty("assignment").GetProperty("overrides").GetArrayLength());
        Assert.IsFalse(value.GetRawText().Contains("payloadJson", StringComparison.Ordinal));
        Assert.AreEqual(Wire.RootElement.GetProperty("caseCount").GetInt32(),
            Wire.RootElement.GetProperty("cases").GetArrayLength());
    }

    [TestMethod]
    public void PreviewDistinguishesSourceSelectionFromCompletePromotionAndRetainsReadPreconditions()
    {
        using JsonDocument source = Parse("configuration-preview-valid");
        Assert.IsTrue(source.RootElement.TryGetProperty("commonSource", out _));
        Assert.IsTrue(source.RootElement.TryGetProperty("applicationSource", out _));
        Assert.IsFalse(source.RootElement.TryGetProperty("proposedAssignment", out _));

        using JsonDocument assignment = Parse("configuration-assignment-preview-complete-scope");
        JsonElement result = assignment.RootElement;
        Assert.IsFalse(result.TryGetProperty("commonSource", out _));
        Assert.IsTrue(result.GetProperty("currentAssignment").GetProperty("eTag").GetString()!.StartsWith('"'));
        JsonElement proposed = result.GetProperty("proposedAssignment");
        Assert.AreEqual("promote", proposed.GetProperty("intent").GetString());
        Assert.AreEqual(0, proposed.GetProperty("overrides").GetArrayLength());
        Assert.AreEqual("reportedCompatible", result.GetProperty("recipients").GetProperty("items")[0]
            .GetProperty("compatibility").GetString());
        Assert.IsFalse(result.GetRawText().Contains("payloadJson", StringComparison.Ordinal));
        Assert.IsFalse(Find("configuration-preview-refuses-ambiguous-source-and-map").GetProperty("schemaValid").GetBoolean());
    }

    [TestMethod]
    public void DiscoverySuppliesPublicationAndAssignmentResourceIdsWithoutLeakingCommonApplicationBindings()
    {
        using JsonDocument common = Parse("configuration-common-source-discovery-without-application-identity");
        JsonElement commonRow = common.RootElement.GetProperty("items")[0];
        JsonElement source = OpenApi.RootElement.GetProperty("components").GetProperty("examples")
            .GetProperty("ConfigurationSource").GetProperty("value");
        Assert.AreEqual(source.GetProperty("sourceId").GetString(), commonRow.GetProperty("sourceId").GetString());
        Assert.IsFalse(commonRow.TryGetProperty("applicationId", out _));
        Assert.IsFalse(commonRow.TryGetProperty("bindingId", out _));
        Assert.IsFalse(commonRow.TryGetProperty("hostApplicationName", out _));

        using JsonDocument application = Parse("configuration-definitionpage-valid");
        JsonElement applicationRow = application.RootElement.GetProperty("items")[0];
        JsonElement assignment = OpenApi.RootElement.GetProperty("components").GetProperty("examples")
            .GetProperty("ConfigurationAssignmentMap").GetProperty("value");
        foreach (string field in new[] { "applicationId", "bindingId", "configurationId", "schemaVersion" })
        {
            Assert.AreEqual(assignment.GetProperty(field).GetString(), applicationRow.GetProperty(field).GetString(), field);
        }
        Assert.AreEqual("ExampleHost", applicationRow.GetProperty("hostApplicationName").GetString());
        Assert.AreEqual(commonRow.GetProperty("defaultsJson").GetString(), applicationRow.GetProperty("defaultsJson").GetString());
    }

    private static JsonDocument SelectSchema(string name)
    {
        var wrapper = new JsonObject
        {
            ["$ref"] = "#/$defs/" + name,
            ["$defs"] = JsonNode.Parse(Schema.RootElement.GetProperty("$defs").GetRawText())
        };
        RemoveAnnotations(wrapper);
        return JsonDocument.Parse(wrapper.ToJsonString());
    }

    private static void RemoveAnnotations(JsonNode? node)
    {
        if (node is JsonObject item)
        {
            foreach (string key in item.Select(property => property.Key).Where(key => key.StartsWith("x-", StringComparison.Ordinal)).ToArray())
            {
                item.Remove(key);
            }
            foreach (JsonNode? child in item.Select(property => property.Value))
            {
                RemoveAnnotations(child);
            }
        }
        else if (node is JsonArray items)
        {
            foreach (JsonNode? child in items)
            {
                RemoveAnnotations(child);
            }
        }
    }

    private static JsonElement Find(string id) => Wire.RootElement.GetProperty("cases").EnumerateArray()
        .Single(value => value.GetProperty("id").GetString() == id);
    private static JsonDocument Parse(string id) => JsonDocument.Parse(Find(id).GetProperty("json").GetString()!);
    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, path)));
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "contracts/management/v1/openapi.json")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("The canonical Management contract is required.");
    }
}
