using System.Globalization;
using System.Net;
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
    public async Task ActualManualReviewRepairsPreserveSharedJsonEditableDraftsAndMetadataReturn()
    {
        await RunFixtureAsync(browser: true, reviewCorrections: true);
    }

    private static async Task<ManagementConfigurationAssignmentMap> ReadConfigurationRepairsMapAsync(HttpClient actor, string applicationId)
    {
        using HttpResponseMessage response = await actor.GetAsync("applications/" + applicationId
            + "/configuration/" + ManualSharedConfiguration + "/assignments?schemaVersion=1");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationAssignmentMap>(response);
    }

    private static async Task VerifyConfigurationRepairsEffectsAsync(ManagementHostFixture fixture, JsonElement proof,
        ManagementConfigurationDefinition application, ManagementConfigurationDefinition common,
        ManagementConfigurationAssignmentMap baseline, string issuer)
    {
        Assert.AreEqual(fixture.ApplicationA, proof.GetProperty("applicationId").GetString());
        Assert.AreEqual(ManualSharedConfiguration, proof.GetProperty("configurationId").GetString());
        Assert.AreEqual("1", proof.GetProperty("schemaVersion").GetString());
        Assert.AreEqual(application.SourceId, proof.GetProperty("sourceId").GetString());
        Assert.AreEqual(application.BindingId, proof.GetProperty("bindingId").GetString());
        Assert.AreEqual(baseline.DefaultReleaseId, proof.GetProperty("defaultReleaseId").GetString());
        Assert.AreEqual("1", proof.GetProperty("generation").GetString());
        string commonPublication = proof.GetProperty("commonPublicationId").GetString()!;
        string applicationPublication = proof.GetProperty("publicationId").GetString()!;
        string key = proof.GetProperty("publicationKey").GetString()!;
        string savedRelease = proof.GetProperty("releaseId").GetString()!;
        string releaseKey = proof.GetProperty("releaseKey").GetString()!;
        string expectedPayload = ManualSharedPayload.Replace("\"Enabled\":true", "\"Enabled\":false", StringComparison.Ordinal);
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(ManualSharedPayload), proof.GetProperty("sharedSha256").GetString());
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(expectedPayload), proof.GetProperty("publishedSha256").GetString());
        Assert.AreNotEqual(proof.GetProperty("publishedSha256").GetString(), proof.GetProperty("bufferedSha256").GetString());

        foreach ((string id, string source, string payload) in new[]
        {
            (commonPublication, common.SourceId, ManualSharedPayload),
            (applicationPublication, application.SourceId, expectedPayload)
        })
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT source_id,payload,sha256,created_at FROM \"{fixture.Options.Persistence.Schema}\".configuration_publication WHERE publication_id=@id");
            command.Parameters.AddWithValue("id", Guid.Parse(id));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(source), reader.GetGuid(0));
            Assert.AreEqual(payload, new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(1)));
            Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(payload), reader.GetString(2));
            if (id == commonPublication)
            {
                Assert.AreEqual(reader.GetDateTime(3).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
                    proof.GetProperty("modifiedAtUtc").GetString());
            }
            Assert.IsFalse(await reader.ReadAsync());
        }

        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT m.revision,m.generation,m.default_release_id,r.common_publication_id,r.application_publication_id,
                (SELECT count(*) FROM "{fixture.Options.Persistence.Schema}".configuration_override WHERE binding_id=m.binding_id)
            FROM "{fixture.Options.Persistence.Schema}".configuration_assignment m
            JOIN "{fixture.Options.Persistence.Schema}".configuration_release r ON r.release_id=m.default_release_id
            WHERE m.binding_id=@id
            """))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(application.BindingId!));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(baseline.ETag, "\"configuration.map." + reader.GetInt64(0).ToString(CultureInfo.InvariantCulture) + "\"");
            Assert.AreEqual(1L, reader.GetInt64(1));
            Assert.AreEqual(Guid.Parse(baseline.DefaultReleaseId!), reader.GetGuid(2));
            Assert.AreEqual(Guid.Parse(commonPublication), reader.GetGuid(3));
            Assert.IsTrue(reader.IsDBNull(4));
            Assert.AreEqual(0L, reader.GetInt64(5));
        }
        Assert.AreEqual(3L, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        Assert.AreEqual(3L, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));

        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT binding_id,common_publication_id,application_publication_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_release WHERE release_id=@id"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(savedRelease));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(application.BindingId!), reader.GetGuid(0));
            Assert.AreEqual(Guid.Parse(commonPublication), reader.GetGuid(1));
            Assert.AreEqual(Guid.Parse(applicationPublication), reader.GetGuid(2));
            Assert.IsFalse(await reader.ReadAsync());
        }

        await VerifySaveOriginalsAsync(fixture, issuer,
        [
            new(key, "management.configuration.source.publish", application.SourceId, "sourcePublished", applicationPublication),
            new(releaseKey, "management.configuration.release.create", application.BindingId!, "releaseCreated", savedRelease)
        ]);

        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT scope,operation_name,finalized,result_payload FROM \"{fixture.Options.OperationsSchema}\".operation_journal WHERE idempotency_key=@key"))
        {
            command.Parameters.AddWithValue("key", key);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.IsTrue(reader.GetString(0).StartsWith("management.configuration.v1/management.configuration.source.publish/" + application.SourceId + "/management.actor.v1.", StringComparison.Ordinal));
            Assert.AreEqual("management.configuration.source.publish", reader.GetString(1));
            Assert.IsTrue(reader.GetBoolean(2));
            ManagementConfigurationDecision decision = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDecision>(reader.GetFieldValue<byte[]>(3))!;
            Assert.AreEqual(applicationPublication, decision.PublicationId);
            Assert.AreEqual("applied", decision.Decision);
            Assert.IsFalse(await reader.ReadAsync());
        }

        int configurationAudits = 0;
        int publicationAudits = 0;
        var codec = new AuditRecordCodec();
        await using NpgsqlCommand auditCommand = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY created_at,record_id LIMIT 257");
        await using NpgsqlDataReader audits = await auditCommand.ExecuteReaderAsync();
        while (await audits.ReadAsync())
        {
            using JsonDocument envelope = JsonDocument.Parse(audits.GetFieldValue<byte[]>(0));
            Assert.IsTrue(codec.TryDecode(Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText()), out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
            if (record!.OperationReference?.OperationName is "management.configuration.source.publish" or "management.configuration.release.create" or "management.configuration.assignment.replace")
            {
                configurationAudits++;
            }
            if (record.OperationReference?.Key == key)
            {
                publicationAudits++;
                Assert.AreEqual(AuditOutcome.Succeeded, record.Outcome);
                Assert.AreEqual(issuer, record.Actor.Subject!.Issuer);
                Assert.AreEqual(OperatorA, record.Actor.Subject.Identifier);
                Assert.AreEqual(application.SourceId, record.Target!.ResourceIdentifier);
            }
        }
        Assert.AreEqual(7, configurationAudits, "Two lookup seeds, three Shared preparation effects, one Application publication and one saved release; buffered editing adds nothing.");
        Assert.AreEqual(1, publicationAudits);
    }
}
