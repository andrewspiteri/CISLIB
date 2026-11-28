using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CIS.RuleEngine;

/// <summary>The ordinary preset or special condition selected for a cell.</summary>
public enum RulePresetKind
{
    /// <summary>No preset has been specified.</summary>
    Unknown,
    /// <summary>A scalar string or a wildcard pattern.</summary>
    String,
    /// <summary>An exact decimal value.</summary>
    Decimal,
    /// <summary>A native Boolean value.</summary>
    Boolean,
    /// <summary>A string membership list.</summary>
    StringList,
    /// <summary>Matches regardless of input.</summary>
    Any,
    /// <summary>Matches an omitted input or an explicit null.</summary>
    Missing
}

/// <summary>The comparison performed by an ordinary condition.</summary>
public enum RuleOperator
{
    /// <summary>No ordinary operator is selected.</summary>
    None,
    /// <summary>Matches equal operands.</summary>
    Equals,
    /// <summary>Matches unequal present operands.</summary>
    NotEquals,
    /// <summary>Matches a complete string against a wildcard pattern.</summary>
    WildcardMatch,
    /// <summary>Matches a decimal input greater than the preset.</summary>
    GreaterThan,
    /// <summary>Matches a decimal input greater than or equal to the preset.</summary>
    GreaterThanOrEqual,
    /// <summary>Matches a decimal input less than the preset.</summary>
    LessThan,
    /// <summary>Matches a decimal input less than or equal to the preset.</summary>
    LessThanOrEqual,
    /// <summary>Matches a present string contained in the preset list.</summary>
    IsIn,
    /// <summary>Matches a present string not contained in the preset list.</summary>
    IsNotIn
}

/// <summary>Controls whether a row is evaluated and eligible for selection.</summary>
public enum RuleState
{
    /// <summary>Evaluate and select a matching row.</summary>
    Enabled = 1,
    /// <summary>Evaluate the row and observe its outcomes without selecting it.</summary>
    Test,
    /// <summary>Skip evaluation of the row.</summary>
    Disabled
}

/// <summary>Controls which enabled matches are selected.</summary>
public enum RuleExecutionMode
{
    /// <summary>Stop at the first enabled match in ordinal order.</summary>
    FirstMatch = 1,
    /// <summary>Select all enabled matches in descending weight order.</summary>
    AllMatching
}

/// <summary>The JSON value kind described by an optional metadata shape.</summary>
public enum RuleMetadataKind
{
    /// <summary>A JSON object with declared properties.</summary>
    Object = 1,
    /// <summary>A JSON array with a declared element shape.</summary>
    Array,
    /// <summary>A JSON string.</summary>
    String,
    /// <summary>A JSON number.</summary>
    Number,
    /// <summary>A native JSON Boolean.</summary>
    Boolean
}

/// <summary>One named property in an inert metadata declaration.</summary>
/// <param name="Name">The exact property name.</param>
/// <param name="Required">Whether the property must be present.</param>
/// <param name="Shape">The declaration for a present value.</param>
public sealed record RuleMetadataProperty(string Name, bool Required, RuleMetadataShape Shape);

/// <summary>An inert JSON shape declared by the application for rule metadata.</summary>
public sealed class RuleMetadataShape
{
    /// <summary>Captures an object, array or scalar declaration.</summary>
    public RuleMetadataShape(RuleMetadataKind kind, bool nullable = false,
        IEnumerable<RuleMetadataProperty>? properties = null, RuleMetadataShape? element = null)
    {
        Kind = kind;
        Nullable = nullable;
        Properties = RuleBounds.Copy(properties ?? [], RuleBounds.MaximumMetadataBytes, nameof(properties));
        Element = element;
    }

    /// <summary>Gets the expected JSON kind.</summary>
    public RuleMetadataKind Kind
    {
        get;
    }

    /// <summary>Gets whether an explicit null satisfies the declaration.</summary>
    public bool Nullable
    {
        get;
    }

    /// <summary>Gets the declared object properties.</summary>
    public IReadOnlyList<RuleMetadataProperty> Properties
    {
        get;
    }

