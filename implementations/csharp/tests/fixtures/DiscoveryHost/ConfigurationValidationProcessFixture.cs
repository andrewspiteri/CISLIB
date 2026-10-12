using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Configuration;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConfigurationValidationProcessFixture
{
    public static int Run(string inputPath, string id, string afterDirectory)
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(ConfigurationFixtureAdapter.ReadDocument(inputPath), id);
        JsonObject input = row["inputs"]!.AsObject();
        string initialDirectory = Directory.GetCurrentDirectory();
        int directoryReads = 0;
        var source = new FixtureSource(input["environment"]!.AsObject());
        (PreparedConfiguration? snapshot, CoreConfigurationException? error) = ConfigurationValidationAdapter.Execute(input, () =>
        {
            directoryReads++;
            return Directory.GetCurrentDirectory();
        }, source.GetValue);
        source.BlockFurtherReads = true;
        JsonObject before = ConfigurationValidationAdapter.Project(snapshot, error, initialDirectory);
        Directory.SetCurrentDirectory(afterDirectory);
        JsonObject after = ConfigurationValidationAdapter.Project(snapshot, error, initialDirectory);
        bool stable = JsonNode.DeepEquals(before, after);
        after["observed"] = new JsonObject
        {
            ["caseId"] = id,
            ["initialActualDirectory"] = initialDirectory,
            ["afterActualDirectory"] = Directory.GetCurrentDirectory(),
            ["directoryReads"] = directoryReads,
            ["storedProjectionStable"] = stable,
            ["recognizedReads"] = new JsonArray(source.Reads.Select(name => (JsonNode)JsonValue.Create(name)!).ToArray()),
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "macos",
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["fixtureSha256"] = Hash(typeof(ConfigurationValidationProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(EnvironmentOptions).Assembly.Location),
            ["inputsSha256"] = Hash(inputPath)
        };
        Console.WriteLine(after.ToJsonString());
        return stable ? 0 : 1;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class FixtureSource(JsonObject environment)
    {
        public List<string> Reads { get; } = [];
        public bool BlockFurtherReads { get; set; }
        public string? GetValue(string name)
        {
            if (BlockFurtherReads) { throw new InvalidOperationException("Validation or stored reads queried the source again."); }
            Reads.Add(name);
            return environment[name]?.GetValue<string>();
        }
    }
}
