using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ArchitectureViolationTests
{
    private static ArchitectureViolationFixture candidate = null!;
    private static JsonObject positive = null!;
    // Private scenario trees can overlap without serializing the other classes;
    // two active copies keep their retained inspection payloads bounded.
    private static SemaphoreSlim scenarios = null!;
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task PrepareCandidate(TestContext context)
    {
        scenarios = new(2, 2);
        try
        {
            candidate = await ArchitectureViolationFixture.PrepareAsync();
            positive = await candidate.AssertPositiveAsync();
            await candidate.PrepareSourceCompilerAsync();
            context.WriteLine("CIS-ARCH-CANDIDATE " + positive.ToJsonString());
        }
        catch
        {
            try { candidate?.Dispose(); }
            finally { scenarios.Dispose(); }
            throw;
        }
    }

    [ClassCleanup]
    public static void ReleaseCandidate(TestContext context)
    {
        if (candidate is null) { return; }
        bool cleanupCompleted = false;
        try { candidate.Dispose(); cleanupCompleted = true; }
        finally
        {
            scenarios.Dispose();
            context.WriteLine("CIS-ARCH-WORK " + candidate.WorkSummary(cleanupCompleted));
        }
    }

    private static async Task RunScenario(Func<Task> run)
    {
        await scenarios.WaitAsync();
        try { await run(); }
        finally { scenarios.Release(); }
    }

    [TestMethod]
    public void NormalInspectorAcceptsTheActualShippingClosureFixture()
    {
        TestContext.WriteLine("CIS-ARCH-FIXTURE " + positive.ToJsonString());
    }

    [TestMethod]
    [DataRow("project-inventory", "ARCH-PROJECT-INVENTORY", "Discovered project count differs from the registered inventory.")]
    [DataRow("framework", "ARCH-FRAMEWORK", "src/CIS.Core/CIS.Core.csproj net10.0")]
    [DataRow("private-runtime-package", "ARCH-PACKAGE-REFERENCE", "src/CIS.Core/CIS.Core.csproj net10.0")]
    [DataRow("lock-content", "ARCH-LOCK-CONTENT", "Lock/assets mismatch: src/CIS.Core/CIS.Core.csproj net10.0 Microsoft.Extensions.Logging.Abstractions/10.0.12")]
    [DataRow("build-asset", "ARCH-PACKAGE-ASSET", "buildTransitive/Unexpected.targets")]
    [DataRow("content-asset", "ARCH-PACKAGE-ASSET", "contentFiles/any/any/attachment.txt")]
    [DataRow("duplicate-asset", "ARCH-PACKAGE-ASSET", "Duplicate package asset paths.")]
    [DataRow("disguised-test-binary", "ARCH-PACKED-ORIGIN", "Packed assembly has no unique evaluated project origin: CIS.Core.ArchitectureTests")]
    [DataRow("packed-content", "ARCH-PACKED-CONTENT", "Packed assembly differs from evaluated Release output: CIS.Core")]
    [DataRow("compiled-reference", "ARCH-COMPILED-REFERENCE", "CIS.Core -> NLog")]
    [DataRow("contract-output", "ARCH-CONTRACT-OUTPUT", "CIS.Logging.EventMetadata:method:ArchitectureBoundaryViolation:")]
    [DataRow("mutable-inheritance", "ARCH-OWNERSHIP-SHAPE", "CIS.Logging.EventMetadata")]
    [DataRow("environment-storage", "ARCH-ENVIRONMENT-MUTABLE", "architectureApplicationName")]
    [DataRow("nested-caller-state", "ARCH-RUNTIME-CALLER-STATE", "CIS.Logging.CoreLogRuntime+ArchitectureCallerState:Caller")]
    public Task NormalInspectorRejectsRealIsolatedViolations(string scenario, string diagnostic, string expectedContext) => RunScenario(async () =>
    {
        using ArchitectureViolationFixture fixture = candidate.CopyForScenario(scenario);
        await fixture.IntroduceAsync(scenario);
        ProcessResult negative = await fixture.InspectAsync();
        Assert.AreEqual(1, negative.ExitCode, negative.StandardOutput + negative.StandardError);
        StringAssert.Contains(negative.StandardError, diagnostic + ": " + expectedContext);
        Assert.AreEqual(1, Regex.Matches(negative.StandardError, @"\bARCH-[A-Z-]+:").Count, "Expected exactly one architecture diagnostic.");
        if (scenario == "contract-output") { StringAssert.Contains(negative.StandardError, " -> CIS.Logging.CoreLogRuntime"); }
        Assert.IsFalse(File.Exists(fixture.ReportPath), "A previous successful inspection must not survive a rejected candidate.");
        var evidence = new JsonObject
        {
            ["schema"] = "cis.architecture-fixture/v1",
            ["scenario"] = scenario,
            ["positive"] = positive.DeepClone(),
            ["negative"] = new JsonObject { ["exitCode"] = negative.ExitCode, ["diagnostic"] = diagnostic, ["expectedContext"] = expectedContext, ["standardError"] = negative.StandardError, ["successfulReportRetained"] = false, ["packageSha256"] = ArchitectureViolationFixture.Hash(fixture.PackagePath) }
        };
        TestContext.WriteLine("CIS-ARCH-FIXTURE " + evidence.ToJsonString());
    });

    [TestMethod]
    [DataRow("configuration")]
    [DataRow("restored-graph")]
    public Task PreparedInspectorRejectsStaleInputsBeforeOpeningThePackage(string change) => RunScenario(async () =>
    {
        using ArchitectureViolationFixture fixture = candidate.CopyForScenario("prepared-guard-" + change);
        fixture.InvalidatePreparedInput(change);
        ProcessResult rejected = await fixture.InspectAsync();
        Assert.AreEqual(1, rejected.ExitCode, rejected.StandardOutput + rejected.StandardError);
        StringAssert.Contains(rejected.StandardError, "Prepared project/configuration/lock/restored graph inputs are stale; run a fresh full inspection.");
        Assert.IsFalse(File.Exists(fixture.ReportPath), "A stale prepared scope must remove the copied successful report.");
        Assert.IsFalse(fixture.PackageInspectionStarted, "Stale project inputs must be rejected before opening the deliberately invalid ZIP.");
        TestContext.WriteLine("CIS-ARCH-PREPARED-GUARD " + JsonSerializer.Serialize(new
        { change, rejected.ExitCode, rejection = rejected.StandardError.Trim(), packageInspectionStarted = false }));
    });
}

// Prepare the immutable real shipping candidate once. Each row owns its mutable copy.
// Trusted fixture registration omits nonshipping projects; normal Developer inspection still covers the full repository.
internal sealed class ArchitectureViolationFixture : IDisposable
{
    private sealed class WorkCounters
    {
        internal int CoreBuilds, CorePacks, Restores, GitPreparations, GitInvocations,
            PositiveInspections, NativeInspectorInvocations, AuxiliaryBuilds, ScenarioCopies, OwnedCleanups,
            FullInspections, PreparedInspections, PreparedGuardInspections, MsBuildEvaluations, PackageOnlyCopies,
            CompilerPreparations, CompilerInvocations;
        internal string TracePath = "";
        internal readonly object TraceLock = new();
        internal readonly SemaphoreSlim CompilerTurn = new(1, 1);
    }

