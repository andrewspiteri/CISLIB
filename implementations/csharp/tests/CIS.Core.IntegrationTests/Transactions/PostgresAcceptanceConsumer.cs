using System.Data.Common;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Tests.IntegrationTests.Transactions;

// A named acceptance consumer, not production operation-result or outgoing-delivery storage.
internal sealed class PostgresAcceptanceConsumer
{
    internal PostgresAcceptanceConsumer(string businessTable, string resultTable, string outgoingTable,
        Action<string, DbConnection, DbTransaction>? observe)
    {
        Business = new PostgresBusinessStore(businessTable, observe);
        Results = new PostgresResultStore(resultTable, observe);
        Outgoing = new PostgresOutgoingStore(outgoingTable, observe);
    }

    internal PostgresBusinessStore Business
    {
        get;
    }
    internal PostgresResultStore Results
    {
        get;
    }
    internal PostgresOutgoingStore Outgoing
    {
        get;
    }

    internal async Task<PostgresBusinessResult> ExecuteAsync(ILocalTransactionContext context, Guid operation,
        bool accepted, CancellationToken cancellationToken = default)
    {
        var result = new PostgresBusinessResult(accepted, accepted ? "accepted" : "declined");
        if (result.Success)
        {
            await Business.InsertAsync(context, operation, cancellationToken);
        }
        await Results.InsertAsync(context, operation, result, cancellationToken);
        if (result.Success)
        {
            await Outgoing.InsertAsync(context, operation, cancellationToken);
        }
        return result;
    }
}

internal sealed record PostgresBusinessResult(bool Success, string Code);

internal sealed class PostgresBusinessStore(string table, Action<string, DbConnection, DbTransaction>? observe)
{
    internal Task InsertAsync(ILocalTransactionContext context, Guid operation, CancellationToken cancellationToken = default) =>
        context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            observe?.Invoke("business", connection, transaction);
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {table} (operation_id, payload) VALUES (@operation, @payload)";
            PostgresAcceptanceParameter.Add(command, "operation", operation);
            PostgresAcceptanceParameter.Add(command, "payload", "business fixture");
            await command.ExecuteNonQueryAsync(token);
        }, cancellationToken);
}

internal sealed class PostgresResultStore(string table, Action<string, DbConnection, DbTransaction>? observe)
{
    internal Task InsertAsync(ILocalTransactionContext context, Guid operation, PostgresBusinessResult result,
        CancellationToken cancellationToken = default) => context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            observe?.Invoke("result", connection, transaction);
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {table} (operation_id, success, payload) VALUES (@operation, @success, @payload)";
            PostgresAcceptanceParameter.Add(command, "operation", operation);
            PostgresAcceptanceParameter.Add(command, "success", result.Success);
            PostgresAcceptanceParameter.Add(command, "payload", result.Code);
            await command.ExecuteNonQueryAsync(token);
        }, cancellationToken);
}

internal sealed class PostgresOutgoingStore(string table, Action<string, DbConnection, DbTransaction>? observe)
{
    internal Task InsertAsync(ILocalTransactionContext context, Guid operation, CancellationToken cancellationToken = default) =>
        context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            observe?.Invoke("outgoing", connection, transaction);
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {table} (operation_id, payload) VALUES (@operation, @payload)";
            PostgresAcceptanceParameter.Add(command, "operation", operation);
            PostgresAcceptanceParameter.Add(command, "payload", "outgoing fixture; no delivery is performed");
            await command.ExecuteNonQueryAsync(token);
        }, cancellationToken);
}

internal static class PostgresAcceptanceParameter
{
    internal static void Add(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
