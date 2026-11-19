namespace CIS.Security;

/// <summary>One trusted issuer-qualified mapping, separate from credential validation.</summary>
public sealed class SecurityIdentityMapping
{
    /// <summary>Creates a bounded immutable mapping; native providers must establish every provider fact first.</summary>
    public SecurityIdentityMapping(SecuritySubject subject, string? application, string? environment,
        IReadOnlyList<string>? mappedRoles = null, string? clientApplicationId = null,
        SecurityActorKind actorKind = SecurityActorKind.Application)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!Enum.IsDefined(actorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(actorKind));
        }
        if ((application is null) != (environment is null)
            || actorKind == SecurityActorKind.Application && application is null)
        {
            throw new ArgumentException("Application mappings require both application and environment.");
        }
        Subject = subject;
        Application = application is null ? null : SecurityText.Application(application, nameof(application));
        Environment = environment is null ? null : SecurityText.Application(environment, nameof(environment));
        MappedRoles = CopyRoles(mappedRoles);
        ClientApplicationId = SecurityText.OptionalIdentifier(clientApplicationId, nameof(clientApplicationId));
        ActorKind = actorKind;
    }

    /// <summary>Gets the exact trusted issuer and subject.</summary>
    public SecuritySubject Subject
    {
        get;
    }
    /// <summary>Gets the mapped calling application.</summary>
    public string? Application
    {
        get;
    }
    /// <summary>Gets the mapped calling environment.</summary>
    public string? Environment
    {
        get;
    }
    /// <summary>Gets role templates allowed by this mapping; token roles must also be present for bearer/native integrations.</summary>
    public IReadOnlyList<string> MappedRoles
    {
        get;
    }
    /// <summary>Gets the exact expected client application identifier, required for Entra mappings.</summary>
    public string? ClientApplicationId
    {
        get;
    }
    /// <summary>Gets whether this mapping establishes an application or human actor.</summary>
    public SecurityActorKind ActorKind
    {
        get;
    }

    internal static IReadOnlyList<string> CopyRoles(IReadOnlyList<string>? roles)
    {
        if (roles is null)
        {
            return Array.AsReadOnly(Array.Empty<string>());
        }
        if (roles.Count > 256)
        {
            throw new ArgumentException("A mapping or credential has at most 256 role identifiers.", nameof(roles));
        }
        var copied = new string[roles.Count];
        for (int index = 0; index < copied.Length; index++)
        {
            copied[index] = SecurityText.Identifier(roles[index], nameof(roles));
        }
        if (copied.Distinct(StringComparer.Ordinal).Count() != copied.Length)
        {
            throw new ArgumentException("Role identifiers must be distinct.", nameof(roles));
        }
        return Array.AsReadOnly(copied);
    }
}

/// <summary>The explicit local metadata-to-credential relationship for an outgoing profile.</summary>
public sealed class SecurityLocalIdentityBinding
{
    /// <summary>Creates the trusted expected local application/environment and credential identity relationship.</summary>
    public SecurityLocalIdentityBinding(string application, string environment, SecuritySubject credentialIdentity)
    {
        Application = SecurityText.Application(application, nameof(application));
        Environment = SecurityText.Application(environment, nameof(environment));
        ArgumentNullException.ThrowIfNull(credentialIdentity);
        CredentialIdentity = credentialIdentity;
    }

    /// <summary>Gets the exact expected canonical local application.</summary>
    public string Application
    {
        get;
    }
    /// <summary>Gets the exact expected canonical local environment.</summary>
    public string Environment
    {
        get;
    }
    /// <summary>Gets the intended credential identity; the provider validates actual material against it.</summary>
    public SecuritySubject CredentialIdentity
    {
        get;
    }
}

