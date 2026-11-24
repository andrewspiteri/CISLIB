using System.Text;
using System.Text.Json;
using CIS.Management.Wire;

namespace CIS.Management.Service.Configuration;

// A finite data-shape check, not a JSON Schema engine or execution of consumer code.
internal static class ManagementConfigurationStructure
{
    internal static ManagementConfigurationValidationResult Validate(ManagementConfigurationShape shape, string payload,
        bool complete = false)
    {
        _ = ManagementConfigurationWire.ComputePayloadSha256(payload);
        using JsonDocument document = JsonDocument.Parse(payload);
        return ValidateComplete(shape, document.RootElement, complete);
    }

    internal static ManagementConfigurationValidationResult ValidateComplete(ManagementConfigurationShape shape, JsonElement value,
        bool complete = true)
    {
        var issues = new List<ManagementConfigurationIssue>();
        int nodes = 0;
        CheckBounds(value, 1, ref nodes, issues);
        if (issues.Count == 0)
        {
            Check(shape, value, complete, issues);
        }
        return new(issues.Count == 0, Array.AsReadOnly(issues.Take(64).ToArray()));
    }

    private static void CheckBounds(JsonElement value, int depth, ref int nodes, List<ManagementConfigurationIssue> issues)
    {
        if (++nodes > 65536 || depth > 16)
        {
            Add(issues, "configuration.structure.limit");
            return;
        }
        if (value.ValueKind == JsonValueKind.String && Encoding.UTF8.GetByteCount(value.GetString()!) > 64 * 1024)
        {
            Add(issues, "configuration.structure.scalar");
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > 4096)
            {
                Add(issues, "configuration.structure.collection");
                return;
            }
            foreach (JsonElement child in value.EnumerateArray())
            {
                CheckBounds(child, depth + 1, ref nodes, issues);
            }
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.EnumerateObject().Count() > 4096)
            {
                Add(issues, "configuration.structure.collection");
                return;
            }
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (Encoding.UTF8.GetByteCount(property.Name) > 64 * 1024)
                {
                    Add(issues, "configuration.structure.scalar");
                }
                CheckBounds(property.Value, depth + 1, ref nodes, issues);
            }
        }
    }

    private static void Check(ManagementConfigurationShape shape, JsonElement value, bool complete,
        List<ManagementConfigurationIssue> issues)
    {
        if (issues.Count >= 64)
        {
            return;
        }
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!shape.Nullable)
            {
                Add(issues, "configuration.structure.null");
            }
            return;
        }
        bool kind = shape.Kind switch
        {
            "object" or "dictionary" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number) && decimal.Truncate(number) == number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => false
        };
        if (!kind)
        {
            Add(issues, "configuration.structure.type");
            return;
        }
        if (shape.Kind == "object")
        {
            foreach (ManagementConfigurationMember member in shape.Members!)
            {
                if (value.TryGetProperty(member.Name, out JsonElement child))
                {
                    Check(member.Shape, child, complete, issues);
                }
                else if (complete && member.Required)
                {
                    Add(issues, "configuration.structure.required");
                }
            }
        }
        else if (shape.Kind == "dictionary")
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                Check(shape.Element!, property.Value, true, issues);
            }
        }
        else if (shape.Kind == "array")
        {
            foreach (JsonElement child in value.EnumerateArray())
            {
                Check(shape.Element!, child, true, issues);
            }
        }
        else if (shape.Kind == "string" && shape.AllowedValues is not null && !shape.AllowedValues.Contains(value.GetString(), StringComparer.Ordinal))
        {
            Add(issues, "configuration.structure.allowed");
        }
    }

    private static void Add(List<ManagementConfigurationIssue> issues, string code)
    {
        if (issues.Count < 64)
        {
            issues.Add(new(code));
        }
    }
}
