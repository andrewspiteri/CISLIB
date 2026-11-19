using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CIS.Workflows.Hosting;

/// <summary>Connects workflows to the application's existing provider and host.</summary>
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
        AddLifetime(services);
        services.TryAddSingleton<PipelineInstrumentation>();
        services.TryAddSingleton<IPipelineFactory>(root => new PipelineFactory(
            root.GetRequiredService<IServiceScopeFactory>(), root.GetRequiredService<PipelineLifetime>(),
            root.GetRequiredService<PipelineInstrumentation>(), root.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }

    /// <summary>Opts into reusable state machines with automatic observations and actual trigger draining.</summary>
    /// <remarks>Shares one lifetime bridge with AddCisWorkflows when both are selected. Register after
    /// ordinary business hosted services and before the cooperating CIS bridges. This does not register
    /// a Pipeline factory; select AddCisWorkflows separately when handlers create child Pipelines.</remarks>
    public static IServiceCollection AddCisStateMachines(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new StateMachineComposition());
        AddLifetime(services);
        services.TryAddSingleton<StateMachineInstrumentation>();
        services.TryAddSingleton<IStateMachineFactory>(root => new StateMachineFactory(
            root.GetRequiredService<IServiceScopeFactory>(), root.GetRequiredService<PipelineLifetime>(),
            root.GetRequiredService<StateMachineInstrumentation>(), root.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }

    private static void AddLifetime(IServiceCollection services)
    {
        services.TryAddSingleton(new PipelineHostingRegistration());
        services.TryAddSingleton(root => root.GetRequiredService<PipelineHostingRegistration>().CreateOwner());
        services.TryAddSingleton(new PipelineHostingOrder(services));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PipelineHostedService>());
    }

    /// <summary>Executes one trigger using an explicitly borrowed caller scope.</summary>
    /// <remarks>The caller keeps that scope alive until this call settles. The machine still owns its
    /// freshly activated handler. The provider is never saved for a later trigger. Custom machines may
    /// implement IStateMachineScopeExecution to support this adapter without a concrete runtime dependency.</remarks>
    public static Task<StateTransitionResult<TState, TTrigger>> TriggerInScope<TState, TTrigger>(
        this IStateMachine<TState, TTrigger> machine, TTrigger trigger, IServiceProvider scopedProvider,
        CancellationToken cancellationToken = default)
        where TState : struct, Enum where TTrigger : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(scopedProvider);
        if (machine is not IStateMachineScopeExecution<TState, TTrigger> scoped)
        {
            throw new ArgumentException("The machine does not support explicit caller-scope execution.", nameof(machine));
        }

        return scoped.TriggerInScope(trigger, scopedProvider, cancellationToken);
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
