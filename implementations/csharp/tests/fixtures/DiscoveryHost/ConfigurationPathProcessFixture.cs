using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Configuration;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConfigurationPathProcessFixture
{
    public static int Run(string inputPath, string id, string afterDirectory)
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(ConfigurationFixtureAdapter.ReadDocument(inputPath), id);
        JsonObject input = row["inputs"]!.AsObject();
        string initialActualDirectory = Directory.GetCurrentDirectory();
        int directoryReads = 0;
        string captured = ConfigurationFixtureAdapter.Text(input, "workingDirectoryAtInitialization") ?? initialActualDirectory;
        var source = new FixtureSource(input["environment"]!.AsObject());
        (PreparedConfiguration? snapshot, CoreConfigurationException? error) = ConfigurationValidationAdapter.Execute(input, () =>
        {
            directoryReads++;
            return captured;
        }, source.GetValue);
        IReadOnlyList<ConfigurationIssue> issues = error?.Issues ?? [];
        string? errorMessage = error?.Message;
        string? before = snapshot?.Logging.FilePath;
        Directory.SetCurrentDirectory(afterDirectory);
        string? after = snapshot?.Logging.FilePath;
        var result = new JsonObject
        {
            ["caseId"] = id,
            ["directorySource"] = input["workingDirectoryAtInitialization"] is null ? "actual-child-cwd" : "declared-fixture-base",
            ["initialActualDirectory"] = initialActualDirectory,
            ["afterActualDirectory"] = Directory.GetCurrentDirectory(),
            ["capturedDirectory"] = captured,
            ["directoryReads"] = directoryReads,
            ["absolutePathBefore"] = before,
            ["absolutePathAfter"] = after,
            ["validatedWorkingDirectory"] = snapshot is null ? null : captured,
            ["issues"] = new JsonArray(issues.Select(issue => (JsonNode)new JsonObject { ["code"] = issue.Code, ["key"] = issue.Key, ["explanation"] = issue.Explanation }).ToArray()),
            ["errorMessage"] = errorMessage,
            ["observed"] = new JsonObject
            {
                ["processId"] = System.Environment.ProcessId,
                ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "macos",
                ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
                ["fixtureSha256"] = Hash(typeof(ConfigurationPathProcessFixture).Assembly.Location),
                ["coreSha256"] = Hash(typeof(EnvironmentOptions).Assembly.Location),
                ["inputsSha256"] = Hash(inputPath),
                ["recognizedReads"] = new JsonArray(source.Reads.Select(name => (JsonNode)JsonValue.Create(name)!).ToArray())
            }
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class FixtureSource(JsonObject environment)
    {
        public List<string> Reads { get; } = [];
        public string? GetValue(string name)
        {
            Reads.Add(name);
            return environment[name]?.GetValue<string>();
        }
    }
}
