using System.Globalization;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Persistence;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    [TestMethod]
    public async Task ActualInstanceStatusPreservesMixedObservationsAcrossPagingAndBrowserReadBoundaries()
    {
        await RunFixtureAsync(browser: true, instanceStatusBoundaries: true);
    }

    private sealed record InstanceStatusBoundaryRow(string RegistrationId, string Name, string State, string Status);

    private sealed record InstanceStatusBoundariesSeed(string BaselineReleaseId, string CandidateReleaseId,
        string Generation, string StaleRegistrationId, IReadOnlyList<InstanceStatusBoundaryRow> Rows);

    private static async Task<InstanceStatusBoundariesSeed> SeedInstanceStatusBoundariesAsync(ManagementHostFixture fixture,
        ConfigurationRolloutSeed rollout, string[] historicalObservations)
    {
        Assert.HasCount(2, historicalObservations);
        // Freshness uses the server receipt clock. Wait for the real existing report;
        // neither producer timestamps nor direct database edits establish staleness.
        TimeSpan remaining;
        await using (NpgsqlCommand clock = fixture.Source.CreateCommand($"""
            SELECT received_at,clock_timestamp() FROM "{fixture.Options.Persistence.Schema}".configuration_adoption
            WHERE registration_id=@id
            """))
        {
            clock.Parameters.AddWithValue("id", Guid.Parse(historicalObservations[0]));
            await using NpgsqlDataReader reader = await clock.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            remaining = reader.GetDateTime(0) + fixture.Options.Persistence.StaleAfter + TimeSpan.FromSeconds(1) - reader.GetDateTime(1);
            Assert.IsFalse(await reader.ReadAsync());
        }
        Assert.IsTrue(remaining <= fixture.Options.Persistence.StaleAfter + TimeSpan.FromSeconds(2));
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }

        // Reuse the fixture's existing transaction provider after its human-authentication
        // restart. This is ordinary native registration/status persistence, with the same
        // workload owner, captured session, sequence admission and authoritative clock.
        PostgreSqlManagementStore store = fixture.CreateSetupStore();
        var owner = new ManagementRegistrationOwner("managed-a", "Development", ManagementHostFixture.Issuer,
            "workload-a", new("notProvided"));
        var desired = new ManagementConfigurationVersion(rollout.BaselineReleaseId, rollout.Generation);
        var accepted = new ManagementConfigurationAppliedVersion(desired.ReleaseId, desired.Generation, "1");
        var earlier = new ManagementConfigurationAppliedVersion(rollout.CandidateReleaseId, "1", "0");
        var rows = new List<InstanceStatusBoundaryRow>();
        foreach ((string name, string state, string status) in new[]
        {
            ("status-current", "Applied", "Up to date"),
            ("status-pending", "Pending", "Pending"),
            ("status-restart", "PendingRestart", "Needs restart"),
            ("status-rejected", "Rejected", "Failed"),
            ("status-faulted", "Faulted", "Failed"),
            ("status-offline", "Applied", "Unknown")
        }.Concat(Enumerable.Range(1, 16).Select(index =>
            ($"status-page-{index:D2}", "Applied", "Up to date"))))
        {
            string id = Guid.NewGuid().ToString("D");
            ManagementStoreResult<ManagementRegistrationReceipt> registration = await store.RegisterAsync(owner, id,
                ManagementHostFixture.Request(name));
            Assert.IsTrue(registration.IsKnownCommitted, registration.Code);
            ManagementConfigurationStatusEntry entry = state switch
            {
                "Applied" => new("shared.settings", "1", "HotReload", state, true, desired, accepted, accepted),
                "Pending" => new("shared.settings", "1", "HotReload", state, false, desired),
                "PendingRestart" => new("shared.settings", "1", "RestartRequired", state, true, desired, earlier, earlier),
                "Rejected" => new("shared.settings", "1", "HotReload", state, true, desired, earlier, earlier,
                    "Configuration.Validation"),
                "Faulted" => new("shared.settings", "1", "HotReload", state, false, desired, null, earlier,
                    "Configuration.LiveSetter"),
                _ => throw new InvalidOperationException("The named boundary fixture has a finite state set.")
            };
            string observed = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
            ManagementStoreResult<ManagementReportReceipt> report = await store.ReportConfigurationStatusAsync(owner, id,
                new("1", observed, [entry]));
            Assert.IsTrue(report.IsKnownCommitted, report.Code);
            if (name == "status-offline")
            {
                ManagementStoreResult<ManagementCloseReceipt> close = await store.CloseAsync(owner, id);
                Assert.IsTrue(close.IsKnownCommitted, close.Code);
            }
            rows.Add(new(id, name, state, status));
        }
        ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>> projection =
            await store.ReadConfigurationAdoptionAsync(fixture.ApplicationA, null, 100);
        Assert.IsTrue(projection.IsKnownCommitted, projection.Code);
        ManagementConfigurationAdoptionEntry stale = projection.Value!.Single(row =>
            row.Entry.RegistrationId == historicalObservations[0]).Entry;
        Assert.AreEqual("stale", stale.RegistrationFreshness);
        Assert.AreEqual("stale", stale.AdoptionFreshness);
        foreach (InstanceStatusBoundaryRow row in rows)
        {
            ManagementConfigurationAdoptionEntry actual = projection.Value!.Single(item => item.Entry.RegistrationId == row.RegistrationId).Entry;
            Assert.AreEqual(row.Name == "status-offline" ? "offline" : "fresh", actual.RegistrationFreshness);
            Assert.AreEqual("fresh", actual.AdoptionFreshness);
            Assert.AreEqual(row.State, actual.Reported!.State);
        }
        Assert.IsTrue(projection.Value!.Any(row => row.Entry.Convergence == "noInstances"));
        return new(rollout.BaselineReleaseId, rollout.CandidateReleaseId, rollout.Generation, historicalObservations[0], rows);
    }

    private static async Task<IReadOnlyList<InstanceStatusFingerprint>> ReadInstanceStatusBoundaryFingerprintsAsync(ManagementHostFixture fixture)
    {
        var fingerprints = new List<InstanceStatusFingerprint>(await ReadInstanceStatusFingerprintsAsync(fixture));
        foreach (string table in new[] { "management_registration", "configuration_session", "configuration_adoption" })
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand(
                $"SELECT row_to_json(t)::text FROM \"{fixture.Options.Persistence.Schema}\".\"{table}\" t ORDER BY registration_id LIMIT 257");
            fingerprints.Add(await FingerprintRowsAsync(table, command));
        }
        return fingerprints;
    }

    private static async Task VerifyInstanceStatusBoundariesAsync(ManagementHostFixture fixture, JsonElement proof,
        IReadOnlyList<InstanceStatusFingerprint> before, InstanceStatusBoundariesSeed seed)
    {
        Assert.AreEqual(fixture.ApplicationA, proof.GetProperty("applicationId").GetString());
        Assert.AreEqual(0, proof.GetProperty("mutations").GetInt32());
        Assert.AreEqual(2, proof.GetProperty("pages").GetInt32());
        Assert.IsTrue(proof.GetProperty("mixedStates").GetBoolean());
        Assert.IsTrue(proof.GetProperty("staleOfflineAndNoInstances").GetBoolean());
        Assert.IsTrue(proof.GetProperty("desiredAndHistoryRemainSeparate").GetBoolean());
        Assert.IsTrue(proof.GetProperty("sameSessionRefreshKeepsDetailsClosed").GetBoolean());
        Assert.IsTrue(proof.GetProperty("lateReadCannotRepopulateAnotherApplication").GetBoolean());
        CollectionAssert.AreEquivalent(seed.Rows.Select(row => row.RegistrationId).ToArray(),
            proof.GetProperty("registrations").EnumerateArray().Select(value => value.GetString()!).ToArray());
        CollectionAssert.AreEqual(before.ToArray(), (await ReadInstanceStatusBoundaryFingerprintsAsync(fixture)).ToArray(),
            "Read-only instance status must leave native reports, sessions, Configuration state, originals and Audit unchanged.");
    }
}
