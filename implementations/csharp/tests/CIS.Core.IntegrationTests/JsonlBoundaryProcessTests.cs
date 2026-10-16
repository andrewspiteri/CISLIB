using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class JsonlBoundaryProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => JsonlBoundaryExpectations.Modes.Select(m => new object[] { m });

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ManagedJsonlInspectionAndStartupPreserveActualPhysicalBoundary(string mode)
    { using var root = new TemporaryDirectory(); await Run(mode, root.Path); }

    [TestMethod]
    [DataRow("status", "object")]
    [DataRow("position", "object")]
    [DataRow("identity-order", "wrong-format")]
    [DataRow("append", "object")]
    [DataRow("failed-growth", "invalid-json")]
    [DataRow("inventory", "object")]
    [DataRow("release", "object")]
    [DataRow("recovery-flag", "partial-json")]
    [DataRow("damaged-preimage", "partial-json")]
    [DataRow("damage-status", "partial-json")]
    [DataRow("restart-flag", "repeat")]
    [DataRow("accounting", "partial-utf8")]
    public async Task OriginalBoundaryReaderCorruptionsAreRejectedAfterPhysicalPositive(string key, string mode)
    {
        using var root = new TemporaryDirectory(); JsonObject report = await Run(mode, root.Path);
        switch (key)
        {
            case "status": report["status"] = "Partial"; break;
            case "position": report["restoredPosition"] = 4; break;
            case "identity-order": report["inspected"] = 1; break;
            case "append": report["after"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Bytes(report, "after")).Replace("boundary.append", "boundary.other", StringComparison.Ordinal))); break;
            case "failed-growth": report["after"] = Convert.ToBase64String([.. Bytes(report, "after"), .. Encoding.UTF8.GetBytes("{}\n")]); break;
            case "inventory": report["inventoryAfter"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")); break;
            case "release": report["finalReleased"] = false; break;
            case "recovery-flag": report["recovered"] = false; break;
            case "damaged-preimage": string name = report["filesystem"]!.AsObject().Select(p => p.Key).First(k => k.EndsWith(".damaged", StringComparison.Ordinal)); report["filesystem"]![name] = Convert.ToBase64String(Encoding.UTF8.GetBytes("changed")); break;
            case "damage-status":
                JsonObject inventory = JsonNode.Parse(Bytes(report, "inventoryAfter"))!.AsObject(); inventory["segments"]!.AsArray()[^2]!["state"] = "closed";
                report["inventoryAfter"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(inventory.ToJsonString() + "\n")); break;
            case "restart-flag": report["restartRecovered"] = true; break;
            case "accounting": report["phases"]![0]!["failed"] = 1L; break;
        }
        string reason = key switch
        {
            "status" => "boundary status/length",
            "position" => "stream position",
            "identity-order" => "identity before inspection",
            "append" => "literal policy record: event_name",
            "failed-growth" => "failed startup preserves bytes",
            "inventory" => "inventory unchanged",
            "release" => "actual inspection/release",
            "recovery-flag" or "restart-flag" => "actual append/recovery diagnostics",
            "damage-status" => "persistent damaged inventory",
            "accounting" => "current delivery accounting",
            _ => "exact physical transition"
        };
        Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(() => JsonlBoundaryExpectations.Validate(mode, report)).Message);
        TestContext.WriteLine("Rejected original boundary control: " + key);
    }

    private async Task<JsonObject> Run(string mode, string root)
    {
        (JsonObject report, _) = await FixtureProcessChecks.RunAsync(["jsonl-boundary", mode, root], root, TestContext,
            environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, timeout: TimeSpan.FromSeconds(30), flatReport: true);
        JsonlBoundaryExpectations.Validate(mode, report);
        string path = Path.Combine(root, "logs", "Ω-🙂.jsonl"), directory = Path.GetDirectoryName(path)!;
        Assert.AreEqual(path, (string?)report["path"]); CollectionAssert.AreEqual(Bytes(report, "after"), File.ReadAllBytes(path));
        var physical = new JsonObject();
        foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        { physical.Add(Path.GetRelativePath(directory, file).Replace('\\', '/'), Convert.ToBase64String(File.ReadAllBytes(file))); }
        Assert.IsTrue(JsonNode.DeepEquals(physical, report["filesystem"]));
        CollectionAssert.AreEqual(report["names"]!.AsArray().Select(p => (string)p!).ToArray(), Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        return report;
    }
    private static byte[] Bytes(JsonObject report, string key) => Convert.FromBase64String((string)report[key]!);
}
