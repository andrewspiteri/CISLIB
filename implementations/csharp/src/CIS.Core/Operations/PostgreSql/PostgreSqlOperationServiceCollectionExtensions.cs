using CIS.Operations.Hosting;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Operations.PostgreSql;

/// <summary>Explicit passive selection of privileged maintenance for the application's selected PostgreSQL journal.</summary>
public static class PostgreSqlOperationServiceCollectionExtensions
{
    /// <summary>Registers the separate scoped maintenance port using the exact passively registered PostgreSQL journal instance.</summary>
    /// <remarks>Register one IOperationJournal instance first. No options, schema, source, scheduler or I/O is selected here.
    /// Empty definition MaintenanceCapabilities leaves that operation's maintenance disabled.</remarks>
    public static IServiceCollection AddCisPostgreSqlOperationRetentionMaintenance(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ServiceDescriptor[] journals = services.Where(item => item.ServiceType == typeof(IOperationJournal)).ToArray();
        if (journals.Length != 1 || journals[0].IsKeyedService
            || journals[0].ImplementationInstance is not PostgreSqlOperationJournal journal)
        {
            throw new InvalidOperationException("Register one passive PostgreSqlOperationJournal instance as IOperationJournal before selecting its maintenance.");
        }
        services.AddCisOperations();
        services.TryAddScoped<IOperationRetentionMaintenance>(provider =>
        {
            (OperationDefinition[] definitions, OperationsComposition? composition) = OperationServiceCollectionExtensions.InspectRegistration(services);
            OperationRegistrationSnapshot snapshot = provider.GetRequiredService<OperationRegistrationSnapshot>();
            if (!ReferenceEquals(composition, snapshot.Composition)
                || !definitions.SequenceEqual(snapshot.Definitions, ReferenceEqualityComparer.Instance)
                || !ReferenceEquals(journal, provider.GetRequiredService<IOperationJournal>()))
            {
                throw new InvalidOperationException("Maintenance must share the selected journal and immutable operation metadata snapshot.");
            }
            return new PostgreSqlOperationRetentionMaintenance(provider, snapshot.Definitions,
                provider.GetRequiredService<LocalApplicationIdentity>(), provider.GetRequiredService<ICapabilityAuthorizer>(),
                provider.GetRequiredService<ICallerContextAccessor>(), provider.GetRequiredService<ILocalTransactionFactory>(), journal);
        });
        return services;
    }
}
