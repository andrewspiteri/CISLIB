using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CIS.Management;
using CIS.Management.Service.Security;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Entra;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using CIS.Tests.IntegrationTests.RestClient;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CIS.Tests.IntegrationTests.Management;

// These cases select the actual product composition over the existing owned service fixtures.
// Isolated signing metadata and newly owned certificates are not an external Entra/cert-manager proof.
[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementSecurityProfileTests
{
    private const string CertificateDomain = "rm07-owned-client-issuer";
    private const string ClientIdentity = "https://identity.example.test/applications/managed-a/environments/development";
    private const string DeniedIdentity = "https://identity.example.test/applications/denied/environments/development";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string ObjectId = "22222222-2222-2222-2222-222222222222";
    private const string ClientId = "33333333-3333-3333-3333-333333333333";
    private const string OtherId = "44444444-4444-4444-4444-444444444444";
    private const string EntraIssuer = "https://login.microsoftonline.com/" + Tenant + "/v2.0";
    private const string EntraAudience = "api://rm07-management";

    [TestMethod]
    [DataRow("certificate")]
    [DataRow("local")]
    public async Task ConfiguredCertificateProfilesUseActualClientTrustAndMappedIdentityWithoutHeaderAuthority(string mode)
    {
        using var files = new PrivateContainerDirectory();
        using X509Certificate2 trusted = Authority();
        using X509Certificate2 unrelated = Authority();
        string roots = Path.Combine(files.Path, "client-root.pem");
        await PrivateContainerDirectory.WriteNewAsync(roots, Encoding.UTF8.GetBytes(trusted.ExportCertificatePem()));
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(start: false);
        await fixture.StartReplicaAsync(options => CertificateOptions(options, roots, mode),
            hostEnvironment: mode == "local" ? Environments.Development : null);

        using RestHttpNativeCertificate allowedLeaf = Leaf(trusted, ClientIdentity);
        using RestHttpNativeCertificate deniedLeaf = Leaf(trusted, DeniedIdentity);
        using RestHttpNativeCertificate unmappedLeaf = Leaf(trusted, "https://identity.example.test/unmapped");
        using RestHttpNativeCertificate foreignLeaf = Leaf(unrelated, ClientIdentity);
        using HttpClient allowed = CertificateClient(fixture, allowedLeaf.Certificate);
        using HttpClient denied = CertificateClient(fixture, deniedLeaf.Certificate);
        using HttpClient unmapped = CertificateClient(fixture, unmappedLeaf.Certificate);
        using HttpClient foreign = CertificateClient(fixture, foreignLeaf.Certificate);

        string registration = Guid.NewGuid().ToString("D");
        using (HttpResponseMessage accepted = await ManagementServiceTests.Put(allowed, "registrations/" + registration,
            ManagementHostFixture.Request("body-reported-label")))
        {
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
            Assert.AreEqual(fixture.ApplicationA, (await ManagementServiceTests.Decode<ManagementRegistrationReceipt>(accepted)).ApplicationId);
        }
        using (HttpResponseMessage instances = await allowed.GetAsync("applications/" + fixture.ApplicationA + "/instances"))
        {
            ManagementInstance actual = (await ManagementServiceTests.Decode<ManagementPage<ManagementInstance>>(instances))
                .Items.Single(item => item.RegistrationId == registration);
            Assert.AreEqual("body-reported-label", actual.ReportedInstanceId);
            Assert.AreEqual("notProvided", actual.InstanceEvidence.Kind,
                "A body label cannot become verified certificate instance evidence.");
        }
        using (var forged = new HttpRequestMessage(HttpMethod.Put, "registrations/" + Guid.NewGuid().ToString("D")))
        {
            forged.Headers.Add("X-CIS-Subject", ClientIdentity);
            forged.Headers.Add("X-CIS-Application", "managed-a");
            using HttpResponseMessage rejected = await denied.SendAsync(forged);
            Assert.AreEqual(HttpStatusCode.Forbidden, rejected.StatusCode,
                "A trusted mapped certificate still needs its complete native capability grant.");
        }
        using (HttpResponseMessage hidden = await denied.GetAsync("applications/" + fixture.ApplicationA))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        using (HttpResponseMessage rejected = await unmapped.GetAsync("service-info"))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, rejected.StatusCode,
                "TLS trust does not invent a receiver-owned workload mapping.");
        }
        try
        {
            using HttpResponseMessage rejected = await foreign.GetAsync("service-info");
            Assert.IsTrue(rejected.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
                "An unrelated client root must never reach management business authority.");
        }
        catch (HttpRequestException)
        {
            // The actual TLS handshake may reject before an HTTP response can exist.
        }
    }

    [TestMethod]
    public async Task ConfiguredEntraProfileValidatesRealSignedWorkloadAndProviderRolesAgainstIsolatedMetadata()
    {
        using RSA signing = RSA.Create(2048);
        using RSA unrelated = RSA.Create(2048);
        var metadata = new OpenIdConnectConfiguration { Issuer = EntraIssuer };
        metadata.SigningKeys.Add(new RsaSecurityKey(signing) { KeyId = "rm07-owned-entra-key" });
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(start: false);
        await fixture.StartReplicaAsync(options => EntraOptions(options, metadata));

        foreach (string token in new[]
        {
            EntraToken(unrelated), EntraToken(signing, issuer: "https://untrusted.invalid/issuer"),
            EntraToken(signing, audience: "another-api"), EntraToken(signing, tenant: OtherId),
            EntraToken(signing, objectId: OtherId), EntraToken(signing, kind: "user"),
            EntraToken(signing, delegated: true), EntraToken(signing, expiration: DateTimeOffset.UtcNow.AddMinutes(-1))
        })
        {
            using HttpResponseMessage rejected = await fixture.Client(subject: null, explicitToken: token).GetAsync("service-info");
            Assert.AreEqual(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.AreEqual("management.unauthenticated", (await ManagementServiceTests.Decode<ManagementProblem>(rejected)).Code);
        }
        using (HttpResponseMessage roleless = await ManagementServiceTests.Put(
            fixture.Client(subject: null, explicitToken: EntraToken(signing, roles: false)),
            "registrations/" + Guid.NewGuid().ToString("D"), ManagementHostFixture.Request("roleless")))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, roleless.StatusCode,
                "Native authentication does not replace a required provider application role.");
        }
        HttpClient allowed = fixture.Client(subject: null, explicitToken: EntraToken(signing));
        allowed.DefaultRequestHeaders.Add(EntraSecurityDefaults.ReportedInstanceHeader, "entra-reported-instance");
        string registration = Guid.NewGuid().ToString("D");
        using (HttpResponseMessage accepted = await ManagementServiceTests.Put(allowed, "registrations/" + registration,
            ManagementHostFixture.Request("different-body-label")))
        {
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
            Assert.AreEqual(fixture.ApplicationA, (await ManagementServiceTests.Decode<ManagementRegistrationReceipt>(accepted)).ApplicationId);
        }
        using HttpResponseMessage instances = await allowed.GetAsync("applications/" + fixture.ApplicationA + "/instances");
        ManagementInstance actual = (await ManagementServiceTests.Decode<ManagementPage<ManagementInstance>>(instances))
            .Items.Single(item => item.RegistrationId == registration);
        Assert.AreEqual("reported", actual.InstanceEvidence.Kind);
        Assert.AreEqual("entra-reported-instance", actual.InstanceEvidence.ReportedIdentifier);
        Assert.IsNull(actual.InstanceEvidence.VerifiedIdentifier);
        Assert.AreEqual("different-body-label", actual.ReportedInstanceId);
    }

    private static ManagementSecurityOptions CertificateOptions(ManagementSecurityOptions source, string roots, string mode) => new()
    {
        Mode = mode,
        BusinessPort = source.BusinessPort,
        OperationalPort = source.OperationalPort,
        ServerCertificateFile = source.ServerCertificateFile,
        ServerPrivateKeyFile = source.ServerPrivateKeyFile,
        ServerTrustBundleFile = source.ServerTrustBundleFile,
        BorrowedNativeKeyScope = source.BorrowedNativeKeyScope,
        CertificateRevocation = CertificateRevocationPolicy.ShortLived,
        LocalDevelopmentEnvironment = EnvironmentOptions.EnvironmentName,
        ClientTrustDomain = CertificateDomain,
        ClientRootBundleFile = roots,
        ClientIssuingAuthorityBundleFile = roots,
        IdentityMappings = [new(new(CertificateDomain, ClientIdentity), "managed-a", "Development"),
            new(new(CertificateDomain, DeniedIdentity), "denied", "Development")],
        Grants = Grants(new(CertificateDomain, ClientIdentity))
    };

    private static ManagementSecurityOptions EntraOptions(ManagementSecurityOptions source, OpenIdConnectConfiguration metadata) => new()
    {
        Mode = "entra",
        BusinessPort = source.BusinessPort,
        OperationalPort = source.OperationalPort,
        ServerCertificateFile = source.ServerCertificateFile,
        ServerPrivateKeyFile = source.ServerPrivateKeyFile,
        ServerTrustBundleFile = source.ServerTrustBundleFile,
        BorrowedNativeKeyScope = source.BorrowedNativeKeyScope,
        CertificateRevocation = CertificateRevocationPolicy.ShortLived,
        EntraTenantId = Tenant,
        EntraAudience = EntraAudience,
        EntraClockSkew = TimeSpan.Zero,
        EntraMetadataManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata),
        IdentityMappings = [new(new(EntraIssuer, ObjectId), "managed-a", "Development", clientApplicationId: ClientId)],
        Grants = Grants(new(EntraIssuer, ObjectId), providerRoles: true)
    };

    private static IReadOnlyList<CapabilityGrant> Grants(SecuritySubject subject, bool providerRoles = false) =>
    [
        new(ManagementHostFixture.Profile, "management.registrations.write", EnvironmentOptions.ApplicationName,
            EnvironmentOptions.EnvironmentName, subject: subject, requiredProviderRole: providerRoles ? "management.register" : null),
        new(ManagementHostFixture.Profile, "management.applications.read", EnvironmentOptions.ApplicationName,
            EnvironmentOptions.EnvironmentName, subject: subject, resourceSelection: CapabilityResourceSelection.AllOfType,
            resourceType: "management.application", requiredProviderRole: providerRoles ? "management.read" : null)
    ];

    private static HttpClient CertificateClient(ManagementHostFixture fixture, X509Certificate2 leaf)
    {
        var trust = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true
        };
        trust.CustomTrustStore.Add(fixture.ServerAuthority);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateChainPolicy = trust,
                ClientCertificates = new X509CertificateCollection { leaf }
            }
        };
        return new(handler)
        {
            BaseAddress = new(fixture.Origins[0] + "/platform/management/v1/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static X509Certificate2 Authority()
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=RM07 owned client root-" + Guid.NewGuid().ToString("N"), key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));
    }

    private static RestHttpNativeCertificate Leaf(X509Certificate2 issuer, string identity)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=not-workload-authority", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new(identity));
        request.CertificateExtensions.Add(names.Build());
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7f;
        serial[^1] |= 1;
        using X509Certificate2 certificate = request.Create(issuer, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(2), serial);
        var native = new RestHttpNativeCertificate(certificate.CopyWithPrivateKey(key));
        try
        {
            native.PrepareForTls(RestSecurityNativeKeyCustody.Shared);
            return native;
        }
        catch (Exception original)
        {
            try
            {
                native.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
    }

    private static string EntraToken(RSA signing, bool roles = true, string? issuer = null, string? audience = null,
        string? tenant = null, string? objectId = null, string kind = "app", bool delegated = false,
        DateTimeOffset? expiration = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["tid"] = tenant ?? Tenant,
            ["oid"] = objectId ?? ObjectId,
            ["azp"] = ClientId,
            ["ver"] = "2.0",
            ["idtyp"] = kind,
            ["application"] = "untrusted-claim-must-not-map",
            ["environment"] = "untrusted-environment"
        };
        if (roles)
        {
            claims["roles"] = new[] { "management.register", "management.read" };
        }
        if (delegated)
        {
            claims["scp"] = "management.read";
        }
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? EntraIssuer,
            Audience = audience ?? EntraAudience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-2),
            NotBefore = DateTime.UtcNow.AddMinutes(-2),
            Expires = (expiration ?? DateTimeOffset.UtcNow.AddMinutes(5)).UtcDateTime,
            SigningCredentials = new(new RsaSecurityKey(signing) { KeyId = "rm07-owned-entra-key" }, SecurityAlgorithms.RsaSha256)
        });
    }
}
