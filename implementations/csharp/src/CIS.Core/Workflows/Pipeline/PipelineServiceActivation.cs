using Microsoft.Extensions.DependencyInjection;

namespace CIS.Workflows;

internal sealed class PipelineServiceActivation : IPipelineActivation
{
    private readonly IServiceProvider services;
    private readonly IAsyncDisposable? ownedScope;

    private PipelineServiceActivation(IServiceProvider services, IAsyncDisposable? ownedScope)
    {
        this.services = services;
        this.ownedScope = ownedScope;
    }

    internal static IPipelineActivation CreateOwned(IServiceScopeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        AsyncServiceScope scope = factory.CreateAsyncScope();
        return new PipelineServiceActivation(scope.ServiceProvider, scope);
    }

    internal static IPipelineActivation Borrowed(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new PipelineServiceActivation(services, null);
    }

    public object Activate(Type stepType)
    {
        return ActivatorUtilities.CreateInstance(services, stepType);
    }

    public ValueTask DisposeAsync()
    {
        return ownedScope?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
