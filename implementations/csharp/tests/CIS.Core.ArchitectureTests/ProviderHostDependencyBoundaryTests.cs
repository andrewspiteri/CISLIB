using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ProviderHostDependencyBoundaryTests
{
    [TestMethod]
    [DataRow("private-assets", "Fixture development package must have PrivateAssets=all")]
    [DataRow("test-package", "Unapproved test package dependency")]
    [DataRow("other-fixture", "Unapproved process-fixture package dependency")]
    [DataRow("unlisted-package", "Unapproved process-fixture package dependency")]
    public async Task FactoryClosureIsLimitedToTheNamedPrivateFixture(string mode, string expected)
    {
        string root = FindRoot(), language = Path.Combine(root, "implementations", "csharp");
        using var fixture = new ProjectFixture(language);
        Directory.CreateDirectory(Path.Combine(fixture.Path, "tests"));
        File.Copy(Path.Combine(language, "tests", "Directory.Build.targets"), Path.Combine(fixture.Path, "tests", "Directory.Build.targets"));
        string relative = mode == "test-package" ? "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj" : "tests/fixtures/DiscoveryHost/CIS.Core.DiscoveryFixture.csproj";
        string projectPath = Path.Combine(fixture.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        XDocument project = XDocument.Load(Path.Combine(language, relative));
        project.Descendants("ProjectReference").Remove();
        string policyPath = Path.Combine(fixture.Path, "eng", "project-boundaries.json");
        JsonObject policy = JsonNode.Parse(File.ReadAllText(policyPath))!.AsObject();
        JsonObject entry = policy["projects"]!.AsArray().Select(x => x!.AsObject()).Single(x => (string?)x["path"] == relative);
        // Isolate the package rule without local edges; no real project or assets are modified.
        entry["projectReferences"] = new JsonArray();
        entry.Remove("restoredProjectReferences");
        JsonObject assets = JsonNode.Parse(File.ReadAllText(Path.Combine(language, Path.GetDirectoryName(relative)!, "obj", "project.assets.json")))!.AsObject();
        JsonObject target = assets["targets"]!["net10.0"]!.AsObject();
        foreach (string key in target.Where(x => (string?)x.Value!["type"] == "project").Select(x => x.Key).ToArray()) { target.Remove(key); }
        if (mode == "other-fixture")
        {
            entry.Remove("developmentPackageReferences");
            entry["packageReferences"] = new JsonArray();
            project.Descendants("PackageReference").Remove();
            foreach (string key in target.Select(x => x.Key).Where(key => !key.StartsWith("Microsoft.Extensions.Logging.Abstractions/", StringComparison.Ordinal) && !key.StartsWith("Microsoft.Extensions.DependencyInjection.Abstractions/", StringComparison.Ordinal)).ToArray()) { target.Remove(key); }
        }
        project.Save(projectPath);
        File.WriteAllText(policyPath, policy.ToJsonString());
        string assetsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
        File.WriteAllText(assetsPath, assets.ToJsonString());
        ProcessResult positive = await Check();
        Assert.AreEqual(0, positive.ExitCode, positive.StandardOutput + positive.StandardError);
        if (mode == "private-assets")
        {
            project.Descendants("PackageReference").Single(x => (string?)x.Attribute("Include") == "Microsoft.Extensions.Logging").Attribute("PrivateAssets")!.Remove();
            project.Save(projectPath);
        }
        else
        {
            string id = mode == "unlisted-package" ? "MSTest.TestFramework/4.5.1" : "Microsoft.Extensions.Logging/10.0.12";
            target[id] = new JsonObject { ["type"] = "package" };
            File.WriteAllText(assetsPath, assets.ToJsonString());
        }
        ProcessResult negative = await Check();
        Assert.AreNotEqual(0, negative.ExitCode, negative.StandardOutput + negative.StandardError);
        StringAssert.Contains(negative.StandardError, expected);
        Task<ProcessResult> Check() => DependencyCheckRunner.Run(language, fixture.Path, relative);
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "implementations", "csharp", "CisLib.slnx"))) { return directory.FullName; }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("CISLIB checkout required.");
    }
}
