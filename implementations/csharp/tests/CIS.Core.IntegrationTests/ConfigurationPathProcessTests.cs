using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConfigurationPathProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> SharedCases => PathRows().Where(Applies).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    public void ConfigurationCatalogMembershipIsComplete()
    {
        _ = FixtureProcessChecks.Cases("configuration-resolution-cases.json", 26);
        _ = FixtureProcessChecks.Cases("passive-configuration-validation-cases.json", 24);
        Assert.AreEqual(14, PathRows().Length);
        Assert.AreEqual(13, PathRows().Count(Applies));
        string[] names = ConfigurationReadExpectations.Expected(new JsonObject());
        Assert.AreEqual(21, names.Length);
        Assert.AreEqual(21, names.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    [DynamicData(nameof(SharedCases))]
    public async Task ChildCwdChangeCannotRedirectValidatedPathOrCreateOutputs(string id)
    {
        string inputs = FixtureProcessChecks.Find("configuration-cases.json");
        JsonObject row = PathRows().Single(row => (string?)row["id"] == id);
        await CheckPathAsync(inputs, row);
    }

    [TestMethod]
    [DataRow("absolute")]
    [DataRow("captured-base")]
    [DataRow("marker-collision")]
    public async Task ExistingAliasAndLiteralTildesRemainLexicalInActualChildProcesses(string mode)
    {
        using var owned = new TemporaryDirectory();
        string alias;
        if (OperatingSystem.IsWindows())
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var buffer = new StringBuilder(32768);
            uint size = GetShortPathNameW(programFiles, buffer, (uint)buffer.Capacity);
            Assert.IsTrue(size > 0 && size < buffer.Capacity, "The Windows lane requires an existing 8.3 alias.");
            alias = buffer.ToString();
            size = GetLongPathNameW(alias, buffer, (uint)buffer.Capacity);
            Assert.IsTrue(size > 0 && size < buffer.Capacity);
            Assert.IsTrue(alias.Contains('~') && Directory.Exists(alias));
            Assert.AreNotEqual(alias, buffer.ToString());
            TestContext.WriteLine("Harness-confirmed existing short alias: " + alias + "; expanded: " + buffer);
        }
        else
        {
            alias = Path.Combine(owned.Path, "existing~base Ω");
            Directory.CreateDirectory(alias);
            TestContext.WriteLine("Linux literal-tilde directory: " + alias + "; no Windows alias claim.");
        }
        string leaf = "cislib-path-never-created-" + Guid.NewGuid().ToString("N");
        string suffix = mode == "marker-collision" ? "\ue000-\ue001-~log.jsonl" : "log.jsonl";
        string expected = Path.Combine(alias, leaf, suffix);
        string input = mode == "absolute" ? expected : leaf + "/" + suffix;
        JsonObject row = PathRows().First(row => (string?)row["expected"]!["status"] != "Invalid").DeepClone().AsObject();
        row["id"] = "PATH-TILDE-" + mode;
        row["inputs"] = new JsonObject
        {
            ["environment"] = new JsonObject { ["CORE_LOG_FILE_PATH"] = input },
            ["setup"] = new JsonObject(),
            ["discovery"] = new JsonObject { ["applicationName"] = "Fixture.App", ["buildVersion"] = "1.0.0" }
        };
        if (mode != "absolute") { row["inputs"]!["workingDirectoryAtInitialization"] = alias; }
        row["expected"] = new JsonObject { ["status"] = "Resolved", ["value"] = expected };
        string inputs = Path.Combine(owned.Path, "inputs.json");
        File.WriteAllText(inputs, new JsonObject { ["cases"] = new JsonArray(row.DeepClone()) }.ToJsonString(), new UTF8Encoding(false, true));
        Assert.IsFalse(Directory.Exists(Path.Combine(alias, leaf)));
        await CheckPathAsync(inputs, row);
        Assert.IsFalse(Directory.Exists(Path.Combine(alias, leaf)));
    }

    private async Task CheckPathAsync(string inputs, JsonObject row)
    {
        string id = (string)row["id"]!;
        using var owned = new TemporaryDirectory();
        string before = Path.Combine(owned.Path, "before"), after = Path.Combine(owned.Path, "after");
        Directory.CreateDirectory(before);
        Directory.CreateDirectory(after);
        (JsonObject result, _) = await FixtureProcessChecks.RunAsync(["configuration-path", inputs, id, after], before, TestContext, inputs);
        string captured = (string?)row["inputs"]!["workingDirectoryAtInitialization"] ?? before;
        Assert.AreEqual(id, (string?)result["caseId"]);
        Assert.AreEqual(before, (string?)result["initialActualDirectory"]);
        Assert.AreEqual(after, (string?)result["afterActualDirectory"]);
        Assert.AreEqual(captured == before ? "actual-child-cwd" : "declared-fixture-base", (string?)result["directorySource"]);
        Assert.AreEqual(captured, (string?)result["capturedDirectory"]);
        Assert.AreEqual(1, result["directoryReads"]!.GetValue<int>());
        if ((string?)row["expected"]!["status"] == "Invalid")
        {
            JsonArray expectedIssues = row["expected"]!["issues"]!.DeepClone().AsArray();
            foreach (JsonNode? issue in expectedIssues)
            {
                if ((string?)issue!["code"] == "InvalidPath") { issue["key"] = "LoggingOptions.FilePath"; }
            }
            CollectionAssert.AreEqual(FixtureProcessChecks.Issues(expectedIssues), FixtureProcessChecks.Issues(result["issues"]!));
            Assert.IsNull(result["absolutePathBefore"]);
            Assert.IsNull(result["absolutePathAfter"]);
            Assert.IsNull(result["validatedWorkingDirectory"]);
            string safe = (string?)result["errorMessage"] + string.Join("|", result["issues"]!.AsArray().Select(issue => (string?)issue!["explanation"]));
            Assert.IsFalse(safe.Contains("invalid\0path", StringComparison.Ordinal));
            Assert.IsFalse(safe.Contains("must-not-appear", StringComparison.Ordinal));
        }
        else
        {
            JsonNode? wanted = row["expected"]!["value"];
            string? expected = wanted is JsonObject relative ? Path.GetFullPath((string)relative["input"]!, captured) : (string?)wanted;
            Assert.AreEqual(expected, (string?)result["absolutePathBefore"]);
            Assert.AreEqual(expected, (string?)result["absolutePathAfter"]);
            Assert.AreEqual(captured, (string?)result["validatedWorkingDirectory"]);
            Assert.AreEqual(0, result["issues"]!.AsArray().Count);
        }
        CollectionAssert.AreEqual(ConfigurationReadExpectations.Expected(row["inputs"]!.AsObject()), result["observed"]!["recognizedReads"]!.AsArray().Select(name => (string)name!).ToArray());
        Assert.AreEqual(0, Directory.GetFileSystemEntries(before).Length);
        Assert.AreEqual(0, Directory.GetFileSystemEntries(after).Length);
        Assert.AreEqual(2, Directory.GetFileSystemEntries(owned.Path).Length);
    }

    private static JsonObject[] PathRows() => FixtureProcessChecks.Cases("configuration-cases.json", 350).OfType<JsonObject>().Where(row => (string?)row["key"] == "CORE_LOG_FILE_PATH").ToArray();
    private static bool Applies(JsonObject row) => row["lanes"] is not JsonArray lanes || lanes.Any(lane => (string?)lane == FixtureProcessChecks.Lane);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetShortPathNameW(string path, StringBuilder buffer, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetLongPathNameW(string path, StringBuilder buffer, uint size);
}
