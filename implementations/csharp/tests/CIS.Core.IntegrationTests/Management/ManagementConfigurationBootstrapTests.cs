using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Azure.Core.Pipeline;
using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Heartbeat;
using CIS.Hosting;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Security;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using CIS.Startup;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Application;
using CIS.Tests.IntegrationTests.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationBootstrapTests
{
    private const string Profile = "configuration-bootstrap";
    private const string Tenant = "11111111-1111-4111-8111-111111111111";
    internal const string ObjectId = "22222222-2222-4222-8222-222222222222";
    private const string ClientId = "33333333-3333-4333-8333-333333333333";
    internal const string NativeIssuer = "https://login.microsoftonline.com/" + Tenant + "/v2.0";

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task NativeSdkBootstrapPopulatesTheConsumingConstructorAndRetainsOneSessionThroughActualBuildCleanup(bool failedBuild, bool liveEmpty)
    {
        using var directory = new TemporaryDirectory();
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false,
            configuration: Deployment());
        await fixture.StartReplicaAsync(selectSecurity: options => WorkloadServer(options));
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "bootstrap-catalog");
        ManagementConfigurationDefinition common = (await Definitions(actor)).Items.Single(item => item.Kind == "common");
        string publication = await PublishValue(actor, common.SourceId, "bootstrap-source", "{\"Count\":42,\"Label\":\"Café — 日本語\"}");
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        string release = (await CreateRelease(actor, fixture.ApplicationA, map.ETag, "bootstrap-release",
            new(1, liveEmpty ? new("absent") : new("present", publication), new("absent"), "Native constructor baseline"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, map.ETag, "bootstrap-assignment", new(1, "replace", release, [], "Live baseline"));
        long registrationsBefore = await Count(fixture, fixture.Options.Persistence.Schema, "management_registration");

        string assertion = Path.Combine(directory.Path, "projected-assertion.txt");
        await File.WriteAllTextAsync(assertion, "owned.fixture.projected.assertion");
        using var sdkHandler = new NativeTokenEndpoint(fixture.Token(ObjectId, issuer: NativeIssuer));
        using var sdkHttp = new HttpClient(sdkHandler);
        using var sdkTransport = new HttpClientTransport(sdkHttp);
        await using var heartbeat = new ManualHeartbeat(capacity: 16);
        var management = new ManagementClientOptions("bootstrap-management") { BaseUrl = fixture.Origins[0] + fixture.Options.BasePath };
        var configuration = new ManagementConfigurationClientOptions("bootstrap-configuration") { BaseUrl = management.BaseUrl };
        NativePreparation? native = null;
        ConstructorWitness? witness = null;
        IServiceProvider? constructorProvider = null;
        int providerConstructions = 0;
        var original = new InvalidOperationException("The actual native host Build failed after constructor resolution.");
        CISApplication app = new(builder =>
        {
            ApplicationTestSupport.Configure(builder, ApplicationTestSupport.Inventory(directory));
            builder.Services.Replace(ServiceDescriptor.Singleton<IHeartbeatOwnerFactory>(heartbeat));
            builder.Configuration = new(ApplicationTestSupport.HostName)
            {
                IsHealthCritical = true,
                PollingInterval = TimeSpan.FromHours(1),
                Enrollments = [new(typeof(ConfigurationHostSettings), "shared.settings", "1", ConfigurationUpdateMode.HotReload)]
            };
            builder.CentralConfiguration = new(management, configuration, new(management.RestClientName)
            {
                ReportingInterval = TimeSpan.FromHours(1),
                ControlCheckInterval = TimeSpan.FromMilliseconds(10),
                InitialReconnectDelay = TimeSpan.FromMilliseconds(10),
                MaximumReconnectDelay = TimeSpan.FromMilliseconds(10)
            }, async token =>
            {
                native = await NativePreparation.CreateAsync(fixture, assertion, sdkTransport, management, configuration, token);
                return native.Descriptor;
            })
            {
                ExpectedCohortId = "canary"
            };
            builder.Services.AddSingleton<ConstructorWitness>(root =>
            {
                constructorProvider = root;
                witness = new(root.GetRequiredService<ConfigurationHostSettings>(), root.GetRequiredService<IConfigurationReader>(),
                    root.GetRequiredService<IManagedWorkAdmission>(), failedBuild);
                return witness;
            });
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<ConstructorWitness>());
            builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime>(root =>
            {
                Interlocked.Increment(ref providerConstructions);
                _ = root.GetRequiredService<ConstructorWitness>();
                Assert.IsTrue(native!.BootstrapRetired, "Actual bootstrap transports and credential work retire before Build.");
                Assert.AreEqual(1, sdkHandler.TokenRequests, "The actual SDK cache and one credential survive the handoff.");
                if (failedBuild)
                {
                    throw original;
                }
                return new NativeHostLifetime();
            }));
        });
        Task start = app.StartAsync();
        Task? disposal = null;
        try
        {
            if (failedBuild)
            {
                Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(start));
                Assert.IsNotNull(witness);
                disposal = app.DisposeAsync().AsTask();
                await witness.DisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.IsFalse(disposal.IsCompleted);
                Assert.IsFalse(native!.Disposed, "The captured actual provider drains before the unclaimed native preparation.");
                witness.ReleaseDisposal.TrySetResult();
                await disposal.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.IsTrue(native.Disposed);
                Assert.AreSame(original, app.Failure);
            }
            else
            {
                await start.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.IsNotNull(witness);
                Assert.AreSame(constructorProvider!.GetRequiredService<IServiceScopeFactory>(), app.Services.GetRequiredService<IServiceScopeFactory>(),
                    "The constructor's internal root scope and the public provider share the one actual scope factory.");
                Assert.AreSame(witness, app.Services.GetRequiredService<ConstructorWitness>());
                Assert.AreEqual(1, witness.Starts);
                Assert.IsTrue(app.Services.GetRequiredService<IManagedWorkAdmission>().TryAdmit());
                ManagementRegistrationStatus status = app.Services.GetRequiredService<ManagementRegistration>().CaptureStatus();
                ConfigurationAcceptanceStatus accepted = app.Services.GetRequiredService<ConfigurationRuntime>().CaptureStatus().Entries
                    .Single(item => item.ConfigurationType == typeof(ConfigurationHostSettings)).Acceptance!;
                Assert.AreEqual(status.RegistrationId, accepted.LastAccepted!.Desired.Target.RegistrationId);
                Assert.AreEqual(release, accepted.LastAccepted.Desired.ReleaseId);
                Assert.AreEqual(0L, accepted.Revision);
                Assert.AreEqual(registrationsBefore + 1, await Count(fixture, fixture.Options.Persistence.Schema, "management_registration"),
                    "Final startup adopts the original registration rather than writing another registration.");
                Assert.HasCount(2, native!.FinalMeterFactories);
                Assert.IsTrue(native.FinalMeterFactories.All(factory => ReferenceEquals(factory, app.Services.GetRequiredService<IMeterFactory>())));
                Assert.AreEqual(1, sdkHandler.TokenRequests);
                if (!liveEmpty)
                {
                    await ReconnectUsesCurrentAuthoritativeStateAsync(app, fixture, actor, common.SourceId, status.RegistrationId, release);
                }
            }
            Assert.AreEqual(1, providerConstructions);
            Assert.IsNotNull(witness);
            Assert.AreEqual(liveEmpty ? (3, "default") : (42, "Café — 日本語"), witness.ConstructorValues);
            Assert.IsFalse(witness.AdmittedAtConstruction, "No pre-Build control receipt can open managed work.");
        }
        finally
        {
            witness?.ReleaseDisposal.TrySetResult();
            await ApplicationTestSupport.SettleAsync(start);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(30));
        }
        Assert.IsTrue(native!.Disposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeMissingOrUnassignedBootstrapNeverConstructsAConsumerFromCapturedDefaults(bool applyCatalog)
    {
        using var directory = new TemporaryDirectory();
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false,
            configuration: Deployment());
        await fixture.StartReplicaAsync(selectSecurity: WorkloadServer);
        if (applyCatalog)
        {
            HttpClient actor = fixture.Client("operator-a");
            await Apply(actor, await Catalog(actor), "unassigned-bootstrap-catalog");
        }
        string assertion = Path.Combine(directory.Path, "unassigned-projected-assertion.txt");
        await File.WriteAllTextAsync(assertion, "owned.fixture.projected.assertion");
        using var sdkHandler = new NativeTokenEndpoint(fixture.Token(ObjectId, issuer: NativeIssuer));
        using var sdkHttp = new HttpClient(sdkHandler);
        using var sdkTransport = new HttpClientTransport(sdkHttp);
        var management = new ManagementClientOptions("missing-management") { BaseUrl = fixture.Origins[0] + fixture.Options.BasePath };
        var configuration = new ManagementConfigurationClientOptions("missing-configuration") { BaseUrl = management.BaseUrl };
        NativePreparation? native = null;
        int constructions = 0;
        CISApplication app = new(builder =>
        {
            ApplicationTestSupport.Configure(builder, ApplicationTestSupport.Inventory(directory));
            builder.Configuration = new(ApplicationTestSupport.HostName)
            {
                IsHealthCritical = true,
                PollingInterval = TimeSpan.FromHours(1),
                Enrollments = [new(typeof(ConfigurationHostSettings), "shared.settings", "1", ConfigurationUpdateMode.HotReload)]
            };
            builder.CentralConfiguration = new(management, configuration, new(management.RestClientName), async token =>
            {
                native = await NativePreparation.CreateAsync(fixture, assertion, sdkTransport, management, configuration, token);
                return native.Descriptor;
            })
            {
                ExpectedCohortId = "canary"
            };
            builder.Services.AddSingleton<ConstructorWitness>(root =>
            {
                constructions++;
                return new(root.GetRequiredService<ConfigurationHostSettings>(), root.GetRequiredService<IConfigurationReader>(),
                    root.GetRequiredService<IManagedWorkAdmission>(), holdDisposal: false);
            });
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<ConstructorWitness>());
        });
        Task start = app.StartAsync();
        try
        {
            _ = await ApplicationTestSupport.FailureAsync(start);
            Assert.IsNotNull(app.Failure);
            Assert.AreEqual(0, constructions);
            Assert.Throws<InvalidOperationException>(() => _ = app.Services);
            Assert.AreEqual(1, sdkHandler.TokenRequests);
        }
        finally
        {
            await ApplicationTestSupport.SettleAsync(start);
            await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        }
        Assert.IsNotNull(native);
        Assert.IsTrue(native.Disposed, "A failed required snapshot still joins the actual credential and bootstrap transports.");
        Assert.HasCount(0, native.FinalMeterFactories, "Missing authoritative bootstrap cannot build final transports or expose captured defaults.");
    }

    private static async Task ReconnectUsesCurrentAuthoritativeStateAsync(CISApplication app, ManagementHostFixture fixture,
        HttpClient actor, string sourceId, string registrationId, string originalRelease)
    {
        ConfigurationRuntime runtime = app.Services.GetRequiredService<ConfigurationRuntime>();
        ConfigurationHostSettings root = app.Services.GetRequiredService<ConfigurationHostSettings>();
        ManagementRegistration management = app.Services.GetRequiredService<ManagementRegistration>();
        IManagedWorkAdmission admission = app.Services.GetRequiredService<IManagedWorkAdmission>();
        int port = new Uri(fixture.Origins[0]).Port;
        await fixture.StopReplicasAsync();
        await Task.Delay(40);
        _ = await management.OnElapsedAsync(CancellationToken.None);
        Assert.IsFalse(admission.TryAdmit(), "An actual unavailable control endpoint closes new managed work.");
        Assert.AreEqual(42, root.Count);
        Assert.AreEqual(originalRelease, ConfigurationHostTestSupport.Entry(runtime).Acceptance!.LastAccepted!.Desired.ReleaseId);

        await fixture.StartReplicaAsync(selectSecurity: WorkloadServer, requestedBusinessPort: port);
        string publication = await PublishValue(actor, sourceId, "bootstrap-source-after-outage", "{\"Count\":84}");
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        string release = (await CreateRelease(actor, fixture.ApplicationA, map.ETag, "bootstrap-release-after-outage",
            new(1, new("present", publication), new("absent"), "Current state after missed polling"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, map.ETag, "bootstrap-assignment-after-outage",
            new(1, "replace", release, [], "Advance while the consumer still retains its old roots"));
        await Task.Delay(40);
        _ = await management.OnElapsedAsync(CancellationToken.None);
        await ConfigurationHostTestSupport.ObserveAsync(() => ConfigurationHostTestSupport.Entry(runtime).Acceptance?.LastAccepted?.Desired.ReleaseId == release,
            "Fresh control kicks the existing Configuration owner and converges without waiting for the one-hour poll interval.");
        await Task.Delay(40);
        _ = await management.OnElapsedAsync(CancellationToken.None);
        Assert.IsTrue(admission.TryAdmit(), "Only fresh control and completed authoritative reconciliation reopen admission.");
        Assert.AreSame(root, app.Services.GetRequiredService<ConfigurationHostSettings>());
        Assert.AreEqual(84, app.Services.GetRequiredService<IConfigurationReader>().Read(root, settings => settings.Count));
        Assert.AreEqual(registrationId, management.CaptureStatus().RegistrationId);
        Assert.AreEqual(1L, ConfigurationHostTestSupport.Entry(runtime).Revision);
        Assert.IsTrue(app.Services.GetRequiredService<ConfigurationHostDirectSettings>().Enabled,
            "Unenrolled roots retain their ordinary captured defaults throughout bootstrap and reconnect.");
    }

    private static ManagementConfigurationOptions Deployment()
    {
        var shape = new ManagementConfigurationShape("object", false,
            [new("Count", true, new("integer", false)), new("Label", true, new("string", false))]);
        var manifest = new ManagementConfigurationManifest(1,
            [new("shared.settings", "1", shape, "{\"Count\":3,\"Label\":\"default\"}")],
            [new("managed-a", "Development", ApplicationTestSupport.HostName, "shared.settings", "1")],
            [new("shared.settings", "1", "Development")],
            [new(NativeIssuer, ObjectId, "managed-a", "Development", "canary")]);
        return new(JsonSerializer.Serialize(manifest, ManagementConfigurationOptions.Json), 32L * 1024 * 1024);
    }

    internal static ManagementSecurityOptions WorkloadServer(ManagementSecurityOptions selected) => new()
    {
        Mode = selected.Mode,
        Profile = selected.Profile,
        AuthenticationScheme = selected.AuthenticationScheme,
        BusinessAddress = selected.BusinessAddress,
        BusinessPort = selected.BusinessPort,
        OperationalPort = selected.OperationalPort,
        ServerCertificateFile = selected.ServerCertificateFile,
        ServerPrivateKeyFile = selected.ServerPrivateKeyFile,
        ServerTrustBundleFile = selected.ServerTrustBundleFile,
        CertificateRevocation = selected.CertificateRevocation,
        BorrowedNativeKeyScope = selected.BorrowedNativeKeyScope,
        IdentityMappings = [.. selected.IdentityMappings, new(new(NativeIssuer, ObjectId), "managed-a", "Development")],
        Grants = [.. selected.Grants, new(selected.Profile, "management.registrations.write", EnvironmentOptions.ApplicationName,
            EnvironmentOptions.EnvironmentName, subject: new(NativeIssuer, ObjectId))],
        NativeProvider = (services, profile, scheme) =>
        {
            selected.NativeProvider!(services, profile, scheme);
            services.PostConfigure<JwtBearerOptions>(scheme, options =>
                options.TokenValidationParameters.ValidIssuers = [ManagementHostFixture.Issuer, NativeIssuer]);
        }
    };

    internal sealed class NativePreparation : IAsyncDisposable
    {
        private readonly EntraPreparedProfile prepared;
        private readonly List<RestHttpTransport> transports = [];
        private readonly RestHttpTransport ordinaryBootstrap;
        private readonly RestHttpTransport configurationBootstrap;
        internal bool BootstrapRetired
        {
            get; private set;
        }
        internal bool Disposed
        {
            get; private set;
        }
        internal List<IMeterFactory> FinalMeterFactories { get; } = [];
        internal CISApplicationCentralConfigurationPreparation Descriptor
        {
            get;
        }

        private NativePreparation(EntraPreparedProfile prepared, ManagementHostFixture fixture,
            ManagementClientOptions management, ManagementConfigurationClientOptions configuration)
        {
            this.prepared = prepared;
            var trust = new RestHttpServerTrust([fixture.ServerAuthority], X509RevocationMode.NoCheck);
            ordinaryBootstrap = RestHttpTransport.CreateSecurityBound(prepared.Binding, serverTrust: trust);
            configurationBootstrap = RestHttpTransport.CreateSecurityBound(prepared.Binding, serverTrust: trust);
            Descriptor = new(this, Bootstrap(management.RestClientName), Bootstrap(configuration.RestClientName), services =>
            {
                services.AddCisSecurity(prepared.Policy);
                services.AddCisEntraSecurity(prepared);
                services.AddCisEntraRestClient(new RestClientOptions(management.RestClientName) { LogCalls = false }
                    .ConfigureCisManagementRestClient(management), Profile, FinalTransport);
                services.AddCisManagementClient(management);
                services.AddCisEntraRestClient(new RestClientOptions(configuration.RestClientName) { LogCalls = false }
                    .ConfigureCisManagementConfigurationRestClient(configuration), Profile, FinalTransport);
                services.AddCisManagementConfigurationClient(configuration);
            }, prepared.ActivateFinalAsync, RetireAsync, ordinaryBootstrap, configurationBootstrap);

            RestClientOptions Bootstrap(string name) => new(name)
            {
                SecurityBinding = prepared.Binding,
                Authenticate = prepared.AuthenticateAsync,
                LogCalls = false
            };

            RestHttpTransport FinalTransport(IServiceProvider root, RestSecurityBinding binding)
            {
                Assert.AreSame(prepared.Binding, binding);
                IMeterFactory meterFactory = root.GetRequiredService<IMeterFactory>();
                FinalMeterFactories.Add(meterFactory);
                RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding, meterFactory, serverTrust: trust);
                transports.Add(transport);
                return transport;
            }
        }

        internal static async Task<NativePreparation> CreateAsync(ManagementHostFixture fixture, string assertion,
            HttpPipelineTransport transport, ManagementClientOptions management, ManagementConfigurationClientOptions configuration,
            CancellationToken token)
        {
            LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
            var policy = new SecurityOptions { PolicyVersion = "configuration-bootstrap-native-v1" };
            policy.Profiles.Add(new(Profile, SecurityProfileKind.EntraWorkloadIdentity, SecurityProfileRoles.Outbound,
                outboundIdentity: new(local.Application, local.Environment, new(NativeIssuer, ObjectId)),
                allowedOrigins: [new Uri(management.BaseUrl!).GetLeftPart(UriPartial.Authority)], requiredForReadiness: true));
            EntraPreparedProfile prepared = await EntraPreparedProfile.PrepareAsync(SecurityPolicySnapshot.Create(policy), Profile, new()
            {
                TenantId = Tenant,
                Outbound = new()
                {
                    ClientId = ClientId,
                    ServicePrincipalObjectId = ObjectId,
                    FederatedSubject = "system:serviceaccount:rm08:configuration-bootstrap",
                    FederatedTokenFile = assertion,
                    Scope = "api://management-native-service/.default",
                    Transport = transport
                }
            }, cancellationToken: token);
            try
            {
                return new(prepared, fixture, management, configuration);
            }
            catch
            {
                await prepared.DisposeAsync();
                throw;
            }
        }

        private async Task RetireAsync()
        {
            await ordinaryBootstrap.DisposeAsync();
            await configurationBootstrap.DisposeAsync();
            await prepared.RetireBootstrapAsync();
            BootstrapRetired = true;
        }

        public async ValueTask DisposeAsync()
        {
            await RetireAsync();
            foreach (RestHttpTransport transport in transports)
            {
                await transport.DisposeAsync();
            }
            await prepared.DisposeAsync();
            Disposed = true;
        }
    }

    private sealed class ConstructorWitness : IStartable, IAsyncDisposable
    {
        private readonly IManagedWorkAdmission admission;
        private readonly bool holdDisposal;
        internal (int Count, string Label) ConstructorValues
        {
            get;
        }
        internal bool AdmittedAtConstruction
        {
            get;
        }
        internal int Starts
        {
            get; private set;
        }
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ConstructorWitness(ConfigurationHostSettings settings, IConfigurationReader reader, IManagedWorkAdmission admission, bool holdDisposal)
        {
            ConstructorValues = reader.Read(settings, value => (value.Count, value.Label));
            AdmittedAtConstruction = admission.TryAdmit();
            this.admission = admission;
            this.holdDisposal = holdDisposal;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Assert.IsTrue(admission.TryAdmit(), "Fresh post-Build control and Configuration reconciliation precede business Startup.");
            Starts++;
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            DisposalEntered.TrySetResult();
            if (holdDisposal)
            {
                await ReleaseDisposal.Task;
            }
        }
    }

    private sealed class NativeHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // The supported workload SDK owns token acquisition/cache. Only its issuer endpoint is an owned fixture;
    // the resulting signed bearer crosses actual native TLS and server validation into the real PostgreSQL service.
    internal sealed class NativeTokenEndpoint(string accessToken) : HttpMessageHandler
    {
        internal int TokenRequests
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.AreEqual("login.microsoftonline.com", request.RequestUri!.Host);
            string json;
            if (request.RequestUri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                TokenRequests++;
                json = JsonSerializer.Serialize(new
                {
                    access_token = accessToken,
                    token_type = "Bearer",
                    expires_in = 3600
                });
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/discovery/instance", StringComparison.Ordinal))
            {
                json = JsonSerializer.Serialize(new
                {
                    tenant_discovery_endpoint = NativeIssuer + "/.well-known/openid-configuration",
                    metadata = new[] { new { preferred_network = "login.microsoftonline.com", preferred_cache = "login.microsoftonline.com", aliases = new[] { "login.microsoftonline.com" } } }
                });
            }
            else
            {
                json = JsonSerializer.Serialize(new
                {
                    issuer = NativeIssuer,
                    token_endpoint = "https://login.microsoftonline.com/" + Tenant + "/oauth2/v2.0/token",
                    authorization_endpoint = "https://login.microsoftonline.com/" + Tenant + "/oauth2/v2.0/authorize",
                    jwks_uri = "https://login.microsoftonline.com/discovery/v2.0/keys"
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
