using System.Text.Json.Nodes;
using CIS.Configuration;

namespace CIS.Tests.DiscoveryFixture;

// Nonshipping projection shared by fixture hosts and native tests.
internal static class ConfigurationValidationAdapter
{
    public static string FindFixture() => Path.Combine(Directory.GetParent(ConfigurationFixtureAdapter.FindFixture())!.FullName, "configuration-validation-cases.json");
    public static ApplicationIdentity Application(JsonObject input)
    {
        JsonNode discovery = input["discovery"]!;
        return new ApplicationIdentity(ConfigurationFixtureAdapter.Text(discovery, "applicationName"),
            ConfigurationFixtureAdapter.Text(discovery, "buildVersion") ?? "unknown",
            ConfigurationFixtureAdapter.Text(discovery, "commitId"), ConfigurationFixtureAdapter.Text(discovery, "buildId"));
    }

    public static JsonObject Project(ConfigurationResolution selected, ValidatedConfiguration? validated, CoreConfigurationException? error)
    {
        var result = new JsonObject
        {
            ["status"] = error is null ? "Valid" : "Invalid",
            ["issues"] = new JsonArray((error?.Issues ?? []).Select(issue => (JsonNode)new JsonObject
            {
                ["code"] = issue.Code,
                ["key"] = issue.Key,
                ["explanation"] = issue.Explanation
            }).ToArray()),
            ["message"] = error?.Message,
            ["exceptionText"] = error?.ToString(),
            ["filePath"] = validated?.FilePath,
            ["workingDirectory"] = validated?.WorkingDirectory,
            ["consoleEnabled"] = selected.ConsoleEnabled,
            ["fileEnabled"] = selected.FileEnabled,
            ["selected"] = new JsonObject(selected.Settings.Select(setting => new KeyValuePair<string, JsonNode?>(setting.Key, new JsonObject
            {
                ["state"] = setting.State.ToString(),
                ["source"] = setting.Source.ToString().ToLowerInvariant(),
                ["sourceKey"] = setting.SourceKey
            })))
        };
        if (validated is not null)
        {
            JsonArray settings = ConfigurationFixtureAdapter.Project(validated.Selected)["settings"]!.AsArray();
            result["values"] = new JsonObject
            {
                ["ApplicationName"] = settings[0]!["value"]!.DeepClone(),
                ["EnvironmentName"] = settings[1]!["value"]!.DeepClone(),
                ["ServiceVersion"] = settings[2]!["value"]!.DeepClone(),
                ["BuildVersion"] = selected.BuildVersion,
                ["CommitId"] = selected.CommitId,
                ["BuildId"] = selected.BuildId,
                ["EnvironmentKind"] = selected.EnvironmentKind.ToString()
            };
            foreach (JsonNode? setting in settings.Skip(3))
            {
                string key = setting!["key"]!.GetValue<string>();
                string property = ConfigurationSettings.Find(key).DiscoveryKey;
                if (key != "CORE_LOG_FILE_PATH")
                {
                    result["values"]![property] = setting["value"]?.DeepClone();
                }
            }
        }
        return result;
    }
}
