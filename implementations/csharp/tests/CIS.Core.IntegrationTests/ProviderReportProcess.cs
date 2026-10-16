using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

internal sealed record ProviderReportCapture(JsonObject Report, string ConsoleText, int ProcessId);

internal static class ProviderReportProcess
{
    internal static async Task<ProviderReportCapture> RunAsync(string family, string mode, TestContext context)
    {
        using var root = new TemporaryDirectory();
        var environment = System.Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CORE_", StringComparison.Ordinal)).ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        string core = Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll");
        string hostHash = Hash(host), coreHash = Hash(core);
        string[] arguments = ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, family, mode, root.Path];
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", arguments, root.Path, TimeSpan.FromSeconds(60), environment, new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardOutput + child.StandardError);
        if (family != "provider-host-filter") { Assert.AreEqual(0, child.StandardErrorBytes.Length); }
        JsonObject report = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(hostHash, (string?)report["fixtureSha256"]);
        Assert.AreEqual(coreHash, (string?)report["coreSha256"]);
        Assert.AreEqual(hostHash, Hash(host)); Assert.AreEqual(coreHash, Hash(core));
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", (string?)report["os"]);
        if (family == "provider-host-filter")
        {
            Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "Microsoft.Extensions.Logging.dll")), (string?)report["factorySha256"]);
            ProviderHostFilterExpectations.Validate(mode, report, child.StandardErrorBytes, child.ProcessId);
        }
        string file = Path.Combine(root.Path, "logs Ω", "active.log");
        bool exists = family != "provider-host-filter" || mode != "core-off";
        Assert.AreEqual(exists, File.Exists(file));
        if (family == "provider-host-filter") { Assert.AreEqual(exists, report["fileExists"]!.GetValue<bool>()); }
        byte[] actual = exists ? File.ReadAllBytes(file) : [];
        CollectionAssert.AreEqual(actual, Convert.FromBase64String(report[family == "provider-host-filter" ? "fileBytes" : "bytes"]!.GetValue<string>()));
        if (exists) { using var lease = new FileStream(file + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        context.WriteLine("Physical provider command: " + string.Join(" ", arguments));
        context.WriteLine("Physical child PID: " + child.ProcessId);
        context.WriteLine("Physical file SHA-256: " + Convert.ToHexStringLower(SHA256.HashData(actual)));
        context.WriteLine("Physical provider report: " + report.ToJsonString());
        if (family == "provider-host-filter") { context.WriteLine("Physical console bytes (base64): " + Convert.ToBase64String(child.StandardErrorBytes.Span)); }
        return new(report, child.StandardError, child.ProcessId);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
