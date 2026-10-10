using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CIS.Tests.ArchitectureTests.Contracts;

// Validates already projected fixture records. It never captures, truncates or writes a log.
internal sealed class EventRecordValidator
{
    private readonly FixtureSchemaValidator schema;
    private readonly JsonElement bounds;

    internal EventRecordValidator(JsonElement schema, JsonElement contract)
    {
        this.schema = new FixtureSchemaValidator(schema);
        bounds = contract.GetProperty("bounds");
    }

    internal bool Accepts(JsonElement fixture)
    {
        try
        {
            int maximumBytes = fixture.TryGetProperty("maxEntryBytes", out JsonElement configured) ? configured.GetInt32() : Limit("recordUtf8BytesIncludingLf");
            if (maximumBytes < Limit("recordMinimumConfiguredBytes") || maximumBytes > Limit("recordMaximumConfiguredBytes"))
            {
                return false;
            }

            bool producer = fixture.GetProperty("mode").GetString() == "producer";
            if (fixture.TryGetProperty("wire", out JsonElement raw))
            {
                string wire = raw.GetString()!;
                if (!wire.EndsWith('\n') || wire.Contains('\r') || wire[..^1].Contains('\n') || wire.StartsWith('\uFEFF') || Encoding.UTF8.GetByteCount(wire) > maximumBytes)
                {
                    return false;
                }

                using JsonDocument document = JsonDocument.Parse(wire, new JsonDocumentOptions { MaxDepth = 64 });
                return Validate(document.RootElement, producer);
            }

            JsonElement record = fixture.GetProperty("record");
            return Encoding.UTF8.GetByteCount(Compact(record.GetRawText())) + 1 <= maximumBytes && Validate(record, producer);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // JsonElement.GetString rejects an escaped unpaired UTF-16 surrogate.
            return false;
        }
    }

    private bool Validate(JsonElement record, bool producer)
    {
        if (!ValidJsonTree(record) || !schema.Accepts(record, producer))
        {
            return false;
        }

        foreach (string name in new[] { "service", "env", "version", "event_name", "component" })
        {
            if (!ControlFree(record.GetProperty(name).GetString()!))
            {
                return false;
            }
        }

        if (!UtcTimestamp(record.GetProperty("timestamp").GetString()!))
        {
            return false;
        }

        if (record.TryGetProperty("operation_id", out JsonElement operation))
        {
            string text = operation.GetString()!;
            if (text != text.Trim() || string.IsNullOrWhiteSpace(text) || !ControlFree(text))
            {
                return false;
            }
        }

        int nodes = 0;
        if (!ValidateAttributes(record.GetProperty("attributes"), 1, ref nodes) || !ValidateResource(record.GetProperty("resource")))
        {
            return false;
        }

        bool hasTrace = record.TryGetProperty("trace_id", out JsonElement trace);
        bool hasSpan = record.TryGetProperty("span_id", out JsonElement span);
        if (hasTrace != hasSpan || hasTrace && (trace.GetString()!.All(character => character == '0') || span.GetString()!.All(character => character == '0')))
        {
            return false;
        }

        nodes = 0;
        return !record.TryGetProperty("exception", out JsonElement exception) || ValidateException(exception, 1, ref nodes);
    }

    private static bool ValidateResource(JsonElement resource)
    {
        foreach (string name in new[] { "initialized_at", "process_started_at" })
        {
            if (resource.TryGetProperty(name, out JsonElement timestamp) && !UtcTimestamp(timestamp.GetString()!))
            {
                return false;
            }
        }

        foreach (string name in new[] { "build_version", "commit_id", "build_id", "host_name", "os_family" })
        {
            if (resource.TryGetProperty(name, out JsonElement identity) && !ControlFree(identity.GetString()!))
            {
                return false;
            }
        }

        return Guid.TryParseExact(resource.GetProperty("instance_id").GetString(), "D", out _);
    }

    private bool ValidateAttributes(JsonElement value, int depth, ref int nodes)
    {
        nodes++;
        if (nodes > Limit("attributeNodesIncludingRoot") || depth > Limit("attributeDepthIncludingRoot"))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name.Length == 0 || property.Name.EnumerateRunes().Count() > Limit("attributeKeyScalars") || !ControlFree(property.Name) || !ValidateAttributes(property.Value, depth + 1, ref nodes))
                {
                    return false;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (!ValidateAttributes(item, depth + 1, ref nodes))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool ValidateException(JsonElement exception, int depth, ref int nodes)
    {
        nodes++;
        if (depth > Limit("exceptionDepthIncludingRoot") || nodes > Limit("exceptionNodes"))
        {
            return false;
        }

        if (exception.TryGetProperty("inner_exceptions", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                if (!ValidateException(child, depth + 1, ref nodes))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private int Limit(string name) => bounds.GetProperty(name).GetInt32();

    internal static bool UtcTimestamp(string value) => DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);

    internal static bool ControlFree(string value) => value.EnumerateRunes().All(rune => Rune.GetUnicodeCategory(rune) != UnicodeCategory.Control);

    internal static bool ValidUnicode(string value)
    {
        for (int offset = 0; offset < value.Length;)
        {
            if (!Rune.TryGetRuneAt(value, offset, out Rune rune))
            {
                return false;
            }

            offset += rune.Utf16SequenceLength;
        }

        return true;
    }

    private static bool ValidJsonTree(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return ValidUnicode(value.GetString()!);
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name) || !ValidUnicode(property.Name) || !ValidJsonTree(property.Value))
                {
                    return false;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().All(ValidJsonTree);
        }
        else if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out double number) && double.IsFinite(number);
        }

        return true;
    }

    // Remove whitespace outside strings without changing the fixture's escaping choices.
    private static string Compact(string raw)
    {
        var result = new StringBuilder(raw.Length);
        bool inString = false;
        bool escaped = false;
        foreach (char character in raw)
        {
            if (inString || !char.IsWhiteSpace(character))
            {
                result.Append(character);
            }

            if (escaped)
            {
                escaped = false;
            }
            else if (inString && character == '\\')
            {
                escaped = true;
            }
            else if (character == '"')
            {
                inString = !inString;
            }
        }

        return result.ToString();
    }
}
