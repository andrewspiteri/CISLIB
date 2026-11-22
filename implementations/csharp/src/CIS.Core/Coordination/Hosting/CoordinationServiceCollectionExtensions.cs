using CIS.Healthcheck;
using CIS.Security;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CIS.Coordination.Hosting;

/// <summary>Connects one explicit authority to the existing sequential host and scoped authorization.</summary>
public static class CoordinationServiceCollectionExtensions
{
    /// <summary>Registers passive metadata and factories. Register the application provider/resolver and native dependencies first.</summary>
    /// <remarks>Use one composition/Build. Provider validation and automatic activity begin only at sequential startup.
    /// Concurrent hosted start/stop is unsupported. Applications retain their data source and borrowed runtime ownership.</remarks>
    public static IServiceCollection AddCisCoordination(this IServiceCollection services, CoordinationOptions? options = null,
        CoordinationHostingOptions? hostingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(CoordinationComposition)))
        {
            throw new InvalidOperationException("Configure one complete coordination composition.");
        }

        CoordinationOptions captured = (options ?? new()).CopyValidated();
        bool critical = hostingOptions?.IsHealthCritical ?? true;
        var composition = new CoordinationComposition();
        services.AddSingleton(composition);
        services.AddSingleton(new CoordinationHostingPolicy(critical));
        services.AddSingleton(root =>
        {
            CoordinationHostLifetime lifetime = composition.Lifetime;
            if (root.GetService<IHostApplicationLifetime>() is { } host)
            {
                lifetime.ObserveHost(host);
            }
            return lifetime;
        });
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton(_ => LocalApplicationIdentity.Capture());
        RegisterOwner(services, composition.Lifetime, root => new CoordinationRuntime(root.GetRequiredService<ICoordinationProvider>(),
            captured, root.GetService<TimeProvider>()), composition.Lifetime.Bind);
        services.TryAddScoped<ICoordinationService, CoordinationService>();
        services.TryAddSingleton(root =>
        {
            CoordinationHostLifetime lifetime = root.GetRequiredService<CoordinationHostLifetime>();
            using IDisposable creation = lifetime.EnterBinding();
            var observations = new CoordinationInstrumentation(composition, root.GetRequiredService<CoordinationRuntime>(),
                root.GetService<TimeProvider>(), root.GetService<ILoggerFactory>());
            lifetime.Bind(observations);
            return observations;
        });
        DecorateGuard(services);
        services.AddSingleton(new CoordinationHostingOrder(services));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartable, CoordinationStartable>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CoordinationHostedService>());
        return services;
    }

    internal static CoordinationComposition EnsureCoordination(IServiceCollection services)
    {
        ServiceDescriptor? existing = services.SingleOrDefault(item => item.ServiceType == typeof(CoordinationComposition));
        if (existing is null)
        {
            services.AddCisCoordination();
            existing = services.Single(item => item.ServiceType == typeof(CoordinationComposition));
        }

        return existing.ImplementationInstance as CoordinationComposition
            ?? throw new InvalidOperationException("Coordination metadata is one passive registration instance.");
    }

    internal static void RegisterOwner<T>(IServiceCollection services, CoordinationHostLifetime lifetime,
        Func<IServiceProvider, T> create, Action<T> bind) where T : class
    {
        ServiceDescriptor[] existing = services.Where(item => item.ServiceType == typeof(T)).ToArray();
        if (existing.Length > 1 || existing.Any(item => item.IsKeyedService || item.Lifetime != ServiceLifetime.Singleton))
        {
            throw new InvalidOperationException("Select one unkeyed singleton runtime owner.");
        }

        if (existing.SingleOrDefault()?.ImplementationInstance is T borrowed)
        {
            bind(borrowed);
            return;
        }

        ServiceDescriptor? original = existing.SingleOrDefault();
        if (original is not null)
        {
            services.Remove(original);
        }
        services.AddSingleton(root =>
        {
            using IDisposable creation = lifetime.EnterBinding();
            T owner = original?.ImplementationFactory is { } factory ? (T)factory(root)
                : original?.ImplementationType is { } type ? (T)ActivatorUtilities.CreateInstance(root, type) : create(root);
            bind(owner);
            return owner;
        });
    }

    private static void DecorateGuard(IServiceCollection services)
    {
        object key = new();
        ServiceDescriptor[] descriptors = services.Where(item => item.ServiceType == typeof(ICoordinationGuard)).ToArray();
        if (descriptors.Length > 1 || descriptors.Any(item => item.IsKeyedService))
        {
            throw new InvalidOperationException("Select one ordinary coordination guard before its hosting bridge.");
        }

        ServiceDescriptor? original = descriptors.SingleOrDefault();
        if (original is null)
        {
            services.AddSingleton<ICoordinationGuard>(root => new ObservedCoordinationGuard(
                root.GetRequiredService<ICoordinationProvider>() as ICoordinationGuard
                    ?? throw new InvalidOperationException("The selected authority requires an explicit original-transaction ownership guard."),
                root.GetRequiredService<CoordinationInstrumentation>()));
            return;
        }

        services.Remove(original);
        if (original.ImplementationInstance is ICoordinationGuard borrowed)
        {
            services.AddKeyedSingleton(key, borrowed);
        }
        else if (original.ImplementationFactory is { } factory)
        {
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(ICoordinationGuard), key, (root, _) => factory(root), original.Lifetime));
        }
        else
        {
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(ICoordinationGuard), key, original.ImplementationType!, original.Lifetime));
        }

        services.Add(ServiceDescriptor.Describe(typeof(ICoordinationGuard), root => new ObservedCoordinationGuard(
            root.GetRequiredKeyedService<ICoordinationGuard>(key), root.GetRequiredService<CoordinationInstrumentation>()), original.Lifetime));
    }
}

internal sealed record CoordinationHostingPolicy(bool IsHealthCritical);
