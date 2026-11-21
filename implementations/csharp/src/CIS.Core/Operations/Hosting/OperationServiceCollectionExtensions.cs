using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CIS.Security;
using CIS.Transactions;

namespace CIS.Operations.Hosting;

/// <summary>Passive definition registration in the application's existing service collection.</summary>
public static class OperationServiceCollectionExtensions
{
    /// <summary>Registers the scoped coordinator in the existing provider without resolving application dependencies or opening a database.</summary>
    /// <remarks>Register an IOperationJournal, the existing transaction factory and established capability authorization.
    /// Definitions may be added before or after this call; final passive metadata is checked when a scope resolves the coordinator.</remarks>
    public static IServiceCollection AddCisOperations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = InspectRegistration(services);
        services.TryAddSingleton(new OperationRegistrationSnapshot([], null));
        services.TryAddSingleton<LocalApplicationIdentity>(_ => LocalApplicationIdentity.Capture());
        services.TryAddScoped<IDurableOperationService>(provider =>
        {
            (OperationDefinition[] definitions, OperationsComposition? composition) = InspectRegistration(services);
            OperationRegistrationSnapshot snapshot = provider.GetRequiredService<OperationRegistrationSnapshot>();
            if (!ReferenceEquals(composition, snapshot.Composition)
                || !definitions.SequenceEqual(snapshot.Definitions, ReferenceEqualityComparer.Instance))
            {
                throw new InvalidOperationException("Build a new provider after changing operation definitions; runtime and host metadata must share one snapshot.");
            }
            return new DurableOperationService(provider, snapshot.Definitions,
                provider.GetRequiredService<LocalApplicationIdentity>(),
                provider.GetRequiredService<ICapabilityAuthorizer>(),
                provider.GetRequiredService<ICallerContextAccessor>(),
                provider.GetRequiredService<IOperationJournal>(),
                provider.GetRequiredService<ILocalTransactionFactory>());
        });
        return services;
    }

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
        OperationDefinition[] definitions = existing.Append(definition).ToArray();
        var composition = new OperationsComposition(identity, definitions.Select(item => item.Name));
        services.AddSingleton(composition);
        services.RemoveAll<OperationRegistrationSnapshot>();
        services.AddSingleton(new OperationRegistrationSnapshot(definitions, composition));
        return services;
    }

    internal static (OperationDefinition[] Definitions, OperationsComposition? Composition) InspectRegistration(IServiceCollection services)
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

internal sealed record OperationRegistrationSnapshot(OperationDefinition[] Definitions, OperationsComposition? Composition);
