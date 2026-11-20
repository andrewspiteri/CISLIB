using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CIS.Security;

/// <summary>One validated immutable startup policy. Changes require replacement of the affected instance.</summary>
public sealed class SecurityPolicySnapshot
{
    private readonly Dictionary<string, SecurityProfile> profiles;
    private readonly HashSet<string> capabilities;

    private SecurityPolicySnapshot(SecurityOptions options, int profileCapacity)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (profileCapacity is < 1 or > 4096 || options.MaximumCapabilities is < 1 or > 65536
            || options.MaximumGrants is < 1 or > 1048576)
        {
            throw new ArgumentOutOfRangeException(nameof(profileCapacity), "Select finite positive policy capacities.");
        }
        if (options.Profiles.Count > profileCapacity || options.Capabilities.Count > options.MaximumCapabilities
            || options.Grants.Count > options.MaximumGrants)
        {
            throw new ArgumentException("The security policy exceeds its declared finite capacity.", nameof(options));
        }
        ProfileCapacity = profileCapacity;
        SecurityProfile[] copiedProfiles = options.Profiles.ToArray();
        string[] copiedCapabilities = options.Capabilities.Select(value => SecurityText.Capability(value, nameof(options))).ToArray();
        CapabilityGrant[] copiedGrants = options.Grants.ToArray();
        if (copiedProfiles.Any(profile => profile is null) || copiedGrants.Any(grant => grant is null))
        {
            throw new ArgumentException("Profiles and grants cannot be null.", nameof(options));
        }
        profiles = new(StringComparer.Ordinal);
        foreach (SecurityProfile profile in copiedProfiles)
        {
            if (!profiles.TryAdd(profile.Alias, profile))
            {
                throw new ArgumentException("Security profile aliases must be distinct.", nameof(options));
            }
            if (profile.IdentityMappings.Select(mapping => (mapping.Subject, mapping.ActorKind)).Distinct().Count()
                != profile.IdentityMappings.Count)
            {
                throw new ArgumentException("One issuer-qualified actor has exactly one mapping per profile.", nameof(options));
            }
            if (profile.Kind != SecurityProfileKind.Native
                && profile.IdentityMappings.Any(mapping => mapping.ActorKind != SecurityActorKind.Application))
            {
                throw new ArgumentException("Workload profiles do not authenticate human actors.", nameof(options));
            }
            if (profile.Kind == SecurityProfileKind.EntraWorkloadIdentity
                && profile.IdentityMappings.Any(mapping => mapping.ClientApplicationId is null))
            {
                throw new ArgumentException("Entra mappings require their exact client application identifier.", nameof(options));
            }
            if ((profile.Roles & SecurityProfileRoles.Outbound) != 0)
            {
                LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
                SecurityLocalIdentityBinding expected = profile.OutboundIdentity!;
                if (local.Application == "unknown" || local.Environment == "unknown"
                    || !string.Equals(local.Application, expected.Application, StringComparison.Ordinal)
                    || !string.Equals(local.Environment, expected.Environment, StringComparison.Ordinal))
                {
                    throw new ArgumentException("The outgoing credential mapping must match known canonical local application/environment values.", nameof(options));
                }
            }
        }
        if (copiedProfiles.Where(profile => (profile.Roles & SecurityProfileRoles.Inbound) != 0)
            .Select(profile => profile.AuthenticationScheme).Distinct(StringComparer.Ordinal).Count()
            != copiedProfiles.Count(profile => (profile.Roles & SecurityProfileRoles.Inbound) != 0))
        {
            throw new ArgumentException("Each inbound profile selects its own native authentication scheme.", nameof(options));
        }
        capabilities = new(copiedCapabilities, StringComparer.Ordinal);
        if (capabilities.Count != copiedCapabilities.Length)
        {
            throw new ArgumentException("Capability names must be distinct.", nameof(options));
        }
        foreach (CapabilityGrant grant in copiedGrants)
        {
            if (!profiles.TryGetValue(grant.Profile, out SecurityProfile? profile)
                || !capabilities.Contains(grant.Capability))
            {
                throw new ArgumentException("Every grant references a registered profile and capability.", nameof(options));
            }
            if (grant.Subject is { } subject && !profile.IdentityMappings.Any(mapping => mapping.Subject == subject))
            {
                throw new ArgumentException("Subject grants reference an explicitly mapped issuer-qualified identity.", nameof(options));
            }
            if (grant.MappedRole is { } role
                && !profile.IdentityMappings.Any(mapping => mapping.MappedRoles.Contains(role, StringComparer.Ordinal)))
            {
                throw new ArgumentException("Role grants reference a role explicitly admitted by an identity mapping.", nameof(options));
            }
            if (profile.Kind == SecurityProfileKind.EntraWorkloadIdentity
                && grant.MappedRole is null && grant.RequiredProviderRole is null)
            {
                throw new ArgumentException("Every Entra capability grant requires an actual validated application role.", nameof(options));
            }
        }
        Profiles = Array.AsReadOnly(copiedProfiles);
        Capabilities = Array.AsReadOnly(copiedCapabilities);
        Grants = Array.AsReadOnly(copiedGrants);
        PolicyVersion = options.PolicyVersion is null ? Digest(copiedProfiles, copiedCapabilities, copiedGrants)
            : SecurityText.Identifier(options.PolicyVersion, nameof(options.PolicyVersion));
    }

    /// <summary>Gets the finite declared profile capacity, also used for telemetry reservations.</summary>
    public int ProfileCapacity
    {
        get;
    }
    /// <summary>Gets the immutable selected profiles.</summary>
    public IReadOnlyList<SecurityProfile> Profiles
    {
        get;
    }
    /// <summary>Gets the immutable registered capabilities.</summary>
    public IReadOnlyList<string> Capabilities
    {
        get;
    }
    /// <summary>Gets the immutable complete grant catalog.</summary>
    public IReadOnlyList<CapabilityGrant> Grants
    {
        get;
    }
    /// <summary>Gets the explicit version or deterministic digest of non-secret policy data.</summary>
    public string PolicyVersion
    {
        get;
    }

    /// <summary>Finds one exact registered alias without changing selection or creating an integration.</summary>
    public bool TryGetProfile(string alias, [NotNullWhen(true)] out SecurityProfile? profile)
    {
        SecurityText.Alias(alias, nameof(alias));
        return profiles.TryGetValue(alias, out profile);
    }

    /// <summary>Gets a selected profile or rejects an unknown application configuration reference.</summary>
    public SecurityProfile GetProfile(string alias)
    {
        SecurityText.Alias(alias, nameof(alias));
        return profiles.TryGetValue(alias, out SecurityProfile? profile)
            ? profile : throw new ArgumentException("The security profile is not registered.", nameof(alias));
    }

    /// <summary>Tests one exact registered capability; unknown names never authorize.</summary>
    public bool IsRegisteredCapability(string capability)
    {
        SecurityText.Capability(capability, nameof(capability));
        return capabilities.Contains(capability);
    }

    internal static SecurityPolicySnapshot Capture(SecurityOptions options, int profileCapacity) => new(options, profileCapacity);
    internal static string PolicyName(string profile) => "cis.security." + SecurityText.Alias(profile, nameof(profile));

    private static string Digest(SecurityProfile[] profiles, string[] capabilities, CapabilityGrant[] grants)
    {
        string canonical = JsonSerializer.Serialize(new
        {
            Version = 1,
            Profiles = profiles.Select(profile => JsonSerializer.Serialize(profile)).Order(StringComparer.Ordinal).ToArray(),
            Capabilities = capabilities.Order(StringComparer.Ordinal).ToArray(),
            Grants = grants.Select(grant => JsonSerializer.Serialize(grant)).Order(StringComparer.Ordinal).ToArray()
        });
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
