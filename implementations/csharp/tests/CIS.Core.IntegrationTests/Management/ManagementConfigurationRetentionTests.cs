using System.Data.Common;
using System.Net;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Tests.IntegrationTests.Operations;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationRetentionTests
{
    [TestMethod]
    public async Task LatestTenEligibleReleasesAndAssignedPinnedSharedReferencesSurviveOneBoundedCleanupBudget()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Policy(10));
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        string source = (await Definitions(actor)).Items.Single(row => row.Kind == "common").SourceId;
        string shared = await PublishValue(actor, source, "shared", "{\"Greeting\":\"shared\"}");
        ManagementConfigurationAssignmentMap a = await Map(actor, fixture.ApplicationA);
        ManagementConfigurationAssignmentMap b = await Map(actor, fixture.ApplicationB);
        string active = (await CreateRelease(actor, fixture.ApplicationA, a.ETag, "active", Pair(shared))).Result!.ReleaseId!;
        string cohort = (await CreateRelease(actor, fixture.ApplicationA, a.ETag, "cohort", Pair(shared))).Result!.ReleaseId!;
        string other = (await CreateRelease(actor, fixture.ApplicationB, b.ETag, "other-binding", Pair(shared))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, a.ETag, "assign", new(1, "replace", active, [new("canary", cohort)], "Keep current default and cohort references"));
        var orphans = new List<string>();
        for (int index = 0; index < 12; index++)
        {
            orphans.Add(await PublishValue(actor, source, "orphan-" + index, "{\"Greeting\":\"orphan-" + index + "\"}"));
        }
        var histories = new List<(string Id, string At)>();
        for (int index = 0; index < 13; index++)
        {
            ManagementConfigurationDecision created = (await CreateRelease(actor, fixture.ApplicationA, (await Map(actor, fixture.ApplicationA)).ETag,
                "history-" + index, new(1, new("absent"), new("absent"), "Eligible history " + index))).Result!;
            histories.Add((created.ReleaseId!, created.DecidedAtUtc));
        }
        string pinned = histories[0].Id;
        actor = await ApplyPolicy(fixture, Policy(10, pins: [pinned, other]), "pin-history");
        string[] expected = histories.Where(item => item.Id != pinned).OrderByDescending(item => item.At, StringComparer.Ordinal)
            .ThenByDescending(item => item.Id, StringComparer.Ordinal).Take(10).Select(item => item.Id).ToArray();
        string registrationId = Guid.NewGuid().ToString("D");
        HttpClient workload = fixture.Client("workload-a", fixture.Origins.Count - 1);
        using (var registration = new HttpRequestMessage(HttpMethod.Put, "registrations/" + registrationId))
        {
            registration.Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request("charged-session")));
            registration.Content.Headers.ContentType = new("application/json");
            using HttpResponseMessage response = await workload.SendAsync(registration);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        byte[] status = ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationStatusReport("1", "2026-11-24T00:00:00.0000000Z",
            [new("shared.settings", "1", "HotReload", "Pending", false)]));
        using (var report = new HttpRequestMessage(HttpMethod.Put, "registrations/" + registrationId + "/configuration-status"))
        {
            report.Content = new ByteArrayContent(status);
            report.Content.Headers.ContentType = new("application/json");
            using HttpResponseMessage response = await workload.SendAsync(report);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        long expectedSessionBytes;
        await using (var retained = fixture.Source.CreateCommand($"""
            SELECT ((SELECT COALESCE(sum(logical_bytes),0) FROM "{fixture.Options.Persistence.Schema}".configuration_session)
                +(SELECT COALESCE(sum(logical_bytes),0) FROM "{fixture.Options.Persistence.Schema}".configuration_adoption))::bigint
            """))
        {
            expectedSessionBytes = (long)(await retained.ExecuteScalarAsync())!;
        }
        await fixture.StopReplicasAsync();
        await using (var age = fixture.Source.CreateCommand($"UPDATE \"{fixture.Options.Persistence.Schema}\".management_registration SET registered_at=clock_timestamp()-interval '8 days'"))
        {
            await age.ExecuteNonQueryAsync();
        }
        await using ServiceProvider provider = MaintenanceProvider(fixture);
        PostgreSqlManagementStore store = MaintenanceStore(fixture, provider);
        int removedRegistrations = 0;
        long releasedSessionBytes = 0;
        for (int index = 0; index < 3; index++)
        {
            ManagementCleanupResult cleanup = (await store.CleanupAsync(1)).Value!;
            Assert.AreEqual(1, cleanup.RemovedRegistrations);
            Assert.AreEqual(0, cleanup.RemovedConfigurationReleases);
            Assert.AreEqual(0, cleanup.RemovedConfigurationPublications);
            removedRegistrations += cleanup.RemovedRegistrations;
            releasedSessionBytes += cleanup.ReleasedConfigurationBytes;
        }
        Assert.AreEqual(3, removedRegistrations);
        Assert.IsGreaterThan(128L + status.Length, releasedSessionBytes);
        Assert.AreEqual(expectedSessionBytes, releasedSessionBytes);
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_adoption"));
        await Drain(store, 2);
        foreach (string id in expected.Append(active).Append(cohort).Append(pinned).Append(other))
        {
            Assert.IsTrue(await Exists(fixture, "configuration_release", "release_id", id), id);
        }
        Assert.AreEqual(14L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        Assert.IsTrue(await Exists(fixture, "configuration_publication", "publication_id", shared));
        foreach (string id in orphans.Take(2))
        {
            Assert.IsFalse(await Exists(fixture, "configuration_publication", "publication_id", id));
        }
        foreach (string id in orphans.Skip(2))
        {
            Assert.IsTrue(await Exists(fixture, "configuration_publication", "publication_id", id));
        }
        Assert.AreEqual(11L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        await AssertOccupancy(fixture);

        // Removing one retained reference cannot reclaim a common publication still used by the other binding.
        actor = await ApplyPolicy(fixture, Policy(0), "unpin");
        await fixture.StopReplicasAsync();
        await Drain(store, 100);
        Assert.IsFalse(await Exists(fixture, "configuration_release", "release_id", other));
        Assert.IsTrue(await Exists(fixture, "configuration_publication", "publication_id", shared));
        await fixture.StartReplicaAsync(serviceOptions: Options(fixture, Policy(0)));
        actor = fixture.Client("operator-a", fixture.Origins.Count - 1);
        ManagementConfigurationAssignmentMap current = await Map(actor, fixture.ApplicationA);
        string defaults = (await CreateRelease(actor, fixture.ApplicationA, current.ETag, "new-defaults",
            new(1, new("absent"), new("absent"), "Release shared reference"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, current.ETag, "replace-default", new(1, "replace", defaults, [], "Release final shared reference"));
        await fixture.StopReplicasAsync();
        await Drain(store, 100);
        Assert.IsFalse(await Exists(fixture, "configuration_publication", "publication_id", shared));
        await AssertOccupancy(fixture);
    }

    [TestMethod]
    public async Task PrunedOriginalPublicationAndReleaseResultsRecoverWithoutRecreationAndRollbackRefuses()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Policy(1));
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        string sourceId = (await Definitions(actor)).Items.Single(row => row.Kind == "common").SourceId;
        ManagementConfigurationSource originalSource = await Source(actor, sourceId);
        const string payload = "{\"Greeting\":\"original\"}";
        using HttpResponseMessage submitted = await Publish(actor, originalSource, "original-publication", payload);
        ManagementConfigurationOperationResult publication = await Decode<ManagementConfigurationOperationResult>(submitted);
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        ManagementConfigurationReleaseCommand command = Pair(publication.Result!.PublicationId!);
        ManagementConfigurationOperationResult release = await CreateRelease(actor, fixture.ApplicationA, map.ETag, "original-release", command);
        for (int index = 1; index <= 2; index++)
        {
            string id = await PublishValue(actor, sourceId, "new-publication-" + index, "{\"Greeting\":\"new-" + index + "\"}");
            await CreateRelease(actor, fixture.ApplicationA, map.ETag, "new-release-" + index, Pair(id));
        }
        long journal = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        await fixture.StopReplicasAsync();
        await using ServiceProvider provider = MaintenanceProvider(fixture);
        await Drain(MaintenanceStore(fixture, provider), 100);
        Assert.IsFalse(await Exists(fixture, "configuration_publication", "publication_id", publication.Result.PublicationId!));
        Assert.IsFalse(await Exists(fixture, "configuration_release", "release_id", release.Result!.ReleaseId!));
        Assert.AreEqual(journal, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        await fixture.StartReplicaAsync();
        actor = fixture.Client("operator-a", fixture.Origins.Count - 1);
        long used = (await Catalog(actor)).UsedBytes;
        using (HttpResponseMessage content = await actor.GetAsync("configuration/publications/" + publication.Result.PublicationId))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, content.StatusCode);
        }
        using (HttpResponseMessage found = await actor.GetAsync("configuration/sources/" + sourceId + "/publication-operations/original-publication"))
        {
            Assert.AreEqual(publication.Result, (await Decode<ManagementConfigurationOperationResult>(found)).Result);
        }
        using (HttpResponseMessage replay = await Publish(actor, originalSource, "original-publication", payload))
        {
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
            Assert.AreEqual("replayed", (await Decode<ManagementConfigurationOperationResult>(replay)).ResultSource);
        }
        ManagementConfigurationOperationResult replayRelease = await CreateRelease(actor, fixture.ApplicationA, map.ETag, "original-release", command);
        Assert.AreEqual(release.Result, replayRelease.Result);
        Assert.AreEqual("replayed", replayRelease.ResultSource);
        Assert.AreEqual(used, (await Catalog(actor)).UsedBytes);
        Assert.IsFalse(await Exists(fixture, "configuration_publication", "publication_id", publication.Result.PublicationId!));
        Assert.IsFalse(await Exists(fixture, "configuration_release", "release_id", release.Result.ReleaseId!));
        using var rollback = new HttpRequestMessage(HttpMethod.Put, BindingPath(fixture.ApplicationA, "assignments"));
        rollback.Headers.Add("Idempotency-Key", "rollback-to-pruned");
        rollback.Headers.TryAddWithoutValidation("If-Match", (await Map(actor, fixture.ApplicationA)).ETag);
        rollback.Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationAssignmentCommand(1,
            "rollback", release.Result.ReleaseId!, [], "Explicit old content is no longer retained")));
        rollback.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage refused = await actor.SendAsync(rollback);
        Assert.AreEqual(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.AreEqual("management.configuration.reference_not_found", (await Decode<ManagementConfigurationProblem>(refused)).Code);
        Assert.IsNull((await Map(actor, fixture.ApplicationA)).Generation);
        await AssertOccupancy(fixture);
    }

    [TestMethod]
    public async Task ProtectedHistoryAtHardQuotaRefusesNewPublicationWithoutEvictionOrCounterDrift()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Policy(0));
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        string sourceId = (await Definitions(actor)).Items.Single(row => row.Kind == "common").SourceId;
        string publication = await PublishValue(actor, sourceId, "protected-publication", "{\"Greeting\":\"protected\"}");
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        string release = (await CreateRelease(actor, fixture.ApplicationA, map.ETag, "protected-release", Pair(publication))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, map.ETag, "protect-current", new(1, "replace", release, [], "Keep active"));
        long used = (await Catalog(actor)).UsedBytes;
        actor = await ApplyPolicy(fixture, Policy(0, quota: used + 64, pins: [release]), "pin-and-bound");
        await using ServiceProvider provider = MaintenanceProvider(fixture);
        ManagementStoreResult<ManagementCleanupResult> cleanup = await MaintenanceStore(fixture, provider).CleanupAsync(100);
        Assert.IsTrue(cleanup.IsKnownCommitted, cleanup.Code);
        Assert.AreEqual(0, cleanup.Value!.RemovedConfigurationReleases + cleanup.Value.RemovedConfigurationPublications);
        ManagementConfigurationSource source = await Source(actor, sourceId);
        using HttpResponseMessage refused = await Publish(actor, source, "capacity-original", "{\"Greeting\":\"" + new string('x', 1000) + "\"}");
        Assert.AreEqual(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.AreEqual("management.configuration.capacity_exceeded", (await Decode<ManagementConfigurationProblem>(refused)).Code);
        Assert.IsTrue(await Exists(fixture, "configuration_release", "release_id", release));
        Assert.IsTrue(await Exists(fixture, "configuration_publication", "publication_id", publication));
        Assert.AreEqual(source.ETag, (await Source(actor, sourceId)).ETag);
        await AssertOccupancy(fixture);
    }

    [TestMethod]
    [DataRow("publication")]
    [DataRow("release")]
    public async Task ConcurrentReplicaReferenceInstallationAndCleanupNeverLeaveDanglingContent(string reference)
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Policy(1));
        HttpClient actor = fixture.Client("operator-a", 1);
        await Apply(actor, await Catalog(actor), "catalog");
        string source = (await Definitions(actor)).Items.Single(row => row.Kind == "common").SourceId;
        string oldPublication = await PublishValue(actor, source, "older", "{\"Greeting\":\"older\"}");
        string newerPublication = await PublishValue(actor, source, "newer", "{\"Greeting\":\"newer\"}");
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        string? oldRelease = null;
        if (reference == "release")
        {
            oldRelease = (await CreateRelease(actor, fixture.ApplicationA, map.ETag, "older-release", Pair(oldPublication))).Result!.ReleaseId!;
            await CreateRelease(actor, fixture.ApplicationA, map.ETag, "newer-release", Pair(newerPublication));
        }

        await using ServiceProvider provider = MaintenanceProvider(fixture);
        PostgreSqlManagementStore store = MaintenanceStore(fixture, provider);
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        Task<HttpResponseMessage> installation;
        Task<ManagementStoreResult<ManagementCleanupResult>> cleanup;
        await using (ILocalTransaction blocker = await factory.BeginAsync("management"))
        {
            await blocker.Context.UseAdoNetAsync(async (connection, transaction, token) =>
            {
                await using DbCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"SELECT catalog_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_catalog WHERE singleton FOR UPDATE";
                await command.ExecuteScalarAsync(token);
            });
            installation = reference == "publication"
                ? Send(actor, BindingPath(fixture.ApplicationA, "releases"), "race-create", map.ETag, Pair(oldPublication))
                : SendAssignment(actor, fixture.ApplicationA, map.ETag, "race-assignment", new(1, "replace", oldRelease!, [], "Protect exact release"));
            cleanup = store.CleanupAsync(1);
            Assert.AreEqual(TransactionOutcome.NotCommitted, (await blocker.RollbackAsync()).Outcome);
        }
        using HttpResponseMessage response = await installation.WaitAsync(TimeSpan.FromSeconds(20));
        ManagementStoreResult<ManagementCleanupResult> cleaned = await cleanup.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.IsTrue(cleaned.IsKnownCommitted, cleaned.Code);
        Assert.IsTrue(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        if (response.StatusCode == HttpStatusCode.OK)
        {
            if (reference == "release")
            {
                Assert.AreEqual(oldRelease, (await Map(actor, fixture.ApplicationA)).DefaultReleaseId);
                Assert.IsTrue(await Exists(fixture, "configuration_release", "release_id", oldRelease!));
            }
            Assert.IsTrue(await Exists(fixture, "configuration_publication", "publication_id", oldPublication));
        }
        else
        {
            Assert.AreEqual("management.configuration.reference_not_found", (await Decode<ManagementConfigurationProblem>(response)).Code);
            Assert.IsNull((await Map(actor, fixture.ApplicationA)).Generation);
        }
        await using var dangling = fixture.Source.CreateCommand($"""
            SELECT count(*) FROM "{fixture.Options.Persistence.Schema}".configuration_release r
            LEFT JOIN "{fixture.Options.Persistence.Schema}".configuration_publication c ON c.publication_id=r.common_publication_id
            LEFT JOIN "{fixture.Options.Persistence.Schema}".configuration_publication a ON a.publication_id=r.application_publication_id
            WHERE (r.common_publication_id IS NOT NULL AND c.publication_id IS NULL)
                OR (r.application_publication_id IS NOT NULL AND a.publication_id IS NULL)
            """);
        Assert.AreEqual(0L, await dangling.ExecuteScalarAsync());
        await AssertOccupancy(fixture);
    }

    [TestMethod]
    public async Task CleanupAcknowledgementLossRetainsUnknownAndFreshOwnersAccountEachActualDeletionOnce()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Policy(0));
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        for (int index = 0; index < 3; index++)
        {
            await CreateRelease(actor, fixture.ApplicationA, map.ETag, "cleanup-history-" + index,
                new(1, new("absent"), new("absent"), "Unassigned bounded history"));
        }
        await fixture.StopReplicasAsync();
        long journal = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        using var boundary = new NativeCommitBoundarySource(fixture.Source);
        await using ServiceProvider uncertainProvider = MaintenanceProvider(fixture, boundary);
        PostgreSqlManagementStore uncertainStore = MaintenanceStore(fixture, uncertainProvider);
        boundary.FailAfterNextCommit();
        ManagementStoreResult<ManagementCleanupResult> uncertain = await uncertainStore.CleanupAsync(1);
        Assert.IsFalse(uncertain.IsKnownCommitted);
        Assert.AreEqual("management.transaction_unknown", uncertain.Code);
        Assert.AreEqual(TransactionOutcome.Unknown, uncertain.AttemptCompletion!.Outcome);
        Assert.IsNull(uncertain.Value, "Unconfirmed cleanup counts cannot be asserted as committed facts.");
        Assert.AreEqual(2L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        await AssertOccupancy(fixture);

        // Independent cleanup owners observe the committed deletion; neither replays a guessed byte delta.
        await using ServiceProvider first = MaintenanceProvider(fixture);
        await using ServiceProvider second = MaintenanceProvider(fixture);
        ManagementStoreResult<ManagementCleanupResult>[] recovered = await Task.WhenAll(
            MaintenanceStore(fixture, first).CleanupAsync(1), MaintenanceStore(fixture, second).CleanupAsync(1));
        Assert.IsTrue(recovered.All(result => result.IsKnownCommitted));
        Assert.AreEqual(2, recovered.Sum(result => result.Value!.RemovedConfigurationReleases));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        Assert.AreEqual(journal, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        await AssertOccupancy(fixture);
    }

    private static ManagementConfigurationOptions Policy(int keep, long quota = 32L * 1024 * 1024, IReadOnlyList<string>? pins = null)
    {
        ManagementConfigurationOptions original = Deployment();
        return new(JsonSerializer.Serialize(original.Manifest, ManagementConfigurationOptions.Json), quota, keep, pins);
    }
    private static ManagementConfigurationReleaseCommand Pair(string publication) => new(1, new("present", publication), new("absent"), "Pin retained source");
    private static async Task<HttpResponseMessage> SendAssignment(HttpClient actor, string application, string etag, string key,
        ManagementConfigurationAssignmentCommand command)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BindingPath(application, "assignments"));
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(command));
        request.Content.Headers.ContentType = new("application/json");
        return await actor.SendAsync(request);
    }
    private static ManagementServiceOptions Options(ManagementHostFixture fixture, ManagementConfigurationOptions policy)
        => new(fixture.Options.ConnectionString, fixture.Options.CopyCursorSigningKey(), fixture.Options.Persistence, fixture.Options.BasePath,
            fixture.Options.MaximumConcurrentRequests, fixture.Options.OperationsSchema, fixture.Options.MessagingSchema,
            fixture.Options.Delivery, fixture.Options.Backend, policy);
    private static async Task<HttpClient> ApplyPolicy(ManagementHostFixture fixture, ManagementConfigurationOptions policy, string key)
    {
        await fixture.StopReplicasAsync();
        await fixture.StartReplicaAsync(serviceOptions: Options(fixture, policy));
        HttpClient actor = fixture.Client("operator-a", fixture.Origins.Count - 1);
        await Apply(actor, await Catalog(actor), key);
        return actor;
    }
    private static ServiceProvider MaintenanceProvider(ManagementHostFixture fixture, DbDataSource? source = null)
    {
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("management", _ => source ?? fixture.Source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        return services.BuildServiceProvider();
    }
    private static PostgreSqlManagementStore MaintenanceStore(ManagementHostFixture fixture, ServiceProvider provider)
    {
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        return new(factory, fixture.Options.Persistence, new PostgreSqlManagementAuthority(factory, fixture.Options));
    }
    private static async Task Drain(PostgreSqlManagementStore store, int maximumRows)
    {
        for (int index = 0; index < 32; index++)
        {
            ManagementStoreResult<ManagementCleanupResult> result = await store.CleanupAsync(maximumRows);
            Assert.IsTrue(result.IsKnownCommitted, result.Code);
            int removed = result.Value!.RemovedRegistrations + result.Value.RemovedConfigurationReleases + result.Value.RemovedConfigurationPublications;
            Assert.IsLessThanOrEqualTo(maximumRows, removed);
            if (removed == 0)
            {
                return;
            }
        }
        Assert.Fail("The finite retention fixture did not settle within its bounded explicit cleanup calls.");
    }
    private static async Task<bool> Exists(ManagementHostFixture fixture, string table, string column, string id)
    {
        await using var command = fixture.Source.CreateCommand($"SELECT EXISTS(SELECT 1 FROM \"{fixture.Options.Persistence.Schema}\".\"{table}\" WHERE \"{column}\"=@id)");
        command.Parameters.AddWithValue("id", Guid.Parse(id));
        return await command.ExecuteScalarAsync() is true;
    }
    private static async Task AssertOccupancy(ManagementHostFixture fixture)
    {
        string[] tables = ["configuration_definition", "configuration_binding", "configuration_source", "configuration_publication",
            "configuration_release", "configuration_assignment", "configuration_override", "configuration_cohort", "configuration_session", "configuration_adoption"];
        string sum = string.Join("+", tables.Select(table => $"(SELECT COALESCE(sum(logical_bytes),0) FROM \"{fixture.Options.Persistence.Schema}\".\"{table}\")"));
        await using var command = fixture.Source.CreateCommand($"SELECT used_bytes,(metadata_bytes+{sum})::bigint FROM \"{fixture.Options.Persistence.Schema}\".configuration_catalog WHERE singleton");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(reader.GetInt64(1), reader.GetInt64(0), "All retained content and metadata must equal the shared logical occupancy.");
    }
}
