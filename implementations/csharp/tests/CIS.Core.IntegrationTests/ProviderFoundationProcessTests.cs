using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ProviderFoundationProcessTests
{
    public static IEnumerable<object[]> Modes => ProviderFoundationExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task MicrosoftProviderUsesTheActualPhysicalRuntime(string mode)
    {
        using var root = new TemporaryDirectory();
        var environment = System.Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CORE_", StringComparison.Ordinal)).ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "provider-foundation", mode, root.Path],
            root.Path, TimeSpan.FromSeconds(60), environment, new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual("", child.StandardError);
        JsonObject report = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(Hash(host), report["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), report["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", report["os"]!.GetValue<string>());
        ProviderFoundationExpectations.Validate(mode, report, child.ProcessId, physical: true);
        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(root.Path, "logs Ω", "active.log")), Convert.FromBase64String(report["bytes"]!.GetValue<string>()));
        using var lease = new FileStream(Path.Combine(root.Path, "logs Ω", "active.log.cis.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        JsonObject changedPid = report.DeepClone().AsObject(); changedPid["pid"] = child.ProcessId + 1;
        Assert.AreEqual("actual child PID", Assert.ThrowsExactly<InvalidDataException>(() => ProviderFoundationExpectations.Validate(mode, changedPid, child.ProcessId, physical: true)).Message);
        if (Convert.FromBase64String(report["bytes"]!.GetValue<string>()).Length > 0)
        {
            JsonObject changedFacts = report.DeepClone().AsObject();
            string[] lines = Encoding.UTF8.GetString(Convert.FromBase64String(changedFacts["bytes"]!.GetValue<string>()))[..^1].Split('\n');
            JsonObject row = JsonNode.Parse(lines[0])!.AsObject(); row["resource"]!["process_id"] = 0;
            lines[0] = row.ToJsonString(); changedFacts["bytes"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"));
            Assert.AreEqual("automatic core facts", Assert.ThrowsExactly<InvalidDataException>(() => ProviderFoundationExpectations.Validate(mode, changedFacts, child.ProcessId, physical: true)).Message);
        }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
