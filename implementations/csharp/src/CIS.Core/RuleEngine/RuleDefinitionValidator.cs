using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CIS.Messaging;
using CIS.Text;

namespace CIS.RuleEngine;

/// <summary>Validates a complete RM11 version and orders its definition for execution.</summary>
public static class RuleDefinitionValidator
{
    /// <summary>Returns a detached version with its rows in ordinal order and cells in schema order.</summary>
    /// <exception cref="ArgumentException">The definition is malformed or exceeds the portable definition bounds.</exception>
    public static RuleDefinition Validate(RuleDefinition definition)
        => Prepare(definition).Definition;

    internal static PreparedRuleDefinition Prepare(RuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateId(definition.RulesetId, "Ruleset ID");
        ValidateId(definition.SchemaRevision, "Schema revision");
        ValidateId(definition.Version, "Version ID");
        ValidateOptionalText(definition.DisplayName, "Ruleset name", allowEmpty: false);
        Require(Enum.IsDefined(definition.Mode), "The ruleset execution mode is unsupported.");
        Require(definition.Columns.Count <= RuleBounds.MaximumColumns, "The ruleset has too many schema columns.");
        Require(definition.Rows.Count <= RuleBounds.MaximumRows, "The ruleset has too many rows.");
        Require(definition.Columns.Count != 0 || definition.Rows.Count == 0,
            "A row requires a schema column and an explicit condition.");

        var columnIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < definition.Columns.Count; index++)
        {
            RuleColumn column = definition.Columns[index];
            Require(column is not null, "A schema column is missing.");
            ValidateColumn(column);
            Require(columnIndices.TryAdd(column.Id, index), $"Duplicate column ID '{column.Id}'.");
        }

        if (definition.MetadataShape is { } metadataShape)
        {
            ValidateShape(metadataShape, 2, new(ReferenceEqualityComparer.Instance));
        }

        var ordinals = new HashSet<int>();
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<PreparedRuleRow>(definition.Rows.Count);
        foreach (RuleRow row in definition.Rows)
        {
            Require(row is not null, "A ruleset row is missing.");
            ValidateId(row.RuleId, "Rule ID");
            Require(ruleIds.Add(row.RuleId), $"Duplicate rule ID '{row.RuleId}'.");
            Require(row.Ordinal >= 0 && ordinals.Add(row.Ordinal), $"Rule '{row.RuleId}' has a negative or duplicate ordinal.");
            Require(Enum.IsDefined(row.State), $"Rule '{row.RuleId}' has an unsupported state.");
            ValidateOptionalText(row.Comment, "Rule comment", allowEmpty: true);
            ValidateMetadata(row, definition.MetadataShape);
            rows.Add(PrepareRow(row, definition.Columns, columnIndices));
        }

