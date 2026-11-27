using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management;
using CIS.Management.Wire;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    private const string FirstVersionPayload = """{"Enabled":false,"Features":{"CountrySearch":true,"Export":false},"Large":9007199254740993,"Unicode":"日本語 🌍 e\u0301"}""";
    private const string SecondVersionPayload = """{"Enabled":false,"Features":{"CountrySearch":true,"Export":true},"Large":9007199254740993,"Unicode":"日本語 🌍 e\u0301"}""";
    private const string NewVersionPayload = """{"Greeting":"Version page","Large":9007199254740993,"Enabled":false}""";

    [TestMethod]
    public async Task ActualVersionPageSavesWithoutDeployingComparesDeployedAndRollsBackExactPairs()
    {
        await RunFixtureAsync(browser: true, versionWorkflow: true);
    }

    private static async Task VerifyVersionWorkflowEffectsAsync(ManagementHostFixture fixture, JsonElement proof,
        ManagementConfigurationDefinition application, ManagementConfigurationDefinition common, string issuer)
    {
        Assert.AreEqual(fixture.ApplicationA, proof.GetProperty("applicationId").GetString());
        Assert.AreEqual(application.SourceId, proof.GetProperty("sourceId").GetString());
        Assert.AreEqual(application.BindingId, proof.GetProperty("bindingId").GetString());
        string commonPublication = proof.GetProperty("commonPublicationId").GetString()!;
        string firstPublication = proof.GetProperty("firstPublicationId").GetString()!;
        string secondPublication = proof.GetProperty("secondPublicationId").GetString()!;
        string firstRelease = proof.GetProperty("firstReleaseId").GetString()!;
        string secondRelease = proof.GetProperty("secondReleaseId").GetString()!;
        string newSource = proof.GetProperty("newSourceId").GetString()!;
        string newBinding = proof.GetProperty("newBindingId").GetString()!;
        string newPublication = proof.GetProperty("newPublicationId").GetString()!;
        string newRelease = proof.GetProperty("newReleaseId").GetString()!;
        Assert.AreNotEqual(firstPublication, secondPublication);
        Assert.AreNotEqual(firstRelease, secondRelease);
        Assert.AreEqual("3", proof.GetProperty("finalGeneration").GetString());
        Assert.IsTrue(proof.GetProperty("saveLeftDeploymentUnchanged").GetBoolean());
        Assert.IsTrue(proof.GetProperty("comparedActualDeployed").GetBoolean());
        Assert.IsTrue(proof.GetProperty("newConfigurationNotDeployed").GetBoolean());

        foreach ((string id, string source, string payload) in new[]
        {
            (commonPublication, common.SourceId, ManualSharedPayload),
            (firstPublication, application.SourceId, FirstVersionPayload),
            (secondPublication, application.SourceId, SecondVersionPayload),
            (newPublication, newSource, NewVersionPayload)
        })
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT source_id,payload,sha256 FROM \"{fixture.Options.Persistence.Schema}\".configuration_publication WHERE publication_id=@id");
            command.Parameters.AddWithValue("id", Guid.Parse(id));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(source), reader.GetGuid(0));
            Assert.AreEqual(payload, new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(1)));
            Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(payload), reader.GetString(2));
            Assert.IsFalse(await reader.ReadAsync());
        }

        foreach ((string id, string binding, string? shared, string publication) in new[]
        {
            (firstRelease, application.BindingId!, (string?)commonPublication, firstPublication),
            (secondRelease, application.BindingId!, (string?)commonPublication, secondPublication),
            (newRelease, newBinding, (string?)null, newPublication)
        })
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT binding_id,common_publication_id,application_publication_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_release WHERE release_id=@id");
            command.Parameters.AddWithValue("id", Guid.Parse(id));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(binding), reader.GetGuid(0));
            if (shared is null)
            {
                Assert.IsTrue(reader.IsDBNull(1));
            }
            else
            {
                Assert.AreEqual(Guid.Parse(shared), reader.GetGuid(1));
            }
            Assert.AreEqual(Guid.Parse(publication), reader.GetGuid(2));
            Assert.IsFalse(await reader.ReadAsync());
        }

        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT generation,default_release_id,
                (SELECT count(*) FROM "{fixture.Options.Persistence.Schema}".configuration_override WHERE binding_id=@id)
            FROM "{fixture.Options.Persistence.Schema}".configuration_assignment WHERE binding_id=@id
            """))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(application.BindingId!));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(3L, reader.GetInt64(0));
            Assert.AreEqual(Guid.Parse(firstRelease), reader.GetGuid(1), "Rollback selects the old immutable pair without rewriting it.");
            Assert.AreEqual(0L, reader.GetInt64(2));
            Assert.IsFalse(await reader.ReadAsync());
        }
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT generation,default_release_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_assignment WHERE binding_id=@id"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(newBinding));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.IsTrue(reader.IsDBNull(0));
            Assert.IsTrue(reader.IsDBNull(1));
        }
        Assert.AreEqual(5L, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        Assert.AreEqual(5L, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));

        JsonElement effects = proof.GetProperty("effects");
        Assert.AreEqual(9, effects.GetArrayLength(), "Three publications, three releases, two deployments and one native setup.");
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonElement effect in effects.EnumerateArray())
        {
            string key = effect.GetProperty("key").GetString()!;
            string family = effect.GetProperty("family").GetString()!;
            Assert.IsTrue(Guid.TryParseExact(key, "D", out _));
            Assert.IsTrue(keys.TryAdd(key, family));
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT operation_name,finalized,result_payload FROM \"{fixture.Options.OperationsSchema}\".operation_journal WHERE idempotency_key=@key");
            command.Parameters.AddWithValue("key", key);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(family, reader.GetString(0));
            Assert.IsTrue(reader.GetBoolean(1));
            ManagementConfigurationDecision decision = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDecision>(reader.GetFieldValue<byte[]>(2))!;
            Assert.AreEqual("applied", decision.Decision);
            Assert.IsFalse(await reader.ReadAsync());
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var codec = new AuditRecordCodec();
        await using NpgsqlCommand auditCommand = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY created_at,record_id LIMIT 257");
        await using NpgsqlDataReader audits = await auditCommand.ExecuteReaderAsync();
        while (await audits.ReadAsync())
        {
            using JsonDocument envelope = JsonDocument.Parse(audits.GetFieldValue<byte[]>(0));
            Assert.IsTrue(codec.TryDecode(Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText()), out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
            string? key = record!.OperationReference?.Key;
            if (key is null || !keys.TryGetValue(key, out string? family))
            {
                continue;
            }
            Assert.IsTrue(seen.Add(key), "Each original effect emits one Audit record.");
            Assert.AreEqual(family, record.OperationReference!.OperationName);
            Assert.AreEqual(AuditOutcome.Succeeded, record.Outcome);
            Assert.AreEqual(issuer, record.Actor.Subject!.Issuer);
            Assert.AreEqual(OperatorA, record.Actor.Subject.Identifier);
        }
        Assert.AreEqual(keys.Count, seen.Count);
    }
}
