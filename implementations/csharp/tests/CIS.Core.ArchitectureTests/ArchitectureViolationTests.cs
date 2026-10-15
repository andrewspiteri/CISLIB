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
[DoNotParallelize]
public sealed class ArchitectureViolationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task NormalInspectorAcceptsTheActualShippingClosureFixture()
    {
        using ArchitectureViolationFixture fixture = await ArchitectureViolationFixture.CreateAsync();
        JsonObject positive = await fixture.AssertPositiveAsync();
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
    public async Task NormalInspectorRejectsRealIsolatedViolations(string scenario, string diagnostic, string expectedContext)
    {
        using ArchitectureViolationFixture fixture = await ArchitectureViolationFixture.CreateAsync();
        JsonObject positive = await fixture.AssertPositiveAsync();
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
            ["positive"] = positive,
            ["negative"] = new JsonObject { ["exitCode"] = negative.ExitCode, ["diagnostic"] = diagnostic, ["expectedContext"] = expectedContext, ["standardError"] = negative.StandardError, ["successfulReportRetained"] = false, ["packageSha256"] = ArchitectureViolationFixture.Hash(fixture.PackagePath) }
        };
        TestContext.WriteLine("CIS-ARCH-FIXTURE " + evidence.ToJsonString());
    }
}

// Each row owns a fresh repository containing the real complete two-project shipping closure.
// Trusted fixture registration omits nonshipping projects; normal Developer inspection still covers the full repository.
internal sealed class ArchitectureViolationFixture : IDisposable
{
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
    private readonly List<string> copiedSourceHashes = [];
    internal string PackagePath { get; private set; } = "";
    internal string ReportPath => Path.Combine(output, "inspection.json");

    private ArchitectureViolationFixture()
    {
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
        language = Path.Combine(repository, "implementations/csharp");
        output = Path.Combine(repository, ".artifacts/inspection");
    }

