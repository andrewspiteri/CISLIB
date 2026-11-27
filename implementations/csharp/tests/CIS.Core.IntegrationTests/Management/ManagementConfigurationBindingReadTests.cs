using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CIS.Hosting;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Service.Security;
using CIS.Management.Wire;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationBindingReadTests
{
    [TestMethod]
    public async Task RetainedReleasePagesOrderTimestampTiesAndAuthorizeEveryActualReleaseBeforeFillingThePage()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "release-list-catalog");
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        var expected = new List<string>();
        for (int index = 0; index < 3; index++)
        {
            expected.Add((await CreateRelease(actor, fixture.ApplicationA, map.ETag, "retained-release-" + index,
                new(1, new("absent"), new("absent"), "Reviewed defaults " + index))).Result!.ReleaseId!);
        }
        await CreateRelease(actor, fixture.ApplicationB, (await Map(actor, fixture.ApplicationB)).ETag, "foreign-retained-release",
            new(1, new("absent"), new("absent"), "A different application binding"));
        await using (var equalTimes = fixture.Source.CreateCommand($"UPDATE \"{fixture.Options.Persistence.Schema}\".configuration_release SET created_at=@time WHERE binding_id=@binding"))
        {
            equalTimes.Parameters.AddWithValue("time", new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc));
            equalTimes.Parameters.AddWithValue("binding", Guid.Parse(map.BindingId));
            Assert.AreEqual(3, await equalTimes.ExecuteNonQueryAsync());
        }
        expected.Sort((left, right) => StringComparer.Ordinal.Compare(right, left));
        ManagementConfigurationCatalog before = await Catalog(actor);
        long operations = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long audit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        var actual = new List<string>();
        string? cursor = null;
        string? firstCursor = null;
        for (int index = 0; index < expected.Count; index++)
        {
            ManagementPage<ManagementConfigurationRelease> page = await Page<ManagementConfigurationRelease>(actor,
                Path(fixture.ApplicationA, "releases", size: 1, cursor: cursor));
            Assert.HasCount(1, page.Items);
            actual.Add(page.Items[0].ReleaseId);
            cursor = page.NextCursor;
            firstCursor ??= cursor;
            Assert.AreEqual(index + 1 < expected.Count, cursor is not null);
        }
        CollectionAssert.AreEqual(expected, actual);
        Assert.IsNotNull(firstCursor);
        foreach ((HttpClient client, string path) in new[]
        {
            (actor, Path(fixture.ApplicationB, "releases", size: 1, cursor: firstCursor)),
            (actor, Path(fixture.ApplicationA, "releases", size: 2, cursor: firstCursor)),
            (fixture.Client("operator-b"), Path(fixture.ApplicationA, "releases", size: 1, cursor: firstCursor)),
            (fixture.Client("operator-a", explicitToken: fixture.Token("operator-a", expiration: DateTimeOffset.UtcNow.AddMinutes(20))),
                Path(fixture.ApplicationA, "releases", size: 1, cursor: firstCursor))
        })
        {
            using HttpResponseMessage refused = await client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        Assert.IsEmpty((await Page<ManagementConfigurationRelease>(fixture.Client("operator-b"), Path(fixture.ApplicationA, "releases"))).Items);
        await fixture.StartReplicaAsync(selectSecurity: options => Grants(options,
            options.Grants.Where(grant => !(grant.Subject?.Identifier == "operator-a"
                && (grant.Capability == "management.applications.read"
                    || grant.Capability == ManagementConfigurationCapabilities.DefinitionsRead
                        && grant.ResourceType == ManagementConfigurationCapabilities.ReleaseResource)))
                .Append(new(options.Profile, ManagementConfigurationCapabilities.DefinitionsRead,
                    EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                    subject: new(ManagementHostFixture.Issuer, "operator-a"), resourceSelection: CapabilityResourceSelection.ExactResource,
                    resourceType: ManagementConfigurationCapabilities.ReleaseResource, resourceIdentifier: expected[^1])).ToArray(),
            policyVersion: "release-read-v2"));
        using (HttpResponseMessage changedPolicy = await fixture.Client("operator-a", 1).GetAsync(
            Path(fixture.ApplicationA, "releases", size: 1, cursor: firstCursor)))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, changedPolicy.StatusCode);
        }
        ManagementPage<ManagementConfigurationRelease> selected = await Page<ManagementConfigurationRelease>(fixture.Client("operator-a", 1),
            Path(fixture.ApplicationA, "releases", size: 1));
        Assert.AreEqual(expected[^1], selected.Items.Single().ReleaseId, "Hidden newer rows cannot consume the authorized page.");
        Assert.IsNull(selected.NextCursor);
        Assert.AreEqual(before, await Catalog(actor));
        Assert.AreEqual(map.ETag, (await Map(actor, fixture.ApplicationA)).ETag);
        Assert.AreEqual(operations, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audit, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    [TestMethod]
    public async Task BindingStatusFiltersDefinitionAndVersionBeforeRecipientPagingAndPreservesIndependentReportedVersions()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: MultipleDefinitions());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "scoped-status-catalog");
        ManagementConfigurationAssignmentMap initial = await Map(actor, fixture.ApplicationA);
        string release = (await CreateRelease(actor, fixture.ApplicationA, initial.ETag, "scoped-status-release",
            new(1, new("absent"), new("absent"), "Defaults"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, initial.ETag, "scoped-status-first", new(1, "replace", release, [], "Initial generation"));
        await Assign(actor, fixture.ApplicationA, (await Map(actor, fixture.ApplicationA)).ETag, "scoped-status-second",
            new(1, "replace", release, [], "New generation of the same immutable release"));
        string accepted = await Register(fixture.Client("workload-a"));
        string pending = await Register(fixture.Client("workload-a"));
        var current = new ManagementConfigurationAppliedVersion(release, "2", "7");
        var previous = new ManagementConfigurationAppliedVersion(release, "1", "6");
        await Report(fixture.Client("workload-a"), accepted, new("1", "2026-11-26T00:00:00.0000000Z",
            [new("shared.settings", "1", "HotReload", "Applied", true, new(release, "2"), current, current)]));
        await Report(fixture.Client("workload-a"), pending, new("1", "2026-11-26T00:00:00.0000000Z",
            [new("shared.settings", "1", "RestartRequired", "PendingRestart", true, new(release, "2"), previous, previous)]));
        ManagementPage<ManagementConfigurationAdoptionEntry> unfiltered = await Page<ManagementConfigurationAdoptionEntry>(actor,
            "applications/" + fixture.ApplicationA + "/configuration-status?pageSize=500");
        Assert.IsTrue(unfiltered.Items.Any(item => item.ConfigurationId == "aaa.settings"));
        Assert.IsTrue(unfiltered.Items.Any(item => item.ConfigurationId == "shared.settings" && item.SchemaVersion == "2"));
        var scoped = new List<ManagementConfigurationAdoptionEntry>();
        string? cursor = null;
        string? firstCursor = null;
        do
        {
            ManagementPage<ManagementConfigurationAdoptionEntry> page = await Page<ManagementConfigurationAdoptionEntry>(actor,
                Path(fixture.ApplicationA, "status", size: 1, cursor: cursor));
            Assert.HasCount(1, page.Items);
            scoped.Add(page.Items.Single());
            cursor = page.NextCursor;
            firstCursor ??= cursor;
            Assert.IsTrue(scoped.Count <= 4, "The exact finite binding does not walk unrelated definitions.");
        } while (cursor is not null);
        Assert.HasCount(3, scoped);
        Assert.IsTrue(scoped.All(item => item.ApplicationId == fixture.ApplicationA && item.ConfigurationId == "shared.settings" && item.SchemaVersion == "1"));
        Assert.AreEqual("applied", scoped.Single(item => item.RegistrationId == accepted).Convergence);
        ManagementConfigurationAdoptionEntry restarting = scoped.Single(item => item.RegistrationId == pending);
        Assert.AreEqual("pendingRestart", restarting.Convergence);
        Assert.AreEqual("2", restarting.Desired!.Generation);
        Assert.AreEqual("1", restarting.Reported!.Applied!.Generation);
        Assert.IsTrue(scoped.Any(item => item.Convergence == "noInstances"));
        Assert.IsNotNull(firstCursor);
        foreach (string path in new[] { Path(fixture.ApplicationA, "status", schema: "2", size: 1, cursor: firstCursor),
            Path(fixture.ApplicationA, "status", size: 2, cursor: firstCursor), Path(fixture.ApplicationB, "status", size: 1, cursor: firstCursor) })
        {
            using HttpResponseMessage refused = await actor.GetAsync(path);
            Assert.AreEqual(path.Contains(fixture.ApplicationB, StringComparison.Ordinal) ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest, refused.StatusCode);
        }
        await fixture.StartReplicaAsync(selectSecurity: options => Grants(options, options.Grants.Where(grant =>
            !(grant.Subject?.Identifier == "operator-a" && (grant.Capability == "management.applications.read"
                || grant.Capability == ManagementConfigurationCapabilities.DefinitionsRead))).ToArray()));
        Assert.HasCount(3, (await Page<ManagementConfigurationAdoptionEntry>(fixture.Client("operator-a", 1),
            Path(fixture.ApplicationA, "status"))).Items, "AdoptionRead does not silently acquire AppRead or DefinitionsRead prerequisites.");
        using HttpResponseMessage hidden = await fixture.Client("operator-b").GetAsync(Path(fixture.ApplicationA, "status"));
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [TestMethod]
    public async Task LegacyOptionalBackendsAndStrictQueriesPreserveHiddenResourcesAndExplicitUnsupportedResults()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "legacy-binding-read-catalog");
        await fixture.StartReplicaAsync(configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new ManagementConfigurationCohortLegacyBackend((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>())));
        using (HttpResponseMessage info = await fixture.Client("operator-a", 1).GetAsync("service-info"))
        {
            ManagementServiceInfo service = ManagementWire.DecodeResponse<ManagementServiceInfo>(await info.Content.ReadAsByteArrayAsync())!;
            Assert.IsFalse(service.Features.Any(feature => feature.OperationId is "listConfigurationReleases" or "getConfigurationBindingStatus"));
        }
        foreach (string kind in new[] { "releases", "status" })
        {
            using (HttpResponseMessage unsupported = await fixture.Client("operator-a", 1).GetAsync(Path(fixture.ApplicationA, kind)))
            {
                Assert.AreEqual(HttpStatusCode.NotImplemented, unsupported.StatusCode);
            }
            using (HttpResponseMessage hidden = await fixture.Client("reader", 1).GetAsync(Path(fixture.ApplicationA, kind)))
            {
                Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
            }
            foreach (string query in new[] { "schemaVersion=1&schemaVersion=1", "schemaVersion=1&%73chemaVersion=1",
                "SchemaVersion=1", "schemaVersion=1&unknown=x", "schemaVersion=1&pageSize=501", "schemaVersion=%C0%AF",
                "schemaVersion=1&cursor=%ED%A0%80", "schemaVersion=1&cursor=forged" })
            {
                using HttpResponseMessage invalid = await actor.GetAsync("applications/" + fixture.ApplicationA
                    + "/configuration/shared.settings/" + kind + "?" + query);
                Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode, kind + ": " + query);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, Path(fixture.ApplicationA, kind)) { Content = new StringContent("{}") };
            using HttpResponseMessage body = await actor.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.BadRequest, body.StatusCode);
        }
    }

    [TestMethod]
    public async Task MissingSchemaAndDisabledBindingsRefuseWithoutFallingBackToAnotherBinding()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "disabled-binding-read-catalog");
        foreach (string kind in new[] { "releases", "status" })
        {
            using HttpResponseMessage missing = await actor.GetAsync(Path(fixture.ApplicationA, kind, schema: "2"));
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        }
        string binding = (await Map(actor, fixture.ApplicationA)).BindingId;
        await using (var disable = fixture.Source.CreateCommand($"UPDATE \"{fixture.Options.Persistence.Schema}\".configuration_binding SET enabled=false WHERE binding_id=@binding"))
        {
            disable.Parameters.AddWithValue("binding", Guid.Parse(binding));
            Assert.AreEqual(1, await disable.ExecuteNonQueryAsync());
        }
        foreach (string kind in new[] { "releases", "status" })
        {
            using HttpResponseMessage disabled = await actor.GetAsync(Path(fixture.ApplicationA, kind));
            Assert.AreEqual(HttpStatusCode.NotFound, disabled.StatusCode);
        }
    }

    [TestMethod]
    public async Task MalformedOptionalPagesNeverExposePartialRowsOrConvertUncertainCleanupIntoAbsence()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "malformed-binding-read-catalog");
        await CreateRelease(actor, fixture.ApplicationA, (await Map(actor, fixture.ApplicationA)).ETag, "malformed-release",
            new(1, new("absent"), new("absent"), "Defaults"));
        var scenario = new ReadScenario();
        await fixture.StartReplicaAsync(configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new OptionalReads((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>(), scenario)));
        HttpClient selected = fixture.Client("operator-a", 1);
        foreach (string kind in new[] { "releases", "status" })
        {
            foreach (string fault in new[] { "foreign", "duplicate", "cleanup", "missing-completion", "not-found-cleanup", "more-empty" })
            {
                scenario.Fault = fault;
                using HttpResponseMessage refused = await selected.GetAsync(Path(fixture.ApplicationA, kind));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode, kind + ": " + fault);
                string text = await refused.Content.ReadAsStringAsync();
                Assert.IsFalse(text.Contains("\"items\"", StringComparison.Ordinal));
                Assert.IsFalse(text.Contains("nextCursor", StringComparison.Ordinal));
            }
            scenario.Fault = "not-found-clean";
            using HttpResponseMessage missing = await selected.GetAsync(Path(fixture.ApplicationA, kind));
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [TestMethod]
    public async Task ExhaustingTheFiniteAuthorizedReleaseScanReturnsNoPartialPrefixOrCursor()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        await Apply(fixture.Client("operator-a"), await Catalog(fixture.Client("operator-a")), "scan-release-catalog");
        const string visibleRelease = "00000001-0000-0000-0000-000000000000";
        var scenario = new ReadScenario { Fault = "prefix" };
        await fixture.StartReplicaAsync(selectSecurity: options => Grants(options,
            options.Grants.Where(grant => !(grant.Subject?.Identifier == "operator-a"
                && grant.Capability == ManagementConfigurationCapabilities.DefinitionsRead
                && grant.ResourceType == ManagementConfigurationCapabilities.ReleaseResource))
                .Append(new(options.Profile, ManagementConfigurationCapabilities.DefinitionsRead,
                    EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                    subject: new(ManagementHostFixture.Issuer, "operator-a"), resourceSelection: CapabilityResourceSelection.ExactResource,
                    resourceType: ManagementConfigurationCapabilities.ReleaseResource, resourceIdentifier: visibleRelease)).ToArray()),
            configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
                new OptionalReads((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>(), scenario)));
        HttpClient actor = fixture.Client("operator-a", 1);
        ManagementPage<ManagementConfigurationRelease> prefix = await Page<ManagementConfigurationRelease>(actor,
            Path(fixture.ApplicationA, "releases", size: 2));
        Assert.AreEqual(visibleRelease, prefix.Items.Single().ReleaseId, "The first fixture row is actually authorized and visible.");
        Assert.IsNull(prefix.NextCursor);
        scenario.Fault = "scan";
        scenario.Calls = 0;
        scenario.Generated = 0;
        using HttpResponseMessage refused = await actor.GetAsync(Path(fixture.ApplicationA, "releases", size: 2));
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.AreEqual("management.discovery.scan_limit", (await Decode<ManagementConfigurationProblem>(refused)).Code);
        Assert.AreEqual(256, scenario.Calls);
        Assert.AreEqual(4096, scenario.Generated);
        Assert.IsFalse((await refused.Content.ReadAsStringAsync()).Contains("\"items\"", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("releases", "configuration_release")]
    [DataRow("status", "configuration_adoption")]
    public async Task CancellationDrainsTheActualOptionalReadBeforeReleasingItsOwnedNativeLock(string kind, string table)
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "cancel-binding-read-catalog");
        await using var connection = await fixture.Source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var hold = connection.CreateCommand())
        {
            hold.Transaction = transaction;
            hold.CommandText = $"LOCK TABLE \"{fixture.Options.Persistence.Schema}\".\"{table}\" IN ACCESS EXCLUSIVE MODE";
            await hold.ExecuteNonQueryAsync();
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<HttpResponseMessage> pending = actor.GetAsync(Path(fixture.ApplicationA, kind), cancellation.Token);
        try
        {
            Assert.IsTrue(await Blocked(fixture, table, true));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                using HttpResponseMessage response = await pending;
            });
            Assert.IsTrue(await Blocked(fixture, table, false), "Native SQL must drain with the owned lock still held.");
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                using HttpResponseMessage response = await pending;
            }
            catch (OperationCanceledException)
            {
            }
            await transaction.RollbackAsync();
        }
    }

    private static string Path(string application, string kind, string schema = "1", int size = 100, string? cursor = null)
        => "applications/" + application + "/configuration/shared.settings/" + kind + "?schemaVersion=" + schema + "&pageSize=" + size
            + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));

    private static async Task<ManagementPage<T>> Page<T>(HttpClient actor, string path) where T : class
    {
        using HttpResponseMessage response = await actor.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        return await Decode<ManagementPage<T>>(response);
    }

    private static ManagementConfigurationOptions MultipleDefinitions()
    {
        var shape = new ManagementConfigurationShape("object", false, [new("Enabled", true, new("boolean", false))]);
        var manifest = new ManagementConfigurationManifest(1,
            [new("aaa.settings", "1", shape, "{\"Enabled\":true}"), new("shared.settings", "1", shape, "{\"Enabled\":true}"),
                new("shared.settings", "2", shape, "{\"Enabled\":true}")],
            [new("managed-a", "Development", "FirstHost", "aaa.settings", "1"), new("managed-a", "Development", "FirstHost", "shared.settings", "1"),
                new("managed-a", "Development", "FirstHost", "shared.settings", "2")],
            [new("aaa.settings", "1", "Development"), new("shared.settings", "1", "Development"), new("shared.settings", "2", "Development")],
            [new(ManagementHostFixture.Issuer, "workload-a", "managed-a", "Development", "canary")]);
        return new(JsonSerializer.Serialize(manifest, ManagementConfigurationOptions.Json), 32L * 1024 * 1024);
    }

    private static async Task<string> Register(HttpClient actor)
    {
        string id = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + id)
        {
            Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request("same-reported-instance")))
        };
        request.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage response = await actor.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return id;
    }

    private static async Task Report(HttpClient actor, string registration, ManagementConfigurationStatusReport report)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + registration + "/configuration-status")
        {
            Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(report))
        };
        request.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage response = await actor.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<bool> Blocked(ManagementHostFixture fixture, string table, bool expected)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await using var query = fixture.Source.CreateCommand("""
                SELECT EXISTS(SELECT 1 FROM pg_locks waiting
                    JOIN pg_stat_activity activity ON activity.pid=waiting.pid
                    WHERE activity.pid<>pg_backend_pid() AND activity.state='active'
                        AND activity.wait_event_type='Lock' AND waiting.locktype='relation'
                        AND NOT waiting.granted AND waiting.relation=to_regclass(@table))
                """);
            query.Parameters.AddWithValue("table", "\"" + fixture.Options.Persistence.Schema + "\".\"" + table + "\"");
            if ((bool)(await query.ExecuteScalarAsync())! == expected)
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
        return false;
    }

    private static ManagementSecurityOptions Grants(ManagementSecurityOptions original, IReadOnlyList<CapabilityGrant> grants,
        string? policyVersion = null)
        => new()
        {
            Mode = original.Mode,
            Profile = original.Profile,
            AuthenticationScheme = original.AuthenticationScheme,
            BusinessAddress = original.BusinessAddress,
            BusinessPort = original.BusinessPort,
            OperationalPort = original.OperationalPort,
            ServerCertificateFile = original.ServerCertificateFile,
            ServerPrivateKeyFile = original.ServerPrivateKeyFile,
            ServerTrustBundleFile = original.ServerTrustBundleFile,
            CertificateRevocation = original.CertificateRevocation,
            BorrowedNativeKeyScope = original.BorrowedNativeKeyScope,
            PolicyVersion = policyVersion ?? original.PolicyVersion,
            IdentityMappings = original.IdentityMappings,
            NativeProvider = original.NativeProvider,
            Grants = grants
        };

    private sealed class ReadScenario
    {
        internal string Fault { get; set; } = "";
        internal int Calls
        {
            get; set;
        }
        internal int Generated
        {
            get; set;
        }
    }

    private sealed class OptionalReads : ManagementConfigurationCohortLegacyBackend,
        IManagementConfigurationReleaseHistoryBackend, IManagementConfigurationBindingAdoptionBackend
    {
        private readonly IManagementConfigurationReleaseHistoryBackend releases;
        private readonly IManagementConfigurationBindingAdoptionBackend adoption;
        private readonly ReadScenario scenario;

        internal OptionalReads(IManagementConfigurationBackend native, ReadScenario scenario) : base(native)
        {
            releases = (IManagementConfigurationReleaseHistoryBackend)native;
            adoption = (IManagementConfigurationBindingAdoptionBackend)native;
            this.scenario = scenario;
        }

        public async Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationRelease>>> ReadReleasesAsync(
            ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken token = default)
        {
            scenario.Calls++;
            ManagementStoreResult<IReadOnlyList<ManagementConfigurationRelease>> result = await releases.ReadReleasesAsync(binding, after, take, token);
            if (scenario.Fault is "scan" or "prefix")
            {
                var rows = new List<ManagementConfigurationRelease>();
                int count = scenario.Fault == "prefix" ? 2 : take;
                for (int index = 0; index < count; index++)
                {
                    int sequence = ++scenario.Generated;
                    rows.Add(new(sequence.ToString("x8") + "-0000-0000-0000-000000000000", binding.ApplicationId, binding.ConfigurationId,
                        binding.SchemaVersion, binding.Environment, new("absent"), new("absent"), "native:operator-a",
                        new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc).AddSeconds(-sequence).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                        "Owned finite scan fixture", "\"retained\""));
                }
                return result with
                {
                    Value = rows.AsReadOnly()
                };
            }
            if (scenario.Fault == "foreign")
            {
                result = result with
                {
                    Value = [result.Value!.Single() with { SchemaVersion = "2" }]
                };
            }
            if (scenario.Fault == "duplicate")
            {
                ManagementConfigurationRelease retained = result.Value?.Single()
                    ?? throw new AssertFailedException("The native fixture must provide one retained release before duplicating its row.");
                result = result with
                {
                    Value = [retained, retained]
                };
            }
            return Fault(result);
        }

        public async Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>>> ReadBindingAdoptionAsync(
            ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken token = default)
        {
            ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>> result = await adoption.ReadBindingAdoptionAsync(binding, after, take, token);
            if (scenario.Fault is "foreign" or "duplicate")
            {
                ManagementConfigurationAdoptionRow row = result.Value!.First();
                result = result with
                {
                    Value = scenario.Fault == "foreign"
                    ? [row with { Entry = row.Entry with { SchemaVersion = "2" } }] : [row, row]
                };
            }
            return Fault(result);
        }

        private ManagementStoreResult<IReadOnlyList<T>> Fault<T>(ManagementStoreResult<IReadOnlyList<T>> result)
            => scenario.Fault switch
            {
                "cleanup" => result with { AttemptCompletion = DirtyCompletion() },
                "missing-completion" => result with { AttemptCompletion = null },
                "not-found-cleanup" => result with
                {
                    Value = null,
                    Code = "management.configuration.not_found",
                    PreviouslyCommitted = false,
                    AttemptCompletion = DirtyCompletion()
                },
                "not-found-clean" => result with { Value = null, Code = "management.configuration.not_found", PreviouslyCommitted = false },
                "more-empty" => result with { Value = [], HasMore = true },
                _ => result
            };

        private static TransactionCompletion DirtyCompletion()
            => new(TransactionOutcome.NotCommitted, TransactionEndReason.RollbackRequested, commitAttempted: false,
                rollbackAttempted: true, rollbackConfirmed: true,
                cleanupErrors: [new(TransactionErrorPhase.ConnectionDispose, "test.cleanup", "Owned cleanup failed.")]);
    }
}
