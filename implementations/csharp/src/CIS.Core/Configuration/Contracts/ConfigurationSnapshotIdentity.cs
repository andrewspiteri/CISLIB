using System.Globalization;

namespace CIS.Configuration;

/// <summary>An independently established target/session, not a credential or physical-process proof.</summary>
public sealed record ConfigurationSnapshotTarget
{
    /// <summary>Captures the trusted bootstrap binding without contacting a provider.</summary>
    public ConfigurationSnapshotTarget(string authorityId, string applicationId, string environment,
        string registrationId, string hostApplicationName, string? cohortId = null)
    {
        ConfigurationSnapshotRules.Uuid(authorityId, nameof(authorityId));
        ConfigurationSnapshotRules.Uuid(applicationId, nameof(applicationId));
        ConfigurationSnapshotRules.Text(environment, 255, nameof(environment));
        ConfigurationSnapshotRules.Uuid(registrationId, nameof(registrationId));
        ConfigurationSnapshotRules.Text(hostApplicationName, 255, nameof(hostApplicationName));
        if (hostApplicationName == "CommonConfiguration")
        {
            throw new ArgumentException("The common scope is not an application host scope.", nameof(hostApplicationName));
        }
        if (cohortId is not null)
        {
            ConfigurationSnapshotRules.Identifier(cohortId, nameof(cohortId));
        }
        AuthorityId = authorityId;
        ApplicationId = applicationId;
        Environment = environment;
        RegistrationId = registrationId;
        HostApplicationName = hostApplicationName;
        CohortId = cohortId;
    }

    /// <summary>Gets the persisted authoritative namespace UUID.</summary>
    public string AuthorityId
    {
        get;
    }
    /// <summary>Gets the admitted application/environment resource UUID.</summary>
    public string ApplicationId
    {
        get;
    }
    /// <summary>Gets the exact trusted environment name.</summary>
    public string Environment
    {
        get;
    }
    /// <summary>Gets the admitted registration incarnation UUID.</summary>
    public string RegistrationId
    {
        get;
    }
    /// <summary>Gets the fixed Configuration host scope, independent of telemetry display names.</summary>
    public string HostApplicationName
    {
        get;
    }
    /// <summary>Gets the optional trusted cohort, fixed for this session.</summary>
    public string? CohortId
    {
        get;
    }
}

/// <summary>Authenticated desired identity that may remain known even when its content is rejected.</summary>
public sealed record ConfigurationDesiredIdentity
{
    /// <summary>Captures exact desired facts; neither an ETag nor a UUID supplies ordering.</summary>
    public ConfigurationDesiredIdentity(ConfigurationSnapshotTarget target, string configurationId,
        string schemaVersion, string releaseId, string assignmentGeneration)
    {
        ArgumentNullException.ThrowIfNull(target);
        ConfigurationSnapshotRules.Identifier(configurationId, nameof(configurationId));
        ConfigurationSnapshotRules.PositiveInt64(schemaVersion, nameof(schemaVersion));
        ConfigurationSnapshotRules.Uuid(releaseId, nameof(releaseId));
        ConfigurationSnapshotRules.PositiveInt64(assignmentGeneration, nameof(assignmentGeneration));
        Target = target;
        ConfigurationId = configurationId;
        SchemaVersion = schemaVersion;
        ReleaseId = releaseId;
        AssignmentGeneration = assignmentGeneration;
    }

    /// <summary>Gets the target/session which must match independently captured bootstrap state.</summary>
    public ConfigurationSnapshotTarget Target
    {
        get;
    }
    /// <summary>Gets the exact portable definition ID.</summary>
    public string ConfigurationId
    {
        get;
    }
    /// <summary>Gets the exact positive decimal schema identity.</summary>
    public string SchemaVersion
    {
        get;
    }
    /// <summary>Gets the immutable release UUID.</summary>
    public string ReleaseId
    {
        get;
    }
    /// <summary>Gets the positive ordered assignment-map decision, as exact decimal text.</summary>
    public string AssignmentGeneration
    {
        get;
    }
}

