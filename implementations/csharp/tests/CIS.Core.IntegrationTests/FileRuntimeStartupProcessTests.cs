using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileRuntimeStartupProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => FileRuntimeStartupExpectations.Modes.Select(mode => new object[] { mode });

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ConfiguredFileAndConsoleStartupInFreshConsumer(string mode)
    {
        using var root = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync(["file-runtime-startup", mode, root.Path], root.Path, TestContext,
            environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null },
            timeout: TimeSpan.FromSeconds(30), physicalStreams: mode == "physical-console");
        byte[]? stderr = mode == "physical-console" ? Convert.FromBase64String((string)actual["stderr"]!) : null;
        if (mode == "physical-console") { Assert.AreEqual("", (string?)actual["stdout"]); }
        FileRuntimeStartupExpectations.Validate(mode, actual, stderr);
        string path = Path.Combine(root.Path, "logs", "Ω-🙂.jsonl");
        Assert.AreEqual(File.Exists(path), actual["fileExists"]!.GetValue<bool>());
        Assert.AreEqual(File.Exists(path + ".cis.inventory.json"), actual["inventoryExists"]!.GetValue<bool>());
        CollectionAssert.AreEqual(File.Exists(path) ? File.ReadAllBytes(path) : [], Convert.FromBase64String((string)actual["fileBytes"]!));
        if (mode == "rollback-format")
        {
            string[] retained = Directory.GetFiles(Path.GetDirectoryName(path)!, "*.closed"); Assert.AreEqual(1, retained.Length);
            Assert.AreEqual("retained-must-survive\n", File.ReadAllText(retained[0]));
        }
        if (mode == "rollback-parent") { Assert.AreEqual("keep-parent", File.ReadAllText(Path.Combine(root.Path, "logs"))); }
        if (mode == "rollback-meta-directory") { Assert.AreEqual("keep", File.ReadAllText(Path.Combine(path + ".cis.inventory.json", "keep.txt"))); }
    }
}