    /// <summary>Gets the declared shape of array elements.</summary>
    public RuleMetadataShape? Element
    {
        get;
    }
}

/// <summary>One condition column in the order declared by the application.</summary>
public sealed class RuleColumn
{
    /// <summary>Captures supported presets and optional constraints for a present input.</summary>
    public RuleColumn(string id, string displayName, RuleValueKind inputKind,
        IEnumerable<RulePresetKind> allowedPresetKinds, int? minimumUtf8Bytes = null, int? maximumUtf8Bytes = null,
        int? exactScalarCount = null, decimal? minimumDecimal = null, decimal? maximumDecimal = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(displayName);

        Id = id;
        DisplayName = displayName;
        InputKind = inputKind;
        AllowedPresetKinds = RuleBounds.Copy(allowedPresetKinds, 7, nameof(allowedPresetKinds));
        MinimumUtf8Bytes = minimumUtf8Bytes;
        MaximumUtf8Bytes = maximumUtf8Bytes;
        ExactScalarCount = exactScalarCount;
        MinimumDecimal = minimumDecimal;
        MaximumDecimal = maximumDecimal;
    }

    /// <summary>Gets the exact column identity.</summary>
    public string Id
    {
        get;
    }

    /// <summary>Gets the application-supplied display name.</summary>
    public string DisplayName
    {
        get;
    }

    /// <summary>Gets the kind required by a reached ordinary comparison.</summary>
    public RuleValueKind InputKind
    {
        get;
    }

    /// <summary>Gets the permitted ordinary preset kinds.</summary>
    public IReadOnlyList<RulePresetKind> AllowedPresetKinds
    {
        get;
    }

    /// <summary>Gets the optional minimum UTF-8 byte count for a present string input.</summary>
    public int? MinimumUtf8Bytes
    {
        get;
    }

    /// <summary>Gets the optional maximum UTF-8 byte count for a present string input.</summary>
    public int? MaximumUtf8Bytes
    {
        get;
    }

    /// <summary>Gets the optional exact Unicode scalar count for a present string input.</summary>
    public int? ExactScalarCount
    {
        get;
    }

    /// <summary>Gets the optional inclusive minimum for a present decimal input.</summary>
    public decimal? MinimumDecimal
    {
        get;
    }

    /// <summary>Gets the optional inclusive maximum for a present decimal input.</summary>
    public decimal? MaximumDecimal
    {
        get;
    }
}

/// <summary>One explicit ordinary condition or special marker in a rule row.</summary>
public sealed class RuleCondition
{
    /// <summary>Records the requested condition for shared definition validation.</summary>
    public RuleCondition(string columnId, RulePresetKind presetKind, RuleOperator @operator = RuleOperator.None,
        RuleValue? preset = null)
    {
        ArgumentNullException.ThrowIfNull(columnId);

        ColumnId = columnId;
        PresetKind = presetKind;
        Operator = @operator;
        Preset = preset;
    }

    /// <summary>Gets the exact identity of the referenced column.</summary>
    public string ColumnId
    {
        get;
    }

    /// <summary>Gets the selected ordinary preset kind or marker.</summary>
    public RulePresetKind PresetKind
    {
        get;
    }

    /// <summary>Gets the requested ordinary comparison, if any.</summary>
    public RuleOperator Operator
    {
        get;
    }

    /// <summary>Gets the provided preset for an ordinary comparison.</summary>
    public RuleValue? Preset
    {
        get;
    }
}

/// <summary>A detached rule row, including its configured conditions and metadata.</summary>
public sealed class RuleRow
{
    /// <summary>Captures a row with a native decimal weight, defaulting to zero.</summary>
    public RuleRow(string ruleId, int ordinal, RuleState state, IEnumerable<RuleCondition> conditions,
        decimal weight = 0m, JsonElement? metadata = null, string? comment = null)
        : this(ruleId, ordinal, state, conditions, weight, weight.ToString(CultureInfo.InvariantCulture), metadata, comment)
    {
    }

