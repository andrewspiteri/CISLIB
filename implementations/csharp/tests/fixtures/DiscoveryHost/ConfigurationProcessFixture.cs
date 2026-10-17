using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Configuration;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConfigurationProcessFixture
{
    public static int Run(string path, string id)
    {
        byte[] inputBytes = File.ReadAllBytes(path);
        JsonObject row = ConfigurationFixtureAdapter.Case(JsonNode.Parse(inputBytes)!.AsObject(), id);
        JsonObject input = row["inputs"]!.AsObject();
        JsonNode hostBefore = input["host"]!.DeepClone();
        string? frameworkBefore = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var source = new RecordingSource();
        (PreparedConfiguration? snapshot, CoreConfigurationException? error) = ConfigurationValidationAdapter.Execute(input, Directory.GetCurrentDirectory, source.GetValue);
        var result = new JsonObject
        {
            ["caseId"] = id,
            ["configuration"] = ConfigurationValidationAdapter.Project(snapshot, error, Directory.GetCurrentDirectory()),
            ["hostBefore"] = hostBefore,
            ["hostAfter"] = input["host"]!.DeepClone(),
            ["frameworkVariableBefore"] = frameworkBefore,
            ["frameworkVariableAfter"] = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            ["observed"] = new JsonObject
            {
                ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown",
                ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
                ["outputRedirected"] = Console.IsOutputRedirected,
                ["errorRedirected"] = Console.IsErrorRedirected,
                ["debuggerAttached"] = Debugger.IsAttached,
                ["processId"] = System.Environment.ProcessId,
                ["fixtureSha256"] = Hash(typeof(ConfigurationProcessFixture).Assembly.Location),
                ["coreSha256"] = Hash(typeof(EnvironmentOptions).Assembly.Location),
                ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes)),
                ["recognizedReads"] = new JsonArray(source.Reads.Select(name => (JsonNode)JsonValue.Create(name)!).ToArray())
            }
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class RecordingSource
    {
        public List<string> Reads { get; } = [];
        public string? GetValue(string name)
        {
            Reads.Add(name);
            return System.Environment.GetEnvironmentVariable(name);
        }
    }
}
