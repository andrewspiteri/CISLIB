namespace CIS.Security;

/// <summary>The supported workload profiles and the explicit native-principal extension point.</summary>
public enum SecurityProfileKind
{
    /// <summary>Explicit Entra workload identity; never an automatic credential chain.</summary>
    EntraWorkloadIdentity,
    /// <summary>Direct application client-certificate authentication.</summary>
    ClientCertificate,
    /// <summary>Isolated development certificates using the certificate integration.</summary>
    LocalDevelopment,
    /// <summary>An explicitly supplied native authentication integration, including validated human principals.</summary>
    Native
}

/// <summary>The independently selected roles of one security profile.</summary>
[Flags]
public enum SecurityProfileRoles
{
    /// <summary>No role; rejected for an active profile.</summary>
    None = 0,
    /// <summary>Receives and validates callers.</summary>
    Inbound = 1,
    /// <summary>Obtains a local workload credential for outgoing calls.</summary>
    Outbound = 2,
    /// <summary>Both independently configured roles.</summary>
    Both = Inbound | Outbound
}

/// <summary>The authenticated actor; an application credential never proves a human.</summary>
public enum SecurityActorKind
{
    /// <summary>A workload/application identity.</summary>
    Application,
    /// <summary>An independently validated human identity.</summary>
    User
}

/// <summary>The strength of instance evidence, independently of application authentication.</summary>
public enum InstanceEvidenceKind
{
    /// <summary>No instance value was provided.</summary>
    NotProvided,
    /// <summary>A reported metadata value, without independent attestation.</summary>
    Reported,
    /// <summary>An instance binding independently validated by the selected integration.</summary>
    Verified
}

/// <summary>An immutable issuer-qualified identity. Equal subject text under another issuer is different.</summary>
public sealed record SecuritySubject
{
    /// <summary>Creates an exact bounded subject without trimming or case folding.</summary>
    public SecuritySubject(string issuer, string identifier)
    {
        Issuer = SecurityText.Identifier(issuer, nameof(issuer));
        Identifier = SecurityText.Identifier(identifier, nameof(identifier));
    }

    /// <summary>Gets the validated issuer or configured trusted chain relationship.</summary>
    public string Issuer
    {
        get;
    }
    /// <summary>Gets the stable provider subject.</summary>
    public string Identifier
    {
        get;
    }
}

/// <summary>Reported metadata remains distinct even when verified instance evidence is available.</summary>
public sealed class CallerInstanceEvidence
{
    /// <summary>Copies optional reported and independently verified instance identifiers.</summary>
    public CallerInstanceEvidence(string? reportedIdentifier = null, string? verifiedIdentifier = null)
    {
        ReportedIdentifier = SecurityText.OptionalIdentifier(reportedIdentifier, nameof(reportedIdentifier));
        VerifiedIdentifier = SecurityText.OptionalIdentifier(verifiedIdentifier, nameof(verifiedIdentifier));
    }

    /// <summary>Gets the strongest actually supplied evidence.</summary>
    public InstanceEvidenceKind Kind => VerifiedIdentifier is not null ? InstanceEvidenceKind.Verified
        : ReportedIdentifier is not null ? InstanceEvidenceKind.Reported : InstanceEvidenceKind.NotProvided;
    /// <summary>Gets unverified metadata; it never changes grants.</summary>
    public string? ReportedIdentifier
    {
        get;
    }
    /// <summary>Gets an independently validated binding, or null.</summary>
    public string? VerifiedIdentifier
    {
        get;
    }
}

/// <summary>Safe authentication facts without credential material or unrestricted claims.</summary>
public sealed class SecurityAuthenticationEvidence
{
    /// <summary>Creates bounded evidence of the native validation actually performed.</summary>
    public SecurityAuthenticationEvidence(DateTimeOffset validatedAtUtc, DateTimeOffset? expiresAtUtc = null,
        string? credentialIdentifier = null)
    {
        ValidatedAtUtc = validatedAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
        CredentialIdentifier = SecurityText.OptionalIdentifier(credentialIdentifier, nameof(credentialIdentifier));
    }

    /// <summary>Gets when the integration validated the credential.</summary>
    public DateTimeOffset ValidatedAtUtc
    {
        get;
    }
    /// <summary>Gets established expiry, when known.</summary>
    public DateTimeOffset? ExpiresAtUtc
    {
        get;
    }
    /// <summary>Gets a bounded diagnostic identifier, never a token or key.</summary>
    public string? CredentialIdentifier
    {
        get;
    }
}

