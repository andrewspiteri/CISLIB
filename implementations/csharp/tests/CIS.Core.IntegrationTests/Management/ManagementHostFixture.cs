using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CIS.Hosting;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Management.Service;
using CIS.Management.Service.Persistence;
using CIS.Management.Service.Metadata;
using CIS.Management.Service.Security;
using CIS.Messaging.RabbitMQ;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Certificates.Hosting;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using CIS.Tests.IntegrationTests.RestClient;
using CIS.Transactions;
using CIS.Transactions.Hosting;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

// Named RM07 acceptance fixture: two real product roots, native signed actor credentials and the owned PostgreSQL service.
internal sealed class ManagementHostFixture : IAsyncDisposable
{
    internal const string Issuer = "https://management-native.example.test/issuer";
    internal const string Audience = "management-native-service";
    internal const string Profile = "management-security";
    private readonly PrivateContainerDirectory files = new();
    private readonly RSA signing = RSA.Create(2048);
    private readonly List<CISApplication> applications = [];
    private X509Certificate2? authority;
    private IDisposable? databaseLease;
    private IDisposable? brokerLease;
    private ServiceProvider? setup;
    private string certificateFile = "";
    private string keyFile = "";
    private string rootFile = "";
    private string inventoryFile = "";
    private readonly List<HttpClient> clients = [];
    private readonly List<RestHttpTransport> clientTransports = [];

    internal ManagementServiceOptions Options { get; private set; } = null!;
    internal NpgsqlDataSource Source { get; private set; } = null!;
    internal string ApplicationA { get; private set; } = "";
    internal string ApplicationB { get; private set; } = "";
    internal string RegistrationA { get; } = Guid.NewGuid().ToString("D");
    internal string RegistrationB { get; } = Guid.NewGuid().ToString("D");
    internal List<string> Origins { get; } = [];
    internal List<int> OperationalPorts { get; } = [];
    internal X509Certificate2 ServerAuthority => authority!;
    internal string ServerAuthorityFile => rootFile;
    internal string ConsumerReportFile => Path.Combine(files.Path, "node-consumer-report.json");

    internal async Task<string> WriteConsumerBearerAsync(string subject)
    {
        string path = Path.Combine(files.Path, "node-bearer-" + Guid.NewGuid().ToString("N") + ".txt");
        await PrivateContainerDirectory.WriteNewAsync(path, Encoding.UTF8.GetBytes(Token(subject)));
        return path;
    }

