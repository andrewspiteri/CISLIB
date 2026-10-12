using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EventPrecedenceProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read("event-precedence-cases.json")["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task SharedPrecedenceRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        string input = LogValueProcessTests.Find("event-precedence-cases.json");
        using var owned = new TemporaryDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "event-precedence", input, id],
            owned.Path, TimeSpan.FromSeconds(30), new Dictionary<string, string?>
            {
                ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read",
                ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR",
                ["CIS_PRIVATE_PROJECTION_TOKEN"] = "CIS_NEVER_CAPTURE_PROJECTION"
            });
        Assert.AreEqual(0, process.ExitCode, process.StandardError); Assert.AreEqual(string.Empty, process.StandardError);
        TestContext.WriteLine(process.StandardOutput);
        JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject();
        JsonObject catalog = LogValueProcessTests.Read("event-precedence-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject semantic = actual.DeepClone().AsObject(); semantic.Remove("observed");
        EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), semantic);
        JsonNode observed = actual["observed"]!;
        Assert.AreEqual(RuntimeInformation.FrameworkDescription, observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown", observed["operatingSystem"]!.GetValue<string>());
        Assert.IsFalse(observed["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(input), observed["inputsSha256"]!.GetValue<string>());
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_PROJECTION", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
