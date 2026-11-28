using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management.Wire;
using CIS.Messaging;
using CIS.RuleEngine;

namespace CIS.Management.Service.Configuration;

internal sealed record RuleEngineAuditObservation(Guid ExecutionId, Guid ApplicationId, Guid RegistrationId, Guid Version,
    string AuthorityId, string Application, string Environment, string InstanceId, string ApplicationBuild,
    int EngineContractVersion, string RulesetId, string SchemaRevision, string? CorrelationId, DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc, RuleExecutionStatus? Status, AuditRecord Record, JsonElement Details)
{
    internal bool MatchesDefinition(RuleDefinition definition)
    {
        if (definition.RulesetId != RulesetId || definition.SchemaRevision != SchemaRevision || definition.Version != Version.ToString("D"))
        {
            return false;
        }
        if (Status is null)
        {
            return true;
        }
        JsonElement rows = Details.GetProperty("rows");
        if (rows.GetArrayLength() != definition.Rows.Count)
        {
            return false;
        }
        RuleRow[] declared = definition.Rows.OrderBy(row => row.Ordinal).ToArray();
        for (int index = 0; index < declared.Length; index++)
        {
            JsonElement row = rows[index];
            if (row.GetProperty("rowIndex").GetInt32() != index
                || row.GetProperty("state").GetString() != declared[index].State.ToString()
                || row.GetProperty("cellOutcomes").GetString()!.Length != definition.Columns.Count)
            {
                return false;
            }
        }
        if (Status != RuleExecutionStatus.Completed)
        {
            return true;
        }
        int[] selected = Details.GetProperty("selectedRowIndices").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        int[] expected = Enumerable.Range(0, declared.Length)
            .Where(index => declared[index].State == RuleState.Enabled
                && rows[index].GetProperty("outcome").GetString() == nameof(RuleRowOutcome.Matched))
            .OrderByDescending(index => declared[index].Weight)
            .ThenBy(index => declared[index].Ordinal)
            .ToArray();
        return (definition.Mode != RuleExecutionMode.FirstMatch || expected.Length <= 1) && selected.SequenceEqual(expected);
    }
}

// The broker contract remains RM06 Audit v1. This consumer accepts only the two closed RM11 detail schemas.
internal sealed class RuleEngineAuditObservationCodec : IMessageCodec<RuleEngineAuditObservation>
{
    private const int MaximumRows = 48;
    private const int MaximumColumns = 24;
    private const int MaximumDepth = 16;
    private static readonly string[] CommonFields =
    [
        "executionId", "authorityId", "applicationId", "application", "environment", "instanceId", "registrationId",
        "applicationBuild", "engineContractVersion", "rulesetId", "schemaRevision", "version", "startedAtUtc"
    ];
    private static readonly string[] StartFields = [.. CommonFields, "inputs"];
    private static readonly string[] CompletionFields = [.. CommonFields, "finishedAtUtc", "status", "rows", "selectedRowIndices"];
    private static readonly string[] CompletionOptional =
    [
        "correlationId", "errorCode", "errorRow", "errorColumn", "priorErrorCode", "priorErrorRow", "priorErrorColumn"
    ];
    private static readonly string[] RowFields = ["rowIndex", "state", "outcome", "cellOutcomes"];
    private static readonly string[] RowOptional = ["errorCode", "errorRow", "errorColumn"];
    private readonly AuditRecordCodec codec = new(new(128 * 1024, MaximumDepth));

    public MessageContract Contract => codec.Contract;

    public byte[] Encode(RuleEngineAuditObservation value) => codec.Encode(value.Record);