        PreparedRuleRow[] orderedRows = rows.OrderBy(row => row.Rule.Ordinal).ToArray();
        var normalized = new RuleDefinition(definition.RulesetId, definition.SchemaRevision, definition.Version,
            definition.Mode, definition.Columns, orderedRows.Select(row => row.Rule), definition.DisplayName,
            definition.MetadataShape);
        RuleDefinitionWire.Encode(normalized);
        return new(normalized, Array.AsReadOnly(orderedRows));
    }

    private static void ValidateColumn(RuleColumn column)
    {
        ValidateId(column.Id, "Column ID");
        ValidateText(column.DisplayName, RuleBounds.MaximumDefinitionBytes, "Column name", allowEmpty: false);
        Require(column.InputKind is RuleValueKind.String or RuleValueKind.Decimal or RuleValueKind.Boolean,
            $"Column '{column.Id}' has an unsupported input type.");
        var allowed = new HashSet<RulePresetKind>();
        foreach (RulePresetKind kind in column.AllowedPresetKinds)
        {
            Require(IsCompatible(column.InputKind, kind) && allowed.Add(kind),
                $"Column '{column.Id}' has an incompatible or repeated preset kind.");
        }

        bool hasStringBounds = column.MinimumUtf8Bytes.HasValue || column.MaximumUtf8Bytes.HasValue
            || column.ExactScalarCount.HasValue;
        bool hasDecimalBounds = column.MinimumDecimal.HasValue || column.MaximumDecimal.HasValue;
        Require(!hasStringBounds || column.InputKind == RuleValueKind.String,
            $"Column '{column.Id}' has string constraints on a non-string input.");
        Require(!hasDecimalBounds || column.InputKind == RuleValueKind.Decimal,
            $"Column '{column.Id}' has decimal constraints on a non-decimal input.");
        Require(column.MinimumUtf8Bytes is not (< 0 or > RuleBounds.MaximumStringBytes)
            && column.MaximumUtf8Bytes is not (< 0 or > RuleBounds.MaximumStringBytes)
            && column.ExactScalarCount is not (< 0 or > RuleBounds.MaximumStringBytes)
            && !(column.MinimumUtf8Bytes > column.MaximumUtf8Bytes),
            $"Column '{column.Id}' has invalid string bounds.");
        if (column.ExactScalarCount is int scalars)
        {
            Require(scalars <= (column.MaximumUtf8Bytes ?? RuleBounds.MaximumStringBytes)
                && scalars * 4 >= (column.MinimumUtf8Bytes ?? 0),
                $"Column '{column.Id}' has incompatible byte and scalar bounds.");
        }
        Require(!(column.MinimumDecimal > column.MaximumDecimal), $"Column '{column.Id}' has invalid decimal bounds.");
    }

    private static PreparedRuleRow PrepareRow(RuleRow row, IReadOnlyList<RuleColumn> columns,
        IReadOnlyDictionary<string, int> columnIndices)
    {
        Require(row.Conditions.Count == columns.Count, $"Rule '{row.RuleId}' needs one condition for each schema column.");
        var cells = new PreparedRuleCell[columns.Count];
        foreach (RuleCondition condition in row.Conditions)
        {
            Require(condition is not null, $"Rule '{row.RuleId}' has an empty condition.");
            ValidateId(condition.ColumnId, "Condition column ID");
            Require(columnIndices.TryGetValue(condition.ColumnId, out int index),
                $"Rule '{row.RuleId}' refers to an unknown column '{condition.ColumnId}'.");
            Require(cells[index] is null, $"Rule '{row.RuleId}' repeats column '{condition.ColumnId}'.");
            RuleColumn column = columns[index];
            cells[index] = new(column, condition, ValidateCondition(row.RuleId, column, condition));
        }

        var ordered = new RuleRow(row.RuleId, row.Ordinal, row.State, cells.Select(cell => cell.Condition),
            row.WeightText, row.Metadata, row.Comment);
        return new(ordered, Array.AsReadOnly(cells));
    }

    private static RuleWildcard? ValidateCondition(string ruleId, RuleColumn column, RuleCondition condition)
    {
        RuleValue? preset = condition.Preset;
        if (condition.PresetKind is RulePresetKind.Any or RulePresetKind.Missing)
        {
            Require(condition.Operator == RuleOperator.None && preset is null,
                $"Rule '{ruleId}', column '{column.Id}' has a marker with an operator or value.");
            return null;
        }

        Require(IsCompatible(column.InputKind, condition.PresetKind) && column.AllowedPresetKinds.Contains(condition.PresetKind),
            $"Rule '{ruleId}', column '{column.Id}' uses a preset kind its schema does not allow.");
        Require(IsOperatorSupported(condition.PresetKind, condition.Operator),
            $"Rule '{ruleId}', column '{column.Id}' uses an incompatible operator.");
        Require(preset is not null && IsPresetValue(condition.PresetKind, preset.Kind),
            $"Rule '{ruleId}', column '{column.Id}' has no compatible typed preset.");

        if (condition.Operator != RuleOperator.WildcardMatch)
        {
            return null;
        }
        try
        {
            return RuleWildcard.Compile(preset.StringValue);
        }
        catch (FormatException error)
        {
            throw new ArgumentException($"Rule '{ruleId}', column '{column.Id}' has an invalid wildcard pattern.",
                "definition", error);
        }
    }

    private static bool IsCompatible(RuleValueKind inputKind, RulePresetKind presetKind)
        => presetKind switch
        {
            RulePresetKind.Any or RulePresetKind.Missing => true,
            RulePresetKind.String or RulePresetKind.StringList => inputKind == RuleValueKind.String,
            RulePresetKind.Decimal => inputKind == RuleValueKind.Decimal,
            RulePresetKind.Boolean => inputKind == RuleValueKind.Boolean,
            _ => false
        };

    private static bool IsPresetValue(RulePresetKind preset, RuleValueKind value)
        => (preset, value) is (RulePresetKind.String, RuleValueKind.String)
            or (RulePresetKind.Decimal, RuleValueKind.Decimal)
            or (RulePresetKind.Boolean, RuleValueKind.Boolean)
            or (RulePresetKind.StringList, RuleValueKind.StringList);

    private static bool IsOperatorSupported(RulePresetKind kind, RuleOperator operation)
        => kind switch
        {
            RulePresetKind.String => operation is RuleOperator.Equals or RuleOperator.NotEquals or RuleOperator.WildcardMatch,
            RulePresetKind.Decimal => operation is RuleOperator.Equals or RuleOperator.NotEquals
                or RuleOperator.GreaterThan or RuleOperator.GreaterThanOrEqual or RuleOperator.LessThan or RuleOperator.LessThanOrEqual,
            RulePresetKind.Boolean => operation is RuleOperator.Equals or RuleOperator.NotEquals,
            RulePresetKind.StringList => operation is RuleOperator.IsIn or RuleOperator.IsNotIn,
            _ => false
        };

    private static void ValidateShape(RuleMetadataShape shape, int wireDepth,
        Dictionary<RuleMetadataShape, int> validatedDepths)
    {
        Require(wireDepth <= RuleBounds.MaximumJsonDepth, "The metadata shape exceeds the definition depth limit.");
        if (validatedDepths.TryGetValue(shape, out int validatedDepth) && validatedDepth >= wireDepth)
        {
            return;
        }
        validatedDepths[shape] = wireDepth;
        Require(Enum.IsDefined(shape.Kind), "Metadata shape kind is unsupported.");
        Require(shape.Kind == RuleMetadataKind.Object || shape.Properties.Count == 0,
            "Only a metadata object shape can declare properties.");
        Require((shape.Kind == RuleMetadataKind.Array) == (shape.Element is not null),
            "A metadata array shape needs exactly one element shape.");
        if (shape.Element is { } element)
        {
            ValidateShape(element, wireDepth + 1, validatedDepths);
        }

        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (RuleMetadataProperty property in shape.Properties)
        {
            Require(property is not null, "A metadata property definition is missing.");
            ValidateText(property.Name, RuleBounds.MaximumMetadataBytes, "Metadata property name", allowEmpty: true);
            Require(propertyNames.Add(property.Name), $"Duplicate metadata property '{property.Name}'.");
            Require(property.Shape is not null, $"Metadata property '{property.Name}' has no shape.");
            ValidateShape(property.Shape, wireDepth + 3, validatedDepths);
        }
    }

    private static void ValidateMetadata(RuleRow row, RuleMetadataShape? shape)
    {
        if (row.Metadata is not { } metadata)
        {
            Require(shape is null || shape.Nullable, $"Rule '{row.RuleId}' needs metadata matching its declared shape.");
            return;
        }
        Require(metadata.ValueKind != JsonValueKind.Undefined, $"Rule '{row.RuleId}' metadata is undefined.");
        ReadOnlySpan<byte> json = JsonMarshal.GetRawUtf8Value(metadata);
        Require(json.Length <= RuleBounds.MaximumMetadataBytes, $"Rule '{row.RuleId}' metadata exceeds the byte limit.");
        // Definition object, rows array, and rule object surround this raw value.
        Require(MessageWire.ValidateJson(json, RuleBounds.MaximumJsonDepth - 3),
            $"Rule '{row.RuleId}' metadata is not strict bounded JSON.");
        if (shape is not null)
        {
            Require(MatchesShape(metadata, shape), $"Rule '{row.RuleId}' metadata does not match its declared shape.");
        }
    }

    private static bool MatchesShape(JsonElement value, RuleMetadataShape shape)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return shape.Nullable;
        }
        if (shape.Kind == RuleMetadataKind.Object && value.ValueKind == JsonValueKind.Object)
        {
            foreach (RuleMetadataProperty property in shape.Properties)
            {
                if (value.TryGetProperty(property.Name, out JsonElement member))
                {
                    if (!MatchesShape(member, property.Shape))
                    {
                        return false;
                    }
                }
                else if (property.Required)
                {
                    return false;
                }
            }
            return true;
        }
        if (shape.Kind == RuleMetadataKind.Array && value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().All(item => MatchesShape(item, shape.Element!));
        }
        return (shape.Kind, value.ValueKind) is (RuleMetadataKind.String, JsonValueKind.String)
            or (RuleMetadataKind.Number, JsonValueKind.Number)
            or (RuleMetadataKind.Boolean, JsonValueKind.True or JsonValueKind.False);
    }

    private static void ValidateId(string id, string description)
        => Require(RuleBounds.IsId(id), $"{description} must be nonempty, bounded Unicode text without controls.");

    private static void ValidateOptionalText(string? text, string description, bool allowEmpty)
    {
        if (text is not null)
        {
            ValidateText(text, RuleBounds.MaximumDefinitionBytes, description, allowEmpty);
        }
    }

    private static void ValidateText(string? value, int maximumBytes, string description, bool allowEmpty)
    {
        Require(value is not null && (allowEmpty || value.Length != 0) && value.Length <= maximumBytes
            && UnicodeScalarText.IsValid(value, maximumBytes, allowControls: true) && Encoding.UTF8.GetByteCount(value) <= maximumBytes,
            $"{description} must be valid Unicode text within its byte limit.");
    }

    private static void Require([DoesNotReturnIf(false)] bool valid, string message)
    {
        if (!valid)
        {
            throw new ArgumentException(message, "definition");
        }
    }
}

internal sealed record PreparedRuleDefinition(RuleDefinition Definition, IReadOnlyList<PreparedRuleRow> Rows);

internal sealed record PreparedRuleRow(RuleRow Rule, IReadOnlyList<PreparedRuleCell> Cells);

internal sealed record PreparedRuleCell(RuleColumn Column, RuleCondition Condition, RuleWildcard? Wildcard);
