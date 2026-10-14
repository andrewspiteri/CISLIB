using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderHostFilterProcessTests
{
    public static IEnumerable<object[]> Modes => ProviderHostFilterExpectations.Modes.Select(mode => new object[] { mode });

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualFactoryAllowsIndependentCoreThresholds(string mode)
    {
        using var root = new TemporaryDirectory();
        var environment = System.Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CORE_", StringComparison.Ordinal)).ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "provider-host-filter", mode, root.Path],
            root.Path, TimeSpan.FromSeconds(60), environment, new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardOutput + child.StandardError);
        JsonObject report = JsonNode.Parse(child.StandardOutput)!.AsObject();
        string directory = Path.GetDirectoryName(host)!;
        Assert.AreEqual(Hash(host), report["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(directory, "CIS.Core.dll")), report["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(directory, "Microsoft.Extensions.Logging.dll")), report["factorySha256"]!.GetValue<string>());
        ProviderHostFilterExpectations.Validate(mode, report, child.StandardError);
        string path = Path.Combine(root.Path, "logs \u03a9", "active.log");
        Assert.AreEqual(File.Exists(path), report["fileExists"]!.GetValue<bool>());
        if (File.Exists(path)) { CollectionAssert.AreEqual(File.ReadAllBytes(path), Convert.FromBase64String(report["fileBytes"]!.GetValue<string>())); }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
