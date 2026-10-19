using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class SourceReplayTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("empty")]
    [DataRow("null-entry")]
    [DataRow("empty-path")]
    [DataRow("parent")]
    [DataRow("absolute")]
    [DataRow("drive")]
    [DataRow("backslash-parent")]
    [DataRow("dot-segment")]
    [DataRow("empty-segment")]
    [DataRow("duplicate")]
    [DataRow("alias-duplicate")]
    [DataRow("bad-hash")]
    [DataRow("uppercase-hash")]
    [DataRow("null-hash")]
    public void InventoryRejectsAmbiguousPathsAndInvalidPins(string mode)
    {
        SourceInventoryEntry[] pins = [new("folder/input.txt", new string('a', 64))];
        if (mode == "empty") { pins = []; }
        if (mode == "null-entry") { pins = [null!]; }
        if (mode == "empty-path") { pins[0] = pins[0] with { Path = "" }; }
        if (mode == "parent") { pins[0] = pins[0] with { Path = "../outside.txt" }; }
        if (mode == "absolute") { pins[0] = pins[0] with { Path = "/outside.txt" }; }
        if (mode == "drive") { pins[0] = pins[0] with { Path = "C:/outside.txt" }; }
        if (mode == "backslash-parent") { pins[0] = pins[0] with { Path = "..\\outside.txt" }; }
        if (mode == "dot-segment") { pins[0] = pins[0] with { Path = "folder/./input.txt" }; }
        if (mode == "empty-segment") { pins[0] = pins[0] with { Path = "folder//input.txt" }; }
        if (mode == "duplicate") { pins = [pins[0], pins[0]]; }
        if (mode == "alias-duplicate") { pins = [pins[0], pins[0] with { Path = "folder\\input.txt" }]; }
        if (mode == "bad-hash") { pins[0] = pins[0] with { Sha256 = "no hash" }; }
        if (mode == "uppercase-hash") { pins[0] = pins[0] with { Sha256 = new string('A', 64) }; }
        if (mode == "null-hash") { pins[0] = pins[0] with { Sha256 = null! }; }
        if (mode == "valid") { SourceReplay.ValidateInventory(pins); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => SourceReplay.ValidateInventory(pins)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("original-change")]
    [DataRow("original-delete")]
    [DataRow("input-change")]
    [DataRow("input-delete")]
    [DataRow("work-change")]
    [DataRow("work-delete")]
    public void EveryMaterializedCopyRetainsTheOriginalPin(string mode)
    {
        using var directory = new TemporaryDirectory();
        string[] roots = ["original", "input", "work"];
        foreach (string root in roots) { Write(Path.Combine(directory.Path, root, "nested/input.txt"), "original bytes"); }
        SourceInventoryEntry[] pins = [new("nested/input.txt", Hash(Path.Combine(directory.Path, "original/nested/input.txt")))];
        foreach (string root in roots)
        {
            string path = Path.Combine(directory.Path, root, "nested/input.txt");
            if (mode == root + "-change") { Write(path, "different bytes"); }
            if (mode == root + "-delete") { File.Delete(path); }
            if (mode.StartsWith(root + "-", StringComparison.Ordinal))
            { Assert.ThrowsExactly<InvalidDataException>(() => SourceReplay.AssertInputs(Path.Combine(directory.Path, root), pins)); }
            else { SourceReplay.AssertInputs(Path.Combine(directory.Path, root), pins); }
        }
    }

    [TestMethod]
    [DataRow("directory")]
    [DataRow("file")]
    public async Task ExistingReplayOutputsArePreserved(string mode)
    {
        using var directory = new TemporaryDirectory();
        string output = Path.Combine(directory.Path, "output");
        if (mode == "directory") { Directory.CreateDirectory(output); }
        else { Write(output, "existing"); }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => SourceReplay.RunAsync(Arguments(directory.Path, output, "Release")));
        if (mode == "directory") { Assert.IsTrue(Directory.Exists(output)); }
        else { Assert.AreEqual("existing", File.ReadAllText(output)); }
    }

    [TestMethod]
    [DataRow("Developer")]
    [DataRow("Release")]
    public async Task ActualWrapperFailureRetainsCopiesAndDoesNotChangeParentEnvironment(string profile)
    {
        using var directory = new TemporaryDirectory();
        string original = Path.Combine(directory.Path, "original"), output = Path.Combine(directory.Path, "output");
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "eng/verify.ps1"))) { repo = repo.Parent; }
        Assert.IsNotNull(repo);
        // The real wrapper is deliberately incomplete here. Its missing implementation must fail; no test result is simulated.
        Copy(Path.Combine(repo.FullName, "eng/verify.ps1"), Path.Combine(original, "eng/verify.ps1"));
        string? cliHome = Environment.GetEnvironmentVariable("DOTNET_CLI_HOME");
        string? ceiling = Environment.GetEnvironmentVariable("GIT_CEILING_DIRECTORIES");
        int code = await SourceReplay.RunAsync([.. Arguments(directory.Path, output, profile), "--input-root", original]);
        Assert.AreEqual(1, code);
        JsonObject summary = Read(Path.Combine(output, "summary.json"));
        Assert.IsTrue(summary["inputVerifiedBefore"]!.GetValue<bool>());
        Assert.IsTrue(summary["inputVerifiedAfter"]!.GetValue<bool>());
        Assert.AreEqual(1, summary["verification"]!["exitCode"]!.GetValue<int>());
        Assert.IsTrue(File.Exists(Path.Combine(output, "verification.log")));
        JsonObject process = Read(Path.Combine(output, "logs/verification.process.json"));
        Assert.AreEqual((string?)process["stdoutSha256"], Hash(Path.Combine(output, "logs/verification.stdout.bin")));
        Assert.AreEqual((string?)process["stderrSha256"], Hash(Path.Combine(output, "logs/verification.stderr.bin")));
        foreach (string root in new[] { original, Path.Combine(output, "input"), Path.Combine(output, "work") })
        { Assert.AreEqual(Hash(Path.Combine(original, "eng/verify.ps1")), Hash(Path.Combine(root, "eng/verify.ps1"))); }
        StringAssert.Contains(summary["detail"]!.GetValue<string>(), "not historical binary/package replay");
        Assert.AreEqual(cliHome, Environment.GetEnvironmentVariable("DOTNET_CLI_HOME"));
        Assert.AreEqual(ceiling, Environment.GetEnvironmentVariable("GIT_CEILING_DIRECTORIES"));
        Assert.AreEqual(Hash(Path.Combine(output, "summary.json")), Hash(Path.Combine(directory.Path, ".artifacts/csharp/replay/latest.json")));
    }

    [TestMethod]
    [DataRow("bad-json")]
    [DataRow("bad-pin")]
    [DataRow("empty")]
    public async Task InvalidProvidedInventoryCannotStartVerification(string mode)
    {
        using var directory = new TemporaryDirectory();
        Write(Path.Combine(directory.Path, "input.txt"), "actual");
        string inventory = Path.Combine(directory.Path, "inventory.json"), output = Path.Combine(directory.Path, "output");
        Write(inventory, mode == "bad-json" ? "{" : mode == "empty" ? "[]" : JsonSerializer.Serialize(new[] { new SourceInventoryEntry("input.txt", new string('0', 64)) }));
        Assert.AreEqual(1, await SourceReplay.RunAsync([.. Arguments(directory.Path, output, "Developer"), "--inventory-path", inventory]));
        JsonObject summary = Read(Path.Combine(output, "summary.json"));
        Assert.IsFalse(summary["inputVerifiedBefore"]!.GetValue<bool>());
        Assert.IsFalse(summary["inputVerifiedAfter"]!.GetValue<bool>());
        Assert.IsNull(summary["verification"]);
        Assert.AreEqual(Hash(inventory), (string?)summary["providedInventorySha256"]);
    }

    [TestMethod]
    public void VerificationKeepsProfileLaneAndFreshCacheBoundaries()
    {
        IReadOnlyDictionary<string, string?> environment = SourceReplay.EnvironmentFor(Path.GetFullPath("replay"));
        Assert.AreEqual(Path.GetFullPath("replay"), environment["GIT_CEILING_DIRECTORIES"]);
        Assert.AreEqual(Path.Combine(Path.GetFullPath("replay"), "packages"), environment["NUGET_PACKAGES"]);
        Assert.AreEqual("false", environment["UseSharedCompilation"]);
        CollectionAssert.AreEqual(new[] { "-NoProfile", "-File", Path.Combine("work", "eng/verify.ps1"), "-Profile", "Release", "-Lane", "native-lane" },
            SourceReplay.VerificationArguments("work", "Release", "native-lane"));
    }

    private static string[] Arguments(string repo, string output, string profile) =>
        ["--repo-root", repo, "--output-root", output, "--profile", profile, "--lane", OperatingSystem.IsWindows() ? "windows-x64" : "linux-ubuntu2404-x64"];
}
