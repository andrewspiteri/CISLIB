using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.Core.Pipeline;
using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Heartbeat;
using CIS.Hosting;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Security;
using CIS.RuleEngine;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Application;
using CIS.Tests.RuleEngineHostFixture;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Bootstrap = CIS.Tests.IntegrationTests.Management.ManagementConfigurationBootstrapTests;
using TestClock = CIS.CommonTesting.ManualTimeProvider;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementRuleEngineVersionTests
{
    private const string UpdatedSchemaRevision = "schema-2";

    [TestMethod]
    public async Task NativeHostsIndependentlyAdoptOrRejectAndKeepCapturedVersionsAcrossControlRecovery()
    {
        string hostScope = ApplicationTestSupport.HostName;
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false,
            configuration: RuleDeployment(hostScope: hostScope));
        await fixture.StartReplicaAsync(selectSecurity: SelectSecurity);
        HttpClient actor = fixture.Client("operator-a");
        string application = fixture.ApplicationA;
        await Apply(actor, await Catalog(actor), "adoption-rule-catalog");
        string firstVersion = await SaveCountry(actor, application, "adoption-save-initial", "MLT");
        string initialTag = (await Assignment(actor, application)).ETag;
        JsonElement firstDecision = await DeployAccepted(actor, application, "adoption-deploy-initial", initialTag, firstVersion);
        RuleDefinition originalSchema = SchemaForHost(SchemaRevision);
        RuleDefinition updatedSchema = SchemaForHost(UpdatedSchemaRevision);

        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = RuleHostInventory(directory);
        string assertion = Path.Combine(directory.Path, "rule-host-assertion.txt");
        await File.WriteAllTextAsync(assertion, "owned.fixture.projected.assertion");
        using var sdkHandler = new Bootstrap.NativeTokenEndpoint(fixture.Token(Bootstrap.ObjectId, issuer: Bootstrap.NativeIssuer));
        using var sdkHttp = new HttpClient(sdkHandler);
        using var sdkTransport = new HttpClientTransport(sdkHttp);
        await using var firstHeartbeat = new ManualHeartbeat(capacity: 16);
        await using var secondHeartbeat = new ManualHeartbeat(capacity: 16);
        var firstClock = new TestClock(DateTimeOffset.UtcNow);
        var secondClock = new TestClock(firstClock.GetUtcNow());
        await using CISApplication compatible = CreateRuleHost(fixture, inventory, assertion, sdkTransport, firstHeartbeat, firstClock,
            "compatible-rule-host", [originalSchema, updatedSchema]);
        await using CISApplication incompatible = CreateRuleHost(fixture, inventory, assertion, sdkTransport, secondHeartbeat, secondClock,
            "older-rule-host", [originalSchema]);
        await compatible.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await incompatible.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.IsTrue(await ObserveRuleHost(compatible, firstClock));
        Assert.IsTrue(await ObserveRuleHost(incompatible, secondClock));
        string compatibleId = Registration(compatible).CaptureStatus().RegistrationId;
        string incompatibleId = Registration(incompatible).CaptureStatus().RegistrationId;
        Assert.AreNotEqual(compatibleId, incompatibleId);

        RuleEngineSelection held = await SelectRule(compatible);
        Assert.AreEqual(firstVersion, held.Prepared?.Definition.Version);
        Assert.AreEqual(firstVersion, (await SelectRule(incompatible)).Prepared?.Definition.Version);
        await AssertRuleInstance(actor, application, compatibleId, "applied", firstVersion, firstVersion, firstVersion);
        await AssertRuleInstance(actor, application, incompatibleId, "applied", firstVersion, firstVersion, firstVersion);

        int port = new Uri(fixture.Origins[0]).Port;
        await fixture.StopReplicasAsync();
        Assert.IsFalse(await ObserveRuleHost(compatible, firstClock));
        Assert.IsFalse(await ObserveRuleHost(incompatible, secondClock));
        Assert.AreEqual("AdmissionUnavailable", (await SelectRule(compatible)).Error?.Code);
        Assert.AreEqual(firstVersion, held.Prepared?.Definition.Version);

        int restarted = fixture.Origins.Count;
        await fixture.StartReplicaAsync(selectSecurity: SelectSecurity, requestedBusinessPort: port,
            serviceOptions: RuleHostServiceOptions(fixture, RuleDeployment(hostScope: hostScope, primarySchemaRevision: UpdatedSchemaRevision)));
        actor = fixture.Client("operator-a", restarted);
        await Apply(actor, await Catalog(actor), "adoption-rule-catalog-v2");
        _ = await ObserveRuleHost(compatible, firstClock);
        _ = await ObserveRuleHost(incompatible, secondClock);
        Assert.IsTrue(await compatible.Services.GetRequiredService<ConfigurationRuntime>().ReconcileCentralAsync()
            .WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.IsTrue(await incompatible.Services.GetRequiredService<ConfigurationRuntime>().ReconcileCentralAsync()
            .WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.IsTrue(await ObserveRuleHost(compatible, firstClock));
        Assert.IsTrue(await ObserveRuleHost(incompatible, secondClock));
        Assert.AreEqual(compatibleId, Registration(compatible).CaptureStatus().RegistrationId);
        Assert.AreEqual(incompatibleId, Registration(incompatible).CaptureStatus().RegistrationId);

        string edit = ReadText(await OnlyRuleset(actor, application), "editETag");
        string draft = DefinitionJson([Row("stable", 10, "ITA")], schemaRevision: UpdatedSchemaRevision);
        JsonElement saved = await SaveAccepted(actor, application, "adoption-save-updated", edit, draft, firstVersion, UpdatedSchemaRevision);
        string secondVersion = ReadText(saved, "version");
        Assert.AreEqual("VersionUnavailable", (await SelectRule(compatible, secondVersion)).Error?.Code);
        await DeployAccepted(actor, application, "adoption-deploy-updated", ReadText(firstDecision, "assignmentETag"), secondVersion);

        await AssertRuleInstance(actor, application, incompatibleId, "pending", secondVersion, firstVersion, firstVersion);
        Assert.AreEqual("VersionUnsupported", (await SelectRule(incompatible, secondVersion)).Error?.Code);

        Assert.IsTrue(await compatible.Services.GetRequiredService<ConfigurationRuntime>().ReconcileAsync());
        _ = await incompatible.Services.GetRequiredService<ConfigurationRuntime>().ReconcileAsync();
        Assert.IsTrue(await ObserveRuleHost(compatible, firstClock));
        Assert.IsTrue(await ObserveRuleHost(incompatible, secondClock));
        await AssertRuleInstance(actor, application, compatibleId, "applied", secondVersion, secondVersion, secondVersion);
        JsonElement rejected = await AssertRuleInstance(actor, application, incompatibleId, "rejected", secondVersion, secondVersion, firstVersion);
        Assert.AreEqual("Rejected", ReadText(rejected, "state"));
        Assert.IsTrue(rejected.GetProperty("liveStateValid").GetBoolean());

        Assert.AreEqual(secondVersion, (await SelectRule(compatible)).Prepared?.Definition.Version);
        Assert.AreEqual(firstVersion, (await SelectRule(compatible, firstVersion)).Prepared?.Definition.Version);
        Assert.AreEqual(secondVersion, (await SelectRule(compatible)).Prepared?.Definition.Version);
        Assert.AreEqual(firstVersion, held.Prepared?.Definition.Version);
        Assert.AreEqual("MLT", held.Prepared?.Definition.Rows[0].Conditions[0].Preset?.StringValue);

        ManagementSecurityOptions SelectSecurity(ManagementSecurityOptions selected)
            => WithRuleGrants(Bootstrap.WorkloadServer(selected), fixture.ApplicationA, secondEditor: false, deployment: true);
    }

    private static CISApplication CreateRuleHost(ManagementHostFixture fixture, TypeManagerOptions inventory, string assertion,
        HttpPipelineTransport sdkTransport, ManualHeartbeat heartbeat, TestClock clock, string instance, RuleDefinition[] schemas)
    {
        var management = new ManagementClientOptions("native-rule-management") { BaseUrl = fixture.Origins[0] + fixture.Options.BasePath };
        var configuration = new ManagementConfigurationClientOptions("native-rule-configuration") { BaseUrl = management.BaseUrl };
        return new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.Replace(ServiceDescriptor.Singleton<IHeartbeatOwnerFactory>(heartbeat));
            builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime>(new RuleHostLifetime()));
            builder.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            builder.Configuration = new(ApplicationTestSupport.HostName)
            {
                IsHealthCritical = true,
                PollingInterval = TimeSpan.FromHours(1),
                Factories = new Dictionary<Type, Func<IConfigurationObject>>
                {
                    [typeof(RuleEngineFixtureConfiguration)] = () => new RuleEngineFixtureConfiguration(schemas)
                },
                Enrollments = [new(typeof(RuleEngineFixtureConfiguration), RuleConfigurationId, "1", ConfigurationUpdateMode.HotReload)]
            };
            builder.CentralConfiguration = new(management, configuration, new(management.RestClientName)
            {
                ReportedInstanceId = instance,
                ReportingInterval = TimeSpan.FromMilliseconds(10),
                ControlCheckInterval = TimeSpan.FromMilliseconds(10),
                InitialReconnectDelay = TimeSpan.FromMilliseconds(10),
                MaximumReconnectDelay = TimeSpan.FromMilliseconds(10)
            }, async token => (await Bootstrap.NativePreparation.CreateAsync(fixture, assertion, sdkTransport,
                management, configuration, token, clock)).Descriptor)
            {
                TimeProvider = clock
            };
            builder.AddCisRuleEngine();
        });
    }

    private static TypeManagerOptions RuleHostInventory(TemporaryDirectory directory)
    {
        Assembly host = typeof(ApplicationTestSupport).Assembly;
        Assembly carrier = typeof(RuleEngineFixtureConfiguration).Assembly;
        Assembly[] selected = [host, carrier];
        foreach (Assembly assembly in selected)
        {
            File.Copy(assembly.Location, Path.Combine(directory.Path, Path.GetFileName(assembly.Location)));
        }
        string manifest = Path.Combine(directory.Path, "rule-host.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = host.FullName,
            assemblies = selected.Select(assembly => new { assemblyName = assembly.FullName, relativePath = Path.GetFileName(assembly.Location) })
        }));
        return new TypeManagerOptions(host) { AssemblyManifestPath = manifest, PrecomputedContracts = [typeof(IConfigurationObject)] };
    }

    private static RuleDefinition SchemaForHost(string revision)
        => RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(DefinitionJson([], schemaRevision: revision)));

    private static ManagementServiceOptions RuleHostServiceOptions(ManagementHostFixture fixture, ManagementConfigurationOptions configuration)
        => new(fixture.Options.ConnectionString, fixture.Options.CopyCursorSigningKey(), fixture.Options.Persistence, fixture.Options.BasePath,
            fixture.Options.MaximumConcurrentRequests, fixture.Options.OperationsSchema, fixture.Options.MessagingSchema, fixture.Options.Delivery,
            fixture.Options.Backend, configuration);

    private static ManagementRegistration Registration(CISApplication app) => app.Services.GetRequiredService<ManagementRegistration>();

    private static Task<RuleEngineSelection> SelectRule(CISApplication app, string? version = null)
        => app.Services.GetRequiredService<RuleEngineRuntime>().SelectAsync(new(RulesetId, new([]), version), CancellationToken.None);

    private static Task<bool> ObserveRuleHost(CISApplication app, TestClock clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(100));
        return Registration(app).OnElapsedAsync(CancellationToken.None);
    }

    private static async Task<JsonElement> AssertRuleInstance(HttpClient actor, string application, string registration, string convergence,
        string requested, string reportedDesired, string applied)
    {
        JsonElement page = await Get(actor, DeploymentRoute(application, "deployment-instances"));
        JsonElement instance = page.GetProperty("items").EnumerateArray().Single(item =>
            item.GetProperty("configuration").TryGetProperty("registrationId", out JsonElement id) && id.GetString() == registration);
        JsonElement configuration = instance.GetProperty("configuration");
        Assert.AreEqual(convergence, ReadText(configuration, "convergence"));
        Assert.AreEqual(requested, ReadText(instance, "requestedVersion"));
        Assert.AreEqual(reportedDesired, ReadText(instance, "reportedDesiredVersion"));
        Assert.AreEqual(applied, ReadText(instance, "appliedVersion"));
        Assert.AreEqual(applied, ReadText(instance, "lastSuccessfulVersion"));
        return configuration.GetProperty("reported");
    }

    private sealed class RuleHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
