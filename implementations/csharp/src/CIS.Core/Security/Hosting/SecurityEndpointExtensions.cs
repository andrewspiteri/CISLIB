using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CIS.Security.Hosting;

/// <summary>Explicit endpoint requirements, consumed by the same native complete-grant handler as imperative calls.</summary>
public static class SecurityEndpointExtensions
{
    /// <summary>Requires all capabilities on one authoritative fixed target.</summary>
    public static IEndpointConventionBuilder RequireCisCapabilities(this IEndpointConventionBuilder builder,
        string profile, CapabilityTarget target, params string[] capabilities)
    {
        ArgumentNullException.ThrowIfNull(target);
        return builder.RequireCisCapabilities(profile, (_, _) => ValueTask.FromResult(target), capabilities);
    }

    /// <summary>Resolves the authoritative target before work; route input alone cannot replace actual resource authorization.</summary>
    public static IEndpointConventionBuilder RequireCisCapabilities(this IEndpointConventionBuilder builder,
        string profile, Func<HttpContext, CancellationToken, ValueTask<CapabilityTarget>> resolveTarget,
        params string[] capabilities)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resolveTarget);
        string alias = SecurityText.Alias(profile, nameof(profile));
        var metadata = new SecurityCapabilityMetadata(alias,
            SecurityAuthorizationAudit.CopyCapabilities(capabilities), resolveTarget);
        builder.Add(endpoint =>
        {
            if (endpoint.Metadata.OfType<SecurityCapabilityMetadata>().Any())
            {
                throw new InvalidOperationException("Declare one complete capability set and authoritative target per endpoint.");
            }
            endpoint.Metadata.Add(metadata);
        });
        builder.RequireAuthorization(SecurityPolicySnapshot.PolicyName(alias));
        return builder;
    }
}

internal sealed class SecurityCapabilityMetadata(string profile, IReadOnlyList<string> capabilities,
    Func<HttpContext, CancellationToken, ValueTask<CapabilityTarget>> resolveTarget)
{
    internal string Profile { get; } = profile;
    internal IReadOnlyList<string> Capabilities { get; } = capabilities;
    internal ValueTask<CapabilityTarget> ResolveTargetAsync(HttpContext context, CancellationToken cancellationToken)
        => resolveTarget(context, cancellationToken);
}
