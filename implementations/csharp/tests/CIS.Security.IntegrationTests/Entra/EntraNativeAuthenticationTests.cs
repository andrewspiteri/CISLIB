using System.Security.Claims;
using System.Security.Cryptography;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using CIS.Startup;
using CIS.CommonTesting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.IdentityModel.Tokens.Jwt;

namespace CIS.Tests.Security.Entra;

[TestClass]
public sealed class EntraNativeAuthenticationTests
{
    [TestMethod]
    public async Task NativeSignedWorkloadMapsExactIdentityAndRequiresTheActualApplicationRole()
    {
        using var signing = RSA.Create(2048);
        await using ServiceProvider root = Services(signing);
        await StartAsync(root);

        AuthenticateResult authentication = await AuthenticateAsync(root, Token(signing));
        Assert.IsTrue(authentication.Succeeded);
        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root, Profile, authentication);
        Assert.IsNotNull(caller.Caller);
        Assert.AreEqual("country-worker", caller.Caller.Application);
        Assert.AreEqual("Development", caller.Caller.Environment);
        Assert.AreEqual(ObjectId, caller.Caller.Subject.Identifier);
        Assert.AreEqual(InstanceEvidenceKind.NotProvided, caller.Caller.Instance.Kind);

        ICapabilityAuthorizer authorizer = caller.Services.GetRequiredService<ICapabilityAuthorizer>();
        Assert.IsTrue((await authorizer.AuthorizeAsync("country.read", Target)).IsAllowed);
        Assert.IsFalse((await authorizer.AuthorizeAsync("country.change", Target)).IsAllowed);
        Assert.IsFalse((await authorizer.AuthorizeAsync("country.read", new("country-api", "Production"))).IsAllowed);

