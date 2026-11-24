using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationDefaultsTests
{
    [TestMethod]
    [DataRow("null")]
    [DataRow("captured")]
    [DataRow("overlay")]
    public async Task NativePublicationsAndReleaseUseTheSameDeclaredObjectDefaultsAsActualLocalAcceptance(string baseline)
    {
        ManagementConfigurationOptions selected = Nested(baseline);
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: selected);
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "defaults-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(ManagementConfigurationWire.EncodeResponse(definitions))
            .Contains("objectDefaults", StringComparison.Ordinal), "Declared default values are local manifest data, never public shape metadata.");
        string commonId = definitions.Items.Single(item => item.Kind == "common").SourceId;
        string applicationId = definitions.Items.Single(item => item.ApplicationId == fixture.ApplicationA).SourceId;
        string common = baseline == "overlay"
            ? "{\"Items\":[{\"Left\":40}],\"Scores\":{\"common\":{\"Left\":50}}}"
            : "{\"Child\":null,\"Items\":[{\"Left\":40}],\"Scores\":{\"common\":{\"Left\":50}}}";
        const string application = "{\"Child\":{\"Right\":9},\"Items\":[{\"Right\":8}],\"Scores\":{\"chosen\":{\"Right\":7}}}";
        string first = await PublishValue(actor, commonId, "default-common", common);
        string second = await PublishValue(actor, applicationId, "default-application", application);
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        string release = (await CreateRelease(actor, fixture.ApplicationA, map.ETag, "default-release",
            new(1, new("present", first), new("present", second), "Preserve declared object defaults"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, map.ETag, "default-assignment", new(1, "replace", release, [], "Assign reviewed exact pair"));

        HttpClient workload = fixture.Client("workload-a");
        string registration = Guid.NewGuid().ToString("D");
        using (var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + registration))
        {
            request.Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request("default-consumer")));
            request.Content.Headers.ContentType = new("application/json");
            using HttpResponseMessage registered = await workload.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, registered.StatusCode);
        }
        using HttpResponseMessage response = await workload.GetAsync("registrations/" + registration + "/configuration/shared.settings?schemaVersion=1");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        ManagementConfigurationSnapshot snapshot = await Decode<ManagementConfigurationSnapshot>(response);
        Assert.AreEqual(common, snapshot.CommonSource.PayloadJson);
        Assert.AreEqual(application, snapshot.ApplicationSource.PayloadJson);
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(common), snapshot.CommonSource.Sha256);
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(application), snapshot.ApplicationSource.Sha256);

        ManagementConfigurationSchema declaration = selected.Manifest.Definitions.Single();
        JsonNode effective = ManagementConfigurationComposition.Compose(Binding(declaration), new(snapshot.CommonSource, snapshot.ApplicationSource));
        var options = new ConfigurationOptions("FirstHost")
        {
            Factories = new Dictionary<Type, Func<IConfigurationObject>>
            {
                [typeof(DeclaredDefaultsSettings)] = () => new DeclaredDefaultsSettings
                {
                    Child = baseline == "null" ? null : new DeclaredChild { Left = 30, Right = 40 }
                }
            }
        };
        ConfigurationCatalog catalog = ConfigurationCatalog.Create([typeof(DeclaredDefaultsSettings)], options);
        ConfigurationEntry entry = catalog.Entries.Single();
        var pipeline = new ConfigurationValuePipeline();
        using JsonDocument lower = JsonDocument.Parse(snapshot.CommonSource.PayloadJson!);
        using JsonDocument upper = JsonDocument.Parse(snapshot.ApplicationSource.PayloadJson!);
        ConfigurationPreparedValues prepared = pipeline.Prepare(entry, lower.RootElement, upper.RootElement);
        Assert.IsFalse(pipeline.Apply(entry, prepared, CancellationToken.None, static () => false, initial: true));
        var root = (DeclaredDefaultsSettings)entry.Instance;
        int left = baseline == "overlay" ? 30 : 1;
        Assert.IsNotNull(root.Child);
        Assert.AreEqual((left, 9), (root.Child.Left, root.Child.Right));
        Assert.AreEqual((1, 8), (root.Items.Single().Left, root.Items.Single().Right));
        CollectionAssert.AreEquivalent(new[] { "chosen" }, root.Scores.Keys.ToArray());
        Assert.AreEqual((1, 7), (root.Scores["chosen"].Left, root.Scores["chosen"].Right));
        Assert.AreEqual(left, effective["Child"]!["Left"]!.GetValue<int>());
        Assert.AreEqual(9, effective["Child"]!["Right"]!.GetValue<int>());
        Assert.AreEqual(1, effective["Items"]![0]!["Left"]!.GetValue<int>());
        Assert.AreEqual(1, effective["Scores"]!["chosen"]!["Left"]!.GetValue<int>());
        Assert.AreEqual(0L, entry.Revision);
        Assert.IsNotNull(entry.AcceptedChecksum, "The actual Configuration commit, not just detached mapping, accepted these values.");

        ManagementConfigurationSource before = await Source(actor, applicationId);
        using HttpResponseMessage invalid = await Publish(actor, before, "invalid-member", "{\"Items\":[{\"Right\":\"wrong\"}]}");
        Assert.AreEqual(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.AreEqual("management.configuration.structural_rejection", (await Decode<ManagementConfigurationProblem>(invalid)).Code);
        Assert.AreEqual(before.ETag, (await Source(actor, applicationId)).ETag);
    }

    [TestMethod]
    public void FlatDeclarationsKeepTheirOriginalBytesAndNestedTemplatesAreDetachedSortedImmutableData()
    {
        ManagementConfigurationSchema flat = Deployment().Manifest.Definitions.Single();
        byte[] previous = JsonSerializer.SerializeToUtf8Bytes(new
        {
            flat.ConfigurationId,
            flat.SchemaVersion,
            flat.Shape,
            flat.DefaultsJson,
            flat.Description
        }, ManagementConfigurationOptions.Json);
        byte[] current = JsonSerializer.SerializeToUtf8Bytes(flat, ManagementConfigurationOptions.Json);
        CollectionAssert.AreEqual(previous, current);
        CollectionAssert.AreEqual(SHA256.HashData(previous), SHA256.HashData(current));
        ManagementConfigurationOptions nested = Nested("null");
        ManagementConfigurationSchema frozen = nested.Manifest.Definitions.Single();
        IReadOnlyList<ManagementConfigurationObjectDefault> frozenTemplates = frozen.ObjectDefaults
            ?? throw new AssertFailedException("The nested declaration must retain its captured templates.");
        string[] paths = frozenTemplates.Select(item => item.Path).ToArray();
        CollectionAssert.AreEqual(paths.Order(StringComparer.Ordinal).ToArray(), paths);
        ManagementConfigurationObjectDefault[] supplied = frozenTemplates.ToArray();
        ManagementConfigurationSchema changed = frozen with
        {
            ObjectDefaults = supplied
        };
        ManagementConfigurationOptions captured = Capture(changed);
        supplied[0] = supplied[0] with
        {
            DefaultsJson = "{\"Left\":99,\"Right\":2}"
        };
        Assert.AreEqual("{\"Left\":1,\"Right\":2}", captured.Manifest.Definitions.Single().ObjectDefaults![0].DefaultsJson);
        Assert.AreNotEqual(captured.ManifestDigest, Capture(changed).ManifestDigest);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("unknown")]
    [DataRow("duplicate")]
    [DataRow("partial")]
    [DataRow("invalid-type")]
    [DataRow("duplicate-json")]
    [DataRow("null")]
    public void NestedManifestTemplatesMustBeExplicitCompleteStrictAndBoundToActualObjectPaths(string invalid)
    {
        ManagementConfigurationSchema schema = Nested("null").Manifest.Definitions.Single();
        ManagementConfigurationObjectDefault[] templates = schema.ObjectDefaults!.ToArray();
        IReadOnlyList<ManagementConfigurationObjectDefault>? selected = invalid switch
        {
            "missing" => null,
            "unknown" => [.. templates, new("/members/Unknown", "{\"Left\":1,\"Right\":2}")],
            "duplicate" => [.. templates, templates[0]],
            "partial" => [templates[0] with { DefaultsJson = "{\"Right\":2}" }, .. templates.Skip(1)],
            "invalid-type" => [templates[0] with { DefaultsJson = "{\"Left\":true,\"Right\":2}" }, .. templates.Skip(1)],
            "duplicate-json" => [templates[0] with { DefaultsJson = "{\"Left\":1,\"Left\":2,\"Right\":2}" }, .. templates.Skip(1)],
            "null" => [templates[0] with { DefaultsJson = "null" }, .. templates.Skip(1)],
            _ => throw new AssertFailedException("An explicit invalid template scenario is required.")
        };
        Assert.ThrowsExactly<ArgumentException>(() => Capture(schema with { ObjectDefaults = selected }));
    }

    [TestMethod]
    public void ExistingDeepUnicodeMemberNamesHaveACompleteFiniteStructuralTemplatePath()
    {
        string name = string.Concat(Enumerable.Repeat("\U0001f30d", 128));
        var leaf = new ManagementConfigurationShape("object", true, [new("Value", true, new("integer", false))]);
        var child = new ManagementConfigurationShape("object", true, [new(name, true, leaf)]);
        var root = new ManagementConfigurationShape("object", false, [new(name, true, child)]);
        string path = "/members/" + name;
        string nestedPath = path + "/members/" + name;
        Assert.IsGreaterThan(1024, System.Text.Encoding.UTF8.GetByteCount(nestedPath));
        string defaults = JsonSerializer.Serialize(new Dictionary<string, object?> { [name] = null });
        ManagementConfigurationSchema captured = Capture(new("shared.settings", "1", root, defaults)
        {
            ObjectDefaults = [new(path, defaults), new(nestedPath, "{\"Value\":1}")]
        }).Manifest.Definitions.Single();
        Assert.IsTrue(captured.ObjectDefaults!.Any(item => item.Path == nestedPath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReplacementCollectionDefaultExpansionRefusesBeforeExceedingTheFiniteCompositionBudget(bool bytes)
    {
        ManagementConfigurationMember[] members = bytes
            ? [new("Padding", true, new("string", false))]
            : Enumerable.Range(0, 256).Select(index => new ManagementConfigurationMember("Value" + index, true, new("integer", false))).ToArray();
        var element = new ManagementConfigurationShape("object", false, members);
        var shape = new ManagementConfigurationShape("object", false, [new("Items", true, new("array", false, Element: element))]);
        string template = bytes ? JsonSerializer.Serialize(new
        {
            Padding = new string('x', 64 * 1024)
        })
            : JsonSerializer.Serialize(Enumerable.Range(0, 256).ToDictionary(index => "Value" + index, _ => 0));
        var declaration = new ManagementConfigurationSchema("shared.settings", "1", shape, "{\"Items\":[]}")
        {
            ObjectDefaults = [new("/members/Items/element", template)]
        };
        declaration = Capture(declaration).Manifest.Definitions.Single();
        string payload = "{\"Items\":[" + string.Join(',', Enumerable.Repeat("{}", 300)) + "]}";
        Assert.IsTrue(ManagementConfigurationStructure.Validate(shape, payload).Valid);
        var source = new ManagementConfigurationSourceContent("present", PayloadJson: payload);
        ConfigurationReferenceException error = Assert.ThrowsExactly<ConfigurationReferenceException>(() =>
            ManagementConfigurationComposition.Compose(Binding(declaration), new(new("absent"), source)));
        Assert.AreEqual("management.configuration.structural_rejection", error.Code);
    }

    [TestMethod]
    public async Task DeclaredTemplatesArePartOfTheOriginalImmutableDefinitionAndCannotChangeUnderItsIdentity()
    {
        ManagementConfigurationOptions selected = Nested("null");
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: selected);
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "original-template");
        ManagementConfigurationCatalog before = await Catalog(actor);
        ManagementConfigurationSchema declaration = selected.Manifest.Definitions.Single();
        ManagementConfigurationObjectDefault[] templates = declaration.ObjectDefaults!.ToArray();
        templates[0] = templates[0] with
        {
            DefaultsJson = "{\"Left\":99,\"Right\":2}"
        };
        ManagementConfigurationOptions changed = Capture(declaration with
        {
            ObjectDefaults = templates
        });
        Assert.AreNotEqual(selected.ManifestDigest, changed.ManifestDigest);
        var options = new ManagementServiceOptions(fixture.Options.ConnectionString, fixture.Options.CopyCursorSigningKey(),
            fixture.Options.Persistence, fixture.Options.BasePath, fixture.Options.MaximumConcurrentRequests, fixture.Options.OperationsSchema,
            fixture.Options.MessagingSchema, fixture.Options.Delivery, fixture.Options.Backend, changed);
        await fixture.StopReplicasAsync();
        await fixture.StartReplicaAsync(serviceOptions: options);
        actor = fixture.Client("operator-a", fixture.Origins.Count - 1);
        ManagementConfigurationCatalog loaded = await Catalog(actor);
        using HttpResponseMessage rejected = await Send(actor, "configuration/catalog/apply", "cannot-redefine-template", loaded.ETag,
            new ManagementConfigurationCatalogCommand(1, loaded.LoadedManifestDigest, "Refuse changed declared defaults"));
        Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.AreEqual("management.configuration.schema_immutable", (await Decode<ManagementConfigurationProblem>(rejected)).Code);
        ManagementConfigurationCatalog after = await Catalog(actor);
        Assert.AreEqual(before.ETag, after.ETag);
        Assert.AreEqual(before.AppliedManifestDigest, after.AppliedManifestDigest);
        Assert.AreEqual(before.UsedBytes, after.UsedBytes);
    }

    [TestMethod]
    public void RestoringAnObjectMergesItsDeclaredNestedValuesWhileReplacementElementsCompleteTheirOwnMissingMembers()
    {
        var grand = new ManagementConfigurationShape("object", false,
            [new("Left", true, new("integer", false)), new("Right", true, new("integer", false))]);
        var child = new ManagementConfigurationShape("object", false, [new("Grand", true, grand)]);
        var shape = new ManagementConfigurationShape("object", false,
            [new("Child", true, child with { Nullable = true }), new("Items", true, new("array", false, Element: child))]);
        ManagementConfigurationSchema schema = Capture(new("shared.settings", "1", shape, "{\"Child\":null,\"Items\":[]}")
        {
            ObjectDefaults = [new("/members/Child", "{\"Grand\":{\"Left\":30,\"Right\":40}}"),
                new("/members/Child/members/Grand", "{\"Left\":1,\"Right\":2}"),
                new("/members/Items/element", "{\"Grand\":{\"Left\":30,\"Right\":40}}"),
                new("/members/Items/element/members/Grand", "{\"Left\":1,\"Right\":2}")]
        }).Manifest.Definitions.Single();
        const string payload = "{\"Child\":{\"Grand\":{\"Right\":9}},\"Items\":[{\"Grand\":{\"Right\":8}}]}";
        var source = new ManagementConfigurationSourceContent("present", PayloadJson: payload);
        JsonNode effective = ManagementConfigurationComposition.Compose(Binding(schema), new(new("absent"), source));
        ConfigurationCatalog catalog = ConfigurationCatalog.Create([typeof(NestedDeclaredSettings)], new ConfigurationOptions("FirstHost"));
        ConfigurationEntry entry = catalog.Entries.Single();
        var pipeline = new ConfigurationValuePipeline();
        using JsonDocument document = JsonDocument.Parse(payload);
        Assert.IsFalse(pipeline.Apply(entry, pipeline.Prepare(entry, null, document.RootElement), CancellationToken.None, static () => false, initial: true));
        var root = (NestedDeclaredSettings)entry.Instance;
        Assert.IsNotNull(root.Child);
        Assert.AreEqual((30, 9), (root.Child.Grand.Left, root.Child.Grand.Right));
        Assert.AreEqual((1, 8), (root.Items.Single().Grand.Left, root.Items.Single().Grand.Right));
        Assert.AreEqual(root.Child.Grand.Left, effective["Child"]!["Grand"]!["Left"]!.GetValue<int>());
        Assert.AreEqual(root.Items.Single().Grand.Left, effective["Items"]![0]!["Grand"]!["Left"]!.GetValue<int>());
    }

    private static ManagementConfigurationOptions Nested(string baseline)
    {
        var child = new ManagementConfigurationShape("object", false,
            [new("Left", true, new("integer", false)), new("Right", true, new("integer", false))]);
        var shape = new ManagementConfigurationShape("object", false,
            [new("Child", true, child with { Nullable = true }), new("Items", true, new("array", false, Element: child)),
                new("Scores", true, new("dictionary", false, Element: child))]);
        string defaults = "{\"Child\":" + (baseline == "null" ? "null" : "{\"Left\":30,\"Right\":40}")
            + ",\"Items\":[{\"Left\":60,\"Right\":70}],\"Scores\":{\"initial\":{\"Left\":80,\"Right\":90}}}";
        return Capture(new("shared.settings", "1", shape, defaults)
        {
            ObjectDefaults = [new("/members/Scores/element", "{\"Left\":1,\"Right\":2}"),
                new("/members/Child", "{\"Left\":1,\"Right\":2}"), new("/members/Items/element", "{\"Left\":1,\"Right\":2}")]
        });
    }

    private static ManagementConfigurationOptions Capture(ManagementConfigurationSchema schema)
    {
        ManagementConfigurationManifest original = Deployment().Manifest;
        return new(JsonSerializer.Serialize(original with
        {
            Definitions = [schema]
        }, ManagementConfigurationOptions.Json), 32L * 1024 * 1024);
    }

    private static ManagementConfigurationBindingResource Binding(ManagementConfigurationSchema schema)
        => new("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "managed-a", "Development",
            schema.ConfigurationId, schema.SchemaVersion, "FirstHost", schema.Shape, schema.DefaultsJson, true, null, null)
        {
            ObjectDefaults = schema.ObjectDefaults
        };

    public sealed class DeclaredDefaultsSettings : ConfigurationObject
    {
        public DeclaredChild? Child { get; set; } = new() { Left = 30, Right = 40 };
        public List<DeclaredChild> Items { get; set; } = [new() { Left = 60, Right = 70 }];
        public Dictionary<string, DeclaredChild> Scores { get; set; } = new(StringComparer.Ordinal) { ["initial"] = new() { Left = 80, Right = 90 } };
    }

    public sealed class DeclaredChild
    {
        public int Left { get; set; } = 1;
        public int Right { get; set; } = 2;
    }

    public sealed class NestedDeclaredSettings : ConfigurationObject
    {
        public NestedDeclaredChild? Child
        {
            get; set;
        }
        public List<NestedDeclaredChild> Items { get; set; } = [];
    }

    public sealed class NestedDeclaredChild
    {
        public DeclaredChild Grand { get; set; } = new() { Left = 30, Right = 40 };
    }
}
