using System.Globalization;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management;
using CIS.Management.Wire;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    [TestMethod]
    public async Task ActualVersionDeployClearsPinsConflictsAndRecoversCommittedDeliveryLoss()
    {
        await RunFixtureAsync(browser: true, deployControls: true);
    }

    [TestMethod]
    public async Task ActualMetadataOnlyVersionDeploysWhileReadonlyOwnSavedVersionCannot()
    {
        await RunFixtureAsync(browser: true, metadataOnlyDeploy: true);
    }

    private sealed record ConfigurationDeployMapState(long Revision, long? Generation, string? DefaultReleaseId, string OverridesJson);
    private sealed record ConfigurationDeployPublication(string SourceId, string PublicationId, string Payload);
    private sealed record ConfigurationDeployRelease(string BindingId, string ReleaseId, string? CommonPublicationId, string ApplicationPublicationId);
    private sealed record ConfigurationDeploySeed(string BaselineReleaseId, string CandidateReleaseId, string? FinanceReleaseId,
        string ReadonlyReleaseId, string AssignmentKey, ConfigurationDeployMapState InitialMap, ConfigurationDeployMapState ReadonlyMap,
        IReadOnlyList<ConfigurationDeployPublication> Publications, IReadOnlyList<ConfigurationDeployRelease> Releases,
        long PublicationCount, long ReleaseCount, bool MetadataOnly);

    private static async Task<ConfigurationDeploySeed> SeedConfigurationDeployAsync(ManagementHostFixture fixture, HttpClient actor,
        ManagementConfigurationDefinition application, ManagementConfigurationDefinition common,
        ManagementConfigurationDefinition readonlyApplication, bool metadataOnly)
    {
        var publications = new List<ConfigurationDeployPublication>();
        async Task<string> Publish(ManagementConfigurationDefinition source, string payload)
        {
            string id = await ManagementConfigurationDistributionTests.PublishValue(actor, source.SourceId, Guid.NewGuid().ToString("D"), payload);
            publications.Add(new(source.SourceId, id, payload));
            return id;
        }

        string shared = await Publish(common, """{"Greeting":"Shared deployment base"}""");
        string first = await Publish(application, """{"Enabled":false,"Count":1}""");
        string second = await Publish(application, """{"Enabled":false,"Count":2}""");
        string readonlyPublication = await Publish(readonlyApplication, """{"Greeting":"READONLY_PRIVATE_CONFIGURATION","Enabled":false,"Count":77}""");
        ManagementConfigurationAssignmentMap empty = await ManagementConfigurationDistributionTests.Map(actor, fixture.ApplicationA);
        Assert.IsNull(empty.DefaultReleaseId);
        Assert.IsEmpty(empty.Overrides);
        var releases = new List<ConfigurationDeployRelease>();
        async Task<string> Release(string applicationId, string bindingId, string etag, string? commonPublication, string publication, string note)
        {
            ManagementConfigurationOperationResult created = await ManagementConfigurationDistributionTests.CreateRelease(actor, applicationId, etag,
                Guid.NewGuid().ToString("D"), new(1, commonPublication is null ? new("absent") : new("present", commonPublication), new("present", publication), note));
            Assert.AreEqual("committed", created.OriginalCompletion);
            string id = created.Result!.ReleaseId!;
            releases.Add(new(bindingId, id, commonPublication, publication));
            return id;
        }

        string baseline = await Release(fixture.ApplicationA, application.BindingId!, empty.ETag, shared, first, "Baseline deployment version");
        string candidate = await Release(fixture.ApplicationA, application.BindingId!, empty.ETag, shared, second, "Candidate deployment version");
        string? finance = metadataOnly ? null : await Release(fixture.ApplicationA, application.BindingId!, empty.ETag, shared, first, "Finance retained version");
        ManagementConfigurationAssignmentMap readonlyEmpty = await ManagementConfigurationDistributionTests.Map(actor, fixture.ApplicationB);
        string readonlyRelease = await Release(fixture.ApplicationB, readonlyApplication.BindingId!, readonlyEmpty.ETag, null, readonlyPublication, "Readonly application saved version");
        string assignmentKey = Guid.NewGuid().ToString("D");
        ManagementConfigurationOverride[] overrides = metadataOnly ? [] : [new("canary", candidate), new("finance", finance!)];
        ManagementConfigurationOperationResult assigned = await ManagementConfigurationDistributionTests.Assign(actor, fixture.ApplicationA, empty.ETag,
            assignmentKey, new(1, "replace", baseline, overrides, "Initial complete deployment map"));
        Assert.AreEqual("committed", assigned.OriginalCompletion);
        return new(baseline, candidate, finance, readonlyRelease, assignmentKey,
            await ReadConfigurationDeployMapAsync(fixture, application.BindingId!),
            await ReadConfigurationDeployMapAsync(fixture, readonlyApplication.BindingId!), publications.AsReadOnly(), releases.AsReadOnly(),
            await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"),
            await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"), metadataOnly);
    }

    private static async Task VerifyConfigurationDeployEffectsAsync(ManagementHostFixture fixture, JsonElement proof, ConfigurationDeploySeed seed,
        ManagementConfigurationDefinition application, ManagementConfigurationDefinition readonlyApplication, string issuer)
    {
        Assert.AreEqual(seed.MetadataOnly, proof.GetProperty("metadataOnly").GetBoolean());
        string[] applied = proof.GetProperty("appliedKeys").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.AreEqual(seed.MetadataOnly ? 1 : 3, applied.Length);
        string? conflict = seed.MetadataOnly ? null : proof.GetProperty("conflictKey").GetString();
        string[] originals = conflict is null ? applied : [.. applied, conflict];
        Assert.AreEqual(originals.Length, originals.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(originals.All(key => Guid.TryParseExact(key, "D", out _)));
        string selected = seed.MetadataOnly ? seed.CandidateReleaseId : seed.BaselineReleaseId;
        long generation = seed.InitialMap.Generation!.Value + applied.Length;
        Assert.AreEqual(generation.ToString(CultureInfo.InvariantCulture), proof.GetProperty("finalGeneration").GetString());
        Assert.AreEqual(selected, proof.GetProperty("finalReleaseId").GetString());
        Assert.AreEqual(new ConfigurationDeployMapState(seed.InitialMap.Revision + applied.Length, generation, selected, "[]"),
            await ReadConfigurationDeployMapAsync(fixture, application.BindingId!));
        Assert.AreEqual(seed.ReadonlyMap, await ReadConfigurationDeployMapAsync(fixture, readonlyApplication.BindingId!), "Readonly target remains exactly unchanged.");
        Assert.AreEqual(seed.PublicationCount, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        Assert.AreEqual(seed.ReleaseCount, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));

        foreach (ConfigurationDeployPublication publication in seed.Publications)
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT source_id,payload,sha256 FROM \"{fixture.Options.Persistence.Schema}\".configuration_publication WHERE publication_id=@id");
            command.Parameters.AddWithValue("id", Guid.Parse(publication.PublicationId));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(publication.SourceId), reader.GetGuid(0));
            Assert.AreEqual(publication.Payload, new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(1)));
            Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(publication.Payload), reader.GetString(2));
            Assert.IsFalse(await reader.ReadAsync());
        }
        foreach (ConfigurationDeployRelease release in seed.Releases)
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT binding_id,common_publication_id,application_publication_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_release WHERE release_id=@id");
            command.Parameters.AddWithValue("id", Guid.Parse(release.ReleaseId));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(release.BindingId), reader.GetGuid(0));
            if (release.CommonPublicationId is null)
            {
                Assert.IsTrue(reader.IsDBNull(1));
            }
            else
            {
                Assert.AreEqual(Guid.Parse(release.CommonPublicationId), reader.GetGuid(1));
            }
            Assert.AreEqual(Guid.Parse(release.ApplicationPublicationId), reader.GetGuid(2));
            Assert.IsFalse(await reader.ReadAsync());
        }
        foreach (string key in originals)
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT scope,operation_name,finalized,result_payload,error_code FROM \"{fixture.Options.OperationsSchema}\".operation_journal WHERE idempotency_key=@key");
            command.Parameters.AddWithValue("key", key);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            const string family = "management.configuration.assignment.replace";
            string prefix = "management.configuration.v1/" + family + "/" + application.BindingId + "/management.actor.v1.";
            Assert.IsTrue(reader.GetString(0).StartsWith(prefix, StringComparison.Ordinal));
            Assert.IsTrue(ManagementConfigurationWire.IsSha256(reader.GetString(0)[prefix.Length..]));
            Assert.AreEqual(family, reader.GetString(1));
            Assert.IsTrue(reader.GetBoolean(2));
            if (key == conflict)
            {
                Assert.IsTrue(reader.IsDBNull(3));
                Assert.AreEqual("management.configuration.precondition_failed", reader.GetString(4));
            }
            else
            {
                ManagementConfigurationDecision decision = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDecision>(reader.GetFieldValue<byte[]>(3))!;
                Assert.AreEqual("assignmentsReplaced", decision.Kind);
                Assert.AreEqual("applied", decision.Decision);
                Assert.AreEqual(application.ApplicationId, decision.ApplicationId);
                Assert.IsTrue(reader.IsDBNull(4));
            }
            Assert.IsFalse(await reader.ReadAsync(), "Each original has one exact journal claim.");
        }
        if (seed.MetadataOnly)
        {
            string denied = proof.GetProperty("readonlyDeniedKey").GetString()!;
            Assert.IsTrue(Guid.TryParseExact(denied, "D", out _));
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT count(*) FROM \"{fixture.Options.OperationsSchema}\".operation_journal WHERE idempotency_key=@key");
            command.Parameters.AddWithValue("key", denied);
            Assert.AreEqual(0L, (long)(await command.ExecuteScalarAsync())!, "Authorization denial cannot create an original claim.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var codec = new AuditRecordCodec();
        int assignmentAudits = 0;
        await using NpgsqlCommand audits = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY created_at,record_id LIMIT 257");
        await using NpgsqlDataReader auditReader = await audits.ExecuteReaderAsync();
        int rows = 0;
        while (await auditReader.ReadAsync())
        {
            Assert.IsTrue(++rows <= 256);
            using JsonDocument envelope = JsonDocument.Parse(auditReader.GetFieldValue<byte[]>(0));
            Assert.IsTrue(codec.TryDecode(Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText()), out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
            if (record!.OperationReference?.OperationName != "management.configuration.assignment.replace")
            {
                continue;
            }
            assignmentAudits++;
            string key = record.OperationReference.Key;
            if (key == seed.AssignmentKey)
            {
                continue;
            }
            Assert.IsTrue(originals.Contains(key, StringComparer.Ordinal) && seen.Add(key));
            Assert.AreEqual(key == conflict ? AuditOutcome.Rejected : AuditOutcome.Succeeded, record.Outcome);
            Assert.AreEqual(issuer, record.Actor.Subject!.Issuer);
            Assert.AreEqual(OperatorA, record.Actor.Subject.Identifier);
            Assert.AreEqual(application.BindingId, record.Target!.ResourceIdentifier);
        }
        Assert.AreEqual(originals.Length, seen.Count);
        Assert.AreEqual(originals.Length + 1, assignmentAudits, "One seed plus one Audit per actual original; recovery and readonly denial add none.");
    }

    private static async Task<ConfigurationDeployMapState> ReadConfigurationDeployMapAsync(ManagementHostFixture fixture, string binding)
    {
        long revision;
        long? generation;
        string? selected;
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT revision,generation,default_release_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_assignment WHERE binding_id=@id"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(binding));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            revision = reader.GetInt64(0);
            generation = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            selected = reader.IsDBNull(2) ? null : reader.GetGuid(2).ToString("D");
            Assert.IsFalse(await reader.ReadAsync());
        }
        var overrides = new List<ManagementConfigurationOverride>();
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT cohort_id,release_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_override WHERE binding_id=@id ORDER BY cohort_id COLLATE \"C\" LIMIT 17"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(binding));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                overrides.Add(new(reader.GetString(0), reader.GetGuid(1).ToString("D")));
            }
            Assert.IsTrue(overrides.Count <= 16);
        }
        return new(revision, generation, selected, JsonSerializer.Serialize(overrides));
    }
}