/// <summary>An immutable bounded projection of established authentication, never a wire credential.</summary>
public sealed class CallerContext
{
    /// <summary>Creates caller data. Constructing this object does not establish an authenticated scope.</summary>
    public CallerContext(string profile, string authenticationMethod, SecuritySubject subject,
        SecurityActorKind actorKind, string? application, string? environment,
        SecurityAuthenticationEvidence authentication, string policyVersion,
        CallerInstanceEvidence? instance = null, SecuritySubject? humanSubject = null,
        string? verifiedDelegation = null)
    {
        Profile = SecurityText.Alias(profile, nameof(profile));
        AuthenticationMethod = SecurityText.Alias(authenticationMethod, nameof(authenticationMethod));
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(authentication);
        if (!Enum.IsDefined(actorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(actorKind));
        }
        if ((application is null) != (environment is null))
        {
            throw new ArgumentException("An established application and environment are supplied together.");
        }
        Subject = subject;
        ActorKind = actorKind;
        Application = application is null ? null : SecurityText.Application(application, nameof(application));
        Environment = environment is null ? null : SecurityText.Application(environment, nameof(environment));
        Authentication = authentication;
        PolicyVersion = SecurityText.Identifier(policyVersion, nameof(policyVersion));
        Instance = instance ?? new();
        HumanSubject = humanSubject;
        VerifiedDelegation = SecurityText.OptionalIdentifier(verifiedDelegation, nameof(verifiedDelegation));
        if (actorKind == SecurityActorKind.Application && humanSubject is not null && VerifiedDelegation is null)
        {
            throw new ArgumentException("Application credentials alone do not establish a human subject.");
        }
    }

    /// <summary>Gets the configured profile that actually authenticated the caller.</summary>
    public string Profile
    {
        get;
    }
    /// <summary>Gets the actual authentication method.</summary>
    public string AuthenticationMethod
    {
        get;
    }
    /// <summary>Gets the validated issuer-qualified provider subject.</summary>
    public SecuritySubject Subject
    {
        get;
    }
    /// <summary>Gets the authenticated actor kind.</summary>
    public SecurityActorKind ActorKind
    {
        get;
    }
    /// <summary>Gets the calling application established by trusted mapping.</summary>
    public string? Application
    {
        get;
    }
    /// <summary>Gets the calling environment established by trusted mapping.</summary>
    public string? Environment
    {
        get;
    }
    /// <summary>Gets independently classified instance evidence.</summary>
    public CallerInstanceEvidence Instance
    {
        get;
    }
    /// <summary>Gets safe credential validation facts.</summary>
    public SecurityAuthenticationEvidence Authentication
    {
        get;
    }
    /// <summary>Gets the fixed trusted mapping/grant policy version.</summary>
    public string PolicyVersion
    {
        get;
    }
    /// <summary>Gets a separately validated human actor, when supplied by an explicit integration.</summary>
    public SecuritySubject? HumanSubject
    {
        get;
    }
    /// <summary>Gets verified delegation provenance, never inferred from copied headers.</summary>
    public string? VerifiedDelegation
    {
        get;
    }
}

/// <summary>Read-only access to the current trusted scope. Absence is not authentication.</summary>
public interface ICallerContextAccessor
{
    /// <summary>Gets the established caller for this scope, or null.</summary>
    CallerContext? Current
    {
        get;
    }
}

/// <summary>A projection of the existing process identity, without another resolver or configuration source.</summary>
public sealed class LocalApplicationIdentity
{
    private LocalApplicationIdentity()
    {
        Application = EnvironmentOptions.ApplicationName;
        Environment = EnvironmentOptions.EnvironmentName;
        InstanceId = EnvironmentOptions.InstanceId;
    }

    /// <summary>Reads the already-resolved canonical identity.</summary>
    public static LocalApplicationIdentity Capture() => new();
    /// <summary>Gets the existing canonical application name.</summary>
    public string Application
    {
        get;
    }
    /// <summary>Gets the existing canonical environment name.</summary>
    public string Environment
    {
        get;
    }
    /// <summary>Gets the existing local instance identifier; it does not attest a remote replica.</summary>
    public string InstanceId
    {
        get;
    }
}
