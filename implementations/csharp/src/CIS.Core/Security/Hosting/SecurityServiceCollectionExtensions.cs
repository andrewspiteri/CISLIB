using CIS.Configuration;
using CIS.Startup;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CIS.Security.Hosting;

/// <summary>Explicit native security registration; collecting setup performs no credential I/O or premature resolution.</summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>Registers the exact independently captured policy used by authenticated pre-provider preparation.</summary>
    /// <remarks>The snapshot remains immutable. Reusing its exact identity permits supported native resource adoption;
    /// this overload performs no credential work and creates no temporary provider.</remarks>
    public static IServiceCollection AddCisSecurity(this IServiceCollection services, SecurityPolicySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(snapshot);
        return Register(services, _ => snapshot, snapshot.ProfileCapacity);
    }

    /// <summary>Captures directly supplied setup as immutable startup policy.</summary>
    public static IServiceCollection AddCisSecurity(this IServiceCollection services, SecurityOptions options,
        int profileCapacity = 16)
    {
        SecurityPolicySnapshot snapshot = SecurityPolicySnapshot.Capture(options, profileCapacity);
        return Register(services, _ => snapshot, profileCapacity);
    }

    /// <summary>Defers policy creation until ordinary DI construction after initial Configuration preparation.</summary>
    /// <remarks>The factory must return detached setup. Use the generic overload for a retained Configuration root.</remarks>
    public static IServiceCollection AddCisSecurity(this IServiceCollection services,
        Func<IServiceProvider, SecurityOptions> optionsFactory, int profileCapacity = 16)
    {
        ArgumentNullException.ThrowIfNull(optionsFactory);
        return Register(services, provider => SecurityPolicySnapshot.Capture(optionsFactory(provider), profileCapacity), profileCapacity);
    }

    /// <summary>Copies and validates the complete policy inside the existing Configuration read gate.</summary>
    public static IServiceCollection AddCisSecurity<TConfiguration>(this IServiceCollection services,
        Func<TConfiguration, SecurityOptions> select, int profileCapacity = 16)
        where TConfiguration : class, IConfigurationObject
    {
        ArgumentNullException.ThrowIfNull(select);
        return Register(services, provider => provider.GetRequiredService<IConfigurationReader>().Read(
            provider.GetRequiredService<TConfiguration>(), root => SecurityPolicySnapshot.Capture(select(root), profileCapacity)), profileCapacity);
    }

    /// <summary>Adds one explicitly named provider projection. Installing this registration does not select or activate a profile.</summary>
    public static IServiceCollection AddCisSecurityProjection<TProjector>(this IServiceCollection services, string profile)
        where TProjector : class, ISecurityIdentityProjector
    {
        ArgumentNullException.ThrowIfNull(services);
        string alias = SecurityText.Alias(profile, nameof(profile));
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ISecurityIdentityProjector)
            && descriptor.IsKeyedService && Equals(descriptor.ServiceKey, alias)))
        {
            throw new ArgumentException("A selected security profile has exactly one identity projector.", nameof(profile));
        }
        services.AddKeyedScoped<ISecurityIdentityProjector, TProjector>(alias);
        services.AddSingleton(new SecurityProjectionRegistration(alias));
        return services;
    }

    /// <summary>Adds a deferred scoped projection factory without constructing a provider or resolving services now.</summary>
    public static IServiceCollection AddCisSecurityProjection(this IServiceCollection services, string profile,
        Func<IServiceProvider, ISecurityIdentityProjector> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        string alias = SecurityText.Alias(profile, nameof(profile));
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ISecurityIdentityProjector)
            && descriptor.IsKeyedService && Equals(descriptor.ServiceKey, alias)))
        {
            throw new ArgumentException("A selected security profile has exactly one identity projector.", nameof(profile));
        }
        services.AddKeyedScoped<ISecurityIdentityProjector>(alias, (provider, _) => factory(provider));
        services.AddSingleton(new SecurityProjectionRegistration(alias));
        return services;
    }

    /// <summary>Configures explicit actual-listener selection, including operational branches without endpoint metadata.</summary>
    public static IServiceCollection AddCisSecurityWeb(this IServiceCollection services, Action<SecurityWebOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<SecurityWebOptions>().Configure(configure);
        return services;
    }

    private static IServiceCollection Register(IServiceCollection services,
        Func<IServiceProvider, SecurityPolicySnapshot> create, int profileCapacity)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(SecurityComposition)))
        {
            throw new InvalidOperationException("Select one complete CIS security policy per host.");
        }
        var composition = new SecurityComposition(profileCapacity);
        services.AddSingleton(composition);
        services.AddSingleton(composition.Telemetry);
        services.AddSingleton(provider =>
        {
            SecurityPolicySnapshot snapshot = create(provider);
            composition.Bind(snapshot, provider, provider.GetService<TimeProvider>() ?? TimeProvider.System);
            return snapshot;
        });
        services.AddScoped<SecurityCallerState>();
        services.AddScoped<ICallerContextAccessor>(provider => provider.GetRequiredService<SecurityCallerState>());
        services.AddScoped<ICapabilityAuthorizer, NativeCapabilityAuthorizer>();
        services.AddSingleton<ISecurityProfileStatusReporter>(provider =>
        {
            _ = provider.GetRequiredService<SecurityPolicySnapshot>();
            return composition;
        });
        services.AddSingleton<ISecurityProfileStatusSource>(provider =>
        {
            _ = provider.GetRequiredService<SecurityPolicySnapshot>();
            return composition;
        });
        services.TryAddSingleton<ISecurityAuditSink, NoopSecurityAuditSink>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddAuthenticationCore();
        services.AddWebEncoders();
        services.AddRouting();
        services.AddAuthorization();
        services.AddOptions<SecurityWebOptions>();
        services.AddSingleton<IPostConfigureOptions<AuthorizationOptions>, SecurityAuthorizationOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, CapabilityRequirementHandler>());
        PreserveNativeResultHandler(services, composition);
        var validation = ServiceDescriptor.Singleton<IStartable>(provider => new SecurityPolicyStartable(
            provider.GetRequiredService<SecurityPolicySnapshot>(), provider.GetRequiredService<IAuthenticationSchemeProvider>(),
            provider.GetServices<SecurityProjectionRegistration>().ToArray(), services, composition));
        int first = -1;
        for (int index = 0; index < services.Count; index++)
        {
            if (services[index].ServiceType == typeof(IStartable) && !services[index].IsKeyedService)
            {
                first = index;
                break;
            }
        }
        if (first < 0)
        {
            services.Add(validation);
        }
        else
        {
            services.Insert(first, validation);
        }
        return services;
    }

    private static void PreserveNativeResultHandler(IServiceCollection services, SecurityComposition composition)
    {
        ServiceDescriptor? original = services.LastOrDefault(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));
        if (original is not null)
        {
            services.Remove(original);
        }
        object key = SecurityAuthorizationMiddlewareResultHandler.NativeHandlerKey;
        if (original?.ImplementationInstance is { } instance)
        {
            services.Add(ServiceDescriptor.KeyedSingleton(typeof(IAuthorizationMiddlewareResultHandler), key, instance));
        }
        else if (original?.ImplementationType is { } implementation)
        {
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(IAuthorizationMiddlewareResultHandler), key, implementation, original.Lifetime));
        }
        else if (original?.ImplementationFactory is { } factory)
        {
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(IAuthorizationMiddlewareResultHandler), key,
                (provider, _) => factory(provider), original.Lifetime));
        }
        else
        {
            services.AddKeyedSingleton<IAuthorizationMiddlewareResultHandler>(key, new AuthorizationMiddlewareResultHandler());
        }
        var descriptor = ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, SecurityAuthorizationMiddlewareResultHandler>();
        services.Add(descriptor);
        composition.ResultHandlerRegistration = descriptor;
    }
}

