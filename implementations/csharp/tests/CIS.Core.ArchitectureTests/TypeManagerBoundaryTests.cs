using System.IO.Compression;
using System.Xml.Linq;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class TypeManagerBoundaryTests
{
    private static readonly string LanguageRoot = FindLanguageRoot();
    private const string ToolProject = "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj";
    private const string HostProject = "tests/fixtures/TypeManager/Host/Host.csproj";
    private const string BuildTargets = "build/type-manager/CIS.TypeManager.targets";

    [TestMethod]
    public async Task PinnedSdkBuildToolPassesWithoutRuntimePackages()
    {
        DependencyCheckRunner.CheckResult result = await RunCheck(LanguageRoot, ToolProject);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
    }

    [TestMethod]
    [DataRow("Private", "true")]
    [DataRow("HintPath", "$(MSBuildProjectDirectory)/Microsoft.Build.Framework.dll")]
    public async Task BuildToolRejectsCopiedOrUnapprovedSdkReferences(string attribute, string value)
    {
        using var fixture = new ProjectFixture(LanguageRoot);
        CopyFile(fixture.Path, ToolProject);
        var project = XDocument.Load(Path.Combine(fixture.Path, ToolProject));
        project.Descendants("Reference").First().SetAttributeValue(attribute, value);
        project.Save(Path.Combine(fixture.Path, ToolProject));

        DependencyCheckRunner.CheckResult result = await RunCheck(fixture.Path, ToolProject);

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.StandardError, "Unapproved pinned-SDK build-tool reference");
    }

    [TestMethod]
    [DataRow("ReferenceOutputAssembly")]
    [DataRow("Private")]
    [DataRow("PrivateAssets")]
    public async Task BuildToolCannotBecomeAHostRuntimeReference(string attribute)
    {
        using var fixture = new ProjectFixture(LanguageRoot);
        CopyFile(fixture.Path, HostProject);
        CopyFile(fixture.Path, BuildTargets);
        var targets = XDocument.Load(Path.Combine(fixture.Path, BuildTargets));
        targets.Descendants("ProjectReference").Single().SetAttributeValue(attribute, "true");
        targets.Save(Path.Combine(fixture.Path, BuildTargets));

        DependencyCheckRunner.CheckResult result = await RunCheck(fixture.Path, HostProject);

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.StandardError, "Build-only project must not become a runtime reference");
    }

    [TestMethod]
    [DataRow("ReferenceOutputAssembly")]
    [DataRow("Private")]
    [DataRow("PrivateAssets")]
    public async Task PackageBuildToolCannotBecomeACoreRuntimeReference(string attribute)
    {
        using var fixture = new ProjectFixture(LanguageRoot);
        const string core = "src/CIS.Core/CIS.Core.csproj";
        var project = XDocument.Load(Path.Combine(fixture.Path, core));
        project.Descendants("ProjectReference").Single().SetAttributeValue(attribute, "true");
        project.Save(Path.Combine(fixture.Path, core));

        DependencyCheckRunner.CheckResult result = await RunCheck(fixture.Path, core);

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.StandardError, "Build-only project must not become a runtime reference");
    }

    [TestMethod]
    [DataRow("unchanged", "")]
    [DataRow("missing-sdk", "Type Manager package asset is missing or duplicated")]
    [DataRow("changed-target", "Packed Type Manager build asset differs from source")]
    [DataRow("changed-helper", "Packed Type Manager helper differs from the checked Release tool")]
    [DataRow("changed-core", "Packed Core DLL differs from the checked Release assembly")]
    [DataRow("changed-version", "Type Manager SDK and runtime package versions differ")]
    public async Task PackageRejectsMissingOrChangedTypeManagerBuildAssets(string change, string expected)
    {
        DependencyCheckRunner.CheckResult result = await DependencyCheckRunner.MetadataResult(LanguageRoot, typeof(TypeManagerBoundaryTests),
            nameof(PackageRejectsMissingOrChangedTypeManagerBuildAssets), [change, expected], check => PackageRejectsMissingOrChangedTypeManagerBuildAssetsInputs(change, check));

        if (change == "unchanged")
        {
            Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
            return;
        }
        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.StandardError, expected);
    }

    internal static void AddMetadataBatchCases(List<DependencyCheckRunner.MetadataCase> cases)
    {
        DependencyCheckRunner.AddMetadataCases(cases, typeof(TypeManagerBoundaryTests), nameof(PackageRejectsMissingOrChangedTypeManagerBuildAssets),
            (values, check) => PackageRejectsMissingOrChangedTypeManagerBuildAssetsInputs(values[0], check), 1);
    }

    private static async Task<DependencyCheckRunner.CheckResult> PackageRejectsMissingOrChangedTypeManagerBuildAssetsInputs(string change,
        Func<string[], Task<DependencyCheckRunner.CheckResult>> check)
    {
        using var directory = new TemporaryDirectory();
        string core = Path.Combine(LanguageRoot, "src", "CIS.Core", "bin", "Release", "net10.0", "CIS.Core.dll");
        string package = Path.Combine(directory.Path, "candidate.nupkg");
        File.Copy(VerificationCheckpoint.Package(Path.GetFullPath("../..", LanguageRoot)), package);
        if (change != "unchanged")
        {
            using var archive = ZipFile.Open(package, ZipArchiveMode.Update);
            string entry = change switch
            {
                "missing-sdk" => "Sdk/Sdk.props",
                "changed-target" => "buildSupport/CIS.TypeManager.targets",
                "changed-helper" => "tools/net10.0/CIS.TypeManager.Build.dll",
                "changed-core" => "lib/net10.0/CIS.Core.dll",
                _ => "Sdk/PackageVersion.props"
            };
            archive.GetEntry(entry)!.Delete();
            if (change == "changed-core")
            {
                // A trailing byte preserves valid PE metadata while changing the
                // artifact hash, so the exact-byte check must catch this case.
                using var output = archive.CreateEntry(entry).Open();
                output.Write(File.ReadAllBytes(core));
                output.WriteByte(0);
            }
            else if (change != "missing-sdk")
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
                writer.Write(change == "changed-version"
                    ? "<Project><PropertyGroup><_CisCoreSdkPackageVersion>99.0.0</_CisCoreSdkPackageVersion></PropertyGroup></Project>"
                    : "deliberately changed package asset");
            }
        }
        return await check(["-PackagePath", package, "-AssemblyPath", core]);
    }

    private static Task<DependencyCheckRunner.CheckResult> RunCheck(string root, string project)
    {
        return DependencyCheckRunner.Run(LanguageRoot, root, project);
    }

    private static void CopyFile(string root, string relativePath)
    {
        string destination = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.Combine(LanguageRoot, relativePath), destination);
    }

    private static string FindLanguageRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CisLib.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Run Type Manager boundary tests from the C# solution.");
    }
}
