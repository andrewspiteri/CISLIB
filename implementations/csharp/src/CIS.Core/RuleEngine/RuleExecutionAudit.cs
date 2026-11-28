using System.Buffers;
using System.Text.Json;
using CIS.Audit;
using CIS.Configuration;
using CIS.Messaging;
using CIS.Security;

namespace CIS.RuleEngine;

/// <summary>Stable v1 event types on the existing CIS Audit route.</summary>
public static class RuleEngineAuditEvents
{
    /// <summary>The exact selected version and captured inputs before comparison.</summary>
    public const string ExecutionStarted = "cis.rule-engine.execution.start";

    /// <summary>The observed trace and chosen terminal status, independently of caller receipt.</summary>
    public const string ExecutionCompleted = "cis.rule-engine.execution.complete";

    /// <summary>The version of each typed Rule Engine audit event.</summary>
    public const int Version = 1;
}

internal sealed record RuleExecutionAuditFacts(Guid ExecutionId, ConfigurationSnapshotTarget Target,
    LocalApplicationIdentity Identity, string RulesetId, string SchemaRevision, string Version,
    string? CorrelationId, DateTimeOffset StartedAtUtc);

internal sealed record RuleExecutionAuditStart(RuleExecutionAuditFacts Facts, RuleInputSnapshot Inputs);

internal sealed record RuleExecutionAuditCompletion(RuleExecutionAuditFacts Facts, RuleEvaluationTrace Trace,
    DateTimeOffset FinishedAtUtc, RuleEvaluationError? PriorError);

internal static class RuleExecutionAudit
{
    private const int MaximumEncodedBytes = 128 * 1024;
    private const int MaximumTransportDepth = 16;
    // Completion adds causation and uses different fixed event/status strings.
    private const int AdditionalCommonBytes = 160;

    internal static readonly AuditEventDefinition<RuleExecutionAuditStart> Start = new(
        RuleEngineAuditEvents.ExecutionStarted, RuleEngineAuditEvents.Version, AuditCategory.Business, new StartCodec());

    internal static readonly AuditEventDefinition<RuleExecutionAuditCompletion> Complete = new(
        RuleEngineAuditEvents.ExecutionCompleted, RuleEngineAuditEvents.Version, AuditCategory.Business, new CompletionCodec());

    internal static AuditCaptureOptions CaptureOptions(RuleExecutionAuditFacts facts, AuditOutcome outcome,
        DateTimeOffset occurredAt, Guid? startId = null)
        => new(outcome, occurredAt,
            correlationId: string.IsNullOrWhiteSpace(facts.CorrelationId) ? null : facts.CorrelationId,
            causationAuditId: startId, domainEventId: facts.ExecutionId.ToString("D") + (startId.HasValue ? ":complete" : ":start"));

    internal static bool Fits(CapturedAuditRecord captured, AuditLimits limits)
        => captured.Record.OriginalMemory.Length <= Math.Min(MaximumEncodedBytes, limits.MaximumPayloadBytes)
            && MessageWire.ValidateJson(captured.Record.OriginalMemory.Span, Math.Min(MaximumTransportDepth, limits.MaximumJsonDepth));

