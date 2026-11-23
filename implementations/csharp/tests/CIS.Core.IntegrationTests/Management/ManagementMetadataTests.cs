using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CIS.Audit;
using CIS.Management;
using CIS.Management.Wire;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementMetadataTests
{
    [TestMethod]
    public async Task OriginalMutationAndOneRequiredAuditAreReplayedAcrossReplicasAndHostReconstruction()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HttpClient first = fixture.Client("operator-a");
        ManagementApplication original = await Application(first, fixture.ApplicationA);
        var command = new ManagementMetadataCommand(1, "Administrative display", "Original description");
        using HttpResponseMessage accepted = await Replace(first, fixture.ApplicationA, "metadata-original", original.MetadataETag, command);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        ManagementMetadataResult applied = await Decode<ManagementMetadataResult>(accepted);
        Assert.AreEqual("prepared", applied.ResultSource);
        Assert.AreEqual("committed", applied.OriginalCompletion);
        Assert.AreEqual("committed", applied.AttemptCompletion!.Outcome);
        Assert.AreEqual("2", applied.Result!.MetadataRevision);
        Assert.IsNull(applied.Rejection);

        // A later real root has no in-memory result cache and retains the original precondition.
        await fixture.StartReplicaAsync();
        HttpClient reconstructed = fixture.Client("operator-a", replica: 2);
        using HttpResponseMessage repeated = await Replace(reconstructed, fixture.ApplicationA, "metadata-original", original.MetadataETag, command);
        ManagementMetadataResult replay = await Decode<ManagementMetadataResult>(repeated);
        Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode);
        Assert.AreEqual("replayed", replay.ResultSource);
        Assert.AreEqual(applied.Result, replay.Result);
        Assert.AreEqual(applied.Operation, replay.Operation);
        Assert.AreEqual("notCommitted", replay.AttemptCompletion!.Outcome);
        using HttpResponseMessage lookupResponse = await reconstructed.GetAsync(Lookup(fixture.ApplicationA, "metadata-original"));
        ManagementMetadataResult found = await Decode<ManagementMetadataResult>(lookupResponse);
        Assert.AreEqual("found", found.ResultSource);
        Assert.AreEqual(applied.Result, found.Result);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        AuditRecord audit = (await Audits(fixture)).Single();
        Assert.AreEqual(AuditOutcome.Succeeded, audit.Outcome);
        Assert.AreEqual(AuditActorKind.User, audit.Actor.Kind);
        Assert.AreEqual("operator-a", audit.Actor.Subject!.Identifier);
        Assert.AreEqual(fixture.ApplicationA, audit.Target!.ResourceIdentifier);
        Assert.AreEqual(applied.Operation.Name, audit.OperationReference!.OperationName);
        Assert.AreEqual(applied.Operation.Scope, audit.OperationReference.Scope);
        Assert.AreEqual(applied.Operation.Key, audit.OperationReference.Key);
        using JsonDocument details = JsonDocument.Parse(audit.DetailsJson);
        Assert.AreEqual("1", details.RootElement.GetProperty("previousRevision").GetString());
        Assert.AreEqual("2", details.RootElement.GetProperty("resultingRevision").GetString());
        Assert.AreEqual(command.DisplayName, details.RootElement.GetProperty("after").GetProperty("displayName").GetString());
    }

    [TestMethod]
    public async Task StaleDecisionRemainsNativeRejectedAndRetainsStableUnchangedAuditFactsAcrossLaterEdits()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HttpClient actor = fixture.Client("operator-a");
        ManagementApplication original = await Application(actor, fixture.ApplicationA);
        using (HttpResponseMessage first = await Replace(actor, fixture.ApplicationA, "first-edit", original.MetadataETag, new(1, "First")))
        {
            Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        }
        using HttpResponseMessage rejected = await Replace(actor, fixture.ApplicationA, "rejected-original", original.MetadataETag, new(1, "Never applied"));
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        ManagementProblem problem = await Decode<ManagementProblem>(rejected);
        Assert.AreEqual("management.precondition_failed", problem.Code);
        Assert.IsNull(problem.Result!.Result, "Native RM03 rejection has no fabricated metadata value.");
        Assert.AreEqual("management.precondition_failed", problem.Result.Rejection!.Code);
        Assert.AreEqual("committed", problem.Result.OriginalCompletion);
        ManagementApplication second = await Application(actor, fixture.ApplicationA);
        using (HttpResponseMessage later = await Replace(actor, fixture.ApplicationA, "later-edit", second.MetadataETag, new(1, "Later")))
        {
            Assert.AreEqual(HttpStatusCode.OK, later.StatusCode);
        }
        using HttpResponseMessage repeated = await Replace(fixture.Client("operator-a", 1), fixture.ApplicationA,
            "rejected-original", original.MetadataETag, new(1, "Never applied"));
        ManagementProblem replay = await Decode<ManagementProblem>(repeated);
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, repeated.StatusCode);
        Assert.AreEqual("replayed", replay.Result!.ResultSource);
        Assert.AreEqual(problem.Result.Rejection, replay.Result.Rejection);
        using HttpResponseMessage lookup = await actor.GetAsync(Lookup(fixture.ApplicationA, "rejected-original"));
        Assert.AreEqual(HttpStatusCode.OK, lookup.StatusCode);
        ManagementMetadataResult found = await Decode<ManagementMetadataResult>(lookup);
        Assert.IsNull(found.Result);
        Assert.AreEqual(problem.Result.Rejection, found.Rejection);
        AuditRecord rejectionAudit = (await Audits(fixture)).Single(record => record.Outcome == AuditOutcome.Rejected);
        using JsonDocument details = JsonDocument.Parse(rejectionAudit.DetailsJson);
        Assert.AreEqual("2", details.RootElement.GetProperty("previousRevision").GetString());
        Assert.AreEqual("2", details.RootElement.GetProperty("resultingRevision").GetString());
        Assert.AreEqual(details.RootElement.GetProperty("before").GetRawText(), details.RootElement.GetProperty("after").GetRawText());
        Assert.AreEqual(3L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        Assert.AreEqual("Later", (await Application(actor, fixture.ApplicationA)).Metadata.DisplayName);
    }

    [TestMethod]
    public async Task ExactFingerprintAndValidatedActorDelegationPreventCrossResultDisclosureOrAccidentalNormalization()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HttpClient actor = fixture.Client("operator-a");
        ManagementApplication original = await Application(actor, fixture.ApplicationA);
        using (HttpResponseMessage first = await Replace(actor, fixture.ApplicationA, "exact-bytes", original.MetadataETag, new(1, "e\u0301")))
        {
            Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        }
        using (HttpResponseMessage changed = await Replace(actor, fixture.ApplicationA, "exact-bytes", original.MetadataETag, new(1, "\u00e9")))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, changed.StatusCode);
            Assert.AreEqual("management.operation.conflict", (await Decode<ManagementProblem>(changed)).Code);
        }
        using (HttpResponseMessage preconditionChanged = await Replace(actor, fixture.ApplicationA, "exact-bytes", "\"metadata-2\"", new(1, "e\u0301")))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, preconditionChanged.StatusCode);
        }
        using (HttpResponseMessage otherActor = await fixture.Client("operator-b").GetAsync(Lookup(fixture.ApplicationA, "exact-bytes")))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, otherActor.StatusCode);
        }
        using (HttpResponseMessage denied = await fixture.Client("reader").GetAsync(Lookup(fixture.ApplicationA, "exact-bytes")))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode);
        }
        using (HttpResponseMessage hidden = await actor.GetAsync(Lookup(fixture.ApplicationB, "exact-bytes")))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        ManagementApplication current = await Application(actor, fixture.ApplicationA);
        HttpClient automation = fixture.Client("workload-a", explicitToken: fixture.Token("workload-a", delegated: true));
        using HttpResponseMessage delegated = await Replace(automation, fixture.ApplicationA, "delegation-key", current.MetadataETag, new(1, "Delegated"));
        Assert.AreEqual(HttpStatusCode.OK, delegated.StatusCode);
        using HttpResponseMessage directLookup = await fixture.Client("workload-a").GetAsync(Lookup(fixture.ApplicationA, "delegation-key"));
        Assert.AreEqual(HttpStatusCode.NotFound, directLookup.StatusCode);
        AuditRecord delegatedAudit = (await Audits(fixture)).Single(record => record.Actor.VerifiedDelegation is not null);
        Assert.AreEqual(AuditActorKind.Application, delegatedAudit.Actor.Kind);
        Assert.AreEqual("workload-a", delegatedAudit.Actor.Subject!.Identifier);
        Assert.AreEqual("operator-a", delegatedAudit.Actor.HumanSubject!.Identifier);
    }

    [TestMethod]
    public async Task ConcurrentSameKeyRequestsAcrossTwoNativeRootsProduceOneEffectJournalAndAudit()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HttpClient first = fixture.Client("operator-a");
        HttpClient second = fixture.Client("operator-a", 1);
        ManagementApplication original = await Application(first, fixture.ApplicationA);
        Task<HttpResponseMessage> one = Replace(first, fixture.ApplicationA, "concurrent-key", original.MetadataETag, new(1, "Concurrent"));
        Task<HttpResponseMessage> two = Replace(second, fixture.ApplicationA, "concurrent-key", original.MetadataETag, new(1, "Concurrent"));
        HttpResponseMessage[] responses = await Task.WhenAll(one, two);
        using HttpResponseMessage a = responses[0];
        using HttpResponseMessage b = responses[1];
        Assert.AreEqual(HttpStatusCode.OK, a.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, b.StatusCode);
        ManagementMetadataResult resultA = await Decode<ManagementMetadataResult>(a);
        ManagementMetadataResult resultB = await Decode<ManagementMetadataResult>(b);
        CollectionAssert.AreEquivalent(new[] { "prepared", "replayed" }, new[] { resultA.ResultSource, resultB.ResultSource });
        Assert.AreEqual(resultA.Result, resultB.Result);
        Assert.AreEqual("2", (await Application(first, fixture.ApplicationA)).MetadataRevision);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    [TestMethod]
    public async Task DistinctConcurrentKeysWithOneOriginalPreconditionRecordOneAppliedAndOneNativeRejectedDecision()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HttpClient first = fixture.Client("operator-a");
        HttpClient second = fixture.Client("operator-a", 1);
        ManagementApplication original = await Application(first, fixture.ApplicationA);
        Task<HttpResponseMessage> one = Replace(first, fixture.ApplicationA, "distinct-first", original.MetadataETag, new(1, "First candidate"));
        Task<HttpResponseMessage> two = Replace(second, fixture.ApplicationA, "distinct-second", original.MetadataETag, new(1, "Second candidate"));
        HttpResponseMessage[] responses = await Task.WhenAll(one, two);
        using HttpResponseMessage a = responses[0];
        using HttpResponseMessage b = responses[1];
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed },
            new[] { a.StatusCode, b.StatusCode });
        HttpResponseMessage accepted = a.StatusCode == HttpStatusCode.OK ? a : b;
        HttpResponseMessage refused = a.StatusCode == HttpStatusCode.PreconditionFailed ? a : b;
        ManagementMetadataResult applied = await Decode<ManagementMetadataResult>(accepted);
        ManagementProblem rejected = await Decode<ManagementProblem>(refused);
        Assert.AreEqual("prepared", applied.ResultSource);
        Assert.AreEqual("committed", applied.OriginalCompletion);
        Assert.AreEqual("2", applied.Result!.MetadataRevision);
        Assert.AreEqual("management.precondition_failed", rejected.Code);
        Assert.AreEqual("prepared", rejected.Result!.ResultSource);
        Assert.AreEqual("committed", rejected.Result.OriginalCompletion);
        Assert.IsNull(rejected.Result.Result);
        Assert.AreEqual("management.precondition_failed", rejected.Result.Rejection!.Code);
        Assert.AreNotEqual(applied.Operation.Key, rejected.Result.Operation.Key);
        ManagementApplication current = await Application(first, fixture.ApplicationA);
        Assert.AreEqual("2", current.MetadataRevision);
        Assert.AreEqual(applied.Result.Metadata, current.Metadata);
        Assert.AreEqual(2L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        IReadOnlyList<AuditRecord> audits = await Audits(fixture);
        Assert.HasCount(2, audits);
        Assert.AreEqual(1, audits.Count(record => record.Outcome == AuditOutcome.Succeeded));
        AuditRecord rejectedAudit = audits.Single(record => record.Outcome == AuditOutcome.Rejected);
        Assert.AreEqual(rejected.Result.Operation.Key, rejectedAudit.OperationReference!.Key);
        using JsonDocument details = JsonDocument.Parse(rejectedAudit.DetailsJson);
        Assert.AreEqual("2", details.RootElement.GetProperty("previousRevision").GetString());
        Assert.AreEqual("2", details.RootElement.GetProperty("resultingRevision").GetString());
        Assert.AreEqual(details.RootElement.GetProperty("before").GetRawText(), details.RootElement.GetProperty("after").GetRawText());
    }

    [TestMethod]
    public async Task ExpiredOriginalPayloadRetainsDuplicateProtectionAndCannotReexecuteOrCreateAnotherAudit()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HttpClient actor = fixture.Client("operator-a");
        ManagementApplication original = await Application(actor, fixture.ApplicationA);
        var command = new ManagementMetadataCommand(1, "Original retained effect");
        using (HttpResponseMessage accepted = await Replace(actor, fixture.ApplicationA, "expired-original", original.MetadataETag, command))
        {
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        }
        ManagementApplication applied = await Application(actor, fixture.ApplicationA);
        // Owned fixture data exercises the real native expiry state. The product definition's
        // default remains indefinite; this does not introduce automatic retention or maintenance.
        await using (var expire = fixture.Source.CreateCommand($"""
            WITH observed AS (SELECT clock_timestamp() AS at)
            UPDATE "{fixture.Options.OperationsSchema}".operation_journal
            SET recorded_at=observed.at-INTERVAL '2 minutes', result_expires_at=observed.at-INTERVAL '1 minute',
                result_payload=NULL
            FROM observed WHERE idempotency_key=@key AND finalized AND protection_expires_at IS NULL
            """))
        {
            expire.Parameters.AddWithValue("key", "expired-original");
            Assert.AreEqual(1, await expire.ExecuteNonQueryAsync());
        }
        using (HttpResponseMessage lookup = await fixture.Client("operator-a", 1).GetAsync(Lookup(fixture.ApplicationA, "expired-original")))
        {
            Assert.AreEqual(HttpStatusCode.Gone, lookup.StatusCode);
            ManagementProblem problem = await Decode<ManagementProblem>(lookup);
            Assert.AreEqual("management.operation.result_expired", problem.Code);
            Assert.AreEqual("expired-original", problem.Operation!.Key);
            Assert.IsNull(problem.Result);
        }
        using (HttpResponseMessage resubmission = await Replace(actor, fixture.ApplicationA, "expired-original", original.MetadataETag, command))
        {
            Assert.AreEqual(HttpStatusCode.Gone, resubmission.StatusCode);
            Assert.AreEqual("management.operation.result_expired", (await Decode<ManagementProblem>(resubmission)).Code);
        }
        Assert.AreEqual(applied, await Application(actor, fixture.ApplicationA));
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        await using var protection = fixture.Source.CreateCommand($"""
            SELECT finalized AND result_payload IS NULL AND result_expires_at<clock_timestamp() AND protection_expires_at IS NULL
            FROM "{fixture.Options.OperationsSchema}".operation_journal WHERE idempotency_key=@key
            """);
        protection.Parameters.AddWithValue("key", "expired-original");
        Assert.IsTrue(await protection.ExecuteScalarAsync() is true, "Expiry must retain the original protected identity.");
    }

    [TestMethod]
    public async Task RequiredAuditStorageRefusalRollsBackMetadataAndOriginalClaim()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(maximumRetainedEnvelopeBytes: 1);
        HttpClient actor = fixture.Client("operator-a");
        ManagementApplication original = await Application(actor, fixture.ApplicationA);
        using (HttpResponseMessage refused = await Replace(actor, fixture.ApplicationA, "audit-refused", original.MetadataETag, new(1, "Must roll back")))
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.AreEqual("management.audit.unavailable", (await Decode<ManagementProblem>(refused)).Code);
        }
        Assert.AreEqual(original, await Application(actor, fixture.ApplicationA));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    [TestMethod]
    public async Task MissingWeakMultiplePreconditionsAndUnsupportedCommandsNeverReachTheJournal()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        HttpClient actor = fixture.Client("operator-a");
        foreach ((string? etag, HttpStatusCode status) in new[]
        {
            ((string?)null, HttpStatusCode.PreconditionRequired), ("W/\"metadata-1\"", HttpStatusCode.BadRequest),
            ("*", HttpStatusCode.BadRequest), ("\"metadata-1\",\"metadata-2\"", HttpStatusCode.BadRequest)
        })
        {
            using HttpResponseMessage response = await Replace(actor, fixture.ApplicationA, "invalid-header", etag, new(1, "Unused"));
            Assert.AreEqual(status, response.StatusCode);
        }
        using var unsupported = new HttpRequestMessage(HttpMethod.Put, Path(fixture.ApplicationA));
        unsupported.Headers.Add("Idempotency-Key", "invalid-version");
        unsupported.Headers.TryAddWithoutValidation("If-Match", "\"metadata-1\"");
        unsupported.Content = new StringContent("{\"commandVersion\":2,\"displayName\":\"Unused\"}", System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage version = await actor.SendAsync(unsupported);
        Assert.AreEqual(HttpStatusCode.BadRequest, version.StatusCode);
        Assert.AreEqual("management.unsupported_version", (await Decode<ManagementProblem>(version)).Code);
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
    }

    private static string Path(string application) => "applications/" + application + "/metadata";
    private static string Lookup(string application, string key) => "applications/" + application + "/metadata-operations/" + key;
    private static async Task<HttpResponseMessage> Replace(HttpClient client, string application, string key, string? etag, ManagementMetadataCommand command)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Path(application));
        request.Headers.Add("Idempotency-Key", key);
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }
        request.Content = new ByteArrayContent(ManagementWire.EncodeRequest(command));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.SendAsync(request);
    }

    private static async Task<T> Decode<T>(HttpResponseMessage response) where T : class
        => ManagementWire.DecodeResponse<T>(await response.Content.ReadAsByteArrayAsync())
            ?? throw new AssertFailedException("A bounded native management response was expected.");

    private static async Task<ManagementApplication> Application(HttpClient client, string application)
    {
        using HttpResponseMessage response = await client.GetAsync("applications/" + application);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await Decode<ManagementApplication>(response);
    }

    private static async Task<long> Count(ManagementHostFixture fixture, string schema, string table)
    {
        await using var command = fixture.Source.CreateCommand($"SELECT count(*) FROM \"{schema}\".\"{table}\"");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<IReadOnlyList<AuditRecord>> Audits(ManagementHostFixture fixture)
    {
        await using var command = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY created_at,record_id");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        var audits = new List<AuditRecord>();
        var codec = new AuditRecordCodec();
        while (await reader.ReadAsync())
        {
            using JsonDocument envelope = JsonDocument.Parse(reader.GetFieldValue<byte[]>(0));
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText());
            Assert.IsTrue(codec.TryDecode(payload, out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
            audits.Add(record!);
        }
        return audits.AsReadOnly();
    }
}
