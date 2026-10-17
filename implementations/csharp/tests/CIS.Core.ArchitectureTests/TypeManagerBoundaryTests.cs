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
        ProcessResult result = await RunCheck(LanguageRoot, ToolProject);
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

        ProcessResult result = await RunCheck(fixture.Path, ToolProject);

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

        ProcessResult result = await RunCheck(fixture.Path, HostProject);

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

        ProcessResult result = await RunCheck(fixture.Path, core);

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
        using var directory = new TemporaryDirectory();
        string core = Path.Combine(LanguageRoot, "src", "CIS.Core", "bin", "Release", "net10.0", "CIS.Core.dll");
        string version = System.Diagnostics.FileVersionInfo.GetVersionInfo(core).ProductVersion!.Split('+', 2)[0];
        string package = Path.Combine(directory.Path, "candidate.nupkg");
        File.Copy(Path.Combine(LanguageRoot, "..", "..", ".artifacts", "csharp", "packages", $"CIS.Core.{version}.nupkg"), package);
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
        ProcessResult result = await DependencyCheckRunner.Run(LanguageRoot, LanguageRoot, "src/CIS.Core/CIS.Core.csproj", "-PackagePath", package, "-AssemblyPath", core);

        if (change == "unchanged")
        {
            Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
            return;
        }
        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.StandardError, expected);
    }

    private static Task<ProcessResult> RunCheck(string root, string project)
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
