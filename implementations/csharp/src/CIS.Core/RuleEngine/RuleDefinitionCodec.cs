using System.Globalization;
using System.Text.Json;
using CIS.Messaging;

namespace CIS.RuleEngine;

/// <summary>Encodes and reads the strict, lossless RM11 version-one definition format.</summary>
public static class RuleDefinitionCodec
{
    /// <summary>Validates and encodes a definition in schema and ordinal order.</summary>
    /// <exception cref="ArgumentException">The definition is malformed or exceeds the portable limits.</exception>
    public static byte[] Encode(RuleDefinition definition)
        => RuleDefinitionWire.Encode(RuleDefinitionValidator.Validate(definition)).ToArray();

    /// <summary>Reads a bounded UTF-8 definition and returns detached, validated data.</summary>
    /// <exception cref="ArgumentException">The payload is malformed, unsupported, or exceeds the portable limits.</exception>
    public static RuleDefinition Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > RuleBounds.MaximumDefinitionBytes
            || !MessageWire.ValidateJson(payload, RuleBounds.MaximumJsonDepth))
        {
            throw Invalid("Use a bounded UTF-8 JSON definition with unique property names.");
        }

        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = RuleBounds.MaximumJsonDepth });
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return RuleDefinitionValidator.Validate(ReadDefinition(document.RootElement));
    }

    private static RuleDefinition ReadDefinition(JsonElement value)
    {
        CheckFields(value, "definition", "rulesetId", "schemaRevision", "version", "mode", "columns", "rows",
            "displayName", "metadataShape");
        return new(ReadString(Required(value, "rulesetId")), ReadString(Required(value, "schemaRevision")),
            ReadString(Required(value, "version")), ReadEnum<RuleExecutionMode>(Required(value, "mode")),
            ReadItems(Required(value, "columns"), ReadColumn), ReadItems(Required(value, "rows"), ReadRow),
            Optional(value, "displayName") is { } name ? ReadString(name) : null,
            Optional(value, "metadataShape") is { } shape ? ReadShape(shape) : null);
    }

    private static RuleColumn ReadColumn(JsonElement value)
    {
        CheckFields(value, "column", "id", "displayName", "inputKind", "allowedPresetKinds", "minimumUtf8Bytes",
            "maximumUtf8Bytes", "exactScalarCount", "minimumDecimal", "maximumDecimal");
        return new(ReadString(Required(value, "id")), ReadString(Required(value, "displayName")),
            ReadEnum<RuleValueKind>(Required(value, "inputKind")),
            ReadItems(Required(value, "allowedPresetKinds"), ReadEnum<RulePresetKind>),
            Optional(value, "minimumUtf8Bytes") is { } minBytes ? ReadInt32(minBytes) : null,
            Optional(value, "maximumUtf8Bytes") is { } maxBytes ? ReadInt32(maxBytes) : null,
            Optional(value, "exactScalarCount") is { } scalars ? ReadInt32(scalars) : null,
            Optional(value, "minimumDecimal") is { } minDecimal ? ReadDecimalBound(minDecimal) : null,
            Optional(value, "maximumDecimal") is { } maxDecimal ? ReadDecimalBound(maxDecimal) : null);
    }

    private static RuleRow ReadRow(JsonElement value)
    {
        CheckFields(value, "row", "ruleId", "ordinal", "state", "weight", "conditions", "metadata", "comment");
        return new(ReadString(Required(value, "ruleId")), ReadInt32(Required(value, "ordinal")),
            ReadEnum<RuleState>(Required(value, "state")), ReadItems(Required(value, "conditions"), ReadCondition),
            ReadString(Required(value, "weight")), Optional(value, "metadata"),
            Optional(value, "comment") is { } comment ? ReadString(comment) : null);
    }

    private static RuleCondition ReadCondition(JsonElement value)
    {
        CheckFields(value, "condition", "columnId", "presetKind", "operator", "preset");
        string columnId = ReadString(Required(value, "columnId"));
        RulePresetKind kind = ReadEnum<RulePresetKind>(Required(value, "presetKind"));
        if (kind is RulePresetKind.Any or RulePresetKind.Missing)
        {
            if (Optional(value, "operator").HasValue || Optional(value, "preset").HasValue)
            {
                throw Invalid("A marker cannot contain an operator or preset.");
            }
            return new(columnId, kind);
        }

        return new(columnId, kind, ReadEnum<RuleOperator>(Required(value, "operator")),
            ReadPreset(Required(value, "preset")));
    }

    private static RuleValue ReadPreset(JsonElement value)
    {
        CheckFields(value, "preset", "kind", "value");
        RuleValueKind kind = ReadEnum<RuleValueKind>(Required(value, "kind"));
        JsonElement item = Required(value, "value");
        return kind switch
        {
            RuleValueKind.String => RuleValue.FromString(ReadString(item)),
            RuleValueKind.Decimal => RuleValue.FromDecimal(ReadString(item)),
            RuleValueKind.Boolean => RuleValue.FromBoolean(ReadBoolean(item)),
            RuleValueKind.StringList => RuleValue.FromStringList(ReadItems(item, ReadString)),
            _ => throw Invalid("Use a supported typed preset.")
        };
    }

    private static RuleMetadataShape ReadShape(JsonElement value)
    {
        CheckFields(value, "metadata shape", "kind", "nullable", "properties", "element");
        return new(ReadEnum<RuleMetadataKind>(Required(value, "kind")), ReadBoolean(Required(value, "nullable")),
            Optional(value, "properties") is { } properties ? ReadItems(properties, ReadShapeProperty) : null,
            Optional(value, "element") is { } element ? ReadShape(element) : null);
    }

    private static RuleMetadataProperty ReadShapeProperty(JsonElement value)
    {
        CheckFields(value, "metadata property", "name", "required", "shape");
        return new(ReadString(Required(value, "name")), ReadBoolean(Required(value, "required")),
            ReadShape(Required(value, "shape")));
    }

    private static void CheckFields(JsonElement value, string kind, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"The {kind} must be a JSON object.");
        }
        foreach (JsonProperty field in value.EnumerateObject())
        {
            if (!names.Contains(field.Name, StringComparer.Ordinal))
            {
                throw Invalid($"The {kind} contains an unsupported field.");
            }
        }
    }

    private static JsonElement Required(JsonElement value, string name)
        => Optional(value, name) ?? throw Invalid($"The required field '{name}' is missing.");

    private static JsonElement? Optional(JsonElement value, string name)
        => value.TryGetProperty(name, out JsonElement field) ? field : null;

    private static IEnumerable<T> ReadItems<T>(JsonElement value, Func<JsonElement, T> read)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("Use a JSON array for a definition collection.");
        }
        foreach (JsonElement item in value.EnumerateArray())
        {
            yield return read(item);
        }
    }

    private static string ReadString(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString()!
            : throw Invalid("Use a JSON string for this definition field.");

    private static int ReadInt32(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number
            : throw Invalid("Use a JSON Int32 for this definition field.");

    private static bool ReadBoolean(JsonElement value)
        => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean()
            : throw Invalid("Use a native JSON Boolean for this definition field.");

    private static decimal ReadDecimalBound(JsonElement value)
    {
        string text = ReadString(value);
        decimal exact = RuleValue.FromDecimal(text).DecimalValue;
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        // The exact parser establishes representability. Preserve native scale when the CLR can do so without changing value.
        return decimal.TryParse(text, style, CultureInfo.InvariantCulture, out decimal withScale) && withScale == exact
            ? withScale : exact;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement value) where TEnum : struct, Enum
    {
        string name = ReadString(value);
        if (name.Length != 0 && Enum.TryParse(char.ToUpperInvariant(name[0]) + name[1..], out TEnum result)
            && Enum.IsDefined(result))
        {
            string declared = result.ToString();
            if (string.Equals(name, char.ToLowerInvariant(declared[0]) + declared[1..], StringComparison.Ordinal))
            {
                return result;
            }
        }
        throw Invalid("Use a supported case-sensitive definition value.");
    }

    private static ArgumentException Invalid(string message) => new(message, "payload");
}
