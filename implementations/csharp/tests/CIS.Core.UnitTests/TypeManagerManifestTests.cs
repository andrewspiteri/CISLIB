using System.Reflection;
using System.Text.Json.Nodes;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class TypeManagerManifestTests
{
    private string directory = null!;
    private string manifestPath = null!;
    private static Assembly Host => typeof(TypeManagerManifestTests).Assembly;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "cis-type-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.Copy(Host.Location, Path.Combine(directory, "Host.dll"));
        manifestPath = Path.Combine(directory, "host.types.json");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    public async Task ValidInventoryIgnoresAdditiveFieldsAndDeduplicatesIdenticalEntries()
    {
        var json = Valid();
        json["future"] = new JsonObject { ["nested"] = new JsonArray(1, 2) };
        json["assemblies"]!.AsArray().Add(json["assemblies"]![0]!.DeepClone());
        await Save(json);
        var inventory = await AssemblyInventory.ReadAsync(manifestPath, Host, default);
        Assert.HasCount(1, inventory.Assemblies);
        Assert.AreEqual(Host.FullName, inventory.Assemblies[0].Identity);
        Assert.Throws<NotSupportedException>(() => ((IList<SelectedAssembly>)inventory.Assemblies).Clear());
    }

    [TestMethod]
    [DataRow("schema-missing")]
    [DataRow("schema-string")]
    [DataRow("schema-null")]
    [DataRow("schema-fraction")]
    [DataRow("schema-new")]
    [DataRow("host-missing")]
    [DataRow("host-mismatch")]
    [DataRow("host-not-selected")]
    [DataRow("assemblies-missing")]
    [DataRow("assemblies-object")]
    [DataRow("assemblies-empty")]
    [DataRow("entry-scalar")]
    [DataRow("identity-simple")]
    [DataRow("identity-blank")]
    [DataRow("path-null")]
    [DataRow("path-empty")]
    [DataRow("path-missing-file")]
    [DataRow("path-rooted")]
    [DataRow("path-parent")]
    [DataRow("path-backslash")]
    [DataRow("path-uri")]
    [DataRow("path-dot")]
    [DataRow("path-empty-segment")]
    [DataRow("identity-conflict")]
    [DataRow("version-conflict")]
    [DataRow("path-conflict")]
    public async Task InvalidInputsFailWithManifestAndOriginalCause(string mode)
    {
        var json = Valid();
        var entry = json["assemblies"]![0]!.AsObject();
        switch (mode)
        {
            case "schema-missing": json.Remove("schemaVersion"); break;
            case "schema-string": json["schemaVersion"] = "1"; break;
            case "schema-null": json["schemaVersion"] = null; break;
            case "schema-fraction": json["schemaVersion"] = 1.5; break;
            case "schema-new": json["schemaVersion"] = 2; break;
            case "host-missing": json.Remove("hostAssembly"); break;
            case "host-mismatch": json["hostAssembly"] = typeof(object).Assembly.FullName; break;
            case "host-not-selected": entry["assemblyName"] = "Other, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"; break;
            case "assemblies-missing": json.Remove("assemblies"); break;
            case "assemblies-object": json["assemblies"] = new JsonObject(); break;
            case "assemblies-empty": json["assemblies"] = new JsonArray(); break;
            case "entry-scalar": json["assemblies"]![0] = 7; break;
            case "identity-simple": entry["assemblyName"] = Host.GetName().Name; break;
            case "identity-blank": entry["assemblyName"] = " "; break;
            case "path-null": entry["relativePath"] = null; break;
            case "path-empty": entry["relativePath"] = ""; break;
            case "path-missing-file": entry["relativePath"] = "missing.dll"; break;
            case "path-rooted": entry["relativePath"] = "/Host.dll"; break;
            case "path-parent": entry["relativePath"] = "../Host.dll"; break;
            case "path-backslash": entry["relativePath"] = "folder\\Host.dll"; break;
            case "path-uri": entry["relativePath"] = "file:///Host.dll"; break;
            case "path-dot": entry["relativePath"] = "./Host.dll"; break;
            case "path-empty-segment": entry["relativePath"] = "folder//Host.dll"; break;
            case "identity-conflict":
                File.Copy(Host.Location, Path.Combine(directory, "Other.dll"));
                json["assemblies"]!.AsArray().Add(new JsonObject { ["assemblyName"] = Host.FullName, ["relativePath"] = "Other.dll" });
                break;
            case "version-conflict":
                var changedVersion = Host.GetName();
                changedVersion.Version = new Version(9, 0, 0, 0);
                File.Copy(Host.Location, Path.Combine(directory, "Version9.dll"));
                json["assemblies"]!.AsArray().Add(new JsonObject { ["assemblyName"] = changedVersion.FullName, ["relativePath"] = "Version9.dll" });
                break;
            case "path-conflict":
                json["assemblies"]!.AsArray().Add(new JsonObject { ["assemblyName"] = "Other, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", ["relativePath"] = "Host.dll" });
                break;
            default: Assert.Fail(mode); break;
        }
        await Save(json);
        var error = await Assert.ThrowsAsync<TypeManagerInitializationException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, default));
        Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, error.FailureKind);
        Assert.AreEqual(manifestPath, error.ManifestPath);
        Assert.IsNotNull(error.InnerException);
        if (mode.StartsWith("path-", StringComparison.Ordinal) && mode is not "path-null" and not "path-empty")
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.AssetPath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.AssemblyIdentity));
        }
    }

    [TestMethod]
    [DataRow("nested\\Host.dll")]
    [DataRow("./Host.dll")]
    [DataRow("nested/./Host.dll")]
    [DataRow("nested//Host.dll")]
    [DataRow("nested/../Host.dll")]
    public async Task NonNormalizedPathsAreRejectedEvenWhenTheSelectedAssemblyExists(string relativePath)
    {
        string target = Path.GetFullPath(relativePath, directory);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target)) { File.Copy(Host.Location, target); }
        var json = Valid();
        json["assemblies"]![0]!["relativePath"] = relativePath;
        await Save(json);

        var error = await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, default));
        Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, error.FailureKind);
        Assert.IsInstanceOfType<FormatException>(error.InnerException);
        StringAssert.Contains(error.InnerException.Message, "normalized relative assembly path");
        Assert.AreEqual(relativePath, error.AssetPath);
        Assert.AreEqual(Host.FullName, error.AssemblyIdentity);
        Assert.AreEqual(manifestPath, error.ManifestPath);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("[]")]
    public async Task MalformedOrDuplicateJsonIsNeverAccepted(string json)
    {
        await File.WriteAllTextAsync(manifestPath, json);
        var error = await Assert.ThrowsAsync<TypeManagerInitializationException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, default));
        Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, error.FailureKind);
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("entry")]
    [DataRow("unknown")]
    [DataRow("nested-array")]
    public async Task DuplicatePropertiesFailEvenWhenAllRequiredFieldsAreOtherwiseValid(string scope)
    {
        string json = Valid().ToJsonString();
        json = scope switch
        {
            "root" => json.Insert(1, "\"schemaVersion\":1,"),
            "entry" => json.Replace("\"relativePath\":\"Host.dll\"", "\"relativePath\":\"Host.dll\",\"relativePath\":\"Host.dll\"", StringComparison.Ordinal),
            "unknown" => json.Insert(1, "\"future\":{\"a\":1,\"a\":2},"),
            "nested-array" => json.Insert(1, "\"future\":[{\"a\":1,\"a\":2}],"),
            _ => throw new ArgumentException(scope)
        };
        await File.WriteAllTextAsync(manifestPath, json);
        var error = await Assert.ThrowsAsync<TypeManagerInitializationException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, default));
        Assert.IsInstanceOfType<FormatException>(error.InnerException);
        StringAssert.Contains(error.InnerException.Message, "Duplicate JSON property");
    }

    [TestMethod]
    public async Task InvalidUtf8CannotBeReplacedWithAValidJsonString()
    {
        byte[] prefix = System.Text.Encoding.UTF8.GetBytes("{\"unknown\":\"");
        byte[] suffix = System.Text.Encoding.UTF8.GetBytes("\"," + Valid().ToJsonString()[1..]);
        await File.WriteAllBytesAsync(manifestPath, [.. prefix, 0xff, .. suffix]);
        var error = await Assert.ThrowsAsync<TypeManagerInitializationException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, default));
        Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, error.FailureKind);
    }

    [TestMethod]
    public async Task MissingManifestRetainsFileFailureAndCancellationIsNotWrapped()
    {
        var missing = await Assert.ThrowsAsync<TypeManagerInitializationException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, default));
        Assert.IsInstanceOfType<FileNotFoundException>(missing.InnerException);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, cancellation.Token));
    }

    [TestMethod]
    public async Task Utf8BomIsAcceptedAndHostMismatchHasBothIdentitiesInVisibleDiagnostics()
    {
        await File.WriteAllBytesAsync(manifestPath, [0xef, 0xbb, 0xbf, .. System.Text.Encoding.UTF8.GetBytes(Valid().ToJsonString())]);
        Assert.HasCount(1, (await AssemblyInventory.ReadAsync(manifestPath, Host, default)).Assemblies);
        var json = Valid();
        json["hostAssembly"] = typeof(object).Assembly.FullName;
        await Save(json);
        var error = await Assert.ThrowsAsync<TypeManagerInitializationException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, default));
        Assert.AreEqual(typeof(object).Assembly.FullName, error.AssemblyIdentity);
        Assert.AreEqual(Host.FullName, error.ActualAssemblyIdentity);
        StringAssert.Contains(error.ToString(), manifestPath);
        StringAssert.Contains(error.ToString(), Host.FullName!);
        StringAssert.Contains(error.ToString(), typeof(object).Assembly.FullName!);
    }

    [TestMethod]
    public async Task PlatformPathRulesRejectWindowsAliasesAndRetainLinuxFilenames()
    {
        foreach (var invalid in new[] { "Host.dll.", "Host.dll " })
        {
            var json = Valid();
            json["assemblies"]![0]!["relativePath"] = invalid;
            await Save(json);
            if (OperatingSystem.IsWindows())
            {
                var error = await Assert.ThrowsAsync<TypeManagerInitializationException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, default));
                Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, error.FailureKind);
                StringAssert.Contains(error.Message, invalid);
            }
            else
            {
                File.Copy(Host.Location, Path.Combine(directory, invalid), overwrite: true);
                Assert.HasCount(1, (await AssemblyInventory.ReadAsync(manifestPath, Host, default)).Assemblies);
            }
        }
    }

    [TestMethod]
    public async Task PathCaseDuplicatesUseThePlatformComparisonRule()
    {
        File.Copy(Host.Location, Path.Combine(directory, "HOST.dll"), overwrite: true);
        var json = Valid();
        json["assemblies"]!.AsArray().Add(new JsonObject { ["assemblyName"] = Host.FullName, ["relativePath"] = "HOST.dll" });
        await Save(json);
        if (OperatingSystem.IsWindows())
        {
            Assert.HasCount(1, (await AssemblyInventory.ReadAsync(manifestPath, Host, default)).Assemblies);
        }
        else
        {
            var error = await Assert.ThrowsAsync<TypeManagerInitializationException>(() => AssemblyInventory.ReadAsync(manifestPath, Host, default));
            Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, error.FailureKind);
        }
    }

    private Task Save(JsonObject json) => File.WriteAllTextAsync(manifestPath, json.ToJsonString());
    private static JsonObject Valid() => new()
    {
        ["schemaVersion"] = 1,
        ["hostAssembly"] = Host.FullName,
        ["assemblies"] = new JsonArray(new JsonObject { ["assemblyName"] = Host.FullName, ["relativePath"] = "Host.dll" })
    };
}
