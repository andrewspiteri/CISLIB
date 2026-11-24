using CIS.Hosting;
using CIS.Management.Service.Http;
using CIS.Management.Service.Persistence;
using CIS.Management.Service.Security;
using CIS.Security;
using CIS.Startup;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Npgsql;
using CIS.Healthcheck;
using CIS.Management.Service.Backends;

namespace CIS.Management.Service;

/// <summary>The independent product host over the existing CISApplication lifecycle and one provider.</summary>
public static partial class ManagementServiceApplication
{
    public static CISApplication Create(string[] args, ManagementServiceOptions options, ManagementSecurityOptions security,
        Action<CISApplicationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(security);
        if (options.Delivery is null)
        {
            throw new ArgumentException("The management service requires explicit durable Audit delivery bootstrap settings.", nameof(options));
        }
        ManagementSecurityOptions? frozenSecurity = null;
        return CISApplication.CreateWeb(args, builder =>
        {
            configure?.Invoke(builder);
            AddDatabase(builder.Services, options);
            frozenSecurity = ManagementSecurityComposition.Configure(builder, security,
                policy => ConfigureMetadataSecurity(policy, options));
            ConfigureMetadata(builder, options);
            builder.Services.AddScoped<PostgreSqlManagementBackend>();
            builder.Services.AddScoped<IManagementBackend>(root => root.GetRequiredService<PostgreSqlManagementBackend>());
            builder.Services.AddScoped<IManagementConfigurationBackend>(root => root.GetRequiredService<PostgreSqlManagementBackend>());
            builder.Services.AddSingleton<IHealthReadinessSource>(root => root.GetRequiredService<PostgreSqlManagementAuthority>());
            builder.Services.AddSingleton(root => new ManagementCursor(options,
                () => root.GetRequiredService<PostgreSqlManagementAuthority>().Current));
            builder.Services.AddSingleton<ManagementDatabase>();
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<ManagementDatabase>());
        }, web =>
        {
            ManagementSecurityComposition.Map(web, frozenSecurity
                ?? throw new InvalidOperationException("Management security must be captured before building the host."));
            ManagementEndpoints.Map(web, options);
        });
    }

    /// <summary>Runs only an explicitly requested deployment migration, with its own ordinary console lifetime.</summary>
    public static async Task MigrateAsync(string[] args, ManagementServiceOptions options, CancellationToken cancellationToken = default)
    {
        await using var application = new CISApplication(args, builder =>
        {
            builder.HeartbeatEnabled = false;
            AddDatabase(builder.Services, options);
            builder.Services.AddSingleton<IStartable, Migration>();
        });
        await application.StartAsync(cancellationToken);
        await application.StopAsync(cancellationToken);
    }

    private static void AddDatabase(IServiceCollection services, ManagementServiceOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton(options.Persistence);
        services.AddSingleton(_ =>
        {
            var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString)
            {
                MaxPoolSize = 4,
                MinPoolSize = 0,
                Pooling = true,
                Timeout = 15,
                CommandTimeout = 30
            };
            return new NpgsqlDataSourceBuilder(connection.ConnectionString).Build();
        });
        services.AddCisTransactions();
        services.AddCisAdoNetDatabase("management", root => root.GetRequiredService<NpgsqlDataSource>(), new()
        {
            DefaultIsolation = TransactionIsolation.ReadCommitted,
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        services.AddSingleton<PostgreSqlManagementAuthority>();
        services.AddSingleton(root => new PostgreSqlManagementStore(root.GetRequiredService<ILocalTransactionFactory>(),
            options.Persistence, root.GetRequiredService<PostgreSqlManagementAuthority>()));
    }

    static partial void ConfigureMetadata(CISApplicationBuilder builder, ManagementServiceOptions options);
    static partial void ConfigureMetadataSecurity(SecurityOptions policy, ManagementServiceOptions options);
    static partial void ConfigureConfiguration(CISApplicationBuilder builder, ManagementServiceOptions options);

    private sealed class Migration(ILocalTransactionFactory transactions, ManagementServiceOptions options) : IStartable
    {
        public Task StartAsync(CancellationToken cancellationToken) => ManagementDatabase.InstallAsync(transactions, options, cancellationToken);
    }
}
