using System.Security.Claims;
using System.Diagnostics.CodeAnalysis;
using CIS.Security.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace CIS.Security.Entra;

/// <summary>Names of bounded reported metadata accepted by this adapter. These headers never authenticate a caller.</summary>
public static class EntraSecurityDefaults
{
    /// <summary>Optional unverified instance metadata, retained separately from application authentication.</summary>
    public const string ReportedInstanceHeader = "X-CIS-Instance-Id";
}

internal sealed class EntraIdentityProjector(EntraProfileRuntime runtime) : ISecurityIdentityProjector
{
    public ValueTask<SecurityProjectionResult> ProjectAsync(HttpContext context, AuthenticateResult authentication,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!authentication.Succeeded || authentication.Principal is null
            || !string.Equals(authentication.Ticket?.AuthenticationScheme,
                runtime.Options.Profile.AuthenticationScheme, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(Rejected());
        }
        if (!EntraJwtEvents.TryGetEvidence(authentication, runtime, out EntraValidatedEvidence? evidence))
        {
            return ValueTask.FromResult(Rejected());
        }
        EntraClaims claims = evidence.Claims;
        string? reported = null;
        if (context.Request.Headers.TryGetValue(EntraSecurityDefaults.ReportedInstanceHeader, out var values))
        {
            if (values.Count != 1)
            {
                return ValueTask.FromResult(Rejected());
            }
            reported = values[0];
        }
        try
        {
            TimeProvider clock = context.RequestServices.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
            if (evidence.Expiry < clock.GetUtcNow() - runtime.Options.ClockSkew)
            {
                return ValueTask.FromResult(SecurityProjectionResult.Rejected(new(SecurityFailureKind.CredentialExpired,
                    "The native workload authentication evidence is outside its configured lifetime tolerance.")));
            }
            var identity = new AuthenticatedSecurityIdentity(new(runtime.Options.Issuer, claims.ObjectId),
                SecurityActorKind.Application, new(clock.GetUtcNow(), evidence.Expiry), "entra-v2-workload",
                claims.Roles, claims.ClientId, new CallerInstanceEvidence(reported));
            return ValueTask.FromResult(SecurityProjectionResult.Authenticated(identity));
        }
        catch (ArgumentException)
        {
            // Only construction of bounded credential-derived facts is translated here.
            // Projector/provider programming faults outside these constructors remain faults.
            return ValueTask.FromResult(Rejected());
        }
    }

    private static SecurityProjectionResult Rejected() => SecurityProjectionResult.Rejected(new(
        SecurityFailureKind.CredentialInvalid, "The authenticated token does not satisfy the selected workload identity profile."));
}

internal sealed record EntraClaims(string ObjectId, string ClientId, IReadOnlyList<string> Roles)
{
    internal static bool TryRead(ClaimsPrincipal principal, EntraOptionsSnapshot options, [NotNullWhen(true)] out EntraClaims? claims)
    {
        claims = null;
        using IEnumerator<ClaimsIdentity> identities = principal.Identities.GetEnumerator();
        if (!identities.MoveNext() || !identities.Current.IsAuthenticated || identities.MoveNext()
            || principal.FindFirst("scp") is not null
            || !Single(principal, "ver", out string? version) || version != "2.0"
            || !Single(principal, "idtyp", out string? kind) || kind != "app"
            || !Single(principal, "tid", out string? tenant) || tenant != options.TenantId
            || !Single(principal, "iss", out string? issuer) || issuer != options.Issuer
            || !Single(principal, "aud", out string? audience) || audience != options.Audience
            || !Single(principal, "oid", out string? objectId)
            || !Single(principal, "azp", out string? clientId))
        {
            return false;
        }
        try
        {
            _ = EntraText.Guid(objectId, nameof(objectId));
            _ = EntraText.Guid(clientId, nameof(clientId));
            string[] roles = principal.FindAll("roles").Take(257).Select(claim => claim.Value).ToArray();
            if (roles.Length > 256 || roles.Distinct(StringComparer.Ordinal).Count() != roles.Length)
            {
                return false;
            }
            foreach (string role in roles)
            {
                _ = EntraText.Identifier(role, nameof(roles));
            }
            claims = new(objectId!, clientId!, Array.AsReadOnly(roles));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool Single(ClaimsPrincipal principal, string type, out string? value)
    {
        using IEnumerator<Claim> matches = principal.FindAll(type).GetEnumerator();
        if (!matches.MoveNext())
        {
            value = null;
            return false;
        }
        value = matches.Current.Value;
        return !matches.MoveNext();
    }
}
