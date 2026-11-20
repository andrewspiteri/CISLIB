using CIS.Security;

namespace CIS.RestClient;

/// <summary>The actual credential boundary of a security-bound native REST client.</summary>
public enum RestSecurityCredentialKind
{
    /// <summary>Credentials are attached through the existing final authentication callback.</summary>
    RequestAuthentication,
    /// <summary>The native TLS transport authenticates using an owned certificate generation.</summary>
    ClientCertificate
}

/// <summary>One immutable credential and destination policy. Its reference identity is its borrowing owner.</summary>
/// <remarks>The metadata constructor is a low-level transport primitive; it does not authenticate or
/// activate a CIS security profile. Provider adapters use the actual root policy constructor.</remarks>
public sealed class RestSecurityBinding
{
    /// <summary>Creates low-level native transport policy without establishing trusted caller identity.</summary>
    public RestSecurityBinding(string profile, SecuritySubject credentialIdentity, string policyVersion,
        RestSecurityCredentialKind credentialKind, IReadOnlyList<string> allowedOrigins)
    {
        Profile = SecurityText.Alias(profile, nameof(profile));
        ArgumentNullException.ThrowIfNull(credentialIdentity);
        CredentialIdentity = credentialIdentity;
        PolicyVersion = SecurityText.Identifier(policyVersion, nameof(policyVersion));
        if (!Enum.IsDefined(credentialKind))
        {
            throw new ArgumentOutOfRangeException(nameof(credentialKind));
        }
        CredentialKind = credentialKind;
        AllowedOrigins = Array.AsReadOnly(CopyOrigins(allowedOrigins));
    }

    /// <summary>Derives provider policy from the exact immutable snapshot of the application's one root.</summary>
    public RestSecurityBinding(SecurityPolicySnapshot policy, string profile, RestSecurityCredentialKind credentialKind)
        : this(profile, OutboundProfile(policy, profile).OutboundIdentity!.CredentialIdentity,
            policy.PolicyVersion, credentialKind, policy.GetProfile(profile).AllowedOrigins)
    {
        PolicyOwner = policy;
        ProfilePolicy = policy.GetProfile(profile);
        bool certificate = ProfilePolicy.Kind is SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment;
        if (ProfilePolicy.Kind != SecurityProfileKind.Native
            && certificate != (credentialKind == RestSecurityCredentialKind.ClientCertificate))
        {
            throw new ArgumentException("The REST credential boundary must match the selected security profile.", nameof(credentialKind));
        }
    }

    /// <summary>Gets the registered profile alias.</summary>
    public string Profile
    {
        get;
    }
    /// <summary>Gets the intended issuer-qualified workload credential identity.</summary>
    public SecuritySubject CredentialIdentity
    {
        get;
    }
    /// <summary>Gets the immutable policy version, never a credential.</summary>
    public string PolicyVersion
    {
        get;
    }
    /// <summary>Gets the actual request or TLS authentication boundary.</summary>
    public RestSecurityCredentialKind CredentialKind
    {
        get;
    }
    /// <summary>Gets the exact nonempty normalized HTTPS origin set.</summary>
    public IReadOnlyList<string> AllowedOrigins
    {
        get;
    }
    /// <summary>Gets the exact owning root snapshot; null identifies the low-level metadata primitive.</summary>
    public SecurityPolicySnapshot? PolicyOwner
    {
        get;
    }
    /// <summary>Gets the exact owning profile within PolicyOwner.</summary>
    public SecurityProfile? ProfilePolicy
    {
        get;
    }

    /// <summary>Rejects a provider binding belonging to any other root snapshot, even with equal display metadata.</summary>
    public void ValidateForPolicy(SecurityPolicySnapshot policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!ReferenceEquals(PolicyOwner, policy) || !ReferenceEquals(ProfilePolicy, policy.GetProfile(Profile)))
        {
            throw new InvalidOperationException("The provider REST binding must belong to this exact immutable root policy.");
        }
    }

    internal bool Allows(Uri destination) => destination.IsAbsoluteUri
        && destination.Scheme == Uri.UriSchemeHttps && destination.UserInfo.Length == 0
        && AllowedOrigins.Contains(destination.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase);

    internal bool HasExactOrigins(IReadOnlyList<string> origins) => origins.Count == AllowedOrigins.Count
        && new HashSet<string>(AllowedOrigins, StringComparer.OrdinalIgnoreCase).SetEquals(origins);

    private static SecurityProfile OutboundProfile(SecurityPolicySnapshot policy, string profile)
    {
        ArgumentNullException.ThrowIfNull(policy);
        SecurityProfile selected = policy.GetProfile(profile);
        if ((selected.Roles & SecurityProfileRoles.Outbound) == 0)
        {
            throw new ArgumentException("A provider REST binding requires an explicitly enabled outbound profile.", nameof(profile));
        }
        return selected;
    }

    private static string[] CopyOrigins(IReadOnlyList<string> origins)
    {
        ArgumentNullException.ThrowIfNull(origins);
        if (origins.Count is < 1 or > 32)
        {
            throw new ArgumentException("Bind between one and thirty-two exact HTTPS origins.", nameof(origins));
        }
        var copied = new List<string>();
        foreach (string origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps
                || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            {
                throw new ArgumentException("Security origins must be exact HTTPS origins without paths or credentials.", nameof(origins));
            }
            string normalized = uri.GetLeftPart(UriPartial.Authority);
            if (copied.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Security origins must be distinct.", nameof(origins));
            }
            copied.Add(normalized);
        }
        return copied.ToArray();
    }
}
