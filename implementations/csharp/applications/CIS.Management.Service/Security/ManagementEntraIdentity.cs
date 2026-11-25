using System.Buffers;
using System.Security.Claims;
using System.Text;
using CIS.Security;

namespace CIS.Management.Service.Security;

/// <summary>Closed provider claim rules for the explicitly selected mixed Management reference.</summary>
internal sealed record ManagementEntraIdentity(SecuritySubject Subject, SecurityActorKind ActorKind,
    string ClientId, IReadOnlyList<string> Roles)
{
    internal static void ValidateOptions(ManagementSecurityOptions options)
    {
        RequireGuid(options.EntraTenantId);
        RequireGuid(options.EntraPortalClientId);
        if (!IsScope(options.EntraOperatorScope))
        {
            throw new ArgumentException("Configure one exact bounded delegated Management scope.");
        }

        string issuer = Issuer(options);
        foreach (SecurityIdentityMapping mapping in options.IdentityMappings)
        {
            RequireGuid(mapping.Subject.Identifier);
            RequireGuid(mapping.ClientApplicationId);
            if (mapping.Subject.Issuer != issuer
                || mapping.ActorKind == SecurityActorKind.User
                    && (mapping.ClientApplicationId != options.EntraPortalClientId
                        || mapping.Application is null || mapping.Environment is null))
            {
                throw new ArgumentException("Management Entra mappings require the selected issuer, object and client; user mappings name the portal application/environment.");
            }
        }

        if (options.IdentityMappings.Select(mapping => mapping.Subject).Distinct().Count() != options.IdentityMappings.Count)
        {
            throw new ArgumentException("A Management Entra object cannot have multiple actor mappings.");
        }

        foreach (CapabilityGrant grant in options.Grants)
        {
            if (grant.Subject is { } subject
                && options.IdentityMappings.Any(mapping => mapping.Subject == subject && mapping.ActorKind == SecurityActorKind.Application)
                && grant.RequiredProviderRole is null)
            {
                throw new ArgumentException("Management Entra workload subject grants require an actual provider application role.");
            }
        }
    }

    internal static string Issuer(ManagementSecurityOptions options)
    {
        return new Uri(options.EntraAuthorityHost, options.EntraTenantId + "/v2.0").AbsoluteUri;
    }

    internal static bool TryRead(ClaimsPrincipal principal, ManagementSecurityOptions options,
        out ManagementEntraIdentity? identity)
    {
        identity = null;
        using IEnumerator<ClaimsIdentity> identities = principal.Identities.GetEnumerator();
        if (!identities.MoveNext() || !identities.Current.IsAuthenticated || identities.MoveNext()
            || !Single(principal, "ver", out string? version) || version != "2.0"
            || !Single(principal, "iss", out string? issuer) || issuer != Issuer(options)
            || !Single(principal, "aud", out string? audience) || audience != options.EntraAudience
            || !Single(principal, "tid", out string? tenant) || tenant != options.EntraTenantId
            || !Single(principal, "oid", out string? objectId) || !IsGuid(objectId)
            || !Single(principal, "azp", out string? clientId) || !IsGuid(clientId))
        {
            return false;
        }

        string[] kinds = principal.FindAll("idtyp").Select(claim => claim.Value).Take(2).ToArray();
        string[] scopes = principal.FindAll("scp").Select(claim => claim.Value).Take(2).ToArray();
        SecurityActorKind actor;
        if (kinds.Length == 1 && kinds[0] == "app" && scopes.Length == 0)
        {
            actor = SecurityActorKind.Application;
        }
        else if (kinds.Length <= 1 && (kinds.Length == 0 || kinds[0] == "user")
            && scopes.Length == 1 && clientId == options.EntraPortalClientId
            && HasRequiredScope(scopes[0], options.EntraOperatorScope!))
        {
            actor = SecurityActorKind.User;
        }
        else
        {
            return false;
        }

        string[] roles = principal.FindAll("roles").Select(claim => claim.Value).Take(257).ToArray();
        if (roles.Length > 256 || roles.Distinct(StringComparer.Ordinal).Count() != roles.Length
            || roles.Any(role => !IsRole(role)))
        {
            return false;
        }

        identity = new(new(issuer!, objectId!), actor, clientId!, Array.AsReadOnly(roles));
        return true;
    }

    private static bool HasRequiredScope(string value, string required)
    {
        if (value.Length > 4096)
        {
            return false;
        }

        string[] scopes = value.Split(' ');
        return scopes.Length is > 0 and <= 128 && scopes.All(IsScope)
            && scopes.Distinct(StringComparer.Ordinal).Count() == scopes.Length
            && scopes.Contains(required, StringComparer.Ordinal);
    }

    private static bool IsScope(string? value)
    {
        return value is { Length: > 0 and <= 256 }
            && value.All(character => character is >= '!' and <= '~' && character is not ('"' or '\\'));
    }

    private static bool IsRole(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
        {
            return false;
        }

        ReadOnlySpan<char> remaining = value.AsSpan();
        int count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out Rune scalar, out int consumed) != OperationStatus.Done
                || Rune.IsControl(scalar) || ++count > 1024)
            {
                return false;
            }

            remaining = remaining[consumed..];
        }

        return true;
    }

    private static bool Single(ClaimsPrincipal principal, string type, out string? value)
    {
        string[] values = principal.FindAll(type).Select(claim => claim.Value).Take(2).ToArray();
        value = values.Length == 1 ? values[0] : null;
        return value is not null;
    }

    private static bool IsGuid(string? value)
    {
        return Guid.TryParseExact(value, "D", out Guid identifier) && identifier != Guid.Empty;
    }

    private static void RequireGuid(string? value)
    {
        if (!IsGuid(value))
        {
            throw new ArgumentException("Configure exact nonempty directory GUIDs in D format.");
        }
    }
}
