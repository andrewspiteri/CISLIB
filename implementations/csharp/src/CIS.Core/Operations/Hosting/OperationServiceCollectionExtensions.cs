using Microsoft.Extensions.DependencyInjection;

namespace CIS.Operations.Hosting;

/// <summary>Passive definition registration in the application's existing service collection.</summary>
public static class OperationServiceCollectionExtensions
{
    /// <summary>Registers one immutable definition and finite metadata without constructing runtime/scoped services.</summary>
    /// <remarks>Definition registration does not open a database, install schema or register a throwing service placeholder.</remarks>
    public static IServiceCollection AddCisOperation<TRequest, TResult>(this IServiceCollection services,
        OperationDefinition<TRequest, TResult> definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        (OperationDefinition[] existing, OperationsComposition? previous) = InspectRegistration(services);
        if (existing.Any(item => item.Name == definition.Name))
        {
            throw new ArgumentException("An operation alias can be registered only once.", nameof(definition));
        }
        if (existing.Length >= OperationsComposition.MaximumOperations)
        {
            throw new InvalidOperationException("Register at most sixteen operation aliases.");
        }
        ServiceDescriptor? descriptor = services.SingleOrDefault(item => item.ServiceType == typeof(OperationsComposition));
        object identity = previous?.RegistrationIdentity ?? new object();
        services.AddSingleton<OperationDefinition>(definition);
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }
        services.AddSingleton(new OperationsComposition(identity, existing.Append(definition).Select(item => item.Name)));
        return services;
    }

    private static (OperationDefinition[] Definitions, OperationsComposition? Composition) InspectRegistration(IServiceCollection services)
    {
        var existing = new List<OperationDefinition>();
        foreach (ServiceDescriptor descriptor in services.Where(item => item.ServiceType == typeof(OperationDefinition)))
        {
            if (descriptor.IsKeyedService || descriptor.ImplementationInstance is not OperationDefinition definition)
            {
                throw new InvalidOperationException("Use passive AddCisOperation definition instances, without opaque or keyed factories.");
            }
            existing.Add(definition);
        }
        if (existing.Count > OperationsComposition.MaximumOperations
            || existing.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != existing.Count)
        {
            throw new InvalidOperationException("Existing operation definitions must be finite and have unique ordinal aliases.");
        }
        ServiceDescriptor[] compositions = services.Where(item => item.ServiceType == typeof(OperationsComposition)).ToArray();
        if (compositions.Length > 1)
        {
            throw new InvalidOperationException("Register one passive operation composition.");
        }
        OperationsComposition? composition = null;
        if (compositions.Length == 1)
        {
            ServiceDescriptor descriptor = compositions[0];
            if (descriptor.IsKeyedService || descriptor.ImplementationInstance is not OperationsComposition captured
                || captured.RegistrationIdentity is null)
            {
                throw new InvalidOperationException("Operation composition must be the passive registration instance.");
            }
            composition = captured;
        }
        string[] names = existing.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        if (composition is null ? existing.Count != 0 : !composition.OperationNames.SequenceEqual(names, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Existing definitions and passive composition must come from the same registration.");
        }
        return (existing.ToArray(), composition);
    }
}
