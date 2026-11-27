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
        => ComposeCore(binding, pair, null, null);

    internal static JsonNode ComposeCandidate(ManagementConfigurationBindingResource binding, ConfigurationSourcePair baseline,
        string selectedSource, string payloadJson)
    {
        if (selectedSource is not ("common" or "application"))
        {
            throw new ArgumentException("Select one actual source layer.", nameof(selectedSource));
        }
        return ComposeCore(binding, baseline, selectedSource, payloadJson);
    }

    private static JsonNode ComposeCore(ManagementConfigurationBindingResource binding, ConfigurationSourcePair pair,
        string? selectedSource, string? payloadJson)
    {
        using JsonDocument defaults = JsonDocument.Parse(binding.DefaultsJson);
        var templates = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [""] = defaults.RootElement };
        foreach (ManagementConfigurationObjectDefault template in binding.ObjectDefaults ?? [])
        {
            using JsonDocument document = JsonDocument.Parse(template.DefaultsJson);
            templates.Add(template.Path, document.RootElement.Clone());
        }
        using JsonDocument? common = selectedSource == "common" ? JsonDocument.Parse(payloadJson!)
            : pair.Common.State == "present" ? JsonDocument.Parse(pair.Common.PayloadJson!) : null;
        using JsonDocument? application = selectedSource == "application" ? JsonDocument.Parse(payloadJson!)
            : pair.Application.State == "present" ? JsonDocument.Parse(pair.Application.PayloadJson!) : null;
        var layers = new List<JsonElement> { defaults.RootElement };
        if (common is not null)
        {
            layers.Add(common.RootElement);
        }
        if (application is not null)
        {
            layers.Add(application.RootElement);
        }
        using var output = new CompositionOutput();
        using (var writer = new Utf8JsonWriter(output))
        {
            int nodes = 0;
            WriteMerged(writer, binding.Shape, layers, "", templates, 1, ref nodes);
        }
        byte[] bytes = output.ToArray();
        using JsonDocument effective = JsonDocument.Parse(bytes);
        if (!ManagementConfigurationStructure.ValidateComplete(binding.Shape, effective.RootElement).Valid)
        {
            throw Invalid();
        }
        return JsonNode.Parse(bytes)!;
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

    private static void WriteMerged(Utf8JsonWriter writer, ManagementConfigurationShape shape, IReadOnlyList<JsonElement> layers,
        string path, IReadOnlyDictionary<string, JsonElement> templates, int depth, ref int nodes)
    {
        if (++nodes > 65_536 || depth > 16)
        {
            throw Invalid();
        }
        JsonElement selected = layers[^1];
        if (selected.ValueKind == JsonValueKind.Null)
        {
            writer.WriteNullValue();
            return;
        }
        if (shape.Kind == "object")
        {
            JsonElement fallback = templates.TryGetValue(path, out JsonElement declared) ? declared : throw Invalid();
            int start = 0;
            for (int index = 0; index < layers.Count; index++)
            {
                if (layers[index].ValueKind == JsonValueKind.Null)
                {
                    start = index + 1;
                }
            }
            writer.WriteStartObject();
            foreach (ManagementConfigurationMember member in shape.Members!)
            {
                var children = new List<JsonElement>(layers.Count);
                if (start > 0 && fallback.TryGetProperty(member.Name, out JsonElement restored))
                {
                    children.Add(restored);
                }
                for (int index = start; index < layers.Count; index++)
                {
                    if (layers[index].TryGetProperty(member.Name, out JsonElement child))
                    {
                        children.Add(child);
                    }
                }
                if (children.Count == 0 && fallback.TryGetProperty(member.Name, out JsonElement initial))
                {
                    children.Add(initial);
                }
                if (children.Count == 0)
                {
                    continue;
                }
                writer.WritePropertyName(member.Name);
                WriteMerged(writer, member.Shape, children, ManagementConfigurationDefaultTemplates.MemberPath(path, member.Name),
                    templates, depth + 1, ref nodes);
            }
            writer.WriteEndObject();
            return;
        }
        if (shape.Kind == "array")
        {
            writer.WriteStartArray();
            foreach (JsonElement child in selected.EnumerateArray())
            {
                WriteMerged(writer, shape.Element!, [child], path + "/element", templates, depth + 1, ref nodes);
            }
            writer.WriteEndArray();
            return;
        }
        if (shape.Kind == "dictionary")
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in selected.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteMerged(writer, shape.Element!, [property.Value], path + "/element", templates, depth + 1, ref nodes);
            }
            writer.WriteEndObject();
            return;
        }
        selected.WriteTo(writer);
    }

    private static ConfigurationReferenceException Invalid() => new("management.configuration.structural_rejection");

    // Enforce expansion before buffering: a small replacement collection must not multiply a large default template without a bound.
    private sealed class CompositionOutput : MemoryStream
    {
        private const int MaximumBytes = 16 * 1024 * 1024;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Admit(buffer.Length);
            base.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Admit(count);
            base.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            Admit(1);
            base.WriteByte(value);
        }

        private void Admit(int bytes)
        {
            if (bytes > MaximumBytes - Position)
            {
                throw Invalid();
            }
        }
    }

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
