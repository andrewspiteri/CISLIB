using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace CIS.Security.Hosting;

/// <summary>Bounded validated provider facts supplied after successful selected-scheme native authentication.</summary>
public sealed class AuthenticatedSecurityIdentity
{
    /// <summary>Copies established facts; the Core integration still applies the exact trusted mapping.</summary>
    public AuthenticatedSecurityIdentity(SecuritySubject subject, SecurityActorKind actorKind,
        SecurityAuthenticationEvidence authentication, string authenticationMethod,
        IReadOnlyList<string>? providerRoles = null, string? clientApplicationId = null,
        CallerInstanceEvidence? instance = null, SecuritySubject? humanSubject = null, string? verifiedDelegation = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(authentication);
        if (!Enum.IsDefined(actorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(actorKind));
        }
        Subject = subject;
        ActorKind = actorKind;
        Authentication = authentication;
        AuthenticationMethod = SecurityText.Alias(authenticationMethod, nameof(authenticationMethod));
        ProviderRoles = SecurityIdentityMapping.CopyRoles(providerRoles);
        ClientApplicationId = SecurityText.OptionalIdentifier(clientApplicationId, nameof(clientApplicationId));
        Instance = instance ?? new();
        HumanSubject = humanSubject;
        VerifiedDelegation = SecurityText.OptionalIdentifier(verifiedDelegation, nameof(verifiedDelegation));
    }

    /// <summary>Gets the established issuer-qualified stable subject.</summary>
    public SecuritySubject Subject
    {
        get;
    }
    /// <summary>Gets the actual authenticated actor kind.</summary>
    public SecurityActorKind ActorKind
    {
        get;
    }
    /// <summary>Gets validation/expiry facts without credential material.</summary>
    public SecurityAuthenticationEvidence Authentication
    {
        get;
    }
    /// <summary>Gets the actual authentication method.</summary>
    public string AuthenticationMethod
    {
        get;
    }
    /// <summary>Gets only bounded validated role identifiers, not an unrestricted claims bag.</summary>
    public IReadOnlyList<string> ProviderRoles
    {
        get;
    }
    /// <summary>Gets the validated provider client application identifier, where applicable.</summary>
    public string? ClientApplicationId
    {
        get;
    }
    /// <summary>Gets separately classified reported/verified instance evidence.</summary>
    public CallerInstanceEvidence Instance
    {
        get;
    }
    /// <summary>Gets a separately established human actor, when an explicit native integration validates it.</summary>
    public SecuritySubject? HumanSubject
    {
        get;
    }
    /// <summary>Gets verified delegation provenance only when actually validated.</summary>
    public string? VerifiedDelegation
    {
        get;
    }
}

/// <summary>A known projection result; expected mapping/validation failures are distinct from programming faults.</summary>
public sealed class SecurityProjectionResult
{
    private SecurityProjectionResult(AuthenticatedSecurityIdentity? identity, SecurityFailure? failure)
    {
        Identity = identity;
        Failure = failure;
    }

    /// <summary>Gets validated provider facts, or null for a declared rejection.</summary>
    public AuthenticatedSecurityIdentity? Identity
    {
        get;
    }
    /// <summary>Gets the safe known rejection, or null.</summary>
    public SecurityFailure? Failure
    {
        get;
    }
    /// <summary>Supplies established facts for the Core trusted mapping.</summary>
    public static SecurityProjectionResult Authenticated(AuthenticatedSecurityIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new(identity, null);
    }
    /// <summary>Declares a known projection failure without fabricating caller identity.</summary>
    public static SecurityProjectionResult Rejected(SecurityFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(null, failure);
    }
}

/// <summary>A platform integration invoked only after the explicitly selected native scheme authenticates successfully.</summary>
/// <remarks>Implementations validate provider-specific restrictions, then supply bounded facts. They do not set caller scope.</remarks>
public interface ISecurityIdentityProjector
{
    /// <summary>Projects actual authenticated evidence. Caller-controlled headers cannot replace credential identity.</summary>
    ValueTask<SecurityProjectionResult> ProjectAsync(HttpContext context, AuthenticateResult authentication,
        CancellationToken cancellationToken = default);
}

/// <summary>Native hosting choices; selectors must use trusted listener data, never the untrusted Host header.</summary>
public sealed class SecurityWebOptions
{
    /// <summary>Gets or sets explicit profile selection for actual listener/operational bindings.</summary>
    /// <remarks>Null uses explicit endpoint metadata only. A configured selector returning null selects no profile.</remarks>
    public Func<HttpContext, string?>? SelectProfile
    {
        get; set;
    }
}
