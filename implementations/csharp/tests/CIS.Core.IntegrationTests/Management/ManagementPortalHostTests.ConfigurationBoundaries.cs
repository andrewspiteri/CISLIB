using System.Net;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Wire;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    internal enum ConfigurationBoundaryScenario
    {
        None,
        Predeployment,
        SharedReferenceDiff,
        SessionStorage
    }

    private const string BoundaryPredeploymentConfiguration = "portal.before.instances";
    private const string BoundaryPredeploymentPayload = """ { "Greeting":"Before any instance", "Enabled":false,"Count":9007199254740993,"Text":"日本語 🌍 e\u0301","Optional":null,"Zero":-0 } """;
    private const string BoundaryApplicationPayload = """{"Display":"Shared-reference-only example","Enabled":false,"Count":9007199254740993}""";

    [TestMethod]
    public async Task ActualPortalCreatesSavesAndDeploysConfigurationBeforeAnyInstanceAndRefusesUnretainedOriginals()
    {
        await RunFixtureAsync(browser: true, configurationBoundary: ConfigurationBoundaryScenario.Predeployment);
    }

    [TestMethod]
    public async Task ActualVersionComparisonShowsChangedSharedReferencesWhenEveryDisplayedJsonByteIsIdentical()
    {
        await RunFixtureAsync(browser: true, configurationBoundary: ConfigurationBoundaryScenario.SharedReferenceDiff);
    }

    [TestMethod]
    public async Task ActualConfigurationCallersRefuseStorageFailuresAndRetireDraftsAcrossRealSessionChanges()
    {
        await RunFixtureAsync(browser: true, configurationBoundary: ConfigurationBoundaryScenario.SessionStorage);
    }

    private sealed record ConfigurationBoundarySeed(string TargetApplicationId, string? PredeploymentApplicationId,
        string? BindingId, string? ApplicationSourceId, string? CommonSourceId, string? BaselineReleaseId,
        string? CandidateReleaseId, string? ApplicationPublicationId, string? FirstCommonPublicationId,
        string? SecondCommonPublicationId, IReadOnlyList<InstanceStatusFingerprint> Fingerprints);

    private static async Task<ConfigurationBoundarySeed> SeedConfigurationBoundaryAsync(ManagementHostFixture fixture, HttpClient actor,
        ConfigurationBoundaryScenario scenario, ManagementConfigurationDefinition? application, ManagementConfigurationDefinition? common)
    {
        if (scenario == ConfigurationBoundaryScenario.Predeployment)
        {
            await using NpgsqlCommand query = fixture.Source.CreateCommand(
                $"SELECT application_id FROM \"{fixture.Options.Persistence.Schema}\".management_application WHERE application='preallocated-app' AND environment='Development'");
            string target = ((Guid)(await query.ExecuteScalarAsync())!).ToString("D");
            await AssertBoundaryNoEnrollmentAsync(fixture, target);
            return new(target, target, null, null, null, null, null, null, null, null,
                await ReadInstanceStatusFingerprintsAsync(fixture));
        }

        if (scenario is not (ConfigurationBoundaryScenario.SharedReferenceDiff or ConfigurationBoundaryScenario.SessionStorage)
            || application is null || common is null)
        {
            throw new ArgumentException("The selected Configuration boundary requires its exact existing Shared/Application binding.");
        }

        ManagementConfigurationAssignmentMap initial = await ReadBoundaryMapAsync(actor, fixture.ApplicationA, ManualSharedConfiguration);
        Assert.IsNotNull(initial.DefaultReleaseId);
        using HttpResponseMessage initialRead = await actor.GetAsync("configuration/releases/" + initial.DefaultReleaseId);
        Assert.AreEqual(HttpStatusCode.OK, initialRead.StatusCode);
        ManagementConfigurationRelease original = await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationRelease>(initialRead);
        Assert.AreEqual("present", original.CommonSource.State);
        Assert.AreEqual("absent", original.ApplicationSource.State);
        string firstCommon = original.CommonSource.PublicationId!;
        string secondCommon = await ManagementConfigurationDistributionTests.PublishValue(actor, common.SourceId,
            Guid.NewGuid().ToString("D"), ManualSharedPayload);
        string appPublication = await ManagementConfigurationDistributionTests.PublishValue(actor, application.SourceId,
            Guid.NewGuid().ToString("D"), BoundaryApplicationPayload);
        Assert.AreNotEqual(firstCommon, secondCommon, "Identical payload bytes still belong to distinct immutable native publications.");

        string baseline = await CreateBoundaryReleaseAsync(actor, fixture.ApplicationA, initial.ETag,
            firstCommon, appPublication, "Deployed Shared reference");
        string candidate = await CreateBoundaryReleaseAsync(actor, fixture.ApplicationA, initial.ETag,
            secondCommon, appPublication, "Same JSON with a different Shared reference");
        using var assign = new HttpRequestMessage(HttpMethod.Put, BoundaryBindingPath(fixture.ApplicationA, ManualSharedConfiguration, "assignments"))
        {
            Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationAssignmentCommand(1,
                "replace", baseline, [], "Deploy the exact comparison baseline")))
        };
        assign.Content.Headers.ContentType = new("application/json");
        assign.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        assign.Headers.Add("If-Match", initial.ETag);
        using HttpResponseMessage assigned = await actor.SendAsync(assign);
        Assert.AreEqual(HttpStatusCode.OK, assigned.StatusCode);
        Assert.AreEqual("committed", (await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationOperationResult>(assigned)).OriginalCompletion);

        await VerifyBoundaryPublicationAsync(fixture, common.SourceId, firstCommon, ManualSharedPayload);
        await VerifyBoundaryPublicationAsync(fixture, common.SourceId, secondCommon, ManualSharedPayload);
        await VerifyBoundaryPublicationAsync(fixture, application.SourceId, appPublication, BoundaryApplicationPayload);
        return new(fixture.ApplicationA, null, application.BindingId, application.SourceId, common.SourceId, baseline, candidate,
            appPublication, firstCommon, secondCommon, await ReadInstanceStatusFingerprintsAsync(fixture));
    }

    private static async Task<string> CreateBoundaryReleaseAsync(HttpClient actor, string application, string etag,
        string common, string source, string note)
    {
        using HttpResponseMessage response = await ManagementConfigurationCatalogTests.Send(actor,
            BoundaryBindingPath(application, ManualSharedConfiguration, "releases"), Guid.NewGuid().ToString("D"), etag,
            new ManagementConfigurationReleaseCommand(1, new("present", common), new("present", source), note));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        ManagementConfigurationOperationResult result = await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationOperationResult>(response);
        Assert.AreEqual("committed", result.OriginalCompletion);
        return result.Result!.ReleaseId!;
    }

    private static async Task VerifyConfigurationBoundaryAsync(ManagementHostFixture fixture, JsonElement proof, ConfigurationBoundarySeed seed,
        ConfigurationBoundaryScenario scenario, string issuer)
    {
        Assert.AreEqual(scenario.ToString(), proof.GetProperty("scenario").GetString());
        Assert.AreEqual(seed.TargetApplicationId, proof.GetProperty("applicationId").GetString());
        if (scenario == ConfigurationBoundaryScenario.Predeployment)
        {
            await VerifyPredeploymentBoundaryAsync(fixture, proof, seed, issuer);
            return;
        }

        Assert.AreEqual(0, proof.GetProperty("effects").GetArrayLength());
        Assert.AreEqual(seed.BaselineReleaseId, proof.GetProperty("baselineReleaseId").GetString());
        Assert.AreEqual(seed.CandidateReleaseId, proof.GetProperty("candidateReleaseId").GetString());
        await VerifyBoundaryPublicationAsync(fixture, seed.CommonSourceId!, seed.FirstCommonPublicationId!, ManualSharedPayload);
        await VerifyBoundaryPublicationAsync(fixture, seed.CommonSourceId!, seed.SecondCommonPublicationId!, ManualSharedPayload);
        await VerifyBoundaryPublicationAsync(fixture, seed.ApplicationSourceId!, seed.ApplicationPublicationId!, BoundaryApplicationPayload);
        await VerifySavePairAsync(fixture, seed.BindingId!, seed.BaselineReleaseId!, seed.FirstCommonPublicationId, seed.ApplicationPublicationId);
        await VerifySavePairAsync(fixture, seed.BindingId!, seed.CandidateReleaseId!, seed.SecondCommonPublicationId, seed.ApplicationPublicationId);
        if (scenario == ConfigurationBoundaryScenario.SharedReferenceDiff)
        {
            Assert.IsTrue(proof.GetProperty("equalDisplayedJson").GetBoolean());
            Assert.IsTrue(proof.GetProperty("sharedReferenceNotice").GetBoolean());
        }
        else
        {
            CollectionAssert.AreEqual(new[] { "quota", "unverified" }, proof.GetProperty("blockedSaveModes").EnumerateArray().Select(value => value.GetString()!).ToArray());
            CollectionAssert.AreEqual(new[] { "quota", "unverified" }, proof.GetProperty("blockedDeployModes").EnumerateArray().Select(value => value.GetString()!).ToArray());
            Assert.IsTrue(proof.GetProperty("changedOperatorRetiredDraft").GetBoolean());
            Assert.IsTrue(proof.GetProperty("unauthenticatedReadRetiredDraft").GetBoolean());
        }

        IReadOnlyList<InstanceStatusFingerprint> after = await ReadInstanceStatusFingerprintsAsync(fixture);
        Assert.AreEqual(seed.Fingerprints.Count, after.Count);
        for (int index = 0; index < after.Count; index++)
        {
            Assert.AreEqual(seed.Fingerprints[index], after[index], "This browser boundary must not change " + after[index].Name + ".");
        }
    }

    private static async Task VerifyPredeploymentBoundaryAsync(ManagementHostFixture fixture, JsonElement proof,
        ConfigurationBoundarySeed seed, string issuer)
    {
        Assert.AreEqual(BoundaryPredeploymentConfiguration, proof.GetProperty("configurationId").GetString());
        Assert.AreEqual("1", proof.GetProperty("schemaVersion").GetString());
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(BoundaryPredeploymentPayload), proof.GetProperty("sha256").GetString());
        CollectionAssert.AreEqual(new[] { "quota", "unverified" }, proof.GetProperty("blockedCreationModes").EnumerateArray().Select(value => value.GetString()!).ToArray());
        string source = proof.GetProperty("sourceId").GetString()!;
        string binding = proof.GetProperty("bindingId").GetString()!;
        string publication = proof.GetProperty("publicationId").GetString()!;
        string release = proof.GetProperty("releaseId").GetString()!;
        await AssertBoundaryNoEnrollmentAsync(fixture, seed.TargetApplicationId);
        await VerifySavePublicationAsync(fixture, source, publication, BoundaryPredeploymentPayload);
        await VerifySavePairAsync(fixture, binding, release, null, publication);
        await AssertSaveMapAsync(fixture, binding, revision: 2, generation: 1, release: release);
        await AssertSaveReleaseCountAsync(fixture, binding, 1);
        await using (NpgsqlCommand query = fixture.Source.CreateCommand($"""
            SELECT s.application_id,b.predeployment_created,b.manifest_managed,d.configuration_id,b.host_scope,b.binding_id,d.schema_version
            FROM "{fixture.Options.Persistence.Schema}".configuration_source s
            JOIN "{fixture.Options.Persistence.Schema}".configuration_binding b ON b.binding_id=s.binding_id AND b.application_id=s.application_id
            JOIN "{fixture.Options.Persistence.Schema}".configuration_definition d ON d.definition_id=b.definition_id AND d.definition_id=s.definition_id
            WHERE s.source_id=@source
            """))
        {
            query.Parameters.AddWithValue("source", Guid.Parse(source));
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(seed.TargetApplicationId), reader.GetGuid(0));
            Assert.IsTrue(reader.GetBoolean(1));
            Assert.IsFalse(reader.GetBoolean(2));
            Assert.AreEqual(BoundaryPredeploymentConfiguration, reader.GetString(3));
            Assert.AreEqual("preallocated-app", reader.GetString(4));
            Assert.AreEqual(Guid.Parse(binding), reader.GetGuid(5));
            Assert.AreEqual(1L, reader.GetInt64(6));
            Assert.IsFalse(await reader.ReadAsync());
        }

        JsonElement[] effects = proof.GetProperty("effects").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { "management.configuration.create", "management.configuration.source.publish",
            "management.configuration.release.create", "management.configuration.assignment.replace" },
            effects.Select(value => value.GetProperty("family").GetString()!).ToArray());
        await VerifySaveOriginalsAsync(fixture, issuer,
        [
            new(effects[0].GetProperty("key").GetString()!, "management.configuration.create", seed.TargetApplicationId, "configurationCreated", source),
            new(effects[1].GetProperty("key").GetString()!, "management.configuration.source.publish", source, "sourcePublished", publication),
            new(effects[2].GetProperty("key").GetString()!, "management.configuration.release.create", binding, "releaseCreated", release),
            new(effects[3].GetProperty("key").GetString()!, "management.configuration.assignment.replace", binding, "assignmentsReplaced", release)
        ]);
        IReadOnlyList<InstanceStatusFingerprint> after = await ReadInstanceStatusFingerprintsAsync(fixture);
        foreach (string name in new[] { "configuration-originals", "configuration-audit" })
        {
            Assert.AreEqual(seed.Fingerprints.Single(value => value.Name == name).Rows + 4, after.Single(value => value.Name == name).Rows,
                "Storage failures cannot create additional Configuration originals or Audit records.");
        }
    }

    private static async Task VerifyBoundaryPublicationAsync(ManagementHostFixture fixture, string source, string publication, string payload)
    {
        await using NpgsqlCommand query = fixture.Source.CreateCommand(
            $"SELECT source_id,payload,sha256 FROM \"{fixture.Options.Persistence.Schema}\".configuration_publication WHERE publication_id=@id");
        query.Parameters.AddWithValue("id", Guid.Parse(publication));
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(Guid.Parse(source), reader.GetGuid(0));
        Assert.AreEqual(payload, new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(1)));
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(payload), reader.GetString(2));
        Assert.IsFalse(await reader.ReadAsync());
    }

    private static async Task AssertBoundaryNoEnrollmentAsync(ManagementHostFixture fixture, string application)
    {
        foreach (string table in new[] { "management_registration", "configuration_session" })
        {
            await using NpgsqlCommand query = fixture.Source.CreateCommand(
                $"SELECT count(*) FROM \"{fixture.Options.Persistence.Schema}\".\"{table}\" WHERE application_id=@application");
            query.Parameters.AddWithValue("application", Guid.Parse(application));
            Assert.AreEqual(0L, (long)(await query.ExecuteScalarAsync())!, "The portal must not create enrollment in " + table + ".");
        }
    }

    private static async Task<ManagementConfigurationAssignmentMap> ReadBoundaryMapAsync(HttpClient actor, string application, string configuration)
    {
        using HttpResponseMessage response = await actor.GetAsync(BoundaryBindingPath(application, configuration, "assignments"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationAssignmentMap>(response);
    }

    private static string BoundaryBindingPath(string application, string configuration, string suffix)
        => "applications/" + application + "/configuration/" + configuration + "/" + suffix + "?schemaVersion=1";
}