    /// <summary>Captures a row and preserves the supplied exact decimal weight text.</summary>
    public RuleRow(string ruleId, int ordinal, RuleState state, IEnumerable<RuleCondition> conditions,
        string weightText, JsonElement? metadata = null, string? comment = null)
        : this(ruleId, ordinal, state, conditions, RuleValue.FromDecimal(weightText).DecimalValue, weightText, metadata, comment)
    {
    }

    private RuleRow(string ruleId, int ordinal, RuleState state, IEnumerable<RuleCondition> conditions, decimal weight,
        string weightText, JsonElement? metadata, string? comment)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        RuleId = ruleId;
        Ordinal = ordinal;
        State = state;
        Conditions = RuleBounds.Copy(conditions, RuleBounds.MaximumColumns, nameof(conditions));
        Weight = weight;
        WeightText = weightText;
        Comment = comment;

        if (metadata is { } value)
        {
            if (value.ValueKind == JsonValueKind.Undefined ||
                JsonMarshal.GetRawUtf8Value(value).Length > RuleBounds.MaximumMetadataBytes)
            {
                throw new ArgumentException("Metadata must contain a bounded JSON value.", nameof(metadata));
            }

            Metadata = value.Clone();
        }
    }

    /// <summary>Gets the stable row identity, independent of order and version.</summary>
    public string RuleId
    {
        get;
    }

    /// <summary>Gets the nonnegative ordinal requested for this version.</summary>
    public int Ordinal
    {
        get;
    }

    /// <summary>Gets the configured state.</summary>
    public RuleState State
    {
        get;
    }

    /// <summary>Gets the configured cells.</summary>
    public IReadOnlyList<RuleCondition> Conditions
    {
        get;
    }

    /// <summary>Gets the exact native decimal weight.</summary>
    public decimal Weight
    {
        get;
    }

    /// <summary>Gets the exact decimal weight text for portable transport.</summary>
    public string WeightText
    {
        get;
    }

    /// <summary>Gets the detached metadata, if supplied.</summary>
    public JsonElement? Metadata
    {
        get;
    }

    /// <summary>Gets the operator comment, if supplied.</summary>
    public string? Comment
    {
        get;
    }
}

/// <summary>A detached version with its complete ordered schema and all configured rows.</summary>
public sealed class RuleDefinition
{
    /// <summary>Captures a definition for shared validation before save or adoption.</summary>
    public RuleDefinition(string rulesetId, string schemaRevision, string version, RuleExecutionMode mode,
        IEnumerable<RuleColumn> columns, IEnumerable<RuleRow> rows, string? displayName = null,
        RuleMetadataShape? metadataShape = null)
    {
        ArgumentNullException.ThrowIfNull(rulesetId);
        ArgumentNullException.ThrowIfNull(schemaRevision);
        ArgumentNullException.ThrowIfNull(version);

        RulesetId = rulesetId;
        SchemaRevision = schemaRevision;
        Version = version;
        Mode = mode;
        Columns = RuleBounds.Copy(columns, RuleBounds.MaximumColumns, nameof(columns));
        Rows = RuleBounds.Copy(rows, RuleBounds.MaximumRows, nameof(rows));
        DisplayName = displayName;
        MetadataShape = metadataShape;
    }

    /// <summary>Gets the stable ruleset identity.</summary>
    public string RulesetId
    {
        get;
    }

    /// <summary>Gets the immutable schema revision.</summary>
    public string SchemaRevision
    {
        get;
    }

    /// <summary>Gets the immutable version identity.</summary>
    public string Version
    {
        get;
    }

    /// <summary>Gets the configured execution mode.</summary>
    public RuleExecutionMode Mode
    {
        get;
    }

    /// <summary>Gets the columns in their declared evaluation order.</summary>
    public IReadOnlyList<RuleColumn> Columns
    {
        get;
    }

    /// <summary>Gets the submitted rows, before validation orders them by ordinal.</summary>
    public IReadOnlyList<RuleRow> Rows
    {
        get;
    }

    /// <summary>Gets the optional application-supplied display name.</summary>
    public string? DisplayName
    {
        get;
    }

