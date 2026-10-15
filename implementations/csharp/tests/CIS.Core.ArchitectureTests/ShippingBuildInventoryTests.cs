using System.Text.Json;
using System.Xml.Linq;
using CIS.Logging;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ShippingBuildInventoryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealMsBuildTaskWritesInventoryOrRejectsEscapingAsset(bool escape)
    {
        using var directory = new TemporaryDirectory();
        string core = typeof(CoreLogRuntime).Assembly.Location;
        string host = typeof(ShippingBuildInventoryTests).Assembly.Location;
        File.Copy(core, Path.Combine(directory.Path, "CIS.Core.dll"));
        File.Copy(host, Path.Combine(directory.Path, "Host.dll"));
        DirectoryInfo? language = new(AppContext.BaseDirectory);
        while (language is not null && !File.Exists(Path.Combine(language.FullName, "CisLib.slnx")))
        {
            language = language.Parent;
        }
        Assert.IsNotNull(language);
        string helper = Path.Combine(language.FullName, "eng/CIS.TypeManager.Build/bin/Release/net10.0/CIS.TypeManager.Build.dll");
        Assert.IsTrue(File.Exists(helper));
        string project = Path.Combine(directory.Path, "Inventory.proj");
        new XDocument(new XElement("Project",
            new XElement("UsingTask", new XAttribute("TaskName", "CIS.TypeManagement.Build.WriteTypeInventory"), new XAttribute("AssemblyFile", helper)),
            new XElement("ItemGroup", new XElement("Asset", new XAttribute("Include", "selected"),
                new XElement("RelativePath", escape ? "../escape.dll" : "Host.dll"))),
            new XElement("Target", new XAttribute("Name", "Inventory"),
                new XElement("CIS.TypeManagement.Build.WriteTypeInventory", new XAttribute("OutputDirectory", directory.Path),
                    new XAttribute("HostFileName", "CIS.Core.dll"), new XAttribute("Assets", "@(Asset)")))))
            .Save(project);
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", ["msbuild", project, "-t:Inventory", "-nologo", "-nr:false"],
            language.FullName, TimeSpan.FromSeconds(60));
        string manifest = Path.Combine(directory.Path, "CIS.Core.types.json");
        if (escape)
        {
            Assert.AreNotEqual(0, result.ExitCode);
            StringAssert.Contains(result.StandardOutput + result.StandardError, "CISTM001");
            StringAssert.Contains(result.StandardOutput + result.StandardError, "escapes the output directory");
            Assert.IsFalse(File.Exists(manifest));
            return;
        }
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        Assert.AreEqual(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        CollectionAssert.AreEquivalent(new[] { "CIS.Core.dll", "Host.dll" }, document.RootElement.GetProperty("assemblies")
            .EnumerateArray().Select(entry => entry.GetProperty("relativePath").GetString()!).ToArray());
        StringAssert.StartsWith(document.RootElement.GetProperty("hostAssembly").GetString()!, "CIS.Core,");
    }
}
