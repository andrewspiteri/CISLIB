using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileTransitionProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => FileTransitionExpectations.Modes.Select(mode => new object[] { mode });

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualProcessResumesKnownStagesOrFailsUnchanged(string mode)
    { using var root = new TemporaryDirectory(); await Run(mode, root.Path); }

    [TestMethod]
    [DataRow("composition")]
    [DataRow("release")]
    [DataRow("intent")]
    [DataRow("code")]
    [DataRow("failure-growth")]
    [DataRow("flag")]
    [DataRow("restart-flag")]
    [DataRow("accounting")]
    [DataRow("record")]
    [DataRow("inventory")]
    [DataRow("archive")]
    [DataRow("neighbor")]
    public async Task OriginalTransitionReaderCorruptionsAreRejectedAfterPhysicalPositive(string key)
    {
        string mode = key is "code" or "failure-growth" ? "ambiguous-both" : "recovery-renamed";
        using var root = new TemporaryDirectory(); JsonObject report = await Run(mode, root.Path); const string active = "logs/Ω-🙂.log";
        void Edit(JsonNode container, string field, Action<JsonObject> mutate)
        { JsonObject value = JsonNode.Parse(Convert.FromBase64String((string)container[field]!))!.AsObject(); mutate(value); container[field] = Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToJsonString() + "\n")); }
        switch (key)
        {
            case "composition": report["composition"] = "other"; break;
            case "release": report["leaseReleased"] = false; break;
            case "intent": Edit(report["before"]!, active + ".cis.transition.json", value => value["originalSha256"] = new string('0', 64)); break;
            case "code": report["error"]!["code"] = "Other"; break;
            case "failure-growth": report["after"]![active] = Convert.ToBase64String(Encoding.UTF8.GetBytes("changed")); break;
            case "flag": report["recovered"] = false; break;
            case "restart-flag": report["restartRecovered"] = true; break;
            case "accounting": report["final"]!["written"] = 0L; break;
            case "record": Edit(report["after"]!, active, value => value["event_name"] = "other"); break;
            case "inventory": Edit(report["after"]!, active + ".cis.inventory.json", value => value["segments"]!.AsArray()[^1]!["id"] = 99); break;
            case "archive": string name = report["after"]!.AsObject().Select(p => p.Key).Single(k => k.EndsWith("00000000000000000002.damaged", StringComparison.Ordinal)); report["after"]![name] = Convert.ToBase64String(Encoding.UTF8.GetBytes("changed")); break;
            case "neighbor": report["after"]!["neighbor.bin"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("changed")); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => FileTransitionExpectations.Validate(mode, report)); TestContext.WriteLine("Rejected original transition control: " + key);
    }

    private async Task<JsonObject> Run(string mode, string root)
    {
        (JsonObject report, _) = await FixtureProcessChecks.RunAsync(["file-transition", mode, root], root, TestContext,
            environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, timeout: TimeSpan.FromSeconds(30), flatReport: true);
        FileTransitionExpectations.Validate(mode, report);
        Assert.AreEqual(Path.Combine(root, "logs", "Ω-🙂.log"), (string?)report["path"]);
        var physical = new JsonObject();
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        { physical.Add(Path.GetRelativePath(root, file).Replace('\\', '/'), Convert.ToBase64String(File.ReadAllBytes(file))); }
        Assert.IsTrue(JsonNode.DeepEquals(physical, report["after"])); return report;
    }
}
