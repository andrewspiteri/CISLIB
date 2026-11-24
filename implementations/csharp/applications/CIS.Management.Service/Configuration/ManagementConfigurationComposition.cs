using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Configuration;

// Finite declarative JSON composition for structural eligibility and payload-free preview only.
// It never materializes a consumer CLR type or runs its validation code.
internal static class ManagementConfigurationComposition
{
    internal static JsonNode Compose(ManagementConfigurationBindingResource binding, ConfigurationSourcePair pair)
    {
        using JsonDocument defaults = JsonDocument.Parse(binding.DefaultsJson);
        JsonNode result = Project(binding.Shape, defaults.RootElement)!;
        foreach (ManagementConfigurationSourceContent source in new[] { pair.Common, pair.Application })
        {
            if (source.State == "present")
            {
                using JsonDocument document = JsonDocument.Parse(source.PayloadJson!);
                result = Merge(binding.Shape, result, document.RootElement)!;
            }
        }
        if (!ManagementConfigurationStructure.ValidateComplete(binding.Shape, JsonSerializer.SerializeToElement(result)).Valid)
        {
            throw new ConfigurationReferenceException("management.configuration.structural_rejection");
        }
        return result;
    }

    internal static void Changes(ManagementConfigurationShape shape, JsonNode? before, JsonNode? after,
        string path, List<ManagementConfigurationChange> changes)
    {
        if (JsonNode.DeepEquals(before, after))
        {
            return;
        }
        if (shape.Kind == "object" && before is JsonObject previous && after is JsonObject current)
        {
            foreach (ManagementConfigurationMember member in shape.Members!)
            {
                string childPath = path + "/" + Escape(member.Name);
                if (previous.ContainsKey(member.Name) != current.ContainsKey(member.Name))
                {
                    Add(changes, childPath, current.ContainsKey(member.Name) ? "added" : "removed");
                }
                else
                {
                    Changes(member.Shape, previous[member.Name], current[member.Name], childPath, changes);
                }
            }
            return;
        }
        Add(changes, path, before is null ? "added" : after is null ? "removed" : "overridden");
    }

    internal static void Add(List<ManagementConfigurationChange> changes, string path, string kind)
    {
        if (changes.Count == 256 || Encoding.UTF8.GetByteCount(path) > 1024)
        {
            throw new ConfigurationReferenceException("management.configuration.preview_too_large");
        }
        changes.Add(new(path, kind));
    }

    private static JsonNode? Merge(ManagementConfigurationShape shape, JsonNode? lower, JsonElement upper)
    {
        if (shape.Kind != "object" || upper.ValueKind != JsonValueKind.Object)
        {
            return Project(shape, upper);
        }
        JsonObject result = lower as JsonObject ?? new();
        foreach (ManagementConfigurationMember member in shape.Members!)
        {
            if (upper.TryGetProperty(member.Name, out JsonElement supplied))
            {
                result[member.Name] = Merge(member.Shape, result[member.Name]?.DeepClone(), supplied);
            }
        }
        return result;
    }

    private static JsonNode? Project(ManagementConfigurationShape shape, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (shape.Kind == "object")
        {
            var result = new JsonObject();
            foreach (ManagementConfigurationMember member in shape.Members!)
            {
                if (value.TryGetProperty(member.Name, out JsonElement child))
                {
                    result.Add(member.Name, Project(member.Shape, child));
                }
            }
            return result;
        }
        if (shape.Kind == "array")
        {
            return new JsonArray(value.EnumerateArray().Select(child => Project(shape.Element!, child)).ToArray());
        }
        if (shape.Kind == "dictionary")
        {
            var result = new JsonObject();
            foreach (JsonProperty property in value.EnumerateObject())
            {
                result.Add(property.Name, Project(shape.Element!, property.Value));
            }
            return result;
        }
        return JsonNode.Parse(value.GetRawText());
    }

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
