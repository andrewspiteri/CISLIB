using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Wire;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationDistributionTests
{
    [TestMethod]
    public async Task ImmutableReleaseCreationUsesMapPreconditionWithoutActivationAndReplaysAfterAssignment()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        ManagementConfigurationDefinition common = definitions.Items.Single(row => row.Kind == "common");
        ManagementConfigurationDefinition application = definitions.Items.Single(row => row.ApplicationId == fixture.ApplicationA);
        string commonPublication = await PublishValue(actor, common.SourceId, "common", "{\"Greeting\":\"common\"}");
        string applicationPublication = await PublishValue(actor, application.SourceId, "application", "{\"Enabled\":false}");
        ManagementConfigurationAssignmentMap empty = await Map(actor, fixture.ApplicationA);
        var command = new ManagementConfigurationReleaseCommand(1, new("present", commonPublication), new("present", applicationPublication), "Pin exact pair");
        ManagementConfigurationOperationResult created = await CreateRelease(actor, fixture.ApplicationA, empty.ETag, "release-original", command);
        ManagementConfigurationAssignmentMap stillEmpty = await Map(actor, fixture.ApplicationA);
        Assert.AreEqual(empty.ETag, stillEmpty.ETag);
        Assert.IsNull(stillEmpty.Generation);
        Assert.IsNull(stillEmpty.DefaultReleaseId);
        string releaseId = created.Result!.ReleaseId!;
        using HttpResponseMessage read = await actor.GetAsync("configuration/releases/" + releaseId);
        ManagementConfigurationRelease release = await Decode<ManagementConfigurationRelease>(read);
        Assert.AreEqual(commonPublication, release.CommonSource.PublicationId);
        Assert.AreEqual(applicationPublication, release.ApplicationSource.PublicationId);
        await PublishValue(actor, common.SourceId, "new-common", "{\"Greeting\":\"new common\"}");
        ManagementConfigurationOperationResult assigned = await Assign(actor, fixture.ApplicationA, empty.ETag, "assign-original",
            new(1, "replace", releaseId, [], "Select pinned pair"));
        Assert.AreEqual("1", assigned.Result!.Generation);
        ManagementConfigurationOperationResult replay = await CreateRelease(actor, fixture.ApplicationA, empty.ETag, "release-original", command);
        Assert.AreEqual("replayed", replay.ResultSource);
        Assert.AreEqual(created.Result, replay.Result);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        using HttpResponseMessage recovery = await actor.GetAsync(BindingPath(fixture.ApplicationA, "release-operations/release-original"));
        Assert.AreEqual(created.Result, (await Decode<ManagementConfigurationOperationResult>(recovery)).Result);
        using HttpResponseMessage denied = await Send(fixture.Client("operator-b"), BindingPath(fixture.ApplicationA, "releases"),
            "not-authorized", empty.ETag, command);
        Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [TestMethod]
    public async Task CompletePromotionRollbackAndReplicaRaceAdvanceOneWholeMapAndRetainOriginalDecisions()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementConfigurationAssignmentMap empty = await Map(actor, fixture.ApplicationA);
        string first = (await CreateRelease(actor, fixture.ApplicationA, empty.ETag, "first-release",
            new(1, new("absent"), new("absent"), "Defaults"))).Result!.ReleaseId!;
        string commonSource = (await Definitions(actor)).Items.Single(row => row.Kind == "common").SourceId;
        string publication = await PublishValue(actor, commonSource, "candidate-source", "{\"Greeting\":\"candidate\"}");
        string second = (await CreateRelease(actor, fixture.ApplicationA, empty.ETag, "second-release",
            new(1, new("present", publication), new("absent"), "Candidate"))).Result!.ReleaseId!;
        ManagementConfigurationOperationResult staged = await Assign(actor, fixture.ApplicationA, empty.ETag, "stage",
            new(1, "replace", first, [new("canary", second)], "Stage canary"));
        var promote = new ManagementConfigurationAssignmentCommand(1, "promote", second, [new("canary", second)], "Promote and retain explicit pin");
        ManagementConfigurationOperationResult promoted = await Assign(actor, fixture.ApplicationA, staged.Result!.ETag!, "promote", promote);
        Assert.AreEqual("2", promoted.Result!.Generation);
        Assert.HasCount(1, promoted.Result.Assignment!.Overrides);
        ManagementConfigurationOperationResult rollback = await Assign(actor, fixture.ApplicationA, promoted.Result.ETag!, "rollback",
            new(1, "rollback", first, [], "Rollback and remove canary pin"));
        Assert.AreEqual("3", rollback.Result!.Generation);
        Assert.IsEmpty(rollback.Result.Assignment!.Overrides);
        ManagementConfigurationOperationResult original = await Assign(actor, fixture.ApplicationA, staged.Result.ETag!, "promote", promote);
        Assert.AreEqual("replayed", original.ResultSource);
        Assert.AreEqual("2", original.Result!.Generation);
        Assert.HasCount(1, original.Result.Assignment!.Overrides);
        ManagementConfigurationOperationResult identical = await Assign(actor, fixture.ApplicationA, rollback.Result.ETag!, "identical-new-decision",
            new(1, "replace", first, [], "Same values, new controlled decision"));
        Assert.AreEqual("4", identical.Result!.Generation);
        Task<HttpResponseMessage> one = SendAssignment(actor, fixture.ApplicationA, identical.Result.ETag!, "race-a", new(1, "replace", first, [], "First writer"));
        Task<HttpResponseMessage> two = SendAssignment(fixture.Client("operator-a", 1), fixture.ApplicationA, identical.Result.ETag!, "race-b",
            new(1, "replace", second, [], "Second writer"));
        using HttpResponseMessage a = await one;
        using HttpResponseMessage b = await two;
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }, new[] { a.StatusCode, b.StatusCode });
        Assert.AreEqual("5", (await Map(actor, fixture.ApplicationA)).Generation);
        using HttpResponseMessage wrongIntent = await SendAssignment(actor, fixture.ApplicationA, staged.Result.ETag!, "promote",
            promote with
            {
                Reason = "Changed original intent"
            });
        Assert.AreEqual(HttpStatusCode.Conflict, wrongIntent.StatusCode);
    }

    [TestMethod]
    public async Task ForeignSourceOrReleaseCannotCrossAnActualBindingAndOversizedResolvedPairsNeverActivate()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        string foreignSource = definitions.Items.Single(row => row.ApplicationId == fixture.ApplicationB).SourceId;
        string foreignPublication = await PublishValue(actor, foreignSource, "foreign-source", "{\"Greeting\":\"foreign\"}");
        ManagementConfigurationAssignmentMap empty = await Map(actor, fixture.ApplicationA);
        using HttpResponseMessage wrong = await Send(actor, BindingPath(fixture.ApplicationA, "releases"), "wrong-application", empty.ETag,
            new ManagementConfigurationReleaseCommand(1, new("absent"), new("present", foreignPublication), "Cannot import another application"));
        Assert.AreEqual(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.AreEqual("management.configuration.reference_scope", (await Decode<ManagementConfigurationProblem>(wrong)).Code);
        string text = new('<', 45_000);
        string payload = "{\"Greeting\":\"" + text + "\",\"Ignored\":\"" + text + "\"}";
        string common = await PublishValue(actor, definitions.Items.Single(row => row.Kind == "common").SourceId, "large-common", payload);
        string application = await PublishValue(actor, definitions.Items.Single(row => row.ApplicationId == fixture.ApplicationA).SourceId, "large-application", payload);
        using HttpResponseMessage tooLarge = await Send(actor, BindingPath(fixture.ApplicationA, "releases"), "encoded-pair", empty.ETag,
            new ManagementConfigurationReleaseCommand(1, new("present", common), new("present", application), "Both sources fit individually"));
        Assert.AreEqual(HttpStatusCode.Conflict, tooLarge.StatusCode);
        Assert.AreEqual("management.configuration.response_too_large", (await Decode<ManagementConfigurationProblem>(tooLarge)).Code);
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        Assert.IsNull((await Map(actor, fixture.ApplicationA)).Generation);
        string foreignRelease = (await CreateRelease(actor, fixture.ApplicationB, (await Map(actor, fixture.ApplicationB)).ETag, "foreign-release",
            new(1, new("absent"), new("present", foreignPublication), "Foreign application"))).Result!.ReleaseId!;
        using HttpResponseMessage wrongAssignment = await SendAssignment(actor, fixture.ApplicationA, empty.ETag, "wrong-release",
            new(1, "replace", foreignRelease, [], "Cannot assign another binding"));
        Assert.AreEqual(HttpStatusCode.Conflict, wrongAssignment.StatusCode);
        Assert.IsNull((await Map(actor, fixture.ApplicationA)).Generation);
    }

    [TestMethod]
    public async Task PreviewRetainsExactScopeAndPreconditionRedactsValuesAndBindsRecipientContinuation()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        string source = (await Definitions(actor)).Items.Single(row => row.Kind == "common").SourceId;
        string publication = await PublishValue(actor, source, "source", "{\"Greeting\":\"private preview value\"}");
        ManagementConfigurationAssignmentMap empty = await Map(actor, fixture.ApplicationA);
        string release = (await CreateRelease(actor, fixture.ApplicationA, empty.ETag, "release",
            new(1, new("present", publication), new("absent"), "Preview candidate"))).Result!.ReleaseId!;
        foreach (string id in new[] { Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D") })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + id)
            {
                Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request("reported-same-process-label")))
            };
            request.Content.Headers.ContentType = new("application/json");
            using HttpResponseMessage registered = await fixture.Client("workload-a").SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, registered.StatusCode);
        }
        var pair = new ManagementConfigurationPreviewCommand(1, fixture.ApplicationA, "shared.settings", "1", new("present", publication), new("absent"));
        using HttpResponseMessage first = await Preview(actor, pair, "?pageSize=1");
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        ManagementConfigurationPreview preview = await Decode<ManagementConfigurationPreview>(first);
        Assert.AreEqual(empty.ETag, preview.CurrentAssignment.ETag);
        Assert.IsFalse(preview.Redacted);
        Assert.IsTrue(preview.Changes.Any(change => change.Path == "/Greeting"));
        Assert.HasCount(1, preview.Recipients.Items);
        Assert.AreEqual("unknown", preview.Recipients.Items[0].Compatibility);
        Assert.IsNotNull(preview.Recipients.NextCursor);
        string continuation = "?pageSize=1&cursor=" + Uri.EscapeDataString(preview.Recipients.NextCursor);
        using HttpResponseMessage next = await Preview(actor, pair, continuation);
        Assert.AreEqual(HttpStatusCode.OK, next.StatusCode);
        Assert.AreNotEqual(preview.Recipients.Items[0].RegistrationId, (await Decode<ManagementConfigurationPreview>(next)).Recipients.Items[0].RegistrationId);
        using HttpResponseMessage foreignCursor = await Preview(fixture.Client("operator-b"), pair, continuation);
        Assert.AreEqual(HttpStatusCode.BadRequest, foreignCursor.StatusCode);
        using HttpResponseMessage redacted = await Preview(fixture.Client("operator-b"), pair);
        ManagementConfigurationPreview hidden = await Decode<ManagementConfigurationPreview>(redacted);
        Assert.IsTrue(hidden.Redacted);
        Assert.IsEmpty(hidden.Changes);
        Assert.IsFalse((await redacted.Content.ReadAsStringAsync()).Contains("private preview value", StringComparison.Ordinal));
        using HttpResponseMessage changedIntent = await Preview(actor, pair with
        {
            CommonSource = new("absent")
        }, continuation);
        Assert.AreEqual(HttpStatusCode.BadRequest, changedIntent.StatusCode);
        await Assign(actor, fixture.ApplicationA, empty.ETag, "assign", new(1, "replace", release, [], "Change map after preview"));
        using HttpResponseMessage staleCursor = await Preview(actor, pair, continuation);
        Assert.AreEqual(HttpStatusCode.BadRequest, staleCursor.StatusCode);
        Assert.AreEqual(1L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        Assert.AreEqual("1", (await Map(actor, fixture.ApplicationA)).Generation);
    }

    [TestMethod]
    public async Task GenerationExhaustionRefusesWithoutWrappingOrReplacingTheCurrentMap()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "catalog");
        ManagementConfigurationAssignmentMap empty = await Map(actor, fixture.ApplicationA);
        string release = (await CreateRelease(actor, fixture.ApplicationA, empty.ETag, "release", new(1, new("absent"), new("absent"), "Defaults"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, empty.ETag, "initial", new(1, "replace", release, [], "Initial assignment"));
        await using (var seed = fixture.Source.CreateCommand($"UPDATE \"{fixture.Options.Persistence.Schema}\".configuration_assignment SET generation=@generation WHERE binding_id=@binding"))
        {
            seed.Parameters.AddWithValue("generation", long.MaxValue);
            seed.Parameters.AddWithValue("binding", Guid.Parse(empty.BindingId));
            await seed.ExecuteNonQueryAsync();
        }
        ManagementConfigurationAssignmentMap before = await Map(actor, fixture.ApplicationA);
        using HttpResponseMessage rejected = await SendAssignment(actor, fixture.ApplicationA, before.ETag, "cannot-wrap",
            new(1, "rollback", release, [], "Exhausted generation"));
        Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.AreEqual("management.configuration.generation_exhausted", (await Decode<ManagementConfigurationProblem>(rejected)).Code);
        Assert.AreEqual(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), (await Map(actor, fixture.ApplicationA)).Generation);
        Assert.AreEqual(before.ETag, (await Map(actor, fixture.ApplicationA)).ETag);
    }

    internal static string BindingPath(string application, string suffix) => "applications/" + application + "/configuration/shared.settings/" + suffix + "?schemaVersion=1";
    internal static async Task<ManagementConfigurationAssignmentMap> Map(HttpClient actor, string application)
    {
        using HttpResponseMessage response = await actor.GetAsync(BindingPath(application, "assignments"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationAssignmentMap>(response);
    }
    internal static async Task<string> PublishValue(HttpClient actor, string source, string key, string payload)
    {
        using HttpResponseMessage response = await Publish(actor, await Source(actor, source), key, payload);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Decode<ManagementConfigurationOperationResult>(response)).Result!.PublicationId!;
    }
    internal static async Task<ManagementConfigurationOperationResult> CreateRelease(HttpClient actor, string application, string etag, string key,
        ManagementConfigurationReleaseCommand command)
    {
        using HttpResponseMessage response = await Send(actor, BindingPath(application, "releases"), key, etag, command);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationOperationResult>(response);
    }
    internal static async Task<ManagementConfigurationOperationResult> Assign(HttpClient actor, string application, string etag, string key,
        ManagementConfigurationAssignmentCommand command)
    {
        using HttpResponseMessage response = await SendAssignment(actor, application, etag, key, command);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationOperationResult>(response);
    }
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
    private static async Task<HttpResponseMessage> Preview(HttpClient actor, ManagementConfigurationPreviewCommand command, string query = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "configuration/preview" + query)
        {
            Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(command))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await actor.SendAsync(request);
    }
}
