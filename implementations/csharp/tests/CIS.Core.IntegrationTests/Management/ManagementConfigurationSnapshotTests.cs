using System.Net;
using System.Net.Http.Headers;
using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Tests.IntegrationTests.Operations;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationSnapshotTests
{
    [TestMethod]
    public async Task NativeFrozenCohortResolvesOnePinnedPairAndExplicitEmptyReleaseWithoutReportedLabelAuthority()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        SnapshotSetup setup = await Prepare(fixture);
        HttpClient workload = fixture.Client("workload-a");
        string registration = await Register(workload, "same-reported-instance");
        using HttpResponseMessage read = await workload.GetAsync(Path(registration));
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode, await read.Content.ReadAsStringAsync());
        ManagementConfigurationSnapshot snapshot = await Decode<ManagementConfigurationSnapshot>(read);
        Assert.AreEqual("canary", snapshot.Desired.CohortId);
        Assert.AreEqual("FirstHost", snapshot.Desired.HostApplicationName);
        Assert.AreEqual(setup.PopulatedRelease, snapshot.Desired.ReleaseId);
        Assert.AreEqual("{\"Greeting\":\"Café — 日本語\"}", snapshot.CommonSource.PayloadJson);
        Assert.AreEqual("{\"Enabled\":false}", snapshot.ApplicationSource.PayloadJson);
        Assert.AreEqual(snapshot.ETag, read.Headers.ETag!.Tag);

        // Change only this owned fixture's trusted native mapping. Existing session capture must not be recomputed.
        await using (NpgsqlCommand change = fixture.Source.CreateCommand($"""
            UPDATE "{fixture.Options.Persistence.Schema}".configuration_cohort SET cohort_id='stable'
            WHERE application_id=@application AND issuer=@issuer AND subject='workload-a'
            """))
        {
            change.Parameters.AddWithValue("application", Guid.Parse(fixture.ApplicationA));
            change.Parameters.AddWithValue("issuer", ManagementHostFixture.Issuer);
            Assert.AreEqual(1, await change.ExecuteNonQueryAsync());
        }
        string later = await Register(workload, "same-reported-instance");
        using HttpResponseMessage defaults = await workload.GetAsync(Path(later));
        Assert.AreEqual(HttpStatusCode.OK, defaults.StatusCode);
        ManagementConfigurationSnapshot empty = await Decode<ManagementConfigurationSnapshot>(defaults);
        Assert.AreEqual("stable", empty.Desired.CohortId);
        Assert.AreEqual(setup.EmptyRelease, empty.Desired.ReleaseId);
        Assert.AreEqual("absent", empty.CommonSource.State);
        Assert.AreEqual("absent", empty.ApplicationSource.State);
        using (HttpResponseMessage retained = await Conditional(workload, registration, snapshot.ETag))
        {
            Assert.AreEqual(HttpStatusCode.NotModified, retained.StatusCode);
            Assert.AreEqual(snapshot.ETag, retained.Headers.ETag!.Tag);
            Assert.IsEmpty(await retained.Content.ReadAsByteArrayAsync());
            Assert.IsTrue(retained.Headers.CacheControl!.NoStore);
        }
        using (HttpResponseMessage otherOwner = await fixture.Client("workload-b").GetAsync(Path(registration)))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, otherOwner.StatusCode);
        }
        using (HttpResponseMessage user = await fixture.Client("operator-a").GetAsync(Path(registration)))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, user.StatusCode);
        }
        using (HttpResponseMessage selection = await workload.GetAsync(Path(registration) + "&cohortId=stable"))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, selection.StatusCode);
        }
        using (HttpResponseMessage unsupported = await workload.GetAsync(Path(registration).Replace("schemaVersion=1", "schemaVersion=2", StringComparison.Ordinal)))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, unsupported.StatusCode);
        }
        using (HttpResponseMessage closed = await workload.DeleteAsync("registrations/" + registration))
        {
            Assert.AreEqual(HttpStatusCode.OK, closed.StatusCode);
        }
        using HttpResponseMessage afterClose = await Conditional(workload, registration, snapshot.ETag);
        Assert.AreEqual(HttpStatusCode.Conflict, afterClose.StatusCode, "A retained token cannot bypass the live registration read.");
    }

    [TestMethod]
    public async Task UnassignedAndPreCatalogSessionsNeverBecomeAnImplicitDefaultsSnapshot()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "snapshot-catalog");
        HttpClient workload = fixture.Client("workload-a");
        foreach (string registration in new[] { fixture.RegistrationA, await Register(workload, "unassigned") })
        {
            using HttpResponseMessage response = await workload.GetAsync(Path(registration));
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.AreEqual("management.configuration.not_found", (await Decode<ManagementConfigurationProblem>(response)).Code);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualNativeCleanupFailureWithholdsCompleteAndConditionalSnapshots(bool conditional)
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        _ = await Prepare(fixture);
        string registration = await Register(fixture.Client("workload-a"), "cleanup");
        var boundary = new NativeCommitBoundarySource(fixture.Source);
        await using ServiceProvider provider = ReadProvider(boundary);
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        var authority = new PostgreSqlManagementAuthority(factory, fixture.Options);
        var store = new PostgreSqlManagementStore(factory, fixture.Options.Persistence, authority);
        ManagementStoreResult<ManagementResolvedConfiguration> original = await store.ReadRegistrationConfigurationAsync(Owner(), registration, "shared.settings", "1");
        Assert.IsTrue(original.IsKnownCommitted, original.Code);
        boundary.FailAfterNextTransactionDisposal();
        ManagementStoreResult<ManagementResolvedConfiguration> failed = await store.ReadRegistrationConfigurationAsync(Owner(), registration,
            "shared.settings", "1", conditional ? original.Value!.ETag : null);
        Assert.IsNull(failed.Value, "Neither content nor a 304 decision escapes failed original read cleanup.");
        Assert.AreEqual("management.authority.unavailable", failed.Code);
        Assert.HasCount(1, failed.AttemptCompletion!.CleanupErrors);
        Assert.IsFalse(authority.IsReady);
    }

    [TestMethod]
    public async Task ConditionalSnapshotWaitsForTheActualNativeReadOwnerToDrain()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        _ = await Prepare(fixture);
        string registration = await Register(fixture.Client("workload-a"), "held-read");
        await using ServiceProvider provider = ReadProvider(new NativeCommitBoundarySource(fixture.Source));
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        var authority = new PostgreSqlManagementAuthority(factory, fixture.Options);
        var store = new PostgreSqlManagementStore(factory, fixture.Options.Persistence, authority);
        ManagementResolvedConfiguration snapshot = (await store.ReadRegistrationConfigurationAsync(Owner(), registration, "shared.settings", "1")).Value!;
        var held = new HeldSnapshotReadFactory(factory);
        var heldAuthority = new PostgreSqlManagementAuthority(held, fixture.Options);
        var heldStore = new PostgreSqlManagementStore(held, fixture.Options.Persistence, heldAuthority);
        Task<ManagementStoreResult<ManagementResolvedConfiguration>> pending = heldStore.ReadRegistrationConfigurationAsync(Owner(), registration,
            "shared.settings", "1", snapshot.ETag);
        try
        {
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(pending.IsCompleted, "Completed SQL and rollback alone cannot release a conditional response before owned disposal.");
        }
        finally
        {
            held.Release.TrySetResult();
        }
        ManagementStoreResult<ManagementResolvedConfiguration> result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(result.IsKnownCommitted, result.Code);
        Assert.IsTrue(result.Value!.NotModified);
        Assert.IsNull(result.Value.Snapshot);
        Assert.AreEqual(snapshot.ETag, result.Value.ETag);
        Assert.IsFalse(heldAuthority.IsReady, "A snapshot read must not become a new control/readiness proof.");
    }

    private static async Task<SnapshotSetup> Prepare(ManagementHostFixture fixture)
    {
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "snapshot-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        ManagementConfigurationDefinition common = definitions.Items.Single(item => item.Kind == "common");
        ManagementConfigurationDefinition application = definitions.Items.Single(item => item.ApplicationId == fixture.ApplicationA);
        string commonId;
        string applicationId;
        using (HttpResponseMessage response = await Publish(actor, await Source(actor, common.SourceId), "snapshot-common", "{\"Greeting\":\"Café — 日本語\"}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            commonId = (await Decode<ManagementConfigurationOperationResult>(response)).Result!.PublicationId!;
        }
        using (HttpResponseMessage response = await Publish(actor, await Source(actor, application.SourceId), "snapshot-application", "{\"Enabled\":false}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            applicationId = (await Decode<ManagementConfigurationOperationResult>(response)).Result!.PublicationId!;
        }
        string path = "applications/" + fixture.ApplicationA + "/configuration/shared.settings";
        ManagementConfigurationAssignmentMap before = await Assignment(actor, path);
        string populated = await Release(actor, path, before.ETag, "snapshot-populated", new(1, new("present", commonId),
            new("present", applicationId), "Pin exact snapshot sources"));
        string empty = await Release(actor, path, before.ETag, "snapshot-empty", new(1, new("absent"), new("absent"), "Explicit defaults release"));
        using var assignment = new HttpRequestMessage(HttpMethod.Put, path + "/assignments?schemaVersion=1");
        assignment.Headers.Add("Idempotency-Key", "snapshot-assignment");
        assignment.Headers.TryAddWithoutValidation("If-Match", before.ETag);
        assignment.Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(
            new ManagementConfigurationAssignmentCommand(1, "replace", empty, [new("canary", populated)], "Assign complete snapshot map")));
        assignment.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage assigned = await actor.SendAsync(assignment);
        Assert.AreEqual(HttpStatusCode.OK, assigned.StatusCode, await assigned.Content.ReadAsStringAsync());
        return new(populated, empty);
    }

    private static async Task<ManagementConfigurationAssignmentMap> Assignment(HttpClient actor, string path)
    {
        using HttpResponseMessage response = await actor.GetAsync(path + "/assignments?schemaVersion=1");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationAssignmentMap>(response);
    }

    private static async Task<string> Release(HttpClient actor, string path, string etag, string key, ManagementConfigurationReleaseCommand command)
    {
        using HttpResponseMessage response = await Send(actor, path + "/releases?schemaVersion=1", key, etag, command);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Decode<ManagementConfigurationOperationResult>(response)).Result!.ReleaseId!;
    }

    private static async Task<string> Register(HttpClient workload, string reported)
    {
        string id = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + id);
        request.Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request(reported)));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await workload.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return id;
    }

    private static async Task<HttpResponseMessage> Conditional(HttpClient workload, string registration, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path(registration));
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return await workload.SendAsync(request);
    }

    private static string Path(string registration) => "registrations/" + registration + "/configuration/shared.settings?schemaVersion=1";

    private static ManagementRegistrationOwner Owner() => new("managed-a", "Development", ManagementHostFixture.Issuer, "workload-a", new("notProvided"));

    private static ServiceProvider ReadProvider(NativeCommitBoundarySource source)
    {
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("management", _ => source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private sealed record SnapshotSetup(string PopulatedRelease, string EmptyRelease);

    // This existing native-owner pattern holds only final disposal. SQL, rollback and completion remain real PostgreSQL facts.
    private sealed class HeldSnapshotReadFactory(ILocalTransactionFactory inner) : ILocalTransactionFactory
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
            => new HeldOwner(await inner.BeginAsync(databaseName, cancellationToken), this);

        public async Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options, CancellationToken cancellationToken = default)
            => new HeldOwner(await inner.BeginAsync(databaseName, options, cancellationToken), this);

        private sealed class HeldOwner(ILocalTransaction owner, HeldSnapshotReadFactory held) : ILocalTransaction
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
