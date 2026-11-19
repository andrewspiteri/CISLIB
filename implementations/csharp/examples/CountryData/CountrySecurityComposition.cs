using System.Net;
using CIS.Configuration;
using CIS.Hosting;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Certificates.Hosting;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Examples.CountryData;

/// <summary>Explicit example selection over the existing Configuration, Startup and native security adapters.</summary>
public static class CountrySecurityComposition
{
    public const string Profile = "country-security";
    public const string Scheme = "country-security";
    public const string CallerClient = "country-service";

    public static void Configure(CISApplicationBuilder builder, bool caller = false)
    {
        string mode = builder.Host.Configuration["security"] ?? "none";
        if (mode is not ("none" or "local" or "certificate" or "entra"))
        {
            throw new ArgumentException("Select --security=none|local|certificate|entra.");
        }
        if (mode == "none")
        {
            if (caller)
            {
                throw new ArgumentException("The caller example requires an explicit security profile.");
            }
            return;
        }
        string? publicConfiguration = builder.Host.Configuration["security-config"];
        if (publicConfiguration is not null)
        {
            if (!Path.IsPathFullyQualified(publicConfiguration))
            {
                throw new ArgumentException("Select an absolute public security configuration file.");
            }
            builder.Host.Configuration.AddJsonFile(publicConfiguration, optional: false, reloadOnChange: false);
        }
        if (builder.Configuration is null)
        {
            CountrySecuritySettings bootstrap = builder.Host.Configuration.Get<CountrySecuritySettings>() ?? new();
            builder.Configuration = new ConfigurationOptions(typeof(CountryCatalog).Assembly.GetName().Name!)
            {
                IsHealthCritical = true,
                Factories = new Dictionary<Type, Func<IConfigurationObject>>
                {
                    [typeof(CountrySecuritySettings)] = () => bootstrap.Copy()
                }
            };
        }
        builder.Services.AddSingleton(root => root.GetRequiredService<IConfigurationReader>().Read(
            root.GetRequiredService<CountrySecuritySettings>(), settings => new CountrySecuritySnapshot(settings.Copy())));
        builder.Services.AddSingleton(new CountrySecurityMode(mode, caller));
        builder.Services.AddCisSecurity(root => root.GetRequiredService<CountrySecuritySnapshot>().Settings.Policy(mode, caller));
        builder.Services.TryAddSingleton<RestHttpNativeKeyScope>(root => CountryNativeKeyCustody.Create(
            root.GetRequiredService<CountrySecuritySnapshot>().Settings.NativeKeyJournalDirectory));
        if (mode == "entra")
        {
            builder.Services.AddCisEntraSecurity(Profile, root =>
            {
                CountrySecuritySettings settings = root.GetRequiredService<CountrySecuritySnapshot>().Settings;
                return new()
                {
                    TenantId = settings.TenantId,
                    Inbound = caller ? null : new()
                    {
                        Audience = settings.Audience
                    },
                    Outbound = caller ? new()
                    {
                        ClientId = settings.CallerClientId,
                        ServicePrincipalObjectId = settings.CallerObjectId,
                        FederatedSubject = settings.FederatedSubject,
                        FederatedTokenFile = settings.FederatedTokenFile,
                        Scope = settings.Scope
                    } : null
                };
            }, caller ? null : Scheme);
            if (caller)
            {
                builder.Services.AddCisEntraRestClient(CallerOptions(builder), Profile);
            }
        }
        else
        {
            builder.Services.AddCisCertificatePemSecurity(Profile, root =>
            {
                // Capture the DI-owned key scope before the provider runtime so reverse disposal drains
                // the runtime's native generations before checking that this borrowed scope is empty.
                _ = root.GetRequiredService<RestHttpNativeKeyScope>();
                CountrySecuritySettings settings = root.GetRequiredService<CountrySecuritySnapshot>().Settings;
                CertificateDevelopmentGate? development = mode == "local"
                    ? new(settings.Environment, [Environments.Development]) : null;
                CountryCredentialReference? credential = caller ? settings.Credential(settings.CallerApplication) : null;
                return new(new(settings.TrustDomain, settings.CaCertificateFile,
                    settings.IssuingAuthorityFile ?? settings.CaCertificateFile,
                    revocation: mode == "local" ? CertificateRevocationPolicy.Development : CertificateRevocationPolicy.Online),
                    credential is null ? null : new(credential.CertificateFile, credential.PrivateKeyFile),
                    serverTrustBundleFile: settings.ServerTrustCertificateFile ?? (mode == "local" ? settings.CaCertificateFile : null),
                    serverRevocationMode: mode == "local" ? System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
                        : System.Security.Cryptography.X509Certificates.X509RevocationMode.Online,
                    development: development);
            }, caller ? null : Scheme, root => root.GetRequiredService<RestHttpNativeKeyScope>());
            if (caller)
            {
                builder.Services.AddCisCertificateRestClient(CallerOptions(builder), Profile);
            }
        }
        if (!caller)
        {
            builder.Services.AddSingleton<CountrySelection>();
            builder.Services.AddSingleton<CountryServerCertificate>();
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<CountryServerCertificate>());
            builder.Services.AddCisSecurityWeb(options => options.SelectProfile = context =>
            {
                CountrySecuritySettings settings = context.RequestServices.GetRequiredService<CountrySecuritySnapshot>().Settings;
                return context.Connection.LocalPort == settings.BusinessPort
                    && IPAddress.IsLoopback(context.Connection.LocalIpAddress ?? IPAddress.None) ? Profile : null;
            });
            builder.WebHost!.ConfigureKestrel(options =>
            {
                CountrySecuritySettings settings = options.ApplicationServices.GetRequiredService<CountrySecuritySnapshot>().Settings;
                if (settings.BusinessPort is < 1 or > 65535 || settings.OperationalPort is < 1 or > 65535
                    || settings.BusinessPort == settings.OperationalPort)
                {
                    throw new ArgumentException("Select distinct finite business TLS and operational ports.");
                }
                options.Listen(IPAddress.Loopback, settings.BusinessPort, listener => listener.UseHttps(https =>
                {
                    https.ServerCertificateSelector = (_, _) => options.ApplicationServices.GetRequiredService<CountryServerCertificate>().Certificate;
                    if (mode != "entra")
                    {
                        https.UseCisCertificateProfileTrust(options.ApplicationServices, Profile);
                    }
                }));
                options.Listen(IPAddress.Loopback, settings.OperationalPort);
            });
        }
    }

    private static RestClientOptions CallerOptions(CISApplicationBuilder builder)
    {
        string target = builder.Host.Configuration["target"] ?? "https://localhost:7443";
        var options = new RestClientOptions(CallerClient) { BaseUrl = target };
        foreach (string operation in new[] { "read", "select", "inspect" })
        {
            options.Operations.Add(operation);
        }
        return options;
    }

    public static void Map(WebApplication web)
    {
        if (web.Services.GetService<CountrySecurityMode>() is null)
        {
            web.MapGet("/country/{identifier}", CountryEndpoints.Get);
            return;
        }
        CountrySecuritySettings settings = web.Services.GetRequiredService<CountrySecuritySnapshot>().Settings;
        var listeners = new CertificateListenerPolicy([new(IPAddress.Loopback, settings.BusinessPort, Profile)],
            [new(IPAddress.Loopback, settings.OperationalPort)], ["/_health", "/_health/ready", "/_metrics"]);
        web.UseCisCertificateListenerIsolation(listeners);
        web.MapGet("/country/{identifier}", CountryEndpoints.Get)
            .RequireCisCapabilities(Profile, CountrySecurityResource.ResolveTarget, "country.read");
        web.MapGet("/selection", (CountrySelection selection) => Results.Ok(selection.Capture()))
            .RequireCisCapabilities(Profile, new CapabilityTarget(settings.ServiceApplication, settings.Environment), "country.read");
        web.MapPut("/selection/{identifier}", async (HttpContext context, CountrySelection selection, ISecurityAuditSink audit) =>
        {
            Country country = CountrySecurityResource.AuthorizedCountry(context);
            CountrySelectionSnapshot selected = selection.Select(country);
            CallerContext caller = context.RequestServices.GetRequiredService<ICallerContextAccessor>().Current
                ?? throw new InvalidOperationException("Protected work requires the established native caller.");
            await audit.OperationCompletedAsync(new(caller, ["country.select"],
                new(settings.ServiceApplication, settings.Environment, "country", country.Iso2),
                SecurityOperationOutcome.Completed, outcomeCode: "country.selected", correlationId: context.TraceIdentifier),
                context.RequestAborted);
            return Results.Ok(selected);
        })
            .RequireCisCapabilities(Profile, CountrySecurityResource.ResolveTarget, "country.select");
    }
}

internal sealed record CountrySecurityMode(string Mode, bool Caller);
