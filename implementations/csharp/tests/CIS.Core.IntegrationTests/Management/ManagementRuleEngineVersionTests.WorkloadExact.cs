using System.Net;
using System.Text.Json;
using CIS.Management;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementRuleEngineVersionTests
{
    [TestMethod]
    public async Task OwnedWorkloadReadsOnlyPreviouslyDeployedExactDefinitionsAcrossPruningAndRestart()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false,
            configuration: RuleDeployment(retainedUnreferencedReleases: 0));
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: false, deployment: true));
        HttpClient actor = fixture.Client("operator-a");
        HttpClient workload = fixture.Client("workload-a");
        await Apply(actor, await Catalog(actor), "workload-exact-catalog");

        string application = fixture.ApplicationA;
        string registration = await Register(workload);
        string first = await SaveCountry(actor, application, "workload-exact-save-first", "MLT");
        string second = await SaveCountry(actor, application, "workload-exact-save-second", "ITA", first);
        string firstDefinition = ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + first), "definitionJson");
        string secondDefinition = ReadText(await Get(actor, RuleRoute(application, "version") + "&version=" + second), "definitionJson");
        string firstRoute = ExactWorkloadRoute(registration, first);
        string secondRoute = ExactWorkloadRoute(registration, second);
        await AssertExactNotFound(workload, firstRoute);

        string empty = (await Assignment(actor, application)).ETag;
        JsonElement firstDeployment = await DeployAccepted(actor, application, "workload-exact-deploy-first", empty, first);
        await AssertExactNotFound(workload, secondRoute);
        await AssertExactNotFound(fixture.Client("workload-b"), firstRoute);
        await AssertExactNotFound(workload, ExactWorkloadRoute(fixture.RegistrationA, first));
        await AssertExactNotFound(workload, ExactWorkloadRoute(registration, first, configuration: OtherRuleConfigurationId));
        await AssertExactNotFound(workload, ExactWorkloadRoute(registration, first, schema: "2"));
        await AssertExactNotFound(workload, ExactWorkloadRoute(registration, Guid.NewGuid().ToString("D")));
        using (HttpResponseMessage user = await actor.GetAsync(firstRoute))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, user.StatusCode, await user.Content.ReadAsStringAsync());
        }

        JsonElement secondDeployment = await DeployAccepted(actor, application, "workload-exact-deploy-second",
            ReadText(firstDeployment, "assignmentETag"), second);
        await AssertDesiredAndSnapshot(actor, workload, application, registration, secondDeployment);
        ManagementConfigurationDesiredContext active = await ReadWorkloadDesired(workload, registration);
        await AssertExactWorkloadVersion(workload, firstRoute, active, first, firstDefinition);
        await AssertExactWorkloadVersion(workload, secondRoute, active, second, secondDefinition);
        Assert.AreEqual(active, await ReadWorkloadDesired(workload, registration));

        await fixture.StopReplicasAsync();
        await PruneSupersededCarrier(fixture);
        Assert.AreEqual(2L, await Count(fixture, fixture.Options.Persistence.Schema, "rule_engine_release_history"));
        int restarted = fixture.Origins.Count;
        await fixture.StartReplicaAsync(selectSecurity: selected => WithRuleGrants(selected, fixture.ApplicationA, secondEditor: false, deployment: true));
        workload = fixture.Client("workload-a", restarted);
        await AssertExactWorkloadVersion(workload, firstRoute, active, first, firstDefinition);
        Assert.AreEqual(active, await ReadWorkloadDesired(workload, registration));

        using (HttpResponseMessage close = await workload.DeleteAsync("registrations/" + registration))
        {
            Assert.AreEqual(HttpStatusCode.OK, close.StatusCode, await close.Content.ReadAsStringAsync());
        }
        using HttpResponseMessage closed = await workload.GetAsync(firstRoute);
        Assert.AreEqual(HttpStatusCode.Conflict, closed.StatusCode, await closed.Content.ReadAsStringAsync());
        Assert.AreEqual("management.registration.closed", (await Decode<ManagementConfigurationProblem>(closed)).Code);
    }

    private static string ExactWorkloadRoute(string registration, string version, string configuration = RuleConfigurationId, string schema = "1")
        => $"registrations/{registration}/configuration/{configuration}/rule-versions/{version}?schemaVersion={schema}";

    private static async Task AssertExactNotFound(HttpClient workload, string route)
    {
        using HttpResponseMessage response = await workload.GetAsync(route);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("management.configuration.not_found", (await Decode<ManagementConfigurationProblem>(response)).Code);
    }

    private static async Task<ManagementConfigurationDesiredContext> ReadWorkloadDesired(HttpClient workload, string registration)
    {
        using HttpResponseMessage response = await workload.GetAsync(RuleSnapshotRoute(registration));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Decode<ManagementConfigurationSnapshot>(response)).Desired;
    }

    private static async Task AssertExactWorkloadVersion(HttpClient workload, string route, ManagementConfigurationDesiredContext expected,
        string version, string definition)
    {
        JsonElement result = await Get(workload, route);
        JsonElement target = result.GetProperty("target");
        Assert.AreEqual(expected.AuthorityId, ReadText(target, "authorityId"));
        Assert.AreEqual(expected.ApplicationId, ReadText(target, "applicationId"));
        Assert.AreEqual(expected.Environment, ReadText(target, "environment"));
        Assert.AreEqual(expected.RegistrationId, ReadText(target, "registrationId"));
        Assert.AreEqual(expected.ConfigurationId, ReadText(target, "configurationId"));
        Assert.AreEqual(expected.SchemaVersion, ReadText(target, "schemaVersion"));
        Assert.AreEqual(expected.HostApplicationName, ReadText(target, "hostApplicationName"));
        string? cohort = target.TryGetProperty("cohortId", out JsonElement reportedCohort) ? reportedCohort.GetString() : null;
        Assert.AreEqual(expected.CohortId, cohort);
        Assert.AreEqual(RulesetId, ReadText(result, "rulesetId"));
        Assert.AreEqual(version, ReadText(result, "version"));
        Assert.AreEqual(definition, ReadText(result, "definitionJson"));
    }
}
