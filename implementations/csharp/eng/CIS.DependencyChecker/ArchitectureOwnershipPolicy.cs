using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace CIS.Verification;

// Static shape and dependency guards complement executed caller-mutation tests; readonly alone cannot prove copying.
internal static class ArchitectureOwnershipPolicy
{
    private static readonly string[] RequiredValues =
    [
        "CIS.Configuration.ConfigurationIssue", "CIS.Logging.EventMetadata", "CIS.Logging.LogValue",
        "CIS.Logging.Values.LogValueScalar", "CIS.Logging.Events.CapturedEventFacts", "CIS.Logging.Events.EncodedEvent",
        "CIS.Logging.Scopes.CapturedOperationContext", "CIS.Logging.Scopes.CapturedScopeContext",
        "CIS.Logging.Exceptions.CapturedException", "CIS.Logging.CoreLogDiagnostics", "CIS.Logging.LogOutputDiagnostics",
        "CIS.Logging.LogWriteResult", "CIS.Logging.LogOutputWriteResult", "CIS.Logging.LogFlushResult",
        "CIS.Logging.LogStopResult", "CIS.Logging.LogOutputDrainResult", "CIS.Logging.LogOutputRejectionSnapshot",
        "CIS.Logging.Runtime.AdmittedEvent", "CIS.Logging.Runtime.OutputAccounting", "CIS.Logging.Runtime.RuntimeAccounting"
    ];
    private static readonly Dictionary<string, string> BuildAssets = new(StringComparer.Ordinal)
    {
        ["buildSupport/CIS.TypeManager.targets"] = "build/type-manager/CIS.TypeManager.targets",
        ["Sdk/Sdk.props"] = "build/type-manager/sdk/Sdk.props",
        ["Sdk/Sdk.targets"] = "build/type-manager/sdk/Sdk.targets",
        ["Sdk/BeforeSdk.targets"] = "build/type-manager/sdk/BeforeSdk.targets",
        ["buildTransitive/CIS.Core.targets"] = "build/type-manager/package/CIS.Core.targets"
    };

    internal static JsonObject Assert(string root, JsonObject report)
    {
        JsonObject facts = Objects(report["assemblies"]).Single(item => Text(item, "project") == "src/CIS.Core/CIS.Core.csproj")["facts"]!.AsObject();
        JsonObject result = AssertAssembly(facts);
        result["packageAssets"] = AssertPackage(root, report);
        return result;
    }

