using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CIS.Verification;

// Only the vocabulary used by the two retained publication schemas. Unknown assertions fail closed.
internal static class PublicationSchema
{
    internal static void Validate(JsonElement value, JsonElement schema)
    {
        CheckVocabulary(schema);
        if (!Matches(value, schema, schema)) { throw new InvalidDataException("Document does not match its publication schema."); }
    }

    private static void CheckVocabulary(JsonElement schema)
    {
        foreach (JsonProperty property in schema.EnumerateObject())
        {
            switch (property.Name)
            {
                case "$schema": case "title": case "description": break;
                case "type":
                    if (property.Value.GetString() is not ("object" or "array" or "string" or "integer" or "null"))
                    { throw new InvalidDataException("Unsupported publication schema type."); }
                    break;
                case "$ref":
                    if (property.Value.GetString() != "#/$defs/artifact") { throw new InvalidDataException("Unsupported publication schema reference."); }
                    break;
                case "additionalProperties":
                    if (property.Value.ValueKind != JsonValueKind.False) { throw new InvalidDataException("Unsupported publication schema property policy."); }
                    break;
                case "properties":
                case "$defs":
                    foreach (JsonProperty child in property.Value.EnumerateObject()) { CheckVocabulary(child.Value); }
                    break;
                case "items": CheckVocabulary(property.Value); break;
                case "anyOf": foreach (JsonElement child in property.Value.EnumerateArray()) { CheckVocabulary(child); } break;
                case "required": case "minItems": case "minLength": case "minimum": case "maximum": case "pattern": case "const": case "enum": break;
                default: throw new InvalidDataException("Unsupported publication schema keyword: " + property.Name);
            }
        }
    }

    private static bool Matches(JsonElement value, JsonElement schema, JsonElement root)
    {
        foreach (JsonProperty rule in schema.EnumerateObject())
        {
            JsonElement constraint = rule.Value;
            switch (rule.Name)
            {
                case "$ref":
                    if (!Matches(value, root.GetProperty("$defs").GetProperty("artifact"), root)) { return false; }
                    break;
                case "type":
                    bool typed = constraint.GetString() switch
                    {
                        "object" => value.ValueKind == JsonValueKind.Object,
                        "array" => value.ValueKind == JsonValueKind.Array,
                        "string" => value.ValueKind == JsonValueKind.String,
                        "null" => value.ValueKind == JsonValueKind.Null,
                        "integer" => value.ValueKind == JsonValueKind.Number && IsInteger(value.GetRawText()),
                        _ => false
                    };
                    if (!typed) { return false; }
                    break;
                case "const": if (!JsonElement.DeepEquals(value, constraint)) { return false; } break;
                case "enum": if (!constraint.EnumerateArray().Any(item => JsonElement.DeepEquals(value, item))) { return false; } break;
                case "anyOf": if (!constraint.EnumerateArray().Any(item => Matches(value, item, root))) { return false; } break;
                case "required" when value.ValueKind == JsonValueKind.Object:
                    if (constraint.EnumerateArray().Any(name => !value.TryGetProperty(name.GetString()!, out _))) { return false; }
                    break;
                case "additionalProperties" when value.ValueKind == JsonValueKind.Object:
                    if (value.EnumerateObject().Any(property => !schema.GetProperty("properties").TryGetProperty(property.Name, out _))) { return false; }
                    break;
                case "properties" when value.ValueKind == JsonValueKind.Object:
                    foreach (JsonProperty property in constraint.EnumerateObject())
                    { if (value.TryGetProperty(property.Name, out JsonElement child) && !Matches(child, property.Value, root)) { return false; } }
                    break;
                case "items" when value.ValueKind == JsonValueKind.Array:
                    if (value.EnumerateArray().Any(item => !Matches(item, constraint, root))) { return false; }
                    break;
                case "minItems" when value.ValueKind == JsonValueKind.Array:
                    if (value.GetArrayLength() < constraint.GetInt32()) { return false; }
                    break;
                case "minLength" when value.ValueKind == JsonValueKind.String:
                    if (value.GetString()!.EnumerateRunes().Count() < constraint.GetInt32()) { return false; }
                    break;
                case "pattern" when value.ValueKind == JsonValueKind.String:
                    if (!Regex.IsMatch(value.GetString()!, constraint.GetString()!, RegexOptions.ECMAScript | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) { return false; }
                    break;
                case "minimum" when value.ValueKind == JsonValueKind.Number:
                    if (value.GetDouble() < constraint.GetDouble()) { return false; }
                    break;
                case "maximum" when value.ValueKind == JsonValueKind.Number:
                    if (value.GetDouble() > constraint.GetDouble()) { return false; }
                    break;
            }
        }
        return true;
    }
    // JSON Schema integers include exponent/fraction spellings such as 1e0 and 1.0.
    // Inspect decimal digits directly: floating-point rounding must not turn fractions into integers.
    private static bool IsInteger(string raw)
    {
        int exponentAt = raw.IndexOfAny(['e', 'E']);
        string mantissa = exponentAt < 0 ? raw : raw[..exponentAt];
        string digits = mantissa.Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('-');
        if (digits.All(character => character == '0')) { return true; }
        long exponent = 0;
        if (exponentAt >= 0 && !long.TryParse(raw.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
        { return raw[exponentAt + 1] != '-'; }
        int dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        long fractionalDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
        if (exponent >= fractionalDigits) { return true; }
        int trailingZeroes = digits.Length - digits.TrimEnd('0').Length;
        return exponent >= fractionalDigits - trailingZeroes;
    }
}
