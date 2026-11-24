namespace CIS.Configuration;

/// <summary>The application's locally selected policy for an enrolled configuration root.</summary>
public enum ConfigurationUpdateMode
{
    /// <summary>Accept effective runtime changes through the existing retained-root commit gate.</summary>
    HotReload,
    /// <summary>Keep active values when valid runtime changes require a new live bootstrap.</summary>
    RestartRequired
}

/// <summary>An explicit exact-type binding; discovery alone does not enroll configuration centrally.</summary>
public sealed record ConfigurationEnrollment
{
    /// <summary>Captures a portable identity and locally owned update policy without constructing the root.</summary>
    public ConfigurationEnrollment(Type configurationType, string configurationId, string schemaVersion,
        ConfigurationUpdateMode updateMode)
    {
        ArgumentNullException.ThrowIfNull(configurationType);
        if (!configurationType.IsClass || configurationType.IsAbstract || configurationType.ContainsGenericParameters
            || !typeof(IConfigurationObject).IsAssignableFrom(configurationType))
        {
            throw new ArgumentException("Select one concrete closed configuration type.", nameof(configurationType));
        }
        if (!Enum.IsDefined(updateMode))
        {
            throw new ArgumentOutOfRangeException(nameof(updateMode));
        }
        ConfigurationSnapshotRules.Identifier(configurationId, nameof(configurationId));
        ConfigurationSnapshotRules.PositiveInt64(schemaVersion, nameof(schemaVersion));
        ConfigurationType = configurationType;
        ConfigurationId = configurationId;
        SchemaVersion = schemaVersion;
        UpdateMode = updateMode;
        PersistedDefinition = ConfigurationIdentity.Create(configurationType);
    }

    /// <summary>Gets the exact local CLR type; it is never selected by remote reflection.</summary>
    public Type ConfigurationType
    {
        get;
    }
    /// <summary>Gets the exact case-sensitive portable identifier.</summary>
    public string ConfigurationId
    {
        get;
    }
    /// <summary>Gets the exact positive Int64 decimal schema identity.</summary>
    public string SchemaVersion
    {
        get;
    }
    /// <summary>Gets the original Configuration definition, excluding assembly version.</summary>
    public string PersistedDefinition
    {
        get;
    }
    /// <summary>Gets the local update policy; publication cannot override it.</summary>
    public ConfigurationUpdateMode UpdateMode
    {
        get;
    }
}