    internal static JsonObject AssertAssembly(JsonObject facts)
    {
        Require(Text(facts, "name") == "CIS.Core", "ARCH-OWNERSHIP-ASSEMBLY", Text(facts, "name"));
        Dictionary<string, JsonObject> types = Objects(facts["types"]).ToDictionary(item => Text(item, "name"), StringComparer.Ordinal);
        var values = RequiredValues.ToHashSet(StringComparer.Ordinal);
        // New public observation/result shapes and captured payloads cannot silently escape the guard.
        foreach (JsonObject type in types.Values)
        {
            string name = Text(type, "name"), space = Text(type, "namespace");
            if (space == "CIS.Logging" && Text(type, "baseType") != "[System.Runtime]System.Enum" && (name.EndsWith("Result", StringComparison.Ordinal) || name.EndsWith("Diagnostics", StringComparison.Ordinal) || name.EndsWith("Snapshot", StringComparison.Ordinal)) ||
                new[] { "CIS.Logging.Events.Captured", "CIS.Logging.Scopes.Captured", "CIS.Logging.Exceptions.Captured" }.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            { values.Add(name); }
        }
        foreach (string name in values)
        {
            Require(types.TryGetValue(name, out JsonObject? type), "ARCH-OWNERSHIP-TYPE", name);
            bool structure = Text(type!, "baseType") == "[System.Runtime]System.ValueType";
            Require(structure || Has(type!, "Sealed") && Text(type!, "baseType") == "[System.Runtime]System.Object", "ARCH-OWNERSHIP-SHAPE", name);
            foreach (JsonObject member in Objects(type!["members"]))
            {
                string context = name + ":" + Text(member, "name"), signature = Text(member, "signature");
                if (Text(member, "kind") == "field")
                {
                    Require(Has(member, "Literal") || Has(member, "InitOnly"), "ARCH-OWNERSHIP-MUTABLE", context);
                    // The encoder allocates this private buffer; no caller buffer is passed at a public seam.
                    bool encodedBuffer = name == "CIS.Logging.Events.EncodedEvent" && Text(member, "name") == "jsonLine" && signature == "Byte[]" && Has(member, "Private") && !Has(member, "Static");
                    Require(encodedBuffer || Owned(signature, name == "CIS.Logging.Runtime.AdmittedEvent"), "ARCH-OWNERSHIP-FIELD", context + " " + signature);
                    Require(!member["publicApi"]!.GetValue<bool>() || Owned(signature, name == "CIS.Logging.Runtime.AdmittedEvent"), "ARCH-OWNERSHIP-EXPOSURE", context);
                }
                if (Text(member, "kind") == "method" && Text(member, "name").StartsWith("set_", StringComparison.Ordinal))
                { Require(structure && signature.Contains("modreq([System.Runtime]System.Runtime.CompilerServices.IsExternalInit)", StringComparison.Ordinal), "ARCH-OWNERSHIP-SETTER", context); }
                if (Text(member, "kind") == "method" && !Has(member, "Private") && Text(member, "name") != ".ctor" && !Text(member, "name").StartsWith("set_", StringComparison.Ordinal))
                {
                    string returns = signature[(signature.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..];
                    bool bytes = name == "CIS.Logging.Runtime.AdmittedEvent" || name == "CIS.Logging.Events.EncodedEvent" && Text(member, "name") == "get_JsonLine";
                    Require(returns == "Void" || Owned(returns, bytes), "ARCH-OWNERSHIP-EXPOSURE", context + " " + returns);
                }
                if (Text(member, "kind") == "property")
                {
                    string returns = signature[(signature.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..];
                    Require(Owned(returns, name == "CIS.Logging.Runtime.AdmittedEvent" || name == "CIS.Logging.Events.EncodedEvent" && Text(member, "name") == "JsonLine"), "ARCH-OWNERSHIP-EXPOSURE", context + " " + returns);
                }
            }
        }
        JsonObject environment = types["CIS.EnvironmentOptions"];
        string[] setup = ["EnvironmentKind", "CommitId", "BuildId"];
        var foundSetup = new HashSet<string>(StringComparer.Ordinal);
        Require(!types.Keys.Any(name => name.StartsWith("CIS.EnvironmentOptions+", StringComparison.Ordinal)), "ARCH-ENVIRONMENT-MUTABLE", "nested environment state");
        Require(Objects(environment["members"]).Any(member => Text(member, "name") == ".cctor"), "ARCH-ENVIRONMENT-CONSTRUCTOR", "CIS.EnvironmentOptions");
        foreach (JsonObject member in Objects(environment["members"]))
        {
            string name = Text(member, "name"), signature = Text(member, "signature");
            if (Text(member, "kind") == "field")
            {
                bool mutableSetup = setup.Any(property => name == "<" + property + ">k__BackingField");
                Require(Has(member, "Static") && Has(member, "Private") && (mutableSetup || Has(member, "InitOnly")), "ARCH-ENVIRONMENT-MUTABLE", name);
                bool logging = name == "<Logging>k__BackingField" && signature == "[CIS.Core]CIS.Logging.LoggingOptions";
                Require(logging || Owned(signature), "ARCH-ENVIRONMENT-FIELD", name + " " + signature);
            }
            if (Text(member, "kind") == "property" && !setup.Contains(name, StringComparer.Ordinal))
            {
                JsonObject? storage = Objects(environment["members"]).SingleOrDefault(field => Text(field, "kind") == "field" && Text(field, "name") == "<" + name + ">k__BackingField");
                string returns = signature[(signature.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..];
                Require(storage is not null && Has(storage, "Static") && Has(storage, "InitOnly") && Text(storage, "signature") == returns, "ARCH-ENVIRONMENT-MUTABLE", name + " readonly backing storage");
            }
            if (Text(member, "kind") == "method" && name.StartsWith("set_", StringComparison.Ordinal))
            {
                Require(setup.Contains(name[4..], StringComparer.Ordinal), "ARCH-ENVIRONMENT-SETTER", name);
                foundSetup.Add(name[4..]);
            }
        }
        Require(foundSetup.SetEquals(setup), "ARCH-ENVIRONMENT-SETTER", "approved setup properties");
        // Runtime components consume a captured selection, never retain the mutable caller setup or DTOs.
        string[] callerTypes = ["CIS.Logging.LoggingOptions", "CIS.Logging.LogEventData", "CIS.Logging.OperationContextData"];
        foreach (JsonObject type in types.Values.Where(type => Text(type, "namespace") is "CIS.Logging.Runtime" or "CIS.Logging.MicrosoftLogger" || Text(type, "name").Split('+')[0] is "CIS.Logging.CoreLogRuntime" or "CIS.Logging.CoreLogger"))
        {
            foreach (JsonObject field in Objects(type["members"]).Where(member => Text(member, "kind") == "field"))
            { Require(!callerTypes.Any(caller => Text(field, "signature").Contains("[CIS.Core]" + caller, StringComparison.Ordinal)), "ARCH-RUNTIME-CALLER-STATE", Text(type, "name") + ":" + Text(field, "name")); }
        }
        int edges = 0;
        foreach (JsonObject edge in Objects(facts["internalDependencies"]))
        {
            string source = Text(edge, "sourceType").Split('+')[0], space = Text(edge, "sourceNamespace"), target = Text(edge, "targetType");
            string targetRoot = target.Split('+')[0];
            bool contract = values.Contains(source) || space is "CIS.Configuration" or "CIS.Text" or "CIS.Logging.Configuration" || source is "CIS.EnvironmentOptions" or "CIS.EnvironmentOverrides" or "CIS.Logging.LoggingOptions" or "CIS.Logging.LogEventData" or "CIS.Logging.OperationContextData" ||
                new[] { "CIS.Logging.Events", "CIS.Logging.Values", "CIS.Logging.Exceptions", "CIS.Logging.Scopes", "CIS.Logging.Rendering" }.Contains(space, StringComparer.Ordinal);
            // These readonly runtime values are contracts; their field closure is checked above.
            bool output = target.StartsWith("CIS.Logging.Runtime.", StringComparison.Ordinal) && !values.Contains(target) || targetRoot is "CIS.Logging.CoreLogRuntime" or "CIS.Logging.CoreLogger" || target.StartsWith("CIS.Logging.MicrosoftLogger.", StringComparison.Ordinal);
            if (contract) { edges++; Require(!output, "ARCH-CONTRACT-OUTPUT", Text(edge, "site") + " -> " + target); }
        }
        return new JsonObject { ["schema"] = "cis.architecture-ownership/v1", ["violations"] = 0, ["immutableTypes"] = values.Count, ["checkedContractUses"] = edges, ["mutableSetupProperties"] = setup.Length };

        bool Owned(string signature, bool bytes = false)
        {
            if (new[] { "String", "Boolean", "Byte", "Int32", "Int64", "Double", "[System.Runtime]System.DateTimeOffset", "[System.Runtime]System.TimeSpan", "[System.Runtime]System.Guid" }.Contains(signature, StringComparer.Ordinal)) { return true; }
            if (signature.StartsWith("[CIS.Core]", StringComparison.Ordinal))
            {
                string type = signature[10..];
                return values.Contains(type) || types.TryGetValue(type, out JsonObject? value) && Text(value, "baseType") == "[System.Runtime]System.Enum";
            }
            foreach (string wrapper in new[] { "System.Nullable`1", "System.Collections.Generic.IReadOnlyList`1", "System.Collections.ObjectModel.ReadOnlyCollection`1" })
            {
                string prefix = "[System.Runtime]" + wrapper + "<";
                if (signature.StartsWith(prefix, StringComparison.Ordinal) && signature.EndsWith('>')) { return Owned(signature[prefix.Length..^1], bytes); }
            }
            foreach (string mapType in new[] { "System.Collections.ObjectModel.ReadOnlyDictionary`2", "System.Collections.Generic.IReadOnlyDictionary`2" })
            {
                string map = "[System.Runtime]" + mapType + "<String, ";
                if (signature.StartsWith(map, StringComparison.Ordinal) && signature.EndsWith('>')) { return Owned(signature[map.Length..^1]); }
            }
            // AdmittedEvent exposes the prepared, internally allocated bytes read-only to its output worker.
            return bytes && signature == "[System.Runtime]System.ReadOnlyMemory`1<Byte>";
        }
    }

    internal static int AssertPackage(string root, JsonObject report)
    {
        string path = Text(report["package"]!, "path");
        Require(Hash(path) == Text(report["package"]!, "sha256"), "ARCH-PACKAGE-FACTS", "package hash");
        using ZipArchive archive = ZipFile.OpenRead(path);
        string[] fixedAssets = ["lib/net10.0/CIS.Core.dll", "lib/net10.0/CIS.Core.xml", "README.md", "CIS.Core.nuspec", "[Content_Types].xml", "_rels/.rels", "Sdk/PackageVersion.props", "tools/net10.0/CIS.TypeManager.Build.dll"];
        string[] allowed = fixedAssets.Concat(BuildAssets.Keys).ToArray();
        Require(archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == archive.Entries.Count, "ARCH-PACKAGE-ASSET", "duplicate paths");
        const string metadataPrefix = "package/services/metadata/core-properties/";
        ZipArchiveEntry[] metadata = archive.Entries.Where(entry => entry.FullName.StartsWith(metadataPrefix, StringComparison.Ordinal) && entry.FullName.EndsWith(".psmdcp", StringComparison.Ordinal) && !entry.FullName[metadataPrefix.Length..].Contains('/')).ToArray();
        Require(metadata.Length == 1, "ARCH-PACKAGE-ASSET", "one NuGet core-properties asset");
        var expected = allowed.Append(metadata[0].FullName).ToHashSet(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries) { Require(expected.Contains(entry.FullName), "ARCH-PACKAGE-ASSET", entry.FullName); }
        Require(expected.SetEquals(archive.Entries.Select(entry => entry.FullName)), "ARCH-PACKAGE-ASSET", "missing required asset");
        JsonObject[] assetFacts = Objects(report["packageAssets"]).ToArray();
        Require(assetFacts.Select(item => Text(item, "path")).Distinct(StringComparer.OrdinalIgnoreCase).Count() == assetFacts.Length, "ARCH-PACKAGE-FACTS", "duplicate observed paths");
        Dictionary<string, JsonObject> observed = assetFacts.ToDictionary(item => Text(item, "path"), StringComparer.Ordinal);
        Require(observed.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected), "ARCH-PACKAGE-FACTS", "asset inventory");
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using Stream stream = entry.Open();
            string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            Require(hash == Text(observed[entry.FullName], "sha256") && entry.Length == observed[entry.FullName]["bytes"]!.GetValue<long>(), "ARCH-PACKAGE-FACTS", entry.FullName);
            if (entry.FullName.EndsWith(".dll", StringComparison.Ordinal))
            {
                JsonObject assembly = Objects(report["assemblies"]).Single(item => Text(item, "packageEntry") == entry.FullName);
                Require(hash == Text(assembly["facts"]!, "sha256") && hash == Text(assembly["builtAssembly"]!, "sha256"), "ARCH-PACKAGE-CONTENT", entry.FullName);
            }
            if (BuildAssets.TryGetValue(entry.FullName, out string? source)) { Require(hash == Hash(Path.Combine(root, source)), "ARCH-PACKAGE-BUILD-ASSET", entry.FullName); }
            if (entry.FullName == "lib/net10.0/CIS.Core.xml")
            {
                JsonObject core = Objects(report["assemblies"]).Single(item => Text(item, "project") == "src/CIS.Core/CIS.Core.csproj");
                Require(hash == Hash(Path.ChangeExtension(Text(core["builtAssembly"]!, "path"), ".xml")), "ARCH-PACKAGE-CONTENT", entry.FullName);
            }
            if (entry.FullName == "README.md") { Require(hash == Hash(Path.GetFullPath("../../README.md", root)), "ARCH-PACKAGE-CONTENT", entry.FullName); }
        }
        using Stream manifest = archive.GetEntry("CIS.Core.nuspec")!.Open();
        XElement metadataElement = XDocument.Load(manifest).Descendants().Single(element => element.Name.LocalName == "metadata");
        string version = metadataElement.Elements().Single(element => element.Name.LocalName == "version").Value;
        using Stream props = archive.GetEntry("Sdk/PackageVersion.props")!.Open();
        XDocument actual = XDocument.Load(props);
        XDocument approved = XDocument.Parse("<Project><PropertyGroup><_CisCoreSdkPackageVersion>" + version + "</_CisCoreSdkPackageVersion></PropertyGroup></Project>");
        Require(XNode.DeepEquals(actual.Root, approved.Root), "ARCH-PACKAGE-VERSION-ASSET", "Sdk/PackageVersion.props");
        return archive.Entries.Count;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static IEnumerable<JsonObject> Objects(JsonNode? node) => node?.AsArray().Select(item => item!.AsObject()) ?? [];
    private static string Text(JsonNode value, string key) => value[key]?.GetValue<string>() ?? "";
    private static bool Has(JsonObject value, string attribute) => Text(value, "attributes").Split(", ", StringSplitOptions.RemoveEmptyEntries).Contains(attribute, StringComparer.Ordinal);
    private static void Require(bool condition, string code, string context) { if (!condition) { throw new InvalidDataException(code + ": " + context); } }
}
