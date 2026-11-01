using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class DependencyBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string LanguageRoot = Path.Combine(RepositoryRoot, "implementations", "csharp");

    [TestMethod]
    public async Task ReleaseCorePassesTheEvaluatedCompiledReferencePolicy()
    {
        string core = Path.Combine(LanguageRoot, "src", "CIS.Core", "bin", "Release", "net10.0", "CIS.Core.dll");
        DependencyCheckRunner.CheckResult result = await RunProjectCheck(LanguageRoot, "-AssemblyPath", core);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
    }

    [TestMethod]
    [DataRow("CIS.Core.TestSupport")]
    [DataRow("Unapproved.Host.Infrastructure")]
    [DataRow("Microsoft.Extensions.Logging")]
    [DataRow("NLog")]
    public async Task CompiledCheckerRejectsForeignAssemblyReferences(string dependencyName)
    {
        using var directory = new TemporaryDirectory();
        string fixture = Path.Combine(directory.Path, "CIS.Core.dll");
        WriteAssemblyFixture(fixture, dependencyName);
        string allowlist = Path.Combine(directory.Path, "allowed.json");
        File.WriteAllText(allowlist, "[\"System.Runtime\"]");
        string checker = Path.Combine(LanguageRoot, "eng", "CIS.DependencyChecker", "bin", "Release", "net10.0", "CIS.DependencyChecker.dll");

        ProcessResult result = await ProcessRunner.RunAsync("dotnet", [checker, fixture, allowlist], LanguageRoot, TimeSpan.FromSeconds(30));

        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.StandardError, "Unapproved compiled assembly reference");
        StringAssert.Contains(result.StandardError, dependencyName);
    }

    [TestMethod]
    [DataRow("ProjectReference", "../../../tests/CIS.Core.TestSupport/CIS.Core.TestSupport.csproj", "Forbidden project reference")]
    [DataRow("FrameworkReference", "Unapproved.Framework", "Forbidden framework reference")]
    [DataRow("PackageReference", "Unapproved.Logger", "Unapproved explicit package")]
    [DataRow("PackageReference", "Microsoft.Extensions.Logging", "Unapproved explicit package")]
    public async Task EvaluatedCoreProjectRejectsForbiddenDependencies(string element, string dependency, string expectedRule)
    {
        using var fixture = new ProjectFixture(LanguageRoot);
        string coreProject = Path.Combine(fixture.Path, "src", "CIS.Core", "CIS.Core.csproj");
        XDocument project = XDocument.Load(coreProject);
        project.Root!.Add(new XElement("ItemGroup", new XElement(element, new XAttribute("Include", dependency))));
        project.Save(coreProject);

        DependencyCheckRunner.CheckResult result = await RunProjectCheck(fixture.Path);

        AssertRejected(result, expectedRule);
    }

    [TestMethod]
    public async Task ImportedProjectReferenceCannotBypassTheBoundary()
    {
        using var fixture = new ProjectFixture(LanguageRoot);
        string propsPath = Path.Combine(fixture.Path, "Directory.Build.props");
        XDocument props = XDocument.Load(propsPath);
        props.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../../tests/CIS.Core.TestSupport/CIS.Core.TestSupport.csproj"))));
        props.Save(propsPath);

        AssertRejected(await RunProjectCheck(fixture.Path), "Forbidden project reference");
    }

    [TestMethod]
    [DataRow("Unapproved.Logger/1.0.0")]
    [DataRow("Microsoft.Extensions.Logging.Abstractions/10.0.999")]
    public async Task RestoredGraphRejectsUnapprovedIdentityOrVersion(string identity)
    {
        DependencyCheckRunner.CheckResult result = await DependencyCheckRunner.MetadataResult(LanguageRoot, typeof(DependencyBoundaryTests),
            nameof(RestoredGraphRejectsUnapprovedIdentityOrVersion), [identity], check => RestoredGraphRejectsUnapprovedIdentityOrVersionInputs(identity, check));
        AssertRejected(result, "Unapproved shipping package dependency");
    }

    [TestMethod]
    public async Task PackageRejectsBundledTestAssembly()
    {
        DependencyCheckRunner.CheckResult result = await DependencyCheckRunner.MetadataResult(LanguageRoot, typeof(DependencyBoundaryTests),
            nameof(PackageRejectsBundledTestAssembly), [], check => PackageRejectsBundledTestAssemblyInputs(check));
        AssertRejected(result, "Unexpected packed asset");
    }

    [TestMethod]
    [DataRow("readme.md", "Duplicate packed asset")]
    [DataRow("LIB/NET10.0/CIS.CORE.XML", "Duplicate packed asset")]
    [DataRow("cis.core.nuspec", "Duplicate packed asset")]
    [DataRow("_RELS/.RELS", "Duplicate packed asset")]
    [DataRow("PACKAGE/services/metadata/core-properties/foreign.psmdcp", "Unexpected packed asset")]
    public async Task PackageNamesAreExactAndCannotRepeatIgnoringCase(string extra, string rule)
    {
        DependencyCheckRunner.CheckResult result = await DependencyCheckRunner.MetadataResult(LanguageRoot, typeof(DependencyBoundaryTests),
            nameof(PackageNamesAreExactAndCannotRepeatIgnoringCase), [extra, rule], check => PackageNamesAreExactAndCannotRepeatIgnoringCaseInputs(extra, check));
        AssertRejected(result, rule);
    }

    [TestMethod]
    [DataRow("ProjectReference")]
    [DataRow("PackageReference")]
    [DataRow("FrameworkReference")]
    public async Task DuplicateEvaluatedReferencesAreRejected(string kind)
    {
        using var fixture = new ProjectFixture(LanguageRoot);
        string coreProject = Path.Combine(fixture.Path, "src/CIS.Core/CIS.Core.csproj");
        XDocument project = XDocument.Load(coreProject);
        project.Root!.Add(new XElement("ItemGroup", new XElement(project.Descendants(kind).Single())));
        project.Save(coreProject);
        string expected = kind switch
        {
            "ProjectReference" => "Duplicate project reference",
            "PackageReference" => "Duplicate package reference",
            _ => "Duplicate framework reference",
        };
        AssertRejected(await RunProjectCheck(fixture.Path), expected);
    }

    [TestMethod]
    [DataRow("missing", "Framework reference inventory mismatch")]
    [DataRow("forged-implicit", "Forbidden framework reference")]
    public async Task ApprovedExplicitFrameworkMustBePresentAndExplicit(string mode, string rule)
    {
        using var fixture = new ProjectFixture(LanguageRoot);
        string coreProject = Path.Combine(fixture.Path, "src/CIS.Core/CIS.Core.csproj");
        XDocument project = XDocument.Load(coreProject);
        XElement framework = project.Descendants("FrameworkReference").Single();
        if (mode == "missing") { framework.Remove(); }
        else { framework.SetAttributeValue("IsImplicitlyDefined", "true"); }
        project.Save(coreProject);
        AssertRejected(await RunProjectCheck(fixture.Path), rule);
    }

    [TestMethod]
    public async Task DuplicateNuspecDependenciesAreRejected()
    {
        DependencyCheckRunner.CheckResult result = await DependencyCheckRunner.MetadataResult(LanguageRoot, typeof(DependencyBoundaryTests),
            nameof(DuplicateNuspecDependenciesAreRejected), [], check => DuplicateNuspecDependenciesAreRejectedInputs(check));
        AssertRejected(result, "Duplicate packed dependency");
    }

    [TestMethod]
    [DataRow("missing", "Packed framework reference inventory mismatch")]
    [DataRow("changed", "Packed framework reference inventory mismatch")]
    [DataRow("duplicate", "Invalid or duplicate packed framework reference")]
    public async Task PackageFrameworkRequirementIsExact(string mode, string rule)
    {
        DependencyCheckRunner.CheckResult result = await DependencyCheckRunner.MetadataResult(LanguageRoot, typeof(DependencyBoundaryTests),
            nameof(PackageFrameworkRequirementIsExact), [mode, rule], check => PackageFrameworkRequirementIsExactInputs(mode, check));
        AssertRejected(result, rule);
    }

    internal static void AddMetadataBatchCases(List<DependencyCheckRunner.MetadataCase> cases)
    {
        DependencyCheckRunner.AddMetadataCases(cases, typeof(DependencyBoundaryTests), nameof(RestoredGraphRejectsUnapprovedIdentityOrVersion),
            (values, check) => RestoredGraphRejectsUnapprovedIdentityOrVersionInputs(values[0], check), 1);
        DependencyCheckRunner.AddMetadataCases(cases, typeof(DependencyBoundaryTests), nameof(PackageRejectsBundledTestAssembly),
            (values, check) => PackageRejectsBundledTestAssemblyInputs(check), 1);
        DependencyCheckRunner.AddMetadataCases(cases, typeof(DependencyBoundaryTests), nameof(PackageNamesAreExactAndCannotRepeatIgnoringCase),
            (values, check) => PackageNamesAreExactAndCannotRepeatIgnoringCaseInputs(values[0], check), 1);
        DependencyCheckRunner.AddMetadataCases(cases, typeof(DependencyBoundaryTests), nameof(DuplicateNuspecDependenciesAreRejected),
            (values, check) => DuplicateNuspecDependenciesAreRejectedInputs(check), 1);
        DependencyCheckRunner.AddMetadataCases(cases, typeof(DependencyBoundaryTests), nameof(PackageFrameworkRequirementIsExact),
            (values, check) => PackageFrameworkRequirementIsExactInputs(values[0], check), 1);
    }

    private static async Task<DependencyCheckRunner.CheckResult> RestoredGraphRejectsUnapprovedIdentityOrVersionInputs(string identity, Func<string[], Task<DependencyCheckRunner.CheckResult>> check)
    {
        using var directory = new TemporaryDirectory();
        string assetsPath = Path.Combine(LanguageRoot, "src", "CIS.Core", "obj", "project.assets.json");
        JsonObject assets = JsonNode.Parse(File.ReadAllText(assetsPath))!.AsObject();
        assets["targets"]!["net10.0"]!.AsObject()[identity] = new JsonObject { ["type"] = "package" };
        string modified = Path.Combine(directory.Path, "project.assets.json");
        File.WriteAllText(modified, assets.ToJsonString());

        return await check(["-AssetsPath", modified]);
    }

    private static async Task<DependencyCheckRunner.CheckResult> PackageRejectsBundledTestAssemblyInputs(Func<string[], Task<DependencyCheckRunner.CheckResult>> check)
    {
        using var directory = new TemporaryDirectory();
        string packagePath = Path.Combine(directory.Path, "fixture.nupkg");
        File.Copy(VerificationCheckpoint.Package(RepositoryRoot), packagePath);
        using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("lib/net10.0/CIS.Core.TestSupport.dll").Open());
            writer.Write("explicitly forbidden synthetic asset");
        }

        return await check(["-PackagePath", packagePath]);
    }

    private static async Task<DependencyCheckRunner.CheckResult> PackageNamesAreExactAndCannotRepeatIgnoringCaseInputs(string extra, Func<string[], Task<DependencyCheckRunner.CheckResult>> check)
    {
        using var directory = new TemporaryDirectory();
        string packagePath = CopyCorePackage(directory.Path);
        using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry(extra).Open());
            writer.Write("synthetic extra asset");
        }
        return await check(["-PackagePath", packagePath]);
    }

    private static async Task<DependencyCheckRunner.CheckResult> DuplicateNuspecDependenciesAreRejectedInputs(Func<string[], Task<DependencyCheckRunner.CheckResult>> check)
    {
        using var directory = new TemporaryDirectory();
        string packagePath = CopyCorePackage(directory.Path);
        using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("CIS.Core.nuspec")!;
            XDocument spec;
            using (Stream data = entry.Open()) { spec = XDocument.Load(data); }
            XElement[] dependencies = spec.Descendants().Where(e => e.Name.LocalName == "dependency").ToArray();
            Assert.AreEqual(1, dependencies.Length);
            dependencies[0].AddAfterSelf(new XElement(dependencies[0]));
            entry.Delete();
            using Stream output = archive.CreateEntry("CIS.Core.nuspec").Open();
            spec.Save(output);
        }
        return await check(["-PackagePath", packagePath]);
    }

    private static async Task<DependencyCheckRunner.CheckResult> PackageFrameworkRequirementIsExactInputs(string mode, Func<string[], Task<DependencyCheckRunner.CheckResult>> check)
    {
        using var directory = new TemporaryDirectory();
        string packagePath = CopyCorePackage(directory.Path);
        using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("CIS.Core.nuspec")!;
            XDocument spec;
            using (Stream data = entry.Open()) { spec = XDocument.Load(data); }
            XElement framework = spec.Descendants().Single(element => element.Name.LocalName == "frameworkReference");
            if (mode == "missing") { framework.Remove(); }
            else if (mode == "changed") { framework.SetAttributeValue("name", "Unapproved.Framework"); }
            else { framework.AddAfterSelf(new XElement(framework)); }
            entry.Delete();
            using Stream output = archive.CreateEntry("CIS.Core.nuspec").Open();
            spec.Save(output);
        }
        return await check(["-PackagePath", packagePath]);
    }

    private static string CopyCorePackage(string root)
    {
        string target = Path.Combine(root, "fixture.nupkg");
        File.Copy(VerificationCheckpoint.Package(RepositoryRoot), target);
        return target;
    }
    private static Task<DependencyCheckRunner.CheckResult> RunProjectCheck(string root, params string[] extraArguments)
    {
        return DependencyCheckRunner.Run(LanguageRoot, root, "src/CIS.Core/CIS.Core.csproj", extraArguments);
    }

    private static void AssertRejected(DependencyCheckRunner.CheckResult result, string expectedRule)
    {
        Assert.AreNotEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        StringAssert.Contains(result.StandardError, expectedRule);
    }

    private static void WriteAssemblyFixture(string path, string dependencyName)
    {
        // A real managed PE fixture with an intentionally forbidden AssemblyRef; no shipping source is modified.
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("CIS.Core.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("CIS.Core"), new Version(1, 0, 0, 0), default, default, (AssemblyFlags)0, AssemblyHashAlgorithm.None);
        metadata.AddAssemblyReference(metadata.GetOrAddString(dependencyName), new Version(1, 0, 0, 0), default, default, (AssemblyFlags)0, default);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var builder = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll), new MetadataRootBuilder(metadata), new BlobBuilder());
        var image = new BlobBuilder();
        builder.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "implementations", "csharp", "CisLib.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run the architecture tests from the CisLib checkout.");
    }
}
