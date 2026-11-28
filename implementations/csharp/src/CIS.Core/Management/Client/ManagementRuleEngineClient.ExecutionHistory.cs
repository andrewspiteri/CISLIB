using System.Globalization;
using System.Text.Json;
using CIS.Audit;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RuleEngine;

namespace CIS.Management;

/// <summary>Optional scoped search and protected execution history details on the Management API.</summary>
public interface IManagementRuleEngineExecutionHistoryClient
{
    /// <summary>Searches bounded execution summaries under an exact application and ruleset.</summary>
    Task<ManagementRuleEngineCallResult<ManagementPage<ManagementRuleExecutionSummary>>> ListExecutionsAsync(string applicationId,
        string rulesetId, ManagementRuleExecutionQuery? query = null, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads independently protected original evidence and its exact immutable definition.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleExecutionDetail>> GetExecutionAsync(string applicationId,
        string rulesetId, string executionId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}

public sealed partial class ManagementRuleEngineClient : IManagementRuleEngineExecutionHistoryClient
{
    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementPage<ManagementRuleExecutionSummary>>> ListExecutionsAsync(
        string applicationId, string rulesetId, ManagementRuleExecutionQuery? query = null, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementRuleExecutionSummary>>(new(applicationId, rulesetId, ExecutionQuery: query ?? new()),
            "/executions", page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleExecutionDetail>> GetExecutionAsync(string applicationId,
        string rulesetId, string executionId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        return CallAsync<ManagementRuleExecutionDetail>(new(applicationId, rulesetId, ExecutionId: executionId),
            "/executions", options: options, token: cancellationToken);
    }

    private static void AddExecutionQuery(RestQuery target, ManagementRuleExecutionQuery query)
    {
        ManagementRuleEngineWire.ValidateExecutionQuery(query);
        AddQuery(target, "executionId", query.ExecutionId);
        AddQuery(target, "version", query.Version);
        AddQuery(target, "instanceId", query.InstanceId);
        AddQuery(target, "status", query.Status);
        AddQuery(target, "startedFromUtc", query.StartedFromUtc);
        AddQuery(target, "startedThroughUtc", query.StartedThroughUtc);
    }
}

public static partial class ManagementRuleEngineWire
{
    private static readonly AuditRecordCodec ExecutionAuditCodec = new(new AuditLimits(128 * 1024, 16));
    private const string ExecutionTimestamp = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    internal static void ValidateExecutionQuery(ManagementRuleExecutionQuery query)
    {
        Require(OptionalUuid(query.ExecutionId) && OptionalUuid(query.Version) && OptionalUuid(query.InstanceId)
            && (query.Status is null || IsExecutionStatus(query.Status))
            && (query.StartedFromUtc is null || ManagementWire.IsTimestamp(query.StartedFromUtc))
            && (query.StartedThroughUtc is null || ManagementWire.IsTimestamp(query.StartedThroughUtc))
            && (query.StartedFromUtc is null || query.StartedThroughUtc is null
                || string.CompareOrdinal(query.StartedFromUtc, query.StartedThroughUtc) <= 0),
            "Supply exact execution filters and an ordered UTC range.");
    }

    internal static bool MatchesExecutionQuery(ManagementRuleExecutionSummary execution, ManagementRuleExecutionQuery? query)
        => query is null || (query.ExecutionId is null || query.ExecutionId == execution.ExecutionId)
            && (query.Version is null || query.Version == execution.Version)
            && (query.InstanceId is null || query.InstanceId == execution.InstanceId)
            && (query.Status is null || query.Status == execution.Status)
            && (query.StartedFromUtc is null || string.CompareOrdinal(query.StartedFromUtc, execution.StartedAtUtc) <= 0)
            && (query.StartedThroughUtc is null || string.CompareOrdinal(execution.StartedAtUtc, query.StartedThroughUtc) <= 0);

    private static void CheckExecutionSummary(ManagementRuleExecutionSummary execution)
    {
        CheckScope(execution.ApplicationId, execution.Environment, execution.RulesetId);
        Require(ManagementWire.IsUuid(execution.ExecutionId) && ManagementWire.IsUuid(execution.Version)
            && IsRuleId(execution.SchemaRevision) && ManagementWire.IsScalarText(execution.ProducerApplication, 255)
            && ManagementWire.IsUuid(execution.InstanceId) && ManagementWire.IsUuid(execution.RegistrationId)
            && ManagementWire.IsScalarText(execution.ApplicationBuild, 512) && ManagementWire.IsTimestamp(execution.StartedAtUtc),
            "Invalid Rule Engine execution identity.");
        Require(IsExecutionStatus(execution.Status) && (execution.StartReceived || execution.CompletionReceived)
            && ((execution.StartReceived && execution.CompletionReceived) == (execution.Status != "Incomplete"))
            && (execution.CompletionReceived ? ManagementWire.IsTimestamp(execution.FinishedAtUtc) : execution.FinishedAtUtc is null),
            "The execution status disagrees with its observed phases.");
        Require(execution.ReturnedMatchCount is null || execution.Status == "Completed"
            && execution.ReturnedMatchCount is >= 0 and <= RuleBounds.MaximumRows,
            "The returned match count requires a paired completed execution.");
    }

    private static void CheckExecutionDetail(ManagementRuleExecutionDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail.Execution);
        ManagementRuleExecutionSummary execution = detail.Execution;
        CheckExecutionSummary(execution);
        RuleDefinition definition = Definition(detail.DefinitionJson, execution.RulesetId, execution.SchemaRevision, execution.Version);
        Require(execution.StartReceived == (detail.StartAuditJson is not null) &&
            execution.CompletionReceived == (detail.CompletionAuditJson is not null), "Execution evidence is missing or unexpected.");

        ExecutionEvidence? start = detail.StartAuditJson is { } startJson
            ? CheckExecutionAudit(startJson, execution, definition, completion: false) : null;
        ExecutionEvidence? completion = detail.CompletionAuditJson is { } completionJson
            ? CheckExecutionAudit(completionJson, execution, definition, completion: true) : null;
        if (start is not null && completion is not null)
        {
            Require(completion.Audit.CausationAuditId == start.Audit.AuditId && completion.Audit.AuditId != start.Audit.AuditId
                && completion.AuthorityId == start.AuthorityId && completion.CorrelationId == start.CorrelationId,
                "The original start and completion evidence do not belong to the same execution.");
        }
    }

    private static ExecutionEvidence CheckExecutionAudit(string raw, ManagementRuleExecutionSummary execution,
        RuleDefinition definition, bool completion)
    {
        if (!ExecutionAuditCodec.TryDecode(RawBytes(raw), out AuditRecord? record, out _) || record is null)
        {
            throw new ArgumentException("Invalid original Rule Engine Audit record.");
        }
        string phase = completion ? "complete" : "start";
        string eventType = completion ? RuleEngineAuditEvents.ExecutionCompleted : RuleEngineAuditEvents.ExecutionStarted;
        string? occurred = completion ? execution.FinishedAtUtc : execution.StartedAtUtc;
        Require(record.EventType == eventType && record.EventVersion == RuleEngineAuditEvents.Version
            && record.Category == AuditCategory.Business && record.DomainEventId == execution.ExecutionId + ":" + phase
            && record.Producer.Application == execution.ProducerApplication && record.Producer.Environment == execution.Environment
            && record.Producer.Instance is { Kind: AuditInstanceKind.Reported } instance && instance.ReportedIdentifier == execution.InstanceId
            && record.CausationAuditId.HasValue == completion
            && Timestamp(record.OccurredAtUtc) == occurred, "Original Audit phase identity does not match the execution.");

        using JsonDocument details = JsonDocument.Parse(record.DetailsJson);
        JsonElement facts = details.RootElement;
        string? authority = EvidenceText(facts, "authorityId");
        string? correlation = EvidenceText(facts, "correlationId");
        Require(ManagementWire.IsUuid(authority) && (!facts.TryGetProperty("correlationId", out _) || IsRuleId(correlation))
            && record.CorrelationId == (string.IsNullOrWhiteSpace(correlation) ? null : correlation)
            && EvidenceText(facts, "executionId") == execution.ExecutionId
            && EvidenceText(facts, "applicationId") == execution.ApplicationId
            && EvidenceText(facts, "application") == execution.ProducerApplication
            && EvidenceText(facts, "environment") == execution.Environment
            && EvidenceText(facts, "instanceId") == execution.InstanceId
            && EvidenceText(facts, "registrationId") == execution.RegistrationId
            && EvidenceText(facts, "applicationBuild") == execution.ApplicationBuild
            && facts.TryGetProperty("engineContractVersion", out JsonElement version)
            && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int auditVersion)
            && auditVersion == RuleEngineAuditEvents.Version
            && EvidenceText(facts, "rulesetId") == execution.RulesetId
            && EvidenceText(facts, "schemaRevision") == execution.SchemaRevision && EvidenceText(facts, "version") == execution.Version
            && EvidenceTime(facts, "startedAtUtc") == execution.StartedAtUtc, "Original Audit facts do not echo the selected execution.");

        if (completion)
        {
            string? status = EvidenceText(facts, "status");
            Require(IsTerminalStatus(status) && (execution.Status == "Incomplete" || status == execution.Status)
                && EvidenceTime(facts, "finishedAtUtc") == execution.FinishedAtUtc
                && record.Outcome == (status == "Completed" ? AuditOutcome.Succeeded :
                    status == "Cancelled" ? AuditOutcome.Canceled : AuditOutcome.Failed),
                "The original completion does not match the terminal observation.");
            CheckCompletionRows(facts, definition.Rows.Count, status!, execution.ReturnedMatchCount);
        }
        else
        {
            Require(record.Outcome == AuditOutcome.Accepted && facts.TryGetProperty("inputs", out JsonElement inputs)
                && inputs.ValueKind == JsonValueKind.Array, "Invalid original execution-start evidence.");
        }

        return new(record, authority!, correlation);
    }

    private static void CheckCompletionRows(JsonElement facts, int count, string status, int? returnedMatchCount)
    {
        if (!facts.TryGetProperty("rows", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() != count || !facts.TryGetProperty("selectedRowIndices", out JsonElement selected)
            || selected.ValueKind != JsonValueKind.Array || selected.GetArrayLength() > count
            || status != "Completed" && selected.GetArrayLength() != 0
            || returnedMatchCount is { } reported && selected.GetArrayLength() != reported)
        {
            throw new ArgumentException("Invalid observed row and selected-index collections.");
        }
        var seen = new HashSet<int>();
        foreach (JsonElement index in selected.EnumerateArray())
        {
            Require(index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out int position)
                && position >= 0 && position < count && seen.Add(position), "Invalid selected row index.");
        }
    }

    private static string? EvidenceText(JsonElement value, string field)
        => value.TryGetProperty(field, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static string? EvidenceTime(JsonElement value, string field)
        => value.TryGetProperty(field, out JsonElement item) && item.ValueKind == JsonValueKind.String
            && item.TryGetDateTimeOffset(out DateTimeOffset time) && time.Offset == TimeSpan.Zero ? Timestamp(time) : null;

    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString(ExecutionTimestamp, CultureInfo.InvariantCulture);

    private static bool IsExecutionStatus(string? value) => value == "Incomplete" || IsTerminalStatus(value);

    private static bool IsTerminalStatus(string? value) => value is "Completed" or "Failed" or "Cancelled";

    private sealed record ExecutionEvidence(AuditRecord Audit, string AuthorityId, string? CorrelationId);
}
