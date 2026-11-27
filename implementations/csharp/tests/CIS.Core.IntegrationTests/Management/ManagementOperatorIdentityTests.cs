using System.Security.Claims;
using CIS.Management.Service.Security;
using CIS.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CIS.Tests.IntegrationTests.Management;

// Pure service-boundary cases: no provider, credential, listener or database I/O.
[TestClass]
public sealed class ManagementOperatorIdentityTests
{
    internal const string Tenant = "11111111-1111-1111-1111-111111111111";
    internal const string Workload = "22222222-2222-2222-2222-222222222222";
    internal const string WorkloadClient = "33333333-3333-3333-3333-333333333333";
    internal const string Operator = "44444444-4444-4444-4444-444444444444";
    internal const string PortalClient = "55555555-5555-5555-5555-555555555555";
    internal const string Audience = "66666666-6666-6666-6666-666666666666";
    internal const string Issuer = "https://login.microsoftonline.com/" + Tenant + "/v2.0";
    internal const string Scope = "management.operator";

    [TestMethod]
    public void ConfigurationSelectsTheCompiledMixedProviderWithoutACallback()
    {
        ManagementSecurityOptions options = Options();
        Assert.AreEqual("entra-management", options.Mode);
        Assert.AreEqual(SecurityProfileKind.Native, options.Kind);
        Assert.IsNull(options.NativeProvider);
        Assert.AreEqual(PortalClient, options.EntraPortalClientId);
        Assert.AreEqual(Scope, options.EntraOperatorScope);
        Assert.AreEqual(2, options.IdentityMappings.Count);
    }

    [TestMethod]
    [DataRow("EntraPortalClientId", "not-a-client")]
    [DataRow("EntraTenantId", "00000000-0000-0000-0000-000000000000")]
    [DataRow("EntraAudience", "")]
    [DataRow("EntraOperatorScope", "")]
    [DataRow("EntraOperatorScope", "management.operator other")]
    [DataRow("EntraAuthorityHost", "http://login.microsoftonline.com/")]
    [DataRow("IdentityMappings:1:ClientApplicationId", WorkloadClient)]
    [DataRow("IdentityMappings:1:Issuer", "https://another.invalid/issuer")]
    [DataRow("IdentityMappings:1:Subject", Workload)]
    [DataRow("IdentityMappings:1:Application", "")]
    [DataRow("Grants:0:RequiredProviderRole", null)]
    public void InvalidTrustClientScopeMappingAndWorkloadGrantCannotActivate(string name, string? value)
    {
        Assert.Throws<ArgumentException>(() => Options(name, value));
    }

    [TestMethod]
    public void ExistingEntraModeStillRejectsHumanMappings()
    {
        Assert.Throws<ArgumentException>(() => Options("Mode", "entra"));
    }

    [TestMethod]
    public void VerifiedUserShapeUsesOidAndPreservesTheDistinctCallingClient()
    {
        ManagementSecurityOptions options = Options();
        ClaimsPrincipal first = Principal(user: true);
        ClaimsPrincipal otherPairwiseSubject = Principal(user: true, replace: "sub", value: "another-pairwise-subject");
        Assert.IsTrue(ManagementEntraIdentity.TryRead(first, options, out ManagementEntraIdentity? identity));
        Assert.IsTrue(ManagementEntraIdentity.TryRead(otherPairwiseSubject, options, out ManagementEntraIdentity? repeated));
        Assert.AreEqual(new SecuritySubject(Issuer, Operator), identity!.Subject);
        Assert.AreEqual(identity.Subject, repeated!.Subject);
        Assert.AreEqual(SecurityActorKind.User, identity.ActorKind);
        Assert.AreEqual(PortalClient, identity.ClientId);
        Assert.HasCount(0, identity.Roles);
    }

