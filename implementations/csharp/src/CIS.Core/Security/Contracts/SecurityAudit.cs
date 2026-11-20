namespace CIS.Security;

/// <summary>The business outcome, separate from authorization success.</summary>
public enum SecurityOperationOutcome
{
    /// <summary>The protected operation completed successfully.</summary>
    Completed,
    /// <summary>The protected operation failed after permission was granted.</summary>
    Failed,
    /// <summary>The protected operation was canceled under its existing owner.</summary>
    Canceled
}

/// <summary>One final composite authorization decision with bounded trusted facts.</summary>
public sealed class SecurityAuthorizationAudit
{
    /// <summary>Copies a completed authorization observation; rejected authentication has no invented caller.</summary>
    public SecurityAuthorizationAudit(CallerContext? caller, IReadOnlyList<string> capabilities,
        CapabilityTarget? target, AuthorizationDecision decision, string? correlationId = null)
    {
        Caller = caller;
        Capabilities = CopyCapabilities(capabilities);
        Target = target;
        ArgumentNullException.ThrowIfNull(decision);
        Decision = decision;
        CorrelationId = correlationId is null ? null : SecurityText.Bounded(correlationId, 128, nameof(correlationId));
    }

    /// <summary>Gets the trusted caller, or null when none was established.</summary>
    public CallerContext? Caller
    {
        get;
    }
    /// <summary>Gets the complete requested capability set.</summary>
    public IReadOnlyList<string> Capabilities
    {
        get;
    }
    /// <summary>Gets the actual resolved target, or null when authentication prevented resolution.</summary>
    public CapabilityTarget? Target
    {
        get;
    }
    /// <summary>Gets the final composite decision.</summary>
    public AuthorizationDecision Decision
    {
        get;
    }
    /// <summary>Gets correlation metadata; it does not establish identity.</summary>
    public string? CorrelationId
    {
        get;
    }

    internal static IReadOnlyList<string> CopyCapabilities(IReadOnlyList<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (capabilities.Count is < 1 or > 256)
        {
            throw new ArgumentException("Supply between one and 256 capabilities.", nameof(capabilities));
        }
        string[] copied = new string[capabilities.Count];
        for (int index = 0; index < copied.Length; index++)
        {
            copied[index] = SecurityText.Capability(capabilities[index], nameof(capabilities));
        }
        return Array.AsReadOnly(copied);
    }
}

/// <summary>The separately reported outcome of protected work.</summary>
public sealed class SecurityOperationAudit
{
    /// <summary>Creates an operation record; the operation owner supplies its actual result.</summary>
    public SecurityOperationAudit(CallerContext caller, IReadOnlyList<string> capabilities,
        CapabilityTarget target, SecurityOperationOutcome outcome, string? outcomeCode = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        Caller = caller;
        Capabilities = SecurityAuthorizationAudit.CopyCapabilities(capabilities);
        Target = target;
        Outcome = outcome;
        OutcomeCode = outcomeCode is null ? null : SecurityText.Bounded(outcomeCode, 128, nameof(outcomeCode));
        CorrelationId = correlationId is null ? null : SecurityText.Bounded(correlationId, 128, nameof(correlationId));
    }

    /// <summary>Gets the established initiating caller; retaining this record does not delegate its authority.</summary>
    public CallerContext Caller
    {
        get;
    }
    /// <summary>Gets the actual requested capabilities.</summary>
    public IReadOnlyList<string> Capabilities
    {
        get;
    }
    /// <summary>Gets the actual target of the completed work.</summary>
    public CapabilityTarget Target
    {
        get;
    }
    /// <summary>Gets the separate business outcome.</summary>
    public SecurityOperationOutcome Outcome
    {
        get;
    }
    /// <summary>Gets an explicitly safe business outcome code.</summary>
    public string? OutcomeCode
    {
        get;
    }
    /// <summary>Gets correlation metadata, without authority.</summary>
    public string? CorrelationId
    {
        get;
    }
}

/// <summary>An awaited integration hook, without a queue or durable-delivery guarantee.</summary>
public interface ISecurityAuditSink
{
    /// <summary>Observes the final native authorization result; hook faults remain observable to the caller.</summary>
    ValueTask AuthorizationCompletedAsync(SecurityAuthorizationAudit audit, CancellationToken cancellationToken = default);
    /// <summary>Observes the separate business outcome; returning successfully is not a durable receipt.</summary>
    ValueTask OperationCompletedAsync(SecurityOperationAudit audit, CancellationToken cancellationToken = default);
}
