using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CIS.Tests.ArchitectureTests.Contracts;

// Converts portable tagged fixture expressions only, never arbitrary runtime objects.
internal sealed class ValueFixtureValidator
{
    private readonly JsonElement contract;
    private int nodes;

    internal ValueFixtureValidator(JsonElement contract) => this.contract = contract;

    internal JsonElement Normalize(JsonElement input)
    {
        nodes = 0;
        return JsonSerializer.SerializeToElement(ConvertValue(input, 1));
    }

    private object? ConvertValue(JsonElement input, int depth)
    {
        nodes++;
        if (nodes > contract.GetProperty("bounds").GetProperty("attributeNodesIncludingRoot").GetInt32() || depth > contract.GetProperty("bounds").GetProperty("attributeDepthIncludingRoot").GetInt32())
        {
            throw new InvalidDataException("Fixture value exceeds traversal limits.");
        }

        string kind = input.GetProperty("kind").GetString()!;
        JsonElement value = input.GetProperty("value");
        return kind switch
        {
            "null" when value.ValueKind == JsonValueKind.Null => null,
            "boolean" when value.ValueKind is JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
            "string" => Text(value),
            "integer" => Integer(value),
            "integer-string" or "decimal-string" => PatternText(value, contract.GetProperty("values").GetProperty(kind).GetProperty("pattern").GetString()!),
            "number" => Number(value),
            "uuid" => Uuid(value),
            "timestamp" => Timestamp(value),
            "array" when value.ValueKind == JsonValueKind.Array => value.EnumerateArray().Select(item => ConvertValue(item, depth + 1)).ToArray(),
            "map" when value.ValueKind == JsonValueKind.Object => Map(value, depth),
            _ => throw new InvalidDataException("Unsupported fixture value kind/type.")
        };
    }

    private static string Text(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || !EventRecordValidator.ValidUnicode(value.GetString()!))
        {
            throw new InvalidDataException("Expected valid Unicode text.");
        }

        return value.GetString()!;
    }

    private static string PatternText(JsonElement value, string pattern)
    {
        string text = Text(value);
        if (!Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new InvalidDataException("Invalid explicit numeric representation.");
        }

        return text;
    }

    private long Integer(JsonElement value)
    {
        string text = PatternText(value, contract.GetProperty("values").GetProperty("integer-string").GetProperty("pattern").GetString()!);
        BigInteger number = BigInteger.Parse(text, CultureInfo.InvariantCulture);
        JsonElement limits = contract.GetProperty("values").GetProperty("integer");
        if (number < BigInteger.Parse(limits.GetProperty("minimum").GetString()!, CultureInfo.InvariantCulture) || number > BigInteger.Parse(limits.GetProperty("maximum").GetString()!, CultureInfo.InvariantCulture))
        {
            throw new InvalidDataException("Integer outside safe range; explicit string selection required.");
        }

        return (long)number;
    }

    private static double Number(JsonElement value)
    {
        string text = PatternText(value, @"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$");
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
        {
            throw new InvalidDataException("Non-finite number.");
        }

        return number;
    }

    private static string Uuid(JsonElement value)
    {
        if (!Guid.TryParseExact(Text(value), "D", out Guid uuid))
        {
            throw new InvalidDataException("Invalid UUID.");
        }

        return uuid.ToString("D");
    }

    private static string Timestamp(JsonElement value)
    {
        string text = Text(value);
        string[] formats = { "yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'" };
        if (!DateTimeOffset.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset timestamp))
        {
            throw new InvalidDataException("Invalid instant.");
        }

        return timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    private Dictionary<string, object?> Map(JsonElement value, int depth)
    {
        var owned = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Name.Length == 0 || !EventRecordValidator.ValidUnicode(property.Name) || !EventRecordValidator.ControlFree(property.Name) || property.Name.EnumerateRunes().Count() > contract.GetProperty("bounds").GetProperty("attributeKeyScalars").GetInt32() || owned.ContainsKey(property.Name))
            {
                throw new InvalidDataException("Invalid map key.");
            }

            owned.Add(property.Name, ConvertValue(property.Value, depth + 1));
        }

        return owned;
    }
}