    [TestMethod]
    [DataRow("scp", "management.operator.extra")]
    [DataRow("scp", "management.operator management.operator")]
    [DataRow("scp", "management.operator\tother")]
    [DataRow("scp", null)]
    [DataRow("azp", WorkloadClient)]
    [DataRow("azp", "invalid")]
    [DataRow("oid", "")]
    [DataRow("ver", "1.0")]
    [DataRow("tid", Workload)]
    [DataRow("idtyp", "app")]
    [DataRow("idtyp", "device")]
    public void OperatorClaimsNeedTheActualClientAndExactDelegatedScope(string name, string? value)
    {
        Assert.IsFalse(ManagementEntraIdentity.TryRead(Principal(true, name, value), Options(), out _));
    }

    [TestMethod]
    public void DuplicateIdentityClaimsOrWorkloadScopeNeverSelectAnotherActor()
    {
        ClaimsPrincipal duplicate = Principal(user: true);
        ((ClaimsIdentity)duplicate.Identity!).AddClaim(new("oid", Workload));
        Assert.IsFalse(ManagementEntraIdentity.TryRead(duplicate, Options(), out _));

        Assert.IsTrue(ManagementEntraIdentity.TryRead(Principal(user: false), Options(), out ManagementEntraIdentity? workload));
        Assert.AreEqual(SecurityActorKind.Application, workload!.ActorKind);
        Assert.IsFalse(ManagementEntraIdentity.TryRead(Principal(false, "scp", Scope), Options(), out _));
    }

    [TestMethod]
    public async Task AnAuthenticatedPrincipalWithoutNativeValidationEvidenceCannotBeProjected()
    {
        ManagementEntraProvider provider = Provider();
        var authentication = AuthenticateResult.Success(new AuthenticationTicket(Principal(user: true), "management-security"));
        Assert.IsNull((await provider.ProjectAsync(new DefaultHttpContext(), authentication)).Identity);
    }

    [TestMethod]
    public async Task ProjectionRetainsTheHumanAndClientOnlyOnItsOwnNativeTicket()
    {
        ManagementEntraProvider provider = Provider();
        var native = new JwtBearerOptions();
        ManagementEntraJwt.Apply(native, provider);
        var context = new DefaultHttpContext();
        var validated = new TokenValidatedContext(context,
            new AuthenticationScheme(provider.Options.AuthenticationScheme, null, typeof(JwtBearerHandler)), native)
        {
            Principal = Principal(user: true),
            SecurityToken = new JsonWebToken("{\"alg\":\"none\"}", "{\"exp\":"
                + DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")
        };
        // The unit checks event/ticket ownership; signed native validation is exercised over HTTPS separately.
        await provider.TokenValidated(validated);
        var authentication = AuthenticateResult.Success(new AuthenticationTicket(validated.Principal!, validated.Properties,
            provider.Options.AuthenticationScheme));
        var identity = (await provider.ProjectAsync(context, authentication)).Identity;
        Assert.IsNotNull(identity);
        Assert.AreEqual(new SecuritySubject(Issuer, Operator), identity.Subject);
        Assert.AreEqual(identity.Subject, identity.HumanSubject);
        Assert.AreEqual(PortalClient, identity.ClientApplicationId);
        Assert.AreEqual(SecurityActorKind.User, identity.ActorKind);
        Assert.IsNull(identity.VerifiedDelegation);
        Assert.IsNull((await Provider().ProjectAsync(context, authentication)).Identity,
            "A ticket produced by a different provider owner cannot establish identity.");

        var copied = AuthenticateResult.Success(new AuthenticationTicket(Principal(user: true), validated.Properties,
            provider.Options.AuthenticationScheme));
        Assert.IsNull((await provider.ProjectAsync(context, copied)).Identity,
            "An equivalent claims bag cannot borrow another principal's validation proof.");
    }

    [TestMethod]
    [DataRow("signature")]
    [DataRow("lifetime")]
    [DataRow("events")]
    [DataRow("forwarding")]
    [DataRow("key-resolver")]
    public void LateNativeConfigurationCannotReplaceValidationOwnership(string mutation)
    {
        ManagementEntraProvider provider = Provider();
        var native = new JwtBearerOptions();
        ManagementEntraJwt.Apply(native, provider);
        var validator = new ManagementEntraJwtValidator(provider);
        Assert.IsTrue(validator.Validate(provider.Options.AuthenticationScheme, native).Succeeded);
        switch (mutation)
        {
            case "signature":
                native.TokenValidationParameters.RequireSignedTokens = false;
                break;
            case "lifetime":
                native.TokenValidationParameters.ValidateLifetime = false;
                break;
            case "events":
                native.Events = new JwtBearerEvents();
                break;
            case "forwarding":
                native.ForwardDefault = "another-scheme";
                break;
            case "key-resolver":
                native.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) => [];
                break;
            default:
                Assert.Fail("Unknown test mutation.");
                break;
        }

        Assert.IsTrue(validator.Validate(provider.Options.AuthenticationScheme, native).Failed);
    }

