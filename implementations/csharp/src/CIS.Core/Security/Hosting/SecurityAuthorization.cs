using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CIS.Security.Hosting;

internal sealed class CapabilityRequirement : IAuthorizationRequirement
{
    internal static CapabilityRequirement Instance { get; } = new();
}

internal sealed class SecurityAuthorizationOptions(SecurityPolicySnapshot snapshot, SecurityComposition composition) : IPostConfigureOptions<AuthorizationOptions>
{
    public void PostConfigure(string? name, AuthorizationOptions options)
    {
        foreach (SecurityProfile profile in snapshot.Profiles)
        {
            if ((profile.Roles & SecurityProfileRoles.Inbound) == 0)
            {
                continue;
            }
            string policyName = SecurityPolicySnapshot.PolicyName(profile.Alias);
            if (options.GetPolicy(policyName) is not null)
            {
                throw new InvalidOperationException("CIS security policy names are reserved for their selected profiles.");
            }
            var policy = new AuthorizationPolicyBuilder(profile.AuthenticationScheme!);
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(CapabilityRequirement.Instance);
            if (profile.AdditionalAuthorizationPolicy is { } additional)
            {
                AuthorizationPolicy extra = options.GetPolicy(additional)
                    ?? throw new InvalidOperationException("A selected additional native authorization policy is not registered.");
                if (extra.AuthenticationSchemes.Any(scheme => !string.Equals(scheme, profile.AuthenticationScheme, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("Additional native policies cannot combine unrelated authentication schemes.");
                }
                policy.Combine(extra);
            }
            AuthorizationPolicy generated = policy.Build();
            composition.CaptureNativeRequirements(profile.Alias, generated);
            options.AddPolicy(policyName, generated);
        }
    }
}

internal sealed class SecurityAuthorizationOperation(string profile, IReadOnlyList<string> capabilities,
    CapabilityTarget? target, SecurityCallerState.Authority? authority, CancellationToken cancellationToken)
{
    internal string Profile { get; } = profile;
    internal IReadOnlyList<string> Capabilities { get; } = capabilities;
    internal CapabilityTarget? Target { get; set; } = target;
    internal SecurityCallerState.Authority? Authority { get; } = authority;
    internal CancellationToken CancellationToken { get; } = cancellationToken;
    internal SecurityFailure? Failure
    {
        get; set;
    }
    internal AuthorizationHandlerContext? Evaluation
    {
        get; set;
    }
    internal bool CompleteGrantAllowed
    {
        get; set;
    }
}

// Only this handler evaluates complete grants. Endpoint/imperative callers differ only in native resource adaptation.
internal sealed class CapabilityRequirementHandler(SecurityPolicySnapshot policy, SecurityCallerState state,
    TimeProvider? timeProvider = null) : AuthorizationHandler<CapabilityRequirement>
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CapabilityRequirement requirement)
    {
        SecurityAuthorizationOperation? operation = context.Resource as SecurityAuthorizationOperation;
        HttpContext? http = context.Resource as HttpContext;
        if (http is not null)
        {
            SecurityCapabilityMetadata? metadata = http.GetEndpoint()?.Metadata.GetMetadata<SecurityCapabilityMetadata>();
            if (metadata is null)
            {
                context.Fail();
                return;
            }
            SecurityAuthorizationOperation? existing = http.Features.Get<SecurityAuthorizationOperation>();
            if (existing is not null && ReferenceEquals(existing.Evaluation, context))
            {
                return;
            }
            operation = new(metadata.Profile, metadata.Capabilities, null, state.CurrentAuthority, http.RequestAborted);
            http.Features.Set(operation);
        }
        if (operation is null)
        {
            context.Fail();
            return;
        }
        if (ReferenceEquals(operation.Evaluation, context))
        {
            return;
        }
        // An earlier native handler can satisfy pending requirements, but cannot
        // substitute for this evaluator or create an implicit administrator.
        operation.Evaluation = context;
        operation.CancellationToken.ThrowIfCancellationRequested();
        SecurityCallerState.Authority? authority = operation.Authority;
        if (authority?.Caller is null || authority.Profile is null
            || !string.Equals(authority.Profile.Alias, operation.Profile, StringComparison.Ordinal)
            || !SecurityProjection.HasSingleAuthenticatedIdentity(context.User))
        {
            operation.Failure = authority?.Failure ?? SecurityFailures.Unauthenticated();
            context.Fail();
            return;
        }
        if (authority.Profile.Kind is SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment
            && authority.Caller.Authentication.ExpiresAtUtc is null)
        {
            operation.Failure = new(SecurityFailureKind.ValidationUnavailable,
                "The certificate integration did not establish leaf expiry.");
            context.Fail();
            return;
        }
        if (authority.Profile.Kind is SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment
            && authority.Caller.Authentication.ExpiresAtUtc is { } expires && expires <= clock.GetUtcNow())
        {
            operation.Failure = SecurityFailures.Expired();
            context.Fail();
            return;
        }
        if (http is not null)
        {
            SecurityCapabilityMetadata metadata = http.GetEndpoint()!.Metadata.GetMetadata<SecurityCapabilityMetadata>()!;
            operation.Target = await metadata.ResolveTargetAsync(http, operation.CancellationToken).ConfigureAwait(false);
            operation.CancellationToken.ThrowIfCancellationRequested();
        }
        if (operation.Target is null)
        {
            throw new InvalidOperationException("An actual capability target is required before protected execution.");
        }
        foreach (string capability in operation.Capabilities)
        {
            operation.CancellationToken.ThrowIfCancellationRequested();
            if (!policy.IsRegisteredCapability(capability)
                || !policy.Grants.Any(grant => Matches(grant, capability, operation.Target, authority)))
            {
                operation.Failure = SecurityFailures.Denied();
                context.Fail();
                return;
            }
        }
        operation.CompleteGrantAllowed = true;
        context.Succeed(requirement);
    }