    private const string CoreProject = "src/CIS.Core/CIS.Core.csproj";
    private static readonly string Original = FindLanguageRoot();
    private static readonly IReadOnlyDictionary<string, string?> ChildEnvironment = new Dictionary<string, string?>
    {
        ["MSBuildSDKsPath"] = null,
        ["MSBuildExtensionsPath"] = null,
        ["MSBUILD_EXE_PATH"] = null,
        ["DOTNET_HOST_PATH"] = null,
        ["MSBUILDDISABLENODEREUSE"] = "1",
        ["UseSharedCompilation"] = "false",
        ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
    };
    private readonly TemporaryDirectory temporary = new();
    private readonly IReadOnlyDictionary<string, string?> gitEnvironment;
    private readonly string repository;
    private readonly string language;
    private readonly string output;
    private readonly string nativeTracePath;
    private readonly List<string> copiedSourceHashes = [];
    private readonly WorkCounters work;
    private readonly bool packageOnly;
    private string scenarioName = "baseline";
    private string? preparedReport;
    private string? preparedReportHash;
    private sealed record CoreCompiler(string Directory, string Compiler, string[] Arguments, string SourceRoot,
        string[] Sources, Dictionary<string, string> FixedInputs);
    private CoreCompiler? sourceCompiler;
    private ArchitectureViolationFixture? compilerFixture;
    internal string PackagePath { get; private set; } = "";
    internal string ReportPath => Path.Combine(output, "inspection.json");
    internal bool PackageInspectionStarted => File.ReadLines(nativeTracePath).Any(line =>
    {
        JsonNode entry = JsonNode.Parse(line)!;
        return (string?)entry["scenario"] == scenarioName && (string?)entry["phase"] == "package-pe" && (string?)entry["outcome"] == "started";
    });

    private ArchitectureViolationFixture(WorkCounters? work = null, string? sharedLanguage = null)
    {
        this.work = work ?? new WorkCounters();
        packageOnly = sharedLanguage is not null;
        string gitConfig = Path.Combine(temporary.Path, "empty.gitconfig");
        File.WriteAllText(gitConfig, "");
        gitEnvironment = new Dictionary<string, string?>(ChildEnvironment)
        {
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = gitConfig,
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_CONFIG_COUNT"] = null,
            ["GIT_CONFIG_PARAMETERS"] = null,
            ["GIT_TEMPLATE_DIR"] = null,
            ["GIT_DIR"] = null,
            ["GIT_WORK_TREE"] = null,
            ["GIT_INDEX_FILE"] = null,
            ["GIT_COMMON_DIR"] = null,
            ["GIT_OBJECT_DIRECTORY"] = null,
            ["GIT_ALTERNATE_OBJECT_DIRECTORIES"] = null
        };
        repository = Path.Combine(temporary.Path, "repository");
        language = sharedLanguage ?? Path.Combine(repository, "implementations/csharp");
        output = Path.Combine(repository, ".artifacts/inspection");
        nativeTracePath = Path.Combine(output, "native-phases.jsonl");
        if (this.work.TracePath.Length == 0) { this.work.TracePath = Path.Combine(output, "work.jsonl"); }
    }