    internal static async Task<ManagementHostFixture> CreateAsync(int replicas = 2, bool start = true,
        long maximumRetainedEnvelopeBytes = 64L * 1024 * 1024)
    {
        var fixture = new ManagementHostFixture();
        try
        {
            OwnedPostgresService database = await IntegrationServices.GetPostgresAsync();
            fixture.databaseLease = database.AcquireClientLease();
            fixture.Source = NpgsqlDataSource.Create(database.CreateConnectionString(4));
            string suffix = Guid.NewGuid().ToString("N");
            OwnedRabbitService broker = await IntegrationServices.GetRabbitAsync();
            fixture.brokerLease = broker.AcquireClientLease();
            RabbitFixtureInformation information = broker.CaptureInformation();
            RabbitFixturePrincipal principal = information.Principals["service"];
            var delivery = new ManagementDeliveryOptions(new()
            {
                BrokerAlias = "ManagementAudit",
                HostName = information.Host,
                Port = information.Port,
                ServerName = information.ServerName,
                VirtualHost = information.VirtualHost,
                UserName = principal.User,
                Password = principal.Password,
                Namespace = information.Namespace + ".management." + suffix[..8],
                SubjectIssuer = "rm07-fixture",
                SecurityProfile = ManagementDeliveryOptions.BrokerScheme,
                TrustedRootCertificatePem = broker.RootPem,
                EndpointNamespaces = [new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName, "management")]
            }, maximumRetainedEnvelopeBytes: maximumRetainedEnvelopeBytes);
            RabbitMqOptions setupBroker = delivery.CreateBrokerOptions();
            setupBroker.UserName = information.Principals["setup"].User;
            setupBroker.Password = information.Principals["setup"].Password;
            await RabbitMqTopology.InstallAsync(setupBroker, delivery.CreateMessagingSnapshot());
            fixture.Options = new(database.CreateConnectionString(4), RandomNumberGenerator.GetBytes(32),
                new("rm07_" + suffix), basePath: "/platform", operationsSchema: "rm07_op_" + suffix,
                messagingSchema: "rm07_msg_" + suffix, delivery: delivery);
            var services = new ServiceCollection();
            services.AddCisAdoNetDatabase("management", _ => fixture.Source, new()
            {
                SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
            });
            fixture.setup = services.BuildServiceProvider();
            ILocalTransactionFactory transactions = fixture.setup.GetRequiredService<ILocalTransactionFactory>();
            await ManagementDatabase.InstallAsync(transactions, fixture.Options, CancellationToken.None);
            var store = new PostgreSqlManagementStore(transactions, fixture.Options.Persistence);
            ManagementStoreResult<ManagementRegistrationReceipt> a = await store.RegisterAsync(Owner("workload-a", "managed-a"),
                fixture.RegistrationA, Request("seed-a"));
            ManagementStoreResult<ManagementRegistrationReceipt> b = await store.RegisterAsync(Owner("workload-b", "managed-b"),
                fixture.RegistrationB, Request("seed-b"));
            Assert.IsTrue(a.IsKnownCommitted, a.Code);
            Assert.IsTrue(b.IsKnownCommitted, b.Code);
            fixture.ApplicationA = a.Value!.ApplicationId;
            fixture.ApplicationB = b.Value!.ApplicationId;
            await fixture.WriteServerMaterialAsync();
            fixture.WriteInventory();
            if (start)
            {
                for (int index = 0; index < replicas; index++)
                {
                    await fixture.StartReplicaAsync();
                }
            }
            return fixture;
        }
        catch (Exception original)
        {
            try
            {
                await fixture.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
    }

    internal async Task StartReplicaAsync(Func<ManagementSecurityOptions, ManagementSecurityOptions>? selectSecurity = null,
        string? hostEnvironment = null, Action<CISApplicationBuilder>? configure = null)
    {
        using var business = new TcpListener(IPAddress.Loopback, 0);
        using var operational = new TcpListener(IPAddress.Loopback, 0);
        business.Start();
        operational.Start();
        int businessPort = ((IPEndPoint)business.LocalEndpoint).Port;
        int operationalPort = ((IPEndPoint)operational.LocalEndpoint).Port;
        var options = new ManagementSecurityOptions
        {
            Mode = "native",
            BusinessPort = businessPort,
            OperationalPort = operationalPort,
            ServerCertificateFile = certificateFile,
            ServerPrivateKeyFile = keyFile,
            ServerTrustBundleFile = rootFile,
            CertificateRevocation = CertificateRevocationPolicy.ShortLived,
            BorrowedNativeKeyScope = RestSecurityNativeKeyCustody.Shared,
            IdentityMappings = Mappings(),
            Grants = Grants(),
            NativeProvider = ConfigureNative
        };
        options = selectSecurity?.Invoke(options) ?? options;
        string[] arguments = hostEnvironment is null ? [] : ["--environment", hostEnvironment];
        CISApplication application = ManagementServiceApplication.Create(arguments, Options, options, builder =>
        {
            builder.TypeManager = new TypeManagerOptions(typeof(ManagementServiceApplication).Assembly)
            {
                AssemblyManifestPath = inventoryFile
            };
            builder.LoggingEnabled = false;
            builder.Host.Logging.ClearProviders();
            configure?.Invoke(builder);
        });
        applications.Add(application);
        business.Stop();
        operational.Stop();
        await application.StartAsync();
        Origins.Add("https://127.0.0.1:" + businessPort.ToString(CultureInfo.InvariantCulture));
        OperationalPorts.Add(operationalPort);
    }

    // The caller owns its ordinary host/facade. This fixture retains borrowed native transports
    // until those callers and all service roots have actually drained, then releases its trust owner.
    internal ManagementClientOptions RegisterBoundClient(IServiceCollection services, string subject, string clientName, int replica = 0)
    {
        var management = new ManagementClientOptions(clientName) { BaseUrl = Origins[replica] + Options.BasePath };
        var rest = new RestClientOptions(clientName) { LogCalls = false }.ConfigureCisManagementRestClient(management);
        var binding = new RestSecurityBinding(clientName, new(Issuer, subject),
            "management-policy-v1", RestSecurityCredentialKind.RequestAuthentication, [Origins[replica]]);
        services.AddCisSecurityBoundRestClient(rest, binding, _ => (authentication, token) =>
        {
            token.ThrowIfCancellationRequested();
            authentication.Headers.Set("Authorization", "Bearer " + Token(subject));
            return Task.CompletedTask;
        }, (root, selected) =>
        {
            RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(selected,
                root.GetRequiredService<IMeterFactory>(), serverTrust: new([ServerAuthority], X509RevocationMode.NoCheck));
            lock (clientTransports)
            {
                clientTransports.Add(transport);
            }
            return transport;
        });
        services.AddCisManagementClient(management);
        return management;
    }

    internal HttpClient Client(string? subject = "workload-a", int replica = 0, string? explicitToken = null)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        handler.SslOptions.CertificateChainPolicy = new()
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true
        };
        handler.SslOptions.CertificateChainPolicy.CustomTrustStore.Add(authority!);
        var client = new HttpClient(handler) { BaseAddress = new(Origins[replica] + "/platform/management/v1/"), Timeout = TimeSpan.FromSeconds(20) };
        if (subject is not null || explicitToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", explicitToken ?? Token(subject!));
        }
        clients.Add(client);
        return client;
    }

    internal string Token(string subject, string? issuer = null, string? audience = null, RSA? key = null,
        DateTimeOffset? expiration = null, bool delegated = false)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["actor_kind"] = subject.StartsWith("operator-", StringComparison.Ordinal) ? "user" : "application"
        };
        if (delegated)
        {
            claims["human_issuer"] = "https://human.example.test";
            claims["human_subject"] = "operator-a";
            claims["delegation"] = "validated-fixture-delegation-v1";
        }
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow.AddMinutes(-2),
            Expires = (expiration ?? DateTimeOffset.UtcNow.AddMinutes(10)).UtcDateTime,
            SigningCredentials = new(new RsaSecurityKey(key ?? signing), SecurityAlgorithms.RsaSha256)
        });
    }

    internal static ManagementRegistrationRequest Request(string instance)
        => new(1, instance, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture), "1.0.0");

    private static ManagementRegistrationOwner Owner(string subject, string application)
        => new(application, "Development", Issuer, subject, new("notProvided"));

    private IReadOnlyList<SecurityIdentityMapping> Mappings() =>
    [
        new(new(Issuer, "workload-a"), "managed-a", "Development"),
        new(new(Issuer, "workload-b"), "managed-b", "Development"),
        new(new(Issuer, "reader"), "reader-app", "Development"),
        new(new(Issuer, "wrong-env"), "managed-a", "Other"),
        new(new(Issuer, "operator-a"), null, null, actorKind: SecurityActorKind.User),
        new(new(Issuer, "operator-b"), null, null, actorKind: SecurityActorKind.User)
    ];

    private IReadOnlyList<CapabilityGrant> Grants()
    {
        var grants = new List<CapabilityGrant>();
        foreach (string subject in new[] { "workload-a", "workload-b" })
        {
            grants.Add(new(Profile, "management.registrations.write", EnvironmentOptions.ApplicationName,
                EnvironmentOptions.EnvironmentName, subject: new(Issuer, subject)));
        }
        foreach (string subject in new[] { "workload-a", "workload-b", "reader", "operator-a", "operator-b" })
        {
            string application = subject == "workload-b" ? ApplicationB : ApplicationA;
            foreach (string capability in new[] { "management.applications.read", "management.applications.metadata.write", "management.operations.read" })
            {
                if (subject == "reader" && capability != "management.applications.read")
                {
                    continue;
                }
                grants.Add(new(Profile, capability, EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                    subject: new(Issuer, subject), resourceSelection: CapabilityResourceSelection.ExactResource,
                    resourceType: "management.application", resourceIdentifier: application));
            }
        }
        return grants.AsReadOnly();
    }

    private void ConfigureNative(IServiceCollection services, string profile, string scheme)
    {
        services.AddAuthentication().AddJwtBearer(scheme, options =>
        {
            options.MapInboundClaims = false;
            options.SaveToken = false;
            options.TokenValidationParameters = new()
            {
                ValidIssuer = Issuer,
                ValidAudience = Audience,
                IssuerSigningKey = new RsaSecurityKey(signing),
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            };
        });
        services.AddCisSecurityProjection(profile, _ => new NativeProjection());
    }

    private void WriteInventory()
    {
        Assembly[] assemblies = [typeof(ManagementServiceApplication).Assembly, typeof(TypeManager).Assembly,
            typeof(MetricsApplicationIntegration).Assembly, typeof(CertificateAuthenticationExtensions).Assembly,
            typeof(EntraServiceCollectionExtensions).Assembly, typeof(RabbitMqServiceCollectionExtensions).Assembly];
        string directory = Directory.CreateDirectory(Path.Combine(files.Path, "type-inventory")).FullName;
        foreach (Assembly assembly in assemblies)
        {
            File.Copy(assembly.Location, Path.Combine(directory, Path.GetFileName(assembly.Location)));
        }
        inventoryFile = Path.Combine(directory, "management.types.json");
        File.WriteAllText(inventoryFile, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = assemblies[0].FullName,
            assemblies = assemblies.Select(assembly => new
            {
                assemblyName = assembly.FullName,
                relativePath = Path.GetFileName(assembly.Location)
            })
        }));
    }

    private async Task WriteServerMaterialAsync()
    {
        using RSA authorityKey = RSA.Create(2048);
        var root = new CertificateRequest("CN=RM07 owned root", authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        root.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        root.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        root.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(root.PublicKey, false));
        authority = root.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));
        using RSA leafKey = RSA.Create(2048);
        var leaf = new CertificateRequest("CN=RM07 product service", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leaf.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leaf.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        leaf.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        leaf.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(leaf.PublicKey, false));
        leaf.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority, true, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        leaf.CertificateExtensions.Add(names.Build());
        using X509Certificate2 certificate = leaf.Create(authority, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(2), RandomNumberGenerator.GetBytes(16));
        certificateFile = Path.Combine(files.Path, "server.pem");
        keyFile = Path.Combine(files.Path, "server-key.pem");
        rootFile = Path.Combine(files.Path, "root.pem");
        await PrivateContainerDirectory.WriteNewAsync(certificateFile, Encoding.UTF8.GetBytes(certificate.ExportCertificatePem()));
        byte[] keyBytes = Encoding.UTF8.GetBytes(leafKey.ExportPkcs8PrivateKeyPem());
        try
        {
            await PrivateContainerDirectory.WriteNewAsync(keyFile, keyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
        await PrivateContainerDirectory.WriteNewAsync(rootFile, Encoding.UTF8.GetBytes(authority.ExportCertificatePem()));
    }

    public async ValueTask DisposeAsync()
    {
        var errors = new List<Exception>();
        foreach (HttpClient client in clients)
        {
            AttemptDispose(client);
        }
        foreach (CISApplication application in applications.AsEnumerable().Reverse())
        {
            await AttemptAsync(() => application.StopAsync());
            await AttemptAsync(() => application.DisposeAsync().AsTask());
        }
        foreach (RestHttpTransport transport in clientTransports)
        {
            await AttemptAsync(() => transport.DisposeAsync().AsTask());
        }
        if (Source is not null && Options is not null)
        {
            await AttemptAsync(async () =>
            {
                await using var command = Source.CreateCommand($"DROP SCHEMA IF EXISTS \"{Options.Persistence.Schema}\" CASCADE;"
                    + $"DROP SCHEMA IF EXISTS \"{Options.OperationsSchema}\" CASCADE;DROP SCHEMA IF EXISTS \"{Options.MessagingSchema}\" CASCADE;");
                await command.ExecuteNonQueryAsync();
            });
        }
        if (setup is not null)
        {
            await AttemptAsync(() => setup.DisposeAsync().AsTask());
        }
        if (Source is not null)
        {
            await AttemptAsync(() => Source.DisposeAsync().AsTask());
        }
        AttemptDispose(databaseLease);
        AttemptDispose(brokerLease);
        AttemptDispose(authority);
        AttemptDispose(signing);
        AttemptDispose(files);
        if (errors.Count != 0)
        {
            throw new AggregateException("Management fixture cleanup did not fully settle.", errors);
        }

        async Task AttemptAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        void AttemptDispose(IDisposable? item)
        {
            try
            {
                item?.Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
    }

    private sealed class NativeProjection : ISecurityIdentityProjector
    {
        public ValueTask<SecurityProjectionResult> ProjectAsync(HttpContext context, AuthenticateResult authentication, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimsPrincipal principal = authentication.Principal ?? throw new InvalidOperationException("Native authentication must have succeeded.");
            string subject = Single(principal, "sub");
            string issuer = Single(principal, "iss");
            string actor = Single(principal, "actor_kind");
            var identity = new SecuritySubject(issuer, subject);
            string? delegation = principal.FindFirst("delegation")?.Value;
            SecuritySubject? human = actor == "user" ? identity : delegation is null ? null
                : new(Single(principal, "human_issuer"), Single(principal, "human_subject"));
            return ValueTask.FromResult(SecurityProjectionResult.Authenticated(new(identity,
                actor == "user" ? SecurityActorKind.User : SecurityActorKind.Application,
                new(DateTimeOffset.UtcNow, DateTimeOffset.FromUnixTimeSeconds(long.Parse(Single(principal, "exp"), CultureInfo.InvariantCulture))),
                "jwt", humanSubject: human, verifiedDelegation: delegation)));
        }

        private static string Single(ClaimsPrincipal principal, string type)
            => principal.FindAll(type).Select(claim => claim.Value).Single();
    }
}
