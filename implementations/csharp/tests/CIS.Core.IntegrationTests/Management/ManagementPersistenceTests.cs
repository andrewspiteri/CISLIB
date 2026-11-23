using System.Globalization;
using CIS.Management;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[DoNotParallelize]
public sealed class ManagementPersistenceTests
{
    private const string Precise = "2026-11-23T12:34:56.1234567Z";

    [TestMethod]
    public async Task TwoReplicasAgreeOnRegistrationAndRejectChangedOrForeignOwnership()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        ManagementRegistrationOwner owner = Owner();
        string id = Id();
        ManagementRegistrationRequest request = Registration();
        ManagementStoreResult<ManagementRegistrationReceipt>[] results = await Task.WhenAll(
            database.Store.RegisterAsync(owner, id, request), database.Replica.RegisterAsync(owner, id, request));

        Assert.IsTrue(results.All(result => result.IsKnownCommitted));
        Assert.AreEqual(results[0].Value, results[1].Value);
        Assert.AreEqual(1, results.Count(result => result.PreviouslyCommitted));
        Assert.AreEqual(1L, await database.ScalarAsync($"SELECT COUNT(*) FROM {database.Table("management_application")}"));
        Assert.AreEqual(1L, await database.ScalarAsync($"SELECT COUNT(*) FROM {database.Table("management_registration")}"));

        Assert.AreEqual("management.registration.conflict", (await database.Store.RegisterAsync(owner, id,
            request with
            {
                ApplicationVersion = "changed"
            })).Code);
        Assert.AreEqual("management.registration.conflict", (await database.Store.RegisterAsync(Owner(subject: "foreign"), id, request)).Code);
        Assert.AreEqual("management.registration.conflict", (await database.Store.RegisterAsync(Owner(application: "another-app"), id, request)).Code);
        Assert.AreEqual("management.registration.conflict", (await database.Store.RegisterAsync(Owner(environment: "another-environment"), id, request)).Code);
        Assert.AreEqual("management.registration.conflict", (await database.Store.RegisterAsync(Owner(issuer: "another-issuer"), id, request)).Code);

