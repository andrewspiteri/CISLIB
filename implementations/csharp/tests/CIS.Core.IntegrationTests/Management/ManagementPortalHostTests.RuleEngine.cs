using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Security;
using CIS.Management.Wire;
using CIS.RuleEngine;
using CIS.Security;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    private const string PortalRuleConfiguration = "rm11.portal";
    private const string PortalRuleset = "country-eligibility";
    private const string PortalRuleSchema = "schema-1";
    private const string PortalUpdatedRuleSchema = "schema-2";
    private const string PortalAddedRuleColumn = "resident";
    private const string PortalAddedRuleColumnName = "Resident";
    private const string PortalRulesetName = "Country eligibility";
    private const string PortalExactDecimal = "9007199254740993.000000000001";
    private const string PortalExactMetadata = """{"balance":9007199254740993,"rate":1.2300e5}""";

    [TestMethod]
    public async Task ActualRuleGridKeepsImmutableHistoryAndDeploysOnlyAnExplicitAuthorizedVersion()
    {
        await RunFixtureAsync(browser: true, ruleEngineWorkflow: true);
    }

    [TestMethod]
    public async Task ActualRuleGridCanSaveFirstVersionOfUpdatedSchema()
    {
        await RunFixtureAsync(browser: true, ruleEngineWorkflow: true, ruleEngineSchemaUpdate: true);
    }

    private sealed record RulePortalSeed(string FirstVersion, string SecondVersion, string ReadOnlyVersion, string FirstDefinition,
        string SecondDefinition, string HistoricalVersion, string RejectedInstance, string LaggingInstance);

    private static ManagementConfigurationManifest WithPortalRules(ManagementConfigurationManifest manifest)
    {
        ManagementConfigurationShape carrier = new("object", false,
            [new("definitionChunks", true, new("array", false, Element: new("string", false)))]);
        const string environment = "Development";
        string schema = PortalRuleDefinition([]);
        return manifest with
        {
            Definitions = [.. manifest.Definitions, new(PortalRuleConfiguration, "1", carrier, """{"definitionChunks":[]}""")],
            Bindings = [.. manifest.Bindings,
                new("managed-a", environment, "FirstHost", PortalRuleConfiguration, "1"),
                new("managed-b", environment, "SecondHost", PortalRuleConfiguration, "1")],
            RuleEngine = [new("managed-a", environment, PortalRuleConfiguration, schema), new("managed-b", environment, PortalRuleConfiguration, schema)]
        };
    }

    private static string PortalRuleDefinition(RuleRow[] rows, RuleExecutionMode mode = RuleExecutionMode.FirstMatch)
    {
        RuleColumn[] columns =
        [
            new("residence", "Residence country", RuleValueKind.String, [RulePresetKind.String, RulePresetKind.StringList], exactScalarCount: 3),
            new("pensioner", "Pensioner", RuleValueKind.Boolean, [RulePresetKind.Boolean]),
            new("income", "Monthly income", RuleValueKind.Decimal, [RulePresetKind.Decimal]),
            new("referral", "Referral", RuleValueKind.String, [RulePresetKind.String])
        ];
        return Encoding.UTF8.GetString(RuleDefinitionCodec.Encode(new(PortalRuleset, PortalRuleSchema, "draft",
            mode, columns, rows, PortalRulesetName)));
    }

    private static ManagementServiceOptions PortalRuleSchemaUpdateOptions(ManagementServiceOptions original)
    {
        ManagementConfigurationOptions currentOptions = original.Configuration!;
        ManagementConfigurationManifest current = currentOptions.Manifest;
        RuleDefinition previous = RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(PortalRuleDefinition([])));
        var updated = new RuleDefinition(PortalRuleset, PortalUpdatedRuleSchema, "draft", previous.Mode,
            [.. previous.Columns, new(PortalAddedRuleColumn, PortalAddedRuleColumnName, RuleValueKind.Boolean, [RulePresetKind.Boolean])],
            [], PortalRulesetName);
        string schemaJson = Encoding.UTF8.GetString(RuleDefinitionCodec.Encode(updated));
        ManagementConfigurationManifest manifest = current with
        {
            RuleEngine = current.RuleEngine!.Select(registration => registration.Application == "managed-a"
                ? registration with
                {
                    SchemaJson = schemaJson
                }
                : registration).ToArray()
        };
        var configuration = new ManagementConfigurationOptions(JsonSerializer.Serialize(manifest, ManagementConfigurationOptions.Json),
            currentOptions.QuotaBytes, currentOptions.RetainedUnreferencedReleases, currentOptions.PinnedReleaseIds);
        return new(original.ConnectionString, original.CopyCursorSigningKey(), original.Persistence, original.BasePath,
            original.MaximumConcurrentRequests, original.OperationsSchema, original.MessagingSchema, original.Delivery,
            original.Backend, configuration);
    }

    private static RuleRow[] PortalRuleRows(string country) =>
    [
        new("eligibility-primary", 10, RuleState.Enabled,
            [new("residence", RulePresetKind.String, RuleOperator.Equals, RuleValue.FromString(country)),
                new("pensioner", RulePresetKind.Boolean, RuleOperator.Equals, RuleValue.FromBoolean(true)),
                new("income", RulePresetKind.Decimal, RuleOperator.GreaterThanOrEqual, RuleValue.FromDecimal("1200.50")),
                new("referral", RulePresetKind.String, RuleOperator.Equals, RuleValue.FromString("direct"))], weight: 1.25m),
        new("eligibility-secondary", 20, RuleState.Test,
            [new("residence", RulePresetKind.StringList, RuleOperator.IsIn, RuleValue.FromStringList(["MLT", "ITA"])),
                new("pensioner", RulePresetKind.Any), new("income", RulePresetKind.Any), new("referral", RulePresetKind.Missing)], weight: 0.5m)
    ];

    private static IEnumerable<CapabilityGrant> PortalRuleGrants(string issuer, string subject, string app, bool write)
    {
        yield return MetadataGrant(issuer, subject, app, "management.rule-engine.read");
        yield return MetadataGrant(issuer, subject, app, "management.rule-engine.deployment.read");
        if (write)
        {
            yield return MetadataGrant(issuer, subject, app, "management.rule-engine.save");
            yield return MetadataGrant(issuer, subject, app, "management.rule-engine.operations.read");
            yield return MetadataGrant(issuer, subject, app, "management.rule-engine.deploy");
        }
    }

    private static ManagementSecurityOptions WithPortalRuleSeeder(ManagementSecurityOptions source, ManagementHostFixture fixture, bool inspection)
        => new()
        {
            Mode = source.Mode,
            Profile = source.Profile,
            AuthenticationScheme = source.AuthenticationScheme,
            BusinessAddress = source.BusinessAddress,
            BusinessPort = source.BusinessPort,
            OperationalPort = source.OperationalPort,
            ServerCertificateFile = source.ServerCertificateFile,
            ServerPrivateKeyFile = source.ServerPrivateKeyFile,
            ServerTrustBundleFile = source.ServerTrustBundleFile,
            CertificateRevocation = source.CertificateRevocation,
            BorrowedNativeKeyScope = source.BorrowedNativeKeyScope,
            IdentityMappings = source.IdentityMappings,
            Grants =
            [
                .. source.Grants,
                .. PortalRuleGrants(ManagementHostFixture.Issuer, "operator-a", fixture.ApplicationA, write: true),
                .. PortalRuleGrants(ManagementHostFixture.Issuer, "operator-a", fixture.ApplicationB, write: true),
                .. inspection ? PortalRuleInspectionGrants(ManagementHostFixture.Issuer, "operator-a", fixture.ApplicationA, write: true)
                    : Array.Empty<CapabilityGrant>()
            ],
            NativeProvider = source.NativeProvider
        };

    private static async Task<RulePortalSeed> SeedPortalRulesAsync(ManagementHostFixture fixture, HttpClient actor, bool inspection)
    {
        string application = fixture.ApplicationA;
        string historical = await SavePortalRule(actor, application, "Historical eligibility", "ITA",
            definition: inspection ? PortalHistoricalInspectionDefinition() : null);
        var earlier = await DeployPortalRule(actor, application, historical);
        string first = await SavePortalRule(actor, application, "Initial eligibility", "MLT", historical);
        var initial = await DeployPortalRule(actor, application, first);
        string second = await SavePortalRule(actor, application, "Expanded trial", "FRA", first);
        string viewer = await SavePortalRule(actor, fixture.ApplicationB, "Read-only eligibility", "ITA");
        HttpClient workload = fixture.Client("workload-a");
        const string rejected = "rule-older-build";
        const string lagging = "rule-lagging-build";
        await ReportPortalRule(workload, "rule-applied-build", "Applied", initial, initial);
        await ReportPortalRule(workload, rejected, "Rejected", initial, earlier);
        await ReportPortalRule(workload, lagging, "Applied", earlier, earlier);

        string InitialDefinition(string version) => PortalRulePath(application, "version") + "&version=" + version;
        return new(first, second, viewer,
            (await ReadRuleFixtureJson(actor, InitialDefinition(first))).GetProperty("definitionJson").GetString()!,
            (await ReadRuleFixtureJson(actor, InitialDefinition(second))).GetProperty("definitionJson").GetString()!,
            historical, rejected, lagging);
    }

    private static async Task<string> SavePortalRule(HttpClient actor, string application, string note, string country, string? baseVersion = null,
        string? definition = null)
    {
        JsonElement rulesets = await ReadRuleFixtureJson(actor, $"applications/{application}/rule-engine/rulesets");
        string edit = rulesets.GetProperty("items").EnumerateArray().Single().GetProperty("editETag").GetString()!;
        JsonElement result = await WriteRuleFixtureJson(actor, PortalRulePath(application, "versions"), edit,
            ManagementRuleEngineWire.EncodeRequest(new ManagementSaveRuleVersionCommand(PortalRuleSchema,
                definition ?? PortalRuleDefinition(PortalRuleRows(country)), note, baseVersion)));
        return result.GetProperty("result").GetProperty("version").GetString()!;
    }

    private static async Task<ManagementConfigurationVersion> DeployPortalRule(HttpClient actor, string application, string version)
    {
        JsonElement current = await ReadRuleFixtureJson(actor, PortalRulePath(application, "deployment"));
        JsonElement response = await WriteRuleFixtureJson(actor, PortalRulePath(application, "deployments"),
            current.GetProperty("assignmentETag").GetString()!,
            ManagementRuleEngineWire.EncodeRequest(new ManagementRequestRuleDeploymentCommand(version, "deploy", "Seed actual requested version")));
        JsonElement desired = response.GetProperty("result").GetProperty("desired");
        return new(desired.GetProperty("releaseId").GetString()!, desired.GetProperty("generation").GetString()!);
    }

    private static async Task ReportPortalRule(HttpClient workload, string instance, string state,
        ManagementConfigurationVersion desired, ManagementConfigurationVersion accepted)
    {
        string registration = Guid.NewGuid().ToString("D");
        string observed = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        using HttpResponseMessage enrolled = await ManagementServiceTests.Put(workload, "registrations/" + registration,
            new ManagementRegistrationRequest(1, instance, observed, "3.1.0"));
        Assert.AreEqual(HttpStatusCode.OK, enrolled.StatusCode, await enrolled.Content.ReadAsStringAsync());
        var applied = new ManagementConfigurationAppliedVersion(accepted.ReleaseId, accepted.Generation, "0");
        var row = new ManagementConfigurationStatusEntry(PortalRuleConfiguration, "1", "HotReload", state, true,
            desired, applied, applied, state == "Rejected" ? "Configuration.Validation" : null);
        using var report = new HttpRequestMessage(HttpMethod.Put, "registrations/" + registration + "/configuration-status")
        {
            Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationStatusReport("1", observed, [row])))
        };
        report.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage response = await workload.SendAsync(report);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string PortalRulePath(string app, string operation)
        => $"applications/{app}/rule-engine/{operation}?rulesetId={Uri.EscapeDataString(PortalRuleset)}";

    private static async Task<JsonElement> ReadRuleFixtureJson(HttpClient actor, string route)
    {
        using HttpResponseMessage response = await actor.GetAsync(route);
        return await RuleFixtureJson(response);
    }

    private static async Task<JsonElement> WriteRuleFixtureJson(HttpClient actor, string route, string etag, byte[] body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = new ByteArrayContent(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage response = await actor.SendAsync(request);
        return await RuleFixtureJson(response);
    }

    private static async Task<JsonElement> RuleFixtureJson(HttpResponseMessage response)
    {
        byte[] content = await response.Content.ReadAsByteArrayAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, Encoding.UTF8.GetString(content));
        using JsonDocument result = JsonDocument.Parse(content);
        return result.RootElement.Clone();
    }

    private static async Task VerifyPortalRuleWorkflowAsync(ManagementHostFixture fixture, RulePortalSeed seed, JsonElement proof)
    {
        string saved = proof.GetProperty("saveVersion").GetString()!;
        string deployed = proof.GetProperty("deployedVersion").GetString()!;
        string rollback = proof.GetProperty("rollbackVersion").GetString()!;
        Assert.AreEqual(seed.FirstVersion, proof.GetProperty("olderBase").GetString());
        Assert.AreEqual(saved, deployed);
        Assert.AreEqual(seed.FirstVersion, rollback);
        Assert.AreEqual(PortalExactDecimal, proof.GetProperty("exactDecimal").GetString());
        Assert.AreEqual(PortalExactMetadata, proof.GetProperty("metadata").GetString());
        Assert.IsTrue(proof.GetProperty("appliedObservation").GetBoolean());
        Assert.IsTrue(proof.GetProperty("pendingObservation").GetBoolean());
        Assert.IsTrue(proof.GetProperty("rejectedObservation").GetBoolean());

        Dictionary<string, byte[]> definitions = await PortalRuleVersions(fixture, fixture.ApplicationA);
        Assert.AreEqual(seed.FirstDefinition, Encoding.UTF8.GetString(definitions[seed.FirstVersion]));
        Assert.AreEqual(seed.SecondDefinition, Encoding.UTF8.GetString(definitions[seed.SecondVersion]));
        Assert.AreNotEqual(seed.FirstVersion, saved);
        Assert.AreNotEqual(seed.SecondVersion, saved);
        RuleDefinition savedDefinition = RuleDefinitionCodec.Decode(definitions[saved]);
        Assert.AreEqual(RuleExecutionMode.AllMatching, savedDefinition.Mode);
        Assert.HasCount(3, savedDefinition.Rows);
        RuleRow edited = savedDefinition.Rows.Single(row => row.RuleId == "eligibility-primary");
        Assert.AreEqual(RuleState.Test, edited.State);
        Assert.AreEqual("0.6500", edited.WeightText);
        Assert.AreEqual("Portal older draft row", edited.Comment);
        Assert.AreEqual(PortalExactMetadata, edited.Metadata?.GetRawText());
        Assert.AreEqual(PortalExactDecimal, edited.Conditions.Single(cell => cell.ColumnId == "income").Preset?.DecimalText);
        Assert.AreEqual(false, edited.Conditions.Single(cell => cell.ColumnId == "pensioner").Preset?.BooleanValue);
        Assert.AreEqual(RulePresetKind.Missing, edited.Conditions.Single(cell => cell.ColumnId == "referral").PresetKind);
        CollectionAssert.AreEqual(new[] { "MLT", "ITA" }, edited.Conditions.Single(cell => cell.ColumnId == "residence").Preset!.StringListValue.ToArray());
        Assert.IsTrue(savedDefinition.Rows.Any(row => row.RuleId != "eligibility-primary" && row.RuleId != "eligibility-secondary"));
        Assert.IsTrue(definitions.ContainsKey(proof.GetProperty("competingVersion").GetString()!));

        Dictionary<string, byte[]> readOnly = await PortalRuleVersions(fixture, fixture.ApplicationB);
        CollectionAssert.AreEqual(new[] { seed.ReadOnlyVersion }, readOnly.Keys.ToArray());
        string schema = fixture.Options.Persistence.Schema;
        await using var assignment = fixture.Source.CreateCommand($"""
            SELECT h.version_id,a.generation,r.deployment_requested_at,r.deployment_requested_generation
            FROM "{schema}".rule_engine_ruleset r
            JOIN "{schema}".configuration_binding b ON b.application_id=r.application_id
            JOIN "{schema}".configuration_definition d ON d.definition_id=b.definition_id AND d.configuration_id=r.configuration_id
            JOIN "{schema}".configuration_assignment a ON a.binding_id=b.binding_id
            JOIN "{schema}".rule_engine_release_history h ON h.ruleset_key=r.ruleset_key AND h.release_id=a.default_release_id
            WHERE r.application_id=@app AND r.ruleset_id=@ruleset
            """);
        assignment.Parameters.AddWithValue("app", Guid.Parse(fixture.ApplicationA));
        assignment.Parameters.AddWithValue("ruleset", PortalRuleset);
        await using (NpgsqlDataReader map = await assignment.ExecuteReaderAsync())
        {
            Assert.IsTrue(await map.ReadAsync());
            Assert.AreEqual(Guid.Parse(rollback), map.GetGuid(0));
            Assert.AreEqual(proof.GetProperty("finalGeneration").GetString(), map.GetInt64(1).ToString(CultureInfo.InvariantCulture));
            Assert.AreEqual(map.GetInt64(1), map.GetInt64(3));
            Assert.AreEqual(DateTimeOffset.Parse(proof.GetProperty("requestedAtUtc").GetString()!, CultureInfo.InvariantCulture).UtcDateTime,
                map.GetDateTime(2));
            Assert.IsFalse(await map.ReadAsync());
        }

        await using var history = fixture.Source.CreateCommand($"""
            SELECT DISTINCT h.version_id
            FROM "{schema}".rule_engine_deployed_version h
            JOIN "{schema}".rule_engine_ruleset r ON r.ruleset_key=h.ruleset_key
            WHERE r.application_id=@app AND r.ruleset_id=@ruleset
            """);
        history.Parameters.AddWithValue("app", Guid.Parse(fixture.ApplicationA));
        history.Parameters.AddWithValue("ruleset", PortalRuleset);
        var proven = new List<string>();
        await using (NpgsqlDataReader reader = await history.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                proven.Add(reader.GetGuid(0).ToString("D"));
            }
        }
        CollectionAssert.AreEquivalent(new[] { seed.HistoricalVersion, seed.FirstVersion, deployed }, proven);
        Assert.IsFalse(proven.Contains(seed.SecondVersion));

        await using var original = fixture.Source.CreateCommand($"""
            SELECT count(*) FROM "{fixture.Options.OperationsSchema}".operation_journal
            WHERE idempotency_key=@key AND operation_name='management.rule-engine.version.save' AND finalized
            """);
        original.Parameters.AddWithValue("key", proof.GetProperty("originalKey").GetString()!);
        Assert.AreEqual(1L, (long)(await original.ExecuteScalarAsync())!);
    }

    private static async Task VerifyPortalRuleSchemaUpdateAsync(ManagementHostFixture fixture, HttpClient actor, RulePortalSeed seed,
        JsonElement proof)
    {
        string saved = proof.GetProperty("savedVersion").GetString()!;
        Assert.AreEqual(seed.SecondVersion, proof.GetProperty("previousVersion").GetString());
        Assert.AreEqual(PortalUpdatedRuleSchema, proof.GetProperty("schemaRevision").GetString());
        Assert.AreNotEqual(seed.SecondVersion, saved);
        Dictionary<string, byte[]> definitions = await PortalRuleVersions(fixture, fixture.ApplicationA);
        Assert.HasCount(4, definitions);
        Assert.AreEqual(seed.FirstDefinition, Encoding.UTF8.GetString(definitions[seed.FirstVersion]));
        Assert.AreEqual(seed.SecondDefinition, Encoding.UTF8.GetString(definitions[seed.SecondVersion]));
        RuleDefinition authored = RuleDefinitionCodec.Decode(definitions[saved]);
        Assert.AreEqual(PortalUpdatedRuleSchema, authored.SchemaRevision);
        Assert.AreEqual(PortalAddedRuleColumn, authored.Columns[^1].Id);
        RuleRow added = authored.Rows.Single();
        RuleCondition resident = added.Conditions[^1];
        Assert.AreEqual(PortalAddedRuleColumn, resident.ColumnId);
        Assert.AreEqual(RuleOperator.Equals, resident.Operator);
        Assert.AreEqual(true, resident.Preset?.BooleanValue);
        JsonElement deployment = await ReadRuleFixtureJson(actor, PortalRulePath(fixture.ApplicationA, "deployment"));
        Guid deployed = deployment.GetProperty("desired").GetProperty("version").GetGuid();
        Assert.AreEqual(Guid.Parse(seed.FirstVersion), deployed, "Saving the first version for the new schema must leave deployment unchanged.");
    }

    private static async Task<Dictionary<string, byte[]>> PortalRuleVersions(ManagementHostFixture fixture, string application)
    {
        string schema = fixture.Options.Persistence.Schema;
        await using var command = fixture.Source.CreateCommand($"""
            SELECT v.version_id,v.definition
            FROM "{schema}".rule_engine_version v
            JOIN "{schema}".rule_engine_ruleset r ON r.ruleset_key=v.ruleset_key
            WHERE r.application_id=@app AND r.ruleset_id=@ruleset
            """);
        command.Parameters.AddWithValue("app", Guid.Parse(application));
        command.Parameters.AddWithValue("ruleset", PortalRuleset);
        var definitions = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            definitions.Add(reader.GetGuid(0).ToString("D"), reader.GetFieldValue<byte[]>(1));
        }
        return definitions;
    }
}
