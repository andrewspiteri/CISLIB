using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    [TestMethod]
    public async Task ActualInstanceStatusRemainsReadOnlyForHistoricalReportsAndAnAdoptionOnlyOperator()
    {
        await RunFixtureAsync(browser: true, instanceStatus: true);
    }

    private sealed record InstanceStatusFingerprint(string Name, int Rows, string Sha256);

    private static async Task<IReadOnlyList<InstanceStatusFingerprint>> ReadInstanceStatusFingerprintsAsync(ManagementHostFixture fixture)
    {
        var fingerprints = new List<InstanceStatusFingerprint>();
        foreach ((string name, string order) in new[]
        {
            ("configuration_publication", "publication_id"),
            ("configuration_release", "release_id"),
            ("configuration_assignment", "binding_id"),
            ("configuration_override", "binding_id,cohort_id")
        })
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand(
                $"SELECT row_to_json(t)::text FROM \"{fixture.Options.Persistence.Schema}\".\"{name}\" t ORDER BY {order} LIMIT 257");
            fingerprints.Add(await FingerprintRowsAsync(name, command));
        }
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"""
            SELECT row_to_json(t)::text FROM "{fixture.Options.OperationsSchema}".operation_journal t
            WHERE operation_name LIKE 'management.configuration.%' ORDER BY operation_name,idempotency_key LIMIT 257
            """))
        {
            fingerprints.Add(await FingerprintRowsAsync("configuration-originals", command));
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int rows = 0;
        int total = 0;
        var codec = new AuditRecordCodec();
        await using NpgsqlCommand audit = fixture.Source.CreateCommand(
            $"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY record_id LIMIT 257");
        await using NpgsqlDataReader reader = await audit.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Assert.IsTrue(++total <= 256, "This named fixture's complete Audit observation must remain bounded.");
            byte[] bytes = reader.GetFieldValue<byte[]>(0);
            using JsonDocument envelope = JsonDocument.Parse(bytes);
            Assert.IsTrue(codec.TryDecode(Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText()),
                out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
            if (record!.OperationReference?.OperationName.StartsWith("management.configuration.", StringComparison.Ordinal) != true)
            {
                continue;
            }
            rows++;
            hash.AppendData(bytes);
            hash.AppendData(new byte[] { 0 });
        }
        fingerprints.Add(new("configuration-audit", rows, Convert.ToHexStringLower(hash.GetHashAndReset())));
        return fingerprints;
    }

    private static async Task<InstanceStatusFingerprint> FingerprintRowsAsync(string name, NpgsqlCommand command)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int rows = 0;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Assert.IsTrue(++rows <= 256, "The complete owned fixture state must fit its fixed observation bound.");
            hash.AppendData(Encoding.UTF8.GetBytes(reader.GetString(0)));
            hash.AppendData(new byte[] { 0 });
        }
        return new(name, rows, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static async Task VerifyInstanceStatusReadOnlyAsync(ManagementHostFixture fixture, JsonElement proof,
        IReadOnlyList<InstanceStatusFingerprint> before, ManagementConfigurationDefinition definition, string[] observations)
    {
        Assert.AreEqual(fixture.ApplicationA, proof.GetProperty("applicationId").GetString());
        Assert.AreEqual(definition.BindingId, proof.GetProperty("bindingId").GetString());
        Assert.AreEqual(0, proof.GetProperty("mutations").GetInt32());
        Assert.IsTrue(proof.GetProperty("adoptionOnlyMetadataUnavailable").GetBoolean());
        Assert.IsTrue(proof.GetProperty("reportedVersionDistinctFromDesired").GetBoolean());
        CollectionAssert.AreEqual(observations, proof.GetProperty("observations").EnumerateArray().Select(value => value.GetString()!).ToArray());
        IReadOnlyList<InstanceStatusFingerprint> after = await ReadInstanceStatusFingerprintsAsync(fixture);
        Assert.AreEqual(before.Count, after.Count);
        for (int index = 0; index < before.Count; index++)
        {
            Assert.AreEqual(before[index], after[index], "Instance status must not change " + before[index].Name + ".");
        }
    }
}