        ManagementInstance instance = (await database.Store.ReadInstancesAsync(results[0].Value!.ApplicationId, null, 100)).Value![0];
        Assert.AreEqual(Precise, instance.ProcessStartedAtUtc);
        Assert.AreEqual("reported-label", instance.ReportedInstanceId);
        Assert.AreEqual("notProvided", instance.InstanceEvidence.Kind);
        Assert.IsNull(instance.InstanceEvidence.VerifiedIdentifier);
    }

    [TestMethod]
    public async Task ExactApplicationNamesAreNotCaseFoldedOrUnicodeNormalized()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string[] names = ["service", "Service", "é", "e\u0301"];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            ManagementStoreResult<ManagementRegistrationReceipt> result = await database.Store.RegisterAsync(Owner(application: name), Id(), Registration());
            Assert.IsTrue(result.IsKnownCommitted);
            Assert.IsTrue(ids.Add(result.Value!.ApplicationId));
        }

        Assert.AreEqual(4L, await database.ScalarAsync($"SELECT COUNT(*) FROM {database.Table("management_application")}"));
    }

    [TestMethod]
    public async Task EqualAndObsoleteReportsDoNotRefreshReceiptOrReplaceNewerState()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        ManagementRegistrationOwner owner = Owner();
        string application = (await database.Store.RegisterAsync(owner, id, Registration())).Value!.ApplicationId;
        var initial = new ManagementObservation("9007199254740993", Precise, "ready", "healthy");
        ManagementStoreResult<ManagementReportReceipt> accepted = await database.Store.ReportObservationAsync(owner, id, initial);
        ManagementStoreResult<ManagementReportReceipt> replay = await database.Replica.ReportObservationAsync(owner, id, initial);
        Assert.IsTrue(accepted.IsKnownCommitted);
        Assert.IsTrue(replay.IsKnownCommitted);
        Assert.IsTrue(replay.PreviouslyCommitted);
        Assert.AreEqual(accepted.Value!.ReceivedAtUtc, replay.Value!.ReceivedAtUtc);
        Assert.AreEqual("replayed", replay.Value.Disposition);
        Assert.AreEqual("management.sequence.conflict", (await database.Replica.ReportObservationAsync(owner, id,
            initial with
            {
                Health = "degraded"
            })).Code);
        Assert.AreEqual("management.sequence.obsolete", (await database.Replica.ReportObservationAsync(owner, id,
            initial with
            {
                Sequence = "9007199254740992"
            })).Code);

        ManagementInstance instance = (await database.Store.ReadInstancesAsync(application, null, 100)).Value![0];
        Assert.AreEqual(initial.Sequence, instance.Observation!.Sequence);
        Assert.AreEqual(Precise, instance.Observation.ObservedAtUtc);
        Assert.AreEqual("healthy", instance.Observation.Health);
        Assert.AreEqual(accepted.Value.ReceivedAtUtc, instance.LastSeenAtUtc);
    }

    [TestMethod]
    public async Task OutOfOrderReplicaRaceLeavesOnlyHighestAcceptedSequence()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        string application = (await database.Store.RegisterAsync(Owner(), id, Registration())).Value!.ApplicationId;
        ManagementStoreResult<ManagementReportReceipt>[] results = await Task.WhenAll(
            database.Store.ReportObservationAsync(Owner(), id, new("1", Precise, "starting", "unknown")),
            database.Replica.ReportObservationAsync(Owner(), id, new("2", Precise, "ready", "healthy")));
        Assert.IsTrue(results.All(result => result.IsKnownCommitted || result.Code == "management.sequence.obsolete"));
        Assert.AreEqual("2", (await database.Store.ReadInstancesAsync(application, null, 100)).Value![0].Observation!.Sequence);
    }

    [TestMethod]
    public async Task PresenceUsesDatabaseReceiptAndCloseIsTerminalWithoutTouchingMetadataRevision()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        string application = (await database.Store.RegisterAsync(Owner(), id, Registration())).Value!.ApplicationId;
        _ = await database.Store.ReportObservationAsync(Owner(), id, new("1", Precise, "ready", "healthy"));
        await database.SqlAsync($"UPDATE {database.Table("management_registration")} SET observation_received_at=clock_timestamp()-interval '91 seconds' WHERE registration_id='{id}'::uuid");
        Assert.AreEqual("stale", (await database.Store.ReadInstancesAsync(application, null, 100)).Value![0].Presence);

        ManagementStoreResult<ManagementCloseReceipt> first = await database.Store.CloseAsync(Owner(), id);
        ManagementStoreResult<ManagementCloseReceipt> replay = await database.Replica.CloseAsync(Owner(), id);
        Assert.IsTrue(first.IsKnownCommitted);
        Assert.AreEqual(first.Value, replay.Value);
        Assert.IsTrue(replay.PreviouslyCommitted);
        Assert.AreEqual("management.registration.closed", (await database.Store.ReportObservationAsync(Owner(), id,
            new("2", Precise, "ready", "healthy"))).Code);
        Assert.AreEqual("management.registration.closed", (await database.Store.ReportInventoryAsync(Owner(), id, Inventory("1", 1))).Code);
        Assert.AreEqual("management.registration.closed", (await database.Store.RegisterAsync(Owner(), id, Registration())).Code);
        Assert.AreEqual("closed", (await database.Store.ReadInstancesAsync(application, null, 100)).Value![0].Presence);
        Assert.AreEqual("1", (await database.Store.ReadApplicationAsync(application)).Value!.MetadataRevision);
    }

    [TestMethod]
    public async Task HundredsOfJobsRemainCompleteAndRejectedReplacementPreservesPriorCatalog()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        string application = (await database.Store.RegisterAsync(Owner(), id, Registration())).Value!.ApplicationId;
        ManagementInventorySnapshot snapshot = Inventory("1", 500);
        ManagementStoreResult<ManagementReportReceipt> first = await database.Store.ReportInventoryAsync(Owner(), id, snapshot);
        Assert.IsTrue(first.IsKnownCommitted);
        ManagementStoreResult<ManagementReportReceipt> replay = await database.Replica.ReportInventoryAsync(Owner(), id, snapshot);
        Assert.AreEqual(first.Value!.ReceivedAtUtc, replay.Value!.ReceivedAtUtc);
        Assert.AreEqual("management.sequence.conflict", (await database.Replica.ReportInventoryAsync(Owner(), id, Inventory("1", 499))).Code);

        ManagementInventorySnapshot invalid = snapshot with
        {
            Sequence = "2",
            Jobs = snapshot.Jobs.Append(snapshot.Jobs[0]).ToArray()
        };
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => database.Store.ReportInventoryAsync(Owner(), id, invalid));
        ManagementInventoryEntry entry = (await database.Store.ReadInventoryAsync(application, null, 100, 8 * 1024 * 1024)).Value![0];
        Assert.AreEqual(500, entry.Inventory.Jobs.Count);
        Assert.AreEqual("1", entry.Inventory.Sequence);
        Assert.AreEqual("0", entry.Inventory.Jobs[0].EffectiveConfiguration!.Revision);
        Assert.IsNull(entry.Inventory.Jobs[0].Observations);
        Assert.AreEqual(Precise, entry.Inventory.ObservedAtUtc);
        Assert.AreEqual(first.Value.ReceivedAtUtc, entry.ReceivedAtUtc);
    }

    [TestMethod]
    public async Task SharedCapacitySerializesReplicaRacesAndRollbackLeavesNoOrphanApplication()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync(schema => new(schema,
            maximumApplications: 2, maximumRegistrationsPerApplication: 1, maximumRetainedRegistrations: 1));
        ManagementStoreResult<ManagementRegistrationReceipt>[] results = await Task.WhenAll(
            database.Store.RegisterAsync(Owner(), Id(), Registration()), database.Replica.RegisterAsync(Owner(), Id(), Registration()));
        Assert.AreEqual(1, results.Count(result => result.IsKnownCommitted));
        Assert.AreEqual(1, results.Count(result => result.Code == "management.capacity.exceeded"));
        Assert.AreEqual("management.capacity.exceeded", (await database.Store.RegisterAsync(Owner(application: "other"), Id(), Registration())).Code);
        Assert.AreEqual(1L, await database.ScalarAsync($"SELECT COUNT(*) FROM {database.Table("management_application")}"));
        Assert.AreEqual(1L, await database.ScalarAsync($"SELECT registration_count FROM {database.Table("management_capacity")}"));
    }

    [TestMethod]
    public async Task InventoryByteRefusalAndPageAdmissionPreserveWholeSnapshotsAndContinuation()
    {
        ManagementInventorySnapshot small = Inventory("1", 3);
        int bytes = ManagementWire.EncodeRequest(small).Length;
        await using ManagementDatabaseFixture limited = await ManagementDatabaseFixture.CreateAsync(schema => new(schema, maximumInventoryBytes: bytes));
        string id = Id();
        string application = (await limited.Store.RegisterAsync(Owner(), id, Registration())).Value!.ApplicationId;
        Assert.IsTrue((await limited.Store.ReportInventoryAsync(Owner(), id, small)).IsKnownCommitted);
        Assert.AreEqual("management.capacity.exceeded", (await limited.Store.ReportInventoryAsync(Owner(), id, Inventory("2", 4))).Code);
        Assert.AreEqual("1", (await limited.Store.ReadInventoryAsync(application, null, 100, 8 * 1024 * 1024)).Value![0].Inventory.Sequence);

        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string firstId = Id();
        string secondId = Id();
        application = (await database.Store.RegisterAsync(Owner(), firstId, Registration())).Value!.ApplicationId;
        _ = await database.Store.RegisterAsync(Owner(), secondId, Registration());
        _ = await database.Store.ReportInventoryAsync(Owner(), firstId, small);
        _ = await database.Store.ReportInventoryAsync(Owner(), secondId, small);
        int pageBytes = 8192 + 8192 + bytes;
        ManagementStoreResult<IReadOnlyList<ManagementInventoryEntry>> page = await database.Store.ReadInventoryAsync(application, null, 100, pageBytes);
        Assert.IsTrue(page.IsKnownCommitted);
        Assert.HasCount(1, page.Value!);
        Assert.IsTrue(page.HasMore);
        ManagementStoreResult<IReadOnlyList<ManagementInventoryEntry>> next = await database.Replica.ReadInventoryAsync(application,
            page.Value![0].RegistrationId, 100, pageBytes);
        Assert.HasCount(1, next.Value!);
        Assert.AreNotEqual(page.Value[0].RegistrationId, next.Value![0].RegistrationId);
        Assert.IsFalse(next.HasMore);
        Assert.AreEqual("management.response_too_large", (await database.Store.ReadInventoryAsync(application, null, 100, 1)).Code);
    }

    [TestMethod]
    public async Task CleanupPurgesOnlyOldTelemetryAndUnknownReportsNeverCreateRegistrations()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        string application = (await database.Store.RegisterAsync(Owner(), id, Registration())).Value!.ApplicationId;
        _ = await database.Store.ReportInventoryAsync(Owner(), id, Inventory("1", 1));
        _ = await database.Store.CloseAsync(Owner(), id);
        await database.SqlAsync($"""
            UPDATE {database.Table("management_registration")} SET registered_at=clock_timestamp()-interval '8 days',
                inventory_received_at=clock_timestamp()-interval '8 days',closed_at=clock_timestamp()-interval '8 days'
                WHERE registration_id='{id}'::uuid
            """);
        ManagementStoreResult<ManagementCleanupResult> cleanup = await database.Store.CleanupAsync(1);
        Assert.IsTrue(cleanup.IsKnownCommitted);
        Assert.AreEqual(1, cleanup.Value!.RemovedRegistrations);
        Assert.IsTrue(cleanup.Value.ReleasedInventoryBytes > 0);
        Assert.AreEqual(0L, await database.ScalarAsync($"SELECT registration_count+inventory_bytes FROM {database.Table("management_capacity")}"));
        Assert.IsNotNull((await database.Store.ReadApplicationAsync(application)).Value);
        Assert.AreEqual("management.not_found", (await database.Store.ReportObservationAsync(Owner(), id, new("2", Precise, "ready", "healthy"))).Code);
        Assert.AreEqual("management.not_found", (await database.Store.ReportInventoryAsync(Owner(), id, Inventory("2", 1))).Code);
        Assert.AreEqual(0L, await database.ScalarAsync($"SELECT COUNT(*) FROM {database.Table("management_registration")}"));
        ManagementStoreResult<ManagementRegistrationReceipt> replacement = await database.Replica.RegisterAsync(Owner(), Id(), Registration());
        Assert.IsTrue(replacement.IsKnownCommitted);
        Assert.AreEqual(application, replacement.Value!.ApplicationId);
    }

    [TestMethod]
    public async Task NativeUnknownAfterActualCommitRecoversOriginalReceiptThroughAnotherRoot()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        int commitsBefore = database.Boundary.ConfirmedNativeCommits;
        database.Boundary.FailAfterNextCommit();
        ManagementStoreResult<ManagementRegistrationReceipt> unknown = await database.Store.RegisterAsync(Owner(), id, Registration());
        Assert.AreEqual("management.transaction_unknown", unknown.Code);
        Assert.AreEqual(TransactionOutcome.Unknown, unknown.AttemptCompletion!.Outcome);
        Assert.IsFalse(unknown.IsKnownCommitted);
        Assert.IsNull(unknown.Value);
        Assert.AreEqual(commitsBefore + 1, database.Boundary.ConfirmedNativeCommits);

        ManagementStoreResult<ManagementRegistrationReceipt> recovered = await database.Replica.RegisterAsync(Owner(), id, Registration());
        Assert.IsTrue(recovered.IsKnownCommitted);
        Assert.IsTrue(recovered.PreviouslyCommitted);
        Assert.AreEqual(TransactionOutcome.NotCommitted, recovered.AttemptCompletion!.Outcome);
        Assert.AreEqual(commitsBefore + 1, database.Boundary.ConfirmedNativeCommits);
        Assert.AreEqual(1L, await database.ScalarAsync($"SELECT COUNT(*) FROM {database.Table("management_registration")}"));
    }

    [TestMethod]
    public async Task ReplayCleanupDiagnosticDoesNotDowngradeOriginalCommittedRegistration()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        ManagementStoreResult<ManagementRegistrationReceipt> first = await database.Store.RegisterAsync(Owner(), id, Registration());
        database.Boundary.FailAfterNextTransactionDisposal();
        ManagementStoreResult<ManagementRegistrationReceipt> replay = await database.Replica.RegisterAsync(Owner(), id, Registration());
        Assert.AreEqual(first.Value, replay.Value);
        Assert.IsTrue(replay.IsKnownCommitted);
        Assert.IsTrue(replay.PreviouslyCommitted);
        Assert.AreEqual(TransactionOutcome.NotCommitted, replay.AttemptCompletion!.Outcome);
        Assert.HasCount(1, replay.AttemptCompletion.CleanupErrors);
    }

    [TestMethod]
    public async Task NativeInstanceEvidenceRemainsSeparateFromReportedRegistrationLabel()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        var native = new ManagementRegistrationOwner("managed-app", "test", "native-issuer", "native-subject",
            new("verified", "native-reported", "native-verified"));
        string application = (await database.Store.RegisterAsync(native, Id(), Registration())).Value!.ApplicationId;
        ManagementInstance instance = (await database.Store.ReadInstancesAsync(application, null, 100)).Value![0];
        Assert.AreEqual("reported-label", instance.ReportedInstanceId);
        Assert.AreEqual("verified", instance.InstanceEvidence.Kind);
        Assert.AreEqual("native-reported", instance.InstanceEvidence.ReportedIdentifier);
        Assert.AreEqual("native-verified", instance.InstanceEvidence.VerifiedIdentifier);
    }

    [TestMethod]
    public async Task MetadataCompareAndSwapUsesOriginalOwnerAndTelemetryNeverChangesItsRevision()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        string application = (await database.Store.RegisterAsync(Owner(), id, Registration())).Value!.ApplicationId;
        await using ILocalTransaction first = await database.Factory.BeginAsync("management");
        await using ILocalTransaction second = await database.Factory.BeginAsync("management");
        Assert.IsNotNull(await database.Store.TryReplaceMetadataAsync(first.Context, application, "1", new("First")));
        Task<ManagementApplication?> competing = database.Replica.TryReplaceMetadataAsync(second.Context, application, "1", new("Second"));
        Assert.AreEqual(TransactionOutcome.Committed, (await first.CommitAsync()).Outcome);
        Assert.IsNull(await competing);
        _ = await second.RollbackAsync();
        ManagementApplication beforeTelemetry = (await database.Store.ReadApplicationAsync(application)).Value!;
        Assert.AreEqual("First", beforeTelemetry.Metadata.DisplayName);
        Assert.AreEqual("2", beforeTelemetry.MetadataRevision);
        _ = await database.Store.ReportObservationAsync(Owner(), id, new("1", Precise, "ready", "healthy"));
        _ = await database.Store.ReportInventoryAsync(Owner(), id, Inventory("1", 1));
        Assert.AreEqual(beforeTelemetry, (await database.Replica.ReadApplicationAsync(application)).Value);
    }

    [TestMethod]
    public async Task SchemaValidationAndReconstructionReadExistingDurableStateWithoutMigration()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        string application = (await database.Store.RegisterAsync(Owner(), id, Registration())).Value!.ApplicationId;
        _ = await database.Store.ReportObservationAsync(Owner(), id, new("7", Precise, "ready", "healthy"));
        _ = await database.Store.ReportInventoryAsync(Owner(), id, Inventory("9", 2));
        await using (ILocalTransaction check = await database.Factory.BeginAsync("management"))
        {
            await ManagementSchema.ValidateAsync(check.Context, database.Options.Schema);
            Assert.AreEqual(TransactionOutcome.NotCommitted, (await check.RollbackAsync()).Outcome);
        }
        var reconstructed = new PostgreSqlManagementStore(database.Factory, database.Options);
        ManagementInstance instance = (await reconstructed.ReadInstancesAsync(application, null, 100)).Value![0];
        Assert.AreEqual("7", instance.Observation!.Sequence);
        Assert.AreEqual("9", instance.InventorySequence);
        Assert.AreEqual("management.sequence.obsolete", (await reconstructed.ReportObservationAsync(Owner(), id,
            new("6", Precise, "ready", "healthy"))).Code);
        Assert.AreEqual("1", (await reconstructed.ReadApplicationAsync(application)).Value!.MetadataRevision);
    }

    [TestMethod]
    public async Task UnsupportedSchemaFailsReadinessAndExplicitInstallDoesNotRewriteIt()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        await database.SqlAsync($"""
            ALTER TABLE {database.Table("management_schema_version")} DROP CONSTRAINT management_schema_version_version_check;
            UPDATE {database.Table("management_schema_version")} SET version={ManagementSchema.Version + 1}
            """);
        await using ILocalTransaction read = await database.Factory.BeginAsync("management");
        await Assert.ThrowsExactlyAsync<ManagementSchemaException>(() => ManagementSchema.ValidateAsync(read.Context, database.Options.Schema));
        _ = await read.RollbackAsync();
        await using ILocalTransaction install = await database.Factory.BeginAsync("management");
        await Assert.ThrowsExactlyAsync<ManagementSchemaException>(() => ManagementSchema.InstallAsync(install.Context,
            database.Options.Schema, database.Options));
        _ = await install.RollbackAsync();
        Assert.AreEqual((long)ManagementSchema.Version + 1, await database.ScalarAsync($"SELECT version FROM {database.Table("management_schema_version")}"));
    }

    [TestMethod]
    public async Task MissingDatabaseStateReturnsUnavailableWithoutAnInMemorySuccessFallback()
    {
        await using ManagementDatabaseFixture database = await ManagementDatabaseFixture.CreateAsync();
        string id = Id();
        _ = await database.Store.RegisterAsync(Owner(), id, Registration());
        await database.SqlAsync($"DROP TABLE {database.Table("management_registration")}");
        ManagementStoreResult<ManagementRegistrationReceipt> result = await database.Replica.RegisterAsync(Owner(), id, Registration());
        Assert.AreEqual("management.unavailable", result.Code);
        Assert.IsNull(result.Value);
        Assert.IsFalse(result.IsKnownCommitted);
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.AttemptCompletion!.Outcome);
    }

    private static string Id() => Guid.NewGuid().ToString("D");

    internal static ManagementRegistrationOwner Owner(string application = "managed-app", string environment = "test",
        string issuer = "test-issuer", string subject = "workload-subject")
        => new(application, environment, issuer, subject, new("notProvided"));

    internal static ManagementRegistrationRequest Registration() => new(1, "reported-label", Precise, "1.0.0");

    internal static ManagementInventorySnapshot Inventory(string sequence, int jobs) => new(sequence, Precise,
        [new("jobs", "1")], [new("managed-app", "maintenance", "Maintenance", string.Empty)],
        Enumerable.Range(0, jobs).Select(index => new ManagementJobInventory("managed-app", "job-" + index.ToString(CultureInfo.InvariantCulture),
            "maintenance", "Job " + index.ToString(CultureInfo.InvariantCulture), ["continuous"],
            EffectiveConfiguration: new("0", false))).ToArray());
}
