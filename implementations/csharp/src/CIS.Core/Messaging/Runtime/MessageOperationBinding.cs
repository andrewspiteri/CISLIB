using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Messaging;

internal static class MessageOperationBindings
{
    internal static async Task<MessageOperationBinding> ResolveAsync<TRequest, TResponse>(IServiceProvider services,
        MessageRequestSubscriptionDefinition<TRequest, TResponse> subscription, MessageOperationReference reference,
        LocalApplicationIdentity local, CancellationToken cancellationToken)
    {
        OperationRegistrationSnapshot registration = services.GetRequiredService<OperationRegistrationSnapshot>();
        OperationDefinition<TRequest, TResponse> operation = registration.Definitions
            .SingleOrDefault(item => item.Name == subscription.Request.OperationName) as OperationDefinition<TRequest, TResponse>
            ?? throw new InvalidOperationException("A durable message binding requires its exact registered Operations input/result types.");
        if (operation.Name != reference.OperationName || operation.DatabaseName != subscription.DatabaseName)
        {
            throw new InvalidOperationException("The durable operation and message must share the registered name and original database.");
        }
        IOperationScopeResolver resolver = operation.ScopeResolver(services)
            ?? throw new InvalidOperationException("The registered operation returned no trusted scope resolver.");
        OperationScope scope = await resolver.ResolveAsync(operation.Name, reference.RequestedScope,
            services.GetRequiredService<ICallerContextAccessor>().Current, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The operation resolver returned no trusted scope.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(local.Application, local.Environment, scope.Scope, operation.Name, reference.Key),
            new(local.Application, local.Environment, scope.ResourceType, scope.ResourceIdentifier));
    }
}
