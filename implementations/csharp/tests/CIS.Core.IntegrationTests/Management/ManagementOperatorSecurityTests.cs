using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using CIS.Management;
using CIS.Management.Service.Security;
using CIS.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using static CIS.Tests.IntegrationTests.Management.ManagementOperatorIdentityTests;

namespace CIS.Tests.IntegrationTests.Management;

// Actual product/native HTTPS proof with owned signing metadata; not a live Entra deployment.
[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementOperatorSecurityTests
{
    [TestMethod]
    public async Task TheSameConfiguredHostKeepsWorkloadControlAndReportingWhileOperatorsInspectItsVersions()
    {
        using RSA signing = RSA.Create(2048);
        await using ManagementHostFixture fixture = await StartAsync(signing);
        HttpClient workload = fixture.Client(subject: null, explicitToken: Token(signing, user: false));
        HttpClient human = fixture.Client(subject: null, explicitToken: Token(signing, user: true));
        string registration = Guid.NewGuid().ToString("D");
        ManagementRegistrationRequest request = ManagementHostFixture.Request("reported-workload") with
        {
            ApplicationVersion = "mixed-mode-1.2"
        };

        using (HttpResponseMessage registered = await ManagementServiceTests.Put(workload, "registrations/" + registration, request))
        {
            Assert.AreEqual(HttpStatusCode.OK, registered.StatusCode);
            Assert.AreEqual(fixture.ApplicationA, (await ManagementServiceTests.Decode<ManagementRegistrationReceipt>(registered)).ApplicationId);
        }

        using (HttpResponseMessage control = await workload.GetAsync("registrations/" + registration + "/control?attemptId=" + Guid.NewGuid().ToString("D")))
        {
            Assert.AreEqual(HttpStatusCode.OK, control.StatusCode);
        }

        string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        using (HttpResponseMessage observation = await ManagementServiceTests.Put(workload, "registrations/" + registration + "/observation",
            new ManagementObservation("1", timestamp, "ready", "healthy")))
        {
            Assert.AreEqual(HttpStatusCode.OK, observation.StatusCode);
        }

        using (HttpResponseMessage inventory = await ManagementServiceTests.Put(workload, "registrations/" + registration + "/inventory",
            new ManagementInventorySnapshot("1", timestamp, [], [], [])))
        {
            Assert.AreEqual(HttpStatusCode.OK, inventory.StatusCode);
        }

        using (HttpResponseMessage response = await human.GetAsync("applications/" + fixture.ApplicationA + "/instances"))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            ManagementInstance instance = (await ManagementServiceTests.Decode<ManagementPage<ManagementInstance>>(response))
                .Items.Single(item => item.RegistrationId == registration);
            Assert.AreEqual("mixed-mode-1.2", instance.ApplicationVersion);
        }

        using (HttpResponseMessage response = await human.GetAsync("applications/" + fixture.ApplicationA + "/inventory"))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue((await ManagementServiceTests.Decode<ManagementPage<ManagementInventoryEntry>>(response)).Items
                .Any(item => item.RegistrationId == registration && item.ApplicationVersion == "mixed-mode-1.2"));
        }

        using (HttpResponseMessage hidden = await human.GetAsync("applications/" + fixture.ApplicationB))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        }

        using (HttpResponseMessage denied = await ManagementServiceTests.Put(human, "registrations/" + Guid.NewGuid().ToString("D"), request))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode,
                "A User actor cannot become a workload even with a deliberately supplied registration grant.");
        }

        using (HttpResponseMessage denied = await human.GetAsync("registrations/" + registration + "/control?attemptId=" + Guid.NewGuid().ToString("D")))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }
    }

    [TestMethod]
    public async Task NativeSignaturesClientAndScopeAreCheckedWithoutBorrowingActorHeadersOrWorkloadRoles()
    {
        using RSA signing = RSA.Create(2048);
        using RSA foreign = RSA.Create(2048);
        await using ManagementHostFixture fixture = await StartAsync(signing);
        var invalid = new List<(string Name, string Token)>
        {
            ("signature", Token(foreign, true)),
            ("issuer", Token(signing, true, issuer: "https://foreign.invalid/issuer")),
            ("audience", Token(signing, true, audience: PortalClient)),
            ("tenant", Token(signing, true, changes: new() { ["tid"] = Workload })),
            ("client", Token(signing, true, changes: new() { ["azp"] = WorkloadClient })),
            ("object", Token(signing, true, changes: new() { ["oid"] = Workload })),
            ("scope", Token(signing, true, changes: new() { ["scp"] = Scope + ".extra" })),
            ("missing scope", Token(signing, true, changes: new() { ["scp"] = null })),
            ("wrong version", Token(signing, true, changes: new() { ["ver"] = "1.0" })),
            ("workload with scope", Token(signing, false, changes: new() { ["scp"] = Scope })),
            ("duplicate object", Token(signing, true, changes: new() { ["oid"] = new[] { Operator, Workload } })),
            ("expiry", Token(signing, true, expiration: DateTimeOffset.UtcNow.AddMinutes(-1)))
        };
        foreach ((string name, string token) in invalid)
        {
            using var forged = new HttpRequestMessage(HttpMethod.Get, "applications/" + fixture.ApplicationA);
            forged.Headers.Add("X-CIS-Subject", Operator);
            forged.Headers.Add("X-CIS-Application", "management-portal");
            forged.Headers.Add("X-CIS-Human-Subject", Operator);
            using HttpResponseMessage rejected = await fixture.Client(subject: null, explicitToken: token).SendAsync(forged);
            Assert.AreEqual(HttpStatusCode.Unauthorized, rejected.StatusCode, name);
        }

        using (HttpResponseMessage roleless = await ManagementServiceTests.Put(
            fixture.Client(subject: null, explicitToken: Token(signing, false, changes: new()
            {
                ["roles"] = null
            })),
            "registrations/" + Guid.NewGuid().ToString("D"), ManagementHostFixture.Request("roleless")))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, roleless.StatusCode,
                "Mixed mode preserves the workload's actual provider-role requirement.");
        }

        using (HttpResponseMessage stable = await fixture.Client(subject: null,
            explicitToken: Token(signing, true, changes: new()
            {
                ["sub"] = "different-pairwise-subject",
                ["idtyp"] = "user"
            }))
            .GetAsync("applications/" + fixture.ApplicationA))
        {
            Assert.AreEqual(HttpStatusCode.OK, stable.StatusCode);
        }

        using (HttpResponseMessage forgedUser = await fixture.Client(subject: null,
            explicitToken: Token(signing, false, changes: new()
            {
                ["actor_kind"] = "user",
                ["human_subject"] = Operator,
                ["delegation"] = "forged"
            })).GetAsync("applications/" + fixture.ApplicationA))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, forgedUser.StatusCode,
                "Unrecognized actor/delegation claims cannot borrow a human's application read grant.");
        }
    }

    private static async Task<ManagementHostFixture> StartAsync(RSA signing)
    {
        var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
        metadata.SigningKeys.Add(new RsaSecurityKey(signing) { KeyId = "rm09-owned-key" });
        ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(start: false);
        try
        {
            await fixture.StartReplicaAsync(source => new()
            {
                Mode = "entra-management",
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
                EntraPortalClientId = PortalClient,
                EntraOperatorScope = Scope,
                EntraClockSkew = TimeSpan.Zero,
                EntraMetadataManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata),
                IdentityMappings = Options().IdentityMappings,
                Grants =
                [
                    new(ManagementHostFixture.Profile, "management.registrations.write", EnvironmentOptions.ApplicationName,
                        EnvironmentOptions.EnvironmentName, subject: new(Issuer, Workload), requiredProviderRole: "management.register"),
                    new(ManagementHostFixture.Profile, "management.applications.read", EnvironmentOptions.ApplicationName,
                        EnvironmentOptions.EnvironmentName, subject: new(Issuer, Operator),
                        resourceSelection: CapabilityResourceSelection.ExactResource, resourceType: "management.application",
                        resourceIdentifier: fixture.ApplicationA),
                    new(ManagementHostFixture.Profile, "management.registrations.write", EnvironmentOptions.ApplicationName,
                        EnvironmentOptions.EnvironmentName, subject: new(Issuer, Operator))
                ]
            });
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

    private static string Token(RSA signing, bool user, string? issuer = null, string? audience = null,
        DateTimeOffset? expiration = null, Dictionary<string, object?>? changes = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["tid"] = Tenant,
            ["oid"] = user ? Operator : Workload,
            ["azp"] = user ? PortalClient : WorkloadClient,
            ["sub"] = "pairwise-subject",
            ["ver"] = "2.0"
        };
        if (user)
        {
            claims["scp"] = Scope;
        }
        else
        {
            claims["idtyp"] = "app";
            claims["roles"] = new[] { "management.register" };
        }

        foreach ((string key, object? value) in changes ?? [])
        {
            claims.Remove(key);
            if (value is not null)
            {
                claims[key] = value;
            }
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-2),
            NotBefore = DateTime.UtcNow.AddMinutes(-2),
            Expires = (expiration ?? DateTimeOffset.UtcNow.AddMinutes(5)).UtcDateTime,
            SigningCredentials = new(new RsaSecurityKey(signing) { KeyId = "rm09-owned-key" }, SecurityAlgorithms.RsaSha256)
        });
    }
}
