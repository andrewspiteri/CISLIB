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
        InitializationWorkingDirectory captured = InitializationWorkingDirectory.Capture(() =>
        {
            directoryReads++;
            return ConfigurationFixtureAdapter.Text(input, "workingDirectoryAtInitialization") ?? Directory.GetCurrentDirectory();
        });
        JsonObject discovery = input["discovery"]!.AsObject();
        ApplicationIdentity identity = ApplicationIdentityDiscovery.Discover(new ApplicationIdentityInput
        {
            ApplicationName = ConfigurationFixtureAdapter.Text(discovery, "applicationName"),
            BuildVersion = ConfigurationFixtureAdapter.Text(discovery, "buildVersion")
        });
        var source = new FixtureSource(input["environment"]!.AsObject());
        ConfigurationResolution selected = ConfigurationResolver.Resolve(ConfigurationFixtureAdapter.Setup(input), identity, CapturedEnvironment.Capture(source));
        ValidatedConfiguration? validated = null;
        IReadOnlyList<ConfigurationIssue> issues = [];
        string? errorMessage = null;
        try
        {
            validated = ConfigurationValidator.Validate(selected, captured);
        }
        catch (CoreConfigurationException exception)
        {
            issues = exception.Issues;
            errorMessage = exception.Message;
        }
        string? before = validated?.FilePath;
        Directory.SetCurrentDirectory(afterDirectory);
        string? after = validated?.FilePath;
        var result = new JsonObject
        {
            ["caseId"] = id,
            ["directorySource"] = input["workingDirectoryAtInitialization"] is null ? "actual-child-cwd" : "declared-fixture-base",
            ["initialActualDirectory"] = initialActualDirectory,
            ["afterActualDirectory"] = Directory.GetCurrentDirectory(),
            ["capturedDirectory"] = captured.Value,
            ["directoryReads"] = directoryReads,
            ["absolutePathBefore"] = before,
            ["absolutePathAfter"] = after,
            ["validatedWorkingDirectory"] = validated?.WorkingDirectory,
            ["issues"] = new JsonArray(issues.Select(issue => (JsonNode)new JsonObject { ["code"] = issue.Code, ["key"] = issue.Key, ["explanation"] = issue.Explanation }).ToArray()),
            ["errorMessage"] = errorMessage,
            ["observed"] = new JsonObject
            {
                ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "macos",
                ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
                ["fixtureSha256"] = Hash(typeof(ConfigurationPathProcessFixture).Assembly.Location),
                ["coreSha256"] = Hash(typeof(ConfigurationValidator).Assembly.Location),
                ["inputsSha256"] = Hash(inputPath),
                ["recognizedReads"] = new JsonArray(source.Reads.Select(name => (JsonNode)JsonValue.Create(name)!).ToArray())
            }
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class FixtureSource(JsonObject environment) : IEnvironmentVariableSource
    {
        public List<string> Reads { get; } = [];
        public string? GetValue(string name)
        {
            Reads.Add(name);
            return environment[name]?.GetValue<string>();
        }
    }
}
