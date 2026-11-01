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
    public async Task CurrentVerificationBindsMaintainedDirtyInputsWithoutReadingHistoricalArchives()
    {
        using var directory = new TemporaryDirectory();
        string[] maintained = ["README.md", ".editorconfig", "implementations/csharp/global.json", "implementations/csharp/tests/new.cs",
            "eng/verify.ps1", "contracts/healthcheck.json", "conformance/healthcheck/cases.json", "compatibility/csharp/baseline.json", "docs/current.md",
            "planning/healthcheck/baseline.json", "planning/logging/plan-amendments.md", "planning/logging/evidence/wp01-s03/unit-tests.trx",
            "verification/healthcheck-linux-permission-control/Program.cs", "verification/healthcheck-linux-permission-control/LinuxPermissionControl.csproj",
            "verification/healthcheck-linux-permission-control/requirements.json", "verification/healthcheck-linux-permission-control/README.md",
            "verification/healthcheck-child-control-v4/Program.cs", "verification/healthcheck-child-control-v4/ChildOperator.csproj", "verification/healthcheck-child-control-v4/README.md"];
        string[] unrelated = ["planning/healthcheck/evidence/archive.zip", "planning/history.md", "implementations/csharp/tests/bin/generated.dll",
            "implementations/csharp/tests/obj/cache.json", "implementations/csharp/tests/TestResults/old.trx", "implementations/csharp/test.csproj.user",
            "verification/healthcheck-grouped-acceptance/FrozenNativeEvidence.cs", "verification/unrelated/Program.cs",
            "verification/healthcheck-child-control-v4/bin/Release/generated.dll"];
        foreach (string relative in maintained.Concat(unrelated))
        {
            string path = Path.Combine(directory.Path, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, relative);
        }
        // An exclusive archive handle makes accidental archive hashing observable.
        using (FileStream archive = File.Open(Path.Combine(directory.Path, unrelated[0]), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            SourceInventoryEntry[] before = SourceInventory.CaptureVerificationInputs(directory.Path);
            CollectionAssert.AreEqual(maintained.Order(StringComparer.Ordinal).ToArray(), before.Select(pin => pin.Path).ToArray());
            AssertDigests(directory.Path, before);
            string changed = Path.Combine(directory.Path, maintained[3]);
            File.AppendAllText(changed, " dirty change");
            SourceInventoryEntry[] after = SourceInventory.CaptureVerificationInputs(directory.Path);
            Assert.AreNotEqual(before.Single(pin => pin.Path == maintained[3]).Sha256, after.Single(pin => pin.Path == maintained[3]).Sha256);
            string added = Path.Combine(directory.Path, "implementations/csharp/tests/added.cs");
            File.WriteAllText(added, "new source");
            CollectionAssert.Contains(SourceInventory.VerificationPaths(directory.Path), "implementations/csharp/tests/added.cs");
            File.Delete(changed);
            CollectionAssert.DoesNotContain(SourceInventory.VerificationPaths(directory.Path), maintained[3]);
        }
        // Historical inventory remains available with its original broader meaning.
        CollectionAssert.Contains((await SourceInventory.CaptureAsync(directory.Path)).Select(pin => pin.Path).ToArray(), unrelated[0]);
    }

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
    [DataRow("Verification")]
    [DataRow("Evidence")]
    public async Task ShellBridgeBootstrapsAnUnbuiltToolCopyWithoutPollutingJson(string bridge)
    {
        using var directory = new TemporaryDirectory();
        DirectoryInfo? language = new(AppContext.BaseDirectory);
        while (language is not null && !File.Exists(Path.Combine(language.FullName, "CisLib.slnx"))) { language = language.Parent; }
        Assert.IsNotNull(language);
        foreach (string file in new[] { "Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.config", "eng/Verification.psm1", "eng/Evidence.psm1" })
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
            param([ValidateSet('Verification', 'Evidence')][string] $Bridge)
            $ErrorActionPreference = 'Stop'
            $inputRoot = Join-Path $PSScriptRoot 'input'
            if ($Bridge -eq 'Evidence') {
                Import-Module (Join-Path $PSScriptRoot 'eng/Evidence.psm1') -Force
                $tool = Get-EvidenceTool
                $json = @(& dotnet $tool source-inventory $inputRoot)
                if ($LASTEXITCODE -ne 0) { throw 'The bootstrapped evidence tool failed.' }
                ($json -join [Environment]::NewLine | ConvertFrom-Json) | ConvertTo-Json -AsArray -Compress
            }
            else {
                Import-Module (Join-Path $PSScriptRoot 'eng/Verification.psm1') -Force
                @(Get-SourceInventory $inputRoot) | ConvertTo-Json -AsArray -Compress
            }
            """);
        string checker = Path.Combine(directory.Path, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll");
        Assert.IsFalse(File.Exists(checker));

        ProcessResult result = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script, "-Bridge", bridge], directory.Path, TimeSpan.FromSeconds(120),
            environment: new Dictionary<string, string?> { ["CIS_VERIFICATION_CHECKER"] = null });

        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.IsTrue(File.Exists(checker));
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        Assert.AreEqual(1, output.RootElement.GetArrayLength());
        Assert.AreEqual("input.txt", output.RootElement[0].GetProperty("path").GetString());
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("bootstrap"))), output.RootElement[0].GetProperty("sha256").GetString());
    }

    [TestMethod]
    public async Task ExplicitCheckerRuntimeAvoidsBootstrapAndRejectsMissingCompanions()
    {
        using var directory = new TemporaryDirectory();
        DirectoryInfo? language = new(AppContext.BaseDirectory);
        while (language is not null && !File.Exists(Path.Combine(language.FullName, "CisLib.slnx"))) { language = language.Parent; }
        Assert.IsNotNull(language);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string compiled = Path.Combine(language.FullName, "eng/CIS.DependencyChecker/bin", configuration, "net10.0");
        string runtime = Directory.CreateDirectory(Path.Combine(directory.Path, "runtime")).FullName;
        foreach (string name in new[] { "CIS.DependencyChecker.dll", "CIS.DependencyChecker.deps.json", "CIS.DependencyChecker.runtimeconfig.json", "CIS.Core.TestSupport.dll" })
        { File.Copy(Path.Combine(compiled, name), Path.Combine(runtime, name)); }
        foreach (string name in new[] { "Evidence.psm1", "Verification.psm1" })
        { File.Copy(Path.Combine(language.FullName, "eng", name), Path.Combine(directory.Path, name)); }
        string input = Directory.CreateDirectory(Path.Combine(directory.Path, "input")).FullName;
        File.WriteAllText(Path.Combine(input, "input.txt"), "prepared runtime");
        string script = Path.Combine(directory.Path, "bridge.ps1");
        File.WriteAllText(script, """
            $ErrorActionPreference = 'Stop'
            Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
            if ((Get-EvidenceTool) -cne $env:CIS_VERIFICATION_CHECKER) { throw 'Wrong selected checker.' }
            Import-Module (Join-Path $PSScriptRoot 'Verification.psm1') -Force
            @(Get-SourceInventory (Join-Path $PSScriptRoot 'input')) | ConvertTo-Json -AsArray -Compress
            """);
        var environment = new Dictionary<string, string?>
        {
            ["CIS_VERIFICATION_CHECKER"] = Path.Combine(runtime, "CIS.DependencyChecker.dll"),
            ["CIS_SOURCE_INVENTORY_DIAGNOSTICS"] = null
        };
        // This isolated bridge has no project or source tree it could bootstrap.
        ProcessResult positive = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script], directory.Path, TimeSpan.FromSeconds(30), environment);
        Assert.AreEqual(0, positive.ExitCode, positive.StandardOutput + positive.StandardError);
        using JsonDocument report = JsonDocument.Parse(positive.StandardOutput);
        Assert.AreEqual("input.txt", report.RootElement[0].GetProperty("path").GetString());
        Assert.IsFalse(Directory.EnumerateFiles(directory.Path, "*.csproj", SearchOption.AllDirectories).Any());
        File.Delete(Path.Combine(runtime, "CIS.Core.TestSupport.dll"));
        ProcessResult negative = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script], directory.Path, TimeSpan.FromSeconds(30), environment);
        Assert.AreNotEqual(0, negative.ExitCode);
        StringAssert.Contains(negative.StandardError, "CIS_VERIFICATION_CHECKER runtime is incomplete");
        StringAssert.Contains(negative.StandardError, "CIS.Core.TestSupport.dll");
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
