namespace CIS.Tests.IntegrationTests.Security;

internal static class SecurityPackagePrograms
{
    internal static string For(string profile) => profile switch
    {
        "Certificates" => CertificateImports + CommonSetup + CertificateSetup + CommonExecution,
        "Entra" => EntraImports + CommonSetup + EntraSetup + CommonExecution,
        _ => CommonSetup + CoreSetup + CommonExecution
    };

    private const string CommonSetup = """
        using System.Runtime.InteropServices;
        using System.Text.Json;
        using CIS.Security;
        using CIS.Security.Hosting;
        using CIS.Startup;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;

        const string selected = "selected";
        var target = new CapabilityTarget("country-api", "package-acceptance");
        var policy = new SecurityOptions { PolicyVersion = "package-security-v1" };
        policy.Capabilities.Add("country.read");
        policy.Capabilities.Add("country.change");
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        int policyCaptures = 0;
        int providerCaptures = 0;
        int outgoingCredentialFactories = 0;
        services.AddCisSecurity(_ =>
        {
            policyCaptures++;
            return policy;
        });
        string adapterPath = "";

        """;

    private const string CoreSetup = """
        const string profile = "CoreOnly";
        Action<HttpContext> supplyNativeInput = _ => { };

        """;

    private const string CertificateImports = """
        using System.Formats.Asn1;
        using System.Security.Cryptography;
        using System.Security.Cryptography.X509Certificates;
        using CIS.Security.Certificates;
        using CIS.Security.Certificates.Hosting;

        """;

    private const string CertificateSetup = """
        const string profile = "Certificates";
        const string scheme = "selected-native";
        const string identity = "https://identity.example.test/applications/package-caller/environments/package-acceptance";
        using RSA authorityKey = RSA.Create(2048);
        var authorityRequest = new CertificateRequest("CN=Package authority " + Guid.NewGuid().ToString("N"),
            authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        authorityRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(authorityRequest.PublicKey, false));
        using X509Certificate2 authority = authorityRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        using RSA leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=Package client", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        leafRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(leafRequest.PublicKey, false));
        leafRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority,
            includeKeyIdentifier: true, includeIssuerAndSerial: true));
        var names = new AsnWriter(AsnEncodingRules.DER);
        names.PushSequence();
        names.WriteCharacterString(UniversalTagNumber.IA5String, identity, new Asn1Tag(TagClass.ContextSpecific, 6));
        names.PopSequence();
        leafRequest.CertificateExtensions.Add(new X509Extension("2.5.29.17", names.Encode(), false));
        using X509Certificate2 leaf = leafRequest.Create(authority, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(20), RandomNumberGenerator.GetBytes(16));
        var subject = new SecuritySubject("package-client-issuer", identity);
        var trust = new CertificateTrustPolicy(subject.Issuer, [authority], [authority], revocation: CertificateRevocationPolicy.ShortLived);
        policy.Profiles.Add(new(selected, SecurityProfileKind.ClientCertificate, SecurityProfileRoles.Inbound, scheme,
            [new(subject, "package-caller", "package-acceptance")]));
        policy.Grants.Add(new(selected, "country.read", target.Application, target.Environment, subject: subject));
        services.AddCisCertificateAuthentication(selected, scheme, _ =>
        {
            providerCaptures++;
            return new CertificateInboundOptions(trust);
        });
        adapterPath = typeof(CertificateTrustPolicy).Assembly.Location;
        // Controlled native connection input proves this package boundary, not a TLS negotiation.
        Action<HttpContext> supplyNativeInput = context => context.Connection.ClientCertificate = leaf;

        """;

    private const string EntraImports = """
        using System.IdentityModel.Tokens.Jwt;
        using System.Security.Claims;
        using System.Security.Cryptography;
        using CIS.Security.Entra;
        using Microsoft.IdentityModel.Protocols;
        using Microsoft.IdentityModel.Protocols.OpenIdConnect;
        using Microsoft.IdentityModel.Tokens;

        """;

