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
        InitializationWorkingDirectory captured = InitializationWorkingDirectory.Capture(() =>
        {
            directoryReads++;
            return Directory.GetCurrentDirectory();
        });
        var source = new FixtureSource(input["environment"]!.AsObject());
        ConfigurationResolution selected = ConfigurationResolver.Resolve(ConfigurationFixtureAdapter.Setup(input), ConfigurationValidationAdapter.Application(input), CapturedEnvironment.Capture(source));
        source.BlockFurtherReads = true;
        ValidatedConfiguration? validated = null;
        CoreConfigurationException? error = null;
        try
        {
            validated = ConfigurationValidator.Validate(selected, captured);
        }
        catch (CoreConfigurationException exception)
        {
            error = exception;
        }
        JsonObject before = ConfigurationValidationAdapter.Project(selected, validated, error);
        Directory.SetCurrentDirectory(afterDirectory);
        JsonObject after = ConfigurationValidationAdapter.Project(selected, validated, error);
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
            ["coreSha256"] = Hash(typeof(ConfigurationValidator).Assembly.Location),
            ["inputsSha256"] = Hash(inputPath)
        };
        Console.WriteLine(after.ToJsonString());
        return stable ? 0 : 1;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class FixtureSource(JsonObject environment) : IEnvironmentVariableSource
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
