using System.Net;
using System.Net.Http.Headers;
using CIS.CommonTesting;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[DoNotParallelize]
public sealed class ManagementBackendTests
{
    [TestMethod]
    [DataRow(ManagementDeploymentMode.Standalone)]
    [DataRow(ManagementDeploymentMode.Shared)]
    public async Task RealProductModesConfirmOnePersistedAuthorityAcrossReplicasWithoutRenewingPresence(ManagementDeploymentMode mode)
    {
        int replicas = mode == ManagementDeploymentMode.Shared ? 2 : 1;
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: replicas,
            backendOptions: new(mode: mode, namespaceName: "native-management"));
        ManagementControlConfirmation? original = null;
        for (int replica = 0; replica < replicas; replica++)
        {
            string attempt = Guid.NewGuid().ToString("D");
            using HttpResponseMessage response = await fixture.Client(replica: replica).GetAsync(Control(fixture.RegistrationA, attempt));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
            ManagementControlConfirmation actual = await Decode<ManagementControlConfirmation>(response);
            Assert.IsTrue(actual.AdmissionAllowed);
            Assert.AreEqual(attempt, actual.AttemptId);
            Assert.AreEqual(fixture.RegistrationA, actual.RegistrationId);
            Assert.AreEqual(15000, actual.ValidForMs);
            Assert.IsTrue(ManagementWire.IsUuid(actual.AuthorityId));
            if (original is not null)
            {
                Assert.AreEqual(original.AuthorityId, actual.AuthorityId);
                Assert.AreEqual(original.ControlRevision, actual.ControlRevision);
            }
            original = actual;
        }
        using HttpResponseMessage information = await fixture.Client().GetAsync("service-info");
        ManagementServiceInfo info = await Decode<ManagementServiceInfo>(information);
        Assert.AreEqual(fixture.Options.Persistence.WireLimits.MaximumInventoryRequestBytes, info.MaximumInventoryRequestBytes);
        Assert.IsTrue(info.Features.Any(feature => feature.OperationId == "getRegistrationControl"));
        await using var query = fixture.Source.CreateCommand($"SELECT count(*) FROM {Table(fixture, "management_registration")} WHERE observation_received_at IS NOT NULL");
        Assert.AreEqual(0L, (long)(await query.ExecuteScalarAsync())!, "Control is not a presence refresh.");
    }

    [TestMethod]
    public async Task ActualControlRequiresExactOwnedRegistrationAndOneClosedAttemptQuery()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        string attempt = Guid.NewGuid().ToString("D");
        foreach (string query in new[] { "", "?attemptId=invalid", "?attemptId=" + attempt + "&attemptId=" + attempt,
            "?AttemptId=" + attempt, "?attemptId=" + attempt + "&unknown=1" })
        {
            using HttpResponseMessage invalid = await fixture.Client().GetAsync("registrations/" + fixture.RegistrationA + "/control" + query);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using (HttpResponseMessage foreign = await fixture.Client("workload-b").GetAsync(Control(fixture.RegistrationA, attempt)))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        using (HttpResponseMessage ungranted = await fixture.Client("reader").GetAsync(Control(fixture.RegistrationA, attempt)))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, ungranted.StatusCode);
        }
        using (HttpResponseMessage closed = await fixture.Client().DeleteAsync("registrations/" + fixture.RegistrationA))
        {
            Assert.AreEqual(HttpStatusCode.OK, closed.StatusCode);
        }
        using HttpResponseMessage afterClose = await fixture.Client().GetAsync(Control(fixture.RegistrationA, Guid.NewGuid().ToString("D")));
        Assert.AreEqual(HttpStatusCode.Conflict, afterClose.StatusCode);
    }

    [TestMethod]
    public async Task RecoveryHoldBlocksFreshAuthorityAndEffectsButRetainsOriginalRecoveryAndLiveDiagnostics()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        HttpClient actor = fixture.Client("operator-a");
        ManagementApplication before = await Application(actor, fixture.ApplicationA);
        using HttpResponseMessage accepted = await Replace(actor, fixture.ApplicationA, "retained-original", before.MetadataETag, "Retained original");
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        ManagementMetadataResult original = await Decode<ManagementMetadataResult>(accepted);
        await Sql(fixture, $"UPDATE {Table(fixture, "management_authority")} SET recovery_hold=true,control_revision='held-native-revision'");
        using (HttpResponseMessage control = await fixture.Client().GetAsync(Control(fixture.RegistrationA, Guid.NewGuid().ToString("D"))))
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, control.StatusCode);
            Assert.AreEqual("management.authority.recovery_required", (await Decode<ManagementProblem>(control)).Code);
        }
        using (HttpResponseMessage registration = await ManagementServiceTests.Put(fixture.Client(),
            "registrations/" + Guid.NewGuid().ToString("D"), ManagementHostFixture.Request("held-bootstrap")))
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, registration.StatusCode);
        }
        using (HttpResponseMessage refused = await Replace(actor, fixture.ApplicationA, "new-held-effect", original.Result!.MetadataETag, "Must not apply"))
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        }
        using (HttpResponseMessage found = await actor.GetAsync("applications/" + fixture.ApplicationA + "/metadata-operations/retained-original"))
        {
            Assert.AreEqual(HttpStatusCode.OK, found.StatusCode);
            ManagementMetadataResult retained = await Decode<ManagementMetadataResult>(found);
            Assert.AreEqual("found", retained.ResultSource);
            Assert.AreEqual("committed", retained.OriginalCompletion);
            Assert.AreEqual(original.Result, retained.Result);
        }
        using (var operational = new HttpClient())
        {
            string origin = "http://127.0.0.1:" + fixture.OperationalPorts[0];
            using HttpResponseMessage health = await operational.GetAsync(origin + "/_health");
            using HttpResponseMessage ready = await operational.GetAsync(origin + "/_health/ready");
            Assert.AreEqual(HttpStatusCode.OK, health.StatusCode, "Recovery hold does not turn process liveness into a critical failure.");
            Assert.AreEqual(HttpStatusCode.InternalServerError, ready.StatusCode);
        }
        await Sql(fixture, $"UPDATE {Table(fixture, "management_authority")} SET recovery_hold=false,control_revision='reconciled-native-revision'");
        using (HttpResponseMessage reconciled = await fixture.Client().GetAsync(Control(fixture.RegistrationA, Guid.NewGuid().ToString("D"))))
        {
            Assert.AreEqual("reconciled-native-revision", (await Decode<ManagementControlConfirmation>(reconciled)).ControlRevision);
        }
        await Sql(fixture, $"ALTER TABLE {Table(fixture, "management_authority")} RENAME TO management_authority_unavailable");
        try
        {
            using HttpResponseMessage unavailable = await fixture.Client().GetAsync(Control(fixture.RegistrationA, Guid.NewGuid().ToString("D")));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode,
                "A successful earlier authority read is not a cache fallback for a failed current lookup.");
        }
        finally
        {
            await Sql(fixture, $"ALTER TABLE {Table(fixture, "management_authority_unavailable")} RENAME TO management_authority");
        }
        ManagementApplication unchanged = await Application(actor, fixture.ApplicationA);
        Assert.AreEqual(original.Result!.MetadataRevision, unchanged.MetadataRevision);
        Assert.AreEqual(original.Result.Metadata, unchanged.Metadata);
    }

    [TestMethod]
    public async Task NamespaceEpochAndChangedAuthorityCannotSilentlyRebindAndRetainedReadinessExpiresWithoutIo()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var options = new ManagementServiceOptions(database.Source.ConnectionString, new byte[32], database.Options);
        var authority = new PostgreSqlManagementAuthority(database.Factory, options, clock);
        ManagementAuthoritySnapshot original = await authority.ProbeAsync();
        int rollbacks = database.Boundary.RollbackInvocations;
        Assert.IsTrue(authority.IsReady);
        Assert.AreEqual(rollbacks, database.Boundary.RollbackInvocations);
        clock.Advance(TimeSpan.FromSeconds(16));
        Assert.IsFalse(authority.IsReady);
        _ = await authority.ProbeAsync();
        Assert.IsTrue(authority.IsReady);
        var wrongEpoch = new PostgreSqlManagementAuthority(database.Factory,
            new(database.Source.ConnectionString, new byte[32], database.Options, backend: new(expectedRecoveryEpoch: 2)));
        Assert.AreEqual("management.authority.mismatch",
            (await Assert.ThrowsExactlyAsync<ManagementBackendException>(() => wrongEpoch.ProbeAsync())).Code);
        var wrongNamespace = new PostgreSqlManagementAuthority(database.Factory,
            new(database.Source.ConnectionString, new byte[32], database.Options, backend: new(namespaceName: "another")));
        await Assert.ThrowsExactlyAsync<ManagementBackendException>(() => wrongNamespace.ProbeAsync());
        await database.SqlAsync($"UPDATE {database.Table("management_authority")} SET authority_id='{Guid.NewGuid():D}'::uuid");
        await Assert.ThrowsExactlyAsync<ManagementBackendException>(() => authority.ProbeAsync());
        Assert.IsFalse(authority.IsReady);
        Assert.AreEqual(original.AuthorityId, authority.Current.AuthorityId);
    }

    [TestMethod]
    public async Task UncertainNativeControlReadCleanupDoesNotIssueFreshConfirmation()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        var authority = new PostgreSqlManagementAuthority(database.Factory,
            new(database.Source.ConnectionString, new byte[32], database.Options));
        var store = new PostgreSqlManagementStore(database.Factory, database.Options, authority);
        var owner = new ManagementRegistrationOwner("owned", "Development", "native", "subject", new("notProvided"));
        string registration = Guid.NewGuid().ToString("D");
        Assert.IsTrue((await store.RegisterAsync(owner, registration, ManagementHostFixture.Request("control-cleanup"))).IsKnownCommitted);
        database.Boundary.FailAfterNextTransactionDisposal();
        ManagementStoreResult<ManagementControlConfirmation> result = await store.ConfirmControlAsync(owner, registration,
            Guid.NewGuid().ToString("D"), 15000);
        Assert.IsNull(result.Value);
        Assert.AreEqual("management.authority.unavailable", result.Code);
        Assert.IsNotNull(result.AttemptCompletion);
        Assert.HasCount(1, result.AttemptCompletion.CleanupErrors);
        Assert.IsFalse(authority.IsReady);
    }

    [TestMethod]
    public async Task ReadinessIsNotRefreshedByAnUncompletedOriginalOrFailedNativeProbeCleanup()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        var authority = new PostgreSqlManagementAuthority(database.Factory,
            new(database.Source.ConnectionString, new byte[32], database.Options));
        await using (ILocalTransaction owner = await database.Factory.BeginAsync("management"))
        {
            _ = await authority.ReadAsync(owner.Context, true);
            Assert.IsFalse(authority.IsReady, "A qualified but unfinished original context is not a completed readiness probe.");
            _ = await owner.RollbackAsync();
        }
        Assert.IsFalse(authority.IsReady);
        _ = await authority.ProbeAsync();
        Assert.IsTrue(authority.IsReady);
        authority.MarkUnavailable();
        database.Boundary.FailAfterNextTransactionDisposal();
        await Assert.ThrowsExactlyAsync<ManagementBackendException>(() => authority.ProbeAsync());
        Assert.IsFalse(authority.IsReady);
        authority.StopObserving();
        await Assert.ThrowsExactlyAsync<ManagementBackendException>(() => authority.ProbeAsync());
        Assert.IsFalse(authority.IsReady);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProbeJoinsHeldOwnerDisposalAndNeverStartsFreshnessAtLateArrival(bool expire)
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var held = new HeldReadCleanupFactory(database.Factory);
        var authority = new PostgreSqlManagementAuthority(held,
            new(database.Source.ConnectionString, new byte[32], database.Options), clock);
        Task<ManagementAuthoritySnapshot> probe = authority.ProbeAsync();
        bool canceled = false;
        try
        {
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(authority.IsReady);
            Assert.IsFalse(probe.IsCompleted);
            if (expire)
            {
                clock.Advance(TimeSpan.FromSeconds(16));
                Assert.IsFalse(probe.IsCompleted, "A deadline cannot abandon actual owned disposal.");
            }
        }
        finally
        {
            held.Release.TrySetResult();
            try
            {
                _ = await probe;
            }
            catch (OperationCanceledException) when (expire)
            {
                canceled = true;
            }
        }
        Assert.AreEqual(expire, canceled);
        Assert.AreEqual(!expire, authority.IsReady);
    }

    [TestMethod]
    public async Task ActualSessionWithSynchronousCommitDisabledCannotQualifyAsDurableAuthority()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        OwnedPostgresService service = await IntegrationServices.GetPostgresAsync();
        using IDisposable lease = service.AcquireClientLease();
        var connection = new NpgsqlConnectionStringBuilder(service.CreateConnectionString(2)) { Options = "-c synchronous_commit=off" };
        await using NpgsqlDataSource source = NpgsqlDataSource.Create(connection.ConnectionString);
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("management", _ => source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        await using ServiceProvider root = services.BuildServiceProvider();
        var authority = new PostgreSqlManagementAuthority(root.GetRequiredService<ILocalTransactionFactory>(),
            new(connection.ConnectionString, new byte[32], database.Options));
        ManagementBackendException failure = await Assert.ThrowsExactlyAsync<ManagementBackendException>(() => authority.ProbeAsync());
        Assert.AreEqual("management.backend.profile_invalid", failure.Code);
        Assert.IsFalse(authority.IsReady);
    }

    [TestMethod]
    public async Task ExplicitVersionOneUpgradePreservesMetadataOriginalJournalAndAuditAcrossActualHostReconstruction()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        HttpClient actor = fixture.Client("operator-a");
        ManagementApplication before = await Application(actor, fixture.ApplicationA);
        using HttpResponseMessage applied = await Replace(actor, fixture.ApplicationA, "before-upgrade", before.MetadataETag, "Before upgrade");
        ManagementMetadataResult original = await Decode<ManagementMetadataResult>(applied);
        await fixture.StopReplicasAsync();
        await Sql(fixture, $"""
            DROP TABLE {Table(fixture, "management_authority")};
            ALTER TABLE {Table(fixture, "management_schema_version")} DROP CONSTRAINT management_schema_version_version_check;
            UPDATE {Table(fixture, "management_schema_version")} SET version=1;
            ALTER TABLE {Table(fixture, "management_schema_version")} ADD CONSTRAINT management_schema_version_version_check CHECK(version=1)
            """);
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("management", _ => fixture.Source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        await using ServiceProvider root = services.BuildServiceProvider();
        await ManagementDatabase.InstallAsync(root.GetRequiredService<ILocalTransactionFactory>(), fixture.Options, CancellationToken.None);
        await fixture.StartReplicaAsync();
        HttpClient reconstructed = fixture.Client("operator-a", replica: fixture.Origins.Count - 1);
        using HttpResponseMessage found = await reconstructed.GetAsync("applications/" + fixture.ApplicationA + "/metadata-operations/before-upgrade");
        ManagementMetadataResult recovered = await Decode<ManagementMetadataResult>(found);
        Assert.AreEqual(HttpStatusCode.OK, found.StatusCode);
        Assert.AreEqual(original.Operation, recovered.Operation);
        Assert.AreEqual(original.Result, recovered.Result);
        Assert.AreEqual("committed", recovered.OriginalCompletion);
        using HttpResponseMessage replay = await Replace(reconstructed, fixture.ApplicationA, "before-upgrade", before.MetadataETag, "Before upgrade");
        Assert.AreEqual("replayed", (await Decode<ManagementMetadataResult>(replay)).ResultSource);
        await using var count = fixture.Source.CreateCommand($"SELECT count(*) FROM \"{fixture.Options.OperationsSchema}\".operation_journal");
        Assert.AreEqual(1L, (long)(await count.ExecuteScalarAsync())!);
        await using var audit = fixture.Source.CreateCommand($"SELECT count(*) FROM \"{fixture.Options.MessagingSchema}\".outbox");
        Assert.AreEqual(1L, (long)(await audit.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public void UnsupportedProvidersIncompleteCapabilitiesAndAsyncFallbackAreRejectedBeforeIo()
    {
        // An omitted target selection is Npgsql's default, not standby fallback.
        // Actual primary/read-write/durability qualification still occurs in SQL.
        Assert.AreEqual("Host=primary", new ManagementServiceOptions("Host=primary", new byte[32]).ConnectionString);
        Assert.ThrowsExactly<NotSupportedException>(() => new ManagementBackendOptions(provider: ManagementBackendProvider.Redis));
        Assert.ThrowsExactly<NotSupportedException>(() => new ManagementBackendOptions(provider: ManagementBackendProvider.CosmosDb));
        Assert.ThrowsExactly<NotSupportedException>(() => new ManagementBackendCapabilities("limited", "cache-only", false, false, false, false, 1024)
            .Validate(ManagementDeploymentMode.Shared));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementServiceOptions("Host=first,second", new byte[32]));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementServiceOptions("Host=first;Target Session Attributes=standby", new byte[32]));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementServiceOptions("Host=first;Target Session Attributes=read-only", new byte[32]));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementServiceOptions("Host=first;Target Session Attributes=prefer-standby", new byte[32]));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementBackendOptions(expectedRecoveryEpoch: 0));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementBackendOptions(controlValidForMs: 15001));
    }

    private static string Table(ManagementHostFixture fixture, string table) => $"\"{fixture.Options.Persistence.Schema}\".\"{table}\"";
    private static string Control(string registration, string attempt) => "registrations/" + registration + "/control?attemptId=" + attempt;
    private static Task<T> Decode<T>(HttpResponseMessage response) where T : class => ManagementServiceTests.Decode<T>(response);
    private static async Task Sql(ManagementHostFixture fixture, string sql)
    {
        await using var command = fixture.Source.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<ManagementApplication> Application(HttpClient client, string application)
    {
        using HttpResponseMessage response = await client.GetAsync("applications/" + application);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await Decode<ManagementApplication>(response);
    }
    private static async Task<HttpResponseMessage> Replace(HttpClient client, string application, string key, string etag, string displayName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "applications/" + application + "/metadata");
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = new ByteArrayContent(ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, displayName)));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.SendAsync(request);
    }

    // Only the final public owner-disposal wait is held. All SQL, rollback and
    // completion facts are those of the existing real native factory/owner.
    private sealed class HeldReadCleanupFactory(ILocalTransactionFactory inner) : ILocalTransactionFactory
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
            => new HeldOwner(await inner.BeginAsync(databaseName, cancellationToken), this);

        public async Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options,
            CancellationToken cancellationToken = default)
            => new HeldOwner(await inner.BeginAsync(databaseName, options, cancellationToken), this);

        private sealed class HeldOwner(ILocalTransaction owner, HeldReadCleanupFactory held) : ILocalTransaction
        {
            public ILocalTransactionContext Context => owner.Context;
            public LocalTransactionStatus Status => owner.Status;
            public TransactionCompletion? Completion => owner.Completion;
            public Task<TransactionCompletion> CommitAsync(CancellationToken cancellationToken = default) => owner.CommitAsync(cancellationToken);
            public Task<TransactionCompletion> RollbackAsync() => owner.RollbackAsync();
            public async ValueTask DisposeAsync()
            {
                held.Entered.TrySetResult();
                await held.Release.Task;
                await owner.DisposeAsync();
            }
        }
    }
}