    internal static ManagementSecurityOptions Options(string? name = null, string? value = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Mode"] = "entra-management",
            ["ServerCertificateFile"] = Path.GetFullPath("operator-test-server.pem"),
            ["ServerPrivateKeyFile"] = Path.GetFullPath("operator-test-key.pem"),
            ["EntraTenantId"] = Tenant,
            ["EntraAudience"] = Audience,
            ["EntraPortalClientId"] = PortalClient,
            ["EntraOperatorScope"] = Scope,
            ["IdentityMappings:0:Issuer"] = Issuer,
            ["IdentityMappings:0:Subject"] = Workload,
            ["IdentityMappings:0:ClientApplicationId"] = WorkloadClient,
            ["IdentityMappings:0:Application"] = "managed-a",
            ["IdentityMappings:0:Environment"] = "Development",
            ["IdentityMappings:1:Issuer"] = Issuer,
            ["IdentityMappings:1:Subject"] = Operator,
            ["IdentityMappings:1:ClientApplicationId"] = PortalClient,
            ["IdentityMappings:1:ActorKind"] = "User",
            ["IdentityMappings:1:Application"] = "management-portal",
            ["IdentityMappings:1:Environment"] = "Development",
            ["Grants:0:Capability"] = "management.registrations.write",
            ["Grants:0:TargetApplication"] = EnvironmentOptions.ApplicationName,
            ["Grants:0:TargetEnvironment"] = EnvironmentOptions.EnvironmentName,
            ["Grants:0:Issuer"] = Issuer,
            ["Grants:0:Subject"] = Workload,
            ["Grants:0:RequiredProviderRole"] = "management.register"
        };
        if (name is not null)
        {
            settings[name] = value;
        }

        return ManagementSecurityOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()).Capture();
    }

    private static ManagementEntraProvider Provider()
    {
        return new(Options(), new StaticConfigurationManager<OpenIdConnectConfiguration>(new()
        {
            Issuer = Issuer
        }), TimeProvider.System);
    }

    private static ClaimsPrincipal Principal(bool user, string? replace = null, string? value = null)
    {
        var claims = new Dictionary<string, string>
        {
            ["ver"] = "2.0",
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["tid"] = Tenant,
            ["oid"] = user ? Operator : Workload,
            ["azp"] = user ? PortalClient : WorkloadClient,
            ["sub"] = "pairwise-user-subject"
        };
        if (user)
        {
            claims["scp"] = Scope;
        }
        else
        {
            claims["idtyp"] = "app";
            claims["roles"] = "management.register";
        }

        if (replace is not null)
        {
            claims.Remove(replace);
            if (value is not null)
            {
                claims[replace] = value;
            }
        }

        return new(new ClaimsIdentity(claims.Select(pair => new Claim(pair.Key, pair.Value)), "management-security"));
    }
}