    internal static async Task<ArchitectureViolationFixture> PrepareAsync()
    {
        var fixture = new ArchitectureViolationFixture();
        try
        {
            Assert.IsTrue(File.Exists(Path.Combine(Original, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll")), "Build the complete solution in Release before these tests; Developer verification does this before running them.");
            fixture.CopyInputs();
            await fixture.RequireAsync("git", ["init", "--quiet", "--template="], fixture.repository);
            await fixture.RequireAsync("git", ["-c", "core.autocrlf=false", "add", "--all"], fixture.repository);
            await fixture.RequireAsync("git", ["-c", "user.name=CISLIB Verification", "-c", "user.email=verification@localhost", "-c", "commit.gpgsign=false", "commit", "--quiet", "--no-verify", "-m", "Architecture fixture inputs"], fixture.repository);
            if (!fixture.TryUsePreparedBaseline())
            {
                await fixture.RestoreAsync(locked: true);
                await fixture.BuildAndPackAsync(buildProjectReferences: true);
            }
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    private bool TryUsePreparedBaseline()
    {
#if CIS_MUTATION_TESTS
        return false;
#else
        // Reuse only physical inputs from this checkout's complete preparation.
        // Every inspector, source-control compile and assertion still runs fresh.
        string originalRepository = Path.GetFullPath("../..", Original);
        string prepared, package;
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        var trees = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        try
        {
            prepared = VerificationCheckpoint.Root(originalRepository);
            string summaryPath = Path.Combine(prepared, "summary.json");
            byte[] summaryBytes = File.ReadAllBytes(summaryPath);
            pins.Add(summaryPath, Convert.ToHexStringLower(SHA256.HashData(summaryBytes)));
            JsonObject summary = JsonNode.Parse(summaryBytes)!.AsObject();
            Check((string?)summary["profile"] == "Preparation" && (string?)summary["scope"] == "prepared-inputs" && (int?)summary["exitCode"] == 0,
                "The explicit checkpoint is not a successful preparation.");
            string[] stages = ["restore", "project-boundaries", "format", "build", "startup-contract", "compatibility-samples",
                "log-value-consumers", "publication-ledger", "pack", "package-boundaries", "architecture-inspection"];
            Check(summary["stages"] is JsonArray actual && actual.Count == stages.Length && actual.Select(stage => (string?)stage!["id"]).SequenceEqual(stages)
                && actual.All(stage => (string?)stage!["status"] == "Passed" && (int?)stage["exitCode"] == 0), "The explicit preparation did not pass all eleven stages.");
            Check((string?)summary["source"]?["inputScope"] == "maintained-csharp-verification-inputs/v1", "The preparation has another source scope.");
            string sourceManifest = VerifiedReference(prepared, summary["source"]!["inputInventory"]!);
            Dictionary<string, string> sourcePins = InputPins(JsonNode.Parse(File.ReadAllText(sourceManifest))!.AsArray());
            Check(sourcePins.Count > 0, "The preparation has no source inputs.");
            foreach ((string relative, string hash) in sourcePins) { pins.Add(ContainedPath(originalRepository, relative), hash); }
            foreach (string copied in copiedSourceHashes)
            {
                string relative = "implementations/csharp/" + copied[..^65];
                Check(sourcePins.TryGetValue(relative, out string? hash) && hash == copied[^64..], "A copied shipping source is absent or stale in the preparation: " + relative);
            }
            string nativeManifest = VerifiedReference(prepared, summary["nativeInputs"]!);
            JsonObject native = JsonNode.Parse(File.ReadAllText(nativeManifest))!.AsObject();
            Check((string?)native["schema"] == "cis.prepared-regression-native-inputs/v1" && (string?)native["configuration"] == "Release"
                && (string?)native["sourceInventorySha256"] == Hash(sourceManifest), "The prepared native manifest identifies another candidate.");
            string[] flags = ["CisMutationTesting", "CisHealthcheckMutationCohort", "CisHealthcheckPilotCohort", "CisLinuxIdentityQualification"];
            Check(native["buildProperties"] is JsonObject properties && properties.Count == flags.Length && flags.All(flag => (string?)properties[flag] == "false"),
                "Prepared native inputs must use the ordinary cohort.");
            string[] roots = new[] { "CIS.Core.UnitTests", "CIS.Core.IntegrationTests", "CIS.Core.ArchitectureTests", "CIS.Core.CompatibilityTests", "CIS.Verification.Tests", "CIS.Core.BusinessCases" }
                .Select(project => $"implementations/csharp/tests/{project}/bin/Release")
                .Concat(["implementations/csharp/src/CIS.Core/bin/Release", "implementations/csharp/eng/CIS.DependencyChecker/bin/Release", "implementations/csharp/tests/CIS.Core.TestSupport/bin/Release"]).ToArray();
            Check(native["roots"]!.AsArray().Select(root => (string)root!).SequenceEqual(roots), "The prepared native root inventory differs.");
            Dictionary<string, string> nativePins = InputPins(native["files"]!.AsArray());
            Dictionary<string, string> actualNative = roots.SelectMany(root => TreePins(ContainedPath(originalRepository, root))
                .Select(pin => new KeyValuePair<string, string>(root + "/" + pin.Key.Replace('\\', '/'), pin.Value))).ToDictionary(pin => pin.Key, pin => pin.Value, StringComparer.Ordinal);
            Check(nativePins.OrderBy(pin => pin.Key, StringComparer.Ordinal).SequenceEqual(actualNative.OrderBy(pin => pin.Key, StringComparer.Ordinal)),
                "The prepared native membership or bytes differ from this checkout.");
            foreach ((string relative, string hash) in nativePins) { pins.Add(ContainedPath(originalRepository, relative), hash); }
            package = VerificationCheckpoint.Package(originalRepository);
            Check(SamePath(package, VerifiedReference(prepared, summary["candidatePackage"]!)), "The selected package belongs to another preparation.");
            string inspectionPath = VerifiedReference(prepared, summary["architectureInspection"]!);
            JsonObject inspection = JsonNode.Parse(File.ReadAllText(inspectionPath))!.AsObject();
            Check((string?)inspection["package"]?["sha256"] == pins[package], "The prepared inspection identifies another package.");
            using (ZipArchive archive = ZipFile.OpenRead(package))
            {
                foreach (string project in new[] { CoreProject, "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj" })
                {
                    JsonObject assembly = inspection["assemblies"]!.AsArray().OfType<JsonObject>().Single(row => (string?)row["project"] == project);
                    JsonObject facts = inspection["projects"]!.AsArray().OfType<JsonObject>().Single(row => (string?)row["project"] == project);
                    string binary = Path.GetFullPath(Path.Combine(Original, Path.GetDirectoryName(project)!, "bin/Release/net10.0", Path.GetFileNameWithoutExtension(project) + ".dll"));
                    Check(SamePath((string)assembly["builtAssembly"]!["path"]!, binary), "The prepared PE belongs to another checkout.");
                    Check(pins.TryGetValue(binary, out string? existing) ? existing == (string)assembly["builtAssembly"]!["sha256"]! : true, "The native manifest and shipping PE pin differ.");
                    pins[binary] = (string)assembly["builtAssembly"]!["sha256"]!;
                    foreach (string field in new[] { "assets", "lock" })
                    {
                        string path = Path.GetFullPath(Path.Combine(Original, Path.GetDirectoryName(project)!, field == "assets" ? "obj/project.assets.json" : "packages.lock.json"));
                        Check(SamePath((string)facts[field]!["path"]!, path), "The prepared restored graph belongs to another checkout.");
                        Check(!pins.TryGetValue(path, out string? existingGraph) || existingGraph == (string)facts[field]!["sha256"]!, "The source manifest and restored graph pin differ.");
                        pins[path] = (string)facts[field]!["sha256"]!;
                    }
                    using Stream packed = (archive.GetEntry((string)assembly["packageEntry"]!) ?? throw new InvalidDataException("The prepared package omitted a shipping PE.")).Open();
                    Check(Convert.ToHexStringLower(SHA256.HashData(packed)) == pins[binary], "The prepared package and Release shipping PE differ.");
                    foreach (string directory in new[] { "bin/Release", "obj" })
                    {
                        string tree = Path.Combine(Original, Path.GetDirectoryName(project)!, directory);
                        trees.Add(tree, TreePins(tree));
                    }
                }
            }
            foreach ((string path, string hash) in pins) { Check(Hash(path) == hash, "A prepared shipping input changed: " + path); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException)
        {
            Trace("prepared-baseline-inputs", "fresh-fallback", [error.Message], 0);
            return false;
        }
        foreach ((string tree, Dictionary<string, string> files) in trees)
        {
            string destination = PathAt(Path.GetRelativePath(Original, tree));
            var copiedPins = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string relative, string hash) in files)
            {
                string source = Path.Combine(tree, relative), copied = Path.Combine(destination, relative);
                Copy(source, copied);
                File.SetAttributes(copied, File.GetAttributes(copied) & ~FileAttributes.ReadOnly);
                Check(Hash(copied) == hash, "A copied prepared build input differs: " + copied);
                RelocateBuildMetadata(copied, originalRepository, repository);
                if (File.Exists(copied)) { copiedPins.Add(relative, Hash(copied)); }
            }
            Check(files.SequenceEqual(TreePins(tree)), "Prepared build input membership or bytes changed during copying: " + tree);
            Check(copiedPins.OrderBy(pin => pin.Key, StringComparer.Ordinal).SequenceEqual(TreePins(destination).OrderBy(pin => pin.Key, StringComparer.Ordinal)),
                "The private prepared build input membership or bytes changed after relocation: " + destination);
        }
        PackagePath = Path.Combine(repository, ".artifacts/packages", Path.GetFileName(package));
        Copy(package, PackagePath);
        Check(Hash(PackagePath) == pins[package], "The copied prepared package differs.");
        foreach ((string path, string hash) in pins) { Check(Hash(path) == hash, "A prepared shipping input changed during copying: " + path); }
        Trace("prepared-baseline-inputs", "completed", [prepared, pins[Path.Combine(prepared, "summary.json")], Hash(PackagePath), trees.Sum(tree => tree.Value.Count).ToString()], 0, 0);
        return true;

        static Dictionary<string, string> InputPins(JsonArray rows)
        {
            Dictionary<string, string> result = rows.OfType<JsonObject>().ToDictionary(row => (string)row["path"]!, row => (string)row["sha256"]!, StringComparer.Ordinal);
            Check(result.Count == rows.Count, "Prepared input inventory contains a malformed row.");
            return result;
        }
        static string ContainedPath(string root, string relative)
        {
            Check(!Path.IsPathRooted(relative) && !relative.Contains(':') && relative.Replace('\\', '/').Split('/').All(part => part.Length > 0 && part is not "." and not ".."), "Prepared input path is not relative.");
            string result = Path.GetFullPath(Path.Combine(root, relative));
            string current = root;
            foreach (string part in Path.GetRelativePath(root, result).Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                Check((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Prepared input paths cannot traverse links.");
            }
            return result;
        }
        string VerifiedReference(string root, JsonNode reference)
        {
            string path = ContainedPath(root, (string)reference["path"]!);
            string hash = (string)reference["sha256"]!;
            Check(!pins.TryGetValue(path, out string? existing) || existing == hash, "Prepared references disagree about their artifact.");
            pins[path] = hash;
            Check(Hash(path) == hash, "A prepared artifact differs from its summary pin.");
            return path;
        }
        static void Check(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
        static bool SamePath(string first, string second) => Path.GetFullPath(first).Equals(Path.GetFullPath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
#endif
    }

    private static Dictionary<string, string> TreePins(string root)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(root);
        return files;
        void Walk(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Prepared build inputs cannot contain linked directories."); }
            foreach (string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Prepared build inputs cannot contain links."); }
                if ((attributes & FileAttributes.Directory) != 0) { Walk(path); }
                else { files.Add(Path.GetRelativePath(root, path), Hash(path)); }
            }
        }
    }

    private static void RelocateBuildMetadata(string path, string before, string after)
    {
        if (path.EndsWith("project.assets.json", StringComparison.Ordinal) || path.EndsWith(".nuget.dgspec.json", StringComparison.Ordinal) || path.EndsWith("project.nuget.cache", StringComparison.Ordinal))
        {
            JsonNode metadata = JsonNode.Parse(File.ReadAllText(path))!;
            Relocate(metadata, before, after);
            File.WriteAllText(path, metadata.ToJsonString());
        }
        else if (path.EndsWith(".FileListAbsolute.txt", StringComparison.Ordinal) || path.EndsWith(".nuget.g.props", StringComparison.Ordinal) || path.EndsWith(".nuget.g.targets", StringComparison.Ordinal))
        { File.WriteAllText(path, Relocated(File.ReadAllText(path), before, after)); }
        else if (path.EndsWith(".AssemblyReference.cache", StringComparison.Ordinal)) { File.Delete(path); }
    }

    private static bool IsPackageOnly(string scenario) => scenario is "build-asset" or "content-asset" or "duplicate-asset" or "disguised-test-binary" or "packed-content";

    internal ArchitectureViolationFixture CopyForScenario(string scenario)
    {
        var copy = new ArchitectureViolationFixture(work, IsPackageOnly(scenario) ? language : null) { scenarioName = scenario };
        try
        {
            if (IsPackageOnly(scenario))
            {
                copy.PackagePath = Path.Combine(copy.repository, ".artifacts/packages", Path.GetFileName(PackagePath));
                Copy(PackagePath, copy.PackagePath);
                // Keep the stale-success removal assertion meaningful in this small copy.
                Copy(ReportPath, copy.ReportPath);
                Interlocked.Increment(ref work.PackageOnlyCopies);
            }
            else
            {
                foreach (string source in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
                {
                    // Command bookkeeping can change while another private scenario runs.
                    // Each inspector writes its own native phase trace instead.
                    if (source == work.TracePath || source == nativeTracePath) { continue; }
                    string destination = Path.Combine(copy.repository, Path.GetRelativePath(repository, source));
                    Copy(source, destination);
                    File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
                    // NuGet records absolute project locations. Relocate only that metadata;
                    // package identities, content hashes and actual compiled outputs stay unchanged.
                    if (source.EndsWith("project.assets.json", StringComparison.Ordinal) ||
                        source.EndsWith(".nuget.dgspec.json", StringComparison.Ordinal) ||
                        source.EndsWith("project.nuget.cache", StringComparison.Ordinal))
                    {
                        JsonNode metadata = JsonNode.Parse(File.ReadAllText(destination))!;
                        Relocate(metadata, repository, copy.repository);
                        File.WriteAllText(destination, metadata.ToJsonString());
                    }
                    else if (source.EndsWith(".FileListAbsolute.txt", StringComparison.Ordinal) ||
                        source.EndsWith(".nuget.g.props", StringComparison.Ordinal) || source.EndsWith(".nuget.g.targets", StringComparison.Ordinal))
                    { File.WriteAllText(destination, Relocated(File.ReadAllText(destination), repository, copy.repository)); }
                    else if (source.EndsWith(".AssemblyReference.cache", StringComparison.Ordinal))
                    { File.Delete(destination); }
                }
                copy.PackagePath = Path.Combine(copy.repository, Path.GetRelativePath(repository, PackagePath));
            }
            copy.copiedSourceHashes.AddRange(copiedSourceHashes);
            copy.sourceCompiler = sourceCompiler;
            if (scenario is not ("project-inventory" or "framework" or "private-runtime-package" or "lock-content"))
            { copy.preparedReport = ReportPath; copy.preparedReportHash = Hash(ReportPath); }
            Assert.AreEqual(Hash(PackagePath), Hash(copy.PackagePath), "Each scenario starts from the same candidate package.");
            Interlocked.Increment(ref work.ScenarioCopies);
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    private static void Relocate(JsonNode node, string before, string after)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in obj.Select(pair => pair.Key).ToArray())
            {
                if (obj[key] is JsonValue value && value.TryGetValue(out string? text) && text is not null)
                { obj[key] = Relocated(text, before, after); }
                else if (obj[key] is JsonNode child) { Relocate(child, before, after); }
                string relocatedKey = Relocated(key, before, after);
                if (relocatedKey != key)
                {
                    JsonNode? entry = obj[key];
                    obj.Remove(key);
                    obj.Add(relocatedKey, entry);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue(out string? text) && text is not null)
                { array[index] = Relocated(text, before, after); }
                else if (array[index] is JsonNode child) { Relocate(child, before, after); }
            }
        }
    }

    private static string Relocated(string text, string before, string after) => text.Replace(before, after, StringComparison.Ordinal)
        .Replace(before.Replace('\\', '/'), after.Replace('\\', '/'), StringComparison.Ordinal);

    internal async Task<JsonObject> AssertPositiveAsync()
    {
        Interlocked.Increment(ref work.PositiveInspections);
        ProcessResult result = await InspectAsync();
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.IsTrue(File.Exists(ReportPath));
        JsonObject report = JsonNode.Parse(File.ReadAllText(ReportPath))!.AsObject();
        Assert.AreEqual(2, report["projects"]!.AsArray().Count);
        Assert.AreEqual(2, report["assemblies"]!.AsArray().Count);
        Assert.AreEqual(5, report["levels"]!.AsArray().Count);
        Assert.AreEqual(0, report["allowlists"]!["violations"]!.GetValue<int>());
        Assert.AreEqual(0, report["ownership"]!["violations"]!.GetValue<int>());
        Assert.AreEqual(2, report["allowlists"]!["runtimePackages"]!.GetValue<int>());
        Assert.AreEqual(14, report["packageAssets"]!.AsArray().Count);
        Assert.AreEqual(2, report["allowlists"]!["targets"]!.GetValue<int>());
        Assert.IsGreaterThan(0, report["ownership"]!["checkedContractUses"]!.GetValue<int>());
        foreach (JsonObject assembly in report["assemblies"]!.AsArray().OfType<JsonObject>())
        { Assert.IsGreaterThan(0, assembly["facts"]!["typeUses"]!.AsArray().Count); }
        return new JsonObject
        {
            ["schema"] = "cis.architecture-fixture-positive/v1",
            ["exitCode"] = result.ExitCode,
            ["fixtureScope"] = "Actual Core and packaged build-helper source; trusted registration contains their complete two-project shipping closure. Full repository inventory remains independently checked by the normal Developer profile.",
            ["projects"] = 2,
            ["targets"] = report["allowlists"]!["targets"]!.GetValue<int>(),
            ["assemblies"] = 2,
            ["levels"] = 5,
            ["copiedSourceFiles"] = copiedSourceHashes.Count,
            ["copiedSourceDigest"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', copiedSourceHashes)))),
            ["inspectionSha256"] = Hash(ReportPath),
            ["packageSha256"] = Hash(PackagePath),
            ["inspectorSha256"] = Hash(Path.Combine(Original, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll"))
        };
    }

    internal async Task<ProcessResult> InspectAsync()
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(nativeTracePath, "");
        bool reused = preparedReport is not null;
        string[] arguments = reused
            ? [Path.Combine(Original, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll"), "architecture-prepared", language, PackagePath, output, preparedReport!, preparedReportHash!]
            : [Path.Combine(Original, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll"), scenarioName == "baseline" ? "architecture-prepare" : "architecture", language, PackagePath, output];
        var environment = new Dictionary<string, string?>(ChildEnvironment)
        {
            ["CIS_ARCHITECTURE_WORK_COUNTERS"] = "true",
            ["CIS_ARCHITECTURE_PHASE_TRACE"] = nativeTracePath,
            ["CIS_ARCHITECTURE_SCENARIO"] = scenarioName
        };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Trace("inspector", "started", arguments, 0);
        try
        {
            ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, language, TimeSpan.FromMinutes(10), environment);
            CountInspector(result, reused);
            Trace("inspector", "completed", arguments, clock.Elapsed.TotalSeconds, result.ExitCode);
            return result;
        }
        catch (ProcessTimeoutException error) { CountInspector(error.Result, reused); Trace("inspector", "timeout", arguments, clock.Elapsed.TotalSeconds); throw; }
        catch (ProcessOutputLimitException error) { CountInspector(error.Result, reused); Trace("inspector", "output-limit", arguments, clock.Elapsed.TotalSeconds); throw; }
    }

    private void CountInspector(ProcessResult result, bool reused)
    {
        Interlocked.Increment(ref work.NativeInspectorInvocations);
        if (scenarioName.StartsWith("prepared-guard-", StringComparison.Ordinal)) { Interlocked.Increment(ref work.PreparedGuardInspections); }
        else if (reused) { Interlocked.Increment(ref work.PreparedInspections); }
        else { Interlocked.Increment(ref work.FullInspections); }
        Interlocked.Add(ref work.MsBuildEvaluations, result.StandardOutput.Split("CIS-ARCH-MSBUILD-COMPLETED", StringSplitOptions.None).Length - 1);
    }

    internal void InvalidatePreparedInput(string change)
    {
        Assert.IsFalse(packageOnly, "A guard owns a mutable prepared input copy.");
        Assert.IsNotNull(preparedReport);
        switch (change)
        {
            case "configuration": File.AppendAllText(PathAt("Directory.Build.props"), "\n<!-- Prepared-input currency control. -->\n"); break;
            case "restored-graph":
                string path = PathAt("src/CIS.Core/obj/project.assets.json");
                JsonObject assets = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                JsonNode package = assets["libraries"]!["Microsoft.Extensions.Logging.Abstractions/10.0.12"]!;
                string before = package["sha512"]!.GetValue<string>();
                package["sha512"] = (before[0] == 'A' ? "B" : "A") + before[1..];
                File.WriteAllText(path, assets.ToJsonString());
                break;
            default: throw new ArgumentException("Unknown prepared-input guard.", nameof(change));
        }
        // If the stale-input guard were bypassed, this actual ZIP would fail at
        // package parsing instead of producing the required stale prerequisite.
        File.WriteAllText(PackagePath, "The prepared-input guard must reject before opening this invalid ZIP.");
    }

    internal async Task IntroduceAsync(string scenario)
    {
        if (packageOnly && !IsPackageOnly(scenario)) { throw new InvalidOperationException("A package-only copy cannot modify the immutable source candidate."); }
        switch (scenario)
        {
            case "project-inventory": Write("shadow/Shadow.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"); break;
            case "framework":
                string projectPath = PathAt("src/CIS.Core/CIS.Core.csproj");
                XDocument project = XDocument.Load(projectPath);
                project.Descendants("FrameworkReference").Single().SetAttributeValue("IsImplicitlyDefined", "true");
                project.Save(projectPath); break;
            case "private-runtime-package":
                AddProjectItem(new XElement("PackageReference", new XAttribute("Include", "Microsoft.Extensions.Logging"), new XAttribute("PrivateAssets", "all")));
                // This rule rejects evaluated/restored metadata; no changed PE is required.
                await RestoreAsync(locked: false); break;
            case "lock-content":
                string lockPath = PathAt("src/CIS.Core/packages.lock.json");
                JsonObject locked = JsonNode.Parse(File.ReadAllText(lockPath))!.AsObject();
                JsonNode dependency = locked["dependencies"]!["net10.0"]!["Microsoft.Extensions.Logging.Abstractions"]!;
                string hash = dependency["contentHash"]!.GetValue<string>();
                dependency["contentHash"] = (hash[0] == 'A' ? "B" : "A") + hash[1..];
                File.WriteAllText(lockPath, locked.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); break;
            case "build-asset": AddPackageEntry("buildTransitive/Unexpected.targets", Encoding.UTF8.GetBytes("<Project />")); break;
            case "content-asset": AddPackageEntry("contentFiles/any/any/attachment.txt", Encoding.UTF8.GetBytes("unexpected content")); break;
            case "duplicate-asset": AddPackageEntry("readme.md", Encoding.UTF8.GetBytes("case duplicate")); break;
            case "disguised-test-binary": AddPackageEntry("lib/net10.0/CIS.Core.dll", File.ReadAllBytes(typeof(ArchitectureViolationTests).Assembly.Location)); break;
            case "packed-content":
                byte[] built = File.ReadAllBytes(PathAt("src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"));
                AddPackageEntry("lib/net10.0/CIS.Core.dll", [.. built, 0]); break;
            case "compiled-reference": await AddCompiledReferenceAsync(); break;
            case "contract-output":
                AppendClassMember("src/CIS.Core/Logging/EventMetadata.cs", "internal static void ArchitectureBoundaryViolation() { _ = CoreLogRuntime.Create(); }");
                await CompileSourceViolationAsync(); break;
            case "mutable-inheritance":
                ReplaceExactlyOnce("src/CIS.Core/Logging/EventMetadata.cs", "public sealed class EventMetadata", "public sealed class EventMetadata : ArchitectureMutableBase");
                Write("src/CIS.Core/ArchitectureMutableBase.cs", "namespace CIS.Logging;\n/// <summary>Deliberately mutable qualification base.</summary>\npublic class ArchitectureMutableBase {\n/// <summary>Deliberately mutable inherited state.</summary>\npublic string State { get; set; } = string.Empty;\n}\n");
                await CompileSourceViolationAsync(); break;
            case "environment-storage":
                ReplaceExactlyOnce("src/CIS.Core/Environment/EnvironmentOptions.cs", "public static string ApplicationName { get; }", "private static string architectureApplicationName = string.Empty;\n    /// <summary>Deliberately mutable identity view.</summary>\n    public static string ApplicationName => architectureApplicationName;");
                ReplaceExactlyOnce("src/CIS.Core/Environment/EnvironmentOptions.cs", "ApplicationName = application?.GetName().Name", "architectureApplicationName = application?.GetName().Name");
                ReplaceExactlyOnce("src/CIS.Core/Environment/EnvironmentOptions.cs", "(ApplicationName, EnvironmentName, ServiceVersion) =", "(architectureApplicationName, EnvironmentName, ServiceVersion) =");
                ReplaceExactlyOnce("src/CIS.Core/Environment/EnvironmentOptions.cs", "ApplicationName = string.Intern(ApplicationName);", "architectureApplicationName = string.Intern(architectureApplicationName);");
                await CompileSourceViolationAsync(); break;
            case "nested-caller-state":
                AppendClassMember("src/CIS.Core/Logging/CoreLogRuntime.cs", "internal sealed class ArchitectureCallerState { internal readonly LoggingOptions Caller = new(); }");
                await CompileSourceViolationAsync(); break;
            default: throw new ArgumentException("Unknown architecture fixture.", nameof(scenario));
        }
    }

    internal async Task PrepareSourceCompilerAsync()
    {
        // The real positive candidate stays immutable. Only this separate owned
        // compiler copy receives the genuine source controls, one at a time.
        ArchitectureViolationFixture shadow = CopyForScenario("compiler-preparation");
        try
        {
            string directory = shadow.PathAt("src/CIS.Core");
            ProcessResult capture = await shadow.RequireAsync("dotnet", ["msbuild", shadow.PathAt(CoreProject), "-t:Compile",
                "-p:Configuration=Release", "-p:BuildProjectReferences=false", "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true",
                "-p:NonExistentFile=" + Path.Combine(directory, ".capture-" + Guid.NewGuid().ToString("N")),
                "-getTargetResult:CoreCompile", "-getProperty:NETCoreSdkVersion,RoslynTargetsPath,CscToolPath,CscToolExe", "-nologo", "-v:quiet"]);
            JsonObject parsed = JsonNode.Parse(capture.StandardOutput)!.AsObject(), properties = parsed["Properties"]!.AsObject();
            JsonObject policy = JsonNode.Parse(File.ReadAllText(PathAt("eng/toolchain.json")))!.AsObject();
            Assert.AreEqual((string?)policy["sdk"], (string?)properties["NETCoreSdkVersion"]);
            Assert.IsTrue(string.IsNullOrEmpty((string?)properties["CscToolPath"]) && string.IsNullOrEmpty((string?)properties["CscToolExe"]));
            JsonObject compile = parsed["TargetResults"]!["CoreCompile"]!.AsObject();
            Assert.AreEqual("Success", (string?)compile["Result"]);
            string[] arguments = compile["Items"]!.AsArray().Select(item => item!["Identity"]!.GetValue<string>()).ToArray();
            Assert.IsTrue(arguments.Length > 0 && arguments.All(value => !value.Contains('\r') && !value.Contains('\n')));
            Assert.AreEqual(1, arguments.Count(value => value == "/noconfig"));
            foreach (string required in new[] { "/nullable:enable", "/warnaserror+", "/deterministic+" }) { Assert.Contains(required, arguments); }
            string compiler = Path.Combine(properties["RoslynTargetsPath"]!.GetValue<string>(), "bincore/csc.dll");
            string capturePath = Path.Combine(directory, "compiler-command.json"); File.WriteAllText(capturePath, capture.StandardOutput);
            var fixedInputs = new Dictionary<string, string>(StringComparer.Ordinal)
            { [shadow.PathAt(CoreProject)] = Hash(shadow.PathAt(CoreProject)), [compiler] = Hash(compiler), [capturePath] = Hash(capturePath) };
            foreach (string generated in Directory.EnumerateFiles(Path.Combine(directory, "obj"), "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".cs" or ".editorconfig" or ".json")) { fixedInputs.Add(generated, Hash(generated)); }
            foreach (string prefix in new[] { "/reference:", "/analyzer:", "/analyzerconfig:", "/additionalfile:" })
            {
                foreach (string argument in arguments.Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    string path = Path.GetFullPath(argument[prefix.Length..].Trim('"'), directory);
                    fixedInputs.TryAdd(path, Hash(path));
                }
            }
            string sourceRoot = PathAt("src/CIS.Core");
            string[] sources = SourceFiles(sourceRoot).Select(path => Path.GetRelativePath(sourceRoot, path)).Order(StringComparer.Ordinal).ToArray();
            foreach (string relative in sources)
            {
                Assert.IsTrue(arguments.Any(argument => argument.Trim('"').Replace('\\', '/') == relative.Replace('\\', '/')
                    || argument.Trim('"').Replace('\\', '/') == Path.Combine(directory, relative).Replace('\\', '/')), "The captured SDK command omitted a Core source: " + relative);
                fixedInputs.Add(Path.Combine(sourceRoot, relative), Hash(Path.Combine(sourceRoot, relative)));
            }
            sourceCompiler = new(directory, compiler, arguments, sourceRoot, sources, fixedInputs);
            compilerFixture = shadow;
        }
        catch { shadow.Dispose(); throw; }
    }

    private async Task CompileSourceViolationAsync(string[]? additionalReferences = null)
    {
        // These source controls deliberately use the same genuine SDK compiler
        // capture. Keep its mutate/compile/restore transaction exclusive.
        await work.CompilerTurn.WaitAsync();
        try { await CompileSourceViolationCoreAsync(additionalReferences ?? []); }
        finally { work.CompilerTurn.Release(); }
    }

    private async Task CompileSourceViolationCoreAsync(string[] additionalReferences)
    {
        CoreCompiler compiler = sourceCompiler ?? throw new InvalidOperationException("Prepare the genuine Core compiler context first.");
        foreach ((string path, string hash) in compiler.FixedInputs) { Assert.AreEqual(hash, Hash(path), "Prepared compiler input changed: " + path); }
        string core = PathAt("src/CIS.Core");
        string[] sources = SourceFiles(core).Select(path => Path.GetRelativePath(core, path)).Order(StringComparer.Ordinal).ToArray();
        Assert.IsTrue(compiler.Sources.All(relative => sources.Contains(relative, StringComparer.Ordinal)), "A source control removed a captured Core input.");
        string[] added = sources.Except(compiler.Sources, StringComparer.Ordinal).ToArray();
        var referencePins = additionalReferences.ToDictionary(Path.GetFullPath, path => Hash(path), StringComparer.Ordinal);
        var sourcePins = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (string relative in sources)
            {
                string original = Path.Combine(core, relative), staged = Path.Combine(compiler.Directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!); File.Copy(original, staged, true);
                sourcePins.Add(relative, Hash(original)); Assert.AreEqual(Hash(original), Hash(staged));
            }
            string response = Path.Combine(compiler.Directory, "control.rsp");
            File.WriteAllText(response, string.Join(Environment.NewLine, compiler.Arguments.Where(value => value != "/noconfig")
                .Concat(referencePins.Keys.Select(path => "/reference:\"" + path + "\""))
                .Concat(added.Select(relative => "\"" + relative + "\""))) + Environment.NewLine, new UTF8Encoding(false));
            string responseHash = Hash(response);
            var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string prefix in new[] { "/out:", "/refout:", "/pdb:", "/doc:" })
            {
                foreach (string argument in compiler.Arguments.Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    string path = Path.GetFullPath(argument[prefix.Length..].Trim('"'), compiler.Directory);
                    Assert.IsTrue(path.StartsWith(compiler.Directory + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Delete(path); outputs.Add(prefix, path);
                }
            }
            if (!outputs.ContainsKey("/pdb:") && compiler.Arguments.Any(value => value.StartsWith("/debug:", StringComparison.OrdinalIgnoreCase) || value == "/debug+"))
            {
                string pdb = Path.ChangeExtension(outputs["/out:"], ".pdb"); File.Delete(pdb); outputs.Add("/pdb:", pdb);
            }
            string runtime = JsonNode.Parse(File.ReadAllText(PathAt("eng/toolchain.json")))!["targetRuntime"]!.GetValue<string>();
            await RequireAsync("dotnet", ["exec", "--fx-version", runtime, "--roll-forward", "Disable", compiler.Compiler, "/noconfig", "@" + response], compiler.Directory);
            Assert.AreEqual(responseHash, Hash(response));
            foreach ((string relative, string hash) in sourcePins)
            { Assert.AreEqual(hash, Hash(Path.Combine(core, relative))); Assert.AreEqual(hash, Hash(Path.Combine(compiler.Directory, relative))); }
            foreach ((string path, string hash) in compiler.FixedInputs) { Assert.AreEqual(hash, Hash(path), "Prepared compiler input changed during execution: " + path); }
            foreach ((string path, string hash) in referencePins) { Assert.AreEqual(hash, Hash(path), "The genuine added compiler reference changed during execution."); }
            string output = outputs["/out:"];
            Assert.IsTrue(File.Exists(output), "The genuine compiler did not emit the changed Core PE.");
            string bin = Path.Combine(core, "bin/Release/net10.0"); Directory.CreateDirectory(bin);
            foreach (string prefix in new[] { "/out:", "/pdb:", "/doc:" })
            {
                if (!outputs.TryGetValue(prefix, out string? file)) { continue; }
                string destination = Path.Combine(bin, Path.GetFileName(file)); File.Copy(file, destination, true);
                Assert.AreEqual(Hash(file), Hash(destination));
            }
            AddPackageEntry("lib/net10.0/CIS.Core.dll", File.ReadAllBytes(output));
            if (outputs.TryGetValue("/doc:", out string? documentation)) { AddPackageEntry("lib/net10.0/CIS.Core.xml", File.ReadAllBytes(documentation)); }
            Trace("compiler-control", "completed", [scenarioName, Hash(output), responseHash, JsonSerializer.Serialize(sourcePins)], 0, 0);
        }
        finally
        {
            foreach (string relative in compiler.Sources) { File.Copy(Path.Combine(compiler.SourceRoot, relative), Path.Combine(compiler.Directory, relative), true); }
            foreach (string relative in added) { File.Delete(Path.Combine(compiler.Directory, relative)); }
        }
    }

    private static IEnumerable<string> SourceFiles(string root) => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private async Task AddCompiledReferenceAsync()
    {
        string foreign = Path.Combine(temporary.Path, "foreign");
        Directory.CreateDirectory(foreign);
        string project = Path.Combine(foreign, "NLog.csproj");
        File.WriteAllText(Path.Combine(foreign, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(foreign, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(foreign, "Directory.Packages.props"), "<Project />");
        File.WriteAllText(Path.Combine(foreign, "NuGet.config"), "<configuration><packageSources><clear /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>");
        File.Copy(PathAt("global.json"), Path.Combine(foreign, "global.json"));
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>NLog</AssemblyName><TreatWarningsAsErrors>true</TreatWarningsAsErrors><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile><RuntimeFrameworkVersion>10.0.12</RuntimeFrameworkVersion><TargetLatestRuntimePatch>false</TargetLatestRuntimePatch></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(foreign, "Marker.cs"), "namespace NLog; public sealed class Marker {}\n");
        await RequireAsync("dotnet", ["build", project, "-c", "Release", "-m:1", "--nologo"], foreign);
        Write("src/CIS.Core/ArchitectureExternalDependency.cs", "namespace CIS.Logging; internal static class ArchitectureExternalDependency { internal static NLog.Marker Create() => new(); }\n");
        // Keep the actual foreign SDK build and genuine changed Core compilation.
        // The existing captured Csc context avoids another full Core build/pack;
        // the inspector must still detect the undeclared reference in the real PE.
        await CompileSourceViolationAsync([Path.Combine(foreign, "bin/Release/net10.0/NLog.dll")]);
    }

    private Task RestoreAsync(bool locked) => RequireAsync("dotnet", ["restore", CoreProject, locked ? "--locked-mode" : "--force-evaluate", "-m:1", "--nologo"]);

    private async Task BuildAndPackAsync(bool buildProjectReferences = false)
    {
        await RequireAsync("dotnet", ["build", CoreProject, "-c", "Release", "--no-restore", "-m:1", "--nologo",
            "-p:BuildProjectReferences=" + (buildProjectReferences ? "true" : "false")]);
        string packages = Path.Combine(repository, ".artifacts/packages");
        await RequireAsync("dotnet", ["pack", CoreProject, "-c", "Release", "--no-build", "--no-restore", "--output", packages, "--nologo"]);
        PackagePath = Directory.GetFiles(packages, "*.nupkg").Single();
    }

    private async Task<ProcessResult> RequireAsync(string executable, string[] arguments, string? workingDirectory = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Trace(executable, "started", arguments, 0);
        ProcessResult result;
        try { result = await ProcessRunner.RunAsync(executable, arguments, workingDirectory ?? language, TimeSpan.FromMinutes(3), executable == "git" ? gitEnvironment : ChildEnvironment); }
        catch (ProcessTimeoutException) { CountCommand(executable, arguments); Trace(executable, "timeout", arguments, clock.Elapsed.TotalSeconds); throw; }
        catch (ProcessOutputLimitException) { CountCommand(executable, arguments); Trace(executable, "output-limit", arguments, clock.Elapsed.TotalSeconds); throw; }
        CountCommand(executable, arguments);
        Trace(executable, "completed", arguments, clock.Elapsed.TotalSeconds, result.ExitCode);
        Assert.AreEqual(0, result.ExitCode, executable + " " + string.Join(' ', arguments) + "\n" + result.StandardOutput + result.StandardError);
        Assert.IsTrue(result.OutputDrained, "The architecture fixture child output did not drain.");
        return result;
    }

    private void Trace(string phase, string outcome, string[] arguments, double seconds, int? exitCode = null)
    {
        lock (work.TraceLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(work.TracePath)!);
            if (File.Exists(work.TracePath) && new FileInfo(work.TracePath).Length >= 256 * 1024) { return; }
            File.AppendAllText(work.TracePath, JsonSerializer.Serialize(new
            { schema = "cis.architecture-work-phase/v1", scenario = scenarioName, phase, outcome, arguments, seconds, exitCode, utc = DateTimeOffset.UtcNow }) + "\n");
        }
    }

    private void CountCommand(string executable, string[] arguments)
    {
        if (executable == "git")
        {
            Interlocked.Increment(ref work.GitInvocations);
            if (arguments[0] == "init") { Interlocked.Increment(ref work.GitPreparations); }
        }
        else if (executable == "dotnet")
        {
            switch (arguments[0])
            {
                case "restore": Interlocked.Increment(ref work.Restores); break;
                case "pack": Interlocked.Increment(ref work.CorePacks); break;
                case "build" when arguments[1] == CoreProject: Interlocked.Increment(ref work.CoreBuilds); break;
                case "build": Interlocked.Increment(ref work.AuxiliaryBuilds); break;
                case "msbuild": Interlocked.Increment(ref work.CompilerPreparations); break;
                case "exec" when arguments.Contains("/noconfig", StringComparer.Ordinal): Interlocked.Increment(ref work.CompilerInvocations); break;
            }
        }
    }

    internal string WorkSummary(bool cleanupCompleted) => JsonSerializer.Serialize(new
    {
        schema = "cis.architecture-work/v1",
        coreBuilds = Volatile.Read(ref work.CoreBuilds),
        corePacks = Volatile.Read(ref work.CorePacks),
        restores = Volatile.Read(ref work.Restores),
        gitPreparations = Volatile.Read(ref work.GitPreparations),
        gitInvocations = Volatile.Read(ref work.GitInvocations),
        positiveInspections = Volatile.Read(ref work.PositiveInspections),
        nativeInspectorInvocations = Volatile.Read(ref work.NativeInspectorInvocations),
        auxiliaryBuilds = Volatile.Read(ref work.AuxiliaryBuilds),
        compilerPreparations = Volatile.Read(ref work.CompilerPreparations),
        compilerInvocations = Volatile.Read(ref work.CompilerInvocations),
        fullInspections = Volatile.Read(ref work.FullInspections),
        preparedInspections = Volatile.Read(ref work.PreparedInspections),
        preparedGuardInspections = Volatile.Read(ref work.PreparedGuardInspections),
        msbuildEvaluationsCompleted = Volatile.Read(ref work.MsBuildEvaluations),
        packageOnlyCopies = Volatile.Read(ref work.PackageOnlyCopies),
        directNativeInvocations = Volatile.Read(ref work.CoreBuilds) + Volatile.Read(ref work.CorePacks) + Volatile.Read(ref work.Restores)
            + Volatile.Read(ref work.GitInvocations) + Volatile.Read(ref work.NativeInspectorInvocations) + Volatile.Read(ref work.AuxiliaryBuilds)
            + Volatile.Read(ref work.CompilerPreparations) + Volatile.Read(ref work.CompilerInvocations),
        scenarioCopies = Volatile.Read(ref work.ScenarioCopies),
        ownedCleanups = Volatile.Read(ref work.OwnedCleanups),
        cleanupCompleted,
        scope = "Direct native child invocations shared by the immutable baseline and scenario copies; internal MSBuild/compiler grandchildren are not counted."
    });

    private void CopyInputs()
    {
        foreach (string directory in new[] { "src/CIS.Core", "eng/CIS.TypeManager.Build", "build/type-manager" })
        {
            string source = Path.Combine(Original, directory);
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(source, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".vs")) { continue; }
                Copy(file, PathAt(directory + "/" + relative));
                copiedSourceHashes.Add(directory + "/" + relative.Replace('\\', '/') + " " + Hash(file));
            }
        }
        foreach (string file in new[] { "Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.config", ".config/dotnet-tools.json", "eng/project-boundaries.json", "eng/runtime-dependencies.json", "eng/development-dependencies.json", "eng/toolchain.json" })
        { Copy(Path.Combine(Original, file), PathAt(file)); }
        string originalRepository = Directory.GetParent(Directory.GetParent(Original)!.FullName)!.FullName;
        foreach (string file in new[] { "README.md", ".gitignore", ".gitattributes", ".editorconfig" }) { Copy(Path.Combine(originalRepository, file), Path.Combine(repository, file)); }
        JsonObject policy = JsonNode.Parse(File.ReadAllText(PathAt("eng/project-boundaries.json")))!.AsObject();
        JsonArray shipping = new(policy["projects"]!.AsArray().Where(item => item!["path"]!.GetValue<string>() is CoreProject or "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj").Select(item => item!.DeepClone()).ToArray());
        Assert.AreEqual(2, shipping.Count);
        policy["projects"] = shipping;
        File.WriteAllText(PathAt("eng/project-boundaries.json"), policy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private void AddProjectProperty(string name, string value)
    {
        string path = PathAt(CoreProject);
        XDocument document = XDocument.Load(path);
        document.Root!.Add(new XElement("PropertyGroup", new XElement(name, value)));
        document.Save(path);
    }

    private void AddProjectItem(XElement item)
    {
        string path = PathAt(CoreProject);
        XDocument document = XDocument.Load(path);
        document.Root!.Add(new XElement("ItemGroup", item));
        document.Save(path);
    }

    private void ReplaceExactlyOnce(string relative, string before, string after)
    {
        string path = PathAt(relative), source = File.ReadAllText(path);
        Assert.AreEqual(1, source.Split(before, StringSplitOptions.None).Length - 1, "Source mutation requires one exact anchor: " + relative + " " + before);
        string changed = source.Replace(before, after, StringComparison.Ordinal);
        Assert.AreNotEqual(source, changed, "Source mutation must change its input.");
        File.WriteAllText(path, changed);
    }

    private void AppendClassMember(string relative, string member)
    {
        string path = PathAt(relative), source = File.ReadAllText(path);
        Assert.IsTrue(source.Contains("namespace CIS.Logging;", StringComparison.Ordinal), "Expected the pinned file-scoped namespace.");
        Assert.AreEqual(1, Regex.Matches(source, @"(?m)^public sealed (?:partial )?class " + Regex.Escape(Path.GetFileNameWithoutExtension(path)) + @"(?: : [^\r\n]+)?\r?$").Count, "Expected one intended top-level class.");
        Assert.AreEqual(1, Regex.Matches(source, @"(?m)^}\r?$").Count, "Expected one unindented top-level class close.");
        Assert.IsTrue(source.TrimEnd().EndsWith('}'));
        int closing = source.LastIndexOf('}');
        File.WriteAllText(path, source.Insert(closing, "\n    " + member + "\n"));
    }

    private void AddPackageEntry(string name, byte[] bytes)
    {
        using ZipArchive archive = ZipFile.Open(PackagePath, ZipArchiveMode.Update);
        archive.GetEntry(name)?.Delete();
        using Stream stream = archive.CreateEntry(name).Open();
        stream.Write(bytes);
    }

    private void Write(string relative, string contents) { string path = PathAt(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents); }
    private string PathAt(string relative) => Path.Combine(language, relative);
    private static void Copy(string source, string destination) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination); }
    internal static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    public void Dispose()
    {
        compilerFixture?.Dispose(); compilerFixture = null;
        if (!Directory.Exists(temporary.Path)) { return; }
        // Git object files are readonly on Windows; normalize only this fixture's owned tree before cleanup.
        foreach (string file in Directory.EnumerateFiles(temporary.Path, "*", SearchOption.AllDirectories))
        {
            FileAttributes attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0) { File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly); }
        }
        temporary.Dispose();
        if (scenarioName == "baseline") { work.CompilerTurn.Dispose(); }
        Interlocked.Increment(ref work.OwnedCleanups);
    }
    private static string FindLanguageRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "implementations/csharp");
            if (File.Exists(Path.Combine(candidate, "CisLib.slnx"))) { return candidate; }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Run inside the CisLib checkout.");
    }
}
