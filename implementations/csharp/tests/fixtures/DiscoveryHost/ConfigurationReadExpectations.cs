using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

// Independent contract-based expectation; this is nonshipping test code.
internal static class ConfigurationReadExpectations
{
    private sealed record Setting(string Key, IReadOnlyList<string> Aliases);
    private static readonly Lazy<IReadOnlyList<Setting>> Settings = new(() => Array.AsReadOnly(Catalog()["settings"]!.AsArray()
        .Select(setting => new Setting(setting!["key"]!.GetValue<string>(),
            Array.AsReadOnly((setting["aliases"]?.AsArray() ?? []).Select(alias => alias!.GetValue<string>()).ToArray())))
        .ToArray()));

    internal static string[] Expected(JsonObject input)
    {
        JsonObject environment = input["environment"] as JsonObject ?? input;
        var names = new List<string>();
        foreach (Setting setting in Settings.Value)
        {
            string key = setting.Key;
            names.Add(key);
            if (string.IsNullOrWhiteSpace(environment[key]?.GetValue<string>()))
            {
                foreach (string alias in setting.Aliases)
                {
                    names.Add(alias);
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
