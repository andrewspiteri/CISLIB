using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.RestClient.Http;
using CIS.Startup;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Security.Certificates.Hosting;

/// <summary>Explicit file-backed certificate composition using existing Startup, Heartbeat and REST owners.</summary>
public static class CertificatePemServiceCollectionExtensions
{
    /// <summary>Registers immutable PEM references without file/key/network work or premature service resolution.</summary>
    /// <remarks>Only enabled roles load their files. Supplied native key scopes remain caller-owned.
    /// Trusted Configuration factories must build these immutable options inside their reader callback.</remarks>
    public static IServiceCollection AddCisCertificatePemSecurity(this IServiceCollection services, string profile,
        Func<IServiceProvider, CertificatePemSecurityOptions> optionsFactory, string? authenticationScheme = null,
        Func<IServiceProvider, RestHttpNativeKeyScope>? borrowedNativeKeys = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsFactory);
        string alias = new SecurityProfile(profile, SecurityProfileKind.ClientCertificate, SecurityProfileRoles.Inbound,
            authenticationScheme ?? "pem-reference-validation").Alias;
        if (services.Any(descriptor => descriptor.ServiceType == typeof(CertificatePemRegistration)
            && descriptor.ImplementationInstance is CertificatePemRegistration registration && registration.Profile == alias))
        {
            throw new InvalidOperationException("Register each PEM profile once.");
        }
        CertificateProfileAvailability.Register(services, alias);
        services.AddMetrics();
        services.AddSingleton(new CertificatePemRegistration(alias));
        services.AddKeyedSingleton<CertificatePemRuntime>(alias,
            (provider, _) => new(provider, alias, authenticationScheme, optionsFactory, borrowedNativeKeys));
        services.AddKeyedSingleton<ISecurityCredentialControl>(alias, (provider, _) =>
            new CertificatePemControl(provider.GetRequiredKeyedService<CertificatePemRuntime>(alias)));
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IStartable)
            && descriptor.ImplementationType == typeof(CertificatePemStartup)))
        {
            var initializer = ServiceDescriptor.Singleton<IStartable, CertificatePemStartup>();
            ServiceDescriptor? firstReceiver = services.Where(descriptor => descriptor.ServiceType == typeof(CertificateReceivingRegistration))
                .Select(descriptor => (CertificateReceivingRegistration)descriptor.ImplementationInstance!)
                .Select(registration => registration.Startable).OrderBy(services.IndexOf).FirstOrDefault();
            if (firstReceiver is null)
            {
                services.Add(initializer);
            }
            else
            {
                services.Insert(services.IndexOf(firstReceiver), initializer);
            }
        }
        if (authenticationScheme is not null)
        {
            CertificateAuthenticationExtensions.AddDeferredCertificateAuthentication(services, alias, authenticationScheme,
                provider => provider.GetRequiredKeyedService<CertificatePemRuntime>(alias).Receiving,
                provider => provider.GetRequiredKeyedService<CertificatePemRuntime>(alias).IsTrustPrepared);
        }
        return services;
    }

    /// <summary>Binds an existing REST alias to the exact provider-owned policy and stable native transport.</summary>
    public static IServiceCollection AddCisCertificateRestClient(this IServiceCollection services, RestClientOptions options, string profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        string alias = new SecurityProfile(profile, SecurityProfileKind.ClientCertificate,
            SecurityProfileRoles.Inbound, "pem-reference-validation").Alias;
        return services.AddCisSecurityBoundRestClient(options,
            provider => provider.GetRequiredKeyedService<CertificatePemRuntime>(alias).Binding
                ?? throw new InvalidOperationException("The certificate REST profile must enable its outgoing role."),
            borrowedTransport: (provider, _) => provider.GetRequiredKeyedService<CertificatePemRuntime>(alias).Transport);
    }

    /// <summary>Applies strict real-listener client trust from the profile's one Startup snapshot.</summary>
    /// <remarks>Use KestrelServerOptions.ApplicationServices during ordinary Kestrel setup. Before preparation the handshake denies;
    /// an anonymous operational listener remains separate. No certificate forwarding or accepting callback is installed.</remarks>
    public static HttpsConnectionAdapterOptions UseCisCertificateProfileTrust(this HttpsConnectionAdapterOptions options,
        IServiceProvider services, string profile)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);
        CertificatePemRuntime runtime = services.GetRequiredKeyedService<CertificatePemRuntime>(profile);
        options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        options.ClientCertificateValidation = (certificate, _, _) => runtime.ValidateTls(certificate);
        return options;
    }
}

internal sealed record CertificatePemRegistration(string Profile);

internal sealed class CertificatePemStartup(IServiceProvider services) : IStartable
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Every initial trust snapshot is prepared in sequence before receiving cross-profile isolation checks.
        foreach (CertificatePemRegistration registration in services.GetServices<CertificatePemRegistration>())
        {
            await services.GetRequiredKeyedService<CertificatePemRuntime>(registration.Profile)
                .StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class CertificatePemControl(CertificatePemRuntime runtime) : ISecurityCredentialControl
{
    public string Profile => runtime.Profile;
    public ValueTask RequestSourceRecheckAsync(CancellationToken cancellationToken = default) => runtime.RequestRecheckAsync(cancellationToken);
}
