using Microsoft.Extensions.DependencyInjection;

namespace CIS.Workflows.Hosting;

internal sealed class PipelineFactory(IServiceScopeFactory scopes, PipelineLifetime lifetime,
    PipelineInstrumentation instrumentation, TimeProvider clock) : IPipelineFactory
{
    public PipelineBuilder<TContext> CreateBuilder<TContext>(string name, TContext context,
        PipelineOptions? options = null) where TContext : class
        => Create<TContext>(name, context, () => PipelineServiceActivation.CreateOwned(scopes), options);

    internal PipelineBuilder<TContext> CreateInScope<TContext>(string name, TContext context,
        IServiceProvider provider, PipelineOptions? options) where TContext : class
        => Create<TContext>(name, context, () => PipelineServiceActivation.Borrowed(provider), options);

    private PipelineBuilder<TContext> Create<TContext>(string name, TContext context,
        Func<IPipelineActivation> activate, PipelineOptions? options) where TContext : class
        => new(new PipelineEnvironment(clock, activate, lifetime,
            instrumentation.ValidateDefinition, instrumentation.Begin), name, context, options);
}
