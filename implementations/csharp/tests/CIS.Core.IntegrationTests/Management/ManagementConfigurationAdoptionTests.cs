using System.Net;
using System.Net.Http.Headers;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Tests.IntegrationTests.Operations;
using CIS.Transactions;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationAdoptionTests
{
    private const string Time = "2026-11-24T00:00:00.0000000Z";

    [TestMethod]
    public async Task NativeOwnerIndependentSequenceAndExactReplayPreserveReceiptPresenceAndOperatorPreconditions()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        Setup state = await Prepare(fixture);
        HttpClient workload = fixture.Client("workload-a");
        ManagementConfigurationStatusReport first = Applied(state.Release);
        string beforePresence = await Presence(fixture, state.Registration);
        ManagementConfigurationCatalog before = await Catalog(state.Actor);
        ManagementConfigurationAssignmentMap beforeMap = await Map(state.Actor, fixture.ApplicationA);
        ManagementReportReceipt receipt = await Accepted(workload, state.Registration, first);
        ManagementConfigurationCatalog charged = await Catalog(state.Actor);
        long expected = 128 + ManagementConfigurationWire.EncodeRequest(first).LongLength;
        Assert.AreEqual(expected, charged.UsedBytes - before.UsedBytes);
        Assert.AreEqual(before.ETag, charged.ETag);
        Assert.AreEqual(beforeMap.ETag, (await Map(state.Actor, fixture.ApplicationA)).ETag);
        Assert.AreEqual(beforePresence, await Presence(fixture, state.Registration), "Adoption does not renew presence.");
        ManagementReportReceipt replay = await Accepted(workload, state.Registration, first);
        Assert.AreEqual("replayed", replay.Disposition);
        Assert.AreEqual(receipt.ReceivedAtUtc, replay.ReceivedAtUtc);
        Assert.AreEqual(charged.UsedBytes, (await Catalog(state.Actor)).UsedBytes);
        using (HttpResponseMessage conflict = await Report(workload, state.Registration, first with
        {
            ObservedAtUtc = "2026-11-24T00:00:01.0000000Z"
        }))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.AreEqual("management.sequence.conflict", (await Decode<ManagementConfigurationProblem>(conflict)).Code);
        }
        await Accepted(workload, state.Registration, first with
        {
            Sequence = "2"
        });
        using HttpResponseMessage obsolete = await Report(workload, state.Registration, first);
        Assert.AreEqual(HttpStatusCode.Conflict, obsolete.StatusCode);
        Assert.AreEqual("management.sequence.obsolete", (await Decode<ManagementConfigurationProblem>(obsolete)).Code);
        using HttpResponseMessage ownership = await Report(fixture.Client("workload-b"), state.Registration, first with
        {
            Sequence = "3"
        });
        Assert.AreEqual(HttpStatusCode.NotFound, ownership.StatusCode);
        using HttpResponseMessage actor = await Report(state.Actor, state.Registration, first with
        {
            Sequence = "3"
        });
        Assert.AreEqual(HttpStatusCode.Forbidden, actor.StatusCode);
        using HttpResponseMessage unsupported = await Report(workload, state.Registration, first with
        {
            Sequence = "3",
            Entries = [first.Entries[0] with { ConfigurationId = "foreign.settings" }]
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, unsupported.StatusCode);
        using HttpResponseMessage missing = await Report(workload, Guid.NewGuid().ToString("D"), first);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        using HttpResponseMessage preCatalogSession = await Report(workload, fixture.RegistrationA, first);
        Assert.AreEqual(HttpStatusCode.NotFound, preCatalogSession.StatusCode, "A report cannot create a configuration session.");
        using HttpResponseMessage close = await workload.DeleteAsync("registrations/" + state.Registration);
        Assert.AreEqual(HttpStatusCode.OK, close.StatusCode);
        using HttpResponseMessage closed = await Report(workload, state.Registration, first with
        {
            Sequence = "3"
        });
        Assert.AreEqual(HttpStatusCode.Conflict, closed.StatusCode);
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("cleanup")]
    [DataRow("replay-cleanup")]
    public async Task NativeStatusCompletionUncertaintyPreservesActualCommitAndFreshReplayWithoutDoubleCharging(string fault)
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        Setup setup = await Prepare(fixture);
        ManagementConfigurationStatusReport report = Applied(setup.Release);
        HttpClient workload = fixture.Client("workload-a");
        bool replay = fault == "replay-cleanup";
        long before = (await Catalog(setup.Actor)).UsedBytes;
        if (replay)
        {
            await Accepted(workload, setup.Registration, report);
        }
        using var boundary = new NativeCommitBoundarySource(fixture.Source);
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("management", _ => boundary, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        var authority = new PostgreSqlManagementAuthority(factory, fixture.Options);
        var store = new PostgreSqlManagementStore(factory, fixture.Options.Persistence, authority);
        if (fault == "unknown")
        {
            boundary.FailAfterNextCommit();
        }
        else
        {
            boundary.FailAfterNextTransactionDisposal();
        }
        var owner = new ManagementRegistrationOwner("managed-a", "Development", ManagementHostFixture.Issuer, "workload-a", new("notProvided"));
        ManagementStoreResult<ManagementReportReceipt> uncertain = await store.ReportConfigurationStatusAsync(owner, setup.Registration, report);
        Assert.IsFalse(uncertain.IsKnownCommitted);
        Assert.IsNull(uncertain.Value, "The actual cleanup/commit uncertainty cannot be represented as a clean receipt.");
        Assert.AreEqual(fault == "unknown" ? "management.transaction_unknown" : "management.authority.unavailable", uncertain.Code);
        Assert.AreEqual(fault == "unknown" ? TransactionOutcome.Unknown : replay ? TransactionOutcome.NotCommitted : TransactionOutcome.Committed,
            uncertain.AttemptCompletion!.Outcome);
        if (fault != "unknown")
        {
            Assert.HasCount(1, uncertain.AttemptCompletion.CleanupErrors);
        }
        Assert.AreEqual(replay ? 0 : 1, boundary.ConfirmedNativeCommits);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_adoption"));
        long expected = before + 128 + ManagementConfigurationWire.EncodeRequest(report).LongLength;
        Assert.AreEqual(expected, (await Catalog(setup.Actor)).UsedBytes);
        string received;
        await using (NpgsqlCommand original = fixture.Source.CreateCommand($"SELECT received_at FROM {Table(fixture, "configuration_adoption")} WHERE registration_id=@id"))
        {
            original.Parameters.AddWithValue("id", Guid.Parse(setup.Registration));
            received = ((DateTime)(await original.ExecuteScalarAsync())!).ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }
        ManagementReportReceipt recovered = await Accepted(workload, setup.Registration, report);
        Assert.AreEqual("replayed", recovered.Disposition);
        Assert.AreEqual("1", recovered.AcceptedSequence);
        Assert.AreEqual(received, recovered.ReceivedAtUtc);
        Assert.AreEqual(expected, (await Catalog(setup.Actor)).UsedBytes);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_adoption"));
    }

    [TestMethod]
    public async Task CurrentMapProjectionDoesNotTreatHistoricalAcceptanceAsCurrentAndPreservesNoRecipientCohorts()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        Setup state = await Prepare(fixture);
        HttpClient workload = fixture.Client("workload-a");
        ManagementConfigurationStatusReport first = Applied(state.Release);
        await Accepted(workload, state.Registration, first);
        ManagementPage<ManagementConfigurationAdoptionEntry> initial = await Status(state.Actor, fixture.ApplicationA);
        ManagementConfigurationAdoptionEntry observed = initial.Items.Single(row => row.RegistrationId == state.Registration);
        Assert.AreEqual("applied", observed.Convergence);
        Assert.AreEqual("fresh", observed.RegistrationFreshness);
        Assert.AreEqual("fresh", observed.AdoptionFreshness);
        Assert.AreEqual("same-reported-label", observed.ReportedInstanceId);
        Assert.IsTrue(initial.Items.Any(row => row.RegistrationId is null && row.Convergence == "noInstances"));
        ManagementConfigurationAssignmentMap before = await Map(state.Actor, fixture.ApplicationA);
        await Assign(state.Actor, fixture.ApplicationA, before.ETag, "advance-generation",
            new(1, "replace", state.Release, [], "New controlled generation with identical content"));
        observed = (await Status(state.Actor, fixture.ApplicationA)).Items.Single(row => row.RegistrationId == state.Registration);
        Assert.AreEqual("pending", observed.Convergence);
        Assert.AreEqual("2", observed.Desired!.Generation);
        Assert.AreEqual("1", observed.Reported!.Applied!.Generation);
        var candidate = first.Entries[0] with
        {
            State = "PendingRestart",
            Desired = new(state.Release, "2"),
            UpdateMode = "RestartRequired"
        };
        await Accepted(workload, state.Registration, new("2", Time, [candidate]));
        Assert.AreEqual("pendingRestart", (await Status(state.Actor, fixture.ApplicationA)).Items.Single(row => row.RegistrationId == state.Registration).Convergence);
        await Accepted(workload, state.Registration, new("3", Time, [candidate with
        {
            State = "Rejected", CandidateFailureCode = "Configuration.Validation"
        }]));
        Assert.AreEqual("rejected", (await Status(state.Actor, fixture.ApplicationA)).Items.Single(row => row.RegistrationId == state.Registration).Convergence);
        await Accepted(workload, state.Registration, new("4", Time, [candidate with
        {
            State = "Faulted", LiveStateValid = false, Applied = null, CandidateFailureCode = "Configuration.LiveSetter"
        }]));
        observed = (await Status(state.Actor, fixture.ApplicationA)).Items.Single(row => row.RegistrationId == state.Registration);
        Assert.AreEqual("faulted", observed.Convergence);
        Assert.IsNull(observed.Reported!.Applied);
        Assert.AreEqual("1", observed.Reported.LastSuccessful!.Generation);
        string json = System.Text.Encoding.UTF8.GetString(ManagementConfigurationWire.EncodeResponse(new ManagementPage<ManagementConfigurationAdoptionEntry>([observed])));
        Assert.IsFalse(json.Contains("payload", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("checksum", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task RegistrationAndAdoptionFreshnessRemainIndependentFromSemanticCurrentAcceptance()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        Setup state = await Prepare(fixture);
        await Accepted(fixture.Client("workload-a"), state.Registration, Applied(state.Release));
        await Sql(fixture, $"UPDATE {Table(fixture, "configuration_adoption")} SET received_at=clock_timestamp()-interval '1 day' WHERE registration_id=@id", state.Registration);
        ManagementConfigurationAdoptionEntry entry = (await Status(state.Actor, fixture.ApplicationA)).Items.Single(row => row.RegistrationId == state.Registration);
        Assert.AreEqual("fresh", entry.RegistrationFreshness);
        Assert.AreEqual("stale", entry.AdoptionFreshness);
        Assert.AreEqual("applied", entry.Convergence, "Semantic matching does not erase the separate stale facet.");
        await Sql(fixture, $"UPDATE {Table(fixture, "management_registration")} SET registered_at=clock_timestamp()-interval '1 day' WHERE registration_id=@id", state.Registration);
        entry = (await Status(state.Actor, fixture.ApplicationA)).Items.Single(row => row.RegistrationId == state.Registration);
        Assert.AreEqual("stale", entry.RegistrationFreshness);
        await Accepted(fixture.Client("workload-a"), state.Registration, Applied(state.Release) with
        {
            Sequence = "2"
        });
        entry = (await Status(state.Actor, fixture.ApplicationA)).Items.Single(row => row.RegistrationId == state.Registration);
        Assert.AreEqual("stale", entry.RegistrationFreshness);
        Assert.AreEqual("fresh", entry.AdoptionFreshness, "A new report renews only its own receipt.");
        using HttpResponseMessage closed = await fixture.Client("workload-a").DeleteAsync("registrations/" + state.Registration);
        Assert.AreEqual(HttpStatusCode.OK, closed.StatusCode);
        entry = (await Status(state.Actor, fixture.ApplicationA)).Items.Single(row => row.RegistrationId == state.Registration);
        Assert.AreEqual("offline", entry.RegistrationFreshness);
    }

    [TestMethod]
    public async Task AppScopedAuthorizationPrecedesPagingAndEachContinuationIsReauthorized()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        Setup state = await Prepare(fixture);
        ManagementPage<ManagementConfigurationAdoptionEntry> first = await Status(state.Actor, fixture.ApplicationA, "?pageSize=1");
        Assert.HasCount(1, first.Items);
        Assert.IsNotNull(first.NextCursor);
        ManagementPage<ManagementConfigurationAdoptionEntry> second = await Status(state.Actor, fixture.ApplicationA,
            "?pageSize=1&cursor=" + Uri.EscapeDataString(first.NextCursor));
        Assert.HasCount(1, second.Items);
        Assert.AreNotEqual(first.Items[0].RegistrationId, second.Items[0].RegistrationId);
        foreach (HttpClient denied in new[] { fixture.Client("reader"), fixture.Client("operator-b"), fixture.Client("workload-a") })
        {
            using HttpResponseMessage response = await denied.GetAsync(StatusPath(fixture.ApplicationA) + "?pageSize=1&cursor=" + Uri.EscapeDataString(first.NextCursor));
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
        using HttpResponseMessage foreign = await state.Actor.GetAsync(StatusPath(fixture.ApplicationB));
        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        using HttpResponseMessage invalid = await state.Actor.GetAsync(StatusPath(fixture.ApplicationA) + "?pageSize=1&cursor=invalid");
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        using HttpResponseMessage filter = await state.Actor.GetAsync(StatusPath(fixture.ApplicationA) + "?cohortId=other");
        Assert.AreEqual(HttpStatusCode.BadRequest, filter.StatusCode);
    }

    [TestMethod]
    public async Task LegalUnicodeMetadataProducesBoundedCompleteContinuationPagesRatherThanAnEncoderFailure()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        Setup setup = await Prepare(fixture);
        HttpClient workload = fixture.Client("workload-a");
        string label = string.Concat(Enumerable.Repeat("\U0001f30d", 1024));
        string version = string.Concat(Enumerable.Repeat("\U0001f30d", 512));
        var expected = new HashSet<string>(StringComparer.Ordinal) { setup.Registration };
        for (int index = 0; index < 64; index++)
        {
            string id = await Register(workload, ManagementHostFixture.Request(label) with
            {
                ApplicationVersion = version
            });
            expected.Add(id);
            await Accepted(workload, id, new("1", Time, [new("shared.settings", "1", "HotReload", "Pending", false)]));
        }
        string? cursor = null;
        var observed = new HashSet<string>(StringComparer.Ordinal);
        int pages = 0;
        do
        {
            using HttpResponseMessage response = await setup.Actor.GetAsync(StatusPath(fixture.ApplicationA) + "?pageSize=500"
                + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor)));
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, System.Text.Encoding.UTF8.GetString(bytes));
            Assert.IsLessThanOrEqualTo(1024 * 1024, bytes.Length);
            ManagementPage<ManagementConfigurationAdoptionEntry> page = ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationAdoptionEntry>>(bytes)!;
            foreach (ManagementConfigurationAdoptionEntry entry in page.Items.Where(item => item.RegistrationId is not null))
            {
                Assert.IsTrue(observed.Add(entry.RegistrationId!), "A continuation must neither overlap nor omit a returned recipient.");
                if (entry.RegistrationId != setup.Registration)
                {
                    Assert.AreEqual(label, entry.ReportedInstanceId);
                    Assert.AreEqual(version, entry.ApplicationVersion);
                }
            }
            cursor = page.NextCursor;
            pages++;
            Assert.IsLessThanOrEqualTo(4, pages);
        } while (cursor is not null);
        Assert.IsGreaterThan(1, pages, "Legal escaped Unicode metadata must force an actual byte-bounded continuation.");
        CollectionAssert.AreEquivalent(expected.ToArray(), observed.ToArray());
    }

    [TestMethod]
    public async Task ChangedCatalogHostPreservesOldSessionHistoryWithoutClaimingCurrentTargetConvergence()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        Setup setup = await Prepare(fixture);
        await Accepted(fixture.Client("workload-a"), setup.Registration, Applied(setup.Release));
        ManagementConfigurationAssignmentMap before = await Map(setup.Actor, fixture.ApplicationA);
        ManagementConfigurationOptions original = Deployment();
        ManagementConfigurationManifest changed = original.Manifest with
        {
            Bindings = original.Manifest.Bindings.Select(binding => binding.Application == "managed-a"
                ? binding with
                {
                    HostApplicationName = "ChangedHost"
                } : binding).ToArray()
        };
        var configuration = new ManagementConfigurationOptions(System.Text.Json.JsonSerializer.Serialize(changed,
            ManagementConfigurationOptions.Json), original.QuotaBytes);
        var options = new ManagementServiceOptions(fixture.Options.ConnectionString, fixture.Options.CopyCursorSigningKey(),
            fixture.Options.Persistence, fixture.Options.BasePath, fixture.Options.MaximumConcurrentRequests,
            fixture.Options.OperationsSchema, fixture.Options.MessagingSchema, fixture.Options.Delivery, fixture.Options.Backend, configuration);
        await fixture.StopReplicasAsync();
        await fixture.StartReplicaAsync(serviceOptions: options);
        HttpClient actor = fixture.Client("operator-a", fixture.Origins.Count - 1);
        await Apply(actor, await Catalog(actor), "change-host");
        ManagementConfigurationAssignmentMap after = await Map(actor, fixture.ApplicationA);
        Assert.AreEqual(before.BindingId, after.BindingId);
        Assert.AreEqual(before.Generation, after.Generation);
        using HttpResponseMessage unsupported = await fixture.Client("workload-a", fixture.Origins.Count - 1).GetAsync(
            "registrations/" + setup.Registration + "/configuration/shared.settings?schemaVersion=1");
        Assert.AreEqual(HttpStatusCode.NotFound, unsupported.StatusCode);
        ManagementConfigurationAdoptionEntry observed = (await Status(actor, fixture.ApplicationA)).Items
            .Single(row => row.RegistrationId == setup.Registration);
        Assert.AreEqual("unknown", observed.Convergence);
        Assert.AreEqual("Applied", observed.Reported!.State);
        Assert.AreEqual(setup.Release, observed.Reported.LastSuccessful!.ReleaseId);
        Assert.AreEqual("fresh", observed.RegistrationFreshness);
        Assert.AreEqual("fresh", observed.AdoptionFreshness);
    }

    [TestMethod]
    public async Task QuotaRefusalRollsBackAndACompleteSmallerReplacementReleasesOnlyItsOwnDelta()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        Setup state = await Prepare(fixture);
        HttpClient workload = fixture.Client("workload-a");
        ManagementConfigurationStatusReport report = Applied(state.Release);
        long before = (await Catalog(state.Actor)).UsedBytes;
        long bytes = 128 + ManagementConfigurationWire.EncodeRequest(report).LongLength;
        await using (NpgsqlCommand limit = fixture.Source.CreateCommand($"UPDATE {Table(fixture, "configuration_catalog")} SET quota_bytes=used_bytes+@limit WHERE singleton"))
        {
            limit.Parameters.AddWithValue("limit", bytes - 1);
            await limit.ExecuteNonQueryAsync();
        }
        using HttpResponseMessage refused = await Report(workload, state.Registration, report);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.AreEqual(before, (await Catalog(state.Actor)).UsedBytes);
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_adoption"));
        await using (NpgsqlCommand restore = fixture.Source.CreateCommand($"UPDATE {Table(fixture, "configuration_catalog")} SET quota_bytes=33554432 WHERE singleton"))
        {
            await restore.ExecuteNonQueryAsync();
        }
        await Accepted(workload, state.Registration, report);
        var empty = new ManagementConfigurationStatusReport("2", Time, []);
        await Accepted(workload, state.Registration, empty);
        Assert.AreEqual(before + 128 + ManagementConfigurationWire.EncodeRequest(empty).LongLength, (await Catalog(state.Actor)).UsedBytes);
        Assert.AreEqual("unknown", (await Status(state.Actor, fixture.ApplicationA)).Items.Single(row => row.RegistrationId == state.Registration).Convergence,
            "Complete replacement omission does not retain an old per-root success.");
    }

    [TestMethod]
    [DataRow("FutureState", "HotReload")]
    [DataRow("Applied", "FutureMode")]
    public async Task FutureRetainedStatesOrModesProjectAsUnknownThroughTheActualAuthorizedRead(string state, string mode)
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        Setup setup = await Prepare(fixture);
        ManagementConfigurationStatusReport original = Applied(setup.Release);
        await Accepted(fixture.Client("workload-a"), setup.Registration, original);
        var retained = original with
        {
            Sequence = "2",
            Entries = [original.Entries[0] with { State = state, UpdateMode = mode }]
        };
        // Model an additive report retained by a newer service version in this isolated database.
        // Current incoming requests remain closed; the original real workload established ownership/session.
        byte[] body = ManagementConfigurationWire.EncodeResponse(retained);
        long bytes = 128 + body.LongLength;
        long delta = bytes - (128 + ManagementConfigurationWire.EncodeRequest(original).LongLength);
        await using NpgsqlConnection connection = await fixture.Source.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using (var update = new NpgsqlCommand($"""
            UPDATE {Table(fixture, "configuration_catalog")} SET used_bytes=used_bytes+@delta WHERE singleton;
            UPDATE {Table(fixture, "configuration_adoption")} SET sequence=2,digest=@digest,body=@body,logical_bytes=@bytes WHERE registration_id=@id
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("delta", delta);
            update.Parameters.AddWithValue("digest", System.Security.Cryptography.SHA256.HashData(body));
            update.Parameters.AddWithValue("body", body);
            update.Parameters.AddWithValue("bytes", bytes);
            update.Parameters.AddWithValue("id", Guid.Parse(setup.Registration));
            Assert.AreEqual(2, await update.ExecuteNonQueryAsync());
        }
        await transaction.CommitAsync();
        ManagementConfigurationAdoptionEntry projected = (await Status(setup.Actor, fixture.ApplicationA)).Items
            .Single(row => row.RegistrationId == setup.Registration);
        Assert.AreEqual("unknown", projected.Convergence);
        Assert.AreEqual(state, projected.Reported!.State);
        Assert.AreEqual(mode, projected.Reported.UpdateMode);
        Assert.IsFalse(state == "FutureState" ? projected.Reported.KnownState.IsKnown : projected.Reported.KnownUpdateMode.IsKnown);
    }

    private static async Task<Setup> Prepare(ManagementHostFixture fixture)
    {
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "adoption-catalog");
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        string release = (await CreateRelease(actor, fixture.ApplicationA, map.ETag, "adoption-release",
            new(1, new("absent"), new("absent"), "Explicit defaults release"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, map.ETag, "adoption-assignment", new(1, "replace", release, [], "Assign current release"));
        string id = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + id)
        {
            Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request("same-reported-label")))
        };
        request.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage registered = await fixture.Client("workload-a").SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, registered.StatusCode, await registered.Content.ReadAsStringAsync());
        return new(actor, id, release);
    }

    private static ManagementConfigurationStatusReport Applied(string release)
    {
        var accepted = new ManagementConfigurationAppliedVersion(release, "1", "0");
        return new("1", Time, [new("shared.settings", "1", "HotReload", "Applied", true,
            new(release, "1"), accepted, accepted)]);
    }

    private static async Task<HttpResponseMessage> Report(HttpClient client, string registration, ManagementConfigurationStatusReport report)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + registration + "/configuration-status")
        {
            Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(report))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.SendAsync(request);
    }

    private static async Task<ManagementReportReceipt> Accepted(HttpClient client, string registration, ManagementConfigurationStatusReport report)
    {
        using HttpResponseMessage response = await Report(client, registration, report);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return ManagementWire.DecodeResponse<ManagementReportReceipt>(await response.Content.ReadAsByteArrayAsync())!;
    }

    private static async Task<string> Register(HttpClient workload, ManagementRegistrationRequest body)
    {
        string id = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + id)
        {
            Content = new ByteArrayContent(ManagementWire.EncodeRequest(body))
        };
        request.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage response = await workload.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return id;
    }

    private static async Task<ManagementPage<ManagementConfigurationAdoptionEntry>> Status(HttpClient client, string app, string query = "")
    {
        using HttpResponseMessage response = await client.GetAsync(StatusPath(app) + query);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementPage<ManagementConfigurationAdoptionEntry>>(response);
    }

    private static string StatusPath(string application) => "applications/" + application + "/configuration-status";
    private static string Table(ManagementHostFixture fixture, string table) => "\"" + fixture.Options.Persistence.Schema + "\".\"" + table + "\"";

    private static async Task<string> Presence(ManagementHostFixture fixture, string registration)
    {
        await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT registered_at::text||'/'||COALESCE(observation_received_at::text,'none') FROM {Table(fixture, "management_registration")} WHERE registration_id=@id");
        command.Parameters.AddWithValue("id", Guid.Parse(registration));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Sql(ManagementHostFixture fixture, string sql, string registration)
    {
        await using NpgsqlCommand command = fixture.Source.CreateCommand(sql);
        command.Parameters.AddWithValue("id", Guid.Parse(registration));
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
    }

    private sealed record Setup(HttpClient Actor, string Registration, string Release);
}
