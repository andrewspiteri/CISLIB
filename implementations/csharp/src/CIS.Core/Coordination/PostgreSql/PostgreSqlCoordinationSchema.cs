namespace CIS.Coordination.PostgreSql;

/// <summary>Explicit, atomic, one-time installation. Registration and construction perform no migration or I/O.</summary>
public static class PostgreSqlCoordinationSchema
{
    /// <summary>Gets the only authority schema version interpreted by this adapter.</summary>
    public const int Version = 1;

    /// <summary>Returns the packaged SQL for a new authority; existing metadata or allocator is never reset or repaired.</summary>
    /// <remarks>Supply one deployment-generated authority identity and retain it in configuration. Run deliberately
    /// using deployment credentials. Reinstallation of an existing authority is refused, including a damaged installation.</remarks>
    public static string GetInstallationSql(Guid authorityId, string schema = "cis_coordination")
    {
        CoordinationText.Id(authorityId, nameof(authorityId));
        string selected = PostgreSqlCoordinationSettings.SchemaName(schema);
        using Stream source = typeof(PostgreSqlCoordinationSchema).Assembly.GetManifestResourceStream(
            "CIS.Coordination.PostgreSql.install-v1.sql")
            ?? throw new InvalidOperationException("The Core package is missing the coordination installation SQL.");
        using var reader = new StreamReader(source);
        return reader.ReadToEnd()
            .Replace("__CIS_COORDINATION_SCHEMA__", $"\"{selected}\"", StringComparison.Ordinal)
            .Replace("__CIS_COORDINATION_AUTHORITY__", authorityId.ToString("D"), StringComparison.Ordinal);
    }
}
