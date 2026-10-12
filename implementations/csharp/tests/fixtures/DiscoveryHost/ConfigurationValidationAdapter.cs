using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

// Nonshipping caller orchestration for existing vectors; each module's real checks run explicitly.
internal static class ConfigurationValidationAdapter
{
    public static string FindFixture() => Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "passive-configuration-validation-cases.json");
    public static (PreparedConfiguration?, CoreConfigurationException?) Execute(JsonObject input, Func<string> directory, Func<string, string?>? read = null)
    {
        ConfigurationFixtureAdapter.Assign(input);
        string baseDirectory = directory();
        var issues = new List<ConfigurationIssue>();
        try { ConfigurationFixtureAdapter.ApplyOverrides(read ?? (key => input["environment"]?[key]?.GetValue<string>())); }
        catch (CoreConfigurationException error) { issues.AddRange(error.Issues); }
        try { ConfigurationFixtureAdapter.Facts.Logging.Prepare(baseDirectory); }
        catch (CoreConfigurationException error) { issues.AddRange(error.Issues); }
        EventMetadata? metadata = null;
        try { metadata = ConfigurationFixtureAdapter.Facts.CaptureMetadata(); }
        catch (CoreConfigurationException error) { issues.AddRange(error.Issues); }
        if (issues.Count != 0) { return (null, new CoreConfigurationException(issues)); }
        return (new PreparedConfiguration(metadata!, ConfigurationFixtureAdapter.Facts.Logging), null);
    }
    public static JsonObject Project(PreparedConfiguration? result, CoreConfigurationException? error, string directory)
    {
        JsonObject projection = new()
        {
            ["status"] = error is null ? "Valid" : "Invalid",
            ["issues"] = new JsonArray((error?.Issues ?? []).Select(issue => (JsonNode)new JsonObject { ["code"] = issue.Code, ["key"] = issue.Key, ["explanation"] = issue.Explanation }).ToArray()),
            ["message"] = error?.Message,
            ["exceptionText"] = error?.ToString(),
            ["filePath"] = result?.Logging.FilePath,
            ["workingDirectory"] = result is null ? null : directory,
            ["consoleEnabled"] = result?.Logging.ConsoleEnabled,
            ["fileEnabled"] = result?.Logging.FileEnabled
        };
        if (result is not null)
        {
            projection["values"] = ConfigurationFixtureAdapter.Values(result);
        }
        return projection;
    }
}
