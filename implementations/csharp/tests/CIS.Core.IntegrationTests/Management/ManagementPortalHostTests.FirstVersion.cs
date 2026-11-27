using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management;
using CIS.Management.Wire;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    private const string FirstExistingVersionPayload = "{\n"
        + "  \"Greeting\": \"First settings · 日本語 🌍 e\\u0301\",\n"
        + "  \"Enabled\": false,\n"
        + "  \"Count\": 9007199254740993\n"
        + "}";

    [TestMethod]
    public async Task ActualFirstVersionSavesAnExistingApplicationBindingWithAnAvailableCommonStream()
    {
        await RunFixtureAsync(browser: true, firstVersion: true);
    }

    private static async Task VerifyFirstConfigurationVersionAsync(ManagementHostFixture fixture, JsonElement proof,
        ManagementConfigurationDefinition definition, ManagementConfigurationDefinition common,
        ManagementConfigurationAssignmentMap before, string issuer)
    {
        Assert.AreEqual(fixture.ApplicationA, proof.GetProperty("applicationId").GetString());
        Assert.AreEqual(definition.BindingId, proof.GetProperty("bindingId").GetString());
        Assert.AreEqual(definition.SourceId, proof.GetProperty("sourceId").GetString());
        Assert.AreEqual(common.SourceId, proof.GetProperty("commonSourceId").GetString());
        Assert.AreEqual(before.ETag, proof.GetProperty("mapETag").GetString());
        string publication = proof.GetProperty("publicationId").GetString()!;
        string release = proof.GetProperty("releaseId").GetString()!;
        string digest = ManagementConfigurationWire.ComputePayloadSha256(FirstExistingVersionPayload);
        Assert.AreEqual(digest, proof.GetProperty("sha256").GetString());

        await using (NpgsqlCommand command = fixture.Source.CreateCommand(
            $"SELECT publication_id,source_id,payload,sha256 FROM \"{fixture.Options.Persistence.Schema}\".configuration_publication LIMIT 2"))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(publication), reader.GetGuid(0));
            Assert.AreEqual(Guid.Parse(definition.SourceId), reader.GetGuid(1));
            Assert.AreEqual(FirstExistingVersionPayload, new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(2)));
            Assert.AreEqual(digest, reader.GetString(3));
            Assert.IsFalse(await reader.ReadAsync(), "Only the Application source is published; the Common stream remains unmodified.");
        }
        await using (NpgsqlCommand command = fixture.Source.CreateCommand(
            $"SELECT release_id,binding_id,common_publication_id,application_publication_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_release LIMIT 2"))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(release), reader.GetGuid(0));
            Assert.AreEqual(Guid.Parse(definition.BindingId!), reader.GetGuid(1));
            Assert.IsTrue(reader.IsDBNull(2));
            Assert.AreEqual(Guid.Parse(publication), reader.GetGuid(3));
            Assert.IsFalse(await reader.ReadAsync());
        }
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT revision,generation,default_release_id,
                (SELECT count(*) FROM "{fixture.Options.Persistence.Schema}".configuration_override WHERE binding_id=@binding)
            FROM "{fixture.Options.Persistence.Schema}".configuration_assignment WHERE binding_id=@binding
            """))
        {
            command.Parameters.AddWithValue("binding", Guid.Parse(definition.BindingId!));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(1L, reader.GetInt64(0));
            Assert.IsTrue(reader.IsDBNull(1));
            Assert.IsTrue(reader.IsDBNull(2));
            Assert.AreEqual(0L, reader.GetInt64(3));
            Assert.IsFalse(await reader.ReadAsync());
        }

        JsonElement[] effects = proof.GetProperty("effects").EnumerateArray().ToArray();
        Assert.AreEqual(2, effects.Length);
        string[] families = ["management.configuration.source.publish", "management.configuration.release.create"];
        CollectionAssert.AreEqual(families, effects.Select(effect => effect.GetProperty("family").GetString()!).ToArray());
        var keys = effects.Select(effect => effect.GetProperty("key").GetString()!).ToArray();
        Assert.AreEqual(2, keys.Distinct(StringComparer.Ordinal).Count());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT scope,operation_name,idempotency_key,finalized,result_payload FROM "{fixture.Options.OperationsSchema}".operation_journal
            WHERE operation_name IN ('management.configuration.source.publish','management.configuration.release.create',
                'management.configuration.assignment.replace','management.configuration.create') LIMIT 3
            """))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string key = reader.GetString(2);
                int index = Array.IndexOf(keys, key);
                Assert.IsTrue(index >= 0 && seen.Add(key));
                Assert.IsTrue(Guid.TryParseExact(key, "D", out _));
                Assert.AreEqual(families[index], reader.GetString(1));
                string resource = index == 0 ? definition.SourceId : definition.BindingId!;
                string prefix = "management.configuration.v1/" + families[index] + "/" + resource + "/management.actor.v1.";
                string scope = reader.GetString(0);
                Assert.IsTrue(scope.StartsWith(prefix, StringComparison.Ordinal));
                Assert.IsTrue(ManagementConfigurationWire.IsSha256(scope[prefix.Length..]));
                Assert.IsTrue(reader.GetBoolean(3));
                ManagementConfigurationDecision decision = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDecision>(reader.GetFieldValue<byte[]>(4))!;
                Assert.AreEqual("applied", decision.Decision);
                Assert.AreEqual(index == 0 ? "sourcePublished" : "releaseCreated", decision.Kind);
                Assert.AreEqual(index == 0 ? publication : release, index == 0 ? decision.PublicationId : decision.ReleaseId);
            }
        }
        Assert.AreEqual(2, seen.Count);

        var codec = new AuditRecordCodec();
        var auditKeys = new HashSet<string>(StringComparer.Ordinal);
        await using NpgsqlCommand audits = fixture.Source.CreateCommand(
            $"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY record_id LIMIT 257");
        await using NpgsqlDataReader records = await audits.ExecuteReaderAsync();
        int auditCount = 0;
        while (await records.ReadAsync())
        {
            Assert.IsTrue(++auditCount <= 256);
            using JsonDocument envelope = JsonDocument.Parse(records.GetFieldValue<byte[]>(0));
            Assert.IsTrue(codec.TryDecode(Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText()),
                out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
            string? family = record!.OperationReference?.OperationName;
            if (family is null || !families.Contains(family, StringComparer.Ordinal))
            {
                continue;
            }
            string key = record.OperationReference!.Key;
            int index = Array.IndexOf(keys, key);
            Assert.IsTrue(index >= 0 && auditKeys.Add(key));
            Assert.AreEqual(families[index], family);
            Assert.AreEqual(AuditOutcome.Succeeded, record.Outcome);
            Assert.AreEqual(issuer, record.Actor.Subject!.Issuer);
            Assert.AreEqual(OperatorA, record.Actor.Subject.Identifier);
            Assert.AreEqual(index == 0 ? definition.SourceId : definition.BindingId, record.Target!.ResourceIdentifier);
        }
        Assert.AreEqual(2, auditKeys.Count);
    }
}
