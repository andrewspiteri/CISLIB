using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CIS.Security.Hosting;

/// <summary>Explicit native stages for the existing host; authentication precedes operational branches and authorization follows readiness.</summary>
public static class SecurityApplicationBuilderExtensions
{
    private const string AuthenticationInstalled = "CIS.Security.AuthenticationInstalled";
    private const string AuthorizationInstalled = "CIS.Security.AuthorizationInstalled";

    /// <summary>Installs native authentication and trusted caller projection after routing and before operational branches.</summary>
    public static IApplicationBuilder UseCisSecurityAuthentication(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (application.Properties.ContainsKey(AuthenticationInstalled))
        {
            throw new InvalidOperationException("Install the CIS authentication/projection stage once per pipeline.");
        }
        application.Properties.Add(AuthenticationInstalled, true);
        SecurityPolicySnapshot policy = application.ApplicationServices.GetRequiredService<SecurityPolicySnapshot>();
        SecurityWebOptions options = application.ApplicationServices.GetRequiredService<IOptions<SecurityWebOptions>>().Value;
        Func<HttpContext, string?>? selectProfile = options.SelectProfile;
        // Explicit native invocation also prevents minimal hosting's implicit insertion from defining this boundary.
        application.UseAuthentication();
        application.Use(async (context, next) =>
        {
            SecurityCallerState state = context.RequestServices.GetRequiredService<SecurityCallerState>();
            ClaimsPrincipal previous = context.User;
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            context.User = anonymous;
            using IDisposable absent = state.Establish(new(null, null, anonymous, failure: SecurityFailures.Unauthenticated()));
            try
            {
                SecurityCapabilityMetadata? metadata = context.GetEndpoint()?.Metadata.GetMetadata<SecurityCapabilityMetadata>();
                if (metadata is not null && context.GetEndpoint()!.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                {
                    throw new InvalidOperationException("An explicitly capability-protected endpoint cannot also allow anonymous execution.");
                }
                string? selectedAlias = selectProfile is { } selector ? selector(context)
                    : metadata?.Profile;
                SecurityCallerState.Authority authority = new(null, null, anonymous, failure: SecurityFailures.Unauthenticated());
                if (selectedAlias is not null && policy.TryGetProfile(selectedAlias, out SecurityProfile? profile)
                    && (profile.Roles & SecurityProfileRoles.Inbound) != 0)
                {
                    if (metadata is not null && !string.Equals(metadata.Profile, profile.Alias, StringComparison.Ordinal))
                    {
                        authority = new(profile, null, anonymous, failure: SecurityFailures.Unauthenticated());
                    }
                    else
                    {
                        AuthenticateResult authentication = await context.AuthenticateAsync(profile.AuthenticationScheme).ConfigureAwait(false);
                        context.RequestAborted.ThrowIfCancellationRequested();
                        authority = await SecurityProjection.ProjectAsync(context, policy, profile, authentication,
                            context.RequestAborted).ConfigureAwait(false);
                    }
                }
                using IDisposable trusted = state.Establish(authority);
                // An unselected surface retains its independent native authentication behavior,
                // but has no CIS caller scope. A selected rejection never falls back to it.
                context.User = selectedAlias is null ? previous : authority.Caller is null ? anonymous : authority.Principal;
                await next(context).ConfigureAwait(false);
            }
            finally
            {
                context.User = previous;
            }
        });
        return application;
    }

    /// <summary>Installs native business authorization after the existing readiness gate and before endpoint execution.</summary>
    public static IApplicationBuilder UseCisSecurityAuthorization(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!application.Properties.ContainsKey(AuthenticationInstalled) || application.Properties.ContainsKey(AuthorizationInstalled))
        {
            throw new InvalidOperationException("Install CIS authentication before the one business authorization stage.");
        }
        if (application.ApplicationServices.GetRequiredService<IAuthorizationMiddlewareResultHandler>()
            is not SecurityAuthorizationMiddlewareResultHandler)
        {
            throw new InvalidOperationException("Preserve the CIS authorization observer; register custom native result handlers before AddCisSecurity.");
        }
        application.Properties.Add(AuthorizationInstalled, true);
        application.Use((context, next) =>
        {
            context.Features.Set<SecurityAuthorizationOperation>(null);
            return next(context);
        });
        application.UseAuthorization();
        return application;
    }

}

internal static class SecurityProjection
{
    internal static async ValueTask<SecurityCallerState.Authority> ProjectAsync(HttpContext context,
        SecurityPolicySnapshot policy, SecurityProfile profile, AuthenticateResult authentication,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        if (!authentication.Succeeded || authentication.Principal is null
            || !string.Equals(authentication.Ticket?.AuthenticationScheme, profile.AuthenticationScheme, StringComparison.Ordinal)
            || !HasSingleAuthenticatedIdentity(authentication.Principal))
        {
            return new(profile, null, anonymous, failure: SecurityFailures.Unauthenticated());
        }
        ISecurityIdentityProjector projector = context.RequestServices.GetRequiredKeyedService<ISecurityIdentityProjector>(profile.Alias);
        SecurityProjectionResult projection = await projector.ProjectAsync(context, authentication, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (projection.Identity is not { } identity)
        {
            return new(profile, null, anonymous, failure: projection.Failure ?? SecurityFailures.Unmapped());
        }
        SecurityIdentityMapping? mapping = profile.IdentityMappings.FirstOrDefault(candidate => candidate.Subject == identity.Subject
            && candidate.ActorKind == identity.ActorKind);
        if (mapping is null || !string.Equals(mapping.ClientApplicationId, identity.ClientApplicationId, StringComparison.Ordinal))
        {
            return new(profile, null, anonymous, failure: SecurityFailures.Unmapped());
        }
        if (profile.Kind != SecurityProfileKind.Native && (identity.ActorKind != SecurityActorKind.Application
            || identity.HumanSubject is not null || identity.VerifiedDelegation is not null))
        {
            return new(profile, null, anonymous, failure: new(SecurityFailureKind.CredentialInvalid,
                "A workload integration cannot establish a human or delegated identity."));
        }
        TimeProvider clock = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        if (profile.Kind is SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment
            && identity.Authentication.ExpiresAtUtc is null)
        {
            return new(profile, null, anonymous, failure: new(SecurityFailureKind.ValidationUnavailable,
                "The certificate integration did not establish leaf expiry."));
        }
        if (profile.Kind is SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment
            && identity.Authentication.ExpiresAtUtc is { } expiry && expiry <= clock.GetUtcNow())
        {
            return new(profile, null, anonymous, failure: SecurityFailures.Expired());
        }
        var caller = new CallerContext(profile.Alias, identity.AuthenticationMethod, identity.Subject,
            identity.ActorKind, mapping.Application, mapping.Environment, identity.Authentication, policy.PolicyVersion,
            identity.Instance, identity.HumanSubject, identity.VerifiedDelegation);
        IReadOnlyList<string> roles = profile.Kind is SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment
            ? mapping.MappedRoles
            : Array.AsReadOnly(mapping.MappedRoles.Where(role => identity.ProviderRoles.Contains(role, StringComparer.Ordinal)).ToArray());
        return new(profile, caller, authentication.Principal, identity.ProviderRoles, roles);
    }

    internal static bool HasSingleAuthenticatedIdentity(ClaimsPrincipal principal)
    {
        using IEnumerator<ClaimsIdentity> identities = principal.Identities.GetEnumerator();
        return identities.MoveNext() && identities.Current.IsAuthenticated && !identities.MoveNext();
    }
}
