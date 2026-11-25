using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Security;
using CIS.Security;
using CIS.TestSupport;
using CIS.TestSupport.Messaging;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

// Named transport requirement: the actual auth HTTP owner drains slow bodies and saturated native sockets.
[TestClass]
[TestCategory("RequiresNode")]
[DoNotParallelize]
public sealed class ManagementPortalTransportTests
{
    [TestMethod]
    public async Task AuthHttpDeadlinesBodyBoundsSaturationAndStopJoinActualNativeSockets()
    {
        var files = new PrivateContainerDirectory();
        bool cleanupConfirmed = true;
        try
        {
            TlsFixtureFiles.Write(files.Path);
            string root = ManagementPortalHostTests.RepositoryRoot();
            ProcessResult result = await ManagementPortalHostTests.RunNodeAsync(root,
                [Path.Combine(root, "implementations", "typescript", "applications", "management-portal", "tests", "native-oidc-http-consumer.mjs"), files.Path],
                new Dictionary<string, string?> { ["NODE_EXTRA_CA_CERTS"] = Path.Combine(files.Path, "root.pem") });
            using JsonDocument report = JsonDocument.Parse(result.StandardOutput);
            Assert.IsTrue(report.RootElement.GetProperty("nativeHttps").GetBoolean());
            Assert.IsTrue(report.RootElement.GetProperty("socketsDrained").GetBoolean());
            Assert.AreEqual(6, report.RootElement.GetProperty("cases").GetArrayLength());
            foreach (JsonElement item in report.RootElement.GetProperty("cases").EnumerateArray())
            {
                Assert.IsTrue(item.GetProperty("responseClosed").GetBoolean());
            }
        }
        catch (ProcessTimeoutException error) when (!error.CleanupCompleted || !error.Result.OutputDrained)
        {
            cleanupConfirmed = false;
            Console.Error.WriteLine("PORTAL_TRANSPORT_CUSTODY_RETAINED directory=" + files.Path);
            throw;
        }
        finally
        {
            if (cleanupConfirmed)
            {
                files.Dispose();
            }
        }
    }
}