    private const string EntraSetup = """
        const string profile = "Entra";
        const string scheme = "selected-native";
        const string tenant = "11111111-1111-1111-1111-111111111111";
        const string objectId = "22222222-2222-2222-2222-222222222222";
        const string clientId = "33333333-3333-3333-3333-333333333333";
        const string issuer = "https://login.microsoftonline.com/" + tenant + "/v2.0";
        const string audience = "api://package-api";
        using RSA signing = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(signing) { KeyId = "package-signing-key" };
        var metadata = new OpenIdConnectConfiguration { Issuer = issuer };
        metadata.SigningKeys.Add(signingKey);
        var subject = new SecuritySubject(issuer, objectId);
        policy.Profiles.Add(new(selected, SecurityProfileKind.EntraWorkloadIdentity, SecurityProfileRoles.Inbound, scheme,
            [new(subject, "package-caller", "package-acceptance", clientApplicationId: clientId)]));
        policy.Grants.Add(new(selected, "country.read", target.Application, target.Environment, subject: subject, requiredProviderRole: "reader"));
        services.AddCisEntraSecurity(selected, _ =>
        {
            providerCaptures++;
            return new EntraSecurityOptions
            {
                TenantId = tenant,
                Inbound = new() { Audience = audience, MetadataManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata) },
                Outbound = new() { ClientId = "unused-invalid-outbound", FederatedTokenFile = Path.GetFullPath("must-not-be-read") }
            };
        }, scheme, borrowedCredential: _ =>
        {
            outgoingCredentialFactories++;
            throw new InvalidOperationException("A receiving-only profile must never create an outgoing credential.");
        });
        adapterPath = typeof(EntraSecurityOptions).Assembly.Location;
        var token = new JwtSecurityToken(issuer, audience,
            [new Claim("tid", tenant), new("oid", objectId), new("azp", clientId), new("ver", "2.0"), new("idtyp", "app"), new("roles", "reader")],
            notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        string encoded = new JwtSecurityTokenHandler().WriteToken(token);
        Action<HttpContext> supplyNativeInput = context => context.Request.Headers.Authorization = "Bearer " + encoded;

        """;

    private const string CommonExecution = """
        services.AddCisSecurityWeb(options => options.SelectProfile = _ => profile == "CoreOnly" ? null : selected);
        bool cacheRegistered = services.Any(descriptor => descriptor.ServiceType.FullName == "Microsoft.Extensions.Caching.Memory.IMemoryCache");
        Require(!cacheRegistered, "Installing a certificate/JWT package must not install a principal cache.");
        bool registrationWasPure = policyCaptures == 0 && providerCaptures == 0 && outgoingCredentialFactories == 0;
        await using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        registrationWasPure &= policyCaptures == 0 && providerCaptures == 0 && outgoingCredentialFactories == 0;
        Require(registrationWasPure, "Registration and provider construction must leave deferred policies/providers untouched.");
        foreach (IStartable participant in root.GetServices<IStartable>())
        {
            await participant.StartAsync(CancellationToken.None);
        }
        Require(policyCaptures == 1 && providerCaptures == (profile == "CoreOnly" ? 0 : 1), "Prepare each selected runtime once.");
        Require(outgoingCredentialFactories == 0, "Inbound-only activation must not construct an outgoing SDK credential.");
        IReadOnlyList<SecurityProfileStatus> statuses = root.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus();
        bool selectedRolesAvailable = statuses.All(status => status.Availability == SecurityProfileAvailability.Available);
        bool allowed = false;
        bool denied = false;
        bool crossTargetDenied = false;
        bool callerEstablished = false;
        var pipeline = new ApplicationBuilder(root);
        pipeline.UseCisSecurityAuthentication();
        pipeline.Run(async context =>
        {
            ICapabilityAuthorizer authorizer = context.RequestServices.GetRequiredService<ICapabilityAuthorizer>();
            callerEstablished = context.RequestServices.GetRequiredService<ICallerContextAccessor>().Current is not null;
            allowed = (await authorizer.AuthorizeAsync("country.read", target)).IsAllowed;
            denied = !(await authorizer.AuthorizeAsync("country.change", target)).IsAllowed;
            crossTargetDenied = !(await authorizer.AuthorizeAsync("country.read", new(target.Application, "OtherEnvironment"))).IsAllowed;
        });
        await using (AsyncServiceScope request = root.CreateAsyncScope())
        {
            var context = new DefaultHttpContext { RequestServices = request.ServiceProvider };
            context.Request.Scheme = "https";
            supplyNativeInput(context);
            await pipeline.Build()(context);
            Require(request.ServiceProvider.GetRequiredService<ICallerContextAccessor>().Current is null,
                "The native middleware must restore caller absence after the operation.");
        }
        Require(allowed == (profile != "CoreOnly") && denied && crossTargetDenied, "The actual complete-grant decision must match selected authority.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            profile, registrationWasPure, allowed, denied, crossTargetDenied, callerEstablished,
            statusCount = statuses.Count, selectedRolesAvailable, outgoingCredentialFactories, cacheRegistered,
            framework = RuntimeInformation.FrameworkDescription,
            corePath = typeof(SecurityOptions).Assembly.Location, adapterPath,
            loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name!).Order(StringComparer.Ordinal).ToArray()
        }));

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
        """;
}
