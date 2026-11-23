using CIS.Security;

namespace CIS.Audit;

/// <summary>One trusted Audit ingress registration, selected from configuration rather than message data.</summary>
public sealed class AuditSecurityIngressBinding
{
    /// <summary>Copies the exact dedicated resource and the complete initial and optional retry capability sets.</summary>
    public AuditSecurityIngressBinding(CapabilityTarget target, IReadOnlyList<string> ingressCapabilities,
        IReadOnlyList<string>? retryCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ResourceType is null || target.ResourceIdentifier is null)
        {
            throw new ArgumentException("Audit ingress requires a dedicated complete resource target.", nameof(target));
        }

        Target = target;
        IngressCapabilities = CopyCapabilities(ingressCapabilities, nameof(ingressCapabilities));
        RetryCapabilities = retryCapabilities is null ? null : CopyCapabilities(retryCapabilities, nameof(retryCapabilities));
    }

    /// <summary>Gets the fixed server-owned resource resolved by this ingress registration.</summary>
    public CapabilityTarget Target
    {
        get;
    }

    /// <summary>Gets the complete initial-delivery capability set.</summary>
    public IReadOnlyList<string> IngressCapabilities
    {
        get;
    }

    /// <summary>Gets the complete retry capability set when the subscription supports retry.</summary>
    public IReadOnlyList<string>? RetryCapabilities
    {
        get;
    }

    internal bool Matches(CapabilityTarget? target, IReadOnlyList<string> capabilities)
        => Target == target && (SameCapabilities(IngressCapabilities, capabilities)
            || RetryCapabilities is { } retry && SameCapabilities(retry, capabilities));

    private static IReadOnlyList<string> CopyCapabilities(IReadOnlyList<string> capabilities, string parameter)
    {
        ArgumentNullException.ThrowIfNull(capabilities, parameter);
        if (capabilities.Count is < 1 or > 8)
        {
            throw new ArgumentException("An ingress binding requires one through eight distinct capabilities.", parameter);
        }

        string[] copied = capabilities.Select(capability => SecurityText.Capability(capability, parameter)).ToArray();
        if (copied.Distinct(StringComparer.Ordinal).Count() != copied.Length)
        {
            throw new ArgumentException("An ingress binding requires distinct capabilities.", parameter);
        }
        return Array.AsReadOnly(copied);
    }

    private static bool SameCapabilities(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
        => expected.Count == actual.Count && actual.Distinct(StringComparer.Ordinal).Count() == actual.Count
            && expected.All(capability => actual.Contains(capability, StringComparer.Ordinal));
}

/// <summary>Immutable finite policy for the explicitly selected Security-to-Audit bridge.</summary>
public sealed class AuditSecurityBridgeOptions
{
    /// <summary>Copies up to sixteen trusted ingress bindings and a positive deadline of at most one minute.</summary>
    public AuditSecurityBridgeOptions(TimeSpan? submissionTimeout = null,
        IReadOnlyList<AuditSecurityIngressBinding>? ingressBindings = null)
    {
        TimeSpan timeout = submissionTimeout ?? TimeSpan.FromSeconds(5);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(submissionTimeout));
        }
        if (ingressBindings?.Count > 16)
        {
            throw new ArgumentException("Select at most sixteen dedicated Audit ingress bindings.", nameof(ingressBindings));
        }

        AuditSecurityIngressBinding[] copied = ingressBindings?.ToArray() ?? [];
        if (copied.Any(binding => binding is null))
        {
            throw new ArgumentException("Audit ingress bindings cannot contain null.", nameof(ingressBindings));
        }

        SubmissionTimeout = timeout;
        IngressBindings = Array.AsReadOnly(copied);
    }

    /// <summary>Gets the cooperative submission deadline, including trusted capture.</summary>
    public TimeSpan SubmissionTimeout
    {
        get;
    }

    /// <summary>Gets trusted fixed ingress metadata; it cannot be supplied by an incoming envelope.</summary>
    public IReadOnlyList<AuditSecurityIngressBinding> IngressBindings
    {
        get;
    }
}
