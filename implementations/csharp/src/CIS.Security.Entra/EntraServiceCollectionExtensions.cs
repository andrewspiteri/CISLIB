using Azure.Identity;
using CIS.Configuration;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Security.Hosting;
using CIS.Startup;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CIS.Security.Entra;

/// <summary>Explicit Entra integration for one already-selected common security profile.</summary>
public static class EntraServiceCollectionExtensions
{
    /// <summary>Defers detached provider setup until ordinary root activation, after initial Configuration preparation.</summary>
    /// <param name="services">The application's one native service collection.</param>
    /// <param name="profile">The exact selected common Entra profile alias.</param>
    /// <param name="optionsFactory">Returns detached trusted setup without acquiring credentials.</param>
    /// <param name="authenticationScheme">The selected receiving scheme, or null for an outbound-only profile.</param>
    /// <param name="borrowedCredential">Optional trusted native integration returning an already configured WorkloadIdentityCredential at Startup. Its owner retains disposal responsibility.</param>
    public static IServiceCollection AddCisEntraSecurity(this IServiceCollection services, string profile,
        Func<IServiceProvider, EntraSecurityOptions> optionsFactory, string? authenticationScheme = null,
        Func<IServiceProvider, WorkloadIdentityCredential>? borrowedCredential = null)
    {
        ArgumentNullException.ThrowIfNull(optionsFactory);
        string alias = EntraText.Identifier(profile, nameof(profile), 64);
        return Register(services, alias, authenticationScheme,
            provider => new(provider.GetRequiredService<SecurityPolicySnapshot>(), alias,
                authenticationScheme, optionsFactory(provider)), borrowedCredential);
    }

    /// <summary>Copies and validates the whole provider setup while holding the existing Configuration read gate.</summary>
    public static IServiceCollection AddCisEntraSecurity<TConfiguration>(this IServiceCollection services, string profile,
        Func<TConfiguration, EntraSecurityOptions> select, string? authenticationScheme = null,
        Func<IServiceProvider, WorkloadIdentityCredential>? borrowedCredential = null)
        where TConfiguration : class, IConfigurationObject
    {
        ArgumentNullException.ThrowIfNull(select);
        string alias = EntraText.Identifier(profile, nameof(profile), 64);
        return Register(services, alias, authenticationScheme, provider =>
        {
            SecurityPolicySnapshot policy = provider.GetRequiredService<SecurityPolicySnapshot>();
            return provider.GetRequiredService<IConfigurationReader>().Read(provider.GetRequiredService<TConfiguration>(),
                root => new EntraOptionsSnapshot(policy, alias, authenticationScheme, select(root)));
        }, borrowedCredential);
    }

    private static IServiceCollection Register(IServiceCollection services, string alias, string? scheme,
        Func<IServiceProvider, EntraOptionsSnapshot> capture,
        Func<IServiceProvider, WorkloadIdentityCredential>? borrowedCredential)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (scheme is not null)
        {
            _ = EntraText.Identifier(scheme, nameof(scheme), 64);
        }
        if (services.Any(descriptor => descriptor.ServiceType == typeof(EntraRegistration)
            && descriptor.ImplementationInstance is EntraRegistration registration && registration.Profile == alias))
        {
            throw new InvalidOperationException("Register each Entra profile once.");
        }
        services.AddSingleton(new EntraRegistration(alias));
        services.AddKeyedSingleton<EntraProfileRuntime>(alias, (provider, _) => new(capture(provider),
            provider.GetRequiredService<ISecurityProfileStatusReporter>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetService<IHealthMonitorFactory>(), provider.GetService<IHeartbeatOwnerFactory>(), provider.GetService<ILogger<EntraProfileRuntime>>(),
            provider.GetService<IHostApplicationLifetime>(),
            borrowedCredential is null ? null : () => borrowedCredential(provider)));
        services.AddKeyedSingleton<ISecurityCredentialControl>(alias, (provider, _) =>
            new EntraCredentialControl(provider.GetRequiredKeyedService<EntraProfileRuntime>(alias)));
        services.AddSingleton<IStartable>(provider => new EntraStartable(provider.GetRequiredKeyedService<EntraProfileRuntime>(alias),
            scheme is null ? null : () => { _ = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(scheme); }));

        if (scheme is not null)
        {
            // AddAuthentication() also installs unrelated data-protection ownership. The common
            // integration already supplies the exact authentication-core dependencies.
            services.TryAddSingleton<IAuthenticationConfigurationProvider>(provider =>
                new EntraAuthenticationConfigurationProvider(provider.GetService<IConfiguration>()));
            new AuthenticationBuilder(services).AddJwtBearer(scheme, _ => { });
            services.AddOptions<JwtBearerOptions>(scheme)
                .Configure<IServiceProvider>((native, provider) => EntraJwtConfiguration.Apply(native,
                    provider.GetRequiredKeyedService<EntraProfileRuntime>(alias)))
                .PostConfigure<IServiceProvider>((native, provider) => EntraJwtConfiguration.Apply(native,
                    provider.GetRequiredKeyedService<EntraProfileRuntime>(alias)));
            services.AddSingleton<IValidateOptions<JwtBearerOptions>>(provider =>
                new EntraJwtOptionsValidator(scheme, provider.GetRequiredKeyedService<EntraProfileRuntime>(alias)));
            services.AddCisSecurityProjection(alias, provider =>
                new EntraIdentityProjector(provider.GetRequiredKeyedService<EntraProfileRuntime>(alias)));
        }
        return services;
    }

    private sealed record EntraRegistration(string Profile);

    private sealed class EntraStartable(EntraProfileRuntime runtime, Action? validateReceiving) : IStartable
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            validateReceiving?.Invoke();
            return runtime.StartAsync(cancellationToken);
        }
    }

    private sealed class EntraCredentialControl(EntraProfileRuntime runtime) : ISecurityCredentialControl
    {
        public string Profile => runtime.Profile;

        public ValueTask RequestSourceRecheckAsync(CancellationToken cancellationToken = default)
            => runtime.RequestSourceRecheckAsync(cancellationToken);
    }

    private sealed class EntraAuthenticationConfigurationProvider : IAuthenticationConfigurationProvider
    {
        internal EntraAuthenticationConfigurationProvider(IConfiguration? configuration)
        {
            AuthenticationConfiguration = (configuration ?? new ConfigurationBuilder().Build()).GetSection("Authentication");
        }

        public IConfiguration AuthenticationConfiguration
        {
            get;
        }
    }
}
