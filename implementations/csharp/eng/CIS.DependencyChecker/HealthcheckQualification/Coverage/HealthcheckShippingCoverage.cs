using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class HealthcheckCoverageVerification
{
    internal static void ValidateProducerProfile(bool currentCollection, JsonObject producer)
    {
        bool currentProducer = (string?)producer["schema"] == HealthcheckPackageVerification.PreparedInputSchema;
        Require(!currentCollection || currentProducer,
            "Current shipping collection requires current prepared-input provenance; a legacy producer cannot close current candidate acceptance.");
        Require(!currentProducer || currentCollection,
            "Current prepared provenance requires a named four-family shipping receipt; an unmarked legacy collection cannot bypass its selection policy.");
    }

    internal static bool ValidateCollectionProfile(JsonObject collection)
    {
        bool current = collection["schema"] is not null || collection["profile"] is not null
            || collection.ContainsKey("testSelection") || collection.ContainsKey("uninstrumentedObservationsExecuted");
        string[] expected = current ? ShippingCoverageCollection.ReceiptFamilies.Keys.ToArray() : ["unit", "integration", "business"];
        bool selected = (string?)collection["schema"] == ShippingCoverageCollection.ReceiptSchema
            && (string?)collection["profile"] == ShippingCoverageCollection.ReceiptProfile;
        bool unfiltered = (string?)collection["schema"] == ShippingCoverageCollection.LegacyReceiptSchema
            && (string?)collection["profile"] == ShippingCoverageCollection.LegacyReceiptProfile;
        Require(!current || (selected || unfiltered) && (bool?)collection["complete"] == true,
            "Unknown or incomplete current shipping coverage receipt.");
        JsonArray families = collection["families"]?.AsArray() ?? throw new InvalidDataException("Missing native coverage families.");
        Require(families.Count == expected.Length && families.Select(family => (string?)family?["name"]).ToHashSet(StringComparer.Ordinal).SetEquals(expected),
            "Coverage requires the exact native family inventory of its declared profile.");
        if (current)
        {
            Require(families.All(family => (string?)family!["status"] == "Passed" && (int?)family["exitCode"] == 0 && (bool?)family["outputDrained"] == true
                && (string?)family["trxSha256"] is { Length: 64 }), "Current shipping collection has an unfinished or failed native family.");
            JsonObject policy = selected ? new JsonObject
            {
                ["collectionProfile"] = ShippingCoverageCollection.ReceiptProfile,
                ["testSelection"] = ShippingCoverageCollection.TestSelection()
            } : new JsonObject();
            ShippingCoverageCollection.ValidateSelection(collection, policy);
            if (selected)
            {
                Require((string?)collection["commands"]?["path"] == "commands.json"
                    && (string?)collection["commands"]?["sha256"] is { Length: 64 }, "Current selected coverage omitted its actual native command log pin.");
            }
        }
        return current;
    }

    private static async Task<JsonObject> AnalyzeShippingCollection(string language, string package, string reports, JsonObject collection,
        IReadOnlyDictionary<string, Source> sources, string coreHash, Dictionary<string, string> pins)
    {
        string policy = Path.Combine(language, "verification/coverage/shipping.json");
        JsonObject policyReference = collection["policy"]?.AsObject() ?? throw new InvalidDataException("Current collection has no policy pin.");
        Require(Path.GetFullPath((string)policyReference["path"]!) == Path.GetFullPath(policy)
            && (string?)policyReference["sha256"] == Pin(policy, pins), "Current shipping coverage policy pin differs.");
        JsonObject actualPolicy = Read(policy); ShippingCoverageCollection.ValidatePolicy(language, actualPolicy); ShippingCoverageCollection.ValidateReceiptPolicy(actualPolicy);
        ShippingCoverageCollection.ValidateSelection(collection, actualPolicy);
        bool selected = (string?)collection["profile"] == ShippingCoverageCollection.ReceiptProfile;
        if (selected)
        {
            string commandPath = SourceReplay.InputPath(reports, "commands.json");
            Require(Pin(commandPath, pins) == (string?)collection["commands"]?["sha256"], "The actual selected coverage commands changed.");
            JsonArray commands = JsonNode.Parse(File.ReadAllText(commandPath))!.AsArray();
            ValidateShippingCommands(commands, language, reports, actualPolicy["settings"]!.GetValue<string>());
        }
        SourceInventoryEntry[] moduleInputs = ValidateShippingModules(language, package, collection, actualPolicy);
        foreach (SourceInventoryEntry pin in moduleInputs)
        { Require(Pin(SourceReplay.InputPath(language, pin.Path), pins) == pin.Sha256, "Shipping module source pin changed."); }
        var directories = new List<string>(); var families = new JsonArray();
        foreach (JsonObject family in collection["families"]!.AsArray().OfType<JsonObject>())
        {
            string name = family["name"]!.GetValue<string>(), relative = name + "/report.trx";
            Require((string?)family["trx"] == relative, "Current shipping native TRX path differs from its family.");
            Require(JsonNode.DeepEquals(family["coreCopies"], ShippingCoverageCollection.CoreCopies(language, name, coreHash)), "Current native Core copies changed or are omitted.");
            foreach (JsonObject copy in family["coreCopies"]!.AsArray().OfType<JsonObject>())
            { Require(Pin((string)copy["path"]!, pins) == (string?)copy["sha256"], "A native Core copy differs from its actual receipt pin."); }
            string trx = SourceReplay.InputPath(reports, relative);
            Require(Pin(trx, pins) == (string?)family["trxSha256"], "Current native TRX differs from its receipt pin.");
            if (selected && name == "unit")
            {
                Require(DateTimeOffset.TryParse((string?)family["startedUtc"], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset started), "Selected Unit coverage omitted its actual launch time.");
                ShippingCoverageCollection.ValidateUnitSelection(FocusedTests.ReadResults(trx, started),
                    Path.Combine(language, "tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll"));
            }
            (long passed, string native) = ValidateTrx(trx, Path.GetDirectoryName(trx)!);
            families.Add(new JsonObject
            {
                ["family"] = name,
                ["testsPassed"] = passed,
                ["trx"] = trx,
                ["trxSha256"] = Pin(trx, pins),
                ["cobertura"] = native,
                ["coberturaSha256"] = Pin(native, pins)
            });
            directories.Add(Path.GetDirectoryName(trx)!);
        }
        JsonObject reference = collection["shippingCoverage"]?.AsObject() ?? throw new InvalidDataException("Current collection has no completed shipping observation.");
        string observationPath = SourceReplay.InputPath(reports, (string)reference["path"]!);
        Require(Pin(observationPath, pins) == (string?)reference["sha256"], "The original shipping observation changed.");
        JsonObject original = Read(observationPath);
        JsonObject recomputed = await ShippingCoverage.SummarizeAsync(language, directories);
        Require(JsonNode.DeepEquals(original, recomputed), "Raw shipping reports no longer reproduce the original two-module observation.");
        Require((int?)collection["shippingAnalysisExitCode"] == ((bool?)original["qualified"] == true ? 0 : 2), "The retained full shipping gate differs from its actual analyzer exit.");
        JsonObject result = SelectShippingHealthcheck(recomputed, sources);
        result["families"] = families;
        result["collectionProfile"] = collection["profile"]!.DeepClone();
        if (selected)
        {
            result["testSelection"] = collection["testSelection"]!.DeepClone();
            result["uninstrumentedObservationsExecuted"] = false;
        }
        result["fullShippingCoverage"] = new JsonObject
        {
            ["path"] = observationPath,
            ["sha256"] = Pin(observationPath, pins),
            ["qualified"] = original["qualified"]!.DeepClone(),
            ["scope"] = "Original complete Core/build-helper gate retained separately; Healthcheck subset results do not qualify the other module or a release."
        };
        foreach (SourceInventoryEntry pin in moduleInputs)
        { Require(Pin(SourceReplay.InputPath(language, pin.Path), pins) == pin.Sha256, "Shipping module source changed during analysis."); }
        _ = ValidateShippingModules(language, package, collection, actualPolicy);
        foreach (JsonObject family in collection["families"]!.AsArray().OfType<JsonObject>())
        { Require(JsonNode.DeepEquals(family["coreCopies"], ShippingCoverageCollection.CoreCopies(language, family["name"]!.GetValue<string>(), coreHash)), "Native Core copy membership changed during analysis."); }
        return result;
    }

    internal static void ValidateShippingCommands(JsonArray commands, string language, string reports, string settings)
    {
        Require(commands.Count == ShippingCoverageCollection.ReceiptFamilies.Count, "Selected coverage needs exactly one actual command per native family.");
        foreach ((string name, string suffix) in ShippingCoverageCollection.ReceiptFamilies)
        {
            JsonObject[] rows = commands.OfType<JsonObject>().Where(command => (string?)command["family"] == name).ToArray();
            Require(rows.Length == 1, "Missing or duplicate selected coverage native command: " + name);
            JsonObject command = rows[0];
            Require((int?)command["exitCode"] == 0 && !string.IsNullOrWhiteSpace((string?)command["executable"])
                && string.Equals(Path.GetFullPath((string?)command["workingDirectory"] ?? ""), Path.GetFullPath(language),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && command["argv"] is JsonArray arguments
                && arguments.Select(argument => argument!.GetValue<string>()).SequenceEqual(
                    ShippingCoverageCollection.CollectorArguments(suffix, settings, Path.Combine(reports, name)), StringComparer.Ordinal),
                "Actual coverage command differs from its exact declared family selection: " + name);
        }
    }

    internal static SourceInventoryEntry[] ValidateShippingModules(string language, string package, JsonObject collection, JsonObject policy)
    {
        ShippingCoverageCollection.ValidateReceiptPolicy(policy);
        JsonObject[] modules = collection["modules"]?.AsArray().OfType<JsonObject>().ToArray() ?? throw new InvalidDataException("Current collection omits actual module pins.");
        Require(modules.Length == 2 && modules.Select(module => (string?)module["name"]).ToHashSet(StringComparer.Ordinal).SetEquals(["CIS.Core", "CIS.TypeManager.Build"]), "Current shipping module pins have an unexpected inventory.");
        var sources = new List<SourceInventoryEntry>();
        using ZipArchive archive = ZipFile.OpenRead(package);
        string[] expectedEntries = policy["assemblies"]!.AsArray().Select(module => (string)module!["packageEntry"]!).Order(StringComparer.Ordinal).ToArray();
        Require(archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Select(entry => entry.FullName).Order(StringComparer.Ordinal)
            .SequenceEqual(expectedEntries, StringComparer.Ordinal), "Package DLL scope differs from the actual shipping collection.");
        foreach (JsonObject module in modules)
        {
            JsonObject expected = policy["assemblies"]!.AsArray().OfType<JsonObject>().Single(item => (string?)item["name"] == (string?)module["name"]);
            Require((string?)module["sourceRoot"] == (string?)expected["sourceRoot"] && (string?)module["packageEntry"] == (string?)expected["packageEntry"], "Shipping module source/package identity differs.");
            using Stream stream = archive.GetEntry((string)module["packageEntry"]!)!.Open();
            string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
            string binary = Path.Combine(language, Path.GetDirectoryName((string)expected["project"]!)!, "bin/Release/net10.0", (string)module["name"]! + ".dll");
            Require((string?)module["sha256"] == hash && Hash(binary) == hash, "Current module and packed bytes differ from the receipt.");
            JsonArray sourcePins = module["sourceInputs"]?.AsArray() ?? throw new InvalidDataException("Missing module source pins.");
            SourceInventoryEntry[] inputs = JsonSerializer.Deserialize<SourceInventoryEntry[]>(sourcePins.ToJsonString()) ?? throw new InvalidDataException("Missing module source pins.");
            SourceReplay.ValidateInventory(inputs); SourceReplay.AssertInputs(language, inputs);
            string root = SourceReplay.InputPath(language, (string)module["sourceRoot"]!);
            string[] current = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                .Select(path => Path.GetRelativePath(language, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
            Require(current.SequenceEqual(inputs.Select(pin => pin.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal), "Shipping module source membership changed.");
            sources.AddRange(inputs);
        }
        return sources.ToArray();
    }

    internal static JsonObject SelectShippingHealthcheck(JsonObject shipping, IReadOnlyDictionary<string, Source> sources)
    {
        Require((string?)shipping["schema"] == "cis.csharp.shipping-coverage-observation/v1", "Unexpected recomputed shipping observation.");
        JsonObject[] assemblies = shipping["assemblies"]!.AsArray().OfType<JsonObject>().ToArray();
        Require(assemblies.Length == 2 && assemblies.Select(assembly => (string?)assembly["assembly"]).ToHashSet(StringComparer.Ordinal).SetEquals(["CIS.Core", "CIS.TypeManager.Build"]), "Recomputed shipping assembly inventory differs.");
        JsonObject core = assemblies.Single(assembly => (string?)assembly["assembly"] == "CIS.Core");
        JsonObject[] selected = core["sources"]!.AsArray().OfType<JsonObject>()
            .Where(source => ((string?)source["path"])?.StartsWith("implementations/csharp/" + HealthcheckPrefix, StringComparison.Ordinal) == true).ToArray();
        string[] expected = sources.Values.Where(source => source.IsHealthcheck).Select(source => source.Path).Order(StringComparer.Ordinal).ToArray();
        Require(selected.Select(source => ((string)source["path"]!)["implementations/csharp/".Length..]).Order(StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal), "Shipping observation omits or adds a Healthcheck source.");
        long lines = 0, covered = 0, branches = 0, hitBranches = 0;
        var files = new JsonArray(); var gaps = new JsonArray();
        foreach (JsonObject source in selected)
        {
            string path = ((string)source["path"]!)["implementations/csharp/".Length..];
            Source original = sources[path];
            Require((string?)source["sha256"] == original.Sha256 && ((string?)source["nonExecutableReason"] is not null) == (original.NonExecutableReason is not null), "Selected Healthcheck source/hash/declaration scope differs.");
            long lc = source["linesCovered"]!.GetValue<long>(), lt = source["linesTotal"]!.GetValue<long>(),
                bc = source["branchesCoveredLowerBound"]!.GetValue<long>(), bt = source["branchesTotal"]!.GetValue<long>();
            Require(lc >= 0 && lc <= lt && bc >= 0 && bc <= bt && (lt > 0 || original.NonExecutableReason is not null), "Invalid or unmeasured Healthcheck source counts.");
            checked { covered += lc; lines += lt; hitBranches += bc; branches += bt; }
            JsonObject copy = source.DeepClone().AsObject(); copy["path"] = path; files.Add(copy);
            if (lc < lt || bc < bt) { gaps.Add(new JsonObject { ["path"] = path, ["uncoveredLines"] = source["uncoveredLines"]!.DeepClone(), ["partialBranchLines"] = source["partiallyCoveredBranchLines"]!.DeepClone() }); }
        }
        bool passed = lines > 0 && branches > 0 && (decimal)covered * 100 >= (decimal)lines * 95 && (decimal)hitBranches * 100 >= (decimal)branches * 90;
        return new JsonObject
        {
            ["sourceFileCount"] = files.Count,
            ["declarationOnlyFileCount"] = sources.Values.Count(source => source.IsHealthcheck && source.NonExecutableReason is not null),
            ["sources"] = files,
            ["rawGaps"] = gaps,
            ["qualified"] = passed,
            ["aggregation"] = "Recomputed unchanged four-family two-module raw observation; sum disjoint Healthcheck source counts only after the existing native line OR/branch MAX aggregation.",
            ["aggregate"] = new JsonObject
            {
                ["linesCovered"] = covered,
                ["linesTotal"] = lines,
                ["branchesCoveredLowerBound"] = hitBranches,
                ["branchesTotal"] = branches,
                ["lineRate"] = lines > 0 ? (double)covered / lines : null,
                ["branchRateLowerBound"] = branches > 0 ? (double)hitBranches / branches : null,
                ["thresholdPassed"] = passed
            }
        };
    }
}