/// <summary>An immutable source reference and exact payload integrity fact, without source content.</summary>
public sealed record ConfigurationPublicationReference
{
    /// <summary>Captures the exact source catalog, publication and lowercase SHA-256 digest.</summary>
    public ConfigurationPublicationReference(string sourceId, string publicationId, string sha256)
    {
        ConfigurationSnapshotRules.Uuid(sourceId, nameof(sourceId));
        ConfigurationSnapshotRules.Uuid(publicationId, nameof(publicationId));
        ConfigurationSnapshotRules.Digest(sha256, nameof(sha256));
        SourceId = sourceId;
        PublicationId = publicationId;
        Sha256 = sha256;
    }

    /// <summary>Gets the stable source-resource UUID.</summary>
    public string SourceId
    {
        get;
    }
    /// <summary>Gets the immutable publication UUID.</summary>
    public string PublicationId
    {
        get;
    }
    /// <summary>Gets the exact source-text digest, not Configuration's typed effective checksum.</summary>
    public string Sha256
    {
        get;
    }
}

/// <summary>The immutable provenance accompanying one complete common/application pair.</summary>
/// <remarks>Null source references mean explicit authorized absence only on a complete snapshot.</remarks>
public sealed record ConfigurationSnapshotProvenance
{
    /// <summary>Captures the desired identity and exact source references without payloads or provider callbacks.</summary>
    public ConfigurationSnapshotProvenance(ConfigurationDesiredIdentity desired,
        ConfigurationPublicationReference? commonSource = null, ConfigurationPublicationReference? applicationSource = null)
    {
        ArgumentNullException.ThrowIfNull(desired);
        Desired = desired;
        CommonSource = commonSource;
        ApplicationSource = applicationSource;
    }

    /// <summary>Gets the authenticated desired identity, subject to the request's independent target check.</summary>
    public ConfigurationDesiredIdentity Desired
    {
        get;
    }
    /// <summary>Gets the exact common publication, or explicit absence in this complete release.</summary>
    public ConfigurationPublicationReference? CommonSource
    {
        get;
    }
    /// <summary>Gets the exact application publication, or explicit absence in this complete release.</summary>
    public ConfigurationPublicationReference? ApplicationSource
    {
        get;
    }
}

internal static class ConfigurationSnapshotRules
{
    internal static void Identifier(string value, string parameter)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value is "." or ".."
            || value.Any(character => !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
                or >= '0' and <= '9' or '-' or '.' or '_' or '~')))
        {
            throw new ArgumentException("Use one through 128 exact URI-unreserved ASCII characters, excluding dot segments.", parameter);
        }
    }

    internal static void PositiveInt64(string value, string parameter)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 19 || value[0] == '0'
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) || parsed <= 0
            || parsed.ToString(CultureInfo.InvariantCulture) != value)
        {
            throw new ArgumentException("Use canonical positive Int64 decimal text.", parameter);
        }
    }

    internal static void Uuid(string value, string parameter)
    {
        if (!Guid.TryParseExact(value, "D", out Guid parsed) || parsed == Guid.Empty || parsed.ToString("D") != value)
        {
            throw new ArgumentException("Use a canonical nonempty lowercase UUID.", parameter);
        }
    }

    internal static void Digest(string value, string parameter)
    {
        if (value is null || value.Length != 64 || value.Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("Use a lowercase hexadecimal SHA-256 digest.", parameter);
        }
    }

    internal static void Text(string value, int maximumScalars, string parameter)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximumScalars * 2)
        {
            throw new ArgumentException("Use bounded nonempty Unicode text without controls.", parameter);
        }
        int count = 0;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsControl(character) || char.IsLowSurrogate(character))
            {
                throw new ArgumentException("Use bounded valid Unicode text without controls.", parameter);
            }
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 == value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw new ArgumentException("Use bounded valid Unicode text without controls.", parameter);
                }
                index++;
            }
            count++;
            if (count > maximumScalars)
            {
                throw new ArgumentException("Use bounded valid Unicode text without controls.", parameter);
            }
        }
    }

    internal static void Code(string value, string parameter)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value.Any(character => !(character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-')))
        {
            throw new ArgumentException("Use a bounded safe diagnostic code, not a message or payload.", parameter);
        }
    }
}