    public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out RuleEngineAuditObservation? value)
    {
        value = null;
        if (!codec.TryDecode(contract, json, out AuditRecord? record) || record is null)
        {
            return false;
        }
        return TryDecode(record, out value);
    }

    internal static bool TryDecode(AuditRecord record, out RuleEngineAuditObservation? value)
    {
        value = null;
        if (record.EventVersion != RuleEngineAuditEvents.Version || record.Category != AuditCategory.Business
            || record.EventType is not (RuleEngineAuditEvents.ExecutionStarted or RuleEngineAuditEvents.ExecutionCompleted)
            || record.OriginalBytes.Length > 128 * 1024)
        {
            return false;
        }
        bool start = record.EventType == RuleEngineAuditEvents.ExecutionStarted;
        try
        {
            using JsonDocument document = JsonDocument.Parse(record.DetailsJson, new JsonDocumentOptions { MaxDepth = MaximumDepth });
            JsonElement details = document.RootElement;
            Shape(details, start ? StartFields : CompletionFields, start ? ["correlationId"] : CompletionOptional);
            Guid executionId = Id(details, "executionId");
            Guid applicationId = Id(details, "applicationId");
            Guid registrationId = Id(details, "registrationId");
            Guid version = Id(details, "version");
            string authorityId = Text(details, "authorityId");
            Require(ManagementWire.IsUuid(authorityId));
            string application = Text(details, "application");
            string environment = Text(details, "environment");
            string instanceId = Text(details, "instanceId");
            Require(application == record.Producer.Application && environment == record.Producer.Environment
                && instanceId == record.Producer.Instance?.ReportedIdentifier);
            string applicationBuild = Text(details, "applicationBuild");
            Require(ManagementWire.IsScalarText(applicationBuild, 512));
            int engineContractVersion = details.GetProperty("engineContractVersion").GetInt32();
            Require(engineContractVersion == RuleEngineAuditEvents.Version);
            string ruleset = RuleId(details, "rulesetId");
            string schema = RuleId(details, "schemaRevision");
            string? correlation = details.TryGetProperty("correlationId", out _) ? RuleId(details, "correlationId") : null;
            Require((string.IsNullOrWhiteSpace(correlation) ? null : correlation) == record.CorrelationId);
            Require(record.DomainEventId == executionId.ToString("D") + (start ? ":start" : ":complete"));
            DateTimeOffset startedAt = Timestamp(details, "startedAtUtc");
            DateTimeOffset? finishedAt = null;
            RuleExecutionStatus? status = null;

            if (start)
            {
                Require(record.Outcome == AuditOutcome.Accepted && record.OccurredAtUtc == startedAt && record.CausationAuditId is null);
                _ = new RuleInputSnapshot(ReadInput(details.GetProperty("inputs")));
            }
            else
            {
                finishedAt = Timestamp(details, "finishedAtUtc");
                Require(finishedAt >= startedAt && record.OccurredAtUtc == finishedAt
                    && record.CausationAuditId is { } causation && causation != Guid.Empty);
                status = EnumValue<RuleExecutionStatus>(details, "status");
                Require(record.Outcome == (status switch
                {
                    RuleExecutionStatus.Completed => AuditOutcome.Succeeded,
                    RuleExecutionStatus.Cancelled => AuditOutcome.Canceled,
                    _ => AuditOutcome.Failed
                }));
                ValidateCompletion(details, status.Value);
            }
            value = new(executionId, applicationId, registrationId, version, authorityId, application, environment, instanceId,
                applicationBuild, engineContractVersion, ruleset, schema, correlation, startedAt, finishedAt, status, record, details.Clone());
            return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException or JsonException or OverflowException)
        {
            return false;
        }
    }

    private static IEnumerable<KeyValuePair<string, RuleValue>> ReadInput(JsonElement input)
    {
        foreach (JsonElement entry in input.EnumerateArray())
        {
            string kind = Text(entry, "kind");
            Shape(entry, kind == "null" ? ["columnId", "kind"] : ["columnId", "kind", "value"], []);
            string key = RuleId(entry, "columnId");
            yield return new(key, kind switch
            {
                "null" => RuleValue.Null,
                "string" => RuleValue.FromString(Text(entry, "value")),
                "decimal" => RuleValue.FromDecimal(Text(entry, "value")),
                "boolean" => RuleValue.FromBoolean(entry.GetProperty("value").GetBoolean()),
                "stringList" => RuleValue.FromStringList(entry.GetProperty("value").EnumerateArray()
                    .Select(item => item.GetString() ?? throw new FormatException("Invalid Rule Engine input text."))),
                "json" => RuleValue.FromJson(entry.GetProperty("value")),
                _ => throw new FormatException("Invalid Rule Engine input kind.")
            });
        }
    }

    private static void ValidateCompletion(JsonElement details, RuleExecutionStatus status)
    {
        JsonElement rows = details.GetProperty("rows");
        int count = rows.GetArrayLength();
        Require(count <= MaximumRows);
        var matches = new HashSet<int>();
        for (int index = 0; index < count; index++)
        {
            JsonElement row = rows[index];
            Shape(row, RowFields, RowOptional);
            Require(row.GetProperty("rowIndex").GetInt32() == index);
            RuleState state = EnumValue<RuleState>(row, "state");
            RuleRowOutcome outcome = EnumValue<RuleRowOutcome>(row, "outcome");
            string cells = Text(row, "cellOutcomes");
            Require(cells.Length <= MaximumColumns);
            Require(state == RuleState.Disabled ? outcome is RuleRowOutcome.NotReached or RuleRowOutcome.Disabled : outcome != RuleRowOutcome.Disabled);
            int? errorColumn = ReadError(row, "errorCode", "errorRow", "errorColumn", count, requireError: outcome == RuleRowOutcome.Error);
            bool cancelledBeforeCell = outcome == RuleRowOutcome.Error && Text(row, "errorCode") == "Cancelled";
            Require(!cancelledBeforeCell || status == RuleExecutionStatus.Cancelled);
            Require(ValidCells(cells, outcome, errorColumn, cancelledBeforeCell));
            if (outcome == RuleRowOutcome.Error)
            {
                Require(row.GetProperty("errorRow").GetInt32() == index);
            }
            if (state == RuleState.Enabled && outcome == RuleRowOutcome.Matched)
            {
                matches.Add(index);
            }
        }
        _ = ReadError(details, "errorCode", "errorRow", "errorColumn", count, requireError: status != RuleExecutionStatus.Completed);
        _ = ReadError(details, "priorErrorCode", "priorErrorRow", "priorErrorColumn", count);
        if (status == RuleExecutionStatus.Cancelled)
        {
            Require(Text(details, "errorCode") == "Cancelled");
        }
        JsonElement selected = details.GetProperty("selectedRowIndices");
        Require(selected.GetArrayLength() == (status == RuleExecutionStatus.Completed ? matches.Count : 0));
        foreach (JsonElement selection in selected.EnumerateArray())
        {
            Require(matches.Remove(selection.GetInt32()));
        }
    }

    private static bool ValidCells(string cells, RuleRowOutcome outcome, int? errorColumn, bool cancelledBeforeCell)
    {
        if (outcome is RuleRowOutcome.NotReached or RuleRowOutcome.Disabled)
        {
            return cells.All(cell => cell == 'u') && errorColumn is null;
        }
        if (outcome == RuleRowOutcome.Matched)
        {
            return cells.All(cell => cell == 'm') && errorColumn is null;
        }
        if (cancelledBeforeCell)
        {
            return errorColumn is { } firstUnevaluated && firstUnevaluated < cells.Length
                && cells.Take(firstUnevaluated).All(cell => cell == 'm') && cells.Skip(firstUnevaluated).All(cell => cell == 'u');
        }
        char stop = outcome == RuleRowOutcome.Error ? 'e' : 'n';
        int index = cells.IndexOf(stop);
        return index >= 0 && (stop != 'e' || errorColumn == index)
            && cells.Take(index).All(cell => cell == 'm') && cells.Skip(index + 1).All(cell => cell == 'u');
    }

    private static int? ReadError(JsonElement value, string code, string row, string column, int maximumRows, bool? requireError = null)
    {
        bool hasError = value.TryGetProperty(code, out _);
        if (requireError is { } required)
        {
            Require(hasError == required);
        }
        bool hasRow = value.TryGetProperty(row, out JsonElement position);
        bool hasColumn = value.TryGetProperty(column, out JsonElement index);
        Require(hasError || !hasRow && !hasColumn);
        if (hasError)
        {
            _ = RuleId(value, code);
        }
        if (hasRow)
        {
            int rowIndex = position.GetInt32();
            Require(rowIndex >= 0 && rowIndex < maximumRows);
        }
        if (!hasColumn)
        {
            return null;
        }
        int columnIndex = index.GetInt32();
        Require(hasRow && columnIndex is >= 0 and < MaximumColumns);
        return columnIndex;
    }

    private static void Shape(JsonElement value, string[] required, string[] optional)
    {
        Require(value.ValueKind == JsonValueKind.Object && required.All(name => value.TryGetProperty(name, out _))
            && value.EnumerateObject().All(member => required.Contains(member.Name, StringComparer.Ordinal)
                || optional.Contains(member.Name, StringComparer.Ordinal)));
    }

    private static string Text(JsonElement value, string name)
        => value.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()! : throw new FormatException("Invalid Rule Engine audit text.");

    private static string RuleId(JsonElement value, string name)
    {
        string result = Text(value, name);
        Require(result.Length > 0 && ManagementWire.IsScalarText(result, 128, allowEmpty: true) && Encoding.UTF8.GetByteCount(result) <= 128);
        return result;
    }

    private static Guid Id(JsonElement value, string name)
    {
        string result = Text(value, name);
        Require(ManagementWire.IsUuid(result));
        return Guid.Parse(result);
    }

    private static DateTimeOffset Timestamp(JsonElement value, string name)
    {
        DateTimeOffset time = value.GetProperty(name).GetDateTimeOffset();
        Require(time.Offset == TimeSpan.Zero);
        return time;
    }

    private static T EnumValue<T>(JsonElement value, string name) where T : struct, Enum
    {
        string text = Text(value, name);
        Require(Enum.TryParse(text, out T result) && Enum.IsDefined(result) && result.ToString() == text);
        return result;
    }

    private static void Require(bool valid)
    {
        if (!valid)
        {
            throw new FormatException("Invalid Rule Engine audit detail.");
        }
    }
}