        AuthenticateResult roleless = await AuthenticateAsync(root, Token(signing, includeRole: false));
        Assert.IsTrue(roleless.Succeeded, "A valid roleless application is authenticated but has no implicit capability grant.");
        await using SecurityHostContextScope noGrant = await SecurityHostContextScope.CreateAsync(root, Profile, roleless);
        Assert.IsFalse((await noGrant.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("country.read", Target)).IsAllowed);
    }

    [TestMethod]
    [DataRow("signature")]
    [DataRow("issuer")]
    [DataRow("audience")]
    [DataRow("expired")]
    [DataRow("tenant")]
    [DataRow("version")]
    [DataRow("human")]
    [DataRow("missing-app-kind")]
    [DataRow("delegated")]
    [DataRow("duplicate-object")]
    [DataRow("oversized-role")]
    public async Task NativeHandlerRejectsInvalidTokensAndAppOnlyViolations(string defect)
    {
        using var signing = RSA.Create(2048);
        using var unrelated = RSA.Create(2048);
        await using ServiceProvider root = Services(signing);
        await StartAsync(root);
        var claims = BaseClaims(includeRole: true);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string issuer = Issuer;
        string audience = Audience;
        DateTimeOffset expiry = now.AddMinutes(10);
        switch (defect)
        {
            case "issuer":
                issuer = "https://unrelated.invalid/tenant/v2.0";
                break;
            case "audience":
                audience = "another-api";
                break;
            case "expired":
                expiry = now.AddMinutes(-2);
                break;
            case "tenant":
                Replace(claims, "tid", OtherId);
                break;
            case "version":
                Replace(claims, "ver", "1.0");
                break;
            case "human":
                Replace(claims, "idtyp", "user");
                break;
            case "missing-app-kind":
                claims.RemoveAll(claim => claim.Type == "idtyp");
                break;
            case "delegated":
                claims.Add(new("scp", "country.read"));
                break;
            case "duplicate-object":
                claims.Add(new("oid", OtherId));
                break;
            case "oversized-role":
                claims.Add(new("roles", new string('x', 1025)));
                break;
        }
        string token = WriteToken(defect == "signature" ? unrelated : signing, claims, issuer, audience, expiry);

        AuthenticateResult result = await AuthenticateAsync(root, token);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNotNull(result.Failure);
    }

    [TestMethod]
    [DataRow("oid")]
    [DataRow("azp")]
    public async Task AValidTokenForAnUnmappedWorkloadCannotEstablishCallerAuthority(string changed)
    {
        using var signing = RSA.Create(2048);
        await using ServiceProvider root = Services(signing);
        await StartAsync(root);
        var claims = BaseClaims(includeRole: true);
        Replace(claims, changed, OtherId);
        AuthenticateResult authentication = await AuthenticateAsync(root,
            WriteToken(signing, claims, Issuer, Audience, DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.IsTrue(authentication.Succeeded, "The native credential is valid; the receiver's trusted application mapping must still run.");

        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root, Profile, authentication);

        Assert.IsNull(caller.Caller);
        Assert.AreEqual(SecurityFailureKind.IdentityUnmapped, caller.Failure!.Kind);
        Assert.IsFalse((await caller.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("country.read", Target)).IsAllowed);
    }

    [TestMethod]
    public async Task NativeClockSkewRemainsExplicitAndDoesNotBecomeAnOutboundGracePeriod()
    {
        using var signing = RSA.Create(2048);
        await using ServiceProvider root = Services(signing);
        await StartAsync(root);
        JwtBearerOptions native = root.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Scheme);
        Assert.AreEqual(TimeSpan.FromSeconds(30), native.TokenValidationParameters.ClockSkew);
        Assert.IsFalse(native.MapInboundClaims);
        Assert.IsFalse(native.SaveToken);

        AuthenticateResult withinSkew = await AuthenticateAsync(root,
            WriteToken(signing, BaseClaims(true), Issuer, Audience, DateTimeOffset.UtcNow.AddSeconds(-10)));

        Assert.IsTrue(withinSkew.Succeeded);
        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root, Profile, withinSkew);
        Assert.IsTrue((await caller.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("country.read", Target)).IsAllowed,
            "Native inbound JWT validation owns its explicitly configured tolerance; outgoing expired tokens are tested separately.");
    }

    [TestMethod]
    public async Task UntrustedApplicationAndInstanceClaimsDoNotReplaceTheMappedCaller()
    {
        using var signing = RSA.Create(2048);
        await using ServiceProvider root = Services(signing);
        await StartAsync(root);
        var claims = BaseClaims(true);
        claims.AddRange([new("application", "administrator"), new("environment", "Production"),
            new("instance", "verified-replica"), new("user", "operator")]);
        AuthenticateResult authentication = await AuthenticateAsync(root,
            WriteToken(signing, claims, Issuer, Audience, DateTimeOffset.UtcNow.AddMinutes(5)));

        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root, Profile, authentication);

        Assert.AreEqual("country-worker", caller.Caller!.Application);
        Assert.AreEqual("Development", caller.Caller.Environment);
        Assert.AreEqual(SecurityActorKind.Application, caller.Caller.ActorKind);
        Assert.IsNull(caller.Caller.HumanSubject);
        Assert.AreEqual(InstanceEvidenceKind.NotProvided, caller.Caller.Instance.Kind);
    }

    [TestMethod]
    public async Task ReportedInstanceHeadersStayBoundedAndUnverified()
    {
        using var signing = RSA.Create(2048);
        await using ServiceProvider root = Services(signing);
        await StartAsync(root);
        await using AsyncServiceScope scope = root.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = "Bearer " + Token(signing);
        context.Request.Headers[EntraSecurityDefaults.ReportedInstanceHeader] = "replica-a";
        AuthenticateResult authentication = await context.AuthenticateAsync(Scheme);
        ISecurityIdentityProjector projector = scope.ServiceProvider.GetRequiredKeyedService<ISecurityIdentityProjector>(Profile);

        SecurityProjectionResult projected = await projector.ProjectAsync(context, authentication);

        Assert.IsNotNull(projected.Identity);
        Assert.AreEqual(InstanceEvidenceKind.Reported, projected.Identity.Instance.Kind);
        Assert.IsNull(projected.Identity.Instance.VerifiedIdentifier);
        context.Request.Headers[EntraSecurityDefaults.ReportedInstanceHeader] = new string('x', 1025);
        Assert.IsNull((await projector.ProjectAsync(context, authentication)).Identity);
    }

    [TestMethod]
    public async Task InboundOnlyStartupNeedsNoOutgoingCredentialAndRegistrationIsDeferred()
    {
        using var signing = RSA.Create(2048);
        int captures = 0;
        int credentials = 0;
        ServiceCollection services = CommonServices();
        services.AddCisEntraSecurity(Profile, _ =>
        {
            captures++;
            EntraSecurityOptions options = Receiving(signing);
            options.Outbound = new EntraWorkloadOptions { ClientId = "unused-invalid-outbound-data" };
            return options;
        }, Scheme, borrowedCredential: _ =>
        {
            credentials++;
            throw new InvalidOperationException("An inbound-only profile must not ask for this credential.");
        });
        Assert.AreEqual(0, captures);
        await using ServiceProvider root = services.BuildServiceProvider();
        Assert.AreEqual(0, captures);

        await StartAsync(root);

        Assert.AreEqual(1, captures);
        Assert.AreEqual(0, credentials);
        SecurityProfileStatus status = root.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus().Single();
        Assert.AreEqual(SecurityProfileAvailability.Available, status.Availability);
    }

    [TestMethod]
    public async Task SkewAboveTheApprovedMaximumFailsProfileActivation()
    {
        using var signing = RSA.Create(2048);
        ServiceCollection services = CommonServices();
        services.AddCisEntraSecurity(Profile, _ =>
        {
            EntraSecurityOptions options = Receiving(signing);
            options.Inbound!.ClockSkew = TimeSpan.FromSeconds(121);
            return options;
        }, Scheme);
        await using ServiceProvider root = services.BuildServiceProvider();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => root.GetServices<IStartable>().ToArray());
    }

    [TestMethod]
    [DataRow("signature")]
    [DataRow("lifetime")]
    [DataRow("events-type")]
    [DataRow("events-instance")]
    [DataRow("forward")]
    [DataRow("parameter-metadata")]
    public async Task LateNativePostConfigurationCannotReplaceTheSelectedValidationBoundary(string replacement)
    {
        using var signing = RSA.Create(2048);
        ServiceCollection services = CommonServices();
        services.AddCisEntraSecurity(Profile, _ => Receiving(signing), Scheme);
        services.AddSingleton<FabricatedEvents>();
        services.PostConfigure<JwtBearerOptions>(Scheme, native =>
        {
            switch (replacement)
            {
                case "signature":
                    native.TokenValidationParameters.SignatureValidator = (raw, _) => new JsonWebToken(raw);
                    native.TokenValidationParameters.ValidateIssuerSigningKey = false;
                    break;
                case "lifetime":
                    native.TokenValidationParameters.ValidateLifetime = false;
                    break;
                case "events-type":
                    native.EventsType = typeof(FabricatedEvents);
                    break;
                case "events-instance":
                    native.Events = new FabricatedEvents();
                    break;
                case "forward":
                    native.ForwardAuthenticate = "another-scheme";
                    break;
                case "parameter-metadata":
                    native.TokenValidationParameters.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        "https://unrelated.invalid/metadata", new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever { RequireHttps = true });
                    break;
            }
        });
        await using ServiceProvider root = services.BuildServiceProvider();

        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => StartAsync(root));
    }

    [TestMethod]
    public async Task MutatingTheOwnedEventDelegateCannotFabricateNativeAuthentication()
    {
        using var signing = RSA.Create(2048);
        int injected = 0;
        ServiceCollection services = CommonServices();
        services.AddCisEntraSecurity(Profile, _ => Receiving(signing), Scheme);
        services.PostConfigure<JwtBearerOptions>(Scheme, native => native.Events.OnMessageReceived = context =>
        {
            injected++;
            context.Principal = FabricatedPrincipal();
            context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1);
            context.Success();
            return Task.CompletedTask;
        });
        await using ServiceProvider root = services.BuildServiceProvider();
        await StartAsync(root);

        AuthenticateResult result = await AuthenticateAsync(root, "not-a-signed-token");

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, injected, "The sealed native event methods do not invoke mutable delegate hooks.");
        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root, Profile, result);
        Assert.IsNull(caller.Caller);
        Assert.IsFalse((await caller.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("country.read", Target)).IsAllowed);
    }

    [TestMethod]
    public async Task FabricatedTicketShapeIsNotNativeValidationEvidence()
    {
        using var signing = RSA.Create(2048);
        await using ServiceProvider root = Services(signing);
        await StartAsync(root);
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        var fabricated = AuthenticateResult.Success(new AuthenticationTicket(FabricatedPrincipal(), properties, Scheme));

        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root, Profile, fabricated);

        Assert.IsNull(caller.Caller);
        Assert.AreEqual(SecurityFailureKind.CredentialInvalid, caller.Failure!.Kind);
        Assert.IsFalse((await caller.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("country.read", Target)).IsAllowed);
    }

    [TestMethod]
    public async Task CallerProjectionUsesTheImmutableFactsOfItsActualNativeValidation()
    {
        using var signing = RSA.Create(2048);
        await using ServiceProvider root = Services(signing);
        await StartAsync(root);
        AuthenticateResult authentication = await AuthenticateAsync(root, Token(signing));
        ClaimsIdentity identity = authentication.Principal!.Identities.Single();
        identity.RemoveClaim(identity.FindFirst("oid")!);
        identity.AddClaim(new("oid", OtherId));
        identity.AddClaim(new("roles", "administrator"));

        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root, Profile, authentication);

        Assert.AreEqual(ObjectId, caller.Caller!.Subject.Identifier,
            "Mutable native claims cannot replace credential facts captured by actual validation.");
        Assert.IsFalse((await caller.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("country.change", Target)).IsAllowed);
    }

    [TestMethod]
    public async Task RetainedNativeEvidenceCannotEstablishAFreshScopeAfterItsActualExpiryAndSkew()
    {
        using var signing = RSA.Create(2048);
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        ServiceCollection services = CommonServices();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCisEntraSecurity(Profile, _ => Receiving(signing), Scheme);
        await using ServiceProvider root = services.BuildServiceProvider();
        await StartAsync(root);
        AuthenticateResult authentication = await AuthenticateAsync(root, Token(signing));
        Assert.IsTrue(authentication.Succeeded);
        authentication.Properties!.ExpiresUtc = clock.GetUtcNow().AddDays(1);
        clock.Advance(TimeSpan.FromMinutes(7));

        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root, Profile, authentication);

        Assert.IsNull(caller.Caller);
        Assert.AreEqual(SecurityFailureKind.CredentialExpired, caller.Failure!.Kind,
            "Mutable ticket expiry cannot extend the immutable expiry established by native validation.");
    }

    private static ClaimsPrincipal FabricatedPrincipal()
    {
        var claims = BaseClaims(true);
        claims.Add(new("iss", Issuer));
        claims.Add(new("aud", Audience));
        return new(new ClaimsIdentity(claims, Scheme));
    }

    private sealed class FabricatedEvents : JwtBearerEvents
    {
        public override Task MessageReceived(MessageReceivedContext context)
        {
            context.Principal = FabricatedPrincipal();
            context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1);
            context.Success();
            return Task.CompletedTask;
        }
    }

    internal const string Profile = "entra";
    internal const string Scheme = "entra-jwt";
    internal const string Tenant = "11111111-1111-1111-1111-111111111111";
    internal const string ObjectId = "22222222-2222-2222-2222-222222222222";
    internal const string ClientId = "33333333-3333-3333-3333-333333333333";
    internal const string OtherId = "44444444-4444-4444-4444-444444444444";
    internal const string Issuer = "https://login.microsoftonline.com/" + Tenant + "/v2.0";
    internal const string Audience = "api://country-api";
    internal static readonly CapabilityTarget Target = new("country-api", "Development");

    internal static ServiceCollection CommonServices()
    {
        var policy = new SecurityOptions { PolicyVersion = "entra-fixture-v1" };
        policy.Profiles.Add(new(Profile, SecurityProfileKind.EntraWorkloadIdentity, SecurityProfileRoles.Inbound,
            Scheme, [new(new(Issuer, ObjectId), "country-worker", "Development", clientApplicationId: ClientId)]));
        policy.Capabilities.Add("country.read");
        policy.Capabilities.Add("country.change");
        policy.Grants.Add(new(Profile, "country.read", "country-api", "Development", new(Issuer, ObjectId), requiredProviderRole: "reader"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCisSecurity(policy);
        return services;
    }

    internal static ServiceProvider Services(RSA signing)
    {
        ServiceCollection services = CommonServices();
        services.AddCisEntraSecurity(Profile, _ => Receiving(signing), Scheme);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    internal static EntraSecurityOptions Receiving(RSA signing)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(new RsaSecurityKey(signing) { KeyId = "fixture-signing-key" });
        return new()
        {
            TenantId = Tenant,
            Inbound = new()
            {
                Audience = Audience,
                MetadataManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration)
            }
        };
    }

    internal static async Task StartAsync(IServiceProvider root)
    {
        foreach (IStartable participant in root.GetServices<IStartable>())
        {
            await participant.StartAsync(CancellationToken.None);
        }
    }

    internal static async Task<AuthenticateResult> AuthenticateAsync(IServiceProvider root, string token)
    {
        await using AsyncServiceScope scope = root.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = "Bearer " + token;
        return await context.AuthenticateAsync(Scheme);
    }

    internal static string Token(RSA signing, bool includeRole = true)
        => WriteToken(signing, BaseClaims(includeRole), Issuer, Audience, DateTimeOffset.UtcNow.AddMinutes(5));

    private static List<Claim> BaseClaims(bool includeRole)
    {
        var claims = new List<Claim>
        {
            new("tid", Tenant), new("oid", ObjectId), new("azp", ClientId), new("ver", "2.0"), new("idtyp", "app")
        };
        if (includeRole)
        {
            claims.Add(new("roles", "reader"));
        }
        return claims;
    }

    private static void Replace(List<Claim> claims, string type, string value)
    {
        claims.RemoveAll(claim => claim.Type == type);
        claims.Add(new(type, value));
    }

    private static string WriteToken(RSA signing, IEnumerable<Claim> claims, string issuer, string audience, DateTimeOffset expiry)
    {
        var token = new JwtSecurityToken(issuer, audience, claims,
            notBefore: DateTime.UtcNow.AddMinutes(-10), expires: expiry.UtcDateTime,
            signingCredentials: new SigningCredentials(new RsaSecurityKey(signing) { KeyId = "fixture-signing-key" }, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
