using System.Text.Json;
using System.Text.RegularExpressions;

namespace CIS.Tests.ArchitectureTests.Contracts;

// Deliberately limited, development-only evaluator for the keywords in our contract schema.
// It fails closed on unknown schema keywords; it is not a general JSON Schema implementation.
internal sealed class FixtureSchemaValidator
{
    private static readonly HashSet<string> SupportedKeywords = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "title", "description", "$defs", "$ref", "type", "required",
        "properties", "additionalProperties", "items", "anyOf", "enum", "const",
        "minLength", "maxLength", "pattern", "minimum", "maximum"
    };

    private readonly JsonElement root;

    internal FixtureSchemaValidator(JsonElement schema)
    {
        CheckKeywords(schema);
        root = schema;
    }

    internal bool Accepts(JsonElement value, bool producer) => Evaluate(root, value, producer);

    private static void CheckKeywords(JsonElement schema)
    {
        foreach (JsonProperty property in schema.EnumerateObject())
        {
            if (!SupportedKeywords.Contains(property.Name))
            {
                throw new NotSupportedException($"Unsupported contract schema keyword: {property.Name}");
            }

            if (property.Name is "properties" or "$defs")
            {
                foreach (JsonProperty child in property.Value.EnumerateObject())
                {
                    CheckKeywords(child.Value);
                }
            }
            else if (property.Name is "items" || property.Name == "additionalProperties" && property.Value.ValueKind == JsonValueKind.Object)
            {
                CheckKeywords(property.Value);
            }
            else if (property.Name == "anyOf")
            {
                foreach (JsonElement child in property.Value.EnumerateArray())
                {
                    CheckKeywords(child);
                }
            }
        }
    }

    private bool Evaluate(JsonElement schema, JsonElement value, bool producer)
    {
        if (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            string path = reference.GetString()!;
            if (!path.StartsWith("#/$defs/", StringComparison.Ordinal))
            {
                throw new NotSupportedException("Only local $defs references are supported.");
            }

            if (!Evaluate(root.GetProperty("$defs").GetProperty(path[8..]), value, producer))
            {
                return false;
            }
        }

        if (schema.TryGetProperty("anyOf", out JsonElement choices) && !choices.EnumerateArray().Any(choice => Evaluate(choice, value, producer)))
        {
            return false;
        }

        if (schema.TryGetProperty("type", out JsonElement type) && !MatchesType(type.GetString()!, value))
        {
            return false;
        }

        if (schema.TryGetProperty("const", out JsonElement constant) && !JsonElement.DeepEquals(value, constant))
        {
            return false;
        }

        if (schema.TryGetProperty("enum", out JsonElement members) && !members.EnumerateArray().Any(member => JsonElement.DeepEquals(value, member)))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Object => EvaluateObject(schema, value, producer),
            JsonValueKind.Array => !schema.TryGetProperty("items", out JsonElement items) || value.EnumerateArray().All(item => Evaluate(items, item, producer)),
            JsonValueKind.String => EvaluateString(schema, value.GetString()!),
            JsonValueKind.Number => EvaluateNumber(schema, value),
            _ => true
        };
    }

    private bool EvaluateObject(JsonElement schema, JsonElement value, bool producer)
    {
        if (schema.TryGetProperty("required", out JsonElement required) && required.EnumerateArray().Any(name => !value.TryGetProperty(name.GetString()!, out _)))
        {
            return false;
        }

        bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties);
        bool hasAdditional = schema.TryGetProperty("additionalProperties", out JsonElement additional);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (hasProperties && properties.TryGetProperty(property.Name, out JsonElement child))
            {
                if (!Evaluate(child, property.Value, producer))
                {
                    return false;
                }
            }
            else if (hasAdditional && additional.ValueKind == JsonValueKind.Object)
            {
                if (!Evaluate(additional, property.Value, producer))
                {
                    return false;
                }
            }
            else if (producer && hasProperties || hasAdditional && additional.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateString(JsonElement schema, string text)
    {
        int scalars = text.EnumerateRunes().Count();
        return (!schema.TryGetProperty("minLength", out JsonElement minimum) || scalars >= minimum.GetInt32())
            && (!schema.TryGetProperty("maxLength", out JsonElement maximum) || scalars <= maximum.GetInt32())
            && (!schema.TryGetProperty("pattern", out JsonElement pattern) || Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
    }

    private static bool EvaluateNumber(JsonElement schema, JsonElement value)
    {
        return value.TryGetDouble(out double number) && double.IsFinite(number)
            && (!schema.TryGetProperty("minimum", out JsonElement minimum) || number >= minimum.GetDouble())
            && (!schema.TryGetProperty("maximum", out JsonElement maximum) || number <= maximum.GetDouble());
    }

    private static bool MatchesType(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number) && number == Math.Truncate(number),
        _ => throw new NotSupportedException($"Unsupported schema type: {type}")
    };
}
