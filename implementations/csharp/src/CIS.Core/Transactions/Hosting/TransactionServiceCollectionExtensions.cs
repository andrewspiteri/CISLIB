using System.Data.Common;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Transactions.Hosting;

/// <summary>Passive local transaction composition in the application's one existing provider.</summary>
public static class TransactionServiceCollectionExtensions
{
    /// <summary>Registers the explicit factory once; equivalent captured runtime options are idempotent.</summary>
    public static IServiceCollection AddCisTransactions(this IServiceCollection services,
        LocalTransactionRuntimeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        TransactionRuntimeSettings captured = TransactionRuntimeSettings.Capture(options);
        TransactionRuntimeSettings? existing = services.Where(item => item.ServiceType == typeof(TransactionRuntimeSettings))
            .Select(item => item.ImplementationInstance).OfType<TransactionRuntimeSettings>().SingleOrDefault();
        if (existing is not null && existing != captured)
        {
            throw new InvalidOperationException("Conflicting local transaction runtime options were already registered.");
        }
        if (existing is null)
        {
            services.AddSingleton(captured);
        }
        services.TryAddSingleton(new TransactionsComposition(new object(), []));
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<TransactionInstrumentation>();
        services.TryAddSingleton<ILocalTransactionFactory>(provider => new LocalTransactionFactory(provider,
            provider.GetServices<TransactionDatabaseRegistration>().ToArray(),
            provider.GetRequiredService<TransactionRuntimeSettings>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<TransactionInstrumentation>()));
        return services;
    }

    /// <summary>Copies one named database's isolation contract without resolving or disposing its host-lived source.</summary>
    public static IServiceCollection AddCisAdoNetDatabase(this IServiceCollection services, string databaseName,
        Func<IServiceProvider, DbDataSource> dataSource, AdoNetDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        var captured = new TransactionDatabaseRegistration(databaseName, dataSource, options);
        TransactionDatabaseRegistration[] existing = services.Where(item => item.ServiceType == typeof(TransactionDatabaseRegistration))
            .Select(item => item.ImplementationInstance).OfType<TransactionDatabaseRegistration>().ToArray();
        if (existing.Any(item => item.Name == captured.Name))
        {
            throw new ArgumentException("A database alias can be registered only once.", nameof(databaseName));
        }
        if (existing.Length >= TransactionsComposition.MaximumDatabases)
        {
            throw new InvalidOperationException("The local transaction factory admits at most sixteen database aliases.");
        }
        if (!services.Any(item => item.ServiceType == typeof(TransactionRuntimeSettings)))
        {
            services.AddCisTransactions();
        }
        services.AddSingleton(captured);
        ServiceDescriptor compositionDescriptor = services.Single(item => item.ServiceType == typeof(TransactionsComposition));
        var composition = (TransactionsComposition)compositionDescriptor.ImplementationInstance!;
        TransactionDatabaseMetadata[] databases = existing.Append(captured).Select(item => new TransactionDatabaseMetadata(
            item.Name, item.DefaultIsolation, item.SupportedIsolationLevels)).ToArray();
        services.Remove(compositionDescriptor);
        services.AddSingleton(new TransactionsComposition(composition.RegistrationIdentity, databases));
        return services;
    }
}
