using CIS.Healthcheck;
using CIS.Coordination.Hosting;
using CIS.Heartbeat;
using CIS.Jobs;
using CIS.Management.Wire;
using CIS.Messaging.Hosting;
using CIS.RestClient.Hosting;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CIS.Management.Hosting;

/// <summary>Explicit passive registration/inventory selection over existing Startup, REST and Heartbeat owners.</summary>
public static class ManagementRegistrationServiceCollectionExtensions
{
    /// <summary>Adds one process reporter after its local prerequisites; reading through IManagementClient remains passive.</summary>
    /// <remarks>Use the ordinary sequential Startup integration. The selected dedicated REST profile must have LogCalls=false.
    /// Existing hosting signals this owner early, closes REST admission, then joins actual reporting/Heartbeat settlement.</remarks>
    public static IServiceCollection AddCisManagementRegistration(this IServiceCollection services, ManagementRegistrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(item => item.ServiceType == typeof(ManagementRegistrationLifetime)
            || item.ServiceType == typeof(ManagementRegistration)
            || !item.IsKeyedService && (item.ServiceType == typeof(IManagedWorkAdmission) || item.ServiceType == typeof(ManagedWorkAdmission))))
        {
            throw new InvalidOperationException("Select one complete optional management registration composition.");
        }
        ManagementRegistrationOptions captured = options.Copy();
        RestClientRegistration[] native = services.Where(item => !item.IsKeyedService
            && item.ServiceType == typeof(RestClientRegistration)).Select(item => item.ImplementationInstance)
            .OfType<RestClientRegistration>().Where(item => item.Options.ClientName == captured.ClientName).ToArray();
        if (native.Length != 1 || native[0].Options.LogCalls || native[0].Options.Codec is not ManagementContentCodec
            || !services.Any(item => item.IsKeyedService && item.ServiceType == typeof(IManagementClient)
                && Equals(item.ServiceKey, captured.ClientName)))
        {
            throw new InvalidOperationException("Select the existing dedicated typed management alias with native LogCalls=false first.");
        }
        var lifetime = new ManagementRegistrationLifetime();
        services.AddSingleton(lifetime);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(root =>
        {
            using IDisposable construction = lifetime.EnterConstruction();
            var admission = new ManagedWorkAdmission(root.GetService<TimeProvider>());
            lifetime.BindAdmission(admission);
            return admission;
        });
        services.TryAddSingleton<IManagedWorkAdmission>(root => root.GetRequiredService<ManagedWorkAdmission>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthReadinessSource, ManagementReadinessSource>());
        services.TryAddSingleton<IManagementObservationSource>(root => root.GetService<IHealthSnapshotSource>() is { } health
            ? new HealthManagementObservationSource(health) : new HostObservationSource(root.GetService<IHostApplicationLifetime>()));
        services.TryAddSingleton<IManagementInventorySource, EmptyInventorySource>();
        services.AddSingleton(root =>
        {
            using IDisposable construction = lifetime.EnterConstruction();
            ManagedWorkAdmission admission = root.GetRequiredService<ManagedWorkAdmission>();
            if (!ReferenceEquals(admission, root.GetRequiredService<IManagedWorkAdmission>()))
            {
                throw new InvalidOperationException("Managed execution must use the actual reporting gate at every work boundary.");
            }
            var runtime = new ManagementRegistration(admission, root.GetRequiredKeyedService<IManagementClient>(captured.ClientName),
                root.GetRequiredService<IHeartbeatOwnerFactory>(), root.GetRequiredService<IManagementObservationSource>(),
                root.GetRequiredService<IManagementInventorySource>(), captured,
                root.GetServices<IManagedStateReconciler>(), root.GetService<TimeProvider>(),
                root.GetService<IHealthMonitorFactory>(), root.GetService<ILogger<ManagementRegistration>>());
            lifetime.Bind(runtime);
            return runtime;
        });
        var startup = ServiceDescriptor.Singleton<IStartable>(root => root.GetRequiredService<ManagementRegistration>());
        int beforeWork = services.ToList().FindIndex(item => !item.IsKeyedService && item.ServiceType == typeof(IStartable)
            && (item.ImplementationType == typeof(MessagingStartable) || item.ImplementationType == typeof(CoordinationStartable)));
        if (beforeWork < 0)
        {
            services.Add(startup);
        }
        else
        {
            services.Insert(beforeWork, startup);
        }
        return services;
    }

    /// <summary>Selects the explicit passive Jobs projection without resolving a handler or activating a job.</summary>
    /// <remarks>Call before selecting registration. Application contributions can instead replace the public inventory source.</remarks>
    public static IServiceCollection AddCisManagementJobsInventory(this IServiceCollection services, ManagementWireLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(IManagementInventorySource)))
        {
            throw new InvalidOperationException("Select one complete management inventory source.");
        }
        ManagementWireLimits selected = limits ?? new();
        services.AddSingleton<IManagementInventorySource>(root => new JobsManagementInventorySource(
            root.GetRequiredService<IJobCatalog>(), root.GetService<JobRuntime>(), selected));
        return services;
    }

    private sealed class EmptyInventorySource : IManagementInventorySource
    {
        public ManagementInventoryContent CaptureInventory(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new([], [], []);
        }
    }

    private sealed class ManagementReadinessSource(ManagedWorkAdmission admission) : IHealthReadinessSource
    {
        public bool IsReady => admission.TryAdmit();
    }

    private sealed class HostObservationSource(IHostApplicationLifetime? lifetime) : IManagementObservationSource
    {
        public ManagementObservationContent CaptureObservation(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(lifetime?.ApplicationStopping.IsCancellationRequested == true ? "stopping"
                : lifetime?.ApplicationStarted.IsCancellationRequested == true ? "ready" : "starting", "unknown");
        }
    }
}
