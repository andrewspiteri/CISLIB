namespace CIS.Messaging.PostgreSql;

/// <summary>Explicit PostgreSQL messaging setup; construction performs no database work.</summary>
public sealed class PostgreSqlMessagingOptions
{
    /// <summary>Gets or sets the separately installed messaging schema.</summary>
    public string Schema { get; set; } = "cis_messaging";
    /// <summary>Gets or sets the cooperative command timeout, from one through 300 seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;
    /// <summary>Gets or sets optional transactional retained-envelope capacity per application/environment.</summary>
    /// <remarks>Select before that owner's first write. Null selects indefinite storage; changing the persisted mode requires an explicit deployment migration.</remarks>
    public long? MaximumRetainedEnvelopeBytes { get; set; }
    /// <summary>Gets or sets immutable retention captured for newly completed records.</summary>
    public MessageRetentionPolicy Retention { get; set; } = new();
}

internal sealed class PostgreSqlMessagingSettings
{
    internal PostgreSqlMessagingSettings(PostgreSqlMessagingOptions? options)
    {
        options ??= new();
        Schema = SchemaName(options.Schema);
        CommandTimeoutSeconds = MessageText.Range(options.CommandTimeoutSeconds, 1, 300, nameof(options.CommandTimeoutSeconds));
        if (options.MaximumRetainedEnvelopeBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaximumRetainedEnvelopeBytes));
        }
        MaximumRetainedEnvelopeBytes = options.MaximumRetainedEnvelopeBytes;
        Retention = options.Retention ?? throw new ArgumentNullException(nameof(options.Retention));
    }

    internal string Schema { get; }
    internal int CommandTimeoutSeconds { get; }
    internal long? MaximumRetainedEnvelopeBytes { get; }
    internal MessageRetentionPolicy Retention { get; }
    internal string Table(string name) => $"\"{Schema}\".\"{name}\"";

    internal static string SchemaName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > 63 || value[0] is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_')
            || value.Any(character => character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
        {
            throw new ArgumentException("Use one ASCII PostgreSQL schema identifier of at most 63 characters.", nameof(value));
        }
        return value;
    }
}
