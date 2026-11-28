using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Service;
using CIS.Management.Service.Persistence;
using CIS.Management.Service.Security;
using CIS.Management.Wire;
using CIS.Operations;
using CIS.Security;
using CIS.Transactions;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed partial class ManagementConfigurationCreationTests
{
    [TestMethod]
    public async Task ZeroRegistrationApplicationCreatesPublishesAndAssignsWithSeparateNativeOriginalsAndNoAutomaticEnrollment()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient setup = fixture.Client("operator-a");
        await Apply(setup, await Catalog(setup), "catalog");
        string application = await PreallocatedApplication(fixture);
        Assert.AreEqual(0L, await AppRegistrations(fixture, application));
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, AppGrants(application)));
        HttpClient actor = fixture.Client("reader", 1);
        ManagementConfigurationCreationContext before = await Context(actor, application);
        const string configuration = "undeployed.settings";
        var command = new ManagementConfigurationCreationCommand(1, configuration, "createGeneric", "Prepare before the first deployment");
        long journal = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        ManagementConfigurationOperationResult created = await Create(actor, application, "create-original", before.ETag, command);
        Assert.AreEqual("configurationCreated", created.Result!.Kind);
        Assert.AreEqual(application, created.Result.ApplicationId);
        Assert.AreEqual(configuration, created.Result.ConfigurationId);
        Assert.AreEqual("preallocated-app", created.Result.HostApplicationName);
        Assert.AreEqual("1", created.Result.SchemaVersion);
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_session"));
        Assert.AreEqual(journal + 1, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit + 1, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        ManagementConfigurationOperationResult replay = await Create(actor, application, "create-original", before.ETag, command);
        Assert.AreEqual("replayed", replay.ResultSource);
        Assert.AreEqual(created.Result, replay.Result);
        ManagementConfigurationSource source = await Source(actor, created.Result.SourceId!);
        Assert.AreEqual(created.Result.SourceETag, source.ETag);
        const string payload = " {\"arbitrary\":{\"large\":9007199254740993,\"nullable\":null},\"enabled\":false,\"zero\":0,\"text\":\"Café — 日本語\"} ";
        using HttpResponseMessage publishedResponse = await Publish(actor, source, "source-original", payload);
        Assert.AreEqual(HttpStatusCode.OK, publishedResponse.StatusCode, await publishedResponse.Content.ReadAsStringAsync());
        ManagementConfigurationOperationResult published = await Decode<ManagementConfigurationOperationResult>(publishedResponse);
        string publication = published.Result!.PublicationId!;
        using (HttpResponseMessage content = await actor.GetAsync("configuration/publications/" + publication))
        {
            Assert.AreEqual(payload, (await Decode<ManagementConfigurationPublication>(content)).PayloadJson);
        }
        ManagementConfigurationAssignmentMap empty = await Map(actor, application, configuration);
        var releaseCommand = new ManagementConfigurationReleaseCommand(1, new("absent"), new("present", publication), "Pin exact prepared source");
        using HttpResponseMessage releaseResponse = await Send(actor, Binding(application, configuration, "releases"), "release-original", empty.ETag, releaseCommand);
        Assert.AreEqual(HttpStatusCode.OK, releaseResponse.StatusCode, await releaseResponse.Content.ReadAsStringAsync());
        ManagementConfigurationOperationResult release = await Decode<ManagementConfigurationOperationResult>(releaseResponse);
        Assert.IsNull((await Map(actor, application, configuration)).Generation, "Creating an immutable release must not activate it.");
        using HttpResponseMessage assignedResponse = await Assign(actor, application, configuration, "assignment-original", empty.ETag,
            new(1, "replace", release.Result!.ReleaseId!, [], "Activate prepared release explicitly"));
        Assert.AreEqual(HttpStatusCode.OK, assignedResponse.StatusCode, await assignedResponse.Content.ReadAsStringAsync());
        ManagementConfigurationOperationResult assigned = await Decode<ManagementConfigurationOperationResult>(assignedResponse);
        Assert.AreEqual("1", assigned.Result!.Generation);
        Assert.AreEqual(0L, await AppRegistrations(fixture, application));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_session"));
        Assert.AreEqual(journal + 4, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit + 4, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        ManagementPage<ManagementConfigurationSummary> summaries = await Summaries(actor, application);
        ManagementConfigurationSummary summary = summaries.Items.Single(item => item.ConfigurationId == configuration);
        Assert.AreEqual("applicationOnly", summary.Origin);
        Assert.IsFalse(summary.HasCohortOverrides);
        Assert.AreEqual(created.Result.SourceId, summary.SourceId);
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original,
            [AppGrant(application, ManagementConfigurationCapabilities.OperationsRead)]));
        HttpClient lookupOnly = fixture.Client("reader", 2);
        foreach ((string path, ManagementConfigurationOperationResult expected) in new[]
        {
            ("applications/" + application + "/configuration-creation-operations/create-original", created),
            ("configuration/sources/" + source.SourceId + "/publication-operations/source-original", published),
            (Binding(application, configuration, "release-operations/release-original"), release),
            (Binding(application, configuration, "assignment-operations/assignment-original"), assigned)
        })
        {
            using HttpResponseMessage response = await lookupOnly.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            ManagementConfigurationOperationResult recovered = await Decode<ManagementConfigurationOperationResult>(response);
            Assert.AreEqual(expected.Result, recovered.Result);
            Assert.AreEqual(expected.Operation.Scope, recovered.Operation.Scope, "Authorization target changes must not alter the real original scope.");
        }
        using HttpResponseMessage noRead = await lookupOnly.GetAsync("configuration/sources/" + source.SourceId);
        Assert.AreEqual(HttpStatusCode.NotFound, noRead.StatusCode);
        Assert.AreEqual(journal + 4, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit + 4, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        await using var auditRows = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox");
        await using var auditReader = await auditRows.ExecuteReaderAsync();
        while (await auditReader.ReadAsync())
        {
            string envelope = Encoding.UTF8.GetString(auditReader.GetFieldValue<byte[]>(0));
            Assert.IsFalse(envelope.Contains("9007199254740993", StringComparison.Ordinal));
            Assert.IsFalse(envelope.Contains("payloadJson", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task ExplicitApplicationGrantsCannotAuthorOtherApplicationsTypedRowsCommonSourcesOrMixedTargetBundles()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient setup = fixture.Client("operator-a");
        await Apply(setup, await Catalog(setup), "catalog");
        string application = await PreallocatedApplication(fixture);
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, AppGrants(application)));
        HttpClient actor = fixture.Client("reader", 1);
        ManagementConfigurationCreationContext current = await Context(actor, application);
        ManagementConfigurationOperationResult created = await Create(actor, application, "first", current.ETag,
            new(1, "new.settings", "createGeneric", "Create isolated generic source"));
        ManagementConfigurationCreationContext next = await Context(actor, application);
        using (HttpResponseMessage duplicate = await Send(actor, Creation(application), "duplicate", next.ETag, new ManagementConfigurationCreationCommand(1, "new.settings", "createGeneric", "No silent reuse")))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.AreEqual("management.configuration.definition_exists", (await Decode<ManagementConfigurationProblem>(duplicate)).Code);
        }
        using (HttpResponseMessage typed = await Send(actor, Creation(application), "typed", next.ETag, new ManagementConfigurationCreationCommand(1, "shared.settings", "reuseGeneric", "Never adopt typed schema")))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, typed.StatusCode);
            Assert.AreEqual("management.configuration.schema_immutable", (await Decode<ManagementConfigurationProblem>(typed)).Code);
        }
        using (HttpResponseMessage foreign = await actor.GetAsync("applications/" + fixture.ApplicationA + "/configuration-creations/context"))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(setup);
        ManagementConfigurationDefinition common = definitions.Items.Single(item => item.Kind == "common");
        ManagementConfigurationDefinition typedSource = definitions.Items.Single(item => item.ApplicationId == application && item.ConfigurationId == "shared.settings");
        foreach (ManagementConfigurationDefinition forbidden in new[] { common, typedSource })
        {
            using HttpResponseMessage read = await actor.GetAsync("configuration/sources/" + forbidden.SourceId);
            Assert.AreEqual(HttpStatusCode.NotFound, read.StatusCode);
            using HttpResponseMessage publish = await Send(actor, "configuration/sources/" + forbidden.SourceId + "/publications", "forbidden-" + forbidden.SourceId,
                "\"configuration.source.1\"", new ManagementConfigurationPublicationCommand(1, "{}", "No generic authority over trusted rows"));
            Assert.AreEqual(HttpStatusCode.NotFound, publish.StatusCode);
        }
        foreach (string typedApplication in new[] { application, fixture.ApplicationA })
        {
            ManagementConfigurationAssignmentMap typedMap = await Map(setup, typedApplication, "shared.settings");
            using HttpResponseMessage release = await Send(setup, Binding(typedApplication, "shared.settings", "releases"), "typed-release-" + typedApplication,
                typedMap.ETag, new ManagementConfigurationReleaseCommand(1, new("absent"), new("absent"), "Retain typed release for authority refusal"));
            Assert.AreEqual(HttpStatusCode.OK, release.StatusCode);
            string releaseId = (await Decode<ManagementConfigurationOperationResult>(release)).Result!.ReleaseId!;
            using HttpResponseMessage read = await actor.GetAsync("configuration/releases/" + releaseId);
            Assert.AreEqual(HttpStatusCode.NotFound, read.StatusCode, "Application-scoped generic definitions authority cannot expose typed or another application's release.");
        }
        ManagementConfigurationSource source = await Source(actor, created.Result!.SourceId!);
        ManagementConfigurationDefinition prepared = definitions.Items.Single(item => item.SourceId == source.SourceId);
        var originalInput = new ManagementPublicationInput(prepared, source.ETag, new(1, "{}", "Preserve the actual logical original target"));
        string logical = "management.configuration.v1/management.configuration.source.publish/" + source.SourceId + "/management.actor.v1." + new string('a', 64);
        _ = ManagementConfigurationOperations.Publication().Normalizer.Normalize(originalInput, new(logical, "management.application", application));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementConfigurationOperations.Publication().Normalizer.Normalize(originalInput,
            new(logical.Replace(source.SourceId, Guid.NewGuid().ToString("D"), StringComparison.Ordinal), "management.application", application)));
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original,
        [
            new(original.Profile, ManagementConfigurationCapabilities.Publish, EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                subject: new(ManagementHostFixture.Issuer, "reader"), resourceSelection: CapabilityResourceSelection.ExactResource,
                resourceType: ManagementConfigurationCapabilities.SourceResource, resourceIdentifier: source.SourceId),
            AppGrant(application, ManagementConfigurationCapabilities.OperationsRead)
        ]));
        using HttpResponseMessage mixed = await Publish(fixture.Client("reader", 2), source, "mixed-bundle", "{}");
        Assert.IsFalse(mixed.IsSuccessStatusCode, "Write and lookup must be authorized together at one actual target, not mixed across source and app grants.");
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        using HttpResponseMessage actionsRead = await actor.GetAsync("configuration/sources/" + source.SourceId + "/actions");
        Assert.AreEqual(HttpStatusCode.OK, actionsRead.StatusCode);
        Assert.IsTrue(CIS.Management.Wire.ManagementSchedulerWire.DecodeResponse<ManagementConfigurationActions>(await actionsRead.Content.ReadAsByteArrayAsync())!.Actions.All(item => item.Allowed));
    }

    [TestMethod]
    public async Task SummariesUseAssignedPinnedLayersAndProtectTheirOriginWithoutContentOrReleaseMetadataAuthority()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        ManagementConfigurationDefinition common = definitions.Items.Single(item => item.Kind == "common");
        ManagementConfigurationDefinition app = definitions.Items.Single(item => item.ApplicationId == fixture.ApplicationA);
        ManagementConfigurationSummary unassigned = (await Summaries(actor, fixture.ApplicationA)).Items.Single();
        Assert.AreEqual("unassigned", unassigned.Origin);
        Assert.IsNull(unassigned.ModifiedAtUtc);
        using HttpResponseMessage commonResponse = await Publish(actor, await Source(actor, common.SourceId), "common", "{\"Greeting\":\"shared\"}");
        string commonPublication = (await Decode<ManagementConfigurationOperationResult>(commonResponse)).Result!.PublicationId!;
        using HttpResponseMessage appResponse = await Publish(actor, await Source(actor, app.SourceId), "application", "{\"Enabled\":false}");
        string appPublication = (await Decode<ManagementConfigurationOperationResult>(appResponse)).Result!.PublicationId!;
        string commonCreatedAt = await PublicationTime(actor, commonPublication);
        string appCreatedAt = await PublicationTime(actor, appPublication);
        using (HttpResponseMessage newer = await Publish(actor, await Source(actor, app.SourceId), "newer-unassigned-application", "{\"Enabled\":true}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, newer.StatusCode);
            string newerPublication = (await Decode<ManagementConfigurationOperationResult>(newer)).Result!.PublicationId!;
            Assert.AreNotEqual(appCreatedAt, await PublicationTime(actor, newerPublication));
        }
        var retained = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string origin, ManagementConfigurationSourceReference commonRef, ManagementConfigurationSourceReference appRef) in new[]
        {
            ("defaultsOnly", new ManagementConfigurationSourceReference("absent"), new ManagementConfigurationSourceReference("absent")),
            ("shared", new ManagementConfigurationSourceReference("present", commonPublication), new ManagementConfigurationSourceReference("absent")),
            ("applicationOnly", new ManagementConfigurationSourceReference("absent"), new ManagementConfigurationSourceReference("present", appPublication)),
            ("applicationOverride", new ManagementConfigurationSourceReference("present", commonPublication), new ManagementConfigurationSourceReference("present", appPublication))
        })
        {
            ManagementConfigurationAssignmentMap baseline = await Map(actor, fixture.ApplicationA, "shared.settings");
            using HttpResponseMessage releaseResponse = await Send(actor, Binding(fixture.ApplicationA, "shared.settings", "releases"), "release-" + origin,
                baseline.ETag, new ManagementConfigurationReleaseCommand(1, commonRef, appRef, "Pin " + origin));
            Assert.AreEqual(HttpStatusCode.OK, releaseResponse.StatusCode, await releaseResponse.Content.ReadAsStringAsync());
            string release = (await Decode<ManagementConfigurationOperationResult>(releaseResponse)).Result!.ReleaseId!;
            retained.Add(origin, release);
            using HttpResponseMessage assignment = await Assign(actor, fixture.ApplicationA, "shared.settings", "assign-" + origin, baseline.ETag,
                new(1, "replace", release, [], "Activate exact layer pair"));
            Assert.AreEqual(HttpStatusCode.OK, assignment.StatusCode, await assignment.Content.ReadAsStringAsync());
            ManagementConfigurationSummary summary = (await Summaries(actor, fixture.ApplicationA)).Items.Single();
            Assert.AreEqual(origin, summary.Origin);
            Assert.IsFalse(summary.HasCohortOverrides);
            Assert.AreEqual(origin == "defaultsOnly" ? null : origin == "shared" ? commonCreatedAt : appCreatedAt, summary.ModifiedAtUtc,
                "The displayed time comes from the pinned default-selected JSON layer, not assignment or newer unassigned publication history.");
        }
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA, "shared.settings");
        using (HttpResponseMessage mixed = await Assign(actor, fixture.ApplicationA, "shared.settings", "mixed", map.ETag,
            new(1, "replace", retained["shared"], [new("canary", retained["applicationOnly"])], "Pin a genuinely different cohort pair")))
        {
            Assert.AreEqual(HttpStatusCode.OK, mixed.StatusCode, await mixed.Content.ReadAsStringAsync());
        }
        ManagementConfigurationSummary changed = (await Summaries(actor, fixture.ApplicationA)).Items.Single();
        Assert.AreEqual("mixed", changed.Origin);
        Assert.IsTrue(changed.HasCohortOverrides);
        Assert.AreEqual(commonCreatedAt, changed.ModifiedAtUtc, "Cohort differences do not replace the selected default-layer date.");
        long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        using HttpResponseMessage protectedResponse = await fixture.Client("operator-b").GetAsync("applications/" + fixture.ApplicationA + "/configuration-summaries");
        Assert.AreEqual(HttpStatusCode.OK, protectedResponse.StatusCode);
        string responseText = await protectedResponse.Content.ReadAsStringAsync();
        ManagementConfigurationSummary protectedSummary = (await Decode<ManagementPage<ManagementConfigurationSummary>>(protectedResponse)).Items.Single();
        Assert.AreEqual("protected", protectedSummary.Origin);
        Assert.IsNull(protectedSummary.ModifiedAtUtc);
        Assert.IsFalse(responseText.Contains(commonPublication, StringComparison.Ordinal));
        Assert.IsFalse(responseText.Contains(appPublication, StringComparison.Ordinal));
        Assert.IsFalse(responseText.Contains("defaultsJson", StringComparison.Ordinal));
        Assert.AreEqual(audit, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, original.Grants.Where(grant =>
            !(grant.Subject?.Identifier == "operator-a" && grant.Capability == ManagementConfigurationCapabilities.DefinitionsRead
                && grant.ResourceType == ManagementConfigurationCapabilities.SourceResource))
            .Append(new(original.Profile, ManagementConfigurationCapabilities.DefinitionsRead, EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                subject: new(ManagementHostFixture.Issuer, "operator-a"), resourceSelection: CapabilityResourceSelection.ExactResource,
                resourceType: ManagementConfigurationCapabilities.SourceResource, resourceIdentifier: app.SourceId)).ToArray()));
        ManagementConfigurationSummary withheldDate = (await Summaries(fixture.Client("operator-a", 1), fixture.ApplicationA)).Items.Single();
        Assert.AreEqual("mixed", withheldDate.Origin, "Release metadata authority can disclose the origin independently of Common-source metadata authority.");
        Assert.IsNull(withheldDate.ModifiedAtUtc, "The selected Common publication time needs actual Common-source metadata authority.");
    }

    [TestMethod]
    public async Task ChangedTrustedCatalogPreservesUnclaimedGenericRowsAndOnlyExactGenericAdoptionCanClaimThem()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient setup = fixture.Client("operator-a");
        await Apply(setup, await Catalog(setup), "catalog");
        string application = await PreallocatedApplication(fixture);
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, AppGrants(application)));
        HttpClient actor = fixture.Client("reader", 1);
        ManagementConfigurationOperationResult created = await Create(actor, application, "create", (await Context(actor, application)).ETag,
            new(1, "preserved.settings", "createGeneric", "Prepare owned row"));
        ManagementConfigurationOptions changed = Deployment("stable");
        await fixture.StartReplicaAsync(serviceOptions: Options(fixture, changed));
        HttpClient changedActor = fixture.Client("operator-a", 2);
        await Apply(changedActor, await Catalog(changedActor), "changed-catalog");
        ManagementConfigurationSource retained = await Source(actor, created.Result!.SourceId!);
        Assert.AreEqual(created.Result.SourceETag, retained.ETag);
        await using var native = fixture.Source.CreateCommand($"""
            SELECT b.manifest_managed,b.predeployment_created,d.manifest_managed,b.enabled,d.enabled
            FROM "{fixture.Options.Persistence.Schema}".configuration_binding b
            JOIN "{fixture.Options.Persistence.Schema}".configuration_definition d ON d.definition_id=b.definition_id WHERE b.binding_id=@binding
            """);
        native.Parameters.AddWithValue("binding", Guid.Parse(created.Result.BindingId!));
        await using (var reader = await native.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync());
            Assert.IsFalse(reader.GetBoolean(0));
            Assert.IsTrue(reader.GetBoolean(1));
            Assert.IsFalse(reader.GetBoolean(2));
            Assert.IsTrue(reader.GetBoolean(3));
            Assert.IsTrue(reader.GetBoolean(4));
        }
        var generic = new ManagementConfigurationSchema("preserved.settings", "1", new("object", false, []), "{}");
        ManagementConfigurationManifest claimed = changed.Manifest with
        {
            Definitions = changed.Manifest.Definitions.Append(generic).ToArray(),
            Bindings = changed.Manifest.Bindings.Append(new("preallocated-app", "Development", "preallocated-app", "preserved.settings", "1")).ToArray()
        };
        var exact = new ManagementConfigurationOptions(JsonSerializer.Serialize(claimed, ManagementConfigurationOptions.Json), 32L * 1024 * 1024);
        await fixture.StartReplicaAsync(serviceOptions: Options(fixture, exact));
        HttpClient exactActor = fixture.Client("operator-a", 3);
        await Apply(exactActor, await Catalog(exactActor), "exact-generic-adoption");
        await using (var reader = await native.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync());
            Assert.IsTrue(reader.GetBoolean(0));
            Assert.IsTrue(reader.GetBoolean(1), "Native-created provenance remains explicit after a compatible deployment claims management.");
            Assert.IsTrue(reader.GetBoolean(2));
        }
        Assert.AreEqual(created.Result.SourceId, (await Definitions(exactActor)).Items.Single(item => item.ApplicationId == application && item.ConfigurationId == generic.ConfigurationId).SourceId);
        var typed = new ManagementConfigurationOptions(JsonSerializer.Serialize(claimed with
        {
            Definitions = claimed.Definitions.Select(item => item.ConfigurationId == generic.ConfigurationId
                ? item with
                {
                    Shape = new("object", false, [new("Value", true, new("string", false))]),
                    DefaultsJson = "{\"Value\":\"typed\"}"
                } : item).ToArray()
        }, ManagementConfigurationOptions.Json), 32L * 1024 * 1024);
        await fixture.StartReplicaAsync(serviceOptions: Options(fixture, typed));
        HttpClient typedActor = fixture.Client("operator-a", 4);
        ManagementConfigurationCatalog pending = await Catalog(typedActor);
        using HttpResponseMessage refusal = await Send(typedActor, "configuration/catalog/apply", "typed-cannot-reinterpret", pending.ETag,
            new ManagementConfigurationCatalogCommand(1, pending.LoadedManifestDigest, "Typed ownership requires another schema version"));
        Assert.AreEqual(HttpStatusCode.Conflict, refusal.StatusCode);
        Assert.AreEqual("management.configuration.schema_immutable", (await Decode<ManagementConfigurationProblem>(refusal)).Code);
        Assert.AreEqual(created.Result.SourceId, (await Source(actor, created.Result.SourceId!)).SourceId);
    }

    [TestMethod]
    public async Task VersionThreeUpgradePreservesExistingRowsAndMarksThemAsTrustedManifestRatherThanNativeCreated()
    {
        await using ManagementDatabaseFixture fixture = await ManagementDatabaseFixture.CreateAsync();
        ManagementStoreResult<ManagementRegistrationReceipt> registration = await fixture.Store.RegisterAsync(
            new("migration-app", "Development", "fixture-issuer", "fixture-subject", new("notProvided")), Guid.NewGuid().ToString("D"), ManagementHostFixture.Request("migration"));
        Assert.IsTrue(registration.IsKnownCommitted, registration.Code);
        string definitionId = Guid.NewGuid().ToString("D");
        string bindingId = Guid.NewGuid().ToString("D");
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new ManagementConfigurationSchema("migration.settings", "1", new("object", false, []), "{}"), ManagementConfigurationOptions.Json);
        string bodyHex = Convert.ToHexStringLower(body);
        string digestHex = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(body));
        await fixture.SqlAsync($"""
            INSERT INTO {fixture.Table("configuration_definition")}(definition_id,configuration_id,schema_version,definition_body,definition_digest,logical_bytes)
                VALUES('{definitionId}','migration.settings',1,decode('{bodyHex}','hex'),decode('{digestHex}','hex'),{body.Length + 64});
            INSERT INTO {fixture.Table("configuration_binding")}(binding_id,application_id,definition_id,host_scope,logical_bytes)
                VALUES('{bindingId}','{registration.Value!.ApplicationId}','{definitionId}','MigrationHost',141)
            """);
        string authority = await fixture.ReadAsync(async (connection, transaction, token) =>
        {
            await using var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = $"SELECT authority_id FROM {fixture.Table("management_authority")} WHERE singleton";
            return ((Guid)(await query.ExecuteScalarAsync(token))!).ToString("D");
        });
        await fixture.SqlAsync($"""
            {ManagementDatabaseFixture.DropRuleEngineTables(fixture.Table)}
            ALTER TABLE {fixture.Table("configuration_binding")} DROP COLUMN predeployment_created;
            ALTER TABLE {fixture.Table("configuration_binding")} DROP COLUMN manifest_managed;
            ALTER TABLE {fixture.Table("configuration_definition")} DROP COLUMN manifest_managed;
            ALTER TABLE {fixture.Table("management_schema_version")} DROP CONSTRAINT management_schema_version_version_check;
            UPDATE {fixture.Table("management_schema_version")} SET version=3;
            ALTER TABLE {fixture.Table("management_schema_version")} ADD CONSTRAINT management_schema_version_version_check CHECK(version=3)
            """);
        for (int index = 0; index < 2; index++)
        {
            await using ILocalTransaction owner = await fixture.Factory.BeginAsync("management");
            await ManagementSchema.InstallAsync(owner.Context, fixture.Options.Schema, fixture.Options);
            Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        }
        Assert.AreEqual((long)ManagementSchema.Version, await fixture.ScalarAsync($"SELECT version FROM {fixture.Table("management_schema_version")}"));
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Table("management_authority")} WHERE authority_id='{authority}'::uuid"));
        Assert.AreEqual(3L, await fixture.ScalarAsync($"""
            SELECT count(*) FROM information_schema.columns WHERE table_schema='{fixture.Options.Schema}'
                AND ((table_name='configuration_definition' AND column_name='manifest_managed' AND column_default='true')
                    OR (table_name='configuration_binding' AND column_name='manifest_managed' AND column_default='true')
                    OR (table_name='configuration_binding' AND column_name='predeployment_created' AND column_default='false'))
            """));
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Table("configuration_definition")} WHERE definition_id='{definitionId}' AND manifest_managed AND definition_body=decode('{bodyHex}','hex')"));
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Table("configuration_binding")} WHERE binding_id='{bindingId}' AND manifest_managed AND NOT predeployment_created"));
    }

    private static string Creation(string application) => "applications/" + application + "/configuration-creations";

    private static async Task<string> PublicationTime(HttpClient actor, string publication)
    {
        using HttpResponseMessage response = await actor.GetAsync("configuration/publications/" + publication);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await Decode<ManagementConfigurationPublication>(response)).CreatedAtUtc;
    }

    private static ManagementServiceOptions Options(ManagementHostFixture fixture, ManagementConfigurationOptions configuration)
        => new(fixture.Options.ConnectionString, fixture.Options.CopyCursorSigningKey(), fixture.Options.Persistence, fixture.Options.BasePath,
            fixture.Options.MaximumConcurrentRequests, fixture.Options.OperationsSchema, fixture.Options.MessagingSchema, fixture.Options.Delivery,
            fixture.Options.Backend, configuration);
    private static string Binding(string application, string configuration, string suffix)
        => "applications/" + application + "/configuration/" + configuration + "/" + suffix + "?schemaVersion=1";

    private static async Task<ManagementConfigurationCreationContext> Context(HttpClient actor, string application)
    {
        using HttpResponseMessage response = await actor.GetAsync(Creation(application) + "/context");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationCreationContext>(response);
    }

    private static async Task<ManagementConfigurationOperationResult> Create(HttpClient actor, string application, string key, string etag, ManagementConfigurationCreationCommand command)
    {
        using HttpResponseMessage response = await Send(actor, Creation(application), key, etag, command);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationOperationResult>(response);
    }

    private static async Task<ManagementConfigurationAssignmentMap> Map(HttpClient actor, string application, string configuration)
    {
        using HttpResponseMessage response = await actor.GetAsync(Binding(application, configuration, "assignments"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationAssignmentMap>(response);
    }

    private static async Task<ManagementPage<ManagementConfigurationSummary>> Summaries(HttpClient actor, string application)
    {
        using HttpResponseMessage response = await actor.GetAsync("applications/" + application + "/configuration-summaries");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementPage<ManagementConfigurationSummary>>(response);
    }

    private static async Task<HttpResponseMessage> Assign(HttpClient actor, string application, string configuration, string key, string etag, ManagementConfigurationAssignmentCommand command)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Binding(application, configuration, "assignments"));
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(command));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await actor.SendAsync(request);
    }

    private static async Task<string> PreallocatedApplication(ManagementHostFixture fixture)
    {
        await using var query = fixture.Source.CreateCommand($"SELECT application_id FROM \"{fixture.Options.Persistence.Schema}\".management_application WHERE application='preallocated-app' AND environment='Development'");
        return ((Guid)(await query.ExecuteScalarAsync())!).ToString("D");
    }

    private static async Task<long> AppRegistrations(ManagementHostFixture fixture, string application)
    {
        await using var query = fixture.Source.CreateCommand($"SELECT count(*) FROM \"{fixture.Options.Persistence.Schema}\".management_registration WHERE application_id=@app");
        query.Parameters.AddWithValue("app", Guid.Parse(application));
        return (long)(await query.ExecuteScalarAsync())!;
    }

    private static IReadOnlyList<CapabilityGrant> AppGrants(string application)
        => new[] { "management.applications.read", ManagementConfigurationCapabilities.Create, ManagementConfigurationCapabilities.DefinitionsRead,
            ManagementConfigurationCapabilities.ContentRead, ManagementConfigurationCapabilities.Validate, ManagementConfigurationCapabilities.Publish,
            ManagementConfigurationCapabilities.OperationsRead, ManagementConfigurationCapabilities.Preview, ManagementConfigurationCapabilities.ReleaseCreate,
            ManagementConfigurationCapabilities.AssignmentWrite }.Select(capability => AppGrant(application, capability)).ToArray();

    private static CapabilityGrant AppGrant(string application, string capability)
        => new(ManagementHostFixture.Profile, capability, EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
            subject: new(ManagementHostFixture.Issuer, "reader"), resourceSelection: CapabilityResourceSelection.ExactResource,
            resourceType: "management.application", resourceIdentifier: application);

    private static ManagementSecurityOptions Grants(ManagementSecurityOptions original, IReadOnlyList<CapabilityGrant> grants)
        => new()
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
            PolicyVersion = original.PolicyVersion,
            IdentityMappings = original.IdentityMappings,
            NativeProvider = original.NativeProvider,
            Grants = grants
        };
}
