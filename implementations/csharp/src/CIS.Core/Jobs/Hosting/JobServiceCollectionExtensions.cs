using CIS.Coordination.Hosting;
using CIS.Configuration;
using CIS.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CIS.Jobs.Hosting;

/// <summary>Selects the existing catalog and explicit workload scopes in the one application provider.</summary>
public static class JobServiceCollectionExtensions
{
    /// <summary>Captures disabled-by-default settings and explicit targets without constructing handlers or performing provider I/O.</summary>
    public static IServiceCollection AddCisJobs(this IServiceCollection services, JobConfigurationSnapshot? configuration = null,
        JobHostingOptions? hostingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(JobsComposition)))
        {
            throw new InvalidOperationException("Configure one complete jobs hosting composition.");
        }

        IJobCatalog catalog = services.LastOrDefault(item => item.ServiceType == typeof(IJobCatalog))?.ImplementationInstance as IJobCatalog
            ?? services.LastOrDefault(item => item.ServiceType == typeof(JobCatalog))?.ImplementationInstance as JobCatalog
            ?? throw new InvalidOperationException("Freeze one passive explicit job catalog before selecting jobs hosting.");
        JobConfigurationSnapshot captured = configuration ?? new(0, limits: catalog.Limits);
        catalog.ValidateConfiguration(captured);
        JobHostingOptions options = (hostingOptions ?? new()).Copy(catalog, captured.Limits);
        CoordinationComposition coordination = CoordinationServiceCollectionExtensions.EnsureCoordination(services);
        var composition = new JobsComposition(options);
        services.AddSingleton(composition);
        CoordinationServiceCollectionExtensions.RegisterOwner(services, coordination.Lifetime, root => new JobRuntime(
            root.GetRequiredService<IJobCatalog>(), root.GetRequiredService<IJobExecutionScopeFactory>(), captured,
            root.GetService<IJobRunSource>(), root.GetService<IJobScheduleEvaluator>(), root.GetService<TimeProvider>(),
            root.GetService<IManagedWorkAdmission>()), coordination.Lifetime.Bind);
        services.TryAddSingleton<ContinuousJobRunner>();
        services.TryAddSingleton<IContinuousJobRunner>(root => root.GetRequiredService<ContinuousJobRunner>());
        services.TryAddSingleton<ScheduledJobDispatcher>();
        services.TryAddSingleton<IScheduledJobDispatcher>(root => root.GetRequiredService<ScheduledJobDispatcher>());
        services.TryAddSingleton(root =>
        {
            using IDisposable creation = coordination.Lifetime.EnterBinding();
            var observations = new JobInstrumentation(composition, SelectedRuntime(root),
                root.GetService<TimeProvider>(), root.GetService<ILoggerFactory>());
            coordination.Lifetime.Bind(observations);
            return observations;
        });
        services.AddSingleton(root =>
        {
            using IDisposable creation = coordination.Lifetime.EnterBinding();
            var supervisor = new JobHostingSupervisor(SelectedRuntime(root), options);
            coordination.Lifetime.Bind(supervisor);
            return supervisor;
        });
        return services;
    }

    /// <summary>Connects one retained Configuration root using the existing whole-copy coordinated reader at sequential startup.</summary>
    public static IServiceCollection AddCisJobConfiguration<TConfiguration>(this IServiceCollection services,
        Func<TConfiguration, long, JobConfigurationSnapshot> capture) where TConfiguration : class, IConfigurationObject
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(capture);
        if (!services.Any(item => item.ServiceType == typeof(JobsComposition))
            || services.Any(item => item.ServiceType == typeof(IJobConfigurationBinding)))
        {
            throw new InvalidOperationException("Select jobs hosting and one complete configuration binding.");
        }

        services.AddSingleton<IJobConfigurationBinding>(root =>
        {
            CoordinationHostLifetime lifetime = root.GetRequiredService<CoordinationHostLifetime>();
            using IDisposable creation = lifetime.EnterBinding();
            var binding = new JobConfigurationSubscription<TConfiguration>(root.GetRequiredService<JobRuntime>(),
                root.GetRequiredService<IConfigurationReader>(), root.GetRequiredService<TConfiguration>(), capture);
            lifetime.Bind(binding);
            return binding;
        });
        return services;
    }

    private static JobRuntime SelectedRuntime(IServiceProvider root)
    {
        JobRuntime runtime = root.GetRequiredService<JobRuntime>();
        if (!ReferenceEquals(runtime.ManagedAdmission, root.GetService<IManagedWorkAdmission>()))
        {
            throw new InvalidOperationException("A supplied Jobs runtime must capture the same optional managed-admission port as its host.");
        }
        return runtime;
    }
}
