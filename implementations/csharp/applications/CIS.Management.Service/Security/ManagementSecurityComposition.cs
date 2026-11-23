using System.Net;
using CIS.Hosting;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Certificates.Hosting;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using CIS.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CIS.Management.Service.Security;

/// <summary>Application-owned selection of existing native security, Startup and listener contracts.</summary>
public static class ManagementSecurityComposition
{
    public const string AuthenticatedPolicy = "management-authenticated";

    public static ManagementSecurityOptions Configure(CISApplicationBuilder builder, ManagementSecurityOptions options,
        Action<SecurityOptions>? configureDelivery = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        if (builder.WebHost is null || !builder.EnableOptionalIntegrations)
        {
            throw new InvalidOperationException("Management requires the web facade and its prepared native integration stage.");
        }
        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(ManagementSecurityOptions)))
        {
            throw new InvalidOperationException("Capture management security once for the single native provider.");
        }

        ManagementSecurityOptions captured = options.Capture();
        builder.Services.AddSingleton(captured);
        var listener = new CertificateListenerPolicy(
            [new(IPAddress.Parse(captured.BusinessAddress), captured.BusinessPort, captured.Profile)],
            [new(IPAddress.Loopback, captured.OperationalPort)], ["/_health", "/_health/ready", "/_metrics"]);
        builder.Services.AddSingleton(new ManagementListenerState(listener));

        if (captured.BorrowedNativeKeyScope is { } borrowed)
        {
            builder.Services.AddSingleton(borrowed);
        }
        else
        {
            builder.Services.AddSingleton(_ => ManagementNativeKeyCustody.Create(captured.NativeKeyJournalDirectory));
        }

        if (captured.Mode == "native")
        {
            captured.NativeProvider!(builder.Services, captured.Profile, captured.AuthenticationScheme);
        }

        var policy = new SecurityOptions { PolicyVersion = captured.PolicyVersion };
        policy.Profiles.Add(new(captured.Profile, captured.Kind, SecurityProfileRoles.Inbound,
            captured.AuthenticationScheme, captured.IdentityMappings));
        foreach (string capability in new[]
        {
            "management.registrations.write", "management.applications.read",
            "management.applications.metadata.write", "management.operations.read"
        }.Concat(captured.AdditionalCapabilities))
        {
            policy.Capabilities.Add(capability);
        }
        foreach (CapabilityGrant grant in captured.Grants)
        {
            policy.Grants.Add(grant);
        }

        configureDelivery?.Invoke(policy);
        builder.Services.AddCisSecurity(policy);
        builder.Services.AddCisSecurityWeb(web => web.SelectProfile = listener.SelectProfile);
        builder.Services.AddAuthorization(authorization => authorization.AddPolicy(AuthenticatedPolicy,
            new AuthorizationPolicyBuilder(captured.AuthenticationScheme).RequireAuthenticatedUser().Build()));

        if (captured.Mode == "entra")
        {
            builder.Services.AddCisEntraSecurity(captured.Profile, _ => new()
            {
                TenantId = captured.EntraTenantId,
                AuthorityHost = captured.EntraAuthorityHost,
                Inbound = new()
                {
                    Audience = captured.EntraAudience,
                    ClockSkew = captured.EntraClockSkew,
                    MetadataManager = captured.EntraMetadataManager
                }
            }, captured.AuthenticationScheme);
        }
        else if (captured.Mode is "local" or "certificate")
        {
            builder.Services.AddCisCertificatePemSecurity(captured.Profile, services =>
            {
                _ = services.GetRequiredService<RestHttpNativeKeyScope>();
                return new(new(captured.ClientTrustDomain, captured.ClientRootBundleFile,
                    captured.ClientIssuingAuthorityBundleFile, captured.ClientIntermediateBundleFile,
                    captured.CertificateRevocation, captured.MaximumShortLivedLifetime),
                    development: captured.Mode == "local"
                        ? new CertificateDevelopmentGate(captured.LocalDevelopmentEnvironment, [Environments.Development]) : null);
            }, captured.AuthenticationScheme, services => services.GetRequiredService<RestHttpNativeKeyScope>());
        }

        builder.Services.AddSingleton<ManagementServerCertificate>();
        builder.Services.AddSingleton<IStartable>(services => services.GetRequiredService<ManagementServerCertificate>());
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Parse(captured.BusinessAddress), captured.BusinessPort, endpoint => endpoint.UseHttps(tls =>
            {
                tls.ServerCertificateChain = kestrel.ApplicationServices.GetRequiredService<ManagementServerCertificate>().PublicChain;
                tls.ServerCertificateSelector = (_, _) => kestrel.ApplicationServices.GetRequiredService<ManagementServerCertificate>().Certificate;
                if (captured.Mode is "local" or "certificate")
                {
                    tls.UseCisCertificateProfileTrust(kestrel.ApplicationServices, captured.Profile);
                }
            }));
            kestrel.Listen(IPAddress.Loopback, captured.OperationalPort);
        });
        return captured;
    }

    /// <summary>Verifies the early native operational isolation before the application maps its typed business routes.</summary>
    public static void Map(WebApplication application, ManagementSecurityOptions captured)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(captured);
        if (!ReferenceEquals(captured, application.Services.GetRequiredService<ManagementSecurityOptions>())
            || !application.Services.GetRequiredService<ManagementListenerState>().Installed)
        {
            throw new InvalidOperationException("The prepared management listener integration and captured security snapshot are required.");
        }
    }
}

internal sealed class ManagementListenerState(CertificateListenerPolicy policy)
{
    internal CertificateListenerPolicy Policy { get; } = policy;

    internal bool Installed
    {
        get; set;
    }
}

/// <summary>Attaches actual listener isolation in the facade's existing pre-health operational stage.</summary>
/// <remarks>The prepared type catalog discovers this public parameterless registrar before Metrics integration.
/// Other applications that merely reference this assembly select no management listener policy.</remarks>
public sealed class ManagementSecurityApplicationIntegration : ICISApplicationIntegration
{
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    public void ConfigureWeb(WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (application.Services.GetService<ManagementListenerState>() is not { } listener)
        {
            return;
        }
        if (listener.Installed)
        {
            throw new InvalidOperationException("Install management listener isolation once before operational endpoints.");
        }

        application.Use(async (context, next) =>
        {
            if (listener.Policy.SelectProfile(context) is not null
                && (context.Request.Path.StartsWithSegments("/_health", StringComparison.OrdinalIgnoreCase)
                    || context.Request.Path.StartsWithSegments("/_metrics", StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
        application.UseCisCertificateListenerIsolation(listener.Policy);
        listener.Installed = true;
    }
}
