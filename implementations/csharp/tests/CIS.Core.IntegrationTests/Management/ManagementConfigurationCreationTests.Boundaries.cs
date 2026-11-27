using System.Collections;
using System.Net;
using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementConfigurationCreationTests
{
    [TestMethod]
    public async Task CreationAuditCapacityRefusalRollsBackEveryMetadataEffectAndTheOriginalClaim()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1,
            configuration: Deployment(), maximumRetainedEnvelopeBytes: 8 * 1024);
        HttpClient setup = fixture.Client("operator-a");
        await Apply(setup, await Catalog(setup), "catalog");
        string application = await PreallocatedApplication(fixture);
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, AppGrants(application)));
        HttpClient actor = fixture.Client("reader", 1);
        for (int index = 0; index < 16; index++)
        {
            ManagementConfigurationCreationContext context = await Context(actor, application);
            string before = await CreationSnapshot(fixture);
            long originals = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
            long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
            string key = "audit-capacity-" + index;
            using HttpResponseMessage response = await Send(actor, Creation(application), key, context.ETag,
                new ManagementConfigurationCreationCommand(1, "audit.settings." + index, "createGeneric", "Prepare within the real owned Audit capacity"));
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.AreEqual(originals + 1, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
                Assert.AreEqual(audit + 1, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
                continue;
            }
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.AreEqual("management.configuration.audit_unavailable", (await Decode<ManagementConfigurationProblem>(response)).Code);
            Assert.AreEqual(before, await CreationSnapshot(fixture), "Definition/source/binding/map counts and catalog revision/used/quota must all roll back.");
            Assert.AreEqual(context, await Context(actor, application));
            Assert.AreEqual(originals, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
            Assert.AreEqual(audit, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
            using HttpResponseMessage missing = await actor.GetAsync("applications/" + application + "/configuration-creation-operations/" + key);
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.AreEqual("management.configuration.operation_not_found", (await Decode<ManagementConfigurationProblem>(missing)).Code);
            return;
        }
        Assert.Fail("The finite native fixture must exhaust its 8 KiB retained Audit budget within 16 real Configuration originals.");
    }

    [TestMethod]
    public async Task CompetingCreationPreconditionsCommitOneEffectAndPreserveEachOriginalAcrossReplay()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient setup = fixture.Client("operator-a");
        await Apply(setup, await Catalog(setup), "catalog");
        string application = await PreallocatedApplication(fixture);
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, AppGrants(application)));
        HttpClient actor = fixture.Client("reader", 1);
        string before = await CreationSnapshot(fixture);
        long definitions = await Count(fixture, fixture.Options.Persistence.Schema, "configuration_definition");
        long sources = await Count(fixture, fixture.Options.Persistence.Schema, "configuration_source");
        long bindings = await Count(fixture, fixture.Options.Persistence.Schema, "configuration_binding");
        long originals = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        ManagementConfigurationCreationContext context = await Context(actor, application);
        var first = new ManagementConfigurationCreationCommand(1, "first.race", "createGeneric", "First current creation context");
        var second = new ManagementConfigurationCreationCommand(1, "second.race", "createGeneric", "Competing same creation context");
        Task<HttpResponseMessage> one = Send(actor, Creation(application), "race-first", context.ETag, first);
        Task<HttpResponseMessage> two = Send(actor, Creation(application), "race-second", context.ETag, second);
        using HttpResponseMessage firstResponse = await one;
        using HttpResponseMessage secondResponse = await two;
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }, new[] { firstResponse.StatusCode, secondResponse.StatusCode });
        Assert.AreEqual(definitions + 1, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_definition"));
        Assert.AreEqual(sources + 1, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_source"));
        Assert.AreEqual(bindings + 1, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_binding"));
        Assert.AreEqual(originals + 2, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit + 2, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        Assert.AreNotEqual(context.ETag, (await Context(actor, application)).ETag);
        Assert.AreNotEqual(before, await CreationSnapshot(fixture));
        string after = await CreationSnapshot(fixture);
        foreach ((string key, ManagementConfigurationCreationCommand command, HttpResponseMessage response) in new[]
        {
            ("race-first", first, firstResponse), ("race-second", second, secondResponse)
        })
        {
            ManagementConfigurationOperationResult original = response.IsSuccessStatusCode
                ? await Decode<ManagementConfigurationOperationResult>(response) : (await Decode<ManagementConfigurationProblem>(response)).Result!;
            Assert.AreEqual("committed", original.OriginalCompletion);
            using HttpResponseMessage replay = await Send(actor, Creation(application), key, context.ETag, command);
            Assert.AreEqual(response.StatusCode, replay.StatusCode);
            ManagementConfigurationOperationResult recovered = replay.IsSuccessStatusCode
                ? await Decode<ManagementConfigurationOperationResult>(replay) : (await Decode<ManagementConfigurationProblem>(replay)).Result!;
            Assert.AreEqual("replayed", recovered.ResultSource);
            Assert.AreEqual(original.Result, recovered.Result);
            Assert.AreEqual(original.Rejection, recovered.Rejection);
        }
        using HttpResponseMessage stale = await Send(actor, Creation(application), "stale-third", context.ETag,
            new ManagementConfigurationCreationCommand(1, "stale.race", "createGeneric", "Never reuse a stale context"));
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.AreEqual(after, await CreationSnapshot(fixture));
        Assert.AreEqual(originals + 3, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit + 3, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    [TestMethod]
    public async Task ExhaustedNativeQuotaRecordsOneRejectedOriginalWithoutChangingAnyCreationMetadata()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient setup = fixture.Client("operator-a");
        await Apply(setup, await Catalog(setup), "catalog");
        string application = await PreallocatedApplication(fixture);
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, AppGrants(application)));
        HttpClient actor = fixture.Client("reader", 1);
        await using (var quota = fixture.Source.CreateCommand($"UPDATE \"{fixture.Options.Persistence.Schema}\".configuration_catalog SET quota_bytes=used_bytes WHERE singleton"))
        {
            Assert.AreEqual(1, await quota.ExecuteNonQueryAsync());
        }
        string before = await CreationSnapshot(fixture);
        long originals = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        ManagementConfigurationCreationContext context = await Context(actor, application);
        var command = new ManagementConfigurationCreationCommand(1, "quota.settings", "createGeneric", "Refuse outside the existing native quota");
        for (int index = 0; index < 2; index++)
        {
            using HttpResponseMessage response = await Send(actor, Creation(application), "quota-original", context.ETag, command);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
            ManagementConfigurationProblem problem = await Decode<ManagementConfigurationProblem>(response);
            Assert.AreEqual("management.configuration.capacity_exceeded", problem.Code);
            Assert.AreEqual("committed", problem.Result!.OriginalCompletion);
            Assert.AreEqual(index == 0 ? "prepared" : "replayed", problem.Result.ResultSource);
            Assert.AreEqual(before, await CreationSnapshot(fixture));
        }
        Assert.AreEqual(originals + 1, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit + 1, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    [TestMethod]
    public async Task OptionalPreparationReadsRejectUncertainAbsenceChangingCollectionsAndMissingOriginAuthorityWitnesses()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient setup = fixture.Client("operator-a");
        await Apply(setup, await Catalog(setup), "catalog");
        var scenario = new CreationReadScenario();
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, original.Grants.Concat(new[]
        {
            OperatorAppGrant(original.Profile, fixture.ApplicationA, ManagementConfigurationCapabilities.Create),
            OperatorAppGrant(original.Profile, fixture.ApplicationA, ManagementConfigurationCapabilities.OperationsRead)
        }).ToArray()), configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new PreparationReads((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>(), scenario)));
        HttpClient actor = fixture.Client("operator-a", 1);
        foreach (string fault in new[] { "null", "cleanup", "missing-completion", "uncertain-not-found", "has-more", "foreign" })
        {
            scenario.Fault = fault;
            using HttpResponseMessage response = await actor.GetAsync(Creation(fixture.ApplicationA) + "/context");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, fault);
            Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("hostApplicationName", StringComparison.Ordinal));
        }
        scenario.Fault = "clean-not-found";
        using (HttpResponseMessage response = await actor.GetAsync(Creation(fixture.ApplicationA) + "/context"))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
        foreach (string fault in new[] { "cleanup", "missing-completion", "foreign", "assigned-no-refs", "mixed-one-ref", "unassigned-with-ref", "growing-rows", "growing-refs", "duplicate",
            "date-no-source", "source-no-date", "date-invalid-source" })
        {
            scenario.Fault = fault;
            scenario.CountReads = 0;
            scenario.Yields = 0;
            using HttpResponseMessage response = await actor.GetAsync("applications/" + fixture.ApplicationA + "/configuration-summaries");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, fault);
            Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("\"items\"", StringComparison.Ordinal));
            if (fault.StartsWith("growing-", StringComparison.Ordinal))
            {
                Assert.AreEqual(1, scenario.CountReads, "Provider count must be captured once.");
                Assert.AreEqual(2, scenario.Yields, "Only the declared count plus one witness may be consumed.");
            }
        }
        scenario.Fault = "none";
        Assert.HasCount(1, (await Summaries(actor, fixture.ApplicationA)).Items);
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, original.Grants.Concat(new[]
        {
            OperatorAppGrant(original.Profile, fixture.ApplicationA, ManagementConfigurationCapabilities.Create),
            OperatorAppGrant(original.Profile, fixture.ApplicationA, ManagementConfigurationCapabilities.OperationsRead)
        }).ToArray()), configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new ManagementConfigurationCohortLegacyBackend((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>())));
        foreach (string path in new[] { Creation(fixture.ApplicationA) + "/context", "applications/" + fixture.ApplicationA + "/configuration-summaries" })
        {
            using HttpResponseMessage response = await fixture.Client("operator-a", 2).GetAsync(path);
            Assert.AreEqual(HttpStatusCode.NotImplemented, response.StatusCode, "An original backend remains valid and declares unsupported optional preparation reads explicitly.");
        }
    }

    [TestMethod]
    public async Task SummaryCursorBindsTheActualActorApplicationAndRequestedSizeWithoutExposingHiddenRows()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient setup = fixture.Client("operator-a");
        await Apply(setup, await Catalog(setup), "catalog");
        await fixture.StartReplicaAsync(selectSecurity: original => Grants(original, AppGrants(fixture.ApplicationA)));
        HttpClient actor = fixture.Client("reader", 1);
        foreach (string name in new[] { "first.cursor", "second.cursor" })
        {
            await Create(actor, fixture.ApplicationA, name, (await Context(actor, fixture.ApplicationA)).ETag,
                new(1, name, "createGeneric", "Prepare one authorized cursor row"));
        }
        string path = "applications/" + fixture.ApplicationA + "/configuration-summaries?pageSize=1";
        using HttpResponseMessage firstResponse = await actor.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        ManagementPage<ManagementConfigurationSummary> first = await Decode<ManagementPage<ManagementConfigurationSummary>>(firstResponse);
        Assert.HasCount(1, first.Items);
        Assert.IsNotNull(first.NextCursor);
        using HttpResponseMessage nextResponse = await actor.GetAsync(path + "&cursor=" + Uri.EscapeDataString(first.NextCursor));
        ManagementPage<ManagementConfigurationSummary> next = await Decode<ManagementPage<ManagementConfigurationSummary>>(nextResponse);
        Assert.HasCount(1, next.Items);
        Assert.AreNotEqual(first.Items.Single().SourceId, next.Items.Single().SourceId);
        Assert.IsNull(next.NextCursor);
        foreach ((HttpClient client, string invalid) in new[]
        {
            (actor, path.Replace("pageSize=1", "pageSize=2", StringComparison.Ordinal)),
            (setup, path),
            (actor, path.Replace(fixture.ApplicationA, fixture.ApplicationB, StringComparison.Ordinal))
        })
        {
            using HttpResponseMessage response = await client.GetAsync(invalid + "&cursor=" + Uri.EscapeDataString(first.NextCursor));
            Assert.IsTrue(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
            Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("\"items\"", StringComparison.Ordinal));
        }
    }

    private static async Task<string> CreationSnapshot(ManagementHostFixture fixture)
    {
        string schema = "\"" + fixture.Options.Persistence.Schema + "\".";
        await using var command = fixture.Source.CreateCommand($"""
            SELECT jsonb_build_array(c.revision,c.used_bytes,c.quota_bytes,
                (SELECT count(*) FROM {schema}configuration_definition),(SELECT count(*) FROM {schema}configuration_source),
                (SELECT count(*) FROM {schema}configuration_binding),(SELECT count(*) FROM {schema}configuration_assignment))::text
            FROM {schema}configuration_catalog c WHERE singleton
            """);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static CapabilityGrant OperatorAppGrant(string profile, string application, string capability)
        => new(profile, capability, EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
            subject: new(ManagementHostFixture.Issuer, "operator-a"), resourceSelection: CapabilityResourceSelection.ExactResource,
            resourceType: "management.application", resourceIdentifier: application);

    private sealed class CreationReadScenario
    {
        internal string Fault { get; set; } = "none";
        internal int CountReads
        {
            get; set;
        }
        internal int Yields
        {
            get; set;
        }
    }

    private sealed class GrowingPreparationList<T>(CreationReadScenario scenario, Func<T> next) : IReadOnlyList<T>
    {
        public int Count => ++scenario.CountReads;
        public T this[int index] => throw new NotSupportedException();
        public IEnumerator<T> GetEnumerator()
        {
            while (true)
            {
                scenario.Yields++;
                yield return next();
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // A finite named compatibility companion: only the catalog and the two actual read routes are used by this test.
    private sealed class PreparationReads(IManagementConfigurationBackend backend, CreationReadScenario scenario)
        : ManagementConfigurationCohortLegacyBackend(backend), IManagementConfigurationCreationBackend
    {
        private readonly IManagementConfigurationCreationBackend creation = (IManagementConfigurationCreationBackend)backend;

        public async Task<ManagementStoreResult<ManagementConfigurationCreationContext>> ReadCreationContextAsync(string applicationId, CancellationToken token = default)
        {
            ManagementStoreResult<ManagementConfigurationCreationContext> result = await creation.ReadCreationContextAsync(applicationId, token);
            return scenario.Fault switch
            {
                "null" => null!,
                "cleanup" => result with { AttemptCompletion = DirtyCompletion() },
                "missing-completion" => result with { AttemptCompletion = null },
                "uncertain-not-found" => result with { Value = null, Code = "management.configuration.not_found", PreviouslyCommitted = false, AttemptCompletion = DirtyCompletion() },
                "clean-not-found" => result with { Value = null, Code = "management.configuration.not_found", PreviouslyCommitted = false },
                "has-more" => result with { HasMore = true },
                "foreign" => result with { Value = result.Value! with { ApplicationId = Guid.NewGuid().ToString("D") } },
                _ => result
            };
        }

        public async Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationSummaryCandidate>>> ReadSummaryCandidatesAsync(string applicationId, string? after, int take, CancellationToken token = default)
        {
            ManagementStoreResult<IReadOnlyList<ManagementConfigurationSummaryCandidate>> result = await creation.ReadSummaryCandidatesAsync(applicationId, after, take, token);
            ManagementConfigurationSummaryCandidate row = result.Value!.Single();
            return scenario.Fault switch
            {
                "cleanup" => result with { AttemptCompletion = DirtyCompletion() },
                "missing-completion" => result with { AttemptCompletion = null },
                "foreign" => result with { Value = [row with { Summary = row.Summary with { ApplicationId = Guid.NewGuid().ToString("D") } }] },
                "assigned-no-refs" => result with { Value = [row with { Summary = row.Summary with { Origin = "shared" }, ReleaseIds = [] }] },
                "mixed-one-ref" => result with { Value = [row with { Summary = row.Summary with { Origin = "mixed", HasCohortOverrides = true }, ReleaseIds = [Guid.NewGuid().ToString("D")] }] },
                "unassigned-with-ref" => result with { Value = [row with { ReleaseIds = [Guid.NewGuid().ToString("D")] }] },
                "growing-rows" => result with { Value = new GrowingPreparationList<ManagementConfigurationSummaryCandidate>(scenario, () => row) },
                "growing-refs" => result with { Value = [row with { Summary = row.Summary with { Origin = "shared" }, ReleaseIds = new GrowingPreparationList<string>(scenario, () => Guid.NewGuid().ToString("D")) }] },
                "duplicate" => result with { Value = [row, row] },
                "date-no-source" => result with { Value = [row with { Summary = row.Summary with { ModifiedAtUtc = "2026-11-26T12:00:00.1234567Z" } }] },
                "source-no-date" => result with { Value = [row with { ModifiedSourceId = Guid.NewGuid().ToString("D") }] },
                "date-invalid-source" => result with { Value = [row with { Summary = row.Summary with { ModifiedAtUtc = "2026-11-26T12:00:00.1234567Z" }, ModifiedSourceId = "invalid" }] },
                _ => result
            };
        }

        private static TransactionCompletion DirtyCompletion()
            => new(TransactionOutcome.NotCommitted, TransactionEndReason.RollbackRequested, commitAttempted: false, rollbackAttempted: true,
                rollbackConfirmed: true, cleanupErrors: [new(TransactionErrorPhase.ConnectionDispose, "fixture.cleanup", "The owned read did not cleanly settle.")]);

        public Task<ManagementConfigurationResponse> CreateConfigurationAsync(ManagementApplication application, string key, string ifMatch, ManagementConfigurationCreationCommand command, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationResponse> LookupCreationAsync(ManagementApplication application, string key, CancellationToken token = default) => throw new NotSupportedException();
    }
}
