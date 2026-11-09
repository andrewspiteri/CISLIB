using CIS.TestSupport;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ShippingDependencyTests
{
    [TestMethod]
    public async Task SingleCorePackageKeepsItsApprovedRuntimeDependencyClosure()
    {
        DirectoryInfo? language = new(AppContext.BaseDirectory);
        while (language is not null && !File.Exists(Path.Combine(language.FullName, "CisLib.slnx"))) { language = language.Parent; }
        Assert.IsNotNull(language);
        using JsonDocument policy = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(language.FullName, "eng/runtime-dependencies.json")));
        string[] expected = policy.RootElement.GetProperty("packages").EnumerateArray()
            .Select(package => package.GetProperty("id").GetString()! + "/" + package.GetProperty("version").GetString()!).ToArray();
        using JsonDocument assets = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(language.FullName, "src/CIS.Core/obj/project.assets.json")));
        string[] actual = assets.RootElement.GetProperty("targets").GetProperty("net10.0").EnumerateObject()
            .Where(item => item.Value.GetProperty("type").GetString() == "package").Select(item => item.Name).ToArray();
        CollectionAssert.AreEquivalent(expected, actual);
        Assembly core = typeof(CoreLogRuntime).Assembly;
        Assert.AreEqual("CIS.Core", core.GetName().Name);
        Assert.AreEqual(new Version(1, 0, 0, 0), core.GetName().Version);
        Assert.AreEqual(0, (core.GetName().GetPublicKeyToken() ?? []).Length);
        string configuration = core.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        ProcessResult evaluated = await ProcessRunner.RunAsync("dotnet",
            ["msbuild", "src/CIS.Core/CIS.Core.csproj", "-t:ResolveReferences", "-getItem:ReferencePath", "-getProperty:MSBuildSDKsPath", "-nologo", "-p:Configuration=" + configuration],
            language.FullName, TimeSpan.FromSeconds(60));
        Assert.AreEqual(0, evaluated.ExitCode, evaluated.StandardOutput + evaluated.StandardError);
        using JsonDocument references = JsonDocument.Parse(evaluated.StandardOutput);
        string sdk = references.RootElement.GetProperty("Properties").GetProperty("MSBuildSDKsPath").GetString()!;
        string installation = Path.GetFullPath("../../..", sdk);
        string[] frameworkRoots = [Path.Combine(installation, "packs/Microsoft.NETCore.App.Ref"), Path.Combine(installation, "packs/Microsoft.AspNetCore.App.Ref")];
        string[] packageRoots = assets.RootElement.GetProperty("packageFolders").EnumerateObject()
            .SelectMany(folder => expected.Select(package => Path.Combine(folder.Name, package.ToLowerInvariant()))).ToArray();
        string[] resolved = references.RootElement.GetProperty("Items").GetProperty("ReferencePath").EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!).ToArray();
        foreach (string path in resolved)
        {
            Assert.IsTrue(frameworkRoots.Concat(packageRoots).Any(root => IsWithin(root, path)), "An unapproved shipping reference was resolved: " + path);
        }
        foreach (AssemblyName reference in core.GetReferencedAssemblies())
        {
            string[] inputs = resolved.Where(path => Path.GetFileNameWithoutExtension(path) == reference.Name).ToArray();
            Assert.HasCount(1, inputs, reference.FullName);
            Assert.AreEqual(reference.FullName, AssemblyName.GetAssemblyName(inputs[0]).FullName, "Compiled reference differs from the approved native input.");
        }
        XDocument project = XDocument.Load(Path.Combine(language.FullName, "src/CIS.Core/CIS.Core.csproj"));
        CollectionAssert.AreEqual(new[] { "Microsoft.AspNetCore.App" }, project.Descendants("FrameworkReference").Select(item => (string)item.Attribute("Include")!).ToArray());
    }
    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
