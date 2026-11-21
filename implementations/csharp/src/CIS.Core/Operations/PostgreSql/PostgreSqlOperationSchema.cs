namespace CIS.Operations.PostgreSql;

/// <summary>Explicit versioned installation SQL. Registration and journal construction perform no migration or I/O.</summary>
public static class PostgreSqlOperationSchema
{
    /// <summary>Gets the only schema version interpreted by this adapter.</summary>
    public const int Version = 1;

    /// <summary>Returns the packaged v1 SQL with one validated, quoted schema identifier.</summary>
    /// <remarks>Run deliberately using application-controlled deployment credentials. This API never opens a connection.</remarks>
    public static string GetInstallationSql(string schema = "cis_operations")
    {
        string selected = PostgreSqlOperationSettings.SchemaName(schema);
        using Stream source = typeof(PostgreSqlOperationSchema).Assembly.GetManifestResourceStream(
            "CIS.Operations.PostgreSql.install-v1.sql")
            ?? throw new InvalidOperationException("The Core package is missing its versioned operation installation SQL.");
        using var reader = new StreamReader(source);
        return reader.ReadToEnd().Replace("__CIS_OPERATIONS_SCHEMA__", $"\"{selected}\"", StringComparison.Ordinal);
    }
}
