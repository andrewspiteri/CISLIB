using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class JsonlInputSelectorTests
{
    private const string Group = "12345678123456781234567812345678";
    public TestContext TestContext { get; set; } = null!;

    private static readonly Lazy<bool> LinkPrivilege = new(ProbeLinkPrivilege);
    public static IEnumerable<object[]> Corruptions => new[] { "pending", "human", "partial-active", "partial-closed", "invalid-utf8", "bom", "crlf", "array-record", "duplicate-record-key", "nonfinite-record", "invalid-metadata", "duplicate-metadata-key", "traversal", "foreign-name", "duplicate-id", "missing-member", "directory-member", "inventory-bound", "entry-bound", "inventory-bom", "schema", "encoding", "bom-flag", "newline", "group-zero", "active-first", "symlink-member" }
        .Where(id => id != "symlink-member" || LinkPrivilege.Value).Select(id => new object[] { id });

    [TestInitialize]
    public void RecordPrerequisite()
    { if (!LinkPrivilege.Value) { TestContext.WriteLine("Unexecuted original selector control: symlink-member; Windows error 1314 (privilege unavailable)."); } }

    private static bool ProbeLinkPrivilege()
    {
        if (!OperatingSystem.IsWindows()) { return true; }
        using var root = new TemporaryDirectory(); string target = Path.Combine(root.Path, "target"), link = Path.Combine(root.Path, "link"); File.WriteAllText(target, "owned probe");
        try { File.CreateSymbolicLink(link, target); return true; }
        catch (IOException error) when ((error.HResult & 0xffff) == 1314) { return false; }
    }

    [TestMethod]
    [DynamicData(nameof(Corruptions))]
    public void OriginalSelectorCorruptionsAreRejectedAfterPositive(string key)
    {
        using var root = new TemporaryDirectory();
        (string active, string closed, string damaged, JsonObject document) = Setup(root.Path);
        string metadata = active + ".cis.inventory.json";
        CollectionAssert.AreEqual(new[] { closed, active }, JsonlInputSelector.Select(active));
        switch (key)
        {
            case "pending": File.WriteAllText(active + ".cis.transition.json", "{}"); break;
            case "human": document["format"] = "human"; break;
            case "partial-active": File.WriteAllText(active, "{\"partial\":"); break;
            case "partial-closed": File.WriteAllText(closed, "{\"partial\":"); break;
            case "invalid-utf8": File.WriteAllBytes(active, [.. Encoding.UTF8.GetBytes("{\"value\":\""), 0xff, .. Encoding.UTF8.GetBytes("\"}\n")]); break;
            case "bom": File.WriteAllBytes(active, [0xef, 0xbb, 0xbf, (byte)'{', (byte)'}', 10]); break;
            case "crlf": File.WriteAllText(active, "{}\r\n"); break;
            case "array-record": File.WriteAllText(active, "[]\n"); break;
            case "duplicate-record-key": File.WriteAllText(active, "{\"a\":1,\"a\":2}\n"); break;
            case "nonfinite-record": File.WriteAllText(active, "{\"a\":NaN}\n"); break;
            case "invalid-metadata": File.WriteAllText(metadata, "{"); break;
            case "duplicate-metadata-key": File.WriteAllText(metadata, "{\"schema\":1,\"schema\":1}"); break;
            case "traversal": document["segments"]![0]!["fileName"] = "../outside"; break;
            case "foreign-name": document["segments"]![0]!["fileName"] = Path.GetFileName(damaged).Replace(Group, new string('f', 32), StringComparison.Ordinal); break;
            case "duplicate-id": document["segments"]![1]!["id"] = 1; break;
            case "missing-member": File.Delete(damaged); break;
            case "directory-member": File.Delete(damaged); Directory.CreateDirectory(damaged); break;
            case "inventory-bound": File.WriteAllBytes(metadata, Encoding.UTF8.GetBytes(new string(' ', JsonlInputSelector.InventoryBytes + 1))); break;
            case "entry-bound": File.WriteAllText(active, "{\"a\":\"" + new string('x', JsonlInputSelector.EntryBytes) + "\"}\n"); break;
            case "inventory-bom": File.WriteAllBytes(metadata, [0xef, 0xbb, 0xbf, .. File.ReadAllBytes(metadata)]); break;
            case "schema": document["schema"] = 2; break;
            case "encoding": document["encoding"] = "utf-16"; break;
            case "bom-flag": document["bom"] = true; break;
            case "newline": document["newline"] = "crlf"; break;
            case "group-zero":
                document["groupId"] = new string('0', 32);
                foreach (JsonNode? member in document["segments"]!.AsArray().Take(2))
                {
                    string old = Path.Combine(root.Path, (string)member!["fileName"]!), next = old.Replace(Group, new string('0', 32), StringComparison.Ordinal);
                    File.Move(old, next); member["fileName"] = Path.GetFileName(next);
                }
                break;
            case "active-first": document["segments"]![0]!["state"] = "active"; break;
            case "symlink-member": File.Delete(damaged); File.CreateSymbolicLink(damaged, Path.Combine(root.Path, "unlisted.closed")); break;
        }
        if (new[] { "human", "traversal", "foreign-name", "duplicate-id", "schema", "encoding", "bom-flag", "newline", "group-zero", "active-first" }.Contains(key, StringComparer.Ordinal))
        { File.WriteAllText(metadata, document.ToJsonString()); }
        Exception? rejection = null;
        try { JsonlInputSelector.Select(active); } catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException) { rejection = error; }
        Assert.IsNotNull(rejection, "Selector accepted " + key);
        TestContext.WriteLine("Rejected original selector control: " + key);
    }

    [TestMethod]
    [DataRow("entry-exact")]
    [DataRow("inventory-exact")]
    public void ExactOriginalBoundsRemainAccepted(string key)
    {
        using var root = new TemporaryDirectory(); (string active, string closed, _, _) = Setup(root.Path);
        if (key == "entry-exact") { File.WriteAllText(active, "{\"a\":\"" + new string('x', JsonlInputSelector.EntryBytes - 9) + "\"}\n"); Assert.AreEqual(JsonlInputSelector.EntryBytes, new FileInfo(active).Length); }
        else
        {
            string path = active + ".cis.inventory.json"; byte[] raw = File.ReadAllBytes(path), exact = new byte[JsonlInputSelector.InventoryBytes];
            Array.Fill(exact, (byte)' '); raw.CopyTo(exact, 0); File.WriteAllBytes(path, exact);
        }
        CollectionAssert.AreEqual(new[] { closed, active }, JsonlInputSelector.Select(active));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualDevelopmentCommandSelectsOrReturnsOnlySafeFailure(bool corrupt)
    {
        using var root = new TemporaryDirectory(); (string active, string closed, _, _) = Setup(root.Path);
        if (corrupt) { File.WriteAllText(active, "{\"private\":"); }
        string repository = Path.GetFullPath("../..", Path.GetDirectoryName(FixtureProcessChecks.Find("case-inventory.json"))!);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string tool = Path.Combine(repository, "implementations/csharp/eng/CIS.DependencyChecker/bin", configuration, "net10.0/CIS.DependencyChecker.dll");
        string before = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tool)));
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", tool, "select-jsonl-inputs", active], root.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        TestContext.WriteLine($"Selector CLI child: {child.ProcessId}; tool={tool}; sha256={before}"); TestContext.WriteLine(child.StandardOutput); TestContext.WriteLine(child.StandardError);
        Assert.AreEqual(before, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tool))));
        Assert.AreEqual(corrupt ? 2 : 0, child.ExitCode);
        if (corrupt) { Assert.AreEqual("", child.StandardOutput); Assert.AreEqual("Cannot select a consistent stopped JSONL group." + Environment.NewLine, child.StandardError); }
        else { Assert.AreEqual("", child.StandardError); CollectionAssert.AreEqual(new[] { closed, active }, JsonSerializer.Deserialize<string[]>(child.StandardOutput)!); }
    }

    private static (string Active, string Closed, string Damaged, JsonObject Document) Setup(string root)
    {
        string active = Path.Combine(root, "active.jsonl"), closed = Path.Combine(root, Name(2, "closed")), damaged = Path.Combine(root, Name(1, "damaged"));
        File.WriteAllText(active, "{\"producer\":2}\n"); File.WriteAllText(closed, "{\"producer\":1}\n"); File.WriteAllBytes(damaged, [0xff, 10]);
        File.WriteAllText(Path.Combine(root, "unlisted.closed"), "{\"not_selected\":true}\n"); File.WriteAllText(active + ".cis.inventory.json.tmp", "private");
        JsonObject Segment(int id, string name, string state) => new() { ["id"] = id, ["fileName"] = name, ["state"] = state };
        var document = new JsonObject
        {
            ["schema"] = 1,
            ["groupId"] = Group,
            ["format"] = "jsonl",
            ["encoding"] = "utf-8",
            ["bom"] = false,
            ["newline"] = "lf",
            ["segments"] = new JsonArray(Segment(1, Path.GetFileName(damaged), "damaged"), Segment(2, Path.GetFileName(closed), "closed"), Segment(3, Path.GetFileName(active), "active"))
        };
        File.WriteAllText(active + ".cis.inventory.json", document.ToJsonString()); return (active, closed, damaged, document);
    }

    private static string Name(int id, string state) => ".cis-log-" + Group + "-" + id.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + "." + state;
}