    /// <summary>Gets the declared metadata shape, if any.</summary>
    public RuleMetadataShape? MetadataShape
    {
        get;
    }
}

/// <summary>A request for an application-scoped evaluator to select and evaluate a deployed version.</summary>
public sealed class RuleEvaluationRequest
{
    /// <summary>Captures the requested ruleset, owned inputs and optional exact selection and correlation.</summary>
    public RuleEvaluationRequest(string rulesetId, RuleInputSnapshot inputs, string? exactVersion = null,
        string? correlationId = null)
    {
        RuleBounds.RequireId(rulesetId, nameof(rulesetId));
        ArgumentNullException.ThrowIfNull(inputs);
        if (exactVersion is not null)
        {
            RuleBounds.RequireId(exactVersion, nameof(exactVersion));
        }

        if (correlationId is not null)
        {
            RuleBounds.RequireId(correlationId, nameof(correlationId));
        }

        RulesetId = rulesetId;
        Inputs = inputs;
        ExactVersion = exactVersion;
        CorrelationId = correlationId;
    }

    /// <summary>Gets the requested ruleset identity.</summary>
    public string RulesetId
    {
        get;
    }

    /// <summary>Gets the detached supplied inputs.</summary>
    public RuleInputSnapshot Inputs
    {
        get;
    }

    /// <summary>Gets an optional exact deployed version request.</summary>
    public string? ExactVersion
    {
        get;
    }

    /// <summary>Gets the diagnostic correlation, if supplied.</summary>
    public string? CorrelationId
    {
        get;
    }
}

