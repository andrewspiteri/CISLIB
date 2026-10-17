using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ArchitectureOwnershipTests
{
    private static readonly string Root = FindRoot();
    private static JsonObject Current()
    {
        string? inspection = System.Environment.GetEnvironmentVariable("CIS_ARCHITECTURE_INSPECTION");
        if (!string.IsNullOrWhiteSpace(inspection)) { return JsonNode.Parse(File.ReadAllText(inspection))!.AsObject(); }
        string reports = Path.GetFullPath("../../.artifacts/csharp/verification/Developer", Root);
        JsonNode summary = JsonNode.Parse(File.ReadAllText(Path.Combine(reports, "summary.json")))!;
        return JsonNode.Parse(File.ReadAllText(Path.Combine(reports, summary["architectureInspection"]!["path"]!.GetValue<string>())))!.AsObject();
    }
    private static JsonObject Facts(JsonObject report) => report["assemblies"]!.AsArray().Single(item => Text(item!, "project") == "src/CIS.Core/CIS.Core.csproj")!["facts"]!.AsObject();
    private static JsonObject Type(JsonObject facts, string name) => facts["types"]!.AsArray().Single(item => Text(item!, "name") == name)!.AsObject();
    private static JsonObject Field(JsonObject type, string name) => type["members"]!.AsArray().Single(item => Text(item!, "kind") == "field" && Text(item!, "name") == name)!.AsObject();
    private static JsonObject Member(string kind, string name, string signature, string attributes = "Private, InitOnly") => new() { ["kind"] = kind, ["name"] = name, ["signature"] = signature, ["attributes"] = attributes, ["publicApi"] = false };
    private static string Text(JsonNode node, string key) => node[key]?.GetValue<string>() ?? "";

    [TestMethod]
    public void ActualNativePackageAndImmutableGraphsPassWithTheApprovedSetupInputs()
    {
        JsonObject report = Current();
        JsonObject result = ArchitectureOwnershipPolicy.Assert(Root, report);
        Assert.AreEqual(0, result["violations"]!.GetValue<int>());
        Assert.IsTrue(result["immutableTypes"]!.GetValue<int>() >= 20);
        Assert.IsTrue(result["checkedContractUses"]!.GetValue<int>() > 100);
        Assert.AreEqual(3, result["mutableSetupProperties"]!.GetValue<int>());
        Assert.AreEqual(report["packageAssets"]!.AsArray().Count, result["packageAssets"]!.GetValue<int>());
        Assert.IsTrue(JsonNode.DeepEquals(report["ownership"], result));
    }

    [TestMethod]
    [DataRow("missing-value", "ARCH-OWNERSHIP-TYPE")]
    [DataRow("unsealed", "ARCH-OWNERSHIP-SHAPE")]
    [DataRow("inherited-state", "ARCH-OWNERSHIP-SHAPE")]
    [DataRow("mutable-capture", "ARCH-OWNERSHIP-MUTABLE")]
    [DataRow("mutable-incoming-accounting", "ARCH-OWNERSHIP-MUTABLE")]
    [DataRow("incoming-accounting-writer", "ARCH-OWNERSHIP-FIELD")]
    [DataRow("borrowed-options", "ARCH-OWNERSHIP-FIELD")]
    [DataRow("borrowed-map", "ARCH-OWNERSHIP-FIELD")]
    [DataRow("borrowed-child", "ARCH-OWNERSHIP-FIELD")]
    [DataRow("public-buffer", "ARCH-OWNERSHIP-FIELD")]
    [DataRow("setter", "ARCH-OWNERSHIP-SETTER")]
    [DataRow("property-buffer", "ARCH-OWNERSHIP-EXPOSURE")]
    [DataRow("method-buffer", "ARCH-OWNERSHIP-EXPOSURE")]
    [DataRow("environment-field", "ARCH-ENVIRONMENT-MUTABLE")]
    [DataRow("environment-setter", "ARCH-ENVIRONMENT-SETTER")]
    [DataRow("environment-state", "ARCH-ENVIRONMENT-FIELD")]
    [DataRow("environment-missing-backing", "ARCH-ENVIRONMENT-MUTABLE")]
    [DataRow("environment-nested-state", "ARCH-ENVIRONMENT-MUTABLE")]
    [DataRow("runtime-options", "ARCH-RUNTIME-CALLER-STATE")]
    [DataRow("runtime-dto", "ARCH-RUNTIME-CALLER-STATE")]
    [DataRow("nested-runtime-options", "ARCH-RUNTIME-CALLER-STATE")]
    [DataRow("nested-logger-dto", "ARCH-RUNTIME-CALLER-STATE")]
    [DataRow("provider-options", "ARCH-RUNTIME-CALLER-STATE")]
    [DataRow("contract-writer", "ARCH-CONTRACT-OUTPUT")]
    [DataRow("environment-runtime", "ARCH-CONTRACT-OUTPUT")]
    [DataRow("value-provider", "ARCH-CONTRACT-OUTPUT")]
    [DataRow("nested-contract-writer", "ARCH-CONTRACT-OUTPUT")]
    [DataRow("contract-nested-runtime", "ARCH-CONTRACT-OUTPUT")]
    public void CompiledFactControlsRejectEachOwnershipAndSeparationViolation(string change, string code)
    {
        JsonObject facts = Facts(Current());
        _ = ArchitectureOwnershipPolicy.AssertAssembly(facts);
        JsonObject metadata = Type(facts, "CIS.Logging.EventMetadata"), environment = Type(facts, "CIS.EnvironmentOptions");
        switch (change)
        {
            case "missing-value": facts["types"]!.AsArray().Remove(metadata); break;
            case "unsealed": metadata["attributes"] = "Public, BeforeFieldInit"; break;
            case "inherited-state": metadata["baseType"] = "[System.Runtime]System.Exception"; break;
            case "mutable-capture": Field(metadata, "<ApplicationName>k__BackingField")["attributes"] = "Private"; break;
            case "mutable-incoming-accounting": Field(Type(facts, "CIS.Logging.Runtime.IncomingAccounting"), "<Accepted>k__BackingField")["attributes"] = "Private"; break;
            case "incoming-accounting-writer": Type(facts, "CIS.Logging.Runtime.IncomingAccounting")["members"]!.AsArray().Add(Member("field", "writer", "[CIS.Core]CIS.Logging.Runtime.OutputEventQueue")); break;
            case "borrowed-options": metadata["members"]!.AsArray().Add(Member("field", "caller", "[CIS.Core]CIS.Logging.LoggingOptions")); break;
            case "borrowed-map": Field(Type(facts, "CIS.Logging.LogValue"), "map")["signature"] = "[System.Collections]System.Collections.Generic.Dictionary`2<String, [CIS.Core]CIS.Logging.LogValue>"; break;
            case "borrowed-child": Field(Type(facts, "CIS.Logging.LogValue"), "array")["signature"] = "[System.Runtime]System.Collections.ObjectModel.ReadOnlyCollection`1<[CIS.Core]CIS.Logging.LogEventData>"; break;
            case "public-buffer": Field(Type(facts, "CIS.Logging.Events.EncodedEvent"), "jsonLine")["attributes"] = "Public, InitOnly"; break;
            case "setter": metadata["members"]!.AsArray().Add(Member("method", "set_ApplicationName", "Default generic=0 (String) -> Void", "Public, SpecialName")); break;
            case "property-buffer": metadata["members"]!.AsArray().Add(Member("property", "Buffer", "Default generic=0 () -> Byte[]")); break;
            case "method-buffer": metadata["members"]!.AsArray().Add(Member("method", "GetBuffer", "Default generic=0 () -> Byte[]", "Assembly")); break;
            case "environment-field": Field(environment, "<ProcessId>k__BackingField")["attributes"] = "Private, Static"; break;
            case "environment-setter": environment["members"]!.AsArray().Add(Member("method", "set_ApplicationName", "Default generic=0 (String) -> Void", "Public, Static, SpecialName")); break;
            case "environment-state": environment["members"]!.AsArray().Add(Member("field", "publication", "Object", "Private, Static, InitOnly")); break;
            case "environment-missing-backing": environment["members"]!.AsArray().Remove(Field(environment, "<ApplicationName>k__BackingField")); break;
            case "environment-nested-state":
            case "nested-runtime-options":
            case "nested-logger-dto":
                string nestedName = change == "environment-nested-state" ? "CIS.EnvironmentOptions+Holder" : change == "nested-runtime-options" ? "CIS.Logging.CoreLogRuntime+Nested" : "CIS.Logging.CoreLogger+Nested";
                JsonObject nested = new() { ["name"] = nestedName, ["namespace"] = change == "environment-nested-state" ? "CIS" : "CIS.Logging", ["attributes"] = "NestedPrivate, Sealed", ["baseType"] = "[System.Runtime]System.Object", ["members"] = new JsonArray(Member("field", "caller", "[CIS.Core]CIS.Logging.LoggingOptions")) };
                facts["types"]!.AsArray().Add(nested); break;
            case "provider-options": Type(facts, "CIS.Logging.MicrosoftLogger.CoreLoggerProvider")["members"]!.AsArray().Add(Member("field", "caller", "[CIS.Core]CIS.Logging.LoggingOptions")); break;
            case "runtime-options": Type(facts, "CIS.Logging.CoreLogRuntime")["members"]!.AsArray().Add(Member("field", "caller", "[CIS.Core]CIS.Logging.LoggingOptions")); break;
            case "runtime-dto": Type(facts, "CIS.Logging.Runtime.OutputQueues")["members"]!.AsArray().Add(Member("field", "caller", "[System.Runtime]System.Collections.Generic.IReadOnlyList`1<[CIS.Core]CIS.Logging.LogEventData>")); break;
            default:
                string source = change == "environment-runtime" ? "CIS.EnvironmentOptions" : change == "value-provider" ? "CIS.Logging.LogValue" : change == "nested-contract-writer" ? "CIS.Logging.EventMetadata" : "CIS.Logging.Events.EventProjection";
                string target = change == "contract-nested-runtime" ? "CIS.Logging.CoreLogRuntime+NestedOutput" : change == "environment-runtime" ? "CIS.Logging.CoreLogRuntime" : change == "value-provider" ? "CIS.Logging.MicrosoftLogger.CoreLoggerProvider" : "CIS.Logging.Runtime.FileOutputWriter";
                facts["internalDependencies"]!.AsArray().Add(new JsonObject { ["sourceType"] = change == "nested-contract-writer" ? source + "+NestedContract" : source, ["sourceNamespace"] = Type(facts, source)["namespace"]!.GetValue<string>(), ["targetType"] = target, ["site"] = source + ":controlled-use" }); break;
        }
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ArchitectureOwnershipPolicy.AssertAssembly(facts));
        StringAssert.StartsWith(error.Message, code + ":");
    }

    [TestMethod]
    [DataRow("content", "ARCH-PACKAGE-ASSET")]
    [DataRow("script", "ARCH-PACKAGE-ASSET")]
    [DataRow("test-binary", "ARCH-PACKAGE-ASSET")]
    [DataRow("missing", "ARCH-PACKAGE-ASSET")]
    [DataRow("duplicate", "ARCH-PACKAGE-ASSET")]
    [DataRow("build-bytes", "ARCH-PACKAGE-BUILD-ASSET")]
    [DataRow("version-script", "ARCH-PACKAGE-VERSION-ASSET")]
    [DataRow("version", "ARCH-PACKAGE-VERSION-ASSET")]
    [DataRow("readme", "ARCH-PACKAGE-CONTENT")]
    [DataRow("documentation", "ARCH-PACKAGE-CONTENT")]
    [DataRow("core-dll", "ARCH-PACKAGE-CONTENT")]
    [DataRow("helper-dll", "ARCH-PACKAGE-CONTENT")]
    [DataRow("inventory", "ARCH-PACKAGE-FACTS")]
    [DataRow("duplicate-observed", "ARCH-PACKAGE-FACTS")]
    [DataRow("hash", "ARCH-PACKAGE-FACTS")]
    public void ActualPackageCopiesRejectUnexpectedOrAlteredAssets(string change, string code)
    {
        JsonObject report = Current();
        _ = ArchitectureOwnershipPolicy.AssertPackage(Root, report);
        using var temporary = new TemporaryDirectory();
        string path = Path.Combine(temporary.Path, "candidate.nupkg");
        File.Copy(report["package"]!["path"]!.GetValue<string>(), path);
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            switch (change)
            {
                case "content": Replace("contentFiles/any/any/attachment.txt", "unwanted"); break;
                case "script": Replace("buildTransitive/Unexpected.targets", "<Project />"); break;
                case "test-binary": Replace("tools/net10.0/CIS.Core.UnitTests.dll", "fixture"); break;
                case "missing": archive.GetEntry("README.md")!.Delete(); break;
                case "duplicate": Replace("readme.md", "duplicate"); break;
                case "build-bytes": Replace("buildTransitive/CIS.Core.targets", "<Project />"); break;
                case "version-script": Replace("Sdk/PackageVersion.props", "<Project><Target Name=\"Unexpected\" /></Project>"); break;
                case "version": Replace("Sdk/PackageVersion.props", "<Project><PropertyGroup><_CisCoreSdkPackageVersion>99.0.0</_CisCoreSdkPackageVersion></PropertyGroup></Project>"); break;
                case "core-dll": Replace("lib/net10.0/CIS.Core.dll", "unexpected DLL bytes"); break;
                case "helper-dll": Replace("tools/net10.0/CIS.TypeManager.Build.dll", "unexpected DLL bytes"); break;
                case "documentation": Replace("lib/net10.0/CIS.Core.xml", "unexpected binary content"); break;
                case "readme": Replace("README.md", "changed"); break;
            }
            void Replace(string name, string content)
            {
                archive.GetEntry(name)?.Delete();
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        report["package"]!["path"] = path;
        report["package"]!["sha256"] = Hash(File.ReadAllBytes(path));
        var assets = new JsonArray();
        using (ZipArchive archive = ZipFile.OpenRead(path))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using Stream stream = entry.Open();
                assets.Add(new JsonObject { ["path"] = entry.FullName, ["bytes"] = entry.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(stream)) });
            }
        }
        report["packageAssets"] = assets;
        if (change == "duplicate-observed") { assets.Add(assets[0]!.DeepClone()); }
        if (change == "inventory") { assets[0]!["sha256"] = new string('0', 64); }
        if (change == "hash") { report["package"]!["sha256"] = new string('0', 64); }
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ArchitectureOwnershipPolicy.AssertPackage(Root, report));
        StringAssert.StartsWith(error.Message, code + ":");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "implementations/csharp");
            if (File.Exists(Path.Combine(candidate, "CisLib.slnx"))) { return candidate; }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Run inside CISLIB.");
    }
}
