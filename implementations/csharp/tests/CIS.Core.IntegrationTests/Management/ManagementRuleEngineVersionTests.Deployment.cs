using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementRuleEngineVersionTests
{
    private const string DeploymentOperation = "management.rule-engine.deployment.request";
    private const string DeploymentReadCapability = "management.rule-engine.deployment.read";
    private const string DeployCapability = "management.rule-engine.deploy";

    [TestMethod]
    public async Task SavedOnlyVersionsStayUnassignedAndCompetingDeployersCommitOneNativeAssignment()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(start: false, configuration: RuleDeployment());
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: true, deployment: true));
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: true, deployment: true));
        HttpClient first = fixture.Client("operator-a");
        HttpClient second = fixture.Client("operator-b", 1);
        HttpClient workload = fixture.Client("workload-a", 1);
        await Apply(first, await Catalog(first), "deploy-apply-catalog");

        string application = fixture.ApplicationA;
        string registration = await Register(workload);
        ManagementConfigurationAssignmentMap empty = await Assignment(first, application);
        string unassignedEarlier = await SaveCountry(first, application, "deploy-save-earlier", "ESP");
        string baseline = await SaveCountry(first, application, "deploy-save-baseline", "MLT", unassignedEarlier);
        string firstVersion = await SaveCountry(first, application, "deploy-save-first", "ITA", baseline);
        string secondVersion = await SaveCountry(first, application, "deploy-save-second", "FRA", firstVersion);
        JsonElement savedOnly = await Get(first, DeploymentRoute(application, "deployment"));
        Assert.AreEqual(empty.ETag, ReadText(savedOnly, "assignmentETag"));
        Assert.IsFalse(savedOnly.TryGetProperty("desired", out _));
        Assert.AreEqual(0L, await Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        using (HttpResponseMessage absent = await workload.GetAsync(RuleSnapshotRoute(registration)))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, absent.StatusCode);
        }
        using (HttpResponseMessage forced = await workload.GetAsync(RuleSnapshotRoute(registration) + "&version=" + baseline))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, forced.StatusCode);
        }

        await AssertGenericReleaseAndAssignmentAreGuarded(first, application, empty.ETag, baseline);
        using (HttpResponseMessage neverDeployed = await RequestDeployment(first, application, "deploy-never-selected", empty.ETag, firstVersion, "rollback"))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, neverDeployed.StatusCode, await neverDeployed.Content.ReadAsStringAsync());
        }
        Assert.AreEqual(empty.ETag, (await Assignment(first, application)).ETag);
        JsonElement initial = await DeployAccepted(first, application, "deploy-baseline", empty.ETag, baseline);
        await AssertDesiredAndSnapshot(first, workload, application, registration, initial);

        JsonElement other = await Get(first, DeploymentRoute(application, "deployment", OtherRulesetId));
        using (HttpResponseMessage wrongRuleset = await RequestDeployment(first, application, "deploy-wrong-ruleset",
            ReadText(other, "assignmentETag"), baseline, "rollback", OtherRulesetId))
        {
            Assert.IsTrue(wrongRuleset.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound,
                await wrongRuleset.Content.ReadAsStringAsync());
        }
        JsonElement otherAfter = await Get(first, DeploymentRoute(application, "deployment", OtherRulesetId));
        Assert.AreEqual(ReadText(other, "assignmentETag"), ReadText(otherAfter, "assignmentETag"));
        Assert.IsFalse(otherAfter.TryGetProperty("desired", out _));

        string current = ReadText(initial, "assignmentETag");
        string schema = fixture.Options.Persistence.Schema;
        long originals = await Count(fixture, fixture.Options.OperationsSchema, "operation_journal");
        long audits = await Count(fixture, fixture.Options.MessagingSchema, "outbox");
        long publications = await Count(fixture, schema, "configuration_publication");
        long releases = await Count(fixture, schema, "configuration_release");
        Task<HttpResponseMessage> pendingFirst = RequestDeployment(first, application, "deploy-race-first", current, firstVersion);
        Task<HttpResponseMessage> pendingSecond = RequestDeployment(second, application, "deploy-race-second", current, secondVersion);
        using HttpResponseMessage firstResponse = await pendingFirst;
        using HttpResponseMessage secondResponse = await pendingSecond;
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed },
            new[] { firstResponse.StatusCode, secondResponse.StatusCode });

        foreach ((HttpClient actor, string key, string version, HttpResponseMessage response) in new[]
        {
            (first, "deploy-race-first", firstVersion, firstResponse), (second, "deploy-race-second", secondVersion, secondResponse)
        })
        {
            string outcome = response.IsSuccessStatusCode ? "result" : "rejection";
            JsonElement original = await ReadDeploymentEnvelope(response);
            AssertDeploymentOriginal(original, key, "prepared");
            JsonElement recovered = await Get(actor, DeploymentRoute(application, "deployment-operations/" + key));
            AssertDeploymentOriginal(recovered, key, "found");
            Assert.AreEqual(original.GetProperty(outcome).GetRawText(), recovered.GetProperty(outcome).GetRawText());
            using HttpResponseMessage replay = await RequestDeployment(actor, application, key, current, version);
            Assert.AreEqual(response.StatusCode, replay.StatusCode);
            JsonElement replayed = await ReadDeploymentEnvelope(replay);
            AssertDeploymentOriginal(replayed, key, "replayed");
            Assert.AreEqual(original.GetProperty(outcome).GetRawText(), replayed.GetProperty(outcome).GetRawText());
        }

        Assert.AreEqual(originals + 2, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
        Assert.AreEqual(audits + 2, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
        Assert.AreEqual(publications + 1, await Count(fixture, schema, "configuration_publication"));
        Assert.AreEqual(releases + 1, await Count(fixture, schema, "configuration_release"));
        HttpResponseMessage winner = firstResponse.IsSuccessStatusCode ? firstResponse : secondResponse;
        JsonElement decision = (await ReadDeploymentEnvelope(winner)).GetProperty("result");
        string winningVersion = firstResponse.IsSuccessStatusCode ? firstVersion : secondVersion;
        Assert.AreEqual(winningVersion, ReadText(decision.GetProperty("desired"), "version"));
        Assert.AreEqual(Generation(initial) + 1, Generation(decision));
        await AssertDesiredAndSnapshot(first, workload, application, registration, decision);

        JsonElement rollback = await DeployAccepted(first, application, "deploy-rollback-earlier", ReadText(decision, "assignmentETag"),
            unassignedEarlier, "rollback");
        Assert.AreEqual(unassignedEarlier, ReadText(rollback.GetProperty("desired"), "version"));
        Assert.AreEqual(Generation(decision) + 1, Generation(rollback));
        await AssertDesiredAndSnapshot(first, workload, application, registration, rollback);
    }

    [TestMethod]
    public async Task PrunedCarrierKeepsOriginalDecisionAndCanBeRematerializedByFreshRollback()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false,
            configuration: RuleDeployment(retainedUnreferencedReleases: 0));
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: false, deployment: true));
        HttpClient actor = fixture.Client("operator-a");
        HttpClient workload = fixture.Client("workload-a");
        string application = fixture.ApplicationA;
        await Apply(actor, await Catalog(actor), "retain-deploy-catalog");
        string registration = await Register(workload);
        string firstVersion = await SaveCountry(actor, application, "retain-deploy-save-first", "MLT");
        string secondVersion = await SaveCountry(actor, application, "retain-deploy-save-second", "ITA", firstVersion);
        string empty = (await Assignment(actor, application)).ETag;
        using (HttpResponseMessage denied = await RequestDeployment(fixture.Client("operator-b"), application,
            "read-only-deploy", empty, firstVersion))
        {
            Assert.IsTrue(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        }
        JsonElement original = await DeployAccepted(actor, application, "retain-deploy-original", empty, firstVersion);
        JsonElement replacement = await DeployAccepted(actor, application, "retain-deploy-replacement", ReadText(original, "assignmentETag"), secondVersion);
        await AssertDesiredAndSnapshot(actor, workload, application, registration, replacement);
        string originalRelease = ReadText(original.GetProperty("desired"), "releaseId");
        string schema = fixture.Options.Persistence.Schema;

        await fixture.StopReplicasAsync();
        await PruneSupersededCarrier(fixture);
        Assert.AreEqual(1L, await Count(fixture, schema, "configuration_release"));
        Assert.AreEqual(1L, await Count(fixture, schema, "configuration_publication"));
        int restarted = fixture.Origins.Count;
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: false, deployment: true));
        actor = fixture.Client("operator-a", restarted);
        workload = fixture.Client("workload-a", restarted);

        long used = (await Catalog(actor)).UsedBytes;
        string operationRoute = DeploymentRoute(application, "deployment-operations/retain-deploy-original");
        JsonElement recovered = await Get(actor, operationRoute);
        AssertDeploymentOriginal(recovered, "retain-deploy-original", "found");
        Assert.AreEqual(original.GetRawText(), recovered.GetProperty("result").GetRawText());
        using (HttpResponseMessage replay = await RequestDeployment(actor, application, "retain-deploy-original", empty, firstVersion))
        {
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode, await replay.Content.ReadAsStringAsync());
            JsonElement replayed = await ReadDeploymentEnvelope(replay);
            AssertDeploymentOriginal(replayed, "retain-deploy-original", "replayed");
            Assert.AreEqual(original.GetRawText(), replayed.GetProperty("result").GetRawText());
        }
        Assert.AreEqual(used, (await Catalog(actor)).UsedBytes);
        Assert.AreEqual(1L, await Count(fixture, schema, "configuration_release"));
        Assert.AreEqual(1L, await Count(fixture, schema, "configuration_publication"));
        await AssertDesiredAndSnapshot(actor, workload, application, registration, replacement);

        JsonElement rolledBack = await DeployAccepted(actor, application, "retain-deploy-rollback", ReadText(replacement, "assignmentETag"),
            firstVersion, "rollback");
        JsonElement selected = rolledBack.GetProperty("desired");
        Assert.AreEqual(firstVersion, ReadText(selected, "version"));
        Assert.AreNotEqual(originalRelease, ReadText(selected, "releaseId"));
        Assert.AreEqual(Generation(replacement) + 1, Generation(rolledBack));
        Assert.AreEqual(2L, await Count(fixture, schema, "configuration_release"));
        Assert.AreEqual(2L, await Count(fixture, schema, "configuration_publication"));
        await AssertDesiredAndSnapshot(actor, workload, application, registration, rolledBack);
    }

    private static async Task<string> SaveCountry(HttpClient actor, string application, string key, string country, string? basis = null)
    {
        string edit = ReadText(await OnlyRuleset(actor, application), "editETag");
        JsonElement saved = await SaveAccepted(actor, application, key, edit, DefinitionJson([Row("stable", 10, country)]), basis);
        return ReadText(saved, "version");
    }

    private static async Task<JsonElement> DeployAccepted(HttpClient actor, string application, string key, string etag,
        string version, string intent = "deploy")
    {
        using HttpResponseMessage response = await RequestDeployment(actor, application, key, etag, version, intent);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        JsonElement envelope = await ReadDeploymentEnvelope(response);
        AssertDeploymentOriginal(envelope, key, "prepared");
        JsonElement decision = envelope.GetProperty("result");
        Assert.AreEqual(intent, ReadText(decision, "intent"));
        Assert.AreEqual(version, ReadText(decision.GetProperty("desired"), "version"));
        return decision;
    }

    private static Task<HttpResponseMessage> RequestDeployment(HttpClient actor, string application, string key, string etag,
        string version, string intent = "deploy", string ruleset = RulesetId)
        => NativeWrite(actor, HttpMethod.Post, DeploymentRoute(application, "deployments", ruleset), key, etag,
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                version,
                intent,
                reason = "Reviewed rule deployment"
            }));

    private static string DeploymentRoute(string application, string route, string ruleset = RulesetId)
        => $"applications/{application}/rule-engine/{route}?rulesetId={Uri.EscapeDataString(ruleset)}";

    private static async Task<JsonElement> ReadDeploymentEnvelope(HttpResponseMessage response)
    {
        JsonElement body = await Read(response);
        return response.IsSuccessStatusCode ? body : body.GetProperty("deploymentResult");
    }

    private static void AssertDeploymentOriginal(JsonElement envelope, string key, string source)
    {
        JsonElement operation = envelope.GetProperty("operation");
        Assert.AreEqual(DeploymentOperation, ReadText(operation, "name"));
        Assert.AreEqual(key, ReadText(operation, "key"));
        Assert.AreEqual(source, ReadText(envelope, "resultSource"));
        Assert.AreEqual("committed", ReadText(envelope, "originalCompletion"));
    }

    private static long Generation(JsonElement decision)
        => long.Parse(ReadText(decision.GetProperty("desired"), "generation"), CultureInfo.InvariantCulture);

    private static string RuleSnapshotRoute(string registration)
        => $"registrations/{registration}/configuration/{RuleConfigurationId}?schemaVersion=1";

    private static async Task AssertDesiredAndSnapshot(HttpClient actor, HttpClient workload, string application, string registration,
        JsonElement decision)
    {
        JsonElement desired = decision.GetProperty("desired");
        string version = ReadText(desired, "version");
        string generation = ReadText(desired, "generation");
        string release = ReadText(desired, "releaseId");
        JsonElement read = await Get(actor, DeploymentRoute(application, "deployment"));
        Assert.AreEqual(application, ReadText(read, "applicationId"));
        Assert.AreEqual("Development", ReadText(read, "environment"));
        Assert.AreEqual(RulesetId, ReadText(read, "rulesetId"));
        Assert.AreEqual(ReadText(decision, "assignmentETag"), ReadText(read, "assignmentETag"));
        Assert.AreEqual(ReadText(decision, "requestedAtUtc"), ReadText(read, "requestedAtUtc"));
        Assert.AreEqual(desired.GetRawText(), read.GetProperty("desired").GetRawText());
        Assert.AreEqual(SchemaRevision, ReadText(desired, "schemaRevision"));

        ManagementConfigurationAssignmentMap map = await Assignment(actor, application);
        Assert.AreEqual(ReadText(decision, "assignmentETag"), map.ETag);
        Assert.AreEqual(release, map.DefaultReleaseId);
        Assert.AreEqual(generation, map.Generation);

        using HttpResponseMessage response = await workload.GetAsync(RuleSnapshotRoute(registration));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        ManagementConfigurationSnapshot snapshot = await Decode<ManagementConfigurationSnapshot>(response);
        Assert.AreEqual(registration, snapshot.Desired.RegistrationId);
        Assert.AreEqual(release, snapshot.Desired.ReleaseId);
        Assert.AreEqual(generation, snapshot.Desired.Generation);
        Assert.AreEqual("absent", snapshot.CommonSource.State);
        Assert.AreEqual("present", snapshot.ApplicationSource.State);
        var carrier = ManagementRuleDefinitionCarrier.Decode(snapshot.ApplicationSource.PayloadJson!);
        string carried = Encoding.UTF8.GetString(carrier.GetDefinitionBytes());
        string saved = ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + version), "definitionJson");
        Assert.AreEqual(saved, carried);
        using HttpResponseMessage generic = await actor.GetAsync("configuration/publications/" + snapshot.ApplicationSource.PublicationId);
        Assert.AreEqual(HttpStatusCode.NotFound, generic.StatusCode);
    }

    private static async Task AssertGenericReleaseAndAssignmentAreGuarded(HttpClient actor, string application, string etag, string version)
    {
        string binding = $"applications/{application}/configuration/{RuleConfigurationId}";
        using HttpResponseMessage release = await NativeWrite(actor, HttpMethod.Post, binding + "/releases?schemaVersion=1",
            "generic-rule-release", etag, ManagementConfigurationWire.EncodeRequest(
                new ManagementConfigurationReleaseCommand(1, new("absent"), new("absent"), "Attempt to form a generic rule release")));
        Assert.AreEqual(HttpStatusCode.Conflict, release.StatusCode, await release.Content.ReadAsStringAsync());
        Assert.AreEqual("management.configuration.rule_engine_managed", (await Decode<ManagementConfigurationProblem>(release)).Code);
        using HttpResponseMessage assign = await NativeWrite(actor, HttpMethod.Put, binding + "/assignments?schemaVersion=1",
            "generic-rule-assignment", etag, ManagementConfigurationWire.EncodeRequest(
                new ManagementConfigurationAssignmentCommand(1, "replace", version, [], "Attempt to assign a saved-only rule version")));
        Assert.AreEqual(HttpStatusCode.Conflict, assign.StatusCode, await assign.Content.ReadAsStringAsync());
        Assert.AreEqual("management.configuration.rule_engine_managed", (await Decode<ManagementConfigurationProblem>(assign)).Code);
    }

    private static async Task<HttpResponseMessage> NativeWrite(HttpClient client, HttpMethod method, string path, string key, string etag, byte[] body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = new ByteArrayContent(body) };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content.Headers.ContentType = new("application/json");
        return await client.SendAsync(request);
    }

    private static async Task PruneSupersededCarrier(ManagementHostFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("management", _ => fixture.Source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        var store = new PostgreSqlManagementStore(factory, fixture.Options.Persistence, new PostgreSqlManagementAuthority(factory, fixture.Options));
        ManagementStoreResult<ManagementCleanupResult> cleanup = await store.CleanupAsync(100);
        Assert.IsTrue(cleanup.IsKnownCommitted, cleanup.Code);
        Assert.AreEqual(1, cleanup.Value!.RemovedConfigurationReleases);
        Assert.AreEqual(1, cleanup.Value.RemovedConfigurationPublications);
        Assert.IsGreaterThan(0L, cleanup.Value.ReleasedConfigurationBytes);
    }
}
