using CIS.Messaging;
using CIS.Operations;
using CIS.Management.Wire;

namespace CIS.Management;

/// <summary>First-release hard ceilings. Stricter enclosing limits still apply.</summary>
public static class ManagementSchedulerLimits
{
    /// <summary>The maximum triggers in the selected finite first-release profile.</summary>
    public const int MaximumTriggers = 1024;
    /// <summary>The maximum tasks in the selected finite first-release profile.</summary>
    public const int MaximumTasks = 4096;
    /// <summary>The maximum unsettled runs in the selected finite first-release profile.</summary>
    public const int MaximumUnsettledRuns = 10000;
    /// <summary>The maximum fanout in the selected finite first-release profile.</summary>
    public const int MaximumFanout = 64;
    /// <summary>The maximum mappings in the selected finite first-release profile.</summary>
    public const int MaximumMappings = 64;
    /// <summary>The maximum payload bytes in the selected finite first-release profile.</summary>
    public const int MaximumPayloadBytes = 64 * 1024;
    /// <summary>The maximum capsule bytes in the selected finite first-release profile.</summary>
    public const int MaximumCapsuleBytes = 256 * 1024;
    /// <summary>The maximum admission bytes in the selected finite first-release profile.</summary>
    public const int MaximumAdmissionBytes = 4 * 1024 * 1024;
    /// <summary>The maximum http bytes in the selected finite first-release profile.</summary>
    public const int MaximumHttpBytes = 1024 * 1024;
    /// <summary>The maximum json depth in the selected finite first-release profile.</summary>
    public const int MaximumJsonDepth = 16;
    /// <summary>The maximum summary bytes in the selected finite first-release profile.</summary>
    public const int MaximumSummaryBytes = 16 * 1024;
    /// <summary>The maximum attempts in the selected finite first-release profile.</summary>
    public const int MaximumAttempts = 16;
    /// <summary>The default page size in the selected finite first-release profile.</summary>
    public const int DefaultPageSize = 100;
    /// <summary>The maximum page size in the selected finite first-release profile.</summary>
    public const int MaximumPageSize = 500;
    /// <summary>The reconciliation batch size in the selected finite first-release profile.</summary>
    public const int ReconciliationBatchSize = 100;
    /// <summary>The default catch up count in the selected finite first-release profile.</summary>
    public const int DefaultCatchUpCount = 100;
    /// <summary>The default catch up window ms in the selected finite first-release profile.</summary>
    public const long DefaultCatchUpWindowMs = 7 * 24 * 60 * 60 * 1000L;
    /// <summary>The minimum progress interval ms in the selected finite first-release profile.</summary>
    public const long MinimumProgressIntervalMs = 5000;
    /// <summary>The detailed history days in the selected finite first-release profile.</summary>
    public const int DetailedHistoryDays = 30;
    /// <summary>The aggregate history days in the selected finite first-release profile.</summary>
    public const int AggregateHistoryDays = 180;
}

/// <summary>Observed immutable code identity, distinct from reported release and native authorization.</summary>
public sealed record ManagementWorkerBuildReference
{
    /// <summary>Retains detached validated facts without selecting runtime work or establishing native authority.</summary>
    public ManagementWorkerBuildReference(string applicationVersion, string buildId, string handlerId, string? sourceRevision = null)
    {
        ApplicationVersion = SchedulerValue.Text(applicationVersion, 512, nameof(applicationVersion));
        BuildId = SchedulerValue.Text(buildId, 512, nameof(buildId));
        HandlerId = SchedulerValue.Text(handlerId, 512, nameof(handlerId));
        SourceRevision = sourceRevision is null ? null : SchedulerValue.Text(sourceRevision, 128, nameof(sourceRevision));
    }

    /// <summary>Gets the detached application version data.</summary>
    public string ApplicationVersion { get; }
    /// <summary>Gets the detached build id data.</summary>
    public string BuildId { get; }
    /// <summary>Gets the detached handler id data.</summary>
    public string HandlerId { get; }
    /// <summary>Gets the detached source revision data.</summary>
    public string? SourceRevision { get; }
}

/// <summary>Explicit semantic start/recovery support. Version ordering never implies compatibility.</summary>
public sealed record ManagementJobExecutionSupport
{
    /// <summary>Retains detached validated facts without selecting runtime work or establishing native authority.</summary>
    public ManagementJobExecutionSupport(MessageContract currentContract, IReadOnlyList<MessageContract> supportedStarts,
        IReadOnlyList<MessageContract> supportedRecoveries, MessageContract? checkpointContract = null, string? operationName = null)
    {
        ArgumentNullException.ThrowIfNull(currentContract);
        CurrentContract = currentContract;
        SupportedStarts = SchedulerValue.Copy(supportedStarts, 16, nameof(supportedStarts));
        SupportedRecoveries = SchedulerValue.Copy(supportedRecoveries, 16, nameof(supportedRecoveries));
        if (!SupportedStarts.Contains(currentContract) || SupportedStarts.Distinct().Count() != SupportedStarts.Count
            || SupportedRecoveries.Distinct().Count() != SupportedRecoveries.Count)
        {
            throw new ArgumentException("Declare distinct exact contracts, including the current start contract.");
        }

        CheckpointContract = checkpointContract;
        OperationName = operationName is null ? null : new OperationLookupRequest(operationName, "contract", "contract").OperationName;
    }

    /// <summary>Gets the detached current contract data.</summary>
    public MessageContract CurrentContract { get; }
    /// <summary>Gets the detached supported starts data.</summary>
    public IReadOnlyList<MessageContract> SupportedStarts { get; }
    /// <summary>Gets the detached supported recoveries data.</summary>
    public IReadOnlyList<MessageContract> SupportedRecoveries { get; }
    /// <summary>Gets the detached checkpoint contract data.</summary>
    public MessageContract? CheckpointContract { get; }
    /// <summary>Gets the detached operation name data.</summary>
    public string? OperationName { get; }
}

internal static class SchedulerValue
{
    internal static string Text(string value, int maximumBytes, string parameterName)
    {
        if (!ManagementWire.IsUtf8Text(value, maximumBytes))
        {
            throw new ArgumentException("Supply exact bounded well-formed Unicode text without controls.", parameterName);
        }

        return value;
    }

    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, int maximum, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count > maximum)
        {
            throw new ArgumentException("The complete collection exceeds its finite bound.", parameterName);
        }

        var copy = new List<T>();
        foreach (T value in values)
        {
            if (value is null || copy.Count == maximum)
            {
                throw new ArgumentException("Supply a bounded collection without null entries.", parameterName);
            }

            copy.Add(value);
        }

        return copy.AsReadOnly();
    }
}
