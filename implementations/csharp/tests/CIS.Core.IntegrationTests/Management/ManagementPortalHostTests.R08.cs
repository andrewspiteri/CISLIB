using System.Globalization;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Security;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    private const string R08Configuration = "portal.r08.flags";
    private const string R08Payload = """ { "Large":9007199254740993,"Unicode":"日本語 🌍 e\u0301","Escaped":"\u00e9","Zero":0,"Enabled":false,"Optional":null } """;

    [TestMethod]
    public async Task ActualR08ReadableConfigurationCreationRecoversItsOriginalAndSeparatesPublicationReleaseAndAssignment()
    {
        await RunFixtureAsync(browser: true, r08: true);
    }

    private static IEnumerable<CapabilityGrant> R08ApplicationGrants(string issuer, string applicationId)
    {
        foreach (string capability in new[]
        {
            ManagementConfigurationCapabilities.Create,
            ManagementConfigurationCapabilities.DefinitionsRead,
            ManagementConfigurationCapabilities.ContentRead,
            ManagementConfigurationCapabilities.Validate,
            ManagementConfigurationCapabilities.Publish,
            ManagementConfigurationCapabilities.OperationsRead,
            ManagementConfigurationCapabilities.Preview,
            ManagementConfigurationCapabilities.ReleaseCreate,
            ManagementConfigurationCapabilities.AssignmentWrite
        })
        {
            yield return ResourceGrant(issuer, OperatorA, capability, "management.application", applicationId);
        }
    }

    private static async Task VerifyR08ConfigurationEffectsAsync(ManagementHostFixture fixture, JsonElement proof,
        ConfigurationLookupProof seed, ManagementConfigurationDefinition typed, ManagementConfigurationAssignmentMap baseline, string issuer)
    {
        Assert.AreEqual(fixture.ApplicationA, proof.GetProperty("applicationId").GetString());
        Assert.AreEqual(R08Configuration, proof.GetProperty("configurationId").GetString());
        Assert.AreEqual("1", proof.GetProperty("schemaVersion").GetString());
        string sourceId = proof.GetProperty("sourceId").GetString()!;
        string bindingId = proof.GetProperty("bindingId").GetString()!;
        string publicationId = proof.GetProperty("publicationId").GetString()!;
        string releaseId = proof.GetProperty("releaseId").GetString()!;
        string digest = proof.GetProperty("draftSha256").GetString()!;
        var expected = new[]
        {
            (Key: proof.GetProperty("creationKey").GetString()!, Family: "management.configuration.create", Kind: "configurationCreated", Resource: fixture.ApplicationA),
            (Key: proof.GetProperty("publicationKey").GetString()!, Family: "management.configuration.source.publish", Kind: "sourcePublished", Resource: sourceId),
            (Key: proof.GetProperty("releaseKey").GetString()!, Family: "management.configuration.release.create", Kind: "releaseCreated", Resource: bindingId),
            (Key: proof.GetProperty("assignmentKey").GetString()!, Family: "management.configuration.assignment.replace", Kind: "assignmentsReplaced", Resource: bindingId)
        };
        Assert.HasCount(4, expected.Select(item => item.Key).Distinct(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(expected.Select(item => item.Key).Concat([sourceId, bindingId, publicationId, releaseId]).All(value => Guid.TryParseExact(value, "D", out _)));
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(R08Payload), digest);

        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT d.definition_body,d.definition_digest,d.manifest_managed,b.manifest_managed,b.predeployment_created,
                b.host_scope,a.application,a.environment,s.revision,s.environment,s.application_id,s.binding_id
            FROM "{fixture.Options.Persistence.Schema}".configuration_source s
            JOIN "{fixture.Options.Persistence.Schema}".configuration_binding b ON b.binding_id=s.binding_id AND b.application_id=s.application_id
            JOIN "{fixture.Options.Persistence.Schema}".configuration_definition d ON d.definition_id=s.definition_id AND d.definition_id=b.definition_id
            JOIN "{fixture.Options.Persistence.Schema}".management_application a ON a.application_id=b.application_id
            WHERE s.source_id=@source AND b.binding_id=@binding
            """))
        {
            command.Parameters.AddWithValue("source", Guid.Parse(sourceId));
            command.Parameters.AddWithValue("binding", Guid.Parse(bindingId));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            byte[] bytes = reader.GetFieldValue<byte[]>(0);
            ManagementConfigurationSchema definition = JsonSerializer.Deserialize<ManagementConfigurationSchema>(bytes, ManagementConfigurationOptions.Json)
                ?? throw new AssertFailedException("The native created definition must be present.");
            Assert.AreEqual(R08Configuration, definition.ConfigurationId);
            Assert.AreEqual("1", definition.SchemaVersion);
            Assert.AreEqual("object", definition.Shape.Kind);
            Assert.IsFalse(definition.Shape.Nullable);
            Assert.IsNotNull(definition.Shape.Members);
            Assert.HasCount(0, definition.Shape.Members!);
            Assert.AreEqual("{}", definition.DefaultsJson);
            Assert.IsTrue(System.Security.Cryptography.SHA256.HashData(bytes).AsSpan().SequenceEqual(reader.GetFieldValue<byte[]>(1)));
            Assert.IsFalse(reader.GetBoolean(2));
            Assert.IsFalse(reader.GetBoolean(3));
            Assert.IsTrue(reader.GetBoolean(4));
            Assert.AreEqual("managed-a", reader.GetString(5));
            Assert.AreEqual(reader.GetString(6), reader.GetString(5));
            Assert.AreEqual("Development", reader.GetString(7));
            Assert.AreEqual(2L, reader.GetInt64(8), "One explicit source publication advances the new source exactly once.");
            Assert.AreEqual(reader.GetString(7), reader.GetString(9));
            Assert.AreEqual(Guid.Parse(fixture.ApplicationA), reader.GetGuid(10));
            Assert.AreEqual(Guid.Parse(bindingId), reader.GetGuid(11));
            Assert.IsFalse(await reader.ReadAsync());
        }

        Assert.AreEqual(2L, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"), "One lookup seed and one R08 publication.");
        Assert.AreEqual(2L, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"), "One lookup seed and one R08 release.");
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT source_id,payload,sha256 FROM \"{fixture.Options.Persistence.Schema}\".configuration_publication WHERE publication_id=@id"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(publicationId));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(sourceId), reader.GetGuid(0));
            Assert.AreEqual(R08Payload, new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(1)));
            Assert.AreEqual(digest, reader.GetString(2));
            Assert.IsFalse(await reader.ReadAsync());
        }
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT binding_id,common_publication_id,application_publication_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_release WHERE release_id=@id"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(releaseId));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(bindingId), reader.GetGuid(0));
            Assert.IsTrue(reader.IsDBNull(1));
            Assert.AreEqual(Guid.Parse(publicationId), reader.GetGuid(2));
            Assert.IsFalse(await reader.ReadAsync());
        }
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT revision,generation,default_release_id,
                (SELECT count(*) FROM "{fixture.Options.Persistence.Schema}".configuration_override WHERE binding_id=@id)
            FROM "{fixture.Options.Persistence.Schema}".configuration_assignment WHERE binding_id=@id
            """))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(bindingId));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(2L, reader.GetInt64(0));
            Assert.AreEqual(1L, reader.GetInt64(1));
            Assert.AreEqual(Guid.Parse(releaseId), reader.GetGuid(2));
            Assert.AreEqual(0L, reader.GetInt64(3));
            Assert.IsFalse(await reader.ReadAsync());
        }
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT revision,generation,default_release_id,
                (SELECT count(*) FROM "{fixture.Options.Persistence.Schema}".configuration_override WHERE binding_id=@id)
            FROM "{fixture.Options.Persistence.Schema}".configuration_assignment WHERE binding_id=@id
            """))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(typed.BindingId!));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(baseline.ETag, "\"configuration.map." + reader.GetInt64(0).ToString(CultureInfo.InvariantCulture) + "\"");
            Assert.IsTrue(reader.IsDBNull(1));
            Assert.IsTrue(reader.IsDBNull(2));
            Assert.AreEqual(0L, reader.GetInt64(3));
            Assert.IsFalse(await reader.ReadAsync());
        }

        var journalKeys = new HashSet<string>(StringComparer.Ordinal);
        string? bindingActor = null;
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT scope,operation_name,idempotency_key,finalized,result_payload FROM \"{fixture.Options.OperationsSchema}\".operation_journal WHERE idempotency_key=ANY(@keys)"))
        {
            command.Parameters.AddWithValue("keys", expected.Select(item => item.Key).ToArray());
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string key = reader.GetString(2);
                var row = expected.Single(item => item.Key == key);
                Assert.IsTrue(journalKeys.Add(key));
                Assert.AreEqual(row.Family, reader.GetString(1));
                Assert.IsTrue(reader.GetBoolean(3));
                string prefix = "management.configuration.v1/" + row.Family + "/" + row.Resource + "/management.actor.v1.";
                string scope = reader.GetString(0);
                Assert.IsTrue(scope.StartsWith(prefix, StringComparison.Ordinal));
                string digestActor = scope[prefix.Length..];
                Assert.IsTrue(ManagementConfigurationWire.IsSha256(digestActor));
                if (row.Resource == bindingId)
                {
                    bindingActor ??= digestActor;
                    Assert.AreEqual(bindingActor, digestActor, "Release and assignment share the same actual binding and native caller.");
                }
                ManagementConfigurationDecision decision = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDecision>(reader.GetFieldValue<byte[]>(4))
                    ?? throw new AssertFailedException("The native original decision must be independently readable.");
                Assert.AreEqual(row.Kind, decision.Kind);
                Assert.AreEqual("applied", decision.Decision);
                Assert.IsFalse(Encoding.UTF8.GetString(reader.GetFieldValue<byte[]>(4)).Contains("9007199254740993", StringComparison.Ordinal));
            }
        }
        Assert.HasCount(4, journalKeys);
        var audits = new List<AuditRecord>();
        var codec = new AuditRecordCodec();
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY created_at,record_id LIMIT 257"))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                byte[] bytes = reader.GetFieldValue<byte[]>(0);
                Assert.IsFalse(Encoding.UTF8.GetString(bytes).Contains("9007199254740993", StringComparison.Ordinal));
                using JsonDocument envelope = JsonDocument.Parse(bytes);
                Assert.IsTrue(codec.TryDecode(Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText()), out AuditRecord? audit, out AuditWireFailure failure), failure.ToString());
                if (expected.Any(item => item.Family == audit!.OperationReference?.OperationName))
                {
                    audits.Add(audit!);
                }
                Assert.IsTrue(audits.Count <= 6);
            }
        }
        Assert.HasCount(6, audits, "Four distinct R08 originals plus the two retained lookup seeds; recovery adds no Audit.");
        foreach (var row in expected)
        {
            AuditRecord audit = audits.Single(item => item.OperationReference!.Key == row.Key);
            Assert.AreEqual(row.Family, audit.OperationReference!.OperationName);
            Assert.AreEqual(AuditOutcome.Succeeded, audit.Outcome);
            Assert.AreEqual(AuditActorKind.User, audit.Actor.Kind);
            Assert.AreEqual(issuer, audit.Actor.Subject!.Issuer);
            Assert.AreEqual(OperatorA, audit.Actor.Subject.Identifier);
            Assert.AreEqual(row.Resource, audit.Target!.ResourceIdentifier);
        }
        foreach (string key in new[] { seed.PublicationKey, seed.ReleaseKey })
        {
            Assert.AreEqual(LookupOperator, audits.Single(item => item.OperationReference!.Key == key).Actor.Subject!.Identifier);
        }
    }
}
