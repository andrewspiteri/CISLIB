using System.Data;
using System.Data.Common;
using System.Globalization;
using CIS.Management.Wire;
using CIS.Transactions;

namespace CIS.Management.Service.Persistence;

internal static class ManagementSql
{
    internal const string DatabaseAlias = "management";

    internal static string Table(string schema, string table) => $"\"{schema}\".\"{table}\"";

    internal static void RequireContext(ILocalTransactionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.DatabaseName != DatabaseAlias || context.Isolation != TransactionIsolation.ReadCommitted)
        {
            throw new ArgumentException("Use the original management ReadCommitted participant.", nameof(context));
        }
    }

    internal static Guid Uuid(string value, string name)
        => ManagementWire.IsUuid(value) ? Guid.Parse(value) : throw new ArgumentException("Use a canonical nonempty UUID.", name);

    internal static string Timestamp(DateTime value)
        => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    internal static string Timestamp(DbDataReader reader, int ordinal) => Timestamp(reader.GetDateTime(ordinal));

    internal static string ETag(long revision) => $"\"metadata-{revision.ToString(CultureInfo.InvariantCulture)}\"";

    internal static DbCommand Command(DbConnection connection, DbTransaction transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = value switch
            {
                Guid => DbType.Guid,
                int => DbType.Int32,
                long => DbType.Int64,
                bool => DbType.Boolean,
                double => DbType.Double,
                byte[] => DbType.Binary,
                _ => DbType.String
            };
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