// Actual Auth.js/PKCE/JWT/PostgreSQL/native-service proof, with an isolated controlled issuer rather than live Entra.
[TestClass]
[TestCategory("RequiresNode")]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementPortalHostTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string PortalClient = "22222222-2222-2222-2222-222222222222";
    private const string Audience = "33333333-3333-3333-3333-333333333333";
    private const string OperatorA = "44444444-4444-4444-4444-444444444444";
    private const string OperatorB = "55555555-5555-5555-5555-555555555555";

    [TestMethod]
    public async Task ActualOidcCallbacksIsolateRevocableDatabaseSessionsAndNativeTargetGrants()
    {
        await RunFixtureAsync(browser: false);
    }

    [TestMethod]
    public async Task ActualDesktopInspectionPreservesReportContextAndKeyboardSessionBoundaries()
    {
        await RunFixtureAsync(browser: true);
    }

    internal static async Task RunFixtureAsync(bool browser)
    {
        var files = new PrivateContainerDirectory();
        RSA? signing = null;
        ManagementHostFixture? fixture = null;
        string suffix = Guid.NewGuid().ToString("N");
        string schema = "cis_portal_" + suffix;
        string ownerRole = "cis_portal_o_" + suffix;
        string runtimeRole = "cis_portal_r_" + suffix;
        string ownerPassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string runtimePassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        bool ownerCreated = false;
        bool runtimeCreated = false;
        bool schemaCreated = false;
        bool cleanupConfirmed = true;
        Exception? original = null;
        try
        {
            TlsFixtureFiles.Write(files.Path);
            signing = RSA.Create(2048);
            fixture = await ManagementHostFixture.CreateAsync(replicas: 1,
                configuration: ManagementConfigurationCatalogTests.Deployment(greetingDefault: "PORTAL_PRIVATE_DEFAULT"));
            HttpClient catalogActor = fixture.Client("operator-a");
            await ManagementConfigurationCatalogTests.Apply(catalogActor,
                await ManagementConfigurationCatalogTests.Catalog(catalogActor), "portal-catalog");
            ManagementConfigurationDefinition definition = (await ManagementConfigurationCatalogTests.Definitions(catalogActor))
                .Items.Single(row => row.ApplicationId == fixture.ApplicationA && row.Kind == "application");
            string secondRegistration = await SeedInventoryAsync(fixture);
            await fixture.StopReplicasAsync();
            int issuerPort = UnusedPort();
            string authority = "https://localhost:" + issuerPort.ToString(CultureInfo.InvariantCulture);
            string issuer = authority + "/" + Tenant + "/v2.0";
            var metadata = new OpenIdConnectConfiguration { Issuer = issuer };
            metadata.SigningKeys.Add(new RsaSecurityKey(signing) { KeyId = "rm09-portal-owned-key" });
            await fixture.StartReplicaAsync(source => new()
            {
                Mode = "entra-management",
                Profile = source.Profile,
                AuthenticationScheme = source.AuthenticationScheme,
                BusinessPort = source.BusinessPort,
                OperationalPort = source.OperationalPort,
                ServerCertificateFile = source.ServerCertificateFile,
                ServerPrivateKeyFile = source.ServerPrivateKeyFile,
                ServerTrustBundleFile = source.ServerTrustBundleFile,
                CertificateRevocation = source.CertificateRevocation,
                MaximumShortLivedLifetime = source.MaximumShortLivedLifetime,
                BorrowedNativeKeyScope = source.BorrowedNativeKeyScope,
                EntraTenantId = Tenant,
                EntraAudience = Audience,
                EntraAuthorityHost = new(authority + "/"),
                EntraPortalClientId = PortalClient,
                EntraOperatorScope = "management.operator",
                EntraClockSkew = TimeSpan.Zero,
                EntraMetadataManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata),
                IdentityMappings =
                [
                    new(new(issuer, OperatorA), "management-portal", EnvironmentOptions.EnvironmentName,
                        clientApplicationId: PortalClient, actorKind: SecurityActorKind.User),
                    new(new(issuer, OperatorB), "management-portal", EnvironmentOptions.EnvironmentName,
                        clientApplicationId: PortalClient, actorKind: SecurityActorKind.User)
                ],
                Grants =
                [
                    ReadGrant(issuer, OperatorA, fixture.ApplicationA),
                    ReadGrant(issuer, OperatorB, fixture.ApplicationB),
                    new(ManagementHostFixture.Profile, ManagementConfigurationCapabilities.DefinitionsRead,
                        EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName, subject: new(issuer, OperatorA),
                        resourceSelection: CapabilityResourceSelection.ExactResource,
                        resourceType: ManagementConfigurationCapabilities.SourceResource, resourceIdentifier: definition.SourceId)
                ]
            });
            await SqlAsync(fixture.Source, $"CREATE ROLE \"{ownerRole}\" LOGIN PASSWORD '{ownerPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT");
            ownerCreated = true;
            await SqlAsync(fixture.Source, $"CREATE ROLE \"{runtimeRole}\" LOGIN PASSWORD '{runtimePassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT");
            runtimeCreated = true;
            await SqlAsync(fixture.Source, $"CREATE SCHEMA \"{schema}\" AUTHORIZATION \"{ownerRole}\"");
            schemaCreated = true;
            string root = RepositoryRoot();
            string portal = Path.Combine(root, "implementations", "typescript", "applications", "management-portal");
            string ownerUrl = ConnectionUrl(fixture.Source.ConnectionString, ownerRole, ownerPassword);
            string runtimeUrl = ConnectionUrl(fixture.Source.ConnectionString, runtimeRole, runtimePassword);
            await RunNodeAsync(portal, [Path.Combine(portal, "scripts", "migrate.mts")],
                new Dictionary<string, string?>
                {
                    ["CIS_PORTAL_SESSION_DATABASE"] = ownerUrl,
                    ["CIS_PORTAL_SESSION_SCHEMA"] = schema
                });
            await SqlAsync(fixture.Source, $"GRANT USAGE ON SCHEMA \"{schema}\" TO \"{runtimeRole}\"; "
                + $"GRANT SELECT ON \"{schema}\".portal_schema_version TO \"{runtimeRole}\"; "
                + $"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{schema}\".users, \"{schema}\".accounts, \"{schema}\".sessions TO \"{runtimeRole}\"");
            await PrivateContainerDirectory.WriteNewAsync(Path.Combine(files.Path, "oidc.key"), Encoding.UTF8.GetBytes(signing.ExportPkcs8PrivateKeyPem()));
            await PrivateContainerDirectory.WriteNewAsync(Path.Combine(files.Path, "management-root.pem"), await File.ReadAllBytesAsync(fixture.ServerAuthorityFile));
            byte[] input = JsonSerializer.SerializeToUtf8Bytes(new
            {
                issuerPort,
                authority,
                tenant = Tenant,
                portalClient = PortalClient,
                audience = Audience,
                operatorA = OperatorA,
                operatorB = OperatorB,
                applicationA = fixture.ApplicationA,
                applicationB = fixture.ApplicationB,
                registrationA = fixture.RegistrationA,
                secondRegistration,
                managementUrl = fixture.Origins[^1] + "/platform/",
                sessionSchema = schema,
                sessionOwnerUrl = ownerUrl,
                sessionRuntimeUrl = runtimeUrl,
                clientSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
                authSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                browser
            });
            string inputFile = Path.Combine(files.Path, "portal-fixture.json");
            await PrivateContainerDirectory.WriteNewAsync(inputFile, input);
            CryptographicOperations.ZeroMemory(input);
            ProcessResult result = await RunNodeAsync(root,
                [Path.Combine(portal, "tests", "native-host-consumer.mjs"), files.Path],
                new Dictionary<string, string?> { ["NODE_EXTRA_CA_CERTS"] = Path.Combine(files.Path, "root.pem") });
            using JsonDocument report = JsonDocument.Parse(result.StandardOutput);
            Assert.IsTrue(report.RootElement.GetProperty("nativeOidc").GetBoolean());
            Assert.IsTrue(report.RootElement.GetProperty("sessionsRealPostgres").GetBoolean());
            Assert.IsTrue(report.RootElement.GetProperty("tokensStayedServerSide").GetBoolean());
            Assert.IsTrue(report.RootElement.GetProperty("hostStopped").GetBoolean());
            Assert.IsTrue(report.RootElement.GetProperty("socketsDrained").GetBoolean());
            Assert.IsTrue(report.RootElement.GetProperty("cases").GetArrayLength() >= 12);
            Assert.AreEqual(browser, report.RootElement.GetProperty("browserChecked").GetBoolean());
        }
        catch (ProcessTimeoutException error) when (!error.CleanupCompleted || !error.Result.OutputDrained)
        {
            cleanupConfirmed = false;
            original = error;
            throw;
        }
        catch (Exception error)
        {
            original = error;
            throw;
        }
        finally
        {
            var errors = new List<Exception>();
            if (!cleanupConfirmed)
            {
                Console.Error.WriteLine("PORTAL_FIXTURE_CUSTODY_RETAINED directory=" + files.Path + " schema=" + schema
                    + " ownerRole=" + ownerRole + " runtimeRole=" + runtimeRole);
            }
            else
            {
                if (schemaCreated)
                {
                    await CleanupAsync($"DROP SCHEMA \"{schema}\" CASCADE");
                }
                if (runtimeCreated)
                {
                    await CleanupAsync($"DROP ROLE \"{runtimeRole}\"");
                }
                if (ownerCreated)
                {
                    await CleanupAsync($"DROP ROLE \"{ownerRole}\"");
                }
                if (fixture is not null)
                {
                    try
                    {
                        await fixture.DisposeAsync();
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                }
                signing?.Dispose();
                try
                {
                    files.Dispose();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            if (errors.Count > 0)
            {
                if (original is not null)
                {
                    errors.Insert(0, original);
                }
                throw new AggregateException("The portal fixture did not fully restore its owned schema/roles.", errors);
            }

            async Task CleanupAsync(string sql)
            {
                try
                {
                    await SqlAsync(fixture?.Source ?? throw new InvalidOperationException("The owned database fixture is unavailable."), sql);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
        }
    }

    private static async Task<string> SeedInventoryAsync(ManagementHostFixture fixture)
    {
        string observed = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        string secondRegistration = Guid.NewGuid().ToString("D");
        HttpClient workload = fixture.Client("workload-a");
        using (HttpResponseMessage registration = await ManagementServiceTests.Put(workload,
            "registrations/" + secondRegistration, new ManagementRegistrationRequest(1, "seed-a-second", observed, "2.4.2")))
        {
            Assert.AreEqual(HttpStatusCode.OK, registration.StatusCode);
        }
        var inventory = new ManagementInventorySnapshot("1", observed, [],
            [new("customer-service", "lifecycle", "Lifecycle")],
            [new("customer-service", "customer.dormancy", "lifecycle", "Customer dormancy", ["scheduled"],
                EffectiveConfiguration: new("0", true), Observations: [new("partition-eu", "scheduled", "released", "jobs.completed")])]);
        using HttpResponseMessage response = await ManagementServiceTests.Put(workload,
            "registrations/" + fixture.RegistrationA + "/inventory", inventory);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return secondRegistration;
    }

    private static CapabilityGrant ReadGrant(string issuer, string subject, string applicationId)
        => new(ManagementHostFixture.Profile, "management.applications.read", EnvironmentOptions.ApplicationName,
            EnvironmentOptions.EnvironmentName, subject: new(issuer, subject), resourceSelection: CapabilityResourceSelection.ExactResource,
            resourceType: "management.application", resourceIdentifier: applicationId);

    private static async Task SqlAsync(NpgsqlDataSource source, string sql)
    {
        await using NpgsqlCommand command = source.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private static string ConnectionUrl(string connectionString, string user, string password)
    {
        var source = new NpgsqlConnectionStringBuilder(connectionString);
        return new UriBuilder("postgresql", source.Host!, source.Port)
        {
            UserName = user,
            Password = password,
            Path = "/" + source.Database
        }.Uri.AbsoluteUri;
    }

    private static int UnusedPort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    internal static string RepositoryRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "implementations", "csharp", "CisLib.slnx")))
        {
            root = root.Parent;
        }
        return root?.FullName ?? throw new InvalidOperationException("The current repository source is required for the portal consumer.");
    }

    internal static async Task<ProcessResult> RunNodeAsync(string directory, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?> environment)
    {
        string node = System.Environment.GetEnvironmentVariable("CIS_TEST_NODE_EXECUTABLE") ?? "node";
        ProcessResult result = await ProcessRunner.RunAsync(node, arguments, directory, TimeSpan.FromSeconds(180), environment,
            maximumCapturedBytesPerStream: 65536);
        Assert.AreEqual(0, result.ExitCode, "Native portal consumer failed: " + result.StandardOutput + result.StandardError);
        Assert.IsTrue(result.OutputDrained);
        return result;
    }
}
