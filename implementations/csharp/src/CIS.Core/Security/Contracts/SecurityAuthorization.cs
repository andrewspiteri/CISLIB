namespace CIS.Security;

/// <summary>The finite resource selections of a complete grant.</summary>
public enum CapabilityResourceSelection
{
    /// <summary>The application/environment itself, with no resource.</summary>
    TargetOnly,
    /// <summary>One exact resource type and identifier.</summary>
    ExactResource,
    /// <summary>Any identifier of one explicit resource type, still within the configured application/environment.</summary>
    AllOfType
}

/// <summary>The authoritative object affected by an operation, independent of caller scope.</summary>
public sealed record CapabilityTarget
{
    /// <summary>Creates an exact bounded target; a resource type and identifier are supplied together.</summary>
    public CapabilityTarget(string application, string environment, string? resourceType = null, string? resourceIdentifier = null)
    {
        Application = SecurityText.Application(application, nameof(application));
        Environment = SecurityText.Application(environment, nameof(environment));
        if ((resourceType is null) != (resourceIdentifier is null))
        {
            throw new ArgumentException("A resource requires both its type and identifier.");
        }
        ResourceType = SecurityText.OptionalIdentifier(resourceType, nameof(resourceType));
        ResourceIdentifier = SecurityText.OptionalIdentifier(resourceIdentifier, nameof(resourceIdentifier));
    }

    /// <summary>Gets the target application, which may differ from the caller.</summary>
    public string Application
    {
        get;
    }
    /// <summary>Gets the target environment.</summary>
    public string Environment
    {
        get;
    }
    /// <summary>Gets the resource type, or null for an application/environment operation.</summary>
    public string? ResourceType
    {
        get;
    }
    /// <summary>Gets the exact resource identifier, or null.</summary>
    public string? ResourceIdentifier
    {
        get;
    }
}

/// <summary>One complete relationship; dimensions from separate grants are never combined.</summary>
public sealed class CapabilityGrant
{
    /// <summary>Creates a grant for exactly one subject or mapped role.</summary>
    public CapabilityGrant(string profile, string capability, string targetApplication, string targetEnvironment,
        SecuritySubject? subject = null, string? mappedRole = null,
        CapabilityResourceSelection resourceSelection = CapabilityResourceSelection.TargetOnly,
        string? resourceType = null, string? resourceIdentifier = null, string? requiredProviderRole = null)
    {
        Profile = SecurityText.Alias(profile, nameof(profile));
        Capability = SecurityText.Capability(capability, nameof(capability));
        TargetApplication = SecurityText.Application(targetApplication, nameof(targetApplication));
        TargetEnvironment = SecurityText.Application(targetEnvironment, nameof(targetEnvironment));
        if ((subject is null) == (mappedRole is null))
        {
            throw new ArgumentException("A complete grant selects exactly one subject or mapped role.");
        }
        if (!Enum.IsDefined(resourceSelection))
        {
            throw new ArgumentOutOfRangeException(nameof(resourceSelection));
        }
        Subject = subject;
        MappedRole = SecurityText.OptionalIdentifier(mappedRole, nameof(mappedRole));
        ResourceSelection = resourceSelection;
        ResourceType = SecurityText.OptionalIdentifier(resourceType, nameof(resourceType));
        ResourceIdentifier = SecurityText.OptionalIdentifier(resourceIdentifier, nameof(resourceIdentifier));
        RequiredProviderRole = SecurityText.OptionalIdentifier(requiredProviderRole, nameof(requiredProviderRole));
        bool valid = resourceSelection switch
        {
            CapabilityResourceSelection.TargetOnly => ResourceType is null && ResourceIdentifier is null,
            CapabilityResourceSelection.ExactResource => ResourceType is not null && ResourceIdentifier is not null,
            CapabilityResourceSelection.AllOfType => ResourceType is not null && ResourceIdentifier is null,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException("Resource fields must exactly match the selected grant shape.");
        }
    }

    /// <summary>Gets the selected profile alias.</summary>
    public string Profile
    {
        get;
    }
    /// <summary>Gets the registered action.</summary>
    public string Capability
    {
        get;
    }
    /// <summary>Gets the exact target application.</summary>
    public string TargetApplication
    {
        get;
    }
    /// <summary>Gets the exact target environment.</summary>
    public string TargetEnvironment
    {
        get;
    }
    /// <summary>Gets the issuer-qualified subject selector, mutually exclusive with MappedRole.</summary>
    public SecuritySubject? Subject
    {
        get;
    }
    /// <summary>Gets a role established by the trusted mapping, mutually exclusive with Subject.</summary>
    public string? MappedRole
    {
        get;
    }
    /// <summary>Gets the complete resource selection.</summary>
    public CapabilityResourceSelection ResourceSelection
    {
        get;
    }
    /// <summary>Gets the selected resource type.</summary>
    public string? ResourceType
    {
        get;
    }
    /// <summary>Gets the exact selected resource identifier.</summary>
    public string? ResourceIdentifier
    {
        get;
    }
    /// <summary>Gets an additional validated provider-role requirement; Entra subject grants require one.</summary>
    public string? RequiredProviderRole
    {
        get;
    }
}

/// <summary>A complete Allow or Deny result, never a partially allowed set.</summary>
public sealed class AuthorizationDecision
{
    private AuthorizationDecision(bool allowed, SecurityFailure? failure)
    {
        IsAllowed = allowed;
        Failure = failure;
    }

    /// <summary>Gets whether every required native and capability requirement passed.</summary>
    public bool IsAllowed
    {
        get;
    }
    /// <summary>Gets the stable safe denial reason, or null on Allow.</summary>
    public SecurityFailure? Failure
    {
        get;
    }
    /// <summary>Gets the stable reason code.</summary>
    public string Code => Failure?.Code ?? "security.allowed";
    /// <summary>Gets a successful composite result.</summary>
    public static AuthorizationDecision Allow { get; } = new(true, null);
    /// <summary>Creates a denied result with the actual known safe cause.</summary>
    public static AuthorizationDecision Deny(SecurityFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(false, failure);
    }
}

/// <summary>Checks the established caller through native authorization and the shared complete-grant handler.</summary>
public interface ICapabilityAuthorizer
{
    /// <summary>Requires every registered capability against the same actual target and awaits true evaluation settlement.</summary>
    ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
        CancellationToken cancellationToken = default);
}

/// <summary>Convenient single-capability use of the same composite authorizer.</summary>
public static class CapabilityAuthorizerExtensions
{
    /// <summary>Authorizes one capability without introducing a second evaluator.</summary>
    public static ValueTask<AuthorizationDecision> AuthorizeAsync(this ICapabilityAuthorizer authorizer,
        string capability, CapabilityTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizer);
        return authorizer.AuthorizeAsync([capability], target, cancellationToken);
    }
}
