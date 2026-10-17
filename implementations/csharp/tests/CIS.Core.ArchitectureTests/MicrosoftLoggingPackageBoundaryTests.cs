using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class MicrosoftLoggingPackageBoundaryTests
{
    private static readonly string RepositoryRoot = FindRoot();
    private static readonly string LanguageRoot = Path.Combine(RepositoryRoot, "implementations", "csharp");

    [TestMethod]
    [DataRow("NLog/5.3.0", "Unapproved shipping package dependency")]
    [DataRow("Microsoft.Extensions.Logging/10.0.12", "Unapproved shipping package dependency")]
    [DataRow("Microsoft.Extensions.DependencyInjection/10.0.12", "Unapproved shipping package dependency")]
    [DataRow("Microsoft.Extensions.DependencyInjection.Abstractions/10.0.11", "Unapproved shipping package dependency")]
    [DataRow("missing-transitive", "Core runtime closure differs from the approved inventory")]
    public async Task CopiedRestoreGraphRejectsTransitiveLeakage(string mutation, string reason)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "project.assets.json");
        JsonObject assets = JsonNode.Parse(File.ReadAllText(Path.Combine(LanguageRoot, "src", "CIS.Core", "obj", "project.assets.json")))!.AsObject();
        File.WriteAllText(path, assets.ToJsonString());
        AssertPassed(await Check("-AssetsPath", path));
        JsonObject target = assets["targets"]!["net10.0"]!.AsObject();
        string approved = "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12";
        if (mutation == "missing-transitive") { target.Remove(approved); }
        else
        {
            string[] parts = mutation.Split('/', 2);
            if (parts[0] == "Microsoft.Extensions.DependencyInjection.Abstractions") { target.Remove(approved); }
            // Change an existing dependency edge, without adding a direct PackageReference.
            target["Microsoft.Extensions.Logging.Abstractions/10.0.12"]!["dependencies"]![parts[0]] = parts[1];
            target[mutation] = new JsonObject { ["type"] = "package" };
        }
        File.WriteAllText(path, assets.ToJsonString());
        AssertRejected(await Check("-AssetsPath", path), reason);
    }

    [TestMethod]
    [DataRow("factory-dll", "Unexpected packed asset")]
    [DataRow("third-party-dll", "Unexpected packed asset")]
    [DataRow("factory-dependency", "Unapproved packed dependency")]
    [DataRow("extra-dependency", "Packed direct dependency inventory mismatch")]
    [DataRow("dependency-version", "Unapproved packed dependency")]
    [DataRow("dependency-exact-range", "Unapproved packed dependency")]
    public async Task CopiedPackageRejectsFactoryAndThirdPartyLeakage(string mutation, string reason)
    {
        using var root = new TemporaryDirectory();
        string core = Path.Combine(LanguageRoot, "src", "CIS.Core", "bin", "Release", "net10.0", "CIS.Core.dll");
        string version = FileVersionInfo.GetVersionInfo(core).ProductVersion!.Split('+', 2)[0];
        string path = Path.Combine(root.Path, "candidate.nupkg");
        File.Copy(Path.Combine(RepositoryRoot, ".artifacts", "csharp", "packages", $"CIS.Core.{version}.nupkg"), path);
        AssertPassed(await Check("-PackagePath", path));
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            if (mutation is "factory-dll" or "third-party-dll")
            {
                string name = mutation == "factory-dll" ? "Microsoft.Extensions.Logging.dll" : "NLog.dll";
                using var writer = new StreamWriter(archive.CreateEntry("lib/net10.0/" + name).Open());
                writer.Write("Intentional forbidden-asset control; not a runnable assembly.");
            }
            else
            {
                ZipArchiveEntry entry = archive.GetEntry("CIS.Core.nuspec")!;
                XDocument manifest;
                using (Stream stream = entry.Open()) { manifest = XDocument.Load(stream); }
                XElement dependency = manifest.Descendants().Single(x => x.Name.LocalName == "dependency");
                if (mutation == "factory-dependency") { dependency.SetAttributeValue("id", "Microsoft.Extensions.Logging"); }
                if (mutation == "extra-dependency") { dependency.Parent!.Add(new XElement(dependency.Name, new XAttribute("id", "NLog"), new XAttribute("version", "5.3.0"))); }
                if (mutation == "dependency-version") { dependency.SetAttributeValue("version", "10.0.11"); }
                if (mutation == "dependency-exact-range") { dependency.SetAttributeValue("version", "[10.0.12]"); }
                entry.Delete();
                using Stream output = archive.CreateEntry("CIS.Core.nuspec").Open(); manifest.Save(output);
            }
        }
        AssertRejected(await Check("-PackagePath", path), reason);
    }

    private static Task<ProcessResult> Check(params string[] extra) => DependencyCheckRunner.Run(LanguageRoot, LanguageRoot, "src/CIS.Core/CIS.Core.csproj", extra);
    private static void AssertPassed(ProcessResult result) => Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
    private static void AssertRejected(ProcessResult result, string reason)
    {
        Assert.AreNotEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        StringAssert.Contains(result.StandardError, reason);
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
