using Microsoft.Extensions.DependencyInjection;

namespace CIS.Workflows.Hosting;

internal sealed class StateMachineFactory(IServiceScopeFactory scopes, PipelineLifetime lifetime,
    StateMachineInstrumentation instrumentation, TimeProvider clock) : IStateMachineFactory
{
    public StateMachineBuilder<TState, TTrigger, TContext> CreateBuilder<TState, TTrigger, TContext>(
        string name, TContext context, StateMachineOptions? options = null)
        where TState : struct, Enum where TTrigger : struct, Enum where TContext : class
    {
        return new(new StateMachineEnvironment(clock, () => PipelineServiceActivation.CreateOwned(scopes),
            lifetime, instrumentation.ValidateDefinition, instrumentation.Begin), name, context, options);
    }
}