    private static bool Matches(CapabilityGrant grant, string capability, CapabilityTarget target,
        SecurityCallerState.Authority authority)
    {
        if (!string.Equals(grant.Profile, authority.Caller!.Profile, StringComparison.Ordinal)
            || !string.Equals(grant.Capability, capability, StringComparison.Ordinal)
            || !string.Equals(grant.TargetApplication, target.Application, StringComparison.Ordinal)
            || !string.Equals(grant.TargetEnvironment, target.Environment, StringComparison.Ordinal)
            || grant.Subject is { } subject && subject != authority.Caller.Subject
            || grant.MappedRole is { } role && !authority.MappedRoles.Contains(role, StringComparer.Ordinal)
            || grant.RequiredProviderRole is { } providerRole && !authority.ProviderRoles.Contains(providerRole, StringComparer.Ordinal))
        {
            return false;
        }
        return grant.ResourceSelection switch
        {
            CapabilityResourceSelection.TargetOnly => target.ResourceType is null && target.ResourceIdentifier is null,
            CapabilityResourceSelection.ExactResource => string.Equals(grant.ResourceType, target.ResourceType, StringComparison.Ordinal)
                && string.Equals(grant.ResourceIdentifier, target.ResourceIdentifier, StringComparison.Ordinal),
            CapabilityResourceSelection.AllOfType => target.ResourceIdentifier is not null
                && string.Equals(grant.ResourceType, target.ResourceType, StringComparison.Ordinal),
            _ => false
        };
    }
}

internal sealed class NativeCapabilityAuthorizer(IAuthorizationService authorization,
    IAuthorizationPolicyProvider policies, SecurityCallerState state, SecurityComposition composition,
    ISecurityAuditSink audit) : ICapabilityAuthorizer
{
    public async ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities,
        CapabilityTarget target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(target);
        IReadOnlyList<string> copied = SecurityAuthorizationAudit.CopyCapabilities(capabilities);
        SecurityCallerState.Authority? authority = state.CurrentAuthority;
        string profile = authority?.Profile?.Alias ?? "unresolved";
        var operation = new SecurityAuthorizationOperation(profile, copied, target, authority, cancellationToken);
        AuthorizationPolicy? policy = authority?.Profile is { } selected
            ? await policies.GetPolicyAsync(SecurityPolicySnapshot.PolicyName(selected.Alias)).ConfigureAwait(false)
            : null;
        cancellationToken.ThrowIfCancellationRequested();
        policy ??= new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(CapabilityRequirement.Instance).Build();
        AuthorizationResult result = await authorization.AuthorizeAsync(
            authority?.Principal ?? new ClaimsPrincipal(new ClaimsIdentity()), operation, policy).ConfigureAwait(false);
        bool isolated = authority?.Profile is not { } currentProfile
            || policy.AuthenticationSchemes.Distinct(StringComparer.Ordinal).SequenceEqual([currentProfile.AuthenticationScheme!], StringComparer.Ordinal);
        if (!isolated)
        {
            operation.Failure = SecurityFailures.Unauthenticated();
        }
        bool complete = operation.CompleteGrantAllowed && operation.Evaluation is not null && operation.Target is not null
            && composition.RetainsNativeRequirements(profile, policy);
        if (result.Succeeded && !complete)
        {
            operation.Failure = new(SecurityFailureKind.ValidationUnavailable,
                "The configured security authorization checks were not performed.");
        }
        AuthorizationDecision decision = result.Succeeded && isolated && complete ? AuthorizationDecision.Allow
            : AuthorizationDecision.Deny(operation.Failure ?? authority?.Failure ?? SecurityFailures.Denied());
        await SecurityAuthorizationObservation.RecordAsync(composition, audit, operation, decision, null, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return decision;
    }
}

