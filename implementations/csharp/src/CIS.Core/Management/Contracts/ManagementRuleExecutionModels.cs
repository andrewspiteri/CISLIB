namespace CIS.Management;

/// <summary>A permission-scoped Rule Engine execution, with independently observed audit phases.</summary>
public sealed record ManagementRuleExecutionSummary(string ApplicationId, string Environment, string RulesetId,
    string ExecutionId, string Version, string SchemaRevision, string ProducerApplication, string InstanceId,
    string RegistrationId, string ApplicationBuild, string StartedAtUtc, string Status,
    bool StartReceived, bool CompletionReceived, string? FinishedAtUtc = null)
{
    /// <summary>Gets the number of returned rows observed in a paired completed execution, when retained.</summary>
    public int? ReturnedMatchCount
    {
        get; init;
    }
}

/// <summary>The original available audit events and immutable rule definition for one authorized execution.</summary>
public sealed record ManagementRuleExecutionDetail(ManagementRuleExecutionSummary Execution, string DefinitionJson,
    string? StartAuditJson = null, string? CompletionAuditJson = null);

/// <summary>Optional exact filters for the authorized execution history, before cursor paging.</summary>
public sealed record ManagementRuleExecutionQuery(string? ExecutionId = null, string? Version = null,
    string? InstanceId = null, string? Status = null, string? StartedFromUtc = null, string? StartedThroughUtc = null);
