using System.Net;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Security;
using CIS.Management.Wire;
using CIS.RuleEngine;
using CIS.Security;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed partial class ManagementRuleEngineVersionTests
{
    private const string RuleConfigurationId = "rm11.local";
    private const string OtherRuleConfigurationId = "rm11.other";
    private const string OrdinaryConfigurationId = "normal.settings";
    private const string RulesetId = "éligibilité/🌍";
    private const string OtherRulesetId = "🌐/other";
    private const string SchemaRevision = "schema-1";
    private const string VersionReason = "Reviewed rule version · preserve <labels> & notes";
    private const string SaveOperation = "management.rule-engine.version.save";
    private const string ReadCapability = "management.rule-engine.read";
    private const string SaveCapability = "management.rule-engine.save";
    private const string OperationReadCapability = "management.rule-engine.operations.read";
    private const string ApplicationResource = "management.application";

    [TestMethod]
    public async Task CompetingEditorsAdvanceOneRevisionAndRecoverEachOriginalDecisionAcrossReplicas()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(start: false, configuration: RuleDeployment());
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: true));
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: true));
        HttpClient first = fixture.Client("operator-a");
        HttpClient second = fixture.Client("operator-b", 1);
        await Apply(first, await Catalog(first), "apply-rules");

        string application = fixture.ApplicationA;
        string firstPagePath = $"applications/{application}/rule-engine/rulesets?pageSize=1";
        JsonElement firstPage = await Get(first, firstPagePath);
        Assert.AreEqual(RulesetId, ReadText(firstPage.GetProperty("items")[0], "rulesetId"));
        string secondPagePath = firstPagePath + "&cursor=" + Uri.EscapeDataString(ReadText(firstPage, "nextCursor"));
        JsonElement secondPage = await Get(first, secondPagePath);
        Assert.AreEqual(OtherRulesetId, ReadText(secondPage.GetProperty("items")[0], "rulesetId"));
        using (HttpResponseMessage otherActorPage = await second.GetAsync(secondPagePath))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, otherActorPage.StatusCode);
        }
        string revision = ReadText(await OnlyRuleset(first, application), "editETag");
        string firstPayload = DefinitionJson([Row("first-rule", 10, "MLT")]);
        string secondPayload = DefinitionJson([Row("second-rule", 10, "ITA")]);
        long originalBefore = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long auditBefore = await Count(fixture, fixture.Options.MessagingSchema, "outbox");

        Task<HttpResponseMessage> pendingFirst = Save(first, application, "first-save", revision, firstPayload);
        Task<HttpResponseMessage> pendingSecond = Save(second, application, "second-save", revision, secondPayload);
        using HttpResponseMessage firstResponse = await pendingFirst;
        using HttpResponseMessage secondResponse = await pendingSecond;
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed },
            new[] { firstResponse.StatusCode, secondResponse.StatusCode });
        Assert.AreEqual(originalBefore + 2, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(auditBefore + 2, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        Assert.AreNotEqual(revision, ReadText(await OnlyRuleset(first, application), "editETag"));

        JsonElement versions = await Get(first, RuleRoute(application, "versions"));
        Assert.AreEqual(1, versions.GetProperty("items").GetArrayLength());
        JsonElement versionSummary = versions.GetProperty("items")[0];
        Assert.AreEqual(VersionReason, ReadText(versionSummary, "versionNote"));
        string savedVersion = ReadText(versionSummary, "version");
        JsonElement savedVersionDetail = await Get(first, RuleRoute(application, "version") + "&version=" + savedVersion);
        Assert.AreEqual(VersionReason, ReadText(savedVersionDetail, "versionNote"));
        foreach ((HttpClient actor, string key, string payload, HttpResponseMessage response) in new[]
        {
            (first, "first-save", firstPayload, firstResponse),
            (second, "second-save", secondPayload, secondResponse)
        })
        {
            JsonElement prepared = SaveEnvelope(await Read(response), response.IsSuccessStatusCode);
            AssertOriginal(prepared, key, "prepared");
            JsonElement lookedUp = await Get(actor, OperationRoute(application, key));
            AssertOriginal(lookedUp, key, "found");
            string outcome = response.IsSuccessStatusCode ? "result" : "rejection";
            Assert.AreEqual(prepared.GetProperty(outcome).GetRawText(), lookedUp.GetProperty(outcome).GetRawText());

            using HttpResponseMessage replay = await Save(actor, application, key, revision, payload);
            Assert.AreEqual(response.StatusCode, replay.StatusCode);
            JsonElement replayed = SaveEnvelope(await Read(replay), replay.IsSuccessStatusCode);
            AssertOriginal(replayed, key, "replayed");
            Assert.AreEqual(prepared.GetProperty(outcome).GetRawText(), replayed.GetProperty(outcome).GetRawText());
        }

        HttpClient winner = firstResponse.IsSuccessStatusCode ? first : second;
        string winningKey = firstResponse.IsSuccessStatusCode ? "first-save" : "second-save";
        string changedPayload = firstResponse.IsSuccessStatusCode ? secondPayload : firstPayload;
        using HttpResponseMessage conflictingKey = await Save(winner, application, winningKey, revision, changedPayload);
        Assert.AreEqual(HttpStatusCode.Conflict, conflictingKey.StatusCode);

        HttpClient otherActor = firstResponse.IsSuccessStatusCode ? second : first;
        using HttpResponseMessage foreign = await otherActor.GetAsync(OperationRoute(application, winningKey));
        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.AreEqual(originalBefore + 2, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(auditBefore + 2, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    [TestMethod]
    public async Task SavedDefinitionsAndStableRuleIdsSurviveRestartWhileGenericPublicationCannotBypassRules()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false, configuration: RuleDeployment());
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: false));
        HttpClient actor = fixture.Client("operator-a");
        HttpClient readOnly = fixture.Client("operator-b");
        await Apply(actor, await Catalog(actor), "apply-rules");

        string application = fixture.ApplicationA;
        JsonElement trusted = await Get(readOnly, RuleRoute(application, "schema") + "&schemaRevision=" + SchemaRevision);
        Assert.AreEqual(application, ReadText(trusted, "applicationId"));
        Assert.AreEqual("Development", ReadText(trusted, "environment"));
        Assert.AreEqual(RulesetId, ReadText(trusted, "rulesetId"));
        Assert.AreEqual(SchemaRevision, ReadText(trusted, "schemaRevision"));
        using HttpResponseMessage foreign = await readOnly.GetAsync($"applications/{fixture.ApplicationB}/rule-engine/rulesets");
        Assert.IsTrue(foreign.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        using (JsonDocument schema = JsonDocument.Parse(ReadText(trusted, "schemaJson")))
        {
            JsonElement column = schema.RootElement.GetProperty("columns")[0];
            Assert.AreEqual("country", ReadText(column, "id"));
            Assert.AreEqual(3, column.GetProperty("exactScalarCount").GetInt32());
            Assert.AreEqual(0, schema.RootElement.GetProperty("rows").GetArrayLength());
        }

        ManagementConfigurationAssignmentMap before = await Assignment(actor, application);
        string firstRevision = ReadText(await OnlyRuleset(actor, application), "editETag");
        string originalDefinition = DefinitionJson([Row("keep", 10, "MLT"), Row("remove", 20, "ITA")]);

        using HttpResponseMessage unauthorized = await Save(readOnly, application, "forbidden", firstRevision, originalDefinition);
        Assert.IsTrue(unauthorized.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        long auditBefore = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        JsonElement first = await SaveAccepted(actor, application, "initial", firstRevision, originalDefinition);
        string firstVersion = ReadText(first, "version");
        string firstBytes = ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + firstVersion), "definitionJson");
        CollectionAssert.AreEqual(new[] { "keep", "remove" }, RuleIds(firstBytes, firstVersion));

        string edit = DefinitionJson([Row("keep", 10, "ITA"), Row("copy-new-id", 20, "ITA")]);
        JsonElement second = await SaveAccepted(actor, application, "edited", ReadText(first, "editETag"), edit, firstVersion);
        string secondVersion = ReadText(second, "version");
        Assert.AreNotEqual(firstVersion, secondVersion);
        string secondBytes = ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + secondVersion), "definitionJson");
        CollectionAssert.AreEqual(new[] { "keep", "copy-new-id" }, RuleIds(secondBytes, secondVersion));
        Assert.AreEqual(firstBytes, ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + firstVersion), "definitionJson"));
        Assert.AreEqual(auditBefore + 2, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));

        string currentRevision = ReadText(await OnlyRuleset(actor, application), "editETag");
        JsonElement unchanged = await SaveAccepted(actor, application, "unchanged", currentRevision, secondBytes, secondVersion);
        string resavedVersion = ReadText(unchanged, "version");
        string settledRevision = ReadText(unchanged, "editETag");
        Assert.AreNotEqual(secondVersion, resavedVersion);
        Assert.AreNotEqual(currentRevision, settledRevision);
        string resavedBytes = ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + resavedVersion), "definitionJson");
        Assert.AreEqual(secondBytes.Replace(secondVersion, resavedVersion, StringComparison.Ordinal), resavedBytes);
        Assert.AreEqual(settledRevision, ReadText(await OnlyRuleset(actor, application), "editETag"));

        await fixture.StopReplicasAsync();
        int restarted = fixture.Origins.Count;
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: false));
        HttpClient newActor = fixture.Client("operator-a", restarted);
        await Register(fixture.Client("workload-a", restarted));
        Assert.AreEqual(settledRevision, ReadText(await OnlyRuleset(newActor, application), "editETag"));
        Assert.AreEqual(firstBytes, ReadText(await Get(newActor, RuleRoute(application, "version") + "&version=" + firstVersion), "definitionJson"));
        Assert.AreEqual(secondBytes, ReadText(await Get(newActor, RuleRoute(application, "version") + "&version=" + secondVersion), "definitionJson"));
        JsonElement restartedOriginal = await Get(newActor, OperationRoute(application, "initial"));
        AssertOriginal(restartedOriginal, "initial", "found");
        Assert.AreEqual(firstVersion, ReadText(restartedOriginal.GetProperty("result"), "version"));
        using (HttpResponseMessage replay = await Save(newActor, application, "initial", firstRevision, originalDefinition))
        {
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode, await replay.Content.ReadAsStringAsync());
            JsonElement recovered = await Read(replay);
            AssertOriginal(recovered, "initial", "replayed");
            Assert.AreEqual(firstVersion, ReadText(recovered.GetProperty("result"), "version"));
        }

        ManagementConfigurationDefinition[] sources = (await Definitions(newActor)).Items
            .Where(item => item.ApplicationId == application).ToArray();
        ManagementConfigurationSource managed = await Source(newActor, sources.Single(item => item.ConfigurationId == RuleConfigurationId).SourceId);
        ManagementConfigurationSource ordinary = await Source(newActor, sources.Single(item => item.ConfigurationId == OrdinaryConfigurationId).SourceId);
        long publications = await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication");
        string validCarrier = JsonSerializer.Serialize(new
        {
            definitionChunks = new[] { edit }
        });
        using (HttpResponseMessage bypass = await Publish(newActor, managed, "generic-bypass", validCarrier))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, bypass.StatusCode, await bypass.Content.ReadAsStringAsync());
            Assert.AreEqual("management.configuration.rule_engine_managed", (await Decode<ManagementConfigurationProblem>(bypass)).Code);
        }
        Assert.AreEqual(publications, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        Assert.AreEqual(managed.ETag, (await Source(newActor, managed.SourceId)).ETag);
        using (HttpResponseMessage unaffected = await Publish(newActor, ordinary, "ordinary-publish", """{"Greeting":"updated"}"""))
        {
            Assert.AreEqual(HttpStatusCode.OK, unaffected.StatusCode, await unaffected.Content.ReadAsStringAsync());
        }
        Assert.AreEqual(publications + 1, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        JsonElement versions = await Get(newActor, RuleRoute(application, "versions"));
        CollectionAssert.AreEquivalent(new[] { firstVersion, secondVersion, resavedVersion },
            versions.GetProperty("items").EnumerateArray().Select(item => ReadText(item, "version")).ToArray());

        ManagementConfigurationAssignmentMap after = await Assignment(newActor, application);
        Assert.AreEqual(before.ETag, after.ETag);
        Assert.AreEqual(before.Generation, after.Generation);
        Assert.AreEqual(before.DefaultReleaseId, after.DefaultReleaseId);
    }

    private static ManagementConfigurationOptions RuleDeployment(int retainedUnreferencedReleases = 10, string hostScope = "FirstHost",
        string primarySchemaRevision = SchemaRevision)
    {
        ManagementConfigurationShape payload = new("object", false,
            [new("definitionChunks", true, new("array", false, Element: new("string", false)))]);
        ManagementConfigurationShape ordinary = new("object", false, [new("Greeting", true, new("string", false))]);
        var manifest = new ManagementConfigurationManifest(1,
            [new(RuleConfigurationId, "1", payload, """{"definitionChunks":[]}"""),
                new(OtherRuleConfigurationId, "1", payload, """{"definitionChunks":[]}"""),
                new(OrdinaryConfigurationId, "1", ordinary, """{"Greeting":"default"}""")],
            [new("managed-a", "Development", hostScope, RuleConfigurationId, "1"),
                new("managed-a", "Development", hostScope, OtherRuleConfigurationId, "1"),
                new("managed-a", "Development", hostScope, OrdinaryConfigurationId, "1")], [], [])
        {
            RuleEngine = [new("managed-a", "Development", RuleConfigurationId, DefinitionJson([], schemaRevision: primarySchemaRevision)),
                new("managed-a", "Development", OtherRuleConfigurationId, DefinitionJson([], OtherRulesetId))]
        };
        return new(JsonSerializer.Serialize(manifest, ManagementConfigurationOptions.Json), 32L * 1024 * 1024,
            retainedUnreferencedReleases);
    }

    private static ManagementSecurityOptions WithRuleGrants(ManagementSecurityOptions original, string applicationId, bool secondEditor,
        bool deployment = false, bool executionHistory = false, bool counters = false)
    {
        CapabilityGrant Grant(string actor, string capability) => new(original.Profile, capability,
            EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName, subject: new(ManagementHostFixture.Issuer, actor),
            resourceSelection: CapabilityResourceSelection.ExactResource, resourceType: ApplicationResource, resourceIdentifier: applicationId);

        List<CapabilityGrant> additional =
        [
            Grant("operator-a", ReadCapability), Grant("operator-a", SaveCapability), Grant("operator-a", OperationReadCapability),
            Grant("operator-b", ReadCapability)
        ];
        if (secondEditor)
        {
            additional.Add(Grant("operator-b", SaveCapability));
            additional.Add(Grant("operator-b", OperationReadCapability));
        }
        if (deployment)
        {
            additional.Add(Grant("operator-a", DeploymentReadCapability));
            additional.Add(Grant("operator-a", DeployCapability));
            additional.Add(Grant("operator-b", DeploymentReadCapability));
            if (secondEditor)
            {
                additional.Add(Grant("operator-b", DeployCapability));
            }
        }
        if (executionHistory)
        {
            additional.Add(Grant("operator-a", "management.rule-engine.executions.read"));
            additional.Add(Grant("operator-a", "management.rule-engine.executions.detail.read"));
            additional.Add(Grant("operator-b", "management.rule-engine.executions.read"));
        }
        if (counters)
        {
            additional.Add(Grant("operator-a", "management.rule-engine.counters.read"));
            additional.Add(Grant("operator-a", "management.rule-engine.counters.reset"));
            additional.Add(Grant("operator-b", "management.rule-engine.counters.read"));
        }

        return new()
        {
            Mode = original.Mode,
            Profile = original.Profile,
            AuthenticationScheme = original.AuthenticationScheme,
            BusinessAddress = original.BusinessAddress,
            BusinessPort = original.BusinessPort,
            OperationalPort = original.OperationalPort,
            ServerCertificateFile = original.ServerCertificateFile,
            ServerPrivateKeyFile = original.ServerPrivateKeyFile,
            ServerTrustBundleFile = original.ServerTrustBundleFile,
            CertificateRevocation = original.CertificateRevocation,
            BorrowedNativeKeyScope = original.BorrowedNativeKeyScope,
            IdentityMappings = original.IdentityMappings,
            Grants = [.. original.Grants, .. additional],
            NativeProvider = original.NativeProvider
        };
    }

    private static RuleRow Row(string id, int ordinal, string country)
        => new(id, ordinal, RuleState.Enabled,
            [new("country", RulePresetKind.String, RuleOperator.Equals, RuleValue.FromString(country))], "9007199254740993.000000000001");

    private static string DefinitionJson(IEnumerable<RuleRow> rows, string rulesetId = RulesetId, string schemaRevision = SchemaRevision)
    {
        RuleColumn country = new("country", "Residence country", RuleValueKind.String, [RulePresetKind.String], exactScalarCount: 3);
        RuleDefinition definition = new(rulesetId, schemaRevision, "unsaved", RuleExecutionMode.FirstMatch, [country], rows, "Eligibility");
        return Encoding.UTF8.GetString(RuleDefinitionWire.Encode(definition).Span);
    }

    private static string[] RuleIds(string definition, string expectedVersion)
    {
        using JsonDocument document = JsonDocument.Parse(definition);
        Assert.AreEqual(expectedVersion, ReadText(document.RootElement, "version"));
        JsonElement[] rows = document.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        foreach (JsonElement row in rows)
        {
            Assert.AreEqual("9007199254740993.000000000001", ReadText(row, "weight"));
        }
        return rows.Select(row => ReadText(row, "ruleId")).ToArray();
    }

    private static string RuleRoute(string application, string route)
        => $"applications/{application}/rule-engine/{route}?rulesetId={Uri.EscapeDataString(RulesetId)}";

    private static string OperationRoute(string application, string key) => RuleRoute(application, "version-operations/" + key);

    private static async Task<ManagementConfigurationAssignmentMap> Assignment(HttpClient client, string application)
    {
        using HttpResponseMessage response = await client.GetAsync($"applications/{application}/configuration/{RuleConfigurationId}/assignments?schemaVersion=1");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationAssignmentMap>(response);
    }

    private static async Task<JsonElement> OnlyRuleset(HttpClient client, string application)
    {
        JsonElement list = await Get(client, $"applications/{application}/rule-engine/rulesets");
        return list.GetProperty("items").EnumerateArray().Single(item => ReadText(item, "rulesetId") == RulesetId);
    }

    private static async Task<JsonElement> SaveAccepted(HttpClient client, string application, string key, string revision,
        string definitionJson, string? baseVersion = null, string schemaRevision = SchemaRevision)
    {
        using HttpResponseMessage response = await Save(client, application, key, revision, definitionJson, baseVersion, schemaRevision);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        JsonElement result = await Read(response);
        AssertOriginal(result, key, "prepared");
        return result.GetProperty("result");
    }

    private static async Task<HttpResponseMessage> Save(HttpClient client, string application, string key, string revision,
        string definitionJson, string? baseVersion = null, string schemaRevision = SchemaRevision)
    {
        const string reason = VersionReason;
        object command = baseVersion is null
            ? new
            {
                schemaRevision,
                definitionJson,
                reason
            }
            : new
            {
                schemaRevision,
                definitionJson,
                reason,
                baseVersion
            };
        using var request = new HttpRequestMessage(HttpMethod.Post, RuleRoute(application, "versions"));
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", revision);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(command));
        request.Content.Headers.ContentType = new("application/json");
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Read(response);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return document.RootElement.Clone();
    }

    private static JsonElement SaveEnvelope(JsonElement response, bool success)
        => success ? response : response.GetProperty("result");

    private static string ReadText(JsonElement value, string property) => value.GetProperty(property).GetString()!;

    private static void AssertOriginal(JsonElement result, string key, string source)
    {
        Assert.AreEqual(SaveOperation, ReadText(result.GetProperty("operation"), "name"));
        Assert.AreEqual(key, ReadText(result.GetProperty("operation"), "key"));
        Assert.AreEqual(source, ReadText(result, "resultSource"));
        Assert.AreEqual("committed", ReadText(result, "originalCompletion"));
    }

    private static async Task<string> Register(HttpClient client)
    {
        string registration = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + registration)
        {
            Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request("After restart")))
        };
        request.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return registration;
    }
}
