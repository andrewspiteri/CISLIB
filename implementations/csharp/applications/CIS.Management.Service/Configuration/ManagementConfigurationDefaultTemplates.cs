using System.Text;
using System.Text.Json;
using CIS.Management.Wire;

namespace CIS.Management.Service.Configuration;

// Only trusted finite data is retained here. Consumer constructors and parent-instance values cannot supply these facts.
internal static class ManagementConfigurationDefaultTemplates
{
    internal static IReadOnlyList<ManagementConfigurationObjectDefault>? Capture(ManagementConfigurationShape root,
        IReadOnlyList<ManagementConfigurationObjectDefault>? supplied)
    {
        var required = new Dictionary<string, ManagementConfigurationShape>(StringComparer.Ordinal);
        Collect(root, "", required);
        if (supplied is { Count: > 4096 })
        {
            throw Invalid("At most 4096 nested object default templates may be declared.");
        }
        var captured = new SortedDictionary<string, ManagementConfigurationObjectDefault>(StringComparer.Ordinal);
        foreach (ManagementConfigurationObjectDefault template in supplied ?? [])
        {
            if (template is null || template.Path is null || template.DefaultsJson is null
                || !required.TryGetValue(template.Path, out ManagementConfigurationShape? shape)
                || captured.ContainsKey(template.Path))
            {
                throw Invalid("Object default paths must identify unique declared nested object shapes.");
            }
            try
            {
                _ = ManagementConfigurationWire.ComputePayloadSha256(template.DefaultsJson);
                using JsonDocument document = JsonDocument.Parse(template.DefaultsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !ManagementConfigurationStructure.ValidateComplete(shape, document.RootElement).Valid
                    || !AllMembersPresent(shape, document.RootElement))
                {
                    throw Invalid("Each nested object default template must supply complete valid declared member data.");
                }
            }
            catch (Exception error) when (error is JsonException or ArgumentException)
            {
                throw new ArgumentException("Each nested object default template must be strict bounded complete object JSON.", nameof(supplied), error);
            }
            captured.Add(template.Path, new(template.Path, template.DefaultsJson));
        }
        if (captured.Count != required.Count)
        {
            throw Invalid("Supply ObjectDefaults for every nested object shape, including nullable objects and array/dictionary elements; root defaults cannot establish declared child defaults.");
        }
        return captured.Count == 0 ? null : Array.AsReadOnly(captured.Values.ToArray());
    }

    internal static string MemberPath(string parent, string name)
        => parent + "/members/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static void Collect(ManagementConfigurationShape shape, string path,
        Dictionary<string, ManagementConfigurationShape> required)
    {
        if (shape.Kind == "object")
        {
            if (path.Length != 0 && Encoding.UTF8.GetByteCount(path) > 16 * 1024)
            {
                throw Invalid("An object default structural path exceeds 16 KiB of UTF-8.");
            }
            if (path.Length != 0 && (!required.TryAdd(path, shape) || required.Count > 4096))
            {
                throw Invalid("The finite nested object default declaration is too large or ambiguous.");
            }
            foreach (ManagementConfigurationMember member in shape.Members!)
            {
                Collect(member.Shape, MemberPath(path, member.Name), required);
            }
        }
        else if (shape.Kind is "array" or "dictionary")
        {
            Collect(shape.Element!, path + "/element", required);
        }
    }

    private static bool AllMembersPresent(ManagementConfigurationShape shape, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (shape.Kind == "object")
        {
            return shape.Members!.All(member => value.TryGetProperty(member.Name, out JsonElement child)
                && AllMembersPresent(member.Shape, child));
        }
        if (shape.Kind == "array")
        {
            return value.EnumerateArray().All(child => AllMembersPresent(shape.Element!, child));
        }
        return shape.Kind != "dictionary" || value.EnumerateObject().All(property => AllMembersPresent(shape.Element!, property.Value));
    }

    private static ArgumentException Invalid(string message) => new(message, "objectDefaults");
}
