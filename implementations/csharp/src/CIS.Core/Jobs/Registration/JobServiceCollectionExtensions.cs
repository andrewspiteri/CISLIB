using CIS.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Jobs.Registration;

/// <summary>Passive exact registrations in the application's one ordinary DI collection.</summary>
public static class JobServiceCollectionExtensions
{
    /// <summary>Registers one immutable category without constructing application services.</summary>
    public static IServiceCollection AddCisJobCategory(this IServiceCollection services, JobCategoryDefinition category)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(category);
        EnsureMutable(services);
        JobCategoryDefinition[] existing = Instances<JobCategoryDefinition>(services);
        if (existing.Any(item => item.ExecutionScope == category.ExecutionScope && item.CategoryId == category.CategoryId))
        {
            throw new ArgumentException("Register each exact scope/category once.", nameof(category));
        }

        services.AddSingleton(category);
        return services;
    }

    /// <summary>Registers one continuous-only binding and offers its concrete handler as an ordinary scoped service.</summary>
    public static IServiceCollection AddCisContinuousJob<THandler>(this IServiceCollection services, JobDefinition definition)
        where THandler : class, IContinuousJobHandler
        => Add<THandler>(services, JobHandlerBinding.Continuous<THandler>(definition));

    /// <summary>Registers one scheduled-only binding using the existing explicit application codec.</summary>
    public static IServiceCollection AddCisScheduledJob<T, THandler>(this IServiceCollection services, JobDefinition definition,
        IMessageCodec<T> codec) where THandler : class, IScheduledJobHandler<T>
        => Add<THandler>(services, JobHandlerBinding.Scheduled<T, THandler>(definition, codec));

    /// <summary>Registers one exact binding whose handler implements both declared modes; duplicate bindings are rejected.</summary>
    public static IServiceCollection AddCisCombinedJob<T, THandler>(this IServiceCollection services, JobDefinition definition,
        IMessageCodec<T> codec) where THandler : class, IContinuousJobHandler, IScheduledJobHandler<T>
        => Add<THandler>(services, JobHandlerBinding.Combined<T, THandler>(definition, codec));

    /// <summary>Freezes the complete passive catalog after all categories/bindings; performs no resolution, activation or I/O.</summary>
    /// <remarks>Use native DI ValidateOnBuild and ValidateScopes to check ordinary handler constructor dependencies.
    /// This method verifies concrete handler registration without constructing the handler or a second provider.</remarks>
    public static IServiceCollection AddCisJobCatalog(this IServiceCollection services, JobRuntimeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureMutable(services);
        JobCategoryDefinition[] categories = Instances<JobCategoryDefinition>(services);
        JobHandlerBinding[] bindings = Instances<JobHandlerBinding>(services);
        foreach (JobHandlerBinding binding in bindings)
        {
            if (!services.Any(item => !item.IsKeyedService && item.ServiceType == binding.HandlerType))
            {
                throw new InvalidOperationException("Every explicit job handler requires its concrete ordinary DI registration.");
            }
        }

        services.AddSingleton(new JobCatalog(categories, bindings, limits));
        services.TryAddSingleton<IJobCatalog>(provider => provider.GetRequiredService<JobCatalog>());
        return services;
    }

    private static IServiceCollection Add<THandler>(IServiceCollection services, JobHandlerBinding binding) where THandler : class
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureMutable(services);
        JobHandlerBinding[] existing = Instances<JobHandlerBinding>(services);
        JobDefinition definition = binding.Definition;
        if (existing.Any(item => item.Definition.ExecutionScope == definition.ExecutionScope && item.Definition.JobId == definition.JobId))
        {
            throw new ArgumentException("Register one explicit binding for each exact scope/job; modes are not merged.", nameof(binding));
        }

        services.AddSingleton(binding);
        services.TryAddScoped<THandler>();
        return services;
    }

    private static T[] Instances<T>(IServiceCollection services) where T : class
    {
        var instances = new List<T>();
        foreach (ServiceDescriptor descriptor in services.Where(item => item.ServiceType == typeof(T)))
        {
            if (descriptor.IsKeyedService || descriptor.ImplementationInstance is not T instance)
            {
                throw new InvalidOperationException("Job metadata requires passive unkeyed registration instances.");
            }

            instances.Add(instance);
        }

        return instances.ToArray();
    }

    private static void EnsureMutable(IServiceCollection services)
    {
        if (services.Any(item => item.ServiceType == typeof(JobCatalog)))
        {
            throw new InvalidOperationException("The job catalog is already frozen; build a new composition to change registrations.");
        }
    }
}
