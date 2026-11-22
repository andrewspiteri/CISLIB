using System.Data.Common;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Messaging.PostgreSql;

/// <summary>Explicit one-time installation; runtime registration and store methods never create schema.</summary>
public static class PostgreSqlMessagingSchema
{
    /// <summary>The independent messaging schema version.</summary>
    public const int Version = 1;

    /// <summary>Returns the embedded, packaged script with one validated quoted schema identifier.</summary>
    public static string GetInstallationSql(string schema = "cis_messaging")
    {
        string selected = PostgreSqlMessagingSettings.SchemaName(schema);
        using Stream resource = typeof(PostgreSqlMessagingSchema).Assembly.GetManifestResourceStream("CIS.Messaging.PostgreSql.install-v1.sql")
            ?? throw new InvalidOperationException("The Core package is missing its messaging installation SQL.");
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd().Replace("__CIS_MESSAGING_SCHEMA__", $"\"{selected}\"", StringComparison.Ordinal);
    }

    /// <summary>Deliberately installs through a deployment-owned participant; the caller still owns commit and cleanup.</summary>
    public static Task InstallAsync(ILocalTransactionContext context, string schema = "cis_messaging", CancellationToken cancellationToken = default)
    {
        PostgreSqlMessagingStore.ValidateContext(context);
        string script = GetInstallationSql(schema);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 60;
            command.CommandText = script;
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, cancellationToken);
    }
}
