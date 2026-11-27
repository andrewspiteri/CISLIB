using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Security;
using CIS.Management.Wire;
using CIS.Security;
using CIS.TestSupport;
using CIS.TestSupport.Messaging;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.JsonWebTokens;
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
public sealed partial class ManagementPortalHostTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string PortalClient = "22222222-2222-2222-2222-222222222222";
    private const string Audience = "33333333-3333-3333-3333-333333333333";
    private const string OperatorA = "44444444-4444-4444-4444-444444444444";
    private const string OperatorB = "55555555-5555-5555-5555-555555555555";
    private const string LookupOperator = "66666666-6666-6666-6666-666666666666";
    private const string ContinuityOperator = "77777777-7777-7777-7777-777777777777";

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

    internal static async Task RunFixtureAsync(bool browser, bool rollout = false, bool r08 = false, bool reviewCorrections = false, bool versionWorkflow = false,
        bool instanceStatus = false, bool firstVersion = false, bool hostShutdown = false, bool canceledHostRead = false,
        bool newSaveRecovery = false, bool releaseConflict = false, bool deployControls = false, bool metadataOnlyDeploy = false,
        ConfigurationBoundaryScenario configurationBoundary = ConfigurationBoundaryScenario.None, bool instanceStatusBoundaries = false)
    {
        bool boundaryScenario = configurationBoundary != ConfigurationBoundaryScenario.None;
        if ((boundaryScenario || instanceStatusBoundaries) && (!browser || rollout || r08 || reviewCorrections || versionWorkflow
            || instanceStatus || firstVersion || hostShutdown || canceledHostRead || newSaveRecovery || releaseConflict || deployControls
            || metadataOnlyDeploy || boundaryScenario && instanceStatusBoundaries))
        {
            throw new ArgumentException("Each browser boundary requires its own native fixture.");
        }
        if (r08 && (!browser || rollout))
        {
            throw new ArgumentException("The R08 continuation requires its own browser fixture, separate from rollout.");
        }

        if (reviewCorrections && (!browser || rollout || r08))
        {
            throw new ArgumentException("The manual-review corrections have their own browser continuation.");
        }
        if (versionWorkflow && (!browser || rollout || r08 || reviewCorrections))
        {
            throw new ArgumentException("The version workflow requires its own browser continuation.");
        }
        if (instanceStatus && (!browser || rollout || r08 || reviewCorrections || versionWorkflow))
        {
            throw new ArgumentException("Instance status requires its own read-only browser continuation.");
        }
        if (firstVersion && (!browser || rollout || r08 || reviewCorrections || versionWorkflow || instanceStatus))
        {
            throw new ArgumentException("First version requires its own empty-binding browser continuation.");
        }
        if (hostShutdown && (browser || rollout || r08 || reviewCorrections || versionWorkflow || instanceStatus || firstVersion))
        {
            throw new ArgumentException("Host shutdown requires its own startup-and-stop fixture entry.");
        }
        if (canceledHostRead && (browser || rollout || r08 || reviewCorrections || versionWorkflow || instanceStatus || firstVersion || hostShutdown))
        {
            throw new ArgumentException("Canceled host read requires its own authenticated startup-and-stop entry.");
        }
        bool configurationAutomation = newSaveRecovery || releaseConflict || deployControls || metadataOnlyDeploy;
        if (configurationAutomation && (!browser || rollout || r08 || reviewCorrections || versionWorkflow || instanceStatus || firstVersion
            || hostShutdown || canceledHostRead || new[] { newSaveRecovery, releaseConflict, deployControls, metadataOnlyDeploy }.Count(value => value) != 1))
        {
            throw new ArgumentException("Each Configuration automation journey requires its own browser fixture.");
        }

        string? manualDirectory = System.Environment.GetEnvironmentVariable("CIS_PORTAL_MANUAL_CONTROL_DIRECTORY");
        int leaseSeconds = 0;
        if (manualDirectory is not null)
        {
            string allowed = Path.Combine(RepositoryRoot(), ".artifacts", "management-portal", "CIS-0029", "WORK-006")
                + Path.DirectorySeparatorChar;
            manualDirectory = Path.GetFullPath(manualDirectory);
            if (!manualDirectory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(manualDirectory)
                || !int.TryParse(System.Environment.GetEnvironmentVariable("CIS_PORTAL_MANUAL_LEASE_SECONDS"), out leaseSeconds)
                || leaseSeconds is < 30 or > 3600 || browser || hostShutdown || canceledHostRead || configurationAutomation)
            {
                throw new InvalidOperationException("Manual portal sessions require an explicit owned directory, a lease of at most one hour and the host-only fixture entry.");
            }
        }
        bool seedConfigurationRollout = rollout || instanceStatus || instanceStatusBoundaries || manualDirectory is not null;
        bool seedSharedConfiguration = reviewCorrections || versionWorkflow || releaseConflict || manualDirectory is not null
            || configurationBoundary is ConfigurationBoundaryScenario.SharedReferenceDiff or ConfigurationBoundaryScenario.SessionStorage;
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
                configuration: PortalConfigurationDeployment(seedConfigurationRollout || deployControls, seedSharedConfiguration, manualDirectory is not null),
                initialInstanceNameA: manualDirectory is null ? "seed-a" : "country-api-eu-01",
                initialInstanceNameB: manualDirectory is null ? "seed-b" : "billing-worker-eu-01");
            HttpClient catalogActor = fixture.Client("operator-a");
            await ManagementConfigurationCatalogTests.Apply(catalogActor,
                await ManagementConfigurationCatalogTests.Catalog(catalogActor), "portal-catalog");
            ManagementPage<ManagementConfigurationDefinition> configurationDefinitions = await ManagementConfigurationCatalogTests.Definitions(catalogActor);
            ManagementConfigurationDefinition definition = configurationDefinitions.Items.Single(row => row.ApplicationId == fixture.ApplicationA && row.Kind == "application" && row.ConfigurationId == "shared.settings");
            ManagementConfigurationDefinition commonDefinition = configurationDefinitions.Items.Single(row => row.Kind == "common" && row.ConfigurationId == "shared.settings");
            ManagementConfigurationDefinition readonlyDefinition = configurationDefinitions.Items.Single(row => row.ApplicationId == fixture.ApplicationB && row.Kind == "application" && row.ConfigurationId == "shared.settings");
            ManagementConfigurationDefinition? manualDefinition = !seedSharedConfiguration ? null : configurationDefinitions.Items.Single(row => row.ApplicationId == fixture.ApplicationA && row.ConfigurationId == ManualSharedConfiguration);
            ManagementConfigurationDefinition? manualCommon = !seedSharedConfiguration ? null : configurationDefinitions.Items.Single(row => row.Kind == "common" && row.ConfigurationId == ManualSharedConfiguration);
            ManagementConfigurationDefinition? billingDefinition = manualDirectory is null ? null : configurationDefinitions.Items.Single(row => row.ApplicationId == fixture.ApplicationB && row.ConfigurationId == ManualBillingConfiguration);
            ManagementConfigurationDefinition? billingCommon = manualDirectory is null ? null : configurationDefinitions.Items.Single(row => row.Kind == "common" && row.ConfigurationId == ManualBillingConfiguration);
            if (manualDefinition is not null && manualCommon is not null)
            {
                await SeedManualSharedConfigurationAsync(fixture, catalogActor, manualDefinition, manualCommon);
            }
            ConfigurationBoundarySeed? boundarySeed = boundaryScenario
                ? await SeedConfigurationBoundaryAsync(fixture, catalogActor, configurationBoundary, manualDefinition, manualCommon) : null;
            ManagementConfigurationAssignmentMap? repairBaseline = reviewCorrections
                ? await ReadConfigurationRepairsMapAsync(catalogActor, fixture.ApplicationA) : null;
            ManagementConfigurationAssignmentMap configurationBaseline = await ManagementConfigurationDistributionTests.Map(catalogActor, fixture.ApplicationA);
            Assert.IsNull(configurationBaseline.DefaultReleaseId);
            Assert.IsNull(configurationBaseline.Generation);
            Assert.IsEmpty(configurationBaseline.Overrides);
            ConfigurationDeploySeed? configurationDeploySeed = deployControls || metadataOnlyDeploy
                ? await SeedConfigurationDeployAsync(fixture, catalogActor, definition, commonDefinition, readonlyDefinition, metadataOnlyDeploy)
                : null;
            string secondRegistration = manualDirectory is null ? await SeedInventoryAsync(fixture) : await SeedManualInventoryAsync(fixture);
            string[] adoptionObservations = seedConfigurationRollout ? await SeedPortalAdoptionObservationsAsync(fixture, catalogActor, configurationBaseline) : [];
            await fixture.StopReplicasAsync();
            int issuerPort = UnusedPort();
            string authority = "https://localhost:" + issuerPort.ToString(CultureInfo.InvariantCulture);
            string issuer = authority + "/" + Tenant + "/v2.0";
            var metadata = new OpenIdConnectConfiguration { Issuer = issuer };
            metadata.SigningKeys.Add(new RsaSecurityKey(signing) { KeyId = "rm09-portal-owned-key" });
            // Seed through the existing temporary native subject in manual sessions too.
            // Its write/read grants are removed before the ordinary A/B sign-in chooser opens.
            bool seedLookup = !hostShutdown && !canceledHostRead;
            string? manualBillingReleaseId = null;
            Func<ManagementSecurityOptions, ManagementSecurityOptions> selectSecurity = source => new()
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
                        clientApplicationId: PortalClient, actorKind: SecurityActorKind.User),
                    new(new(issuer, LookupOperator), "management-portal", EnvironmentOptions.EnvironmentName,
                        clientApplicationId: PortalClient, actorKind: SecurityActorKind.User),
                    .. manualDirectory is null ? Array.Empty<SecurityIdentityMapping>() : new[]
                    {
                        new SecurityIdentityMapping(new(issuer, ContinuityOperator), "management-portal", EnvironmentOptions.EnvironmentName,
                            clientApplicationId: PortalClient, actorKind: SecurityActorKind.User)
                    }
                ],
                Grants =
                [
                    ReadGrant(issuer, OperatorA, fixture.ApplicationA),
                    .. boundarySeed?.PredeploymentApplicationId is not string predeploymentApplication ? Array.Empty<CapabilityGrant>()
                        : [ReadGrant(issuer, OperatorA, predeploymentApplication), .. R08ApplicationGrants(issuer, predeploymentApplication)],
                    .. manualDirectory is null ? Array.Empty<CapabilityGrant>() : [ReadGrant(issuer, OperatorA, fixture.ApplicationB)],
                    ReadGrant(issuer, OperatorB, fixture.ApplicationB),
                    .. manualDirectory is null ? Array.Empty<CapabilityGrant>() : [ReadGrant(issuer, ContinuityOperator, fixture.ApplicationA)],
                    MetadataGrant(issuer, OperatorA, fixture.ApplicationA, "management.applications.metadata.write"),
                    MetadataGrant(issuer, OperatorA, fixture.ApplicationA, "management.operations.read"),
                    .. manualDirectory is null ? Array.Empty<CapabilityGrant>() : (CapabilityGrant[])
                    [
                        MetadataGrant(issuer, OperatorA, fixture.ApplicationB, "management.applications.metadata.write"),
                        MetadataGrant(issuer, OperatorA, fixture.ApplicationB, "management.operations.read"),
                        .. R08ApplicationGrants(issuer, fixture.ApplicationB),
                        .. ConfigurationGrants(issuer, OperatorA, readonlyDefinition, commonDefinition, write: true, content: true, assignment: true)
                    ],
                    MetadataGrant(issuer, LookupOperator, fixture.ApplicationA, "management.operations.read"),
                    .. seedLookup ? new[]
                    {
                        ReadGrant(issuer, LookupOperator, fixture.ApplicationA),
                        MetadataGrant(issuer, LookupOperator, fixture.ApplicationA, "management.applications.metadata.write")
                    } : Array.Empty<CapabilityGrant>(),
                    .. ConfigurationGrants(issuer, OperatorA, definition, commonDefinition, write: true,
                        content: !rollout && !metadataOnlyDeploy || manualDirectory is not null, assignment: seedConfigurationRollout || deployControls || metadataOnlyDeploy),
                    .. manualDefinition is null || manualCommon is null ? Array.Empty<CapabilityGrant>()
                        : ConfigurationGrants(issuer, OperatorA, manualDefinition, manualCommon, write: true, content: true, assignment: true),
                    .. r08 || versionWorkflow || newSaveRecovery || manualDirectory is not null ? R08ApplicationGrants(issuer, fixture.ApplicationA) : Array.Empty<CapabilityGrant>(),
                    .. billingDefinition is null || billingCommon is null ? Array.Empty<CapabilityGrant>()
                        : ConfigurationGrants(issuer, OperatorA, billingDefinition, billingCommon, write: true, content: true, assignment: true),
                    .. ConfigurationGrants(issuer, OperatorB, readonlyDefinition, commonDefinition, write: false, content: false),
                    .. billingDefinition is null || billingCommon is null ? Array.Empty<CapabilityGrant>()
                        : ConfigurationGrants(issuer, OperatorB, billingDefinition, billingCommon, write: false, content: true),
                    .. manualBillingReleaseId is null ? Array.Empty<CapabilityGrant>() : new[]
                    {
                        ResourceGrant(issuer, OperatorB, ManagementConfigurationCapabilities.DefinitionsRead,
                            ManagementConfigurationCapabilities.ReleaseResource, manualBillingReleaseId)
                    },
                    .. configurationDeploySeed is null ? Array.Empty<CapabilityGrant>() : new[]
                    {
                        ResourceGrant(issuer, OperatorB, ManagementConfigurationCapabilities.DefinitionsRead,
                            ManagementConfigurationCapabilities.ReleaseResource, configurationDeploySeed.ReadonlyReleaseId)
                    },
                    .. !seedLookup || billingDefinition is null || billingCommon is null ? Array.Empty<CapabilityGrant>() : (CapabilityGrant[])
                    [
                        ReadGrant(issuer, LookupOperator, fixture.ApplicationB),
                        MetadataGrant(issuer, LookupOperator, fixture.ApplicationB, "management.applications.metadata.write"),
                        MetadataGrant(issuer, LookupOperator, fixture.ApplicationB, "management.operations.read"),
                        .. ConfigurationGrants(issuer, LookupOperator, billingDefinition, billingCommon, write: true, content: true, assignment: true)
                    ],
                    .. seedLookup ? ConfigurationGrants(issuer, LookupOperator, definition, commonDefinition, write: true, content: true, assignment: seedConfigurationRollout)
                        : new[]
                        {
                            ResourceGrant(issuer, LookupOperator, ManagementConfigurationCapabilities.OperationsRead,
                                ManagementConfigurationCapabilities.SourceResource, definition.SourceId),
                            ResourceGrant(issuer, LookupOperator, ManagementConfigurationCapabilities.OperationsRead,
                                ManagementConfigurationCapabilities.BindingResource, definition.BindingId!)
                        },
                    .. rollout || instanceStatus ? new[] { ResourceGrant(issuer, LookupOperator, ManagementConfigurationCapabilities.AdoptionRead,
                        "management.application", definition.ApplicationId!) } : Array.Empty<CapabilityGrant>()
                ]
            };
            await fixture.StartReplicaAsync(selectSecurity);
            string? lookupOriginalKey = null;
            ConfigurationLookupProof? configurationLookup = null;
            ConfigurationRolloutSeed? rolloutSeed = null;
            if (seedLookup)
            {
                lookupOriginalKey = await SeedLookupOriginalAsync(fixture, signing, issuer);
                if (!firstVersion && !configurationAutomation && !boundaryScenario)
                {
                    configurationLookup = await SeedConfigurationLookupAsync(fixture, signing, issuer, definition);
                    if (seedConfigurationRollout)
                    {
                        rolloutSeed = await SeedConfigurationRolloutAsync(fixture, signing, issuer, definition, configurationLookup);
                    }
                }
                if (billingDefinition is not null && billingCommon is not null)
                {
                    manualBillingReleaseId = await SeedManualReviewStateAsync(fixture, signing, issuer,
                        definition, commonDefinition, billingDefinition, billingCommon);
                }
                await fixture.StopReplicasAsync();
                seedLookup = false;
                await fixture.StartReplicaAsync(selectSecurity);
            }
            IReadOnlyList<InstanceStatusFingerprint>? instanceStatusBaseline = instanceStatus
                ? await ReadInstanceStatusFingerprintsAsync(fixture) : null;
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
            InstanceStatusBoundariesSeed? statusBoundariesSeed = instanceStatusBoundaries
                ? await SeedInstanceStatusBoundariesAsync(fixture, rolloutSeed!, adoptionObservations) : null;
            IReadOnlyList<InstanceStatusFingerprint>? statusBoundariesBaseline = instanceStatusBoundaries
                ? await ReadInstanceStatusBoundaryFingerprintsAsync(fixture) : null;
            byte[] input = JsonSerializer.SerializeToUtf8Bytes(new
            {
                issuerPort,
                authority,
                tenant = Tenant,
                portalClient = PortalClient,
                audience = Audience,
                operatorA = OperatorA,
                operatorB = OperatorB,
                lookupOperator = LookupOperator,
                continuityOperator = manualDirectory is null ? null : ContinuityOperator,
                lookupOriginalKey,
                configurationId = definition.ConfigurationId,
                configurationSchema = definition.SchemaVersion,
                configurationSourceId = definition.SourceId,
                configurationCommonSourceId = commonDefinition.SourceId,
                configurationBindingId = definition.BindingId,
                configurationLookup = configurationLookup is null ? null : new
                {
                    publicationKey = configurationLookup.PublicationKey,
                    releaseKey = configurationLookup.ReleaseKey
                },
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
                browser,
                r08,
                reviewCorrections,
                versionWorkflow,
                instanceStatus,
                instanceStatusBoundaries = statusBoundariesSeed is null ? (JsonElement?)null
                    : JsonSerializer.SerializeToElement(statusBoundariesSeed, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                configurationBoundary = boundarySeed is null ? null : new
                {
                    scenario = configurationBoundary.ToString(),
                    applicationId = boundarySeed.TargetApplicationId,
                    baselineReleaseId = boundarySeed.BaselineReleaseId,
                    candidateReleaseId = boundarySeed.CandidateReleaseId
                },
                firstVersion,
                hostShutdown,
                canceledHostRead,
                newSaveRecovery,
                releaseConflict,
                deployControls,
                metadataOnlyDeploy,
                configurationDeploy = configurationDeploySeed is null ? null : new
                {
                    metadataOnly = metadataOnlyDeploy,
                    baselineReleaseId = configurationDeploySeed.BaselineReleaseId,
                    candidateReleaseId = configurationDeploySeed.CandidateReleaseId,
                    financeReleaseId = configurationDeploySeed.FinanceReleaseId,
                    readonlyReleaseId = configurationDeploySeed.ReadonlyReleaseId
                },
                rollout = rolloutSeed is null ? null : new
                {
                    baselineReleaseId = rolloutSeed.BaselineReleaseId,
                    candidateReleaseId = rolloutSeed.CandidateReleaseId,
                    financeReleaseId = rolloutSeed.FinanceReleaseId,
                    assignmentKey = rolloutSeed.AssignmentKey,
                    releaseCount = rolloutSeed.ReleaseCount,
                    publicationCount = rolloutSeed.PublicationCount
                },
                adoptionObservations,
                manual = manualDirectory is null ? null : new
                {
                    controlDirectory = manualDirectory,
                    leaseSeconds,
                    openBrowser = System.Environment.GetEnvironmentVariable("CIS_PORTAL_MANUAL_OPEN_BROWSER") == "true"
                }
            });
            string inputFile = Path.Combine(files.Path, "portal-fixture.json");
            await PrivateContainerDirectory.WriteNewAsync(inputFile, input);
            CryptographicOperations.ZeroMemory(input);
            ProcessResult result = await RunNodeAsync(root,
                [Path.Combine(portal, "tests", "native-host-consumer.mjs"), files.Path],
                new Dictionary<string, string?> { ["NODE_EXTRA_CA_CERTS"] = Path.Combine(files.Path, "root.pem") },
                manualDirectory is null ? null : TimeSpan.FromSeconds(leaseSeconds + 180));
            using JsonDocument report = JsonDocument.Parse(result.StandardOutput);
            Assert.IsTrue(report.RootElement.GetProperty("hostStopped").GetBoolean());
            Assert.IsTrue(report.RootElement.GetProperty("socketsDrained").GetBoolean());
            if (hostShutdown)
            {
                VerifyIncompleteTlsHostShutdownReport(report.RootElement);
            }
            else if (canceledHostRead)
            {
                VerifyCanceledHostReadReport(report.RootElement);
            }
            else if (manualDirectory is null)
            {
                Assert.IsTrue(report.RootElement.GetProperty("nativeOidc").GetBoolean());
                Assert.IsTrue(report.RootElement.GetProperty("sessionsRealPostgres").GetBoolean());
                Assert.IsTrue(report.RootElement.GetProperty("tokensStayedServerSide").GetBoolean());
                Assert.IsTrue(report.RootElement.GetProperty("cases").GetArrayLength() >= 12);
                Assert.AreEqual(browser, report.RootElement.GetProperty("browserChecked").GetBoolean());
                await VerifyMetadataEffectsAsync(fixture, report.RootElement.GetProperty("metadataAppliedKeys"),
                    report.RootElement.GetProperty("metadataConflictKeys"), lookupOriginalKey!, issuer);
                if (browser)
                {
                    if (boundaryScenario)
                    {
                        await VerifyConfigurationBoundaryAsync(fixture, report.RootElement.GetProperty("configurationBoundary"),
                            boundarySeed!, configurationBoundary, issuer);
                    }
                    else if (instanceStatusBoundaries)
                    {
                        await VerifyInstanceStatusBoundariesAsync(fixture, report.RootElement.GetProperty("instanceStatusBoundaries"),
                            statusBoundariesBaseline!, statusBoundariesSeed!);
                    }
                    else if (newSaveRecovery)
                    {
                        await VerifyConfigurationSaveRecoveryAsync(fixture, report.RootElement.GetProperty("configurationSave"), issuer);
                    }
                    else if (releaseConflict)
                    {
                        await VerifyConfigurationReleaseConflictAsync(fixture, report.RootElement.GetProperty("releaseConflict"), manualDefinition!, manualCommon!, issuer);
                    }
                    else if (deployControls || metadataOnlyDeploy)
                    {
                        await VerifyConfigurationDeployEffectsAsync(fixture, report.RootElement.GetProperty("configurationDeploy"), configurationDeploySeed!, definition, readonlyDefinition, issuer);
                    }
                    else if (firstVersion)
                    {
                        await VerifyFirstConfigurationVersionAsync(fixture, report.RootElement.GetProperty("firstVersion"),
                            definition, commonDefinition, configurationBaseline, issuer);
                    }
                    else if (instanceStatus)
                    {
                        await VerifyInstanceStatusReadOnlyAsync(fixture, report.RootElement.GetProperty("instanceStatus"),
                            instanceStatusBaseline!, definition, adoptionObservations);
                    }
                    else if (versionWorkflow)
                    {
                        await VerifyVersionWorkflowEffectsAsync(fixture, report.RootElement.GetProperty("versionWorkflow"),
                            manualDefinition!, manualCommon!, issuer);
                    }
                    else if (reviewCorrections)
                    {
                        await VerifyConfigurationRepairsEffectsAsync(fixture, report.RootElement.GetProperty("configurationRepairs"),
                            manualDefinition!, manualCommon!, repairBaseline!, issuer);
                    }
                    else if (r08)
                    {
                        await VerifyR08ConfigurationEffectsAsync(fixture, report.RootElement.GetProperty("r08"), configurationLookup!, definition, configurationBaseline, issuer);
                    }
                    else if (rollout)
                    {
                        await VerifyConfigurationRolloutEffectsAsync(fixture, report.RootElement.GetProperty("rollout"), rolloutSeed!, definition, issuer);
                    }
                    else
                    {
                        await VerifyConfigurationEffectsAsync(fixture, report.RootElement.GetProperty("configuration"), configurationLookup!, definition, configurationBaseline, issuer);
                    }
                }
            }
            else
            {
                Assert.IsTrue(report.RootElement.GetProperty("manualSession").GetBoolean());
            }
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
            if (manualDirectory is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(manualDirectory, "fixture-cleanup.json"), JsonSerializer.Serialize(new
                {
                    completedUtc = DateTimeOffset.UtcNow,
                    fixtureOwnersDisposed = cleanupConfirmed,
                    note = cleanupConfirmed
                        ? "Per-test Management/schema/role/private-key owners settled. IntegrationServices containers close with the test assembly owner."
                        : "Native cleanup is unconfirmed; private files, database roles/schema and fixture custody remain retained."
                }));
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
        string pastRegistration = Guid.NewGuid().ToString("D");
        using (HttpResponseMessage registration = await ManagementServiceTests.Put(workload,
            "registrations/" + pastRegistration, new ManagementRegistrationRequest(1, "seed-a-previous", observed, "0.9.0")))
        {
            Assert.AreEqual(HttpStatusCode.OK, registration.StatusCode);
        }
        using (HttpResponseMessage closed = await workload.DeleteAsync("registrations/" + pastRegistration))
        {
            Assert.AreEqual(HttpStatusCode.OK, closed.StatusCode);
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

    private static CapabilityGrant MetadataGrant(string issuer, string subject, string applicationId, string capability)
        => new(ManagementHostFixture.Profile, capability, EnvironmentOptions.ApplicationName,
            EnvironmentOptions.EnvironmentName, subject: new(issuer, subject), resourceSelection: CapabilityResourceSelection.ExactResource,
            resourceType: "management.application", resourceIdentifier: applicationId);

    private static CapabilityGrant ResourceGrant(string issuer, string subject, string capability, string type, string? id)
        => new(ManagementHostFixture.Profile, capability, EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
            subject: new(issuer, subject), resourceSelection: id is null ? CapabilityResourceSelection.AllOfType : CapabilityResourceSelection.ExactResource,
            resourceType: type, resourceIdentifier: id);

    private static IEnumerable<CapabilityGrant> ConfigurationGrants(string issuer, string subject, ManagementConfigurationDefinition application,
        ManagementConfigurationDefinition common, bool write, bool content, bool assignment = false)
    {
        foreach (string id in new[] { application.SourceId, common.SourceId })
        {
            yield return ResourceGrant(issuer, subject, ManagementConfigurationCapabilities.DefinitionsRead, ManagementConfigurationCapabilities.SourceResource, id);
            if (content)
            {
                yield return ResourceGrant(issuer, subject, ManagementConfigurationCapabilities.ContentRead, ManagementConfigurationCapabilities.SourceResource, id);
            }
            if (write)
            {
                foreach (string capability in new[] { ManagementConfigurationCapabilities.Validate, ManagementConfigurationCapabilities.Publish, ManagementConfigurationCapabilities.OperationsRead })
                {
                    yield return ResourceGrant(issuer, subject, capability, ManagementConfigurationCapabilities.SourceResource, id);
                }
            }
        }
        yield return ResourceGrant(issuer, subject, ManagementConfigurationCapabilities.DefinitionsRead, ManagementConfigurationCapabilities.BindingResource, application.BindingId!);
        if (write)
        {
            foreach (string capability in new[] { ManagementConfigurationCapabilities.Preview, ManagementConfigurationCapabilities.ReleaseCreate, ManagementConfigurationCapabilities.OperationsRead })
            {
                yield return ResourceGrant(issuer, subject, capability, ManagementConfigurationCapabilities.BindingResource, application.BindingId!);
            }
            yield return ResourceGrant(issuer, subject, ManagementConfigurationCapabilities.DefinitionsRead, ManagementConfigurationCapabilities.ReleaseResource, null);
            if (assignment)
            {
                yield return ResourceGrant(issuer, subject, ManagementConfigurationCapabilities.AssignmentWrite, ManagementConfigurationCapabilities.BindingResource, application.BindingId!);
                yield return ResourceGrant(issuer, subject, ManagementConfigurationCapabilities.AdoptionRead, "management.application", application.ApplicationId!);
            }
        }
    }

    private static async Task<string> SeedLookupOriginalAsync(ManagementHostFixture fixture, RSA signing, string issuer)
    {
        string access = LookupToken(signing, issuer);
        HttpClient actor = fixture.Client(subject: null, replica: fixture.Origins.Count - 1, explicitToken: access);
        using HttpResponseMessage read = await actor.GetAsync("applications/" + fixture.ApplicationA);
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
        ManagementApplication original = await ManagementServiceTests.Decode<ManagementApplication>(read);
        string key = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Put, "applications/" + fixture.ApplicationA + "/metadata")
        {
            Content = new ByteArrayContent(ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, original.Metadata.DisplayName)))
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("If-Match", original.MetadataETag);
        using HttpResponseMessage changed = await actor.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        ManagementMetadataResult result = await ManagementServiceTests.Decode<ManagementMetadataResult>(changed);
        Assert.AreEqual("committed", result.OriginalCompletion);
        Assert.AreEqual("applied", result.Result!.Decision);
        return key;
    }

    private static string LookupToken(RSA signing, string issuer)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>
            {
                ["tid"] = Tenant,
                ["oid"] = LookupOperator,
                ["azp"] = PortalClient,
                ["sub"] = "pairwise-" + LookupOperator,
                ["ver"] = "2.0",
                ["scp"] = "management.operator"
            },
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new(new RsaSecurityKey(signing) { KeyId = "rm09-portal-owned-key" }, SecurityAlgorithms.RsaSha256)
        });

    private static ManagementConfigurationOptions PortalConfigurationDeployment(bool rollout, bool manual = false, bool reviewExamples = false)
    {
        ManagementConfigurationManifest original = ManagementConfigurationCatalogTests.Deployment(greetingDefault: "PORTAL_PRIVATE_DEFAULT").Manifest;
        ManagementConfigurationSchema definition = original.Definitions.Single();
        var shape = new ManagementConfigurationShape("object", false,
            [new("Greeting", true, new("string", false)), new("Enabled", true, new("boolean", false)), new("Count", true, new("integer", false))]);
        ManagementConfigurationManifest manifest = original with
        {
            Definitions = [definition with { Shape = shape, DefaultsJson = JsonSerializer.Serialize(new { Greeting = "PORTAL_PRIVATE_DEFAULT", Enabled = true, Count = 0 }) }],
            Cohorts = rollout ? [.. original.Cohorts, new(ManagementHostFixture.Issuer, "workload-finance", "managed-a", "Development", "finance")] : original.Cohorts
        };
        if (manual)
        {
            manifest = WithManualSharedConfiguration(manifest);
        }
        if (reviewExamples)
        {
            manifest = WithManualReviewExamples(manifest);
        }
        return new ManagementConfigurationOptions(JsonSerializer.Serialize(manifest, ManagementConfigurationOptions.Json), 32L * 1024 * 1024);
    }

    private sealed record ConfigurationLookupProof(string PublicationKey, string ReleaseKey);

    private static async Task<ConfigurationLookupProof> SeedConfigurationLookupAsync(ManagementHostFixture fixture, RSA signing,
        string issuer, ManagementConfigurationDefinition definition)
    {
        string access = LookupToken(signing, issuer);
        HttpClient actor = fixture.Client(subject: null, replica: fixture.Origins.Count - 1, explicitToken: access);
        ManagementConfigurationSource source = await ManagementConfigurationCatalogTests.Source(actor, definition.SourceId);
        string publicationKey = Guid.NewGuid().ToString("D");
        using var publication = new HttpRequestMessage(HttpMethod.Post, "configuration/sources/" + definition.SourceId + "/publications")
        {
            Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationPublicationCommand(1,
                "{\"Greeting\":\"Native C original\",\"Enabled\":false}", "Own original before read/write grants are removed")))
        };
        publication.Content.Headers.ContentType = new("application/json");
        publication.Headers.Add("Idempotency-Key", publicationKey);
        publication.Headers.Add("If-Match", source.ETag);
        using HttpResponseMessage changed = await actor.SendAsync(publication);
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        ManagementConfigurationOperationResult result = await ManagementConfigurationCatalogTests.Decode<ManagementConfigurationOperationResult>(changed);
        Assert.AreEqual("committed", result.OriginalCompletion);
        string releaseKey = Guid.NewGuid().ToString("D");
        ManagementConfigurationAssignmentMap map = await ManagementConfigurationDistributionTests.Map(actor, fixture.ApplicationA);
        using var release = new HttpRequestMessage(HttpMethod.Post, "applications/" + fixture.ApplicationA + "/configuration/shared.settings/releases?schemaVersion=1")
        {
            Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationReleaseCommand(1,
                new("absent"), new("present", result.Result!.PublicationId), "Own release original before read/write grants are removed")))
        };
        release.Content.Headers.ContentType = new("application/json");
        release.Headers.Add("Idempotency-Key", releaseKey);
        release.Headers.Add("If-Match", map.ETag);
        using HttpResponseMessage released = await actor.SendAsync(release);
        Assert.AreEqual(HttpStatusCode.OK, released.StatusCode);
        return new(publicationKey, releaseKey);
    }

    private static async Task VerifyMetadataEffectsAsync(ManagementHostFixture fixture, JsonElement keys, JsonElement conflictKeys, string lookupOriginalKey, string issuer)
    {
        string[] applied = keys.EnumerateArray().Select(item => item.GetString()!).ToArray();
        string[] rejected = conflictKeys.EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.IsTrue(applied.Length is >= 2 and <= 4);
        Assert.IsTrue(rejected.Length is >= 1 and <= 2);
        Assert.AreEqual(applied.Length, applied.Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(rejected.Length, rejected.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(applied.Concat(rejected).All(key => Guid.TryParseExact(key, "D", out _)));
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT metadata_revision FROM \"{fixture.Options.Persistence.Schema}\".management_application WHERE application_id=@application"))
        {
            command.Parameters.AddWithValue("application", Guid.Parse(fixture.ApplicationA));
            Assert.AreEqual(2L + applied.Length, (long)(await command.ExecuteScalarAsync())!, "Each successful original key has exactly one metadata effect, including lost-response recovery and the seeded same-actor lookup.");
        }
        var audits = new List<AuditRecord>();
        var codec = new AuditRecordCodec();
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY created_at,record_id LIMIT 257"))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                using JsonDocument envelope = JsonDocument.Parse(reader.GetFieldValue<byte[]>(0));
                byte[] payload = Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText());
                Assert.IsTrue(codec.TryDecode(payload, out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
                if (record!.OperationReference?.OperationName == "management.metadata.replace")
                {
                    audits.Add(record);
                }
                Assert.IsTrue(audits.Count <= 16, "This named fixture has only the bounded metadata commands in its consumer.");
            }
        }
        Assert.AreEqual(applied.Length + rejected.Length + 1, audits.Count);
        foreach (string key in applied)
        {
            AuditRecord audit = audits.Single(item => item.OperationReference!.Key == key);
            Assert.AreEqual(AuditOutcome.Succeeded, audit.Outcome);
            Assert.AreEqual(AuditActorKind.User, audit.Actor.Kind);
            Assert.AreEqual(issuer, audit.Actor.Subject!.Issuer);
            Assert.AreEqual(OperatorA, audit.Actor.Subject.Identifier);
            Assert.AreEqual(fixture.ApplicationA, audit.Target!.ResourceIdentifier);
        }
        foreach (string key in rejected)
        {
            Assert.AreEqual(AuditOutcome.Rejected, audits.Single(item => item.OperationReference!.Key == key).Outcome);
        }
        AuditRecord lookup = audits.Single(item => item.OperationReference!.Key == lookupOriginalKey);
        Assert.AreEqual(AuditOutcome.Succeeded, lookup.Outcome);
        Assert.AreEqual(LookupOperator, lookup.Actor.Subject!.Identifier);
    }

    private static async Task VerifyConfigurationEffectsAsync(ManagementHostFixture fixture, JsonElement proof, ConfigurationLookupProof lookup,
        ManagementConfigurationDefinition definition, ManagementConfigurationAssignmentMap baseline, string issuer)
    {
        string publicationKey = proof.GetProperty("publicationKey").GetString()!;
        string digest = proof.GetProperty("draftSha256").GetString()!;
        string publicationId = proof.GetProperty("publicationId").GetString()!;
        JsonElement[] releases = proof.GetProperty("releases").EnumerateArray().ToArray();
        Assert.IsTrue(Guid.TryParseExact(publicationKey, "D", out _) && Guid.TryParseExact(publicationId, "D", out _));
        Assert.IsTrue(ManagementConfigurationWire.IsSha256(digest));
        Assert.HasCount(2, releases, "One release follows publication; another deliberately reuses existing references without publication.");
        Assert.AreEqual(2L, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_publication"));
        Assert.AreEqual(3L, await ManagementConfigurationCatalogTests.Count(fixture, fixture.Options.Persistence.Schema, "configuration_release"));
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT source_id,payload,sha256 FROM \"{fixture.Options.Persistence.Schema}\".configuration_publication WHERE publication_id=@id"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(publicationId));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(definition.SourceId), reader.GetGuid(0));
            string raw = new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(1));
            Assert.AreEqual(digest, ManagementConfigurationWire.ComputePayloadSha256(raw));
            Assert.AreEqual(digest, reader.GetString(2));
            Assert.IsTrue(raw.Contains("9007199254740993", StringComparison.Ordinal));
            Assert.IsTrue(raw.Contains("\\u00e9", StringComparison.Ordinal));
            using JsonDocument payload = JsonDocument.Parse(raw);
            JsonElement value = payload.RootElement;
            Assert.AreEqual(JsonValueKind.Object, value.ValueKind);
            Assert.AreEqual(JsonValueKind.False, value.GetProperty("Enabled").ValueKind);
            Assert.AreEqual("false", value.GetProperty("Enabled").GetRawText());
            Assert.AreEqual(JsonValueKind.Number, value.GetProperty("Count").ValueKind);
            Assert.AreEqual("9007199254740993", value.GetProperty("Count").GetRawText());
            Assert.AreEqual("9007199254740993", value.GetProperty("Unrelated").GetRawText());
            Assert.AreEqual(JsonValueKind.Number, value.GetProperty("Zero").ValueKind);
            Assert.AreEqual("0", value.GetProperty("Zero").GetRawText());
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("Optional").ValueKind);
            Assert.AreEqual("\"\\u00e9\"", value.GetProperty("Escaped").GetRawText());
            Assert.AreEqual("é", value.GetProperty("Escaped").GetString());
            Assert.AreEqual("Portal exact 🌍 e\u0301", value.GetProperty("Greeting").GetString());
            Assert.IsFalse(await reader.ReadAsync());
        }
        foreach (JsonElement release in releases)
        {
            string id = release.GetProperty("releaseId").GetString()!;
            Assert.IsTrue(Guid.TryParseExact(id, "D", out _));
            await using NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT binding_id,common_publication_id,application_publication_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_release WHERE release_id=@id");
            command.Parameters.AddWithValue("id", Guid.Parse(id));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(Guid.Parse(definition.BindingId!), reader.GetGuid(0));
            Assert.IsTrue(reader.IsDBNull(1));
            Assert.AreEqual(Guid.Parse(publicationId), reader.GetGuid(2));
        }
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT revision,generation,default_release_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_assignment WHERE binding_id=@id"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(baseline.BindingId));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(baseline.ETag, "\"configuration.map." + reader.GetInt64(0).ToString(CultureInfo.InvariantCulture) + "\"");
            Assert.AreEqual(baseline.Generation, reader.IsDBNull(1) ? null : reader.GetInt64(1).ToString(CultureInfo.InvariantCulture));
            Assert.AreEqual(baseline.DefaultReleaseId, reader.IsDBNull(2) ? null : reader.GetGuid(2).ToString("D"));
            Assert.IsFalse(await reader.ReadAsync());
        }
        var actualOverrides = new List<ManagementConfigurationOverride>();
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT cohort_id,release_id FROM \"{fixture.Options.Persistence.Schema}\".configuration_override WHERE binding_id=@id ORDER BY cohort_id COLLATE \"C\" LIMIT @limit"))
        {
            command.Parameters.AddWithValue("id", Guid.Parse(baseline.BindingId));
            command.Parameters.AddWithValue("limit", baseline.Overrides.Count + 1);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                actualOverrides.Add(new(reader.GetString(0), reader.GetGuid(1).ToString("D")));
            }
        }
        CollectionAssert.AreEqual(baseline.Overrides.ToArray(), actualOverrides.ToArray(),
            "Publication/release must preserve every exact cohort entry; one additional row also fails this bounded comparison.");
        var audits = new List<AuditRecord>();
        var codec = new AuditRecordCodec();
        await using (NpgsqlCommand command = fixture.Source.CreateCommand($"SELECT envelope FROM \"{fixture.Options.MessagingSchema}\".outbox ORDER BY created_at,record_id LIMIT 257"))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                using JsonDocument envelope = JsonDocument.Parse(reader.GetFieldValue<byte[]>(0));
                Assert.IsTrue(codec.TryDecode(Encoding.UTF8.GetBytes(envelope.RootElement.GetProperty("payload").GetRawText()), out AuditRecord? record, out AuditWireFailure failure), failure.ToString());
                if (record!.OperationReference?.OperationName is "management.configuration.source.publish" or "management.configuration.release.create")
                {
                    audits.Add(record);
                }
                Assert.IsTrue(audits.Count <= 16);
            }
        }
        Assert.HasCount(5, audits, "Only two seeded originals and three deliberate browser originals may produce these effects/Audits.");
        foreach (string key in new[] { publicationKey }.Concat(releases.Select(item => item.GetProperty("key").GetString()!)))
        {
            AuditRecord audit = audits.Single(item => item.OperationReference!.Key == key);
            Assert.AreEqual(AuditOutcome.Succeeded, audit.Outcome);
            Assert.AreEqual(AuditActorKind.User, audit.Actor.Kind);
            Assert.AreEqual(issuer, audit.Actor.Subject!.Issuer);
            Assert.AreEqual(OperatorA, audit.Actor.Subject.Identifier);
            Assert.AreEqual(key == publicationKey ? definition.SourceId : definition.BindingId, audit.Target!.ResourceIdentifier);
        }
        foreach (string key in new[] { lookup.PublicationKey, lookup.ReleaseKey })
        {
            Assert.AreEqual(LookupOperator, audits.Single(item => item.OperationReference!.Key == key).Actor.Subject!.Identifier);
        }
    }

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

    internal static async Task<ProcessResult> RunNodeAsync(string directory, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?> environment,
        TimeSpan? timeout = null)
    {
        string node = System.Environment.GetEnvironmentVariable("CIS_TEST_NODE_EXECUTABLE") ?? "node";
        ProcessResult result = await ProcessRunner.RunAsync(node, arguments, directory, timeout ?? TimeSpan.FromSeconds(180), environment,
            maximumCapturedBytesPerStream: 65536);
        Assert.AreEqual(0, result.ExitCode, "Native portal consumer failed: " + result.StandardOutput + result.StandardError);
        Assert.IsTrue(result.OutputDrained);
        return result;
    }
}