/// <summary>One explicitly selected immutable profile and its independent enabled roles.</summary>
public sealed class SecurityProfile
{
    /// <summary>Creates common profile policy. Provider-specific trust and credential inputs remain adapter-owned.</summary>
    public SecurityProfile(string alias, SecurityProfileKind kind, SecurityProfileRoles roles,
        string? authenticationScheme = null, IReadOnlyList<SecurityIdentityMapping>? identityMappings = null,
        SecurityLocalIdentityBinding? outboundIdentity = null, IReadOnlyList<string>? allowedOrigins = null,
        string? additionalAuthorizationPolicy = null, bool requiredForReadiness = false)
    {
        Alias = SecurityText.Alias(alias, nameof(alias));
        if (alias == "unresolved")
        {
            throw new ArgumentException("The unresolved alias is reserved for bounded diagnostics.", nameof(alias));
        }
        if (!Enum.IsDefined(kind) || roles is SecurityProfileRoles.None || (roles & ~SecurityProfileRoles.Both) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roles));
        }
        Kind = kind;
        Roles = roles;
        AuthenticationScheme = authenticationScheme is null ? null : SecurityText.Alias(authenticationScheme, nameof(authenticationScheme));
        if ((roles & SecurityProfileRoles.Inbound) != 0 && AuthenticationScheme is null)
        {
            throw new ArgumentException("An inbound profile selects one explicit native authentication scheme.");
        }
        if (identityMappings?.Count > 4096)
        {
            throw new ArgumentException("A profile accepts at most 4096 nonnull identity mappings.", nameof(identityMappings));
        }
        IdentityMappings = Array.AsReadOnly((identityMappings ?? []).ToArray());
        if (IdentityMappings.Any(mapping => mapping is null))
        {
            throw new ArgumentException("Identity mappings cannot be null.", nameof(identityMappings));
        }
        OutboundIdentity = outboundIdentity;
        if ((roles & SecurityProfileRoles.Outbound) != 0 && outboundIdentity is null)
        {
            throw new ArgumentException("An outbound profile requires its explicit local credential mapping.");
        }
        if (allowedOrigins?.Count > 64)
        {
            throw new ArgumentException("Supply at most 64 exact HTTPS origins.", nameof(allowedOrigins));
        }
        var origins = new List<string>();
        foreach (string origin in allowedOrigins ?? [])
        {
            if (origins.Count >= 64 || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
                || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/"
                || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            {
                throw new ArgumentException("Supply at most 64 exact HTTPS origins without credentials, paths, query or fragments.", nameof(allowedOrigins));
            }
            origins.Add(uri.GetLeftPart(UriPartial.Authority));
        }
        AllowedOrigins = Array.AsReadOnly(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        if ((roles & SecurityProfileRoles.Outbound) != 0 && AllowedOrigins.Count == 0)
        {
            throw new ArgumentException("Security-bound outgoing profiles require explicit HTTPS origins.");
        }
        AdditionalAuthorizationPolicy = additionalAuthorizationPolicy is null ? null
            : SecurityText.Alias(additionalAuthorizationPolicy, nameof(additionalAuthorizationPolicy));
        RequiredForReadiness = requiredForReadiness;
    }

    /// <summary>Gets the finite profile alias.</summary>
    public string Alias
    {
        get;
    }
    /// <summary>Gets the selected integration kind.</summary>
    public SecurityProfileKind Kind
    {
        get;
    }
    /// <summary>Gets the independent enabled roles.</summary>
    public SecurityProfileRoles Roles
    {
        get;
    }
    /// <summary>Gets the explicitly selected inbound native scheme, when needed.</summary>
    public string? AuthenticationScheme
    {
        get;
    }
    /// <summary>Gets the immutable trusted identity mappings.</summary>
    public IReadOnlyList<SecurityIdentityMapping> IdentityMappings
    {
        get;
    }
    /// <summary>Gets the local credential relationship for an enabled outgoing role.</summary>
    public SecurityLocalIdentityBinding? OutboundIdentity
    {
        get;
    }
    /// <summary>Gets normalized immutable allowed HTTPS origins.</summary>
    public IReadOnlyList<string> AllowedOrigins
    {
        get;
    }
    /// <summary>Gets an optional additional native policy applied to both endpoint and imperative checks.</summary>
    public string? AdditionalAuthorizationPolicy
    {
        get;
    }
    /// <summary>Gets whether unavailable enabled-role credentials affect readiness.</summary>
    public bool RequiredForReadiness
    {
        get;
    }
}

/// <summary>Mutable setup input copied into one immutable startup snapshot; never a live policy.</summary>
public sealed class SecurityOptions
{
    /// <summary>Gets or sets a non-secret deployment policy version; null selects a deterministic non-secret digest.</summary>
    public string? PolicyVersion
    {
        get; set;
    }
    /// <summary>Gets the explicitly selected profile catalog.</summary>
    public IList<SecurityProfile> Profiles { get; } = new List<SecurityProfile>();
    /// <summary>Gets registered lowercase ASCII capability names.</summary>
    public IList<string> Capabilities { get; } = new List<string>();
    /// <summary>Gets complete scoped grants.</summary>
    public IList<CapabilityGrant> Grants { get; } = new List<CapabilityGrant>();
    /// <summary>Gets or sets the validated capability ceiling, default 256.</summary>
    public int MaximumCapabilities { get; set; } = 256;
    /// <summary>Gets or sets the validated total-grant ceiling, default 4096.</summary>
    public int MaximumGrants { get; set; } = 4096;
}