    internal static bool CompletionFits(CapturedAuditRecord start, RuleExecutionAuditFacts facts,
        PreparedRuleDefinition prepared, AuditLimits limits)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        WriteFacts(writer, facts);
        writer.Flush();
        int common = start.Record.OriginalMemory.Length - start.Record.DetailsJson.Length + AdditionalCommonBytes;
        // Each row fits this fixed header/error allowance plus one character per condition.
        // Selected indexes are at most two digits and come from the same bounded row sequence.
        int maximum = checked(common + buffer.WrittenCount + 384
            + prepared.Rows.Sum(row => 177 + row.Cells.Count));
        return maximum <= Math.Min(MaximumEncodedBytes, limits.MaximumPayloadBytes)
            && (prepared.Rows.Count == 0 || limits.MaximumJsonDepth >= 4);
    }

    private static void WriteFacts(Utf8JsonWriter writer, RuleExecutionAuditFacts facts)
    {
        writer.WriteString("executionId", facts.ExecutionId);
        writer.WriteString("authorityId", facts.Target.AuthorityId);
        writer.WriteString("applicationId", facts.Target.ApplicationId);
        writer.WriteString("application", facts.Identity.Application);
        writer.WriteString("environment", facts.Target.Environment);
        writer.WriteString("instanceId", facts.Identity.InstanceId);
        writer.WriteString("registrationId", facts.Target.RegistrationId);
        writer.WriteString("applicationBuild", EnvironmentOptions.BuildVersion);
        writer.WriteNumber("engineContractVersion", RuleEngineAuditEvents.Version);
        writer.WriteString("rulesetId", facts.RulesetId);
        writer.WriteString("schemaRevision", facts.SchemaRevision);
        writer.WriteString("version", facts.Version);
        if (facts.CorrelationId is { } correlation)
        {
            writer.WriteString("correlationId", correlation);
        }
        writer.WriteString("startedAtUtc", facts.StartedAtUtc);
    }

    private static void WriteError(Utf8JsonWriter writer, RuleEvaluationError? error,
        string code, string rowIndex, string columnIndex)
    {
        if (error is null)
        {
            return;
        }
        writer.WriteString(code, error.Code);
        if (error.RowIndex is { } row)
        {
            writer.WriteNumber(rowIndex, row);
        }
        if (error.ColumnIndex is { } column)
        {
            writer.WriteNumber(columnIndex, column);
        }
    }

    private sealed class StartCodec : IAuditDetailsCodec<RuleExecutionAuditStart>
    {
        public void Validate(RuleExecutionAuditStart details) => ArgumentNullException.ThrowIfNull(details);

        public void Write(Utf8JsonWriter writer, RuleExecutionAuditStart details)
        {
            writer.WriteStartObject();
            WriteFacts(writer, details.Facts);
            writer.WritePropertyName("inputs");
            details.Inputs.WriteTo(writer);
            writer.WriteEndObject();
        }
    }

    private sealed class CompletionCodec : IAuditDetailsCodec<RuleExecutionAuditCompletion>
    {
        public void Validate(RuleExecutionAuditCompletion details) => ArgumentNullException.ThrowIfNull(details);

        public void Write(Utf8JsonWriter writer, RuleExecutionAuditCompletion details)
        {
            RuleEvaluationTrace trace = details.Trace;
            writer.WriteStartObject();
            WriteFacts(writer, details.Facts);
            writer.WriteString("finishedAtUtc", details.FinishedAtUtc);
            writer.WriteString("status", trace.Status.ToString());
            WriteError(writer, trace.Error, "errorCode", "errorRow", "errorColumn");
            WriteError(writer, details.PriorError, "priorErrorCode", "priorErrorRow", "priorErrorColumn");
            writer.WriteStartArray("rows");
            foreach (RuleRowTrace row in trace.Rows)
            {
                writer.WriteStartObject();
                writer.WriteNumber("rowIndex", row.RowIndex);
                writer.WriteString("state", row.State.ToString());
                writer.WriteString("outcome", row.Outcome.ToString());
                string cellOutcomes = string.Create(row.Cells.Count, row.Cells, static (encoded, cells) =>
                {
                    for (int index = 0; index < cells.Count; index++)
                    {
                        encoded[index] = cells[index].Outcome switch
                        {
                            RuleCellOutcome.NotEvaluated => 'u',
                            RuleCellOutcome.NotMatched => 'n',
                            RuleCellOutcome.Matched => 'm',
                            RuleCellOutcome.Error => 'e',
                            _ => throw new InvalidOperationException("Undeclared rule cell outcome.")
                        };
                    }
                });
                writer.WriteString("cellOutcomes", cellOutcomes);
                WriteError(writer, row.Error, "errorCode", "errorRow", "errorColumn");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("selectedRowIndices");
            foreach (int index in trace.SelectedRowIndices)
            {
                writer.WriteNumberValue(index);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
