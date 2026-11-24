using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationPublicationTests
{
    [TestMethod]
    public async Task LiteralSourceWithinInputBoundsCannotCommitAnUnreadableEscapedPublicationResponse()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementConfigurationDefinition descriptor = (await Definitions(actor)).Items.Single(row => row.Kind == "common");
        ManagementConfigurationSource source = await Source(actor, descriptor.SourceId);
        ManagementConfigurationCatalog before = await Catalog(actor);
        string text = new('<', 60_000);
        string payload = "{\"Greeting\":\"" + text + "\",\"IgnoredFirst\":\"" + text + "\",\"IgnoredSecond\":\"" + text + "\"}";
        Assert.IsLessThan(256 * 1024, Encoding.UTF8.GetByteCount(payload));
        var command = new ManagementConfigurationPublicationCommand(1, payload, "Publish reviewed source");
        byte[] literal = JsonSerializer.SerializeToUtf8Bytes(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Assert.IsLessThan(1024 * 1024, literal.Length);
        Assert.IsNotNull(ManagementConfigurationWire.DecodeRequest<ManagementConfigurationPublicationCommand>(literal),
            "The raw caller's complete command is valid; representability is a later original-operation decision.");
        long priorJournal = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long priorAudit = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        for (int index = 0; index < 2; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "configuration/sources/" + source.SourceId + "/publications");
            request.Headers.Add("Idempotency-Key", "literal-original-key");
            request.Headers.TryAddWithoutValidation("If-Match", source.ETag);
            request.Content = new ByteArrayContent(literal);
            request.Content.Headers.ContentType = new("application/json");
            using HttpResponseMessage response = await actor.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync());
            ManagementConfigurationProblem problem = await Decode<ManagementConfigurationProblem>(response);
            Assert.AreEqual("management.configuration.response_too_large", problem.Code);
            Assert.AreEqual("literal-original-key", problem.Operation!.Key);
            Assert.AreEqual(index == 0 ? "prepared" : "replayed", problem.Result!.ResultSource);
            Assert.AreEqual("committed", problem.Result.OriginalCompletion);
            Assert.IsNull(problem.Result.Result);
            Assert.AreEqual("management.configuration.response_too_large", problem.Result.Rejection!.Code);
        }
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        Assert.AreEqual(priorJournal + 1, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(priorAudit + 1, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        Assert.AreEqual(source.ETag, (await Source(actor, source.SourceId)).ETag);
        Assert.AreEqual(before.UsedBytes, (await Catalog(actor)).UsedBytes);
        using HttpResponseMessage lookup = await actor.GetAsync("configuration/sources/" + source.SourceId + "/publication-operations/literal-original-key");
        Assert.AreEqual(HttpStatusCode.OK, lookup.StatusCode);
        ManagementConfigurationOperationResult found = await Decode<ManagementConfigurationOperationResult>(lookup);
        Assert.AreEqual("found", found.ResultSource);
        Assert.AreEqual("management.configuration.response_too_large", found.Rejection!.Code);
        Assert.AreEqual("literal-original-key", found.Operation.Key);
    }

    [TestMethod]
    public async Task ConcurrentOriginalKeyPublishesExactUnicodeOnceAndRecoversPayloadFreeDecisionAcrossReplicas()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient first = fixture.Client("operator-a");
        HttpClient second = fixture.Client("operator-a", 1);
        await Apply(first, await Catalog(first), "catalog");
        ManagementConfigurationDefinition descriptor = (await Definitions(first)).Items.Single(row => row.Kind == "common");
        ManagementConfigurationSource source = await Source(first, descriptor.SourceId);
        const string payload = " {\"Greeting\":\"Café — 日本語\",\"Enabled\":false} ";
        Task<HttpResponseMessage> one = Publish(first, source, "same-original", payload);
        Task<HttpResponseMessage> two = Publish(second, source, "same-original", payload);
        using HttpResponseMessage a = await one;
        using HttpResponseMessage b = await two;
        Assert.AreEqual(HttpStatusCode.OK, a.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, b.StatusCode);
        ManagementConfigurationOperationResult resultA = await Decode<ManagementConfigurationOperationResult>(a);
        ManagementConfigurationOperationResult resultB = await Decode<ManagementConfigurationOperationResult>(b);
        CollectionAssert.AreEquivalent(new[] { "prepared", "replayed" }, new[] { resultA.ResultSource, resultB.ResultSource });
        Assert.AreEqual(resultA.Result, resultB.Result);
        Assert.AreEqual("committed", resultB.OriginalCompletion);
        using HttpResponseMessage content = await first.GetAsync("configuration/publications/" + resultA.Result!.PublicationId);
        ManagementConfigurationPublication publication = await Decode<ManagementConfigurationPublication>(content);
        Assert.AreEqual(payload, publication.PayloadJson);
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(payload), publication.Sha256);
        using HttpResponseMessage lookup = await second.GetAsync("configuration/sources/" + source.SourceId + "/publication-operations/same-original");
        Assert.AreEqual(resultA.Result, (await Decode<ManagementConfigurationOperationResult>(lookup)).Result);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        Assert.AreEqual(2L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(2L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        await using var audit = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox");
        await using var reader = await audit.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string envelope = Encoding.UTF8.GetString(reader.GetFieldValue<byte[]>(0));
            Assert.IsFalse(envelope.Contains("payloadJson", StringComparison.Ordinal));
            Assert.IsFalse(envelope.Contains("Greeting", StringComparison.Ordinal));
            Assert.IsFalse(envelope.Contains("日本語", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task DistinctKeysWithOneSourcePreconditionRecordOnlyOneEffectAndStructuralRefusalDoesNotPublish()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementConfigurationDefinition descriptor = (await Definitions(actor)).Items.Single(row => row.Kind == "common");
        ManagementConfigurationSource source = await Source(actor, descriptor.SourceId);
        Task<HttpResponseMessage> one = Publish(actor, source, "first", "{\"Greeting\":\"first\"}");
        Task<HttpResponseMessage> two = Publish(fixture.Client("operator-a", 1), source, "second", "{\"Greeting\":\"second\"}");
        using HttpResponseMessage a = await one;
        using HttpResponseMessage b = await two;
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }, new[] { a.StatusCode, b.StatusCode });
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        ManagementConfigurationSource current = await Source(actor, source.SourceId);
        using HttpResponseMessage invalid = await Publish(actor, current, "invalid-shape", "{\"Enabled\":\"not a boolean\"}");
        Assert.AreEqual(HttpStatusCode.Conflict, invalid.StatusCode);
        ManagementConfigurationProblem problem = await Decode<ManagementConfigurationProblem>(invalid);
        Assert.AreEqual("management.configuration.structural_rejection", problem.Result!.Rejection!.Code);
        Assert.AreEqual(current.ETag, (await Source(actor, source.SourceId)).ETag);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        using HttpResponseMessage conflicting = await Publish(actor, source, "first", "{\"Greeting\":\"changed intent\"}");
        Assert.AreEqual(HttpStatusCode.Conflict, conflicting.StatusCode);
    }

    [TestMethod]
    public async Task CatalogReplaySurvivesLoadedManifestChangeAndCohortIsFrozenForTheOriginalRegistration()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        ManagementConfigurationCatalog original = await Catalog(actor);
        ManagementConfigurationOperationResult accepted = await Apply(actor, original, "original-catalog");
        HttpClient workload = fixture.Client("workload-a");
        string registration = Guid.NewGuid().ToString("D");
        await Register(workload, registration, "reported-cohort-does-not-control-targeting");
        Assert.AreEqual("canary", await Cohort(fixture, registration));
        ManagementConfigurationOptions replacement = Deployment("stable");
        var options = new ManagementServiceOptions(fixture.Options.ConnectionString, fixture.Options.CopyCursorSigningKey(),
            fixture.Options.Persistence, fixture.Options.BasePath, fixture.Options.MaximumConcurrentRequests,
            fixture.Options.OperationsSchema, fixture.Options.MessagingSchema, fixture.Options.Delivery, fixture.Options.Backend, replacement);
        await fixture.StopReplicaAsync(1);
        await fixture.StartReplicaAsync(serviceOptions: options);
        HttpClient freshActor = fixture.Client("operator-a", 2);
        using (HttpResponseMessage replay = await Send(freshActor, "configuration/catalog/apply", "original-catalog", original.ETag,
            new ManagementConfigurationCatalogCommand(1, original.LoadedManifestDigest, "Apply trusted deployment")))
        {
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
            Assert.AreEqual(accepted.Result, (await Decode<ManagementConfigurationOperationResult>(replay)).Result);
        }
        ManagementConfigurationCatalog current = await Catalog(freshActor);
        Assert.AreNotEqual(current.LoadedManifestDigest, current.AppliedManifestDigest);
        await Apply(freshActor, current, "new-cohort-mapping");
        HttpClient freshWorkload = fixture.Client("workload-a", 2);
        await Register(freshWorkload, registration, "reported-cohort-does-not-control-targeting");
        Assert.AreEqual("canary", await Cohort(fixture, registration));
        string newer = Guid.NewGuid().ToString("D");
        await Register(freshWorkload, newer, "new-instance");
        Assert.AreEqual("stable", await Cohort(fixture, newer));
        using HttpResponseMessage lookup = await freshActor.GetAsync("configuration/catalog/operations/original-catalog");
        Assert.AreEqual(accepted.Result, (await Decode<ManagementConfigurationOperationResult>(lookup)).Result);
    }

    private static async Task Register(HttpClient client, string id, string instance)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + id)
        {
            Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request(instance) with
            {
                ProcessStartedAtUtc = "2026-11-24T00:00:00.0000000Z"
            }))
        };
        request.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string?> Cohort(ManagementHostFixture fixture, string registration)
    {
        await using var command = fixture.Source.CreateCommand($"SELECT cohort_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_session WHERE registration_id=@id");
        command.Parameters.AddWithValue("id", Guid.Parse(registration));
        return await command.ExecuteScalarAsync() as string;
    }
}
