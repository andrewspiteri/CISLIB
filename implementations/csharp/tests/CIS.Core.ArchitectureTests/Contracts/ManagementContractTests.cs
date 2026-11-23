using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests.Contracts;

[TestClass]
public sealed class ManagementContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly JsonDocument Schema = Read("contracts/management/v1/contract.schema.json");

    private static readonly JsonDocument Wire = Read("conformance/management/v1/wire-cases.json");

    private static readonly JsonDocument OpenApi = Read("contracts/management/v1/openapi.json");

    private static readonly HashSet<string> SchemaNames = new(StringComparer.Ordinal)
    {
        "RegistrationRequest", "Observation", "InventorySnapshot", "MetadataCommand",
        "Application", "MetadataResult", "Problem"
    };

    public static IEnumerable<object[]> WireCases => Wire.RootElement.GetProperty("cases").EnumerateArray()
        .Select(fixture => new object[] { fixture.GetProperty("id").GetString()! });

    [TestMethod]
    [DynamicData(nameof(WireCases))]
    public void IndependentWireCasesCheckTheSelectedPortableSchema(string id)
    {
        JsonElement fixture = Find(id);
        string name = fixture.GetProperty("schema").GetString()!;
        Assert.IsTrue(SchemaNames.Contains(name), id + ": declared schema");

        using JsonDocument selected = SelectSchema(name);
        var validator = new FixtureSchemaValidator(selected.RootElement);
        bool producer = fixture.GetProperty("direction").GetString() == "request";
        bool accepted;
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(fixture.GetProperty("json").GetString()!);
            accepted = validator.Accepts(parsed.RootElement, producer);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        {
            // Malformed JSON or a string that cannot be decoded is not an accepted
            // schema input. The runtime/client tests separately check full wire validity.
            accepted = false;
        }

        Assert.AreEqual(fixture.GetProperty("schemaValid").GetBoolean(), accepted, id);
    }

    [TestMethod]
    public void FixtureCatalogKeepsSchemaAndRuntimeExpectationsSeparate()
    {
        JsonElement catalog = Wire.RootElement;
        JsonElement[] cases = catalog.GetProperty("cases").EnumerateArray().ToArray();
        Assert.AreEqual("cis.management", catalog.GetProperty("contract").GetString());
        Assert.AreEqual(1, catalog.GetProperty("version").GetInt32());
        Assert.AreEqual(catalog.GetProperty("caseCount").GetInt32(), cases.Length);
        Assert.AreEqual(cases.Length,
            cases.Select(fixture => fixture.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.IsFalse(string.IsNullOrWhiteSpace(catalog.GetProperty("validationBoundary").GetString()));
        Assert.IsTrue(SchemaNames.SetEquals(cases.Select(fixture => fixture.GetProperty("schema").GetString()!)));

        foreach (JsonElement fixture in cases)
        {
            string? direction = fixture.GetProperty("direction").GetString();
            Assert.IsTrue(direction is "request" or "response");
            _ = fixture.GetProperty("valid").GetBoolean();
            _ = fixture.GetProperty("schemaValid").GetBoolean();
            Assert.IsNotNull(fixture.GetProperty("json").GetString());
        }

        // A parsed JSON value/schema cannot prove duplicate-name rejection,
        // signed Int64 range, calendar dates or native Jobs byte/reference rules.
        foreach (string id in new[]
        {
            "metadata-duplicate-command-field",
            "metadata-escaped-duplicate-command-field",
            "observation-int64-overflow-is-runtime-rule",
            "observation-calendar-validity-is-runtime-rule",
            "inventory-category-utf8-byte-bound-is-runtime-rule",
            "inventory-job-utf8-byte-bound-is-runtime-rule",
            "inventory-duplicate-scope-category-is-runtime-rule",
            "inventory-duplicate-scope-job-is-runtime-rule",
            "inventory-missing-category-is-runtime-rule"
        })
        {
            JsonElement fixture = Find(id);
            Assert.IsTrue(fixture.GetProperty("schemaValid").GetBoolean(), id);
            Assert.IsFalse(fixture.GetProperty("valid").GetBoolean(), id);
        }
    }

    [TestMethod]
    public void LargeValuesTimestampsAndUnicodeRemainLiteralFixtureData()
    {
        using JsonDocument application = Parse("application-lossless-revision");
        JsonElement revision = application.RootElement.GetProperty("metadataRevision");
        Assert.AreEqual(JsonValueKind.String, revision.ValueKind);
        Assert.AreEqual("9007199254740993", revision.GetString());

        using JsonDocument observation = Parse("observation-lossless-large-sequence");
        Assert.AreEqual("9007199254740993", observation.RootElement.GetProperty("sequence").GetString());
        Assert.AreEqual("2026-11-23T12:34:56.1234567Z", observation.RootElement.GetProperty("observedAtUtc").GetString());

        using JsonDocument result = Parse("metadata-original-matching-replay");
        Assert.AreEqual("2026-11-23T12:34:56.1234567Z",
            result.RootElement.GetProperty("result").GetProperty("decidedAtUtc").GetString());

        using JsonDocument inventory = Parse("inventory-lossless-native-owner-and-revision");
        JsonElement job = inventory.RootElement.GetProperty("jobs")[0];
        Assert.AreEqual("9007199254740993", job.GetProperty("effectiveConfiguration").GetProperty("revision").GetString());
        JsonElement owner = job.GetProperty("observations")[0].GetProperty("observedOwner");
        Assert.AreEqual("9007199254740993", owner.GetProperty("generation").GetString());
        Assert.AreEqual("2026-11-23T12:34:56.1234567Z", owner.GetProperty("observedAtUtc").GetString());

        using JsonDocument decomposed = Parse("metadata-unicode-decomposed-preserved");
        string displayName = decomposed.RootElement.GetProperty("displayName").GetString()!;
        Assert.AreEqual("Cafe\u0301 — 日本語", displayName);
        Assert.IsFalse(displayName.IsNormalized(NormalizationForm.FormC));

        using JsonDocument omitted = Parse("metadata-description-omission-clears");
        Assert.IsFalse(omitted.RootElement.TryGetProperty("description", out _));
        using JsonDocument explicitNull = Parse("metadata-explicit-null-description-rejected");
        Assert.AreEqual(JsonValueKind.Null, explicitNull.RootElement.GetProperty("description").ValueKind);
    }

    [TestMethod]
    public void OutcomeFixturesKeepOriginalCommitAndCurrentAttemptDistinct()
    {
        using JsonDocument replay = Parse("metadata-replay-retains-original-through-attempt-cleanup");
        Assert.AreEqual("replayed", replay.RootElement.GetProperty("resultSource").GetString());
        Assert.AreEqual("committed", replay.RootElement.GetProperty("originalCompletion").GetString());
        JsonElement attempt = replay.RootElement.GetProperty("attemptCompletion");
        Assert.AreEqual("notCommitted", attempt.GetProperty("outcome").GetString());
        Assert.IsFalse(attempt.GetProperty("cleanupSucceeded").GetBoolean());

        using JsonDocument unknown = Parse("problem-unknown-completion-retains-operation");
        Assert.AreEqual("edit-1", unknown.RootElement.GetProperty("operation").GetProperty("key").GetString());
        Assert.AreEqual("unknown", unknown.RootElement.GetProperty("result").GetProperty("originalCompletion").GetString());

        using JsonDocument future = Parse("metadata-unknown-response-status-preserved");
        Assert.AreEqual("future-completion", future.RootElement.GetProperty("originalCompletion").GetString());
        Assert.IsFalse(Find("metadata-unknown-response-status-preserved")
            .GetProperty("expected").GetProperty("knownOriginalCompletion").GetBoolean());
    }

    [TestMethod]
    public void OpenApiContainsAuthenticatedOperationsAndResolvableExamplesAndSchemas()
    {
        (string Method, string Path)[] required =
        [
            ("get", "/service-info"),
            ("put", "/registrations/{registrationId}"),
            ("put", "/registrations/{registrationId}/observation"),
            ("put", "/registrations/{registrationId}/inventory"),
            ("delete", "/registrations/{registrationId}"),
            ("get", "/applications"),
            ("get", "/applications/{applicationId}"),
            ("get", "/applications/{applicationId}/instances"),
            ("get", "/applications/{applicationId}/inventory"),
            ("get", "/applications/{applicationId}/capabilities"),
            ("put", "/applications/{applicationId}/metadata"),
            ("get", "/applications/{applicationId}/metadata-operations/{operationKey}")
        ];
        JsonElement document = OpenApi.RootElement;
        JsonElement schemes = document.GetProperty("components").GetProperty("securitySchemes");
        foreach ((string method, string path) in required)
        {
            string label = method.ToUpperInvariant() + " " + path;
            JsonElement operation = document.GetProperty("paths").GetProperty(path).GetProperty(method);
            JsonElement security = operation.GetProperty("security");
            Assert.IsTrue(security.GetArrayLength() > 0, label);
            foreach (JsonElement requirement in security.EnumerateArray())
            {
                Assert.IsTrue(requirement.EnumerateObject().Any(), label + ": no anonymous alternative");
                foreach (JsonProperty scheme in requirement.EnumerateObject())
                {
                    Assert.IsTrue(schemes.TryGetProperty(scheme.Name, out _), label + ": security scheme");
                }
            }

            if (operation.TryGetProperty("requestBody", out JsonElement request))
            {
                AssertExamplesResolve(request.GetProperty("content"), null, label + ": request");
            }

            bool hasSuccess = false;
            bool hasProblem = false;
            foreach (JsonProperty response in operation.GetProperty("responses").EnumerateObject())
            {
                if (!int.TryParse(response.Name, out int status) || !response.Value.TryGetProperty("content", out JsonElement content))
                {
                    continue;
                }

                if (status is >= 200 and < 300)
                {
                    hasSuccess = true;
                    AssertExamplesResolve(content, null, label + ": success");
                }
                else if (status >= 400)
                {
                    hasProblem = true;
                    Assert.IsTrue(content.TryGetProperty("application/problem+json", out _), label);
                    AssertExamplesResolve(content, status, label + ": problem");
                }
            }

            Assert.IsTrue(hasSuccess, label + ": success example");
            Assert.IsTrue(hasProblem, label + ": problem example");
        }

        AssertReferencesResolve(document, document);
        AssertReferencesResolve(Schema.RootElement, Schema.RootElement);
    }

    private static void AssertExamplesResolve(JsonElement content, int? problemStatus, string label)
    {
        Assert.IsTrue(content.EnumerateObject().Any(), label);
        foreach (JsonProperty media in content.EnumerateObject())
        {
            JsonElement examples = media.Value.GetProperty("examples");
            Assert.IsTrue(examples.EnumerateObject().Any(), label);
            foreach (JsonProperty example in examples.EnumerateObject())
            {
                string reference = example.Value.GetProperty("$ref").GetString()!;
                Assert.IsTrue(reference.StartsWith("#/components/examples/", StringComparison.Ordinal), label);
                JsonElement value = ResolveReference(reference, OpenApi.RootElement).GetProperty("value");
                if (problemStatus is { } status)
                {
                    Assert.AreEqual(status, value.GetProperty("status").GetInt32(), label);
                }
            }
        }
    }

    private static void AssertReferencesResolve(JsonElement value, JsonElement document)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name == "$ref")
                {
                    _ = ResolveReference(property.Value.GetString()!, document);
                }
                else if (property.Name != "examples")
                {
                    // Example payloads are data, not schemas. Required operation
                    // example references are checked separately above.
                    AssertReferencesResolve(property.Value, document);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                AssertReferencesResolve(item, document);
            }
        }
    }

    private static JsonElement ResolveReference(string reference, JsonElement document)
    {
        const string canonical = "./contract.schema.json#/$defs/";
        if (reference.StartsWith(canonical, StringComparison.Ordinal))
        {
            return Schema.RootElement.GetProperty("$defs").GetProperty(reference[canonical.Length..]);
        }

        Assert.IsTrue(reference.StartsWith("#/$defs/", StringComparison.Ordinal)
            || reference.StartsWith("#/components/", StringComparison.Ordinal), reference);
        JsonElement current = document;
        foreach (string token in reference[2..].Split('/'))
        {
            string name = token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            Assert.IsTrue(current.TryGetProperty(name, out JsonElement next), reference);
            current = next;
        }

        return current;
    }

    private static JsonDocument SelectSchema(string name)
    {
        // Preserve local reference resolution in the existing evaluator while
        // selecting a particular DTO. This adds no schema keywords or engine.
        var wrapper = new JsonObject
        {
            ["$ref"] = "#/$defs/" + name,
            ["$defs"] = JsonNode.Parse(Schema.RootElement.GetProperty("$defs").GetRawText())
        };
        RemoveRuntimeAnnotations(wrapper);
        return JsonDocument.Parse(wrapper.ToJsonString());
    }

    private static void RemoveRuntimeAnnotations(JsonObject schema)
    {
        // x-* entries document runtime obligations. Removing only those schema
        // annotations does not validate them; actual Wire/client tests must do so.
        foreach (string key in schema.Select(entry => entry.Key).Where(key => key.StartsWith("x-", StringComparison.Ordinal)).ToArray())
        {
            schema.Remove(key);
        }

        foreach (string name in new[] { "$defs", "properties" })
        {
            if (schema[name] is JsonObject definitions)
            {
                foreach (JsonObject definition in definitions.Select(entry => entry.Value).OfType<JsonObject>())
                {
                    RemoveRuntimeAnnotations(definition);
                }
            }
        }

        foreach (string name in new[] { "items", "additionalProperties" })
        {
            if (schema[name] is JsonObject child)
            {
                RemoveRuntimeAnnotations(child);
            }
        }

        if (schema["anyOf"] is JsonArray alternatives)
        {
            foreach (JsonObject alternative in alternatives.OfType<JsonObject>())
            {
                RemoveRuntimeAnnotations(alternative);
            }
        }
    }

    private static JsonElement Find(string id) => Wire.RootElement.GetProperty("cases").EnumerateArray()
        .Single(fixture => fixture.GetProperty("id").GetString() == id);

    private static JsonDocument Parse(string id) => JsonDocument.Parse(Find(id).GetProperty("json").GetString()!);

    private static JsonDocument Read(string relativePath) => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, relativePath)));

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

        throw new DirectoryNotFoundException("Run the Management contract checks from the CisLib checkout.");
    }
}
