using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using CIS.Logging;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

// One owned preparation supplies real immutable PE files to all native task cases.
internal sealed class ShippingBuildInventoryFixture : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string?> ChildEnvironment = new Dictionary<string, string?>
    {
        ["UseSharedCompilation"] = "false",
        ["MSBUILDDISABLENODEREUSE"] = "1",
        ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
    };
    private readonly TemporaryDirectory directory;
    private readonly Dictionary<string, string> pins = new(StringComparer.Ordinal);
    private readonly string language, helper;
    private int nativeTasks;
    private double preparationSeconds;

    private ShippingBuildInventoryFixture()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "CisLib.slnx"))) { at = at.Parent; }
        language = at?.FullName ?? throw new DirectoryNotFoundException("Build inside the maintained C# checkout.");
        helper = Path.Combine(language, "eng/CIS.TypeManager.Build/bin/Release/net10.0/CIS.TypeManager.Build.dll");
        Assert.IsTrue(File.Exists(helper), "Build the real shipping inventory helper before native task qualification.");
        directory = new TemporaryDirectory();
    }

    internal static async Task<ShippingBuildInventoryFixture> CreateAsync()
    {
        var fixture = new ShippingBuildInventoryFixture();
        try { await fixture.PrepareAsync(); return fixture; }
        catch { fixture.Dispose(); throw; }
    }

    private async Task PrepareAsync()
    {
        string root = directory.Path;
        foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
        { File.WriteAllText(Path.Combine(root, name), "<Project />\n"); }
        File.Copy(Path.Combine(language, "global.json"), Path.Combine(root, "global.json"));
        File.WriteAllText(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>");
        File.WriteAllText(Path.Combine(root, "Plain.cs"), "public static class FixtureValue { public static int Read() => 7; }\n");
        File.WriteAllText(Path.Combine(root, "Satellite.cs"), "[assembly: System.Reflection.AssemblyCulture(\"fr\")] public static class SatelliteValue { public static int Read() => 9; }\n");
        File.WriteAllText(Path.Combine(root, "LocalAttribute.cs"), """
            [assembly: InventoryFixture.MarkerAttribute]
            namespace InventoryFixture;
            [System.AttributeUsage(System.AttributeTargets.Assembly)]
            public sealed class MarkerAttribute : System.Attribute
            {
                public MarkerAttribute()
                {
                    string? marker = System.Environment.GetEnvironmentVariable("CIS_INVENTORY_EXECUTION_MARKER");
                    if (marker is not null) { System.IO.File.WriteAllText(marker, "application attribute executed"); }
                }
            }
            """);
        string images = Directory.CreateDirectory(Path.Combine(root, "images")).FullName;
        XElement Compile(string source, string output, string target = "library", bool reference = false) => new("Csc",
            new XAttribute("Sources", Path.Combine(root, source)), new XAttribute("OutputAssembly", Path.Combine(images, output)),
            new XAttribute("References", "@(ReferencePath)"), new XAttribute("TargetType", target),
            new XAttribute("NoConfig", "true"), new XAttribute("NoStandardLib", "true"), new XAttribute("NoLogo", "true"),
            new XAttribute("Nullable", "enable"), new XAttribute("LangVersion", "14.0"), new XAttribute("Optimize", "true"),
            new XAttribute("TreatWarningsAsErrors", "true"), new XAttribute("Deterministic", "true"),
            new XAttribute("UseSharedCompilation", "false"), new XAttribute("RefOnly", reference));
        string project = Path.Combine(root, "Fixtures.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("EnableDefaultCompileItems", "false"), new XElement("RestoreConfigFile", Path.Combine(root, "NuGet.Config"))),
            new XElement("Target", new XAttribute("Name", "Fixtures"), new XAttribute("DependsOnTargets", "ResolveReferences"),
                Compile("Satellite.cs", "Satellite.dll"), Compile("Plain.cs", "Reference.dll", reference: true),
                Compile("Plain.cs", "Module.dll", target: "module"), Compile("LocalAttribute.cs", "LocalAttribute.dll"))))
            .Save(project);
        string[] arguments = ["msbuild", project, "-restore", "-t:Fixtures", "-p:Configuration=Release", "-nologo", "-nr:false"];
        var clock = Stopwatch.StartNew();
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, language, TimeSpan.FromSeconds(120), ChildEnvironment);
        preparationSeconds = clock.Elapsed.TotalSeconds;
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.IsTrue(result.OutputDrained);
        File.Copy(typeof(CoreLogRuntime).Assembly.Location, Path.Combine(images, "CIS.Core.dll"));
        File.Copy(typeof(ShippingBuildInventoryTests).Assembly.Location, Path.Combine(images, "Host.dll"));
        File.Copy(Path.Combine(images, "Host.dll"), Path.Combine(images, "Alias.dll"));
        File.WriteAllText(Path.Combine(images, "Malformed.dll"), "This is not an executable image.");
        var native = new BlobBuilder(); new NativeImage().Serialize(native);
        File.WriteAllBytes(Path.Combine(images, "Native.dll"), native.ToArray());
        foreach (string file in Directory.EnumerateFiles(images).Append(helper)) { pins.Add(file, Hash(file)); }
    }

    internal Case CreateCase()
    {
        AssertUnchanged();
        return new Case(this);
    }

    internal string WorkSummary => JsonSerializer.Serialize(new
    {
        sdkPreparations = 1,
        compiledPeImages = 4,
        nativeMsBuildTaskInvocations = Volatile.Read(ref nativeTasks),
        preparationSeconds,
        helperSha256 = pins[helper],
        scope = "New task controls only; original positive/escape controls retain their own real native invocations. No Core source build."
    });

    internal static XElement Asset(string name, string? relative, bool selected = false, bool native = false)
    {
        var item = new XElement("Asset", new XAttribute("Include", name));
        if (relative is not null) { item.Add(new XElement("RelativePath", relative)); }
        if (selected) { item.Add(new XElement("CisExplicitSelection", "TRUE")); }
        if (native) { item.Add(new XElement("AssetType", "NATIVE")); }
        return item;
    }

    private void AssertUnchanged()
    { foreach ((string path, string hash) in pins) { Assert.AreEqual(hash, Hash(path), "Shared PE/helper input changed: " + path); } }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    public void Dispose() => directory.Dispose();

    internal sealed class Case : IDisposable
    {
        private readonly ShippingBuildInventoryFixture owner;
        private readonly TemporaryDirectory directory = new();
        internal string Path => directory.Path;
        internal string Manifest => System.IO.Path.Combine(Path, "CIS.Core.types.json");
        internal string OutputProperty => System.IO.Path.Combine(Path, "task-output.txt");
        internal string ExecutionMarker => System.IO.Path.Combine(Path, "application-code-ran.txt");

        internal Case(ShippingBuildInventoryFixture owner)
        {
            this.owner = owner;
            try
            {
                foreach (string file in Directory.EnumerateFiles(System.IO.Path.Combine(owner.directory.Path, "images")))
                { File.Copy(file, System.IO.Path.Combine(Path, System.IO.Path.GetFileName(file))); }
                owner.AssertUnchanged();
            }
            catch { directory.Dispose(); throw; }
        }

        internal async Task<ProcessResult> RunAsync(XElement[] assets, TestContext context)
        {
            owner.AssertUnchanged();
            string project = System.IO.Path.Combine(Path, "Inventory.proj");
            new XDocument(new XElement("Project",
                new XElement("UsingTask", new XAttribute("TaskName", "CIS.TypeManagement.Build.WriteTypeInventory"), new XAttribute("AssemblyFile", owner.helper)),
                new XElement("ItemGroup", assets.Select(asset => new XElement(asset))),
                new XElement("Target", new XAttribute("Name", "Inventory"),
                    new XElement("CIS.TypeManagement.Build.WriteTypeInventory", new XAttribute("OutputDirectory", Path),
                        new XAttribute("HostFileName", "CIS.Core.dll"), new XAttribute("Assets", "@(Asset)"),
                        new XElement("Output", new XAttribute("TaskParameter", "ManifestPath"), new XAttribute("PropertyName", "_ActualInventory"))),
                    new XElement("WriteLinesToFile", new XAttribute("File", OutputProperty), new XAttribute("Lines", "$(_ActualInventory)"),
                        new XAttribute("Overwrite", "true"), new XAttribute("Encoding", "UTF-8")))))
                .Save(project);
            string[] arguments = ["msbuild", project, "-t:Inventory", "-nologo", "-nr:false"];
            var environment = new Dictionary<string, string?>(ChildEnvironment) { ["CIS_INVENTORY_EXECUTION_MARKER"] = ExecutionMarker };
            var clock = Stopwatch.StartNew();
            Interlocked.Increment(ref owner.nativeTasks);
            ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, owner.language, TimeSpan.FromSeconds(60), environment);
            context.WriteLine("CIS-INVENTORY-COMMAND " + JsonSerializer.Serialize(new
            {
                executable = "dotnet",
                arguments,
                workingDirectory = owner.language,
                result.ProcessId,
                result.ExitCode,
                result.OutputDrained,
                seconds = clock.Elapsed.TotalSeconds,
                helperSha256 = owner.pins[owner.helper],
                projectSha256 = Hash(project),
                stdout = result.StandardOutput,
                stderr = result.StandardError
            }));
            Assert.IsTrue(result.OutputDrained);
            owner.AssertUnchanged();
            return result;
        }
        public void Dispose() => directory.Dispose();
    }

    // This is a valid PE with a code section and no CLR metadata directory.
    // It is input data for the real task and is never loaded or executed.
    private sealed class NativeImage() : PEBuilder(PEHeaderBuilder.CreateLibraryHeader(), deterministicIdProvider: null)
    {
        protected override ImmutableArray<Section> CreateSections() => [new(".text", SectionCharacteristics.ContainsCode | SectionCharacteristics.MemRead | SectionCharacteristics.MemExecute)];
        protected override BlobBuilder SerializeSection(string name, SectionLocation location)
        { var code = new BlobBuilder(); code.WriteByte(0xc3); return code; }
        protected override PEDirectoriesBuilder GetDirectories() => new();
    }
}
