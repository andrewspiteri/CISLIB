using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using CIS.Logging;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ShippingBuildInventoryTests
{
    private static readonly Lazy<Task<ShippingBuildInventoryFixture>> Prepared = new(ShippingBuildInventoryFixture.CreateAsync);
    public TestContext TestContext { get; set; } = null!;

    [ClassCleanup]
    public static void ReleaseFixtures(TestContext context)
    {
        if (!Prepared.IsValueCreated || !Prepared.Value.IsCompletedSuccessfully) { return; }
        ShippingBuildInventoryFixture fixture = Prepared.Value.Result;
        try { fixture.Dispose(); }
        finally { context.WriteLine("CIS-INVENTORY-WORK " + fixture.WorkSummary); }
    }

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

    [TestMethod]
    public async Task ImplicitNativeAndNonAssemblyAssetsAreSkippedWhileManagedExeAndCanonicalDuplicatesAreIncludedOnce()
    {
        using var scenario = (await Prepared.Value).CreateCase();
        File.Copy(Path.Combine(scenario.Path, "Host.dll"), Path.Combine(scenario.Path, "Managed.EXE"));
        var assets = new[]
        {
            ShippingBuildInventoryFixture.Asset("ignored-native", null, native: true),
            ShippingBuildInventoryFixture.Asset("ignored-content", "missing.txt"),
            ShippingBuildInventoryFixture.Asset("ignored-whitespace", "$([System.String]::Concat(' ', ' '))"),
            ShippingBuildInventoryFixture.Asset("managed", "Managed.EXE"),
            ShippingBuildInventoryFixture.Asset("same-managed", "sub/../Managed.EXE", selected: true),
            ShippingBuildInventoryFixture.Asset("same-host", "./CIS.Core.dll")
        };
        ProcessResult result = await scenario.RunAsync(assets, TestContext);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        AssertManifest(scenario, ["CIS.Core.dll", "Managed.EXE"]);
    }

    [TestMethod]
    public async Task OptionalSatelliteIsNotPublishedAsAnOrdinaryDiscoveryAssembly()
    {
        using var scenario = (await Prepared.Value).CreateCase();
        Assert.AreEqual("fr", System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(scenario.Path, "Satellite.dll")).CultureName);
        ProcessResult result = await scenario.RunAsync([ShippingBuildInventoryFixture.Asset("satellite", "Satellite.dll")], TestContext);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        AssertManifest(scenario, ["CIS.Core.dll"]);
    }

    [TestMethod]
    public async Task LocalAttributeConstructorMetadataIsInspectedWithoutExecutingApplicationCode()
    {
        using var scenario = (await Prepared.Value).CreateCase();
        ProcessResult result = await scenario.RunAsync([ShippingBuildInventoryFixture.Asset("local-attribute", "LocalAttribute.dll")], TestContext);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        AssertManifest(scenario, ["CIS.Core.dll", "LocalAttribute.dll"]);
        Assert.IsFalse(File.Exists(scenario.ExecutionMarker), "Inspecting metadata must not construct the application's assembly attribute.");
    }

    [TestMethod]
    [DataRow("native-explicit", "Explicit discovery asset is native:")]
    [DataRow("missing-path", "Selected asset has no output path:")]
    [DataRow("nonmanaged-explicit", "Explicit discovery asset must be a managed DLL or EXE:")]
    [DataRow("malformed", "Selected managed asset has invalid executable metadata: Malformed.dll")]
    [DataRow("native-image", "Selected managed asset has invalid executable metadata: Native.dll")]
    [DataRow("module", "Selected managed asset has invalid executable metadata: Module.dll")]
    [DataRow("satellite-explicit", "Explicit required assembly cannot be a satellite:")]
    [DataRow("reference", "Selected runtime asset is a reference assembly:")]
    [DataRow("simple-name", "Conflicting assembly selection:")]
    [DataRow("whitespace", "Explicit discovery asset must be a managed DLL or EXE:")]
    [DataRow("rooted", "Expected a relative output path:")]
    [DataRow("colon", "Expected a relative output path:")]
    public async Task RealTaskRejectsInvalidSelectionsWithoutPublishingAnInventory(string mode, string expected)
    {
        using var scenario = (await Prepared.Value).CreateCase();
        if (mode is "native-image" or "module")
        {
            using var stream = File.OpenRead(Path.Combine(scenario.Path, mode == "module" ? "Module.dll" : "Native.dll"));
            using var pe = new PEReader(stream);
            Assert.AreEqual(mode == "module", pe.HasMetadata, "The native task must receive the intended genuine PE image kind.");
            if (mode == "module") { Assert.IsFalse(pe.GetMetadataReader().IsAssembly); }
        }
        XElement[] assets = mode switch
        {
            "native-explicit" => [ShippingBuildInventoryFixture.Asset("selected-native", null, selected: true, native: true)],
            "missing-path" => [ShippingBuildInventoryFixture.Asset("selected-without-path", null)],
            "nonmanaged-explicit" => [ShippingBuildInventoryFixture.Asset("selected-content", "missing.txt", selected: true)],
            "malformed" => [ShippingBuildInventoryFixture.Asset("broken-image", "Malformed.dll")],
            "native-image" => [ShippingBuildInventoryFixture.Asset("native-image", "Native.dll")],
            "module" => [ShippingBuildInventoryFixture.Asset("managed-module", "Module.dll")],
            "satellite-explicit" => [ShippingBuildInventoryFixture.Asset("selected-satellite", "Satellite.dll", selected: true)],
            "reference" => [ShippingBuildInventoryFixture.Asset("compile-reference", "Reference.dll")],
            "simple-name" => [ShippingBuildInventoryFixture.Asset("first", "Host.dll"), ShippingBuildInventoryFixture.Asset("conflict", "Alias.dll")],
            "whitespace" => [ShippingBuildInventoryFixture.Asset("blank", "$([System.String]::Concat(' ', ' '))", selected: true)],
            "rooted" => [ShippingBuildInventoryFixture.Asset("rooted", Path.Combine(scenario.Path, "Host.dll"))],
            "colon" => [ShippingBuildInventoryFixture.Asset("drive-relative", "drive:Host.dll")],
            _ => throw new InvalidOperationException("Unknown native inventory control.")
        };
        ProcessResult result = await scenario.RunAsync(assets, TestContext);
        Assert.AreNotEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        StringAssert.Contains(result.StandardOutput + result.StandardError, "CISTM001");
        StringAssert.Contains(result.StandardOutput + result.StandardError, expected);
        Assert.IsFalse(File.Exists(scenario.Manifest), "A rejected selection must not publish a successful-looking inventory.");
    }

    [TestMethod]
    public async Task IdenticalInventoryPreservesItsTimestampAndChangedContentIsRewrittenAsCanonicalUtf8()
    {
        using var scenario = (await Prepared.Value).CreateCase();
        XElement[] assets = [ShippingBuildInventoryFixture.Asset("managed", "Host.dll")];
        ProcessResult first = await scenario.RunAsync(assets, TestContext);
        Assert.AreEqual(0, first.ExitCode, first.StandardOutput + first.StandardError);
        byte[] expected = File.ReadAllBytes(scenario.Manifest);
        Assert.IsFalse(expected.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        Assert.AreEqual((byte)'\n', expected[^1]);
        var sentinel = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(scenario.Manifest, sentinel);
        ProcessResult same = await scenario.RunAsync(assets, TestContext);
        Assert.AreEqual(0, same.ExitCode, same.StandardOutput + same.StandardError);
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(scenario.Manifest));
        Assert.AreEqual(sentinel, File.GetLastWriteTimeUtc(scenario.Manifest), "An unchanged manifest must not be rewritten.");
        File.WriteAllText(scenario.Manifest, "stale inventory\n");
        File.SetLastWriteTimeUtc(scenario.Manifest, sentinel);
        ProcessResult changed = await scenario.RunAsync(assets, TestContext);
        Assert.AreEqual(0, changed.ExitCode, changed.StandardOutput + changed.StandardError);
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(scenario.Manifest));
        Assert.AreNotEqual(sentinel, File.GetLastWriteTimeUtc(scenario.Manifest));
        AssertManifest(scenario, ["CIS.Core.dll", "Host.dll"]);
    }

    private static void AssertManifest(ShippingBuildInventoryFixture.Case scenario, string[] paths)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(scenario.Manifest));
        Assert.AreEqual(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        JsonElement[] entries = document.RootElement.GetProperty("assemblies").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(paths, entries.Select(entry => entry.GetProperty("relativePath").GetString()!).ToArray());
        CollectionAssert.AreEqual(entries.Select(entry => entry.GetProperty("assemblyName").GetString()!).Order(StringComparer.Ordinal).ToArray(),
            entries.Select(entry => entry.GetProperty("assemblyName").GetString()!).ToArray());
        StringAssert.StartsWith(document.RootElement.GetProperty("hostAssembly").GetString()!, "CIS.Core,");
        Assert.AreEqual(scenario.Manifest, File.ReadAllText(scenario.OutputProperty).Trim(), "The actual MSBuild task must return its produced ManifestPath output.");
    }
}
