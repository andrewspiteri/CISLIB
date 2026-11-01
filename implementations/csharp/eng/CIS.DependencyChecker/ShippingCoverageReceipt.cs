using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class ShippingCoverageCollection
{
    internal const string LegacyReceiptSchema = "cis.shipping-coverage-collection/v1";
    internal const string LegacyReceiptProfile = "core-and-build-helper-four-families/v1";
    internal const string ReceiptSchema = "cis.shipping-coverage-collection/v2";
    internal const string ReceiptProfile = "core-and-build-helper-four-families/v2";
    internal const string ExcludedMeasurementMethod = "CIS.Tests.UnitTests.IncomingMeasurementsTests.RecordQuietFullContendedAndCompleteCallerCosts";
    internal const string UnitFilter = "FullyQualifiedName!=" + ExcludedMeasurementMethod;
    internal static JsonObject TestSelection() => new()
    {
        // These counts describe the frozen current cases: three capacities by
        // six levels, and the complete current observation/stress selection.
        ["unitFilter"] = UnitFilter,
        ["excludedMethod"] = ExcludedMeasurementMethod,
        ["expectedExcludedRows"] = 18,
        ["uninstrumentedFilter"] = "TestCategory=PerformanceObservation|TestCategory=ExtendedStress",
        ["uninstrumentedBudgetSeconds"] = 300,
        ["uninstrumentedExpectedRows"] = 52
    };

    internal static string PolicyProfile(JsonObject policy)
    {
        if (!policy.ContainsKey("collectionProfile") && !policy.ContainsKey("testSelection")) { return LegacyReceiptProfile; }
        Require((string?)policy["collectionProfile"] == ReceiptProfile && JsonNode.DeepEquals(policy["testSelection"], TestSelection()),
            "The instrumented test selection must omit only the declared empirical method and retain the separate uninstrumented obligation.");
        return ReceiptProfile;
    }

    internal static void ValidateSelection(JsonObject receipt, JsonObject policy)
    {
        string profile = PolicyProfile(policy);
        Require((string?)receipt["profile"] == profile, "The coverage receipt profile differs from its pinned selection policy; downgrade is forbidden.");
        if (profile == LegacyReceiptProfile)
        {
            Require((string?)receipt["schema"] == LegacyReceiptSchema && !receipt.ContainsKey("testSelection")
                && !receipt.ContainsKey("uninstrumentedObservationsExecuted")
                && receipt["families"]!.AsArray().All(family => !family!.AsObject().ContainsKey("filter")),
                "Original v1 shipping coverage retains its unfiltered meaning.");
            return;
        }
        Require((string?)receipt["schema"] == ReceiptSchema && JsonNode.DeepEquals(receipt["testSelection"], TestSelection()),
            "Current shipping coverage omitted or changed its exact instrumented test selection.");
        Require((bool?)receipt["uninstrumentedObservationsExecuted"] == false,
            "Instrumented coverage cannot claim the separate uninstrumented observation run.");
        foreach (JsonObject family in receipt["families"]!.AsArray().OfType<JsonObject>())
        {
            string? filter = (string?)family["name"] == "unit" ? UnitFilter : null;
            Require(family.ContainsKey("filter") && (string?)family["filter"] == filter,
                "Only the Unit empirical observation method may be omitted from instrumented coverage.");
        }
    }

    internal static void ValidateUnitSelection(IEnumerable<FocusedTests.NativeCase> cases, string expectedAssembly)
    {
        FocusedTests.NativeCase[] rows = cases.ToArray();
        Require(Path.IsPathFullyQualified(expectedAssembly), "The expected native Unit assembly must be fully qualified.");
        string expected = Path.GetFullPath(expectedAssembly);
        Require(rows.All(row => Path.IsPathFullyQualified(row.Source) && string.Equals(Path.GetFullPath(row.Source), expected,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)),
            "Selected Unit coverage contains a case from another or relative native assembly source.");
        Require(rows.All(row => row.Method != ExcludedMeasurementMethod), "Instrumented Unit coverage unexpectedly executed the omitted empirical method.");
        const string prefix = "CIS.Tests.UnitTests.IncomingMeasurementsTests.";
        Require(rows.Count(row => row.Method == prefix + "HeldIndexAndRotatingSlotsRetainAtMostTheirExplicitOccurrenceBounds") == 1
            && rows.Count(row => row.Method == prefix + "RecordActualRuntimeIdlePollingAndQuietWriterLatency") == 3,
            "The instrumented selection lost the real GC ownership or quiet-polling cases; category exclusions are forbidden.");
    }
    internal static readonly IReadOnlyDictionary<string, string> ReceiptFamilies = new Dictionary<string, string>(StringComparer.Ordinal)
    { ["unit"] = "UnitTests", ["integration"] = "IntegrationTests", ["architecture"] = "ArchitectureTests", ["business"] = "BusinessCases" };

    internal static void ValidateReceiptPolicy(JsonObject policy)
    {
        _ = PolicyProfile(policy);
        JsonObject families = policy["families"]!.AsObject();
        Require(families.Count == ReceiptFamilies.Count && ReceiptFamilies.All(family => (string?)families[family.Key] == family.Value), "The current shipping collection requires exactly its four original native families.");
        string[] modules = policy["assemblies"]!.AsArray().Select(module => string.Join('|', new[] { "name", "project", "sourceRoot", "packageEntry" }.Select(key => (string?)module![key]))).Order(StringComparer.Ordinal).ToArray();
        Require(modules.SequenceEqual(new[]
        {
            "CIS.Core|src/CIS.Core/CIS.Core.csproj|src/CIS.Core|lib/net10.0/CIS.Core.dll",
            "CIS.TypeManager.Build|eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj|eng/CIS.TypeManager.Build|tools/net10.0/CIS.TypeManager.Build.dll"
        }, StringComparer.Ordinal), "The shipping collection module inventory differs from the fixed Core/build-helper scope.");
    }

    internal static string FindCollectorPackage(string language, IEnumerable<string> suffixes)
    {
        var found = new List<string>();
        foreach (string suffix in suffixes)
        {
            JsonObject assets = Read(Path.Combine(language, "tests/CIS.Core." + suffix, "obj/project.assets.json"));
            JsonObject[] libraries = assets["libraries"]!.AsObject().Where(pair => pair.Key.Equals("Microsoft.CodeCoverage/18.10.1", StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value!.AsObject()).ToArray();
            Require(libraries.Length == 1 && (string?)libraries[0]["type"] == "package", "The restored native project must select Microsoft.CodeCoverage18.10.1.");
            string archive = libraries[0]["path"]!.GetValue<string>().TrimEnd('/', '\\') + "/microsoft.codecoverage.18.10.1.nupkg";
            string[] candidates = assets["packageFolders"]!.AsObject().Select(folder => SourceReplay.InputPath(folder.Key, archive)).Where(File.Exists).ToArray();
            Require(candidates.Length == 1, "The restored collector archive is missing or ambiguous: " + suffix);
            HealthcheckCoverageVerification.ValidateCollectorPackage(candidates[0]); found.Add(candidates[0]);
        }
        string[] unique = found.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        Require(unique.Length == 1, "Native coverage projects must use one actual pinned collector package location.");
        return unique[0];
    }

    internal static JsonObject CreateReceipt(string language, JsonObject policy, string package, string collectorPackage)
    {
        ValidateReceiptPolicy(policy);
        Require(PolicyProfile(policy) == ReceiptProfile, "New shipping collection requires the explicit v2 selection policy.");
        HealthcheckCoverageVerification.ValidateSettings(System.Xml.Linq.XDocument.Load(Contained(language, policy["settings"]!.GetValue<string>())), shipping: true);
        HealthcheckCoverageVerification.ValidateCollectorPackage(collectorPackage);
        var modules = new JsonArray();
        using (ZipArchive archive = ZipFile.OpenRead(package))
        {
            foreach (JsonObject module in policy["assemblies"]!.AsArray().OfType<JsonObject>())
            {
                string entry = module["packageEntry"]!.GetValue<string>();
                ZipArchiveEntry[] matches = archive.Entries.Where(item => item.FullName == entry).ToArray();
                Require(matches.Length == 1, "Missing or duplicate actual shipping DLL: " + entry);
                using Stream stream = matches[0].Open();
                string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
                Require(Hash(Binary(language, module)) == hash, "Current module output differs from the package.");
                modules.Add(new JsonObject
                {
                    ["name"] = module["name"]!.DeepClone(),
                    ["packageEntry"] = entry,
                    ["sha256"] = hash,
                    ["sourceRoot"] = module["sourceRoot"]!.DeepClone(),
                    ["sourceInputs"] = JsonSerializer.SerializeToNode(Sources(Contained(language, module["sourceRoot"]!.GetValue<string>()), [".cs"])
                        .Order(StringComparer.Ordinal).Select(path => new SourceInventoryEntry(Slashes(Path.GetRelativePath(language, path)), Hash(path))).ToArray())
                });
            }
        }
        string core = modules.OfType<JsonObject>().Single(module => (string?)module["name"] == "CIS.Core")["sha256"]!.GetValue<string>();
        return new JsonObject
        {
            ["schema"] = ReceiptSchema,
            ["profile"] = ReceiptProfile,
            ["testSelection"] = TestSelection(),
            ["uninstrumentedObservationsExecuted"] = false,
            ["complete"] = false,
            ["scope"] = "Actual four-family no-build shipping coverage; only eighteen empirical Unit observation rows are omitted. All other functional, GC and stress cases and full Core/build-helper denominators remain. Native files are unchanged; separate uninstrumented observations are not credited here.",
            ["packageSha256"] = Hash(package),
            ["coreSha256"] = core,
            ["collectorVersion"] = "18.10.1",
            ["collectorPackage"] = Pin(collectorPackage),
            ["settings"] = Pin(Contained(language, policy["settings"]!.GetValue<string>())),
            ["policy"] = Pin(Path.Combine(language, "verification/coverage/shipping.json")),
            ["modules"] = modules,
            ["families"] = new JsonArray(ReceiptFamilies.Select(family => (JsonNode)new JsonObject
            {
                ["name"] = family.Key,
                ["status"] = "NotRun",
                ["exitCode"] = null,
                ["filter"] = family.Key == "unit" ? UnitFilter : null,
                ["trx"] = family.Key + "/report.trx",
                ["coreCopies"] = CoreCopies(language, family.Key, core)
            }).ToArray())
        };
    }

    internal static JsonArray CoreCopies(string language, string family, string coreHash)
    {
        Require(ReceiptFamilies.TryGetValue(family, out string? suffix), "Unknown shipping native family.");
        string directory = Path.Combine(language, "tests/CIS.Core." + suffix, "bin/Release/net10.0");
        string[] copies = Directory.EnumerateFiles(directory, "CIS.Core.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        Require(copies.Contains(Path.Combine(directory, "CIS.Core.dll"), StringComparer.Ordinal), "Missing native host Core copy.");
        return new JsonArray(copies.Select(path => { Require(Hash(path) == coreHash, "A native Core copy differs from the candidate: " + path); return (JsonNode)Pin(path); }).ToArray());
    }

    private static JsonObject Pin(string path) => new() { ["path"] = Path.GetFullPath(path), ["sha256"] = Hash(path) };
}
