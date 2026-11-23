namespace CIS.Coordination.PostgreSql;

/// <summary>Explicit PostgreSQL authority selection, copied without database access.</summary>
public sealed class PostgreSqlCoordinationOptions
{
    /// <summary>Gets or sets the deployment-generated identity persisted by explicit installation.</summary>
    public Guid AuthorityId
    {
        get; set;
    }

    /// <summary>Gets or sets the installed schema, initially cis_coordination.</summary>
    public string Schema { get; set; } = "cis_coordination";

    /// <summary>Gets or sets the cooperative native command timeout, initially fifteen seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;
}

internal sealed class PostgreSqlCoordinationSettings
{
    internal PostgreSqlCoordinationSettings(string databaseName, PostgreSqlCoordinationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        DatabaseName = Transactions.TransactionText.Alias(databaseName);
        AuthorityId = CoordinationText.Id(options.AuthorityId, nameof(options.AuthorityId));
        Schema = SchemaName(options.Schema);
        if (options.CommandTimeoutSeconds is < 1 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Use a native command timeout from one to 300 seconds.");
        }
        CommandTimeoutSeconds = options.CommandTimeoutSeconds;
    }

    internal string DatabaseName
    {
        get;
    }
    internal Guid AuthorityId
    {
        get;
    }
    internal string Schema
    {
        get;
    }
    internal int CommandTimeoutSeconds
    {
        get;
    }
    internal string Metadata => $"\"{Schema}\".\"authority_metadata\"";
    internal string Grants => $"\"{Schema}\".\"ownership_grant\"";
    internal string Sequence => $"\"{Schema}\".\"grant_generation\"";

    internal static string SchemaName(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Length is < 1 or > 63 || schema[0] is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_')
            || schema.Any(value => value is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
        {
            throw new ArgumentException("Use an ASCII PostgreSQL schema identifier of one to 63 characters.", nameof(schema));
        }
        return schema;
    }
}
