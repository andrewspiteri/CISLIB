using System.Text;
using System.Text.Json;

namespace CIS.Tests.ArchitectureTests.Contracts;

// Pure deterministic sample comparison, not a producer or cumulative package replay harness.
internal sealed class WireSampleComparer
{
    private readonly JsonElement schema;
    private readonly EventRecordValidator validator;

    internal WireSampleComparer(JsonElement schema, JsonElement contract)
    {
        this.schema = schema;
        validator = new EventRecordValidator(schema, contract);
    }

    internal bool IsValid(byte[] bytes)
    {
        try
        {
            string wire = new UTF8Encoding(false, true).GetString(bytes);
            JsonElement fixture = JsonSerializer.SerializeToElement(new { mode = "reader", wire });
            return validator.Accepts(fixture);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    internal bool Preserves(byte[] baseline, byte[] candidate)
    {
        if (!IsValid(baseline) || !IsValid(candidate))
        {
            return false;
        }

        using JsonDocument expected = JsonDocument.Parse(baseline);
        using JsonDocument observed = JsonDocument.Parse(candidate);
        return SameKnownFields(schema, expected.RootElement, observed.RootElement);
    }

    private bool SameKnownFields(JsonElement node, JsonElement expected, JsonElement observed)
    {
        if (node.TryGetProperty("$ref", out JsonElement reference))
        {
            string name = reference.GetString()!["#/$defs/".Length..];
            return SameKnownFields(schema.GetProperty("$defs").GetProperty(name), expected, observed);
        }

        if (expected.ValueKind != JsonValueKind.Object || observed.ValueKind != JsonValueKind.Object || !node.TryGetProperty("properties", out JsonElement properties))
        {
            // Payload maps, arrays and scalar kinds remain exact; this is not a recursive ignore list.
            return JsonElement.DeepEquals(expected, observed);
        }

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            bool before = expected.TryGetProperty(property.Name, out JsonElement oldValue);
            bool after = observed.TryGetProperty(property.Name, out JsonElement newValue);
            if (before != after || before && !SameKnownFields(property.Value, oldValue, newValue))
            {
                return false;
            }
        }

        // Only protocol objects with the declared unknown-field policy can gain optional fields.
        return node.TryGetProperty("additionalProperties", out JsonElement additions) && additions.ValueKind == JsonValueKind.True
            || JsonElement.DeepEquals(expected, observed);
    }
}
