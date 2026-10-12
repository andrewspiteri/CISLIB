using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ExceptionContentProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Cases => ExceptionContentSources.Cases.Select(id => new object[] { id });

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task ActualContentExclusionAndLossCasesRunInUninitializedChildren(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        using var owned = new TemporaryDirectory();
        ProcessResult child = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "exception-content", id],
            owned.Path, TimeSpan.FromSeconds(15), new Dictionary<string, string?>
            {
                ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read",
                ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR",
                ["CIS_PRIVATE_EXCEPTION_TOKEN"] = ExceptionContentSources.PrivateSentinel
            });
        Assert.AreEqual(0, child.ExitCode, child.StandardError);
        Assert.AreEqual(string.Empty, child.StandardError);
        TestContext.WriteLine(child.StandardOutput);
        JsonObject actual = JsonNode.Parse(child.StandardOutput)!.AsObject();
        ExceptionContentExpectations.Verify(id, actual);
        JsonNode observed = actual["observed"]!;
        Assert.IsFalse(observed["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.IsFalse(child.StandardOutput.Contains(ExceptionContentSources.PrivateSentinel, StringComparison.Ordinal));
        Assert.IsFalse(child.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
