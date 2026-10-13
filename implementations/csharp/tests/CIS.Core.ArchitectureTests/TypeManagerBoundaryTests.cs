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

    private static Task<ProcessResult> RunCheck(string root, string project)
    {
        string script = Path.Combine(LanguageRoot, "eng", "check-dependencies.ps1");
        return ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script, "-LanguageRoot", root,
            "-ProjectPath", project], root, TimeSpan.FromSeconds(60));
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
