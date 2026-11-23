using CIS.Security;

namespace CIS.Coordination;

/// <summary>A bounded consumer request. Scope text is untrusted until resolved and authorized.</summary>
public sealed class CoordinationRequest
{
    /// <summary>Copies requested identity without supplying environment, owner, grant or permission claims.</summary>
    public CoordinationRequest(string executionScope, string dutyId, string resourceId = CoordinationKey.DefaultResourceId)
    {
        ExecutionScope = CoordinationText.Utf8(executionScope, CoordinationKey.MaximumExecutionScopeBytes, nameof(executionScope));
        DutyId = CoordinationText.Utf8(dutyId, CoordinationKey.MaximumDutyIdBytes, nameof(dutyId));
        ResourceId = CoordinationText.Utf8(resourceId, CoordinationKey.MaximumResourceIdBytes, nameof(resourceId));
    }

    /// <summary>Gets the requested scope, subject to trusted application resolution.</summary>
    public string ExecutionScope
    {
        get;
    }
    /// <summary>Gets the requested stable duty.</summary>
    public string DutyId
    {
        get;
    }
    /// <summary>Gets the requested resource or explicit default.</summary>
    public string ResourceId
    {
        get;
    }
}

/// <summary>An application-resolved duty and actual capability target. Construction alone grants no permission.</summary>
public sealed class CoordinationTarget
{
    /// <summary>Copies a trusted resource mapping and one to eight capabilities evaluated together.</summary>
    public CoordinationTarget(CoordinationKey key, CapabilityTarget resource, IReadOnlyList<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!string.Equals(key.Environment, resource.Environment, StringComparison.Ordinal)
            || capabilities.Count is < 1 or > 8)
        {
            throw new ArgumentException("Supply the same environment and one to eight complete-target capabilities.");
        }
        Key = key;
        Resource = resource;
        string[] copied = new string[capabilities.Count];
        for (int index = 0; index < copied.Length; index++)
        {
            copied[index] = SecurityText.Capability(capabilities[index], nameof(capabilities));
        }
        Capabilities = Array.AsReadOnly(copied);
    }

    /// <summary>Gets the trusted full key.</summary>
    public CoordinationKey Key
    {
        get;
    }
    /// <summary>Gets the actual resource evaluated by the existing scoped authorizer.</summary>
    public CapabilityTarget Resource
    {
        get;
    }
    /// <summary>Gets the complete required capability set, not a caller advertisement.</summary>
    public IReadOnlyList<string> Capabilities
    {
        get;
    }

    /// <summary>Builds an exact bounded key fingerprint target for an explicitly registered duty.</summary>
    public static CoordinationTarget ForDuty(CoordinationKey key, string targetApplication,
        IReadOnlyList<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new(key, new CapabilityTarget(targetApplication, key.Environment, "coordination-duty",
            key.GetResourceIdentifier()), capabilities);
    }
}

/// <summary>Trusted application mapping from requested text to an actual permitted scope/resource.</summary>
/// <remarks>Return null for unknown or ineligible duties. Never accept serialized CallerContext as authentication.</remarks>
public interface ICoordinationTargetResolver
{
    /// <summary>Resolves outside runtime locks; the service still requires the current scoped capability authorization.</summary>
    ValueTask<CoordinationTarget?> ResolveAsync(CoordinationRequest request, LocalApplicationIdentity application,
        CancellationToken cancellationToken = default);
}

/// <summary>The scoped consumer facade over one independently renewing bounded engine.</summary>
public interface ICoordinationService
{
    /// <summary>Resolves and authorizes the actual scope before attempting one fresh managed acquisition.</summary>
    Task<ManagedCoordinationResult> AcquireAsync(CoordinationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A managed acquisition outcome; only confirmed usable ownership supplies a lease.</summary>
public sealed class ManagedCoordinationResult
{
    /// <summary>Enforces the ownership/result boundary without inventing business execution.</summary>
    public ManagedCoordinationResult(CoordinationAcquireOutcome outcome, ICoordinationLease? lease = null,
        string? reasonCode = null, CoordinationOwnerObservation? observedOwner = null)
    {
        if (!Enum.IsDefined(outcome) || (outcome == CoordinationAcquireOutcome.Acquired) != (lease is not null)
            || (observedOwner is not null && outcome != CoordinationAcquireOutcome.Busy))
        {
            throw new ArgumentException("Only a confirmed acquired result carries a managed lease.");
        }
        Outcome = outcome;
        Lease = lease;
        ReasonCode = CoordinationText.Code(reasonCode);
        ObservedOwner = observedOwner;
    }

    /// <summary>Gets confirmed ownership, normal contention, refusal or uncertainty.</summary>
    public CoordinationAcquireOutcome Outcome
    {
        get;
    }
    /// <summary>Gets the managed handle only for Acquired.</summary>
    public ICoordinationLease? Lease
    {
        get;
    }
    /// <summary>Gets an optional bounded stable code.</summary>
    public string? ReasonCode
    {
        get;
    }
    /// <summary>Gets the last authority Busy observation without performing snapshot-time I/O.</summary>
    public CoordinationOwnerObservation? ObservedOwner
    {
        get;
    }
}
