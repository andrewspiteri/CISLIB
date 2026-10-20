using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[module: CIS.Tests.ArchitectureTests.InspectionMarker]

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class AssemblyInspectionTests
{
    private static readonly string LanguageRoot = FindLanguageRoot();

    [TestMethod]
    [DataRow("src/CIS.Core", "CIS.Core")]
    [DataRow("eng/CIS.TypeManager.Build", "CIS.TypeManager.Build")]
    public async Task EveryPackagedAssemblyExposesCompiledFacts(string projectDirectory, string name)
    {
        using var temporary = new TemporaryDirectory();
        string binary = Path.Combine(LanguageRoot, projectDirectory, "bin/Release/net10.0", name + ".dll");
        JsonNode report = await Inspect(binary, temporary.Path);
        Assert.AreEqual(name, report["name"]!.GetValue<string>());
        Assert.IsTrue(report["references"]!.AsArray().Count > 0);
        Assert.IsTrue(report["types"]!.AsArray().Any(type => type!["publicApi"]!.GetValue<bool>()));
        Assert.IsTrue(report["typeUses"]!.AsArray().Count > 0);
        Assert.IsTrue(report["sha256"]!.GetValue<string>().Length == 64);
    }

    [TestMethod]
    public async Task SignaturesVisibilityAndInternalIlUsesAreInspectedWithoutExecutingInitializers()
    {
        using var temporary = new TemporaryDirectory();
        JsonNode report = await Inspect(typeof(AssemblyInspectionTests).Assembly.Location, temporary.Path);
        Assert.AreEqual(report["customAttributes"]!["total"]!.GetValue<int>(), report["customAttributes"]!["visited"]!.GetValue<int>());
        Assert.IsTrue(report["typeUses"]!.AsArray().Any(use => use!["Site"]!.GetValue<string>() == "<Module>:module" && use["Type"]!.GetValue<string>().EndsWith("InspectionMarkerAttribute", StringComparison.Ordinal)));
        JsonArray types = report["types"]!.AsArray();
        JsonNode fixture = types.Single(type => type!["name"]!.GetValue<string>().EndsWith("InspectionFixture`1", StringComparison.Ordinal))!;
        Assert.IsTrue(fixture["publicApi"]!.GetValue<bool>());
        Assert.IsTrue(fixture["members"]!.AsArray().Any(member => member!["kind"]!.GetValue<string>() == "property" && member["signature"]!.GetValue<string>().Contains("!0", StringComparison.Ordinal)));
        Assert.IsTrue(types.Single(type => type!["name"]!.GetValue<string>().EndsWith("InspectionFixture`1+PublicNested", StringComparison.Ordinal))!["publicApi"]!.GetValue<bool>());
        Assert.IsFalse(types.Single(type => type!["name"]!.GetValue<string>().EndsWith("InspectionHidden+PublicNested", StringComparison.Ordinal))!["publicApi"]!.GetValue<bool>());
        Assert.IsTrue(report["typeUses"]!.AsArray().Any(use => use!["Type"]!.GetValue<string>() == "System.InvalidOperationException" && use["Site"]!.GetValue<string>().Contains("InspectionFixture", StringComparison.Ordinal)));
        Assert.IsTrue(report["internalDependencies"]!.AsArray().Any(use => use!["sourceType"]!.GetValue<string>().Contains("InspectionFixture", StringComparison.Ordinal) && use["targetType"]!.GetValue<string>().EndsWith("InspectionTarget", StringComparison.Ordinal)));
        Assert.IsTrue(report["namespaceDependencies"]!.AsArray().Any(edge => edge!["source"]!.GetValue<string>() == "CIS.Tests.ArchitectureTests"));
    }

    [TestMethod]
    public async Task NonManagedInputFailsWithoutProducingAnInspection()
    {
        using var temporary = new TemporaryDirectory();
        string input = Path.Combine(temporary.Path, "invalid.dll"), output = Path.Combine(temporary.Path, "inspection.json");
        File.WriteAllText(input, "not a PE image");
        ProcessResult result = await Run(input, output);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.StandardError, "Assembly inspection failed");
        Assert.IsFalse(File.Exists(output));
    }

    [TestMethod]
    public void DeveloperInventoryIncludesEveryRestoredTargetAndEveryPackageDll()
    {
        string repository = Directory.GetParent(Directory.GetParent(LanguageRoot)!.FullName)!.FullName;
        string reports = VerificationCheckpoint.Root(repository);
        JsonNode summary = JsonNode.Parse(File.ReadAllText(Path.Combine(reports, "summary.json")))!;
        JsonNode reference = summary["architectureInspection"]!;
        string path = Path.Combine(reports, reference["path"]!.GetValue<string>());
        Assert.AreEqual(reference["sha256"]!.GetValue<string>(), Hash(path));
        JsonNode report = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.AreEqual("cis.architecture-inspection/v1", report["schema"]!.GetValue<string>());
        var discovered = Directory.EnumerateFiles(LanguageRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(project => !Path.GetRelativePath(LanguageRoot, project).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".vs"))
            .Select(project => Path.GetRelativePath(LanguageRoot, project).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(discovered.SetEquals(report["projects"]!.AsArray().Select(project => project!["project"]!.GetValue<string>())));
        foreach (JsonNode project in report["projects"]!.AsArray().OfType<JsonNode>())
        {
            JsonNode restored = JsonNode.Parse(File.ReadAllText(project["assets"]!["path"]!.GetValue<string>()))!;
            Assert.IsTrue(restored["targets"]!.AsObject().Select(target => target.Key).ToHashSet(StringComparer.Ordinal)
                .SetEquals(project["targets"]!.AsArray().Select(target => target!["target"]!.GetValue<string>())));
        }
        string package = report["package"]!["path"]!.GetValue<string>();
        Assert.AreEqual(report["package"]!["sha256"]!.GetValue<string>(), Hash(package));
        using var archive = System.IO.Compression.ZipFile.OpenRead(package);
        Assert.IsTrue(archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal)
            .SetEquals(report["assemblies"]!.AsArray().Select(assembly => assembly!["packageEntry"]!.GetValue<string>())));
        foreach (JsonNode assembly in report["assemblies"]!.AsArray().OfType<JsonNode>())
        {
            using Stream stream = archive.GetEntry(assembly["packageEntry"]!.GetValue<string>())!.Open();
            string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
            Assert.AreEqual(hash, assembly["facts"]!["sha256"]!.GetValue<string>());
            Assert.AreEqual(hash, Hash(assembly["builtAssembly"]!["path"]!.GetValue<string>()));
        }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    [TestMethod]
    public async Task ArchitectureConsistencyFailureUsesTheDocumentedDiagnostic()
    {
        using var temporary = new TemporaryDirectory();
        string engine = Path.Combine(temporary.Path, "eng");
        Directory.CreateDirectory(engine);
        File.Copy(Path.Combine(LanguageRoot, "global.json"), Path.Combine(temporary.Path, "global.json"));
        File.Copy(Path.Combine(LanguageRoot, "eng/toolchain.json"), Path.Combine(engine, "toolchain.json"));
        File.WriteAllText(Path.Combine(engine, "project-boundaries.json"), "{\"projects\":[]}");
        File.WriteAllText(Path.Combine(temporary.Path, "unregistered.csproj"), "<Project />");
        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            [Path.Combine(LanguageRoot, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll"), "architecture", temporary.Path,
                Path.Combine(temporary.Path, "unused.nupkg"), Path.Combine(temporary.Path, "report")], LanguageRoot, TimeSpan.FromSeconds(30));
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.StandardError, "Architecture inspection failed: ARCH-PROJECT-INVENTORY: Discovered project count differs");
        Assert.IsFalse(File.Exists(Path.Combine(temporary.Path, "report/inspection.json")));
    }

    private static async Task<JsonNode> Inspect(string input, string directory)
    {
        string output = Path.Combine(directory, "inspection.json");
        ProcessResult result = await Run(input, output);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        return JsonNode.Parse(File.ReadAllText(output))!;
    }

    private static Task<ProcessResult> Run(string input, string output) => ProcessRunner.RunAsync("dotnet",
        [Path.Combine(LanguageRoot, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll"), "inspect", input, output], LanguageRoot, TimeSpan.FromSeconds(30));

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

// An inspected assembly must never execute this initializer. It contains real generic,
// nested, field, property, event, exception and method-body dependency metadata.
public class InspectionFixture<[InspectionMarker] T> : IInspectionContract where T : InspectionTarget
{
    static InspectionFixture() => throw new InvalidOperationException("Inspection executed target code.");
    InspectionTarget IInspectionContract.Get() => new();
    public T? Value { get; set; }
    public event EventHandler? Changed;
    public InspectionTarget Exercise()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); return InspectionTarget.Create<InspectionTarget>(); }
        catch (InvalidOperationException) { return new InspectionTarget(); }
    }
    public sealed class PublicNested;
}

public class InspectionTarget
{
    public static T Create<T>() where T : new() => new();
}

internal sealed class InspectionHidden
{
    public sealed class PublicNested;
}

[AttributeUsage(AttributeTargets.Module | AttributeTargets.GenericParameter)]
public sealed class InspectionMarkerAttribute : Attribute;

public interface IInspectionContract
{
    InspectionTarget Get();
}
