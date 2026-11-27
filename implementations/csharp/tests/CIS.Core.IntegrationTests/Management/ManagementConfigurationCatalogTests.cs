using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationCatalogTests
{
    [TestMethod]
    public void TrustedManifestRejectsDuplicateDecodedNamesBomMalformedUnicodeAndExcessDepth()
    {
        const string valid = "{\"manifestVersion\":1,\"definitions\":[],\"bindings\":[],\"commonSources\":[],\"cohorts\":[]}";
        Assert.IsNotNull(new ManagementConfigurationOptions(valid, 1024));
        foreach (string manifest in new[]
        {
            valid.Replace("\"manifestVersion\":1", "\"manifestVersion\":1,\"manifestVersion\":1", StringComparison.Ordinal),
            valid.Replace("\"manifestVersion\":1", "\"manifestVersion\":1,\"manifest\\u0056ersion\":1", StringComparison.Ordinal),
            "\ufeff" + valid,
            valid.Replace("\"definitions\":[]", "\"definitions\":[\"\\ud800\"]", StringComparison.Ordinal),
            valid.Replace("\"definitions\":[]", "\"definitions\":" + new string('[', 33) + "0" + new string(']', 33), StringComparison.Ordinal),
            valid.Replace("manifestVersion", "ManifestVersion", StringComparison.Ordinal)
        })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new ManagementConfigurationOptions(manifest, 1024));
        }
    }

    [TestMethod]
    public void OversizedTrustedManifestIsRejectedBeforeAllocatingItsUtf8Copy()
    {
        const string valid = "{\"manifestVersion\":1,\"definitions\":[],\"bindings\":[],\"commonSources\":[],\"cohorts\":[]}";
        _ = new ManagementConfigurationOptions(valid, 1024);
        string[] oversized = [new('x', 16 * 1024 * 1024 + 1), new('é', 8 * 1024 * 1024 + 1)];
        foreach (string manifest in oversized)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.ThrowsExactly<ArgumentException>(() => new ManagementConfigurationOptions(manifest, 1024));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsLessThan(1024L * 1024, allocated, "Reject both excess character length and excess strict UTF-8 bytes before copying a sixteen-MiB buffer.");
        }
    }

    [TestMethod]
    public async Task EmptyCatalogHasAStableReadablePreconditionAndNativeApplyDiscoversUsableSourceIds()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        ManagementConfigurationCatalog empty = await Catalog(actor);
        Assert.IsFalse(empty.Ready);
        Assert.IsNull(empty.AppliedManifestDigest);
        Assert.IsTrue(ManagementWire.IsStrongETag(empty.ETag));
        using (HttpResponseMessage denied = await fixture.Client("operator-b").GetAsync("configuration/catalog"))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode);
        }
        using (HttpResponseMessage info = await actor.GetAsync("service-info"))
        {
            ManagementServiceInfo service = ManagementWire.DecodeResponse<ManagementServiceInfo>(await info.Content.ReadAsByteArrayAsync())!;
            Assert.IsTrue(service.Features.Any(feature => feature.OperationId == "applyConfigurationCatalog"));
            Assert.IsFalse(service.Features.Any(feature => feature.OperationId == "publishConfigurationSource"));
        }
        ManagementConfigurationOperationResult applied = await Apply(actor, empty, "initial-catalog");
        Assert.AreEqual("committed", applied.OriginalCompletion);
        ManagementConfigurationCatalog current = await Catalog(actor);
        Assert.IsTrue(current.Ready);
        Assert.AreEqual(empty.CatalogId, current.CatalogId);
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        Assert.HasCount(4, definitions.Items);
        Assert.AreEqual(fixture.ApplicationA, definitions.Items.Single(row => row.HostApplicationName == "FirstHost").ApplicationId);
        Assert.AreEqual(fixture.ApplicationB, definitions.Items.Single(row => row.HostApplicationName == "SecondHost").ApplicationId);
        ManagementConfigurationDefinition preallocated = definitions.Items.Single(row => row.HostApplicationName == "ThirdHost");
        await using (var query = fixture.Source.CreateCommand($"SELECT application_count FROM \"{fixture.Options.Persistence.Schema}\".management_capacity WHERE singleton"))
        {
            Assert.AreEqual(3, (int)(await query.ExecuteScalarAsync())!);
        }
        using (HttpResponseMessage read = await actor.GetAsync("configuration/sources/" + preallocated.SourceId))
        {
            Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
            ManagementConfigurationSource source = await Decode<ManagementConfigurationSource>(read);
            Assert.IsEmpty(source.Publications.Items);
            Assert.IsTrue(ManagementWire.IsStrongETag(source.ETag));
        }
        ManagementConfigurationOperationResult again = await Apply(actor, current, "same-deployment-new-intent");
        Assert.AreEqual(current.ETag, again.Result!.ETag);
        CollectionAssert.AreEquivalent(definitions.Items.Select(item => item.SourceId).ToArray(),
            (await Definitions(actor)).Items.Select(item => item.SourceId).ToArray());
        using HttpResponseMessage finalInfo = await actor.GetAsync("service-info");
        ManagementServiceInfo installed = ManagementWire.DecodeResponse<ManagementServiceInfo>(await finalInfo.Content.ReadAsByteArrayAsync())!;
        string[] configurationOperations =
        [
            "getConfigurationCatalog", "applyConfigurationCatalog", "getConfigurationCatalogOperation",
            "getConfigurationDefinitions", "getApplicationConfigurationDefinitions", "validateConfiguration",
            "getConfigurationSource", "publishConfigurationSource", "getConfigurationPublication", "getConfigurationPublicationOperation",
            "previewConfiguration", "createConfigurationRelease", "getConfigurationRelease", "getConfigurationAssignments",
            "replaceConfigurationAssignments", "getConfigurationReleaseOperation", "getConfigurationAssignmentOperation",
            "getRegistrationConfiguration", "reportConfigurationStatus", "getConfigurationStatus",
            "getConfigurationSourceActions", "getConfigurationBindingActions", "getConfigurationCohorts", "getConfigurationEditorContext",
            "previewConfigurationCandidate", "listConfigurationReleases", "getConfigurationBindingStatus",
            "getConfigurationCreationContext", "createConfiguration", "getConfigurationCreationOperation", "getConfigurationSummaries"
        ];
        string[] managementOperations =
        [
            "getServiceInfo", "registerInstance", "getRegistrationControl", "closeRegistration", "reportObservation", "replaceInventory",
            "listApplications", "getApplication", "listInstances", "listInventory", "getApplicationCapabilities",
            "replaceApplicationMetadata", "getMetadataOperation", "queryApplications", "listEnvironments"
        ];
        // Include the scoped definitions and discovery companions rather than carrying forward stale pre-RM09 totals.
        CollectionAssert.AreEquivalent(configurationOperations, installed.Features
            .Where(feature => feature.OperationId.Contains("Configuration", StringComparison.Ordinal)).Select(feature => feature.OperationId).ToArray());
        CollectionAssert.AreEquivalent(configurationOperations.Concat(managementOperations).ToArray(),
            installed.Features.Select(feature => feature.OperationId).ToArray());
        Assert.AreEqual(installed.Features.Count, installed.Features.Select(feature => feature.OperationId).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public async Task MetadataDiscoveryDoesNotDiscloseDefaultsOrGrantPublicationAndContentAuthority()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementConfigurationDefinition common = (await Definitions(actor)).Items.Single(row => row.Kind == "common");
        ManagementConfigurationSource source = await Source(actor, common.SourceId);
        using HttpResponseMessage published = await Publish(actor, source, "original-source", "{\"Greeting\":\"Café — 日本語\"}");
        string publicationId = (await Decode<ManagementConfigurationOperationResult>(published)).Result!.PublicationId!;
        HttpClient reader = fixture.Client("operator-b");
        ManagementPage<ManagementConfigurationDefinition> metadata = await Definitions(reader);
        Assert.HasCount(4, metadata.Items);
        Assert.IsTrue(metadata.Items.All(row => row.DefaultsJson is null));
        ManagementConfigurationDefinition commonMetadata = metadata.Items.Single(row => row.Kind == "common");
        Assert.IsNull(commonMetadata.ApplicationId);
        Assert.IsNull(commonMetadata.BindingId);
        Assert.IsNull(commonMetadata.HostApplicationName);
        using HttpResponseMessage deniedRead = await reader.GetAsync("configuration/publications/" + publicationId);
        Assert.AreEqual(HttpStatusCode.NotFound, deniedRead.StatusCode);
        using HttpResponseMessage deniedWrite = await Publish(reader, source, "forbidden", "{}");
        Assert.AreEqual(HttpStatusCode.NotFound, deniedWrite.StatusCode);
        using var unauthenticated = new HttpClient();
        using HttpResponseMessage unauthorized = await unauthenticated.GetAsync("http://127.0.0.1:" + fixture.OperationalPorts[0]
            + "/platform/management/v1/configuration/definitions");
        Assert.AreEqual(HttpStatusCode.Forbidden, unauthorized.StatusCode,
            "The actual operational listener must reject business routes before authentication. Status: " + unauthorized.StatusCode);
        Assert.IsEmpty(await unauthorized.Content.ReadAsByteArrayAsync(), "Listener isolation must expose no configuration body.");
    }

    [TestMethod]
    public async Task NativeAuditRefusalRollsBackCatalogProvisioningAndItsOriginalOperationClaim()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment(), maximumRetainedEnvelopeBytes: 1);
        HttpClient actor = fixture.Client("operator-a");
        ManagementConfigurationCatalog before = await Catalog(actor);
        using HttpResponseMessage refused = await Send(actor, "configuration/catalog/apply", "audit-refused", before.ETag,
            new ManagementConfigurationCatalogCommand(1, before.LoadedManifestDigest, "Apply trusted deployment"));
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        ManagementConfigurationProblem problem = await Decode<ManagementConfigurationProblem>(refused);
        Assert.AreEqual("management.configuration.audit_unavailable", problem.Code);
        Assert.AreEqual("audit-refused", problem.Operation!.Key);
        Assert.AreEqual(before, await Catalog(actor));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_definition"));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        using HttpResponseMessage absent = await actor.GetAsync("configuration/catalog/operations/audit-refused");
        Assert.AreEqual(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.AreEqual("management.configuration.operation_not_found", (await Decode<ManagementConfigurationProblem>(absent)).Code);
        Assert.AreEqual(before, await Catalog(actor));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
    }

    [TestMethod]
    public async Task CurrentVersionMigrationPreservesCatalogAndAuthorityAndVersionTwoUpgradePreservesApplications()
    {
        await using ManagementDatabaseFixture fixture = await ManagementDatabaseFixture.CreateAsync();
        string before = await fixture.ReadAsync(async (connection, transaction, token) =>
        {
            await using var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = $"SELECT authority_id FROM {fixture.Table("management_authority")} WHERE singleton";
            return ((Guid)(await query.ExecuteScalarAsync(token))!).ToString("D");
        });
        var registered = await fixture.Store.RegisterAsync(new("prior-app", "Development", "issuer", "subject", new("notProvided")),
            Guid.NewGuid().ToString("D"), ManagementHostFixture.Request("prior"));
        string[] tables = ["configuration_adoption", "configuration_session", "configuration_cohort", "configuration_override",
            "configuration_assignment", "configuration_release", "configuration_publication", "configuration_source", "configuration_binding",
            "configuration_definition", "configuration_catalog"];
        await fixture.SqlAsync(string.Join(";", tables.Select(table => "DROP TABLE " + fixture.Table(table))) + $"""
            ; ALTER TABLE {fixture.Table("management_schema_version")} DROP CONSTRAINT management_schema_version_version_check;
            UPDATE {fixture.Table("management_schema_version")} SET version=2;
            ALTER TABLE {fixture.Table("management_schema_version")} ADD CONSTRAINT management_schema_version_version_check CHECK(version=2)
            """);
        for (int index = 0; index < 2; index++)
        {
            await using ILocalTransaction owner = await fixture.Factory.BeginAsync("management");
            await ManagementSchema.InstallAsync(owner.Context, fixture.Options.Schema, fixture.Options);
            Assert.AreEqual(TransactionOutcome.Committed, (await owner.CommitAsync()).Outcome);
        }
        Assert.IsNotNull((await fixture.Store.ReadApplicationAsync(registered.Value!.ApplicationId)).Value);
        string after = await fixture.ReadAsync(async (connection, transaction, token) =>
        {
            await using var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = $"SELECT authority_id FROM {fixture.Table("management_authority")} WHERE singleton";
            return ((Guid)(await query.ExecuteScalarAsync(token))!).ToString("D");
        });
        Assert.AreEqual(before, after);
    }

    internal static ManagementConfigurationOptions Deployment(string cohort = "canary", string greetingDefault = "default", long quota = 32L * 1024 * 1024)
    {
        var shape = new ManagementConfigurationShape("object", false,
            [new("Greeting", true, new("string", false)), new("Enabled", true, new("boolean", false))]);
        var manifest = new ManagementConfigurationManifest(1, [new("shared.settings", "1", shape,
            JsonSerializer.Serialize(new { Greeting = greetingDefault, Enabled = true }))],
            [new("managed-a", "Development", "FirstHost", "shared.settings", "1"),
                new("managed-b", "Development", "SecondHost", "shared.settings", "1"),
                new("preallocated-app", "Development", "ThirdHost", "shared.settings", "1")],
            [new("shared.settings", "1", "Development")],
            [new(ManagementHostFixture.Issuer, "workload-a", "managed-a", "Development", cohort)]);
        return new(JsonSerializer.Serialize(manifest, ManagementConfigurationOptions.Json), quota);
    }

    internal static async Task<ManagementConfigurationCatalog> Catalog(HttpClient actor)
    {
        using HttpResponseMessage read = await actor.GetAsync("configuration/catalog");
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
        return await Decode<ManagementConfigurationCatalog>(read);
    }
    internal static async Task<ManagementPage<ManagementConfigurationDefinition>> Definitions(HttpClient actor)
    {
        using HttpResponseMessage response = await actor.GetAsync("configuration/definitions");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await Decode<ManagementPage<ManagementConfigurationDefinition>>(response);
    }
    internal static async Task<ManagementConfigurationSource> Source(HttpClient actor, string sourceId)
    {
        using HttpResponseMessage response = await actor.GetAsync("configuration/sources/" + sourceId);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await Decode<ManagementConfigurationSource>(response);
    }
    internal static async Task<ManagementConfigurationOperationResult> Apply(HttpClient actor, ManagementConfigurationCatalog catalog, string key)
    {
        using HttpResponseMessage response = await Send(actor, "configuration/catalog/apply", key, catalog.ETag,
            new ManagementConfigurationCatalogCommand(1, catalog.LoadedManifestDigest, "Apply trusted deployment"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationOperationResult>(response);
    }
    internal static Task<HttpResponseMessage> Publish(HttpClient actor, ManagementConfigurationSource source, string key, string payload)
        => Send(actor, "configuration/sources/" + source.SourceId + "/publications", key, source.ETag,
            new ManagementConfigurationPublicationCommand(1, payload, "Publish reviewed source"));
    internal static async Task<HttpResponseMessage> Send<T>(HttpClient actor, string path, string key, string etag, T command) where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(command));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await actor.SendAsync(request);
    }
    internal static async Task<T> Decode<T>(HttpResponseMessage response) where T : class
        => ManagementConfigurationWire.DecodeResponse<T>(await response.Content.ReadAsByteArrayAsync())
            ?? throw new AssertFailedException("A bounded configuration response was expected.");
    internal static async Task<long> Count(ManagementHostFixture fixture, string schema, string table)
    {
        await using var command = fixture.Source.CreateCommand($"SELECT count(*) FROM \"{schema}\".\"{table}\"");
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