/// <summary>The mockable application entry point for audited rule evaluation.</summary>
public interface IRuleEvaluator
{
    /// <summary>Evaluates a locally supported version eligible for the app's deployment scope.</summary>
    Task<RuleEvaluationResult> EvaluateAsync(RuleEvaluationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The terminal outcome of an invocation.</summary>
public enum RuleExecutionStatus
{
    /// <summary>The invocation completed successfully, including when no rows were selected.</summary>
    Completed = 1,
    /// <summary>The invocation did not produce a usable result.</summary>
    Failed,
    /// <summary>Caller cancellation ended evaluation before finalization.</summary>
    Cancelled
}

/// <summary>An observed row outcome in evaluation order.</summary>
public enum RuleRowOutcome
{
    /// <summary>Execution stopped before reaching the row.</summary>
    NotReached,
    /// <summary>The row was reached and skipped due to its disabled state.</summary>
    Disabled,
    /// <summary>A reached condition did not match.</summary>
    NotMatched,
    /// <summary>All conditions in the evaluated row matched.</summary>
    Matched,
    /// <summary>A reached condition produced an evaluation error.</summary>
    Error
}

/// <summary>An observed condition outcome in schema order.</summary>
public enum RuleCellOutcome
{
    /// <summary>The condition was not evaluated.</summary>
    NotEvaluated,
    /// <summary>The condition did not match.</summary>
    NotMatched,
    /// <summary>The condition matched.</summary>
    Matched,
    /// <summary>Evaluation of the condition failed.</summary>
    Error
}

/// <summary>A stable error code, explanation and optional position in the captured definition.</summary>
/// <param name="Code">The stable code.</param>
/// <param name="Message">A readable explanation.</param>
/// <param name="RowIndex">The zero-based ordinal-ordered position, if applicable.</param>
/// <param name="ColumnIndex">The zero-based schema-ordered position, if applicable.</param>
public sealed record RuleEvaluationError(string Code, string Message, int? RowIndex = null, int? ColumnIndex = null);

/// <summary>The observed outcome of a condition in the ordered captured schema.</summary>
/// <param name="ColumnIndex">The zero-based schema-ordered position.</param>
/// <param name="Outcome">The actual condition outcome.</param>
/// <param name="Error">The evaluation error, if applicable.</param>
public sealed record RuleCellTrace(int ColumnIndex, RuleCellOutcome Outcome, RuleEvaluationError? Error = null);

/// <summary>The observed outcome for a row and its conditions.</summary>
public sealed class RuleRowTrace
{
    /// <summary>Captures a row outcome and its ordered condition observations.</summary>
    public RuleRowTrace(int rowIndex, RuleState state, RuleRowOutcome outcome, IEnumerable<RuleCellTrace> cells,
        RuleEvaluationError? error = null)
    {
        RowIndex = rowIndex;
        State = state;
        Outcome = outcome;
        Cells = RuleBounds.Copy(cells, RuleBounds.MaximumColumns, nameof(cells));
        Error = error;
    }

    /// <summary>Gets the zero-based ordinal-ordered position.</summary>
    public int RowIndex
    {
        get;
    }

    /// <summary>Gets the captured configured state of the row.</summary>
    public RuleState State
    {
        get;
    }

    /// <summary>Gets the actual row outcome.</summary>
    public RuleRowOutcome Outcome
    {
        get;
    }

    /// <summary>Gets the condition observations.</summary>
    public IReadOnlyList<RuleCellTrace> Cells
    {
        get;
    }

    /// <summary>Gets the row evaluation error, if applicable.</summary>
    public RuleEvaluationError? Error
    {
        get;
    }
}

/// <summary>The immutable outcome and observations of an application-facing invocation.</summary>
public sealed class RuleEvaluationResult
{
    /// <summary>Captures settled observations. Only completed invocations may expose selected rows.</summary>
    public RuleEvaluationResult(Guid executionId, string rulesetId, string? version, RuleExecutionStatus status,
        IEnumerable<RuleRow>? matches = null, IEnumerable<RuleRowTrace>? trace = null,
        IEnumerable<RuleEvaluationError>? diagnostics = null, RuleEvaluationError? error = null)
    {
        if (executionId == Guid.Empty)
        {
            throw new ArgumentException("An execution identity must be nonempty.", nameof(executionId));
        }

        RuleBounds.RequireId(rulesetId, nameof(rulesetId));
        if (version is not null)
        {
            RuleBounds.RequireId(version, nameof(version));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Matches = RuleBounds.Copy(matches ?? [], RuleBounds.MaximumRows, nameof(matches));
        if (status != RuleExecutionStatus.Completed && Matches.Count > 0)
        {
            throw new ArgumentException("Only a completed invocation may contain selected rows.", nameof(matches));
        }

        ExecutionId = executionId;
        RulesetId = rulesetId;
        Version = version;
        Status = status;
        Trace = RuleBounds.Copy(trace ?? [], RuleBounds.MaximumRows, nameof(trace));
        Diagnostics = RuleBounds.Copy(diagnostics ?? [], RuleBounds.MaximumRows, nameof(diagnostics));
        Error = error;
    }

    /// <summary>Gets the invocation identity used for audit and counters.</summary>
    public Guid ExecutionId
    {
        get;
    }

    /// <summary>Gets the requested ruleset identity.</summary>
    public string RulesetId
    {
        get;
    }

    /// <summary>Gets the captured version, if version selection succeeded.</summary>
    public string? Version
    {
        get;
    }

    /// <summary>Gets the terminal outcome.</summary>
    public RuleExecutionStatus Status
    {
        get;
    }

    /// <summary>Gets the selected rows in result order.</summary>
    public IReadOnlyList<RuleRow> Matches
    {
        get;
    }

    /// <summary>Gets the observed rows in evaluation order.</summary>
    public IReadOnlyList<RuleRowTrace> Trace
    {
        get;
    }

    /// <summary>Gets continued Test errors and other bounded diagnostics.</summary>
    public IReadOnlyList<RuleEvaluationError> Diagnostics
    {
        get;
    }

    /// <summary>Gets the terminal error, if applicable.</summary>
    public RuleEvaluationError? Error
    {
        get;
    }

    /// <summary>Gets the prior cancellation or evaluation failure if a later failure replaced the outward error.</summary>
    public RuleEvaluationError? PriorError
    {
        get; init;
    }

    /// <summary>Gets established or uncertain Audit refusal when durable acceptance could not be confirmed.</summary>
    public CIS.Audit.AuditSubmissionOutcome? AuditFailure
    {
        get; init;
    }
}