internal static class SecurityAuthorizationObservation
{
    internal static async ValueTask RecordAsync(SecurityComposition composition, ISecurityAuditSink audit,
        SecurityAuthorizationOperation operation, AuthorizationDecision decision, string? correlationId,
        CancellationToken cancellationToken)
    {
        composition.Telemetry.RecordAuthorization(operation.Profile, decision.IsAllowed);
        cancellationToken.ThrowIfCancellationRequested();
        await audit.AuthorizationCompletedAsync(new(operation.Authority?.Caller, operation.Capabilities,
            operation.Target, decision, correlationId), cancellationToken).ConfigureAwait(false);
    }
}

// Preserve the native/application result handler and its original DI ownership.
// Observe only the final composite result, then delegate native challenge/forbid/next behavior.
internal sealed class SecurityAuthorizationMiddlewareResultHandler(SecurityComposition composition,
    SecurityPolicySnapshot snapshot) : IAuthorizationMiddlewareResultHandler
{
    internal static object NativeHandlerKey { get; } = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        SecurityCapabilityMetadata? metadata = context.GetEndpoint()?.Metadata.GetMetadata<SecurityCapabilityMetadata>();
        if (metadata is not null)
        {
            SecurityCallerState state = context.RequestServices.GetRequiredService<SecurityCallerState>();
            SecurityAuthorizationOperation operation = context.Features.Get<SecurityAuthorizationOperation>()
                ?? new(metadata.Profile, metadata.Capabilities, null, state.CurrentAuthority, context.RequestAborted);
            SecurityProfile selected = snapshot.GetProfile(metadata.Profile);
            string[] schemes = policy.AuthenticationSchemes.Distinct(StringComparer.Ordinal).ToArray();
            if (schemes.Length != 1 || !string.Equals(schemes[0], selected.AuthenticationScheme, StringComparison.Ordinal))
            {
                operation.Failure = SecurityFailures.Unauthenticated();
                authorizeResult = PolicyAuthorizationResult.Forbid();
                policy = new AuthorizationPolicy(policy.Requirements, [selected.AuthenticationScheme!]);
            }
            if (authorizeResult.Succeeded && (!operation.CompleteGrantAllowed || operation.Evaluation is null
                || operation.Target is null || !composition.RetainsNativeRequirements(metadata.Profile, policy)))
            {
                operation.Failure = new(SecurityFailureKind.ValidationUnavailable,
                    "The configured security authorization checks were not performed.");
                authorizeResult = PolicyAuthorizationResult.Forbid();
            }
            AuthorizationDecision decision = authorizeResult.Succeeded ? AuthorizationDecision.Allow
                : AuthorizationDecision.Deny(operation.Failure ?? operation.Authority?.Failure ?? SecurityFailures.Denied());
            string? correlation = System.Diagnostics.Activity.Current?.TraceId.ToString();
            await SecurityAuthorizationObservation.RecordAsync(composition,
                context.RequestServices.GetRequiredService<ISecurityAuditSink>(), operation, decision,
                correlation, context.RequestAborted).ConfigureAwait(false);
        }
        IAuthorizationMiddlewareResultHandler native = context.RequestServices.GetRequiredKeyedService<IAuthorizationMiddlewareResultHandler>(NativeHandlerKey);
        RequestDelegate protectedNext = metadata is not null && !authorizeResult.Succeeded
            ? _ => throw new InvalidOperationException("A denied capability operation cannot execute its protected body.")
            : next;
        await native.HandleAsync(protectedNext, context, policy, authorizeResult).ConfigureAwait(false);
    }
}
