using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CIS.RuleEngine;

internal static class RuleDefinitionWire
{
    // Measure the exact definition bytes, including escaped strings, typed presets, and raw metadata.
    internal static ReadOnlyMemory<byte> Encode(RuleDefinition definition)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = RuleBounds.MaximumJsonDepth
        });

        writer.WriteStartObject();
        writer.WriteString("rulesetId", definition.RulesetId);
        writer.WriteString("schemaRevision", definition.SchemaRevision);
        writer.WriteString("version", definition.Version);
        writer.WriteString("mode", WireName(definition.Mode));
        WriteOptionalText(writer, "displayName", definition.DisplayName);
        if (definition.MetadataShape is { } shape)
        {
            writer.WritePropertyName("metadataShape");
            WriteShape(writer, shape);
        }

        writer.WriteStartArray("columns");
        foreach (RuleColumn column in definition.Columns)
        {
            WriteColumn(writer, column);
        }
        writer.WriteEndArray();

        writer.WriteStartArray("rows");
        foreach (RuleRow row in definition.Rows)
        {
            WriteRow(writer, row);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        CheckSize(writer);
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static void WriteColumn(Utf8JsonWriter writer, RuleColumn column)
    {
        writer.WriteStartObject();
        writer.WriteString("id", column.Id);
        writer.WriteString("displayName", column.DisplayName);
        writer.WriteString("inputKind", WireName(column.InputKind));
        writer.WriteStartArray("allowedPresetKinds");
        foreach (RulePresetKind kind in column.AllowedPresetKinds)
        {
            writer.WriteStringValue(WireName(kind));
        }
        writer.WriteEndArray();
        if (column.MinimumUtf8Bytes is int minimumBytes)
        {
            writer.WriteNumber("minimumUtf8Bytes", minimumBytes);
        }
        if (column.MaximumUtf8Bytes is int maximumBytes)
        {
            writer.WriteNumber("maximumUtf8Bytes", maximumBytes);
        }
        if (column.ExactScalarCount is int exactScalars)
        {
            writer.WriteNumber("exactScalarCount", exactScalars);
        }
        if (column.MinimumDecimal is decimal minimumDecimal)
        {
            writer.WriteString("minimumDecimal", minimumDecimal.ToString(CultureInfo.InvariantCulture));
        }
        if (column.MaximumDecimal is decimal maximumDecimal)
        {
            writer.WriteString("maximumDecimal", maximumDecimal.ToString(CultureInfo.InvariantCulture));
        }
        writer.WriteEndObject();
        CheckSize(writer);
    }

    private static void WriteRow(Utf8JsonWriter writer, RuleRow row)
    {
        writer.WriteStartObject();
        writer.WriteString("ruleId", row.RuleId);
        writer.WriteNumber("ordinal", row.Ordinal);
        writer.WriteString("state", WireName(row.State));
        writer.WriteString("weight", row.WeightText);
        WriteOptionalText(writer, "comment", row.Comment);
        if (row.Metadata is { } metadata)
        {
            writer.WritePropertyName("metadata");
            writer.WriteRawValue(JsonMarshal.GetRawUtf8Value(metadata), skipInputValidation: true);
        }
        CheckSize(writer);

        writer.WriteStartArray("conditions");
        foreach (RuleCondition condition in row.Conditions)
        {
            writer.WriteStartObject();
            writer.WriteString("columnId", condition.ColumnId);
            writer.WriteString("presetKind", WireName(condition.PresetKind));
            if (condition.Preset is { } preset)
            {
                writer.WriteString("operator", WireName(condition.Operator));
                writer.WriteStartObject("preset");
                preset.WriteFields(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            CheckSize(writer);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteShape(Utf8JsonWriter writer, RuleMetadataShape shape)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", WireName(shape.Kind));
        writer.WriteBoolean("nullable", shape.Nullable);
        if (shape.Properties.Count > 0)
        {
            writer.WriteStartArray("properties");
            foreach (RuleMetadataProperty property in shape.Properties)
            {
                writer.WriteStartObject();
                writer.WriteString("name", property.Name);
                writer.WriteBoolean("required", property.Required);
                writer.WritePropertyName("shape");
                WriteShape(writer, property.Shape);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        if (shape.Element is { } element)
        {
            writer.WritePropertyName("element");
            WriteShape(writer, element);
        }
        writer.WriteEndObject();
        CheckSize(writer);
    }

    private static void WriteOptionalText(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static string WireName<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static void CheckSize(Utf8JsonWriter writer)
    {
        if (writer.BytesCommitted + writer.BytesPending > RuleBounds.MaximumDefinitionBytes)
        {
            throw new ArgumentException("The encoded ruleset definition exceeds its byte limit.", "definition");
        }
    }
}
