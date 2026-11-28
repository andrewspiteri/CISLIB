using System.Text.Json.Serialization;

namespace CIS.Management;

/// <summary>One operational rule counter and its last recorded reset, if available.</summary>
public sealed record ManagementRuleCounterItem(string RuleId, string? Count = null, string? LastResetAtUtc = null);

/// <summary>A scoped, bounded observation of rule counters; missing counts do not mean zero.</summary>
public sealed record ManagementRuleCounterPage(string ApplicationId, string Environment, string RulesetId, string Availability,
    IReadOnlyList<ManagementRuleCounterItem> Items, string? ObservedAtUtc = null, string? NextCursor = null);

/// <summary>A closed reset request for one rule, a selected set, or all retained rule identities.</summary>
public sealed record ManagementRuleCounterResetCommand(string Selection, IReadOnlyList<string> RuleIds, string Reason);

/// <summary>The original reset observation and exact counts of distinct applied and rejected outcomes.</summary>
public sealed record ManagementRuleCounterResetDecision(string Selection, string ResetAtUtc, string AppliedCount, string RejectedCount);

/// <summary>One original per-rule reset outcome.</summary>
public sealed record ManagementRuleCounterResetOutcome(string RuleId, string Disposition);

/// <summary>One bounded page of outcomes from the original, independently authorized reset.</summary>
public sealed record ManagementRuleCounterResetOutcomesPage(string ApplicationId, string Environment, string RulesetId,
    ManagementOperationReference Operation, IReadOnlyList<ManagementRuleCounterResetOutcome> Items, string? NextCursor = null);

/// <summary>The original counter-reset decision and separate current operation-attempt observations.</summary>
public sealed record ManagementRuleCounterResetOperationResult(string ApplicationId, string Environment, string RulesetId,
    ManagementOperationReference Operation, string ResultSource, string OriginalCompletion,
    ManagementRuleCounterResetDecision? Result = null, ManagementTransactionFacts? AttemptCompletion = null,
    ManagementOperationRejection? Rejection = null)
{
    /// <summary>Gets the recognized source of the original result.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownResultSource => new(ResultSource, ResultSource is "prepared" or "replayed" or "found");

    /// <summary>Gets original completion independently from the current attempt's cleanup.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownOriginalCompletion => new(OriginalCompletion,
        OriginalCompletion is "committed" or "notCommitted" or "unknown");
}

/// <summary>Optional scoped counter reads, audited resets and original result recovery.</summary>
public interface IManagementRuleEngineCounterClient
{
    /// <summary>Reads current or retained counter observations under one authorized ruleset.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleCounterPage>> ListCountersAsync(string applicationId, string rulesetId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Requests an audited counter reset under the caller's original operation key.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleCounterResetOperationResult>> ResetCountersAsync(string applicationId, string rulesetId,
        string operationKey, ManagementRuleCounterResetCommand command, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Recovers the original counter reset under the current actor and ruleset.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleCounterResetOperationResult>> GetCounterResetOperationAsync(string applicationId,
        string rulesetId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Pages the individual outcomes of the exact original reset.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleCounterResetOutcomesPage>> ListCounterResetOutcomesAsync(string applicationId,
        string rulesetId, string operationKey, ManagementPageRequest? page = null, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default);
}
