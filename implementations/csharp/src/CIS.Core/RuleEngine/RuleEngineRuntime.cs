using CIS.Configuration;
using CIS.Management;
using CIS.Management.Wire;

namespace CIS.RuleEngine;

internal sealed record RuleEngineSelection(PreparedRuleDefinition? Prepared, ConfigurationSnapshotTarget? Target = null,
    RuleEvaluationError? Error = null);

// Native Configuration owns adoption and its synchronization. The runtime holds
// only a bounded set of immutable versions already witnessed by that authority.
internal sealed class RuleEngineRuntime(ConfigurationRuntime configuration, IManagedWorkAdmission admission)
{
    private const int MaximumRetainedSelections = 16;
    private readonly object cacheGate = new();
    private readonly Dictionary<VersionKey, PreparedRuleDefinition> retained = [];
    private readonly Queue<VersionKey> retentionOrder = [];

    internal async Task<RuleEngineSelection> SelectAsync(RuleEvaluationRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (token.IsCancellationRequested)
        {
            return Failed("Cancelled", "The rule evaluation was cancelled.");
        }

        ConfigurationEntry? entry = configuration.FindRuleEngineBinding(request.RulesetId);
        if (entry is null)
        {
            return Failed("RulesetUnavailable", "The application has no enrolled binding for this ruleset.");
        }
        if (Capture(entry) is not { } local || !Admits(local.Target))
        {
            return Failed("AdmissionUnavailable", "The current host cannot admit this ruleset.");
        }

        ConfigurationEnrollment enrollment = entry.Enrollment!;
        Remember(Key(local.Target, enrollment, local.Prepared), local.Prepared);
        if (request.ExactVersion is null || request.ExactVersion == local.Prepared.Definition.Version)
        {
            return new(local.Prepared, local.Target);
        }
        if (!ManagementWire.IsUuid(request.ExactVersion))
        {
            return Failed("VersionUnavailable", "The exact deployed version is unavailable.");
        }

        var requested = new VersionKey(local.Target, enrollment.ConfigurationId, enrollment.SchemaVersion,
            request.RulesetId, request.ExactVersion);
        PreparedRuleDefinition? prepared;
        lock (cacheGate)
        {
            if (retained.TryGetValue(requested, out prepared))
            {
                return new(prepared, local.Target);
            }
        }

        ManagementConfigurationStore? store = configuration.RuleEngineStore;
        if (store is null)
        {
            return Failed("VersionUnavailable", "The exact deployed version is unavailable.");
        }
        ManagementRuleWorkloadVersion? response;
        try
        {
            response = await store.ReadRuleVersionAsync(local.Target, enrollment, request.RulesetId, request.ExactVersion, token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Failed("Cancelled", "The rule evaluation was cancelled.");
        }
        catch (ManagementRuleWorkloadVersionReadException error)
        {
            return Failed("VersionUnavailable", error.InvalidResponse
                ? "The authority did not return a valid exact deployed version."
                : "The authority could not confirm the exact deployed version.");
        }
        if (token.IsCancellationRequested)
        {
            return Failed("Cancelled", "The rule evaluation was cancelled.");
        }
        if (response is null)
        {
            return Failed("VersionUnavailable", "The exact deployed version is unavailable.");
        }

        try
        {
            prepared = ((RuleEngineConfiguration)entry.Instance).Prepare(response.DecodeDefinition());
        }
        catch (ArgumentException)
        {
            return Failed("VersionUnsupported", "The application does not support the exact deployed version.");
        }
        if (Capture(entry) is not { } current || current.Target != local.Target || current.SessionEpoch != local.SessionEpoch
            || !Admits(local.Target))
        {
            return Failed("AdmissionUnavailable", "The current host cannot admit this ruleset.");
        }

        Remember(requested, prepared);
        return new(prepared, local.Target);
    }

    private bool Admits(ConfigurationSnapshotTarget target)
    {
        ManagedWorkAdmissionSnapshot current = admission.CaptureSnapshot();
        return current.AdmissionAllowed && current.AuthorityId == target.AuthorityId && current.RegistrationId == target.RegistrationId;
    }

    private static LocalSelection? Capture(ConfigurationEntry entry)
    {
        lock (entry.ValueGate)
        {
            return !entry.Disabled && entry.SnapshotAdmission?.IsClosed == false
                && entry.SnapshotTarget is { } target && entry.Acceptance is { HasIntactAcceptedState: true } applied
                && applied.LastAccepted?.Desired.Target == target && entry.AcceptedRuleDefinition is { } prepared
                    ? new(prepared, target, entry.SessionEpoch) : null;
        }
    }

    private static RuleEngineSelection Failed(string code, string message) => new(null, Error: new(code, message));

    private static VersionKey Key(ConfigurationSnapshotTarget target, ConfigurationEnrollment enrollment, PreparedRuleDefinition prepared)
        => new(target, enrollment.ConfigurationId, enrollment.SchemaVersion, prepared.Definition.RulesetId,
            prepared.Definition.Version);

    private void Remember(VersionKey key, PreparedRuleDefinition version)
    {
        lock (cacheGate)
        {
            if (!retained.TryAdd(key, version))
            {
                return;
            }
            retentionOrder.Enqueue(key);
            if (retentionOrder.Count > MaximumRetainedSelections)
            {
                retained.Remove(retentionOrder.Dequeue());
            }
        }
    }

    private sealed record LocalSelection(PreparedRuleDefinition Prepared, ConfigurationSnapshotTarget Target, long SessionEpoch);
    private sealed record VersionKey(ConfigurationSnapshotTarget Target, string ConfigurationId, string SchemaVersion, string RulesetId, string Version);
}
