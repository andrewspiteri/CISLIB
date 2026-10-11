using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

// Typed constructor inputs and a test-only wire projection; never a shipping serializer.
internal static class LogValueFixtureAdapter
{
    // Original T01 selection stays stable; T02 qualifies the remaining original rows separately.
    internal static readonly string[] Kinds = ["null", "boolean", "string", "number", "integer", "integer-string", "decimal-string", "array", "map"];

    public static JsonObject Execute(JsonObject row)
    {
        int factoryCalls = 0;
        LogValue? value = null;
        string? error = null;
        string? parameter = null;
        try
        {
            value = Construct(row["input"]!.AsObject(), () => factoryCalls++);
        }
        catch (ArgumentException failure)
        {
            error = failure.GetType().Name;
            parameter = failure.ParamName;
        }
        catch (InvalidDataException)
        {
            error = "InputAdapterTypeMismatch";
        }
        return new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["valid"] = value is not null,
            ["kind"] = value?.Kind.ToString(),
            ["isTruncated"] = value?.IsTruncated,
            ["json"] = value is null ? null : Project(value),
            ["error"] = error,
            ["parameter"] = parameter,
            ["factoryCalls"] = factoryCalls
        };
    }

    public static LogValue Construct(JsonObject input, Action beforeFactory)
    {
        string kind = input["kind"]!.GetValue<string>();
        JsonNode? value = input["value"];
        switch (kind)
        {
            case "null" when value is null:
                beforeFactory();
                return LogValue.Null();
            case "boolean" when value is JsonValue boolean && boolean.TryGetValue<bool>(out bool flag):
                beforeFactory();
                return LogValue.Boolean(flag);
            case "string":
                string text = Text(value);
                beforeFactory();
                return LogValue.String(text);
            case "integer":
                long integer = long.Parse(Text(value), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                beforeFactory();
                return LogValue.Integer(integer);
            case "integer-string":
                string integerText = Text(value);
                beforeFactory();
                return LogValue.IntegerString(integerText);
            case "decimal-string":
                string decimalText = Text(value);
                beforeFactory();
                return LogValue.DecimalString(decimalText);
            case "number":
                double number = double.Parse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture);
                beforeFactory();
                return LogValue.Number(number);
            case "uuid":
                if (!Guid.TryParseExact(Text(value), "D", out Guid uuid))
                {
                    throw new InvalidDataException("Expected Guid constructor transport.");
                }
                beforeFactory();
                return LogValue.Uuid(uuid);
            case "timestamp":
                if (!DateTimeOffset.TryParse(Text(value), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset timestamp))
                {
                    throw new InvalidDataException("Expected DateTimeOffset constructor transport.");
                }
                beforeFactory();
                return LogValue.Timestamp(timestamp);
            case "array" when value is JsonArray items:
                LogValue[] array = items.Select(item => Construct(item!.AsObject(), beforeFactory)).ToArray();
                beforeFactory();
                return LogValue.Array(array);
            case "map" when value is JsonObject entries:
                var map = entries.ToDictionary(pair => pair.Key, pair => Construct(pair.Value!.AsObject(), beforeFactory), StringComparer.Ordinal);
                beforeFactory();
                return LogValue.Map(map);
            default:
                throw new InvalidDataException("Constructor input is not a supported typed value.");
        }
    }

    public static JsonNode? Project(LogValue value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, value);
        }
        return JsonNode.Parse(stream.ToArray());
    }

    public static void Validate(JsonObject row, JsonObject actual)
    {
        bool expected = row["expectedValid"]!.GetValue<bool>();
        if (actual["valid"]!.GetValue<bool>() != expected ||
            expected && !JsonNode.DeepEquals(row["expectedJson"], actual["json"]))
        {
            throw new InvalidDataException("Typed Core value differs from the portable expectation.");
        }
        if (expected)
        {
            string expectedKind = row["input"]!["kind"]!.GetValue<string>() switch
            {
                "null" => "Null",
                "boolean" => "Boolean",
                "string" => "String",
                "number" => "Number",
                "integer" => "Integer",
                "integer-string" => "IntegerString",
                "decimal-string" => "DecimalString",
                "uuid" => "Uuid",
                "timestamp" => "Timestamp",
                "array" => "Array",
                "map" => "Map",
                _ => throw new InvalidDataException("Unexpected supported tag.")
            };
            if (actual["kind"]!.GetValue<string>() != expectedKind)
            {
                throw new InvalidDataException("Explicit value kind was changed.");
            }
        }
        if (expected && actual["isTruncated"]!.GetValue<bool>())
        {
            throw new InvalidDataException("A complete scalar fixture was unexpectedly truncated.");
        }
    }

    private static void Write(Utf8JsonWriter writer, LogValue value)
    {
        switch (value.Kind)
        {
            case LogValueKind.Null:
                writer.WriteNullValue();
                break;
            case LogValueKind.Boolean:
                writer.WriteBooleanValue(value.Scalar.Boolean);
                break;
            case LogValueKind.String:
            case LogValueKind.IntegerString:
            case LogValueKind.DecimalString:
            case LogValueKind.Uuid:
            case LogValueKind.Timestamp:
                writer.WriteStringValue(value.Scalar.Text);
                break;
            case LogValueKind.Integer:
                writer.WriteNumberValue(value.Scalar.Integer);
                break;
            case LogValueKind.Number:
                writer.WriteNumberValue(value.Scalar.Number);
                break;
            case LogValueKind.Array:
                writer.WriteStartArray();
                foreach (LogValue child in value.ArrayItems)
                {
                    Write(writer, child);
                }
                writer.WriteEndArray();
                break;
            case LogValueKind.Map:
                writer.WriteStartObject();
                foreach ((string key, LogValue child) in value.MapProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    Write(writer, child);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new InvalidDataException("Value is outside the supported value model.");
        }
    }

    private static string Text(JsonNode? value)
    {
        if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out string? text) || text is null)
        {
            throw new InvalidDataException("Expected explicit string constructor transport.");
        }
        return text;
    }
}
