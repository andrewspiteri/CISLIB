using System.Net;
using System.Reflection;
using System.Text.Json;
using Azure.Core.Pipeline;
using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Examples.CountryData;
using CIS.Heartbeat;
using CIS.Hosting;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Management;
using CIS.Tests.UnitTests.RestClient;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Bootstrap = CIS.Tests.IntegrationTests.Management.ManagementConfigurationBootstrapTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.CountryData;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class CountryCentralConfigurationTests
{
    [TestMethod]
    public async Task MaintainedCountryApplicationStagesRejectsPromotesAndRestartsFromTheCurrentAuthoritativeRollback()
    {
        using var directory = new TemporaryDirectory();
        var requests = new Dictionary<string, int>(StringComparer.Ordinal);
        await using RestHttpLocalServer countries = await RestHttpLocalServer.StartAsync(async context =>
        {
            lock (requests)
            {
                string key = context.Request.Path.Value!;
                requests[key] = requests.GetValueOrDefault(key) + 1;
            }
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":false,\"data\":[{\"name\":\"Malta\",\"Iso2\":\"MT\",\"Iso3\":\"MLT\"}]}", context.RequestAborted);
        });
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 2, start: false,
            configuration: Deployment());
        await fixture.StartReplicaAsync(selectSecurity: Bootstrap.WorkloadServer);
        await fixture.StartReplicaAsync(selectSecurity: Bootstrap.WorkloadServer);
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "country-catalog");
        ManagementPage<ManagementConfigurationDefinition> definitions = await Definitions(actor);
        string common = definitions.Items.Single(item => item.Kind == "common").SourceId;
        string application = definitions.Items.Single(item => item.ApplicationId == fixture.ApplicationA).SourceId;
        string firstCommon = await PublishValue(actor, common, "country-common-one", "{\"RefreshInterval\":\"01:00:00\",\"RequestTimeout\":\"00:00:10\"}");
        string firstApplication = await PublishValue(actor, application, "country-application-one",
            JsonSerializer.Serialize(new
            {
                SourceUrl = countries.Address + "/v1",
                RetryPolicyName = (string?)null
            }));
        ManagementConfigurationAssignmentMap empty = await ReadMap(actor, fixture.ApplicationA);
        string first = await Release(actor, fixture.ApplicationA, empty.ETag, "country-release-one", firstCommon, firstApplication);
        await AssignMap(actor, fixture.ApplicationA, empty.ETag, "country-baseline", new(1, "replace", first, [], "Initial country baseline"));

        string manifest = Inventory(directory);
        string assertion = System.IO.Path.Combine(directory.Path, "country-projected-assertion.txt");
        await File.WriteAllTextAsync(assertion, "owned.fixture.projected.assertion");
        using var sdkHandler = new Bootstrap.NativeTokenEndpoint(fixture.Token(Bootstrap.ObjectId, issuer: Bootstrap.NativeIssuer));
        using var sdkHttp = new HttpClient(sdkHandler);
        using var sdkTransport = new HttpClientTransport(sdkHttp);
        var hosts = new List<(CISApplication App, ManualHeartbeat Heartbeat)>();
        try
        {
            (CISApplication App, ManualHeartbeat Heartbeat) running = await StartCountry();
            CountrySettings initial = running.App.Services.GetRequiredService<CountrySettings>();
            Assert.AreEqual(countries.Address + "/v1", initial.SourceUrl);
            Assert.AreEqual(TimeSpan.FromSeconds(10), initial.RequestTimeout);
            Assert.IsTrue(running.App.Services.GetRequiredService<CountryCatalog>().TryGet("MT", out _));
            ConfigurationRuntime runtime = running.App.Services.GetRequiredService<ConfigurationRuntime>();
            string originalRegistration = running.App.Services.GetRequiredService<ManagementRegistration>().CaptureStatus().RegistrationId;

            string secondCommon = await PublishValue(actor, common, "country-common-two", "{\"RequestTimeout\":\"00:00:20\"}");
            string secondApplication = await PublishValue(actor, application, "country-application-two",
                JsonSerializer.Serialize(new
                {
                    SourceUrl = countries.Address + "/v2",
                    RetryPolicyName = (string?)null
                }));
            ManagementConfigurationAssignmentMap current = await ReadMap(actor, fixture.ApplicationA);
            string second = await Release(actor, fixture.ApplicationA, current.ETag, "country-release-two", secondCommon, secondApplication);
            _ = await runtime.ReconcileAsync();
            Assert.AreEqual(first, Acceptance(running.App).LastAccepted!.Desired.ReleaseId, "Publication and release creation do not activate a candidate.");
            Assert.AreEqual(0L, Acceptance(running.App).Revision);
            await AssignMap(actor, fixture.ApplicationA, current.ETag, "country-canary", new(1, "replace", first,
                [new("canary", second)], "Stage the exact trusted cohort"));
            _ = await runtime.ReconcileAsync();
            Assert.AreEqual(ConfigurationAcceptanceOutcome.PendingRestart, Acceptance(running.App).Outcome);
            Assert.AreSame(initial, running.App.Services.GetRequiredService<CountrySettings>());
            Assert.AreEqual(TimeSpan.FromSeconds(10), initial.RequestTimeout);
            await ReportCurrent(running.App);
            ManagementConfigurationAdoptionEntry staged = await Adoption(actor, fixture.ApplicationA, originalRegistration);
            Assert.AreEqual("pendingRestart", staged.Convergence);
            Assert.AreEqual(first, staged.Reported!.Applied!.ReleaseId);
            Assert.AreEqual(second, staged.Desired!.ReleaseId);

            string invalidApplication = await PublishValue(actor, application, "country-invalid-application", "{\"SourceUrl\":\"file:///invalid-country-source\"}");
            current = await ReadMap(actor, fixture.ApplicationA);
            string invalid = await Release(actor, fixture.ApplicationA, current.ETag, "country-invalid-release", secondCommon, invalidApplication);
            await AssignMap(actor, fixture.ApplicationA, current.ETag, "country-invalid-assignment", new(1, "replace", first,
                [new("canary", invalid)], "A structural string still needs local URI validation"));
            _ = await runtime.ReconcileAsync();
            Assert.AreEqual(ConfigurationAcceptanceOutcome.Rejected, Acceptance(running.App).Outcome);
            Assert.IsTrue(Acceptance(running.App).HasIntactAcceptedState);
            Assert.AreEqual(first, Acceptance(running.App).LastAccepted!.Desired.ReleaseId);
            Assert.AreEqual(countries.Address + "/v1", initial.SourceUrl);
            await ReportCurrent(running.App);
            Assert.AreEqual("rejected", (await Adoption(actor, fixture.ApplicationA, originalRegistration)).Convergence);

            current = await ReadMap(actor, fixture.ApplicationA);
            await AssignMap(actor, fixture.ApplicationA, current.ETag, "country-promote", new(1, "promote", second, [],
                "Promote the valid release and explicitly remove the canary override"));
            string promotedGeneration = (await ReadMap(actor, fixture.ApplicationA)).Generation!;
            await CloseCountry(running);
            (CISApplication App, ManualHeartbeat Heartbeat) restarted = await StartCountry();
            CountrySettings active = restarted.App.Services.GetRequiredService<CountrySettings>();
            Assert.AreNotSame(initial, active, "A new application host captures a fresh root rather than adopting a staged object.");
            Assert.AreEqual(countries.Address + "/v2", active.SourceUrl);
            Assert.AreEqual(TimeSpan.FromSeconds(20), active.RequestTimeout);
            Assert.AreEqual(second, Acceptance(restarted.App).LastAccepted!.Desired.ReleaseId);
            Assert.AreEqual(promotedGeneration, Acceptance(restarted.App).LastAccepted!.Desired.AssignmentGeneration);
            Assert.AreEqual(0L, Acceptance(restarted.App).Revision);
            string nextRegistration = restarted.App.Services.GetRequiredService<ManagementRegistration>().CaptureStatus().RegistrationId;
            Assert.AreNotEqual(originalRegistration, nextRegistration, "This observes a new host session, not distinct physical process identity.");

            current = await ReadMap(actor, fixture.ApplicationA);
            await AssignMap(actor, fixture.ApplicationA, current.ETag, "country-rollback", new(1, "rollback", first, [],
                "Return the whole map to the original explicit references"));
            string rollbackGeneration = (await ReadMap(actor, fixture.ApplicationA)).Generation!;
            Assert.IsTrue(long.Parse(rollbackGeneration, System.Globalization.CultureInfo.InvariantCulture)
                > long.Parse(promotedGeneration, System.Globalization.CultureInfo.InvariantCulture));
            _ = await restarted.App.Services.GetRequiredService<ConfigurationRuntime>().ReconcileAsync();
            Assert.AreEqual(ConfigurationAcceptanceOutcome.PendingRestart, Acceptance(restarted.App).Outcome);
            Assert.AreEqual(countries.Address + "/v2", active.SourceUrl);
            await ReportCurrent(restarted.App);
            Assert.AreEqual("pendingRestart", (await Adoption(actor, fixture.ApplicationA, nextRegistration)).Convergence);
            await CloseCountry(restarted);
            (CISApplication App, ManualHeartbeat Heartbeat) rolledBack = await StartCountry();
            Assert.AreEqual(countries.Address + "/v1", rolledBack.App.Services.GetRequiredService<CountrySettings>().SourceUrl);
            Assert.AreEqual(first, Acceptance(rolledBack.App).LastAccepted!.Desired.ReleaseId);
            Assert.AreEqual(rollbackGeneration, Acceptance(rolledBack.App).LastAccepted!.Desired.AssignmentGeneration);
            Assert.AreEqual(0L, Acceptance(rolledBack.App).Revision);
            lock (requests)
            {
                Assert.AreEqual(2, requests["/v1"]);
                Assert.AreEqual(1, requests["/v2"], "Pending/rejected configuration never reruns the catalog with an unaccepted source.");
            }
        }
        finally
        {
            foreach (var host in hosts.ToArray().Reverse())
            {
                await CloseCountry(host);
            }
        }

        async Task<(CISApplication App, ManualHeartbeat Heartbeat)> StartCountry()
        {
            var heartbeat = new ManualHeartbeat(capacity: 16);
            CISApplication app = CountryApplication.CreateService([], builder =>
            {
                builder.TypeManager = new(typeof(CountryApplication).Assembly)
                {
                    AssemblyManifestPath = manifest
                };
                builder.EnableOptionalIntegrations = false;
                builder.LoggingEnabled = false;
                builder.Host.Logging.ClearProviders();
                builder.WebHost!.UseKestrel().UseUrls("http://127.0.0.1:0");
                builder.Services.Replace(ServiceDescriptor.Singleton<IHeartbeatOwnerFactory>(heartbeat));
                var management = new ManagementClientOptions("country-management") { BaseUrl = fixture.Origins[1] + fixture.Options.BasePath };
                var configuration = new ManagementConfigurationClientOptions("country-configuration") { BaseUrl = management.BaseUrl };
                var central = new CISApplicationCentralConfigurationOptions(management, configuration, new(management.RestClientName)
                {
                    ReportingInterval = TimeSpan.FromMilliseconds(10),
                    ControlCheckInterval = TimeSpan.FromMilliseconds(10),
                    InitialReconnectDelay = TimeSpan.FromMilliseconds(10),
                    MaximumReconnectDelay = TimeSpan.FromMilliseconds(10)
                }, async token => (await Bootstrap.NativePreparation.CreateAsync(fixture, assertion, sdkTransport,
                    management, configuration, token)).Descriptor)
                {
                    ExpectedCohortId = "canary"
                };
                CountryCentralConfiguration.Configure(builder, central, TimeSpan.FromHours(1));
            });
            var result = (App: app, Heartbeat: heartbeat);
            hosts.Add(result);
            await app.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
            return result;
        }

        async Task CloseCountry((CISApplication App, ManualHeartbeat Heartbeat) host)
        {
            try
            {
                await host.App.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally
            {
                await host.Heartbeat.DisposeAsync();
            }
            hosts.Remove(host);
        }
    }

    private static ConfigurationAcceptanceStatus Acceptance(CISApplication app)
        => app.Services.GetRequiredService<ConfigurationRuntime>().CaptureStatus().Entries
            .Single(entry => entry.ConfigurationType == typeof(CountrySettings)).Acceptance!;

    private static async Task ReportCurrent(CISApplication app)
    {
        await Task.Delay(40);
        _ = await app.Services.GetRequiredService<ManagementRegistration>().OnElapsedAsync(CancellationToken.None);
    }

    private static async Task<ManagementConfigurationAdoptionEntry> Adoption(HttpClient actor, string application, string registration)
    {
        using HttpResponseMessage response = await actor.GetAsync("applications/" + application + "/configuration-status");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Decode<ManagementPage<ManagementConfigurationAdoptionEntry>>(response)).Items.Single(item => item.RegistrationId == registration);
    }

    private static string Path(string application, string kind)
        => "applications/" + application + "/configuration/" + CountryCentralConfiguration.ConfigurationId + "/" + kind + "?schemaVersion=1";

    private static async Task<ManagementConfigurationAssignmentMap> ReadMap(HttpClient actor, string application)
    {
        using HttpResponseMessage response = await actor.GetAsync(Path(application, "assignments"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await Decode<ManagementConfigurationAssignmentMap>(response);
    }

    private static async Task<string> Release(HttpClient actor, string application, string etag, string key, string common, string specific)
    {
        using HttpResponseMessage response = await Send(actor, Path(application, "releases"), key, etag,
            new ManagementConfigurationReleaseCommand(1, new("present", common), new("present", specific), "Pin exact CountryData sources"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await Decode<ManagementConfigurationOperationResult>(response)).Result!.ReleaseId!;
    }

    private static async Task AssignMap(HttpClient actor, string application, string etag, string key, ManagementConfigurationAssignmentCommand command)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Path(application, "assignments"));
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(command));
        request.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage response = await actor.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string Inventory(TemporaryDirectory directory)
    {
        Assembly host = typeof(CountryApplication).Assembly;
        string file = System.IO.Path.Combine(directory.Path, "CountryData.dll");
        File.Copy(host.Location, file);
        string manifest = System.IO.Path.Combine(directory.Path, "country.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = host.FullName,
            assemblies = new[] { new { assemblyName = host.FullName, relativePath = "CountryData.dll" } }
        }));
        return manifest;
    }

    private static ManagementConfigurationOptions Deployment()
    {
        var shape = new ManagementConfigurationShape("object", false,
            [new("SourceUrl", true, new("string", false)), new("RefreshInterval", true, new("string", false)),
                new("RequestTimeout", true, new("string", false)), new("RetryPolicyName", true, new("string", true))]);
        var defaults = new CountrySettings();
        var manifest = new ManagementConfigurationManifest(1,
            [new(CountryCentralConfiguration.ConfigurationId, CountryCentralConfiguration.SchemaVersion, shape, JsonSerializer.Serialize(new
            {
                defaults.SourceUrl,
                RefreshInterval = defaults.RefreshInterval.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
                RequestTimeout = defaults.RequestTimeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
                defaults.RetryPolicyName
            }))],
            [new("managed-a", "Development", typeof(CountryApplication).Assembly.GetName().Name!, CountryCentralConfiguration.ConfigurationId, "1")],
            [new(CountryCentralConfiguration.ConfigurationId, "1", "Development")],
            [new(Bootstrap.NativeIssuer, Bootstrap.ObjectId, "managed-a", "Development", "canary")]);
        return new(JsonSerializer.Serialize(manifest, ManagementConfigurationOptions.Json), 32L * 1024 * 1024);
    }
}