internal sealed record SecurityProjectionRegistration(string Profile);

internal sealed class NoopSecurityAuditSink : ISecurityAuditSink
{
    public ValueTask AuthorizationCompletedAsync(SecurityAuthorizationAudit audit, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask OperationCompletedAsync(SecurityOperationAudit audit, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

internal sealed class SecurityPolicyStartable(SecurityPolicySnapshot policy, IAuthenticationSchemeProvider schemes,
    SecurityProjectionRegistration[] projections, IServiceCollection services, SecurityComposition composition) : IStartable
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateResultHandler(services, composition);
        foreach (SecurityProfile profile in policy.Profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((profile.Roles & SecurityProfileRoles.Inbound) != 0)
            {
                AuthenticationScheme? scheme = await schemes.GetSchemeAsync(profile.AuthenticationScheme!).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (scheme is null || !projections.Any(projection => projection.Profile == profile.Alias))
                {
                    throw new InvalidOperationException("Each inbound profile requires its selected native scheme and one registered projector.");
                }
            }
        }
    }

    internal static void ValidateResultHandler(IServiceCollection services, SecurityComposition composition)
    {
        ServiceDescriptor? effective = services.LastOrDefault(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));
        if (!ReferenceEquals(effective, composition.ResultHandlerRegistration))
        {
            throw new InvalidOperationException("Register a custom native authorization result handler before AddCisSecurity so its ownership and security observations are preserved.");
        }
    }
}
