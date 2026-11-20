namespace CIS.Security;

/// <summary>Known portable security failure categories; transport status alone does not establish a specific cause.</summary>
public enum SecurityFailureKind
{
    /// <summary>No usable credential can be obtained.</summary>
    CredentialUnavailable,
    /// <summary>Local or validated provider evidence establishes invalid credential material.</summary>
    CredentialInvalid,
    /// <summary>Established credential expiry has passed.</summary>
    CredentialExpired,
    /// <summary>No acceptable authenticated principal was established.</summary>
    Unauthenticated,
    /// <summary>Native authentication passed but no trusted identity mapping applies.</summary>
    IdentityUnmapped,
    /// <summary>The actual composite authorization was denied.</summary>
    CapabilityDenied,
    /// <summary>Required validation evidence is unavailable.</summary>
    ValidationUnavailable,
    /// <summary>A remote authentication rejection is known without a more precise cause.</summary>
    AuthenticationRejected,
    /// <summary>The available HTTP/transport facts do not establish a security-specific cause.</summary>
    CauseUnknown
}

/// <summary>Where the evidence for a security failure came from.</summary>
public enum SecurityFailureSource
{
    /// <summary>Reliable local credential or validation evidence.</summary>
    Local,
    /// <summary>A documented structured remote security response.</summary>
    Remote,
    /// <summary>No reliable local/remote diagnosis is available.</summary>
    Unknown
}

/// <summary>A bounded safe diagnosis, without exceptions, tokens, keys or unrestricted claims.</summary>
public sealed class SecurityFailure
{
    /// <summary>Creates a known category while keeping actual status and evidence provenance separate.</summary>
    public SecurityFailure(SecurityFailureKind kind, string message,
        SecurityFailureSource source = SecurityFailureSource.Local, int? httpStatusCode = null,
        string? providerCode = null, string? transportCode = null)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (httpStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
        }
        Kind = kind;
        Message = SecurityText.Bounded(message, 512, nameof(message));
        Source = source;
        HttpStatusCode = httpStatusCode;
        ProviderCode = providerCode is null ? null : SecurityText.Bounded(providerCode, 128, nameof(providerCode));
        TransportCode = transportCode is null ? null : SecurityText.Bounded(transportCode, 128, nameof(transportCode));
    }

    /// <summary>Gets the known category.</summary>
    public SecurityFailureKind Kind
    {
        get;
    }
    /// <summary>Gets the stable portable code.</summary>
    public string Code => Kind switch
    {
        SecurityFailureKind.CredentialUnavailable => "security.credential_unavailable",
        SecurityFailureKind.CredentialInvalid => "security.credential_invalid",
        SecurityFailureKind.CredentialExpired => "security.credential_expired",
        SecurityFailureKind.Unauthenticated => "security.unauthenticated",
        SecurityFailureKind.IdentityUnmapped => "security.identity_unmapped",
        SecurityFailureKind.CapabilityDenied => "security.capability_denied",
        SecurityFailureKind.ValidationUnavailable => "security.validation_unavailable",
        SecurityFailureKind.AuthenticationRejected => "security.authentication_rejected",
        _ => "security.cause_unknown"
    };
    /// <summary>Gets the bounded explicitly safe explanation.</summary>
    public string Message
    {
        get;
    }
    /// <summary>Gets whether reliable evidence is local, remote or unknown.</summary>
    public SecurityFailureSource Source
    {
        get;
    }
    /// <summary>Gets an actual HTTP status only when one exists.</summary>
    public int? HttpStatusCode
    {
        get;
    }
    /// <summary>Gets an explicitly safe provider code, when established.</summary>
    public string? ProviderCode
    {
        get;
    }
    /// <summary>Gets an actual bounded transport classification, when established.</summary>
    public string? TransportCode
    {
        get;
    }
}

/// <summary>The finite availability state of one explicitly configured profile.</summary>
public enum SecurityProfileAvailability
{
    /// <summary>The enabled integration has usable receiving policy/credential material.</summary>
    Available,
    /// <summary>A required integration or credential is unavailable.</summary>
    Unavailable,
    /// <summary>The actual known credential expiry has passed.</summary>
    Expired,
    /// <summary>Admission has closed for stopping.</summary>
    Stopping
}

/// <summary>Safe detached status; it contains no credential material or complete grants.</summary>
public sealed class SecurityProfileStatus
{
    /// <summary>Copies one registered profile's current availability and known active leaf expiry.</summary>
    public SecurityProfileStatus(string profile, SecurityProfileAvailability availability,
        SecurityFailure? failure = null, DateTimeOffset? credentialExpiresAtUtc = null)
    {
        Profile = SecurityText.Alias(profile, nameof(profile));
        if (!Enum.IsDefined(availability))
        {
            throw new ArgumentOutOfRangeException(nameof(availability));
        }
        Availability = availability;
        Failure = failure;
        CredentialExpiresAtUtc = credentialExpiresAtUtc?.ToUniversalTime();
    }

    /// <summary>Gets the registered finite alias.</summary>
    public string Profile
    {
        get;
    }
    /// <summary>Gets the actual known state.</summary>
    public SecurityProfileAvailability Availability
    {
        get;
    }
    /// <summary>Gets a safe cause/degraded reason, when known.</summary>
    public SecurityFailure? Failure
    {
        get;
    }
    /// <summary>Gets known active leaf expiry; no leaf is represented by null.</summary>
    public DateTimeOffset? CredentialExpiresAtUtc
    {
        get;
    }
}

/// <summary>Publishes safe owned state; reporting never authenticates a caller or changes grants.</summary>
public interface ISecurityProfileStatusReporter
{
    /// <summary>Publishes status for a registered profile alias.</summary>
    void Publish(SecurityProfileStatus status);
    /// <summary>Records one material certificate-candidate terminal transition, never each unchanged poll.</summary>
    void RecordCertificateReload(string profile, SecurityCertificateReloadOutcome outcome);
}

/// <summary>Reads bounded already-owned status without credential/source I/O.</summary>
public interface ISecurityProfileStatusSource
{
    /// <summary>Copies current status for the finite profile catalog.</summary>
    IReadOnlyList<SecurityProfileStatus> CaptureStatus();
}

/// <summary>The finite terminal outcomes of a changed certificate candidate.</summary>
public enum SecurityCertificateReloadOutcome
{
    /// <summary>A validated generation was atomically adopted.</summary>
    Adopted,
    /// <summary>A changed candidate was rejected.</summary>
    Rejected,
    /// <summary>Adoption was deferred by the finite generation capacity.</summary>
    Deferred
}

internal static class SecurityFailures
{
    internal static SecurityFailure Unauthenticated() => new(SecurityFailureKind.Unauthenticated,
        "No acceptable authenticated caller was established.");
    internal static SecurityFailure Unmapped() => new(SecurityFailureKind.IdentityUnmapped,
        "The authenticated identity has no applicable trusted mapping.");
    internal static SecurityFailure Denied() => new(SecurityFailureKind.CapabilityDenied,
        "The actual capability and target were not authorized.");
    internal static SecurityFailure Expired() => new(SecurityFailureKind.CredentialExpired,
        "The established credential is expired.");
}
