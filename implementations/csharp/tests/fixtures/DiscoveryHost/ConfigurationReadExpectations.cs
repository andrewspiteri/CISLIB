using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

// Independent contract-based expectation; this is nonshipping test code.
internal static class ConfigurationReadExpectations
{
    internal static string[] Expected(JsonObject input)
    {
        JsonObject environment = input["environment"] as JsonObject ?? input;
        var names = new List<string>();
        foreach (JsonNode? setting in Catalog()["settings"]!.AsArray())
        {
            string key = setting!["key"]!.GetValue<string>();
            names.Add(key);
            if (string.IsNullOrWhiteSpace(environment[key]?.GetValue<string>()))
            {
                foreach (JsonNode? alias in setting["aliases"]?.AsArray() ?? [])
                {
                    names.Add(alias!.GetValue<string>());
                }
            }
        }
        return names.ToArray();
    }

    internal static JsonObject Catalog()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "contracts", "logging", "v1", "configuration.json");
            if (File.Exists(path))
            {
                return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find the independent configuration contract.");
    }
}
