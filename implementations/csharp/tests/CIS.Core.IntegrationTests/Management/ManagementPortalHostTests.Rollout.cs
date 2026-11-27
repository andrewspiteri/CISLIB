using System.Globalization;
using System.Net;
using System.Security.Cryptography;
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
    public async Task ActualDesktopConfigurationRolloutPreservesCompleteMapsAndOriginalResults()
    {
        await RunFixtureAsync(browser: true, rollout: true);
    }

    private sealed record ConfigurationRolloutSeed(string BaselineReleaseId, string CandidateReleaseId, string FinanceReleaseId,
        string AssignmentKey, string Generation, string MapETag, long ReleaseCount, long PublicationCount);

    private static async Task<string[]> SeedPortalAdoptionObservationsAsync(ManagementHostFixture fixture, HttpClient actor,
        ManagementConfigurationAssignmentMap map)
    {
        string historicalRelease = (await ManagementConfigurationDistributionTests.CreateRelease(actor, fixture.ApplicationA, map.ETag, Guid.NewGuid().ToString("D"),
            new(1, new("absent"), new("absent"), "Historical reported defaults release"))).Result!.ReleaseId!;
        HttpClient workload = fixture.Client("workload-a");
        var registrations = new List<string>();
        foreach (string name in new[] { "adoption-historical", "adoption-faulted" })
        {
            string id = Guid.NewGuid().ToString("D");
            string observed = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
            using HttpResponseMessage registered = await ManagementServiceTests.Put(workload, "registrations/" + id,
                new ManagementRegistrationRequest(1, name, observed, "3.1.0"));
            Assert.AreEqual(HttpStatusCode.OK, registered.StatusCode);
            var accepted = new ManagementConfigurationAppliedVersion(historicalRelease, "1", "0");
            var entry = name == "adoption-historical"
                ? new ManagementConfigurationStatusEntry("shared.settings", "1", "HotReload", "Applied", true, new(historicalRelease, "1"), accepted, accepted)
                : new ManagementConfigurationStatusEntry("shared.settings", "1", "HotReload", "Faulted", false, new(historicalRelease, "1"), null, accepted, "Configuration.LiveSetter");
            using var report = new HttpRequestMessage(HttpMethod.Put, "registrations/" + id + "/configuration-status")
            {
                Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationStatusReport("1", observed, [entry])))
            };
            report.Content.Headers.ContentType = new("application/json");
            using HttpResponseMessage reported = await workload.SendAsync(report);
            Assert.AreEqual(HttpStatusCode.OK, reported.StatusCode);
            registrations.Add(id);
        }
        return registrations.ToArray();
    }

    private static async Task<ConfigurationRolloutSeed> SeedConfigurationRolloutAsync(ManagementHostFixture fixture, RSA signing,
        string issuer, ManagementConfigurationDefinition definition, ConfigurationLookupProof lookup)
    {
        HttpClient actor = fixture.Client(subject: null, replica: fixture.Origins.Count - 1, explicitToken: LookupToken(signing, issuer));
        using HttpResponseMessage original = await actor.GetAsync(ManagementConfigurationDistributionTests.BindingPath(fixture.ApplicationA, "release-operations/" + lookup.ReleaseKey));
        Assert.AreEqual(HttpStatusCode.OK, original.StatusCode);
        string financeId = (await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationOperationResult>(original)).Result!.ReleaseId!;
        using HttpResponseMessage financeRead = await actor.GetAsync("configuration/releases/" + financeId);
        Assert.AreEqual(HttpStatusCode.OK, financeRead.StatusCode);
        ManagementConfigurationRelease finance = await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationRelease>(financeRead);
        ManagementConfigurationAssignmentMap initial = await ManagementConfigurationDistributionTests.Map(actor, fixture.ApplicationA);
        string baseline = (await ManagementConfigurationDistributionTests.CreateRelease(actor, fixture.ApplicationA, initial.ETag, Guid.NewGuid().ToString("D"),
            new(1, finance.CommonSource, finance.ApplicationSource, "Retained baseline release"))).Result!.ReleaseId!;
        string candidate = (await ManagementConfigurationDistributionTests.CreateRelease(actor, fixture.ApplicationA, initial.ETag, Guid.NewGuid().ToString("D"),
            new(1, finance.CommonSource, finance.ApplicationSource, "Retained candidate release"))).Result!.ReleaseId!;
        string assignmentKey = Guid.NewGuid().ToString("D");
        ManagementConfigurationOperationResult assigned = await ManagementConfigurationDistributionTests.Assign(actor, fixture.ApplicationA, initial.ETag, assignmentKey,
            new(1, "replace", baseline, [new("finance", financeId)], "Own assignment before read/write grants are removed"));
        Assert.AreEqual("committed", assigned.OriginalCompletion);
        ManagementConfigurationAssignmentMap after = await ManagementConfigurationDistributionTests.Map(actor, fixture.ApplicationA);
        for (int index = 0; index < 8; index++)
        {
            await ManagementConfigurationDistributionTests.CreateRelease(actor, fixture.ApplicationA, after.ETag, Guid.NewGuid().ToString("D"),
                new(1, finance.CommonSource, finance.ApplicationSource, "Bounded retained selection " + index.ToString(CultureInfo.InvariantCulture)));
        }
        return new(baseline, candidate, financeId, assignmentKey, after.Generation!, after.ETag,
            await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"),
            await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
    }

    private static async Task VerifyConfigurationRolloutEffectsAsync(ManagementHostFixture fixture, JsonElement proof,
        ConfigurationRolloutSeed seed, ManagementConfigurationDefinition definition, string issuer)
    {
        string[] applied = proof.GetProperty("appliedKeys").EnumerateArray().Select(item => item.GetString()!).ToArray();
        string conflict = proof.GetProperty("conflictKey").GetString()!;
        Assert.HasCount(4, applied);
        Assert.AreEqual(applied.Length, applied.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(applied.Append(conflict).All(key => Guid.TryParseExact(key, "D", out _)));
        Assert.AreEqual(seed.ReleaseCount, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        Assert.AreEqual(seed.PublicationCount, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        long generation = long.Parse(seed.Generation, CultureInfo.InvariantCulture) + applied.Length;
        long revision = long.Parse(seed.MapETag["\"configuration.map.".Length..^1], CultureInfo.InvariantCulture) + applied.Length;
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT revision,generation,default_release_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_assignment WHERE binding_id=@id"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(definition.BindingId!));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(revision, reader.GetInt64(0));
            Assert.AreEqual(generation, reader.GetInt64(1));
            Assert.AreEqual(Guid.Parse(seed.BaselineReleaseId), reader.GetGuid(2));
            Assert.IsFalse(await reader.ReadAsync());
        }
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT cohort_id,release_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_override WHERE binding_id=@id ORDER BY cohort_id COLLATE \"C\" LIMIT 2"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(definition.BindingId!));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual("finance", reader.GetString(0));
            Assert.AreEqual(Guid.Parse(seed.FinanceReleaseId), reader.GetGuid(1));
            Assert.IsFalse(await reader.ReadAsync(), "The canary must be explicitly removed while the unrelated Finance pin remains exact.");
        }
        var audits = new List<AuditRecord>();
        var codec = new AuditRecordCodec();
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY created_at,record_id LIMIT 257"))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                using JsonDocument envelope = JsonDocument.Parse(reader.GetFieldValue<byte[]>(0));
                Assert.IsTrue(codec.TryDecode(Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText()), out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
                if (record!.OperationReference?.OperationName == "management.configuration.assignment.replace")
                {
                    audits.Add(record);
                }
                Assert.IsTrue(audits.Count <= 8);
            }
        }
        Assert.HasCount(applied.Length + 2, audits, "Each original, including the known conflict, has exactly one Audit; recovery has no repeated effect.");
        foreach (string key in applied.Append(conflict))
        {
            AuditRecord audit = audits.Single(item => item.OperationReference!.Key == key);
            Assert.AreEqual(key == conflict ? AuditOutcome.Rejected : AuditOutcome.Succeeded, audit.Outcome);
            Assert.AreEqual(issuer, audit.Actor.Subject!.Issuer);
            Assert.AreEqual(OperatorA, audit.Actor.Subject.Identifier);
            Assert.AreEqual(definition.BindingId, audit.Target!.ResourceIdentifier);
        }
        Assert.AreEqual(LookupOperator, audits.Single(item => item.OperationReference!.Key == seed.AssignmentKey).Actor.Subject!.Identifier);
    }
}
