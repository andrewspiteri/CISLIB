namespace CIS.Operations.PostgreSql;

/// <summary>Explicit PostgreSQL journal setup, copied by its adapter without opening a database.</summary>
public sealed class PostgreSqlOperationOptions
{
    /// <summary>Gets or sets the installed schema, initially cis_operations.</summary>
    public string Schema { get; set; } = "cis_operations";

    /// <summary>Gets or sets each command's cooperative native timeout, initially fifteen seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;
}

internal sealed class PostgreSqlOperationSettings
{
    internal PostgreSqlOperationSettings(PostgreSqlOperationOptions? options)
    {
        options ??= new();
        Schema = SchemaName(options.Schema);
        if (options.CommandTimeoutSeconds is < 1 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Use a command timeout between one and 300 seconds.");
        }
        CommandTimeoutSeconds = options.CommandTimeoutSeconds;
    }

    internal string Schema
    {
        get;
    }

    internal int CommandTimeoutSeconds
    {
        get;
    }

    internal string Journal => $"\"{Schema}\".\"operation_journal\"";

    internal string VersionTable => $"\"{Schema}\".\"schema_version\"";

    internal static string SchemaName(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Length is < 1 or > 63 || schema[0] is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_')
            || schema.Any(character => character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
        {
            throw new ArgumentException("Use an ASCII PostgreSQL schema identifier of one to 63 characters.", nameof(schema));
        }
        return schema;
    }
}
