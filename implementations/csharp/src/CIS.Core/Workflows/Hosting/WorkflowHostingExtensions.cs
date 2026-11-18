using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CIS.Workflows.Hosting;

/// <summary>Connects sequential workflows to the application's existing provider and host.</summary>
public static class WorkflowHostingExtensions
{
    /// <summary>Opts into workflow factories, automatic observations and actual execution draining.</summary>
    /// <remarks>Register after ordinary business hosted services and before CIS Heartbeat/Startup bridges.
    /// CISApplication arranges its cooperating bridges automatically. Disable concurrent hosted
    /// starting/stopping and always await host Stop before disposing its provider.</remarks>
    public static IServiceCollection AddCisWorkflows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new WorkflowsComposition());
        services.TryAddSingleton(new PipelineHostingRegistration());
        services.TryAddSingleton(root => root.GetRequiredService<PipelineHostingRegistration>().CreateOwner());
        services.TryAddSingleton<PipelineInstrumentation>();
        services.TryAddSingleton<IPipelineFactory>(root => new PipelineFactory(
            root.GetRequiredService<IServiceScopeFactory>(), root.GetRequiredService<PipelineLifetime>(),
            root.GetRequiredService<PipelineInstrumentation>(), root.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddSingleton(new PipelineHostingOrder(services));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PipelineHostedService>());
        return services;
    }

    /// <summary>Builds steps against an explicitly borrowed caller scope rather than a new execution scope.</summary>
    /// <remarks>The caller must keep the provider and its scope alive until Execute actually settles.
    /// The pipeline still owns and disposes the fresh step instances it activates.</remarks>
    public static PipelineBuilder<TContext> CreateBuilderInScope<TContext>(this IPipelineFactory factory,
        string name, TContext context, IServiceProvider scopedProvider, PipelineOptions? options = null)
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(scopedProvider);
        if (factory is not PipelineFactory integrated)
        {
            throw new ArgumentException("The caller-scope adapter requires the factory registered by AddCisWorkflows.", nameof(factory));
        }

        return integrated.CreateInScope(name, context, scopedProvider, options);
    }
}
