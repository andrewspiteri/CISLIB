using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.CompatibilityTests.Healthcheck.HealthcheckCompatibilityInputs;

namespace CIS.Tests.CompatibilityTests.Healthcheck;

[TestClass]
public sealed class HealthcheckHistoricalInputCompatibilityTests
{
    private const string OriginalSchema = "contracts/healthcheck/v1/report.schema.json";
    private const string OriginalInventory = "conformance/healthcheck/common/fixtures.json";
    private static readonly string[] OriginalPaths =
    [
        OriginalSchema, OriginalInventory, "contracts/healthcheck/v1/README.md", "conformance/healthcheck/common/README.md"
    ];

    [TestMethod]
    public void CurrentAmendedInputsCoexistWithTheExactOriginalReceiptInputs()
    {
        using var copy = new HistoricalCopy();
        foreach (string original in OriginalPaths) { copy.CopyCanonical(original); }

        IReadOnlyDictionary<string, string> selected = HistoricalInputs(copy.Root);
        JsonArray frozen = Read(Path.Combine(Repository(), "planning/healthcheck/baseline.json"))["frozenInputs"]!["wireAndFixtures"]!.AsArray();
        foreach (string original in OriginalPaths)
        {
            string receiptHash = frozen.Single(item => item!["path"]!.GetValue<string>() == original)!["sha256"]!.GetValue<string>();
            Assert.AreEqual(receiptHash, Hash(selected[original]));
            Assert.AreNotEqual(receiptHash, Hash(Path.Combine(copy.Root, original)), "Current amended inputs must not replace their original expectations.");
        }
        Assert.AreEqual(4, selected.Count);
    }

    [TestMethod]
    [DataRow("changed-original")]
    [DataRow("missing-original-with-current-present")]
    [DataRow("redirected-map")]
    public void HistoricalSelectionRejectsCorruptionAndCannotFallBackToCurrentInputs(string change)
    {
        using var copy = new HistoricalCopy();
        string mirror = Path.Combine(copy.Directory, "report.schema.json");
        if (change == "changed-original")
        {
            // A harmless JSON whitespace edit is still a change to exact frozen bytes.
            File.AppendAllText(mirror, "\n");
        }
        else if (change == "missing-original-with-current-present")
        {
            string canonical = Path.Combine(copy.Root, OriginalSchema);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
            File.Copy(mirror, canonical);
            File.Delete(mirror);
        }
        else
        {
            copy.CopyCanonical(OriginalSchema);
            string mapPath = Path.Combine(copy.Directory, "path-map.json");
            JsonObject map = Read(mapPath);
            map["files"]![0]!["mirror"] = "../../../../contracts/healthcheck/v1/report.schema.json";
            File.WriteAllText(mapPath, map.ToJsonString());
        }

        Assert.ThrowsExactly<InvalidDataException>(() => HistoricalInputs(copy.Root));
    }

    private sealed class HistoricalCopy : IDisposable
    {
        internal HistoricalCopy()
        {
            Root = Path.Combine(Path.GetTempPath(), "cis-healthcheck-hc00-" + Guid.NewGuid().ToString("N"));
            Directory = Path.Combine(Root, HistoricalDirectory);
            System.IO.Directory.CreateDirectory(Directory);
            foreach (string filename in new[] { "path-map.json", "report.schema.json", "fixtures.json", "contract.README.md", "common.README.md" })
            {
                File.Copy(Path.Combine(Repository(), HistoricalDirectory, filename), Path.Combine(Directory, filename));
            }
        }

        internal string Root { get; }
        internal string Directory { get; }

        internal void CopyCanonical(string relative)
        {
            string destination = Path.Combine(Root, relative);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(Repository(), relative), destination);
        }

        public void Dispose() => System.IO.Directory.Delete(Root, recursive: true);
    }
}
