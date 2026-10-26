using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class SourceInventoryTests
{
    [TestMethod]
    public async Task WorkingTreeInventoryIncludesCurrentInputsWithoutStagingDeletions()
    {
        using var directory = new TemporaryDirectory();
        try
        {
            await GitAsync(directory.Path, "init", "--quiet");
            File.WriteAllText(Path.Combine(directory.Path, ".gitignore"), "ignored.txt\n");
            File.WriteAllText(Path.Combine(directory.Path, "kept.txt"), "staged");
            File.WriteAllText(Path.Combine(directory.Path, "removed.txt"), "removed");
            await GitAsync(directory.Path, "add", "--", ".gitignore", "kept.txt", "removed.txt");
            File.Delete(Path.Combine(directory.Path, "removed.txt"));
            File.WriteAllBytes(Path.Combine(directory.Path, "kept.txt"), [0, 1, 255, 10]);
            File.WriteAllText(Path.Combine(directory.Path, "new.txt"), "new");
            File.WriteAllText(Path.Combine(directory.Path, "résumé notes.txt"), "unicode");
            File.WriteAllText(Path.Combine(directory.Path, "ignored.txt"), "ignored");
            string index = Path.Combine(directory.Path, ".git", "index");
            byte[] before = File.ReadAllBytes(index);

            SourceInventoryEntry[] inventory = await SourceInventory.CaptureAsync(directory.Path);

            CollectionAssert.AreEqual(new[] { ".gitignore", "kept.txt", "new.txt", "résumé notes.txt" }, inventory.Select(item => item.Path).ToArray());
            CollectionAssert.AreEqual(before, File.ReadAllBytes(index), "Inventory must not write the index.");
            AssertDigests(directory.Path, inventory);
        }
        finally { MakeOwnedGitObjectsWritable(directory.Path); }
    }

    [TestMethod]
    public async Task CopyInventoryExcludesBuildDirectoriesAndKeepsHiddenSource()
    {
        using var directory = new TemporaryDirectory();
        foreach (string folder in new[] { ".artifacts", "bin", "obj", ".vs", "src/bin", "src/.git" })
        {
            Directory.CreateDirectory(Path.Combine(directory.Path, folder));
            File.WriteAllText(Path.Combine(directory.Path, folder, "excluded.txt"), "excluded");
        }
        File.WriteAllText(Path.Combine(directory.Path, ".settings"), "hidden source");
        File.WriteAllText(Path.Combine(directory.Path, "src", "source.cs"), "source");

        SourceInventoryEntry[] inventory = await SourceInventory.CaptureAsync(directory.Path);

        CollectionAssert.AreEqual(new[] { ".settings", "src/source.cs" }, inventory.Select(item => item.Path).ToArray());
        AssertDigests(directory.Path, inventory);
    }

    [TestMethod]
    public async Task AliasToGitRootKeepsIgnoredFilesExcludedAndIndexBytesUnchanged()
    {
        using var directory = new TemporaryDirectory();
        string root = Directory.CreateDirectory(Path.Combine(directory.Path, "actual repo")).FullName;
        string alias = Path.Combine(directory.Path, "alias repo");
        try
        {
            await GitAsync(root, "init", "--quiet");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored.txt\n");
            File.WriteAllText(Path.Combine(root, "kept.txt"), "tracked");
            await GitAsync(root, "add", "--", ".gitignore", "kept.txt");
            File.WriteAllText(Path.Combine(root, "new.txt"), "untracked");
            File.WriteAllText(Path.Combine(root, "ignored.txt"), "must stay excluded");
            string index = Path.Combine(root, ".git", "index");
            byte[] before = File.ReadAllBytes(index);
            if (OperatingSystem.IsWindows())
            {
                ProcessResult link = await ProcessRunner.RunAsync("cmd.exe", ["/d", "/c", "mklink", "/J", alias, root], directory.Path, TimeSpan.FromSeconds(20));
                Assert.AreEqual(0, link.ExitCode, link.StandardOutput + link.StandardError);
            }
            else { Directory.CreateSymbolicLink(alias, root); }

            SourceInventoryEntry[] canonical = await SourceInventory.CaptureAsync(root);
            SourceInventoryEntry[] linked = await SourceInventory.CaptureAsync(alias);

            CollectionAssert.AreEqual(new[] { ".gitignore", "kept.txt", "new.txt" }, canonical.Select(item => item.Path).ToArray());
            CollectionAssert.AreEqual(canonical, linked);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(index), "Neither spelling may write the Git index.");
            AssertDigests(alias, linked);
        }
        finally
        {
            // Delete only the owned link, without recursion, before disposing the fixture directory.
            if (Directory.Exists(alias)) { Directory.Delete(alias); }
            MakeOwnedGitObjectsWritable(root);
        }
    }

    [TestMethod]
    public async Task NestedDirectoryUsesCopyInventoryInsteadOfItsParentsIndex()
    {
        using var directory = new TemporaryDirectory();
        try
        {
            await GitAsync(directory.Path, "init", "--quiet");
            File.WriteAllText(Path.Combine(directory.Path, ".gitignore"), "nested/\n");
            string nested = Directory.CreateDirectory(Path.Combine(directory.Path, "nested")).FullName;
            File.WriteAllText(Path.Combine(nested, "input.txt"), "not in parent index");

            SourceInventoryEntry[] inventory = await SourceInventory.CaptureAsync(nested);

            CollectionAssert.AreEqual(new[] { "input.txt" }, inventory.Select(item => item.Path).ToArray());
            AssertDigests(nested, inventory);
        }
        finally { MakeOwnedGitObjectsWritable(directory.Path); }
    }

    [TestMethod]
    public async Task InvalidGitIndexFailsInsteadOfReturningAPartialInventory()
    {
        using var directory = new TemporaryDirectory();
        try
        {
            await GitAsync(directory.Path, "init", "--quiet");
            string index = Path.Combine(directory.Path, ".git", "index");
            byte[] invalid = Encoding.UTF8.GetBytes("not a Git index");
            File.WriteAllBytes(index, invalid);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => SourceInventory.CaptureAsync(directory.Path));
            CollectionAssert.AreEqual(invalid, File.ReadAllBytes(index));
        }
        finally { MakeOwnedGitObjectsWritable(directory.Path); }
    }

    [TestMethod]
    public async Task NativeCommandWritesJsonAndRejectsMissingRoots()
    {
        using var directory = new TemporaryDirectory();
        using var diagnostics = new TemporaryDirectory();
        DirectoryInfo? language = new(AppContext.BaseDirectory);
        while (language is not null && !File.Exists(Path.Combine(language.FullName, "CisLib.slnx"))) { language = language.Parent; }
        Assert.IsNotNull(language);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string checker = Path.Combine(language.FullName, "eng", "CIS.DependencyChecker", "bin", configuration, "net10.0", "CIS.DependencyChecker.dll");
        Assert.IsTrue(File.Exists(checker), "Build the native checker before running its command control.");
        File.WriteAllText(Path.Combine(directory.Path, "input.txt"), "native command");
        string tracePath = Path.Combine(diagnostics.Path, "git.jsonl");
        ProcessResult positive = await ProcessRunner.RunAsync("dotnet", [checker, "source-inventory", directory.Path], language.FullName, TimeSpan.FromSeconds(30),
            environment: new Dictionary<string, string?> { ["CIS_SOURCE_INVENTORY_DIAGNOSTICS"] = tracePath });
        Assert.AreEqual(0, positive.ExitCode, positive.StandardError);
        using JsonDocument output = JsonDocument.Parse(positive.StandardOutput);
        Assert.AreEqual(1, output.RootElement.GetArrayLength());
        Assert.AreEqual("input.txt", output.RootElement[0].GetProperty("path").GetString());
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("native command"))), output.RootElement[0].GetProperty("sha256").GetString());
        string[] trace = File.ReadAllLines(tracePath);
        Assert.HasCount(2, trace);
        using JsonDocument started = JsonDocument.Parse(trace[0]);
        Assert.AreEqual(8 * 1024 * 1024, started.RootElement.GetProperty("maximumCapturedStandardOutputBytes").GetInt32());
        using JsonDocument completed = JsonDocument.Parse(trace[1]);
        JsonElement capture = completed.RootElement;
        Assert.AreEqual("completed", capture.GetProperty("event").GetString());
        Assert.IsTrue(capture.GetProperty("processId").GetInt32() > 0);
        Assert.AreEqual(capture.GetProperty("standardOutputBytes").GetInt64(), capture.GetProperty("standardOutputTotalBytes").GetInt64());
        Assert.AreEqual(capture.GetProperty("standardErrorBytes").GetInt64(), capture.GetProperty("standardErrorTotalBytes").GetInt64());
        Assert.IsFalse(capture.GetProperty("standardOutputTruncated").GetBoolean());
        Assert.IsFalse(capture.GetProperty("standardErrorTruncated").GetBoolean());
        Assert.IsTrue(capture.GetProperty("outputDrained").GetBoolean());

        ProcessResult negative = await ProcessRunner.RunAsync("dotnet", [checker, "source-inventory", Path.Combine(directory.Path, "missing")], language.FullName, TimeSpan.FromSeconds(30));
        Assert.AreEqual(1, negative.ExitCode, negative.StandardError);
        Assert.AreEqual("", negative.StandardOutput);
        StringAssert.Contains(negative.StandardError, "Source root does not exist");
    }

    [TestMethod]
    public async Task ShellBridgeBootstrapsAnUnbuiltToolCopyWithoutPollutingJson()
    {
        using var directory = new TemporaryDirectory();
        DirectoryInfo? language = new(AppContext.BaseDirectory);
        while (language is not null && !File.Exists(Path.Combine(language.FullName, "CisLib.slnx"))) { language = language.Parent; }
        Assert.IsNotNull(language);
        foreach (string file in new[] { "Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.config", "eng/Verification.psm1" })
        {
            string target = Path.Combine(directory.Path, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(language.FullName, file), target);
        }
        foreach (string folder in new[] { "eng/CIS.DependencyChecker", "tests/CIS.Core.TestSupport" })
        {
            string source = Path.Combine(language.FullName, folder);
            string target = Directory.CreateDirectory(Path.Combine(directory.Path, folder)).FullName;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj")) { continue; }
                if (Path.GetExtension(file) is ".cs" or ".csproj" || Path.GetFileName(file) == "packages.lock.json")
                {
                    string destination = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
            }
        }
        string input = Directory.CreateDirectory(Path.Combine(directory.Path, "input")).FullName;
        File.WriteAllText(Path.Combine(input, "input.txt"), "bootstrap");
        string script = Path.Combine(directory.Path, "bridge.ps1");
        File.WriteAllText(script, """
            $ErrorActionPreference = 'Stop'
            Import-Module (Join-Path $PSScriptRoot 'eng/Verification.psm1') -Force
            @(Get-SourceInventory (Join-Path $PSScriptRoot 'input')) | ConvertTo-Json -AsArray -Compress
            """);
        string checker = Path.Combine(directory.Path, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll");
        Assert.IsFalse(File.Exists(checker));

        ProcessResult result = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script], directory.Path, TimeSpan.FromSeconds(120));

        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.IsTrue(File.Exists(checker));
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.AreEqual(1, output.RootElement.GetArrayLength());
        Assert.AreEqual("input.txt", output.RootElement[0].GetProperty("path").GetString());
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("bootstrap"))), output.RootElement[0].GetProperty("sha256").GetString());
    }

    private static void AssertDigests(string root, SourceInventoryEntry[] inventory)
    {
        foreach (SourceInventoryEntry item in inventory)
        {
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, item.Path)))), item.Sha256);
        }
    }

    private static async Task GitAsync(string root, params string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", ["-C", root, .. arguments], root, TimeSpan.FromSeconds(20));
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
    }

    private static void MakeOwnedGitObjectsWritable(string ownedRoot)
    {
        // This exact directory was created by TemporaryDirectory; cleanup also runs after assertion failures.
        string objects = Path.GetFullPath(Path.Combine(ownedRoot, ".git", "objects"));
        if (!objects.StartsWith(Path.GetFullPath(ownedRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        { throw new InvalidOperationException("Git fixture cleanup escaped its owned root."); }
        if (!Directory.Exists(objects)) { return; }
        foreach (string file in Directory.EnumerateFiles(objects, "*", SearchOption.AllDirectories))
        { File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly); }
    }
}
