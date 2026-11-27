using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management;
using CIS.Management.Wire;
using CIS.Operations;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    private const string RecoveryConfiguration = "portal.partial.save";
    private const string RecoveryPayload = "{\n"
        + "  \"Greeting\": \"Recovered Save · 日本語 🌍 e\\u0301\",\n"
        + "  \"Enabled\": false,\n"
        + "  \"Count\": 9007199254740993\n"
        + "}";
    private const string ConflictSavePayload = """{"Enabled":false,"Features":{"CountrySearch":true,"Export":true},"Large":9007199254740993,"Unicode":"日本語 🌍 e\u0301"}""";

    [TestMethod]
    public async Task ActualNewConfigurationSaveRecoversEachOriginalWithoutRepeatingSetupPublicationOrRelease()
    {
        await RunFixtureAsync(browser: true, newSaveRecovery: true);
    }

    [TestMethod]
    public async Task ActualVersionSaveReconcilesAClosedReleaseConflictWithoutRepublishingItsFrozenJson()
    {
        await RunFixtureAsync(browser: true, releaseConflict: true);
    }

    private sealed record SaveOriginal(string Key, string Family, string Resource, string? Kind, string? ResultId);

    private static async Task VerifyConfigurationSaveRecoveryAsync(ManagementHostFixture fixture, JsonElement proof, string issuer)
    {
        Assert.AreEqual(fixture.ApplicationA, proof.GetProperty("applicationId").GetString());
        Assert.AreEqual(RecoveryConfiguration, proof.GetProperty("configurationId").GetString());
        string source = proof.GetProperty("sourceId").GetString()!;
        string binding = proof.GetProperty("bindingId").GetString()!;
        string publication = proof.GetProperty("publicationId").GetString()!;
        string release = proof.GetProperty("releaseId").GetString()!;
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(RecoveryPayload), proof.GetProperty("sha256").GetString());
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT s.application_id,s.binding_id,d.configuration_id,d.schema_version,b.predeployment_created,b.manifest_managed
            FROM "{fixture.Options.Persistence.Schema}".configuration_source s
            JOIN "{fixture.Options.Persistence.Schema}".configuration_binding b ON b.binding_id=s.binding_id AND b.application_id=s.application_id
            JOIN "{fixture.Options.Persistence.Schema}".configuration_definition d ON d.definition_id=b.definition_id AND d.definition_id=s.definition_id
            WHERE s.source_id=@source
            """))
        {
            command.Parameters.AddWithValue("source", Guid.Parse(source));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(fixture.ApplicationA), reader.GetGuid(0));
            Assert.AreEqual(Guid.Parse(binding), reader.GetGuid(1));
            Assert.AreEqual(RecoveryConfiguration, reader.GetString(2));
            Assert.AreEqual(1L, reader.GetInt64(3));
            Assert.IsTrue(reader.GetBoolean(4));
            Assert.IsFalse(reader.GetBoolean(5));
            Assert.IsFalse(await reader.ReadAsync());
        }
        await VerifySavePublicationAsync(fixture, source, publication, RecoveryPayload);
        await VerifySavePairAsync(fixture, binding, release, null, publication);
        await AssertSaveMapAsync(fixture, binding, revision: 1, generation: null, release: null);
        await AssertSaveReleaseCountAsync(fixture, binding, 1);
        JsonElement[] effects = proof.GetProperty("effects").EnumerateArray().ToArray();
        Assert.AreEqual(3, effects.Length);
        CollectionAssert.AreEqual(new[] { "management.configuration.create", "management.configuration.source.publish", "management.configuration.release.create" },
            effects.Select(item => item.GetProperty("family").GetString()!).ToArray());
        await VerifySaveOriginalsAsync(fixture, issuer,
        [
            new(effects[0].GetProperty("key").GetString()!, "management.configuration.create", fixture.ApplicationA, "configurationCreated", source),
            new(effects[1].GetProperty("key").GetString()!, "management.configuration.source.publish", source, "sourcePublished", publication),
            new(effects[2].GetProperty("key").GetString()!, "management.configuration.release.create", binding, "releaseCreated", release)
        ]);
    }

    private static async Task VerifyConfigurationReleaseConflictAsync(ManagementHostFixture fixture, JsonElement proof,
        ManagementConfigurationDefinition application, ManagementConfigurationDefinition common, string issuer)
    {
        Assert.AreEqual(fixture.ApplicationA, proof.GetProperty("applicationId").GetString());
        Assert.AreEqual(ManualSharedConfiguration, proof.GetProperty("configurationId").GetString());
        Assert.AreEqual(application.SourceId, proof.GetProperty("sourceId").GetString());
        Assert.AreEqual(application.BindingId, proof.GetProperty("bindingId").GetString());
        string publication = proof.GetProperty("publicationId").GetString()!;
        string sharedPublication = proof.GetProperty("commonPublicationId").GetString()!;
        string release = proof.GetProperty("releaseId").GetString()!;
        string originalRelease = proof.GetProperty("initialReleaseId").GetString()!;
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(ConflictSavePayload), proof.GetProperty("sha256").GetString());
        await VerifySavePublicationAsync(fixture, application.SourceId, publication, ConflictSavePayload);
        await VerifySavePublicationAsync(fixture, common.SourceId, sharedPublication, ManualSharedPayload);
        await VerifySavePairAsync(fixture, application.BindingId!, originalRelease, sharedPublication, null);
        await VerifySavePairAsync(fixture, application.BindingId!, release, sharedPublication, publication);
        await AssertSaveReleaseCountAsync(fixture, application.BindingId!, 2);
        await AssertSaveMapAsync(fixture, application.BindingId!, revision: 3, generation: 2, release: originalRelease);
        await VerifySaveOriginalsAsync(fixture, issuer,
        [
            new(proof.GetProperty("publicationKey").GetString()!, "management.configuration.source.publish", application.SourceId, "sourcePublished", publication),
            new(proof.GetProperty("conflictKey").GetString()!, "management.configuration.release.create", application.BindingId!, null, null),
            new(proof.GetProperty("competingKey").GetString()!, "management.configuration.assignment.replace", application.BindingId!, "assignmentsReplaced", originalRelease),
            new(proof.GetProperty("releaseKey").GetString()!, "management.configuration.release.create", application.BindingId!, "releaseCreated", release)
        ]);
    }

    private static async Task VerifySavePublicationAsync(ManagementHostFixture fixture, string source, string publication, string raw)
    {
        await using NpgsqlCommand command = fixture.Source.CreateCommand(
            $"SELECT publication_id,payload,sha256 FROM \"{fixture.Options.Persistence.Schema}\".configuration_publication WHERE source_id=@source LIMIT 2");
        command.Parameters.AddWithValue("source", Guid.Parse(source));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(Guid.Parse(publication), reader.GetGuid(0));
        Assert.AreEqual(raw, new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(1)));
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(raw), reader.GetString(2));
        Assert.IsFalse(await reader.ReadAsync(), "Recovery must not create another source publication.");
    }

    private static async Task VerifySavePairAsync(ManagementHostFixture fixture, string binding, string release, string? common, string? application)
    {
        await using NpgsqlCommand command = fixture.Source.CreateCommand(
            $"SELECT binding_id,common_publication_id,application_publication_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_release WHERE release_id=@release");
        command.Parameters.AddWithValue("release", Guid.Parse(release));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(Guid.Parse(binding), reader.GetGuid(0));
        if (common is null)
        {
            Assert.IsTrue(reader.IsDBNull(1));
        }
        else
        {
            Assert.AreEqual(Guid.Parse(common), reader.GetGuid(1));
        }
        if (application is null)
        {
            Assert.IsTrue(reader.IsDBNull(2));
        }
        else
        {
            Assert.AreEqual(Guid.Parse(application), reader.GetGuid(2));
        }
        Assert.IsFalse(await reader.ReadAsync());
    }

    private static async Task AssertSaveReleaseCountAsync(ManagementHostFixture fixture, string binding, long expected)
    {
        await using NpgsqlCommand command = fixture.Source.CreateCommand(
            $"SELECT count(*) FROM \"{fixture.Options.Persistence.Schema}\".configuration_release WHERE binding_id=@binding");
        command.Parameters.AddWithValue("binding", Guid.Parse(binding));
        Assert.AreEqual(expected, (long)(await command.ExecuteScalarAsync())!);
    }

    private static async Task AssertSaveMapAsync(ManagementHostFixture fixture, string binding, long revision, long? generation, string? release)
    {
        await using NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT revision,generation,default_release_id,
                (SELECT count(*) FROM "{fixture.Options.Persistence.Schema}".configuration_override WHERE binding_id=@binding)
            FROM "{fixture.Options.Persistence.Schema}".configuration_assignment WHERE binding_id=@binding
            """);
        command.Parameters.AddWithValue("binding", Guid.Parse(binding));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(revision, reader.GetInt64(0));
        if (generation is null)
        {
            Assert.IsTrue(reader.IsDBNull(1));
        }
        else
        {
            Assert.AreEqual(generation.Value, reader.GetInt64(1));
        }
        if (release is null)
        {
            Assert.IsTrue(reader.IsDBNull(2));
        }
        else
        {
            Assert.AreEqual(Guid.Parse(release), reader.GetGuid(2));
        }
        Assert.AreEqual(0L, reader.GetInt64(3));
        Assert.IsFalse(await reader.ReadAsync());
    }

    private static async Task VerifySaveOriginalsAsync(ManagementHostFixture fixture, string issuer, SaveOriginal[] originals)
    {
        Assert.AreEqual(originals.Length, originals.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(originals.All(row => Guid.TryParseExact(row.Key, "D", out _)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT scope,operation_name,idempotency_key,finalized,business_decision,result_payload,error_code
            FROM "{fixture.Options.OperationsSchema}".operation_journal WHERE idempotency_key=ANY(@keys)
            """))
        {
            command.Parameters.AddWithValue("keys", originals.Select(row => row.Key).ToArray());
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                SaveOriginal row = originals.Single(item => item.Key == reader.GetString(2));
                Assert.IsTrue(seen.Add(row.Key));
                Assert.AreEqual(row.Family, reader.GetString(1));
                Assert.IsTrue(reader.GetBoolean(3));
                string prefix = "management.configuration.v1/" + row.Family + "/" + row.Resource + "/management.actor.v1.";
                string scope = reader.GetString(0);
                Assert.IsTrue(scope.StartsWith(prefix, StringComparison.Ordinal));
                Assert.IsTrue(ManagementConfigurationWire.IsSha256(scope[prefix.Length..]));
                Assert.AreEqual((short)(row.Kind is null ? OperationBusinessDecision.Rejected : OperationBusinessDecision.Succeeded), reader.GetInt16(4));
                if (row.Kind is null)
                {
                    Assert.IsTrue(reader.IsDBNull(5));
                    Assert.AreEqual("management.configuration.precondition_failed", reader.GetString(6));
                }
                else
                {
                    byte[] payload = reader.GetFieldValue<byte[]>(5);
                    ManagementConfigurationDecision decision = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDecision>(payload)!;
                    Assert.AreEqual(row.Kind, decision.Kind);
                    Assert.AreEqual("applied", decision.Decision);
                    string? resultId = row.Kind switch
                    {
                        "configurationCreated" => decision.SourceId,
                        "sourcePublished" => decision.PublicationId,
                        "releaseCreated" => decision.ReleaseId,
                        "assignmentsReplaced" => decision.Assignment?.DefaultReleaseId,
                        _ => throw new AssertFailedException("Only the four selected native operation kinds belong in this fixture.")
                    };
                    Assert.AreEqual(row.ResultId, resultId);
                    Assert.IsFalse(Encoding.UTF8.GetString(payload).Contains("9007199254740993", StringComparison.Ordinal));
                }
            }
        }
        Assert.AreEqual(originals.Length, seen.Count);
        var codec = new AuditRecordCodec();
        var auditKeys = new HashSet<string>(StringComparer.Ordinal);
        await using NpgsqlCommand audits = fixture.Source.CreateCommand(
            $"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY record_id LIMIT 257");
        await using NpgsqlDataReader records = await audits.ExecuteReaderAsync();
        int count = 0;
        while (await records.ReadAsync())
        {
            Assert.IsTrue(++count <= 256);
            byte[] envelopeBytes = records.GetFieldValue<byte[]>(0);
            using JsonDocument envelope = JsonDocument.Parse(envelopeBytes);
            Assert.IsTrue(codec.TryDecode(Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText()),
                out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
            SaveOriginal? row = originals.SingleOrDefault(item => item.Key == record!.OperationReference?.Key);
            if (row is null)
            {
                continue;
            }
            Assert.IsTrue(auditKeys.Add(row.Key));
            Assert.AreEqual(row.Family, record!.OperationReference!.OperationName);
            Assert.AreEqual(row.Kind is null ? AuditOutcome.Rejected : AuditOutcome.Succeeded, record.Outcome);
            Assert.AreEqual(issuer, record.Actor.Subject!.Issuer);
            Assert.AreEqual(OperatorA, record.Actor.Subject.Identifier);
            Assert.AreEqual(row.Resource, record.Target!.ResourceIdentifier);
            Assert.IsFalse(Encoding.UTF8.GetString(envelopeBytes).Contains("9007199254740993", StringComparison.Ordinal));
        }
        Assert.AreEqual(originals.Length, auditKeys.Count);
    }
}
