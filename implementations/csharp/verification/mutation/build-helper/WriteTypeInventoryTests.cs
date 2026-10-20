using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using CIS.TypeManagement.Build;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: CIS.Tests.BuildHelper.InventoryMarker<int>]

namespace CIS.Tests.BuildHelper;

[TestClass]
public sealed class WriteTypeInventoryTests
{
    [TestMethod]
    public void PlatformPathIdentityPreservesDistinctLinuxFilesAndRejectsWindowsAliases()
    {
        using var fixture = new Fixture();
        fixture.Copy(typeof(WriteTypeInventoryTests).Assembly.Location, "Selected.dll");
        var task = fixture.Create(fixture.Asset("Selected.dll"), fixture.Asset("selected.dll"));
        if (OperatingSystem.IsWindows())
        {
            fixture.Rejected(task, "Conflicting assembly selection");
        }
        else
        {
            fixture.Copy(typeof(ITaskItem).Assembly.Location, "selected.dll");
            Assert.IsTrue(task.Execute()); Assert.IsEmpty(fixture.Engine.Errors);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(task.ManifestPath));
            JsonElement[] entries = document.RootElement.GetProperty("assemblies").EnumerateArray().ToArray();
            Assert.HasCount(3, entries);
            CollectionAssert.AreEquivalent(new[] { "Host.dll", "Selected.dll", "selected.dll" },
                entries.Select(entry => entry.GetProperty("relativePath").GetString()!).ToArray());
        }
    }

    [TestMethod]
    public void NewTaskHasEmptyUnselectedInputsAndNoPublishedManifest()
    {
        var task = new WriteTypeInventory();
        Assert.AreEqual("", task.OutputDirectory); Assert.AreEqual("", task.HostFileName);
        Assert.AreEqual("", task.ManifestPath); Assert.IsEmpty(task.Assets);
    }

    [TestMethod]
    public void InventoryContainsExactIdentitiesAndNormalizedSortedPaths()
    {
        using var fixture = new Fixture();
        fixture.Copy(typeof(WriteTypeInventoryTests).Assembly.Location, "nested/Selected.dll");
        var task = fixture.Create(fixture.Asset("nested/Selected.dll"), fixture.Asset("Host.dll"));
        Assert.IsTrue(task.Execute());
        Assert.IsEmpty(fixture.Engine.Errors);
        Assert.AreEqual(Path.Combine(fixture.Root, "CIS.TypeManager.Build.types.json"), task.ManifestPath);
        byte[] bytes = File.ReadAllBytes(task.ManifestPath);
        Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        string text = System.Text.Encoding.UTF8.GetString(bytes);
        StringAssert.EndsWith(text, "\n"); Assert.IsFalse(text.Contains('\r'));
        StringAssert.StartsWith(text, "{\n  \"schemaVersion\": 1,\n");
        Assert.HasCount(1, fixture.Engine.Messages);
        Assert.AreEqual($"Type Manager inventory: 2 assemblies -> {task.ManifestPath}", fixture.Engine.Messages.Single());
        using JsonDocument document = JsonDocument.Parse(bytes);
        Assert.AreEqual(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(typeof(WriteTypeInventory).Assembly.FullName, document.RootElement.GetProperty("hostAssembly").GetString());
        JsonElement[] entries = document.RootElement.GetProperty("assemblies").EnumerateArray().ToArray();
        Assert.HasCount(2, entries);
        string[] identities = entries.Select(entry => entry.GetProperty("assemblyName").GetString()!).ToArray();
        CollectionAssert.AreEqual(identities.Order(StringComparer.Ordinal).ToArray(), identities);
        CollectionAssert.AreEquivalent(new[] { typeof(WriteTypeInventory).Assembly.FullName!, typeof(WriteTypeInventoryTests).Assembly.FullName! }, identities);
        CollectionAssert.AreEquivalent(new[] { "Host.dll", "nested/Selected.dll" }, entries.Select(entry => entry.GetProperty("relativePath").GetString()!).ToArray());
    }

    [TestMethod]
    public void IdenticalInventoryDoesNotRewriteButDifferentContentDoes()
    {
        using var fixture = new Fixture(); var task = fixture.Create();
        Assert.IsTrue(task.Execute());
        DateTime stamp = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(task.ManifestPath, stamp);
        byte[] first = File.ReadAllBytes(task.ManifestPath);
        Assert.IsTrue(task.Execute()); Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(task.ManifestPath));
        File.WriteAllText(task.ManifestPath, "old inventory");
        Assert.IsTrue(task.Execute()); CollectionAssert.AreEqual(first, File.ReadAllBytes(task.ManifestPath));
    }

    [TestMethod]
    [DataRow("", "Expected a relative output path")]
    [DataRow(" ", "Expected a relative output path")]
    [DataRow("../outside.dll", "escapes the output directory")]
    [DataRow("..", "escapes the output directory")]
    [DataRow("name:stream.dll", "Expected a relative output path")]
    public void HostRejectsInvalidRelativePaths(string path, string error)
    {
        using var fixture = new Fixture(); var task = fixture.Create(); task.HostFileName = path;
        fixture.Rejected(task, error);
    }

    [TestMethod]
    public void HostRejectsAnAbsolutePath()
    {
        using var fixture = new Fixture(); var task = fixture.Create(); task.HostFileName = Path.Combine(fixture.Root, "Host.dll");
        fixture.Rejected(task, "Expected a relative output path");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeAssetsAreSkippedUnlessExplicitlyRequired(bool required)
    {
        using var fixture = new Fixture(); var asset = fixture.Asset("absent-native.dll", required); asset.SetMetadata("AssetType", "NATIVE");
        var task = fixture.Create(asset);
        if (required) { fixture.Rejected(task, "Explicit discovery asset is native"); }
        else { fixture.OnlyHost(task); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NonAssemblyExtensionsAreSkippedUnlessExplicitlyRequired(bool required)
    {
        using var fixture = new Fixture(); var task = fixture.Create(fixture.Asset("absent.txt", required));
        if (required) { fixture.Rejected(task, "must be a managed DLL or EXE"); }
        else { fixture.OnlyHost(task); }
    }

    [TestMethod]
    public void MissingSelectedPathIsRejected()
    {
        using var fixture = new Fixture(); fixture.Rejected(fixture.Create(fixture.Asset("")), "Selected asset has no output path");
    }

    [TestMethod]
    [DataRow("../escape.dll")]
    [DataRow("drive:stream.dll")]
    public void SelectedAssemblyCannotEscapeTheOutput(string path)
    {
        using var fixture = new Fixture(); fixture.Rejected(fixture.Create(fixture.Asset(path)), path.Contains(':') ? "Expected a relative output path" : "escapes the output directory");
    }

    [TestMethod]
    public void MissingSelectedAssemblyIsRejected()
    {
        using var fixture = new Fixture(); fixture.Rejected(fixture.Create(fixture.Asset("missing.dll")), "missing.dll");
    }

    [TestMethod]
    [DataRow("Selected.DLL")]
    [DataRow("Selected.EXE")]
    public void ManagedDllAndExeExtensionsAreCaseInsensitive(string name)
    {
        using var fixture = new Fixture(); fixture.Copy(typeof(WriteTypeInventoryTests).Assembly.Location, name);
        var task = fixture.Create(fixture.Asset(name, true)); Assert.IsTrue(task.Execute());
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(task.ManifestPath));
        Assert.AreEqual(2, document.RootElement.GetProperty("assemblies").GetArrayLength());
    }

    [TestMethod]
    public void InvalidManagedImageRetainsTheSelectedPathInItsSafeFailure()
    {
        using var fixture = new Fixture(); File.WriteAllText(Path.Combine(fixture.Root, "invalid.dll"), "not a PE image");
        fixture.Rejected(fixture.Create(fixture.Asset("invalid.dll")), "Selected managed asset has invalid executable metadata: invalid.dll");
    }

    [TestMethod]
    public void NativePeImageCannotBecomeAManagedDiscoveryAsset()
    {
        using var fixture = new Fixture();
        byte[] native = File.ReadAllBytes(typeof(WriteTypeInventory).Assembly.Location);
        using (var reader = new PEReader(new MemoryStream(native)))
        {
            int directory = reader.PEHeaders.PEHeaderStartOffset + (reader.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus ? 112 : 96) + 14 * 8;
            native.AsSpan(directory, 8).Clear();
        }
        File.WriteAllBytes(Path.Combine(fixture.Root, "native.dll"), native);
        fixture.Rejected(fixture.Create(fixture.Asset("native.dll")), "Selected managed asset has invalid executable metadata: native.dll");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SatelliteAssemblyIsSkippedUnlessExplicitlyRequired(bool required)
    {
        using var fixture = new Fixture(); fixture.CopySatellite();
        var task = fixture.Create(fixture.Asset("Satellite.dll", required));
        if (required) { fixture.Rejected(task, "Explicit required assembly cannot be a satellite"); }
        else { fixture.OnlyHost(task); }
    }

    [TestMethod]
    public void HostIsAlwaysARequiredNeutralAssembly()
    {
        using var fixture = new Fixture(); fixture.CopySatellite();
        var task = fixture.Create(); task.HostFileName = "Satellite.dll";
        fixture.Rejected(task, "Explicit required assembly cannot be a satellite");
    }

    [TestMethod]
    public void DuplicateIdentityAtAnotherPathIsRejected()
    {
        using var fixture = new Fixture(); fixture.Copy(typeof(WriteTypeInventory).Assembly.Location, "other.dll");
        fixture.Rejected(fixture.Create(fixture.Asset("other.dll")), "Conflicting assembly selection");
    }

    [TestMethod]
    public void MetadataModuleWithoutAnAssemblyDefinitionIsRejected()
    {
        using var fixture = new Fixture(); fixture.WriteMetadata("Module.dll", assembly: false);
        fixture.Rejected(fixture.Create(fixture.Asset("Module.dll")), "Selected managed asset has invalid executable metadata: Module.dll");
    }

    [TestMethod]
    public void LocallyDefinedReferenceAssemblyAttributeIsAlsoRejected()
    {
        using var fixture = new Fixture(); fixture.WriteMetadata("LocalReference.dll", localReference: true);
        fixture.Rejected(fixture.Create(fixture.Asset("LocalReference.dll", true)), "Selected runtime asset is a reference assembly");
    }

    [TestMethod]
    public void ACompileTimeReferenceAssemblyIsRejected()
    {
        using var fixture = new Fixture();
        // The generated test project supplies the exact build output, independently of the current directory.
        string reference = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "reference-path.txt")).Trim();
        fixture.Copy(reference, "reference.dll");
        fixture.Rejected(fixture.Create(fixture.Asset("reference.dll", true)), "Selected runtime asset is a reference assembly");
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cis-build-mutation-" + Guid.NewGuid().ToString("N"));
        internal Engine Engine { get; } = new();
        internal Fixture() { Directory.CreateDirectory(Root); Copy(typeof(WriteTypeInventory).Assembly.Location, "Host.dll"); }
        internal void Copy(string source, string relative)
        {
            string target = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
        }
        internal void CopySatellite() => WriteMetadata("Satellite.dll", culture: "fr");
        internal void WriteMetadata(string name, bool assembly = true, string? culture = null, bool localReference = false)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString(name), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            AssemblyDefinitionHandle identity = default;
            if (assembly) { identity = metadata.AddAssembly(metadata.GetOrAddString("MetadataFixture"), new Version(1, 0, 0, 0), culture is null ? default : metadata.GetOrAddString(culture), default, 0, 0); }
            if (localReference)
            {
                metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
                    MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
                MethodDefinitionHandle constructor = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                    MethodImplAttributes.Runtime, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(new byte[] { 0x20, 0, 1 }), 0, MetadataTokens.ParameterHandle(1));
                metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed, metadata.GetOrAddString("System.Runtime.CompilerServices"),
                    metadata.GetOrAddString("ReferenceAssemblyAttribute"), default, MetadataTokens.FieldDefinitionHandle(1), constructor);
                metadata.AddCustomAttribute(identity, constructor, metadata.GetOrAddBlob(new byte[] { 1, 0, 0, 0 }));
            }
            var image = new BlobBuilder();
            new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
                new MetadataRootBuilder(metadata), new BlobBuilder()).Serialize(image);
            File.WriteAllBytes(Path.Combine(Root, name), image.ToArray());
        }
        internal TaskItem Asset(string path, bool required = false)
        {
            var asset = new TaskItem("selected"); asset.SetMetadata("RelativePath", path);
            asset.SetMetadata("CisExplicitSelection", required ? "TRUE" : "false"); return asset;
        }
        internal WriteTypeInventory Create(params ITaskItem[] assets) => new() { BuildEngine = Engine, OutputDirectory = Root, HostFileName = "Host.dll", Assets = assets };
        internal void Rejected(WriteTypeInventory task, string error)
        {
            Assert.IsFalse(task.Execute()); Assert.HasCount(1, Engine.Errors);
            StringAssert.Contains(Engine.Errors.Single(), "CISTM001"); StringAssert.Contains(Engine.Errors.Single(), error);
            Assert.IsFalse(File.Exists(Path.Combine(Root, "CIS.TypeManager.Build.types.json")));
        }
        internal void OnlyHost(WriteTypeInventory task)
        {
            Assert.IsTrue(task.Execute()); Assert.IsEmpty(Engine.Errors);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(task.ManifestPath));
            Assert.AreEqual(1, document.RootElement.GetProperty("assemblies").GetArrayLength());
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class Engine : IBuildEngine
    {
        internal List<string> Errors { get; } = [];
        internal List<string> Messages { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "qualification.proj";
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? "");
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? "");
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => throw new NotSupportedException();
    }
}

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class InventoryMarkerAttribute<T> : Attribute { }