    internal static async Task<ArchitectureViolationFixture> CreateAsync()
    {
        var fixture = new ArchitectureViolationFixture();
        try
        {
            Assert.IsTrue(File.Exists(Path.Combine(Original, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll")), "Build the complete solution in Release before these tests; Developer verification does this before running them.");
            fixture.CopyInputs();
            await fixture.RequireAsync("git", ["init", "--quiet", "--template="], fixture.repository);
            await fixture.RequireAsync("git", ["-c", "core.autocrlf=false", "add", "--all"], fixture.repository);
            await fixture.RequireAsync("git", ["-c", "user.name=CISLIB Verification", "-c", "user.email=verification@localhost", "-c", "commit.gpgsign=false", "commit", "--quiet", "--no-verify", "-m", "Architecture fixture inputs"], fixture.repository);
            await fixture.RestoreAsync(locked: true);
            await fixture.BuildAndPackAsync();
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    internal async Task<JsonObject> AssertPositiveAsync()
    {
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

    internal Task<ProcessResult> InspectAsync() => ProcessRunner.RunAsync("dotnet",
        [Path.Combine(Original, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll"), "architecture", language, PackagePath, output], language, TimeSpan.FromMinutes(10), ChildEnvironment);

    internal async Task IntroduceAsync(string scenario)
    {
        switch (scenario)
        {
            case "project-inventory": Write("shadow/Shadow.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"); break;
            case "framework":
                // Keep the normal locked package closure; let the architecture checker diagnose the forbidden framework.
                AddProjectProperty("RestoreEnablePackagePruning", "false");
                AddProjectItem(new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")));
                await RestoreAsync(locked: false); await BuildAndPackAsync(); break;
            case "private-runtime-package":
                AddProjectItem(new XElement("PackageReference", new XAttribute("Include", "Microsoft.Extensions.Logging"), new XAttribute("PrivateAssets", "all")));
                await RestoreAsync(locked: false); await BuildAndPackAsync(); break;
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
                await BuildAndPackAsync(); break;
            case "mutable-inheritance":
                ReplaceExactlyOnce("src/CIS.Core/Logging/EventMetadata.cs", "public sealed class EventMetadata", "public sealed class EventMetadata : ArchitectureMutableBase");
                Write("src/CIS.Core/ArchitectureMutableBase.cs", "namespace CIS.Logging;\n/// <summary>Deliberately mutable qualification base.</summary>\npublic class ArchitectureMutableBase {\n/// <summary>Deliberately mutable inherited state.</summary>\npublic string State { get; set; } = string.Empty;\n}\n");
                await BuildAndPackAsync(); break;
            case "environment-storage":
                ReplaceExactlyOnce("src/CIS.Core/Environment/EnvironmentOptions.cs", "public static string ApplicationName { get; }", "private static string architectureApplicationName = string.Empty;\n    /// <summary>Deliberately mutable identity view.</summary>\n    public static string ApplicationName => architectureApplicationName;");
                ReplaceExactlyOnce("src/CIS.Core/Environment/EnvironmentOptions.cs", "ApplicationName = application?.GetName().Name", "architectureApplicationName = application?.GetName().Name");
                ReplaceExactlyOnce("src/CIS.Core/Environment/EnvironmentOptions.cs", "(ApplicationName, EnvironmentName, ServiceVersion) =", "(architectureApplicationName, EnvironmentName, ServiceVersion) =");
                ReplaceExactlyOnce("src/CIS.Core/Environment/EnvironmentOptions.cs", "ApplicationName = string.Intern(ApplicationName);", "architectureApplicationName = string.Intern(architectureApplicationName);");
                await BuildAndPackAsync(); break;
            case "nested-caller-state":
                AppendClassMember("src/CIS.Core/Logging/CoreLogRuntime.cs", "internal sealed class ArchitectureCallerState { internal readonly LoggingOptions Caller = new(); }");
                await BuildAndPackAsync(); break;
            default: throw new ArgumentException("Unknown architecture fixture.", nameof(scenario));
        }
    }

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
        string original = File.ReadAllText(PathAt(CoreProject));
        AddProjectItem(new XElement("Reference", new XAttribute("Include", "NLog"), new XElement("HintPath", Path.Combine(foreign, "bin/Release/net10.0/NLog.dll")), new XElement("Private", "false")));
        Write("src/CIS.Core/ArchitectureExternalDependency.cs", "namespace CIS.Logging; internal static class ArchitectureExternalDependency { internal static NLog.Marker Create() => new(); }\n");
        await BuildAndPackAsync();
        // An undeclared compiled reference must be detected from the actual PE, even after its build-time reference item is removed.
        File.WriteAllText(PathAt(CoreProject), original);
    }

    private Task RestoreAsync(bool locked) => RequireAsync("dotnet", ["restore", CoreProject, locked ? "--locked-mode" : "--force-evaluate", "-m:1", "--nologo"]);

    private async Task BuildAndPackAsync()
    {
        await RequireAsync("dotnet", ["build", CoreProject, "-c", "Release", "--no-restore", "-m:1", "--nologo"]);
        string packages = Path.Combine(repository, ".artifacts/packages");
        await RequireAsync("dotnet", ["pack", CoreProject, "-c", "Release", "--no-build", "--no-restore", "--output", packages, "--nologo"]);
        PackagePath = Directory.GetFiles(packages, "*.nupkg").Single();
    }

    private async Task RequireAsync(string executable, string[] arguments, string? workingDirectory = null)
    {
        ProcessResult result = await ProcessRunner.RunAsync(executable, arguments, workingDirectory ?? language, TimeSpan.FromMinutes(3), executable == "git" ? gitEnvironment : ChildEnvironment);
        Assert.AreEqual(0, result.ExitCode, executable + " " + string.Join(' ', arguments) + "\n" + result.StandardOutput + result.StandardError);
    }

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
        if (!Directory.Exists(temporary.Path)) { return; }
        // Git object files are readonly on Windows; normalize only this fixture's owned tree before cleanup.
        foreach (string file in Directory.EnumerateFiles(temporary.Path, "*", SearchOption.AllDirectories))
        {
            FileAttributes attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0) { File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly); }
        }
        temporary.Dispose();
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
