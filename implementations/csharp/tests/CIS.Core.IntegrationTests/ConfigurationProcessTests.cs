using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConfigurationProcessTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("CFG-RESOLVE-DEFAULTS")]
    [DataRow("CFG-RESOLVE-ENVIRONMENT-ALL")]
    [DataRow("CFG-RESOLVE-DD-ALIASES")]
    [DataRow("CFG-RESOLVE-INDEPENDENT-LEVELS")]
    [DataRow("CFG-RESOLVE-GLOBAL-NONE-OWN-LEVELS")]
    [DataRow("CFG-RESOLVE-BOTH-NONE")]
    [DataRow("CFG-RESOLVE-TELEMETRY-CORE")]
    [DataRow("CFG-RESOLVE-TELEMETRY-DD")]
    public async Task ChildConfigurationMatchesSharedInputsWithoutChangingHostLabel(string id)
    {
        JsonObject document = JsonNode.Parse(File.ReadAllText(FindInputs()))!.AsObject();
        JsonObject row = document["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject input = row["inputs"]!.AsObject();
        var environment = new Dictionary<string, string?>();
        foreach (JsonNode? setting in row["expected"]!["settings"]!.AsArray()) { environment[setting!["key"]!.GetValue<string>()] = null; }
        foreach (string alias in new[] { "DD_SERVICE", "DD_ENV", "DD_VERSION" }) { environment[alias] = null; }
        foreach ((string name, JsonNode? value) in input["environment"]!.AsObject()) { environment[name] = value?.GetValue<string>(); }
        string? hostLabel = input["host"]!["environmentName"]?.GetValue<string>();
        environment["ASPNETCORE_ENVIRONMENT"] = hostLabel;
        environment["CORE_PASSWORD"] = "unknown-child-fixture-sentinel";
        string?[] before = environment.Keys.Select(Environment.GetEnvironmentVariable).ToArray();
        using var directory = new TemporaryDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", FixturePath, "configuration", FindInputs(), id], directory.Path, TimeSpan.FromSeconds(15), environment);
        Assert.AreEqual(0, process.ExitCode, process.StandardOutput + process.StandardError);
        Assert.AreEqual(string.Empty, process.StandardError);
        TestContext.WriteLine(process.StandardOutput);
        JsonObject result = JsonNode.Parse(process.StandardOutput)!.AsObject();
        Assert.IsTrue(JsonNode.DeepEquals(row["expected"], result["configuration"]), process.StandardOutput);
        Assert.IsTrue(JsonNode.DeepEquals(input["host"], result["hostBefore"]));
        Assert.IsTrue(JsonNode.DeepEquals(result["hostBefore"], result["hostAfter"]));
        Assert.AreEqual(hostLabel, result["frameworkVariableBefore"]?.GetValue<string>());
        Assert.AreEqual(hostLabel, result["frameworkVariableAfter"]?.GetValue<string>());
        JsonObject observed = result["observed"]!.AsObject();
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "macos", observed["operatingSystem"]!.GetValue<string>());
        Assert.AreEqual(Hash(FixturePath), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(FixturePath)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(FindInputs()), observed["inputsSha256"]!.GetValue<string>());
        string[] reads = observed["recognizedReads"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.AreEqual(21, reads.Length);
        Assert.AreEqual(21, reads.Distinct(StringComparer.Ordinal).Count());
        Assert.IsFalse(reads.Contains("ASPNETCORE_ENVIRONMENT", StringComparer.Ordinal));
        Assert.IsFalse(reads.Contains("CORE_PASSWORD", StringComparer.Ordinal));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(directory.Path).Length, "Selection created filesystem outputs.");
        CollectionAssert.AreEqual(before, environment.Keys.Select(Environment.GetEnvironmentVariable).ToArray());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string FindInputs()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "logging", "configuration-resolution-cases.json");
            if (File.Exists(path)) { return path; }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find configuration inputs.");
    }
}
