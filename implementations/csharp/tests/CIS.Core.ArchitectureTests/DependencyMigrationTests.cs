using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class DependencyMigrationTests
{
    private static readonly string Language = FindLanguage();

    [TestMethod]
    [DataRow("TargetFramework", "net9.0", "Unexpected target framework")]
    [DataRow("IsPackable", "false", "Unexpected packability")]
    [DataRow("AssemblyName", "Other.Core", "Unexpected shipping identity AssemblyName")]
    [DataRow("PackageId", "Other.Core", "Unexpected shipping identity PackageId")]
    [DataRow("RootNamespace", "Other", "Unexpected shipping identity RootNamespace")]
    public async Task EvaluatedShippingSettingsRetainTheirBoundaries(string name, string value, string reason)
    {
        using var fixture = new ProjectFixture(Language);
        string path = Path.Combine(fixture.Path, "src/CIS.Core/CIS.Core.csproj");
        XDocument project = XDocument.Load(path);
        project.Root!.Add(new XElement("PropertyGroup", new XElement(name, value)));
        project.Save(path);
        Reject(await Check(fixture.Path), reason);
    }

    [TestMethod]
    [DataRow("missing-target", "Missing restored target")]
    [DataRow("missing-direct", "Required restored package is missing")]
    [DataRow("unknown-project", "Unapproved restored project dependency")]
    [DataRow("unknown-type", "Unexpected restored dependency type")]
    public async Task RestoredInventoryDoesNotLoseRequiredOrUnexpectedEntries(string mode, string reason)
    {
        using var fixture = new TemporaryDirectory();
        JsonObject assets = JsonNode.Parse(File.ReadAllText(Path.Combine(Language, "src/CIS.Core/obj/project.assets.json")))!.AsObject();
        JsonObject target = assets["targets"]!["net10.0"]!.AsObject();
        const string direct = "Microsoft.Extensions.Logging.Abstractions/10.0.12";
        if (mode == "missing-target") { assets["targets"]!.AsObject().Remove("net10.0"); }
        if (mode == "missing-direct") { target.Remove(direct); }
        if (mode == "unknown-type") { target[direct]!["type"] = "unapproved"; }
        if (mode == "unknown-project")
        {
            target["Foreign/1.0.0"] = new JsonObject { ["type"] = "project" };
            assets["libraries"]!["Foreign/1.0.0"] = new JsonObject { ["msbuildProject"] = "Foreign.csproj" };
        }
        string path = Path.Combine(fixture.Path, "assets.json");
        File.WriteAllText(path, assets.ToJsonString());
        Reject(await DependencyCheckRunner.Run(Language, Language, "src/CIS.Core/CIS.Core.csproj", "-AssetsPath", path), reason);
    }

    [TestMethod]
    public async Task ManualCoreReferenceIsStillForbidden()
    {
        using var fixture = new ProjectFixture(Language);
        string path = Path.Combine(fixture.Path, "src/CIS.Core/CIS.Core.csproj");
        XDocument project = XDocument.Load(path);
        project.Root!.Add(new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "Foreign.Assembly"))));
        project.Save(path);
        Reject(await Check(fixture.Path), "Manual assembly references are forbidden");
    }

    [TestMethod]
    public async Task SdkPinsMustAgree()
    {
        using var fixture = new ProjectFixture(Language);
        string path = Path.Combine(fixture.Path, "eng/toolchain.json");
        JsonObject policy = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        policy["sdk"] = "unavailable-test-sdk";
        File.WriteAllText(path, policy.ToJsonString());
        Reject(await Check(fixture.Path), "SDK pins disagree");
    }

    [TestMethod]
    [DataRow("unknown-selection")]
    [DataRow("unregistered-project")]
    public async Task ProjectInventoryCannotBeBypassed(string mode)
    {
        using var fixture = new ProjectFixture(Language);
        if (mode == "unregistered-project") { File.WriteAllText(Path.Combine(fixture.Path, "Foreign.csproj"), "<Project />"); }
        ProcessResult result = await DependencyCheckRunner.Run(Language, fixture.Path, mode == "unknown-selection" ? "Foreign.csproj" : "-");
        Reject(result, mode == "unknown-selection" ? "Unknown project requested" : "Unregistered C# project");
    }

    [TestMethod]
    public async Task SdkBuildToolCannotOmitAnApprovedReference()
    {
        using var fixture = new ProjectFixture(Language);
        const string relative = "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj";
        string path = Path.Combine(fixture.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        XDocument project = XDocument.Load(Path.Combine(Language, relative));
        project.Descendants("Reference").First().Remove();
        project.Save(path);
        Reject(await DependencyCheckRunner.Run(Language, fixture.Path, relative), "Build-tool SDK reference inventory mismatch");
    }

    [TestMethod]
    public async Task ExampleRoleGuardIsObservedByACompiledCheckerMutant()
    {
        using var fixture = new ProjectFixture(Language);
        string path = Path.Combine(fixture.Path, "src/CIS.Core/CIS.Core.csproj");
        XDocument project = XDocument.Load(path);
        project.Descendants("FrameworkReference").Where(reference => (string?)reference.Attribute("Include") == "Microsoft.AspNetCore.App").Remove();
        project.Root!.Add(new XElement("ItemGroup", new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App"), new XAttribute("IsImplicitlyDefined", "true"))));
        project.Save(path);
        string policyPath = Path.Combine(fixture.Path, "eng/project-boundaries.json");
        JsonObject policy = JsonNode.Parse(File.ReadAllText(policyPath))!.AsObject();
        JsonNode core = policy["projects"]!.AsArray().Single(item => (string?)item!["role"] == "shipping")!;
        core.AsObject().Remove("additionalExplicitFrameworkReferences");
        core["additionalImplicitFrameworkReferences"] = new JsonArray("Microsoft.AspNetCore.App");
        File.WriteAllText(policyPath, policy.ToJsonString());
        string assets = Path.Combine(fixture.Path, "src/CIS.Core/obj/project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
        File.Copy(Path.Combine(Language, "src/CIS.Core/obj/project.assets.json"), assets);
        Reject(await Check(fixture.Path), "Forbidden framework reference");
        string tool = Path.Combine(Language, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll");
        ProcessResult mutant = await ProcessRunner.RunAsync("dotnet", [tool, "dependency-role-control", Language, fixture.Path, "src/CIS.Core/CIS.Core.csproj", fixture.Path], Language, TimeSpan.FromMinutes(2));
        Assert.AreEqual(0, mutant.ExitCode, mutant.StandardOutput + mutant.StandardError);
        StringAssert.Contains(mutant.StandardOutput, "\"roleGuardMutationObserved\":true");
    }

    private static Task<ProcessResult> Check(string root) => DependencyCheckRunner.Run(Language, root, "src/CIS.Core/CIS.Core.csproj");
    private static void Reject(ProcessResult result, string reason)
    {
        Assert.AreEqual(1, result.ExitCode, result.StandardOutput + result.StandardError);
        StringAssert.Contains(result.StandardError, reason);
    }
    private static string FindLanguage()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        { if (File.Exists(Path.Combine(directory.FullName, "CisLib.slnx"))) { return directory.FullName; } }
        throw new DirectoryNotFoundException("Run dependency checks from the C# solution.");
    }
}
