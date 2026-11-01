using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class HealthcheckCoverageVerification
{
    private static readonly IReadOnlyDictionary<string, string> DeclarationOnly = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Contracts/HealthEnums.cs"] = "b116889483e362275486d0e995045445746f28d6ddabda87ab4426f96c74a7eb",
        ["Contracts/Execution/HealthExecutionEnums.cs"] = "16eb3eb24b45ec5ff7e05871b87df6ad7caf6a2ab22fab9d65793d4af8188aca",
        ["Contracts/HealthMonitorContracts.cs"] = "276f47fc4a5c852c136afb93e4d6a56dcc12b3a322341a4919122820e3e7359d",
        ["Contracts/Execution/IHealthExecutionBinding.cs"] = "2c35a40e5363db838a89ac1658078fd9122ccc3dfe59cbdf8e2f6bf8674033cd",
        ["Contracts/Collection/IHealthCollector.cs"] = "5b2e45d468dad1ac512ed2a17731fc958fb3915f213a039a46a1cf51d35458a5",
        ["Internal/Contracts/IHealthStateAccess.cs"] = "1d09b7c0a81c4cc236c4b72dbba0a9c047b66a8a2f65deec16beee9fba45ec5f",
        ["Internal/Contracts/IHealthExecutionState.cs"] = "a7d45d27a59f504900daa273f51cb780e222cf5e086a5f26cde9efdfbefd5c27",
        ["Internal/Contracts/IHealthCollectionState.cs"] = "1005bdbf6820b5ceb01edd8ee886e471686ab61ead85c6f126effad46d158b2c"
    };

    internal static async Task<int> RunAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["language-root", "package", "provenance", "reports", "output"], []);
        string language = input.PathOf("language-root"), package = input.PathOf("package"), provenance = input.PathOf("provenance");
        string reports = input.PathOf("reports"), output = input.PathOf("output");
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Use a new or empty coverage analysis output directory.");
        Directory.CreateDirectory(output);
        var pins = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var result = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["complete"] = false,
            ["qualified"] = false,
            ["scope"] = "Exact Healthcheck subtree of complete native Core coverage; one actual platform lane; no blanket release qualification",
            ["aggregation"] = "Fixed native source/class/line identity; line hits OR; branch covered counts MAX as a conservative lower bound, never anonymous-branch summation",
            ["minimumLinePercent"] = 95,
            ["minimumBranchPercent"] = 90,
            ["platform"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["analyzerRuntime"] = RuntimeInformation.FrameworkDescription,
            ["verifierSha256"] = Hash(typeof(HealthcheckCoverageVerification).Assembly.Location)
        };
        int exit = 1;
        HashSet<string>? sourcePaths = null;
        JsonObject? producerInputs = null;
        try
        {
            Require(RuntimeInformation.ProcessArchitecture == Architecture.X64 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()), "Coverage analysis requires its actual supported x64 platform lane.");
            string packageHash = Pin(package, pins), provenanceHash = Pin(provenance, pins);
            string coreHash = PackageCoreHash(package);
            JsonObject producer = Read(provenance);
            producerInputs = producer;
            if ((string?)producer["schema"] == HealthcheckPackageVerification.PreparedInputSchema) { HealthcheckPackageVerification.ValidatePreparedOutput(language, output); }
            HealthcheckPackageVerification.ValidatePreparedProducer(producer, language, package);
            Require((string?)producer["packageSha256"] == packageHash && (string?)producer["coreSha256"] == coreHash, "Coverage producer provenance identifies different package/Core bytes.");
            Dictionary<string, Source> sources = SourceInventory(language, producer, pins);
            sourcePaths = sources.Keys.ToHashSet(StringComparer.Ordinal);
            string canonicalCore = Path.Combine(language, "src", "CIS.Core", "bin", "Release", "net10.0", "CIS.Core.dll");
            Require(Pin(canonicalCore, pins) == coreHash, "Canonical Core output differs from the exact candidate package.");
            string collectionPath = Path.Combine(reports, "collection.json");
            string collectionHash = Pin(collectionPath, pins); JsonObject collection = Read(collectionPath);
            ValidateCollectionCompletion(collection);
            bool shipping = ValidateCollectionProfile(collection);
            ValidateProducerProfile(shipping, producer);
            Require((string?)collection["collectorVersion"] == "18.10.1" && (string?)collection["packageSha256"] == packageHash
                && (string?)collection["coreSha256"] == coreHash, "Native collection metadata differs from exact tool/package/Core pins.");
            string toolPath = ValidateCollectorReference(collection, reports, pins), toolHash = Pin(toolPath, pins);
            JsonObject settings = collection["settings"]?.AsObject() ?? throw new InvalidDataException("Missing actual coverage settings pin.");
            string settingsPath = Path.GetFullPath((string)settings["path"]!, reports), settingsHash = Pin(settingsPath, pins);
            Require((string?)settings["sha256"] == settingsHash, "Collection settings checksum differs.");
            ValidateSettings(ReadXml(settingsPath), shipping);
            result["candidate"] = new JsonObject
            {
                ["packageSha256"] = packageHash,
                ["coreSha256"] = coreHash,
                ["producerProvenanceSha256"] = provenanceHash,
                ["collectionSha256"] = collectionHash,
                ["collectorPackageSha256"] = toolHash,
                ["settingsSha256"] = settingsHash,
                ["originScope"] = "Supplied producer pins, canonical/copy-local Core bytes, native PDB source paths and retained no-build collection receipt"
            };
            JsonArray families = collection["families"]!.AsArray();
            if (shipping)
            {
                JsonObject observation = await AnalyzeShippingCollection(language, package, reports, collection, sources, coreHash, pins);
                foreach ((string key, JsonNode? value) in observation) { result[key] = value?.DeepClone(); }
            }
            else
            {
                var observations = new List<NativeReport>(); var familyResults = new JsonArray();
                foreach (JsonNode? familyNode in families)
                {
                    JsonObject family = familyNode!.AsObject(); string name = (string)family["name"]!;
                    Require((int?)family["exitCode"] == 0, "Native coverage family process failed: " + name);
                    ValidateCopies(language, name, family["coreCopies"]!.AsArray(), coreHash, pins);
                    string expectedTrx = name + "/report.trx";
                    Require(((string)family["trx"]!).Replace('\\', '/') == expectedTrx, "Native family TRX must use its own retained report path.");
                    string trx = SourceReplay.InputPath(reports, expectedTrx); string trxHash = Pin(trx, pins);
                    (long passed, string nativePath) = ValidateTrx(trx, Path.GetDirectoryName(trx)!);
                    string nativeHash = Pin(nativePath, pins);
                    NativeReport native = ParseCoverage(ReadXml(nativePath), language, sources); observations.Add(native);
                    familyResults.Add(new JsonObject
                    {
                        ["family"] = name,
                        ["testsPassed"] = passed,
                        ["trx"] = trx,
                        ["trxSha256"] = trxHash,
                        ["cobertura"] = nativePath,
                        ["coberturaSha256"] = nativeHash,
                        ["completeNativeTotals"] = new JsonObject
                        {
                            ["linesCovered"] = native.LinesCovered,
                            ["linesTotal"] = native.LinesTotal,
                            ["branchesCovered"] = native.BranchesCovered,
                            ["branchesTotal"] = native.BranchesTotal
                        }
                    });
                }
                result["families"] = familyResults;
                JsonObject summary = Summarize(sources, Aggregate(observations));
                foreach ((string key, JsonNode? value) in summary) { result[key] = value?.DeepClone(); }
            }
            result["complete"] = true;
            exit = (bool)result["qualified"]! ? 0 : 2;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or JsonException or XmlException or FormatException or OverflowException or TimeoutException)
        { result["failure"] = error.ToString(); }
        finally
        {
            try
            {
                foreach ((string path, string hash) in pins) { Require(Hash(path) == hash, "Coverage analysis input changed: " + path); }
                if (producerInputs is not null) { HealthcheckPackageVerification.ValidatePreparedProducer(producerInputs, language, package); }
                if (sourcePaths is not null)
                {
                    string[] current = Directory.EnumerateFiles(Path.Combine(language, "src", "CIS.Core"), "*.cs", SearchOption.AllDirectories)
                        .Select(path => Path.GetRelativePath(language, path).Replace('\\', '/'))
                        .Where(path => !path.Split('/').Any(part => part is "bin" or "obj")).ToArray();
                    Require(sourcePaths.SetEquals(current), "Core source filename inventory changed during coverage analysis.");
                }
                result["inputsVerifiedAfter"] = true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or XmlException or FormatException or OverflowException)
            { result["integrityFailure"] = error.ToString(); result["complete"] = false; result["qualified"] = false; exit = 1; }
            result["inputPins"] = JsonSerializer.SerializeToNode(pins); result["exitCode"] = exit;
            WriteJson(Path.Combine(output, "results.json"), result);
        }
        Console.WriteLine(new JsonObject { ["complete"] = result["complete"]!.DeepClone(), ["qualified"] = result["qualified"]!.DeepClone(), ["aggregate"] = result["aggregate"]?.DeepClone(), ["exitCode"] = exit }.ToJsonString());
        return exit;
    }

    private static Dictionary<string, Source> SourceInventory(string language, JsonObject producer, Dictionary<string, string> pins)
    {
        var producerPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? item in producer["sourceInputs"]!.AsArray())
        {
            string relative = (string)item!["path"]!; Require(producerPaths.Add(relative), "Duplicate producer source pin.");
            string path = SourceReplay.InputPath(language, relative), hash = Pin(path, pins);
            Require((string?)item["sha256"] == hash, "Producer source pin differs: " + relative);
        }
        var sources = new Dictionary<string, Source>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(language, "src", "CIS.Core"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(language, path).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is "bin" or "obj")) { continue; }
            Require(producerPaths.Contains(relative), "Producer provenance omits actual Core C# source: " + relative);
            string hash = Pin(path, pins); bool healthcheck = relative.StartsWith(HealthcheckPrefix, StringComparison.Ordinal); string? reason = null;
            if (healthcheck && DeclarationOnly.ContainsKey(relative[HealthcheckPrefix.Length..]))
            {
                string normalized = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
                ValidateDeclarationOnly(relative, normalized);
                reason = "Reviewed declaration-only enum/interface file; normalized source hash pinned; no executable code excluded";
            }
            sources.Add(relative, new Source(relative, hash, healthcheck, reason));
        }
        string[] structural = ["src/CIS.Core/CIS.Core.csproj", "global.json", "Directory.Build.props", "Directory.Packages.props"];
        Require(structural.All(producerPaths.Contains), "Producer provenance omits structural Core build inputs.");
        Require(DeclarationOnly.Keys.All(relative => sources.ContainsKey(HealthcheckPrefix + relative)), "A reviewed declaration-only Healthcheck source is missing.");
        return sources;
    }

    internal static void ValidateDeclarationOnly(string languageRelativePath, string text)
    {
        Require(languageRelativePath.StartsWith(HealthcheckPrefix, StringComparison.Ordinal)
            && DeclarationOnly.TryGetValue(languageRelativePath[HealthcheckPrefix.Length..], out string? expected)
            && Digest(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))) == expected,
            "Unknown or changed reviewed Healthcheck declaration-only source: " + languageRelativePath);
    }

    internal static void ValidateSettings(XDocument document, bool shipping = false)
    {
        XElement[] collectors = document.Descendants("DataCollector").ToArray();
        Require(collectors.Length == 1 && (string?)collectors[0].Attribute("friendlyName") == "Code Coverage"
            && (string?)collectors[0].Attribute("uri") == "datacollector://Microsoft/CodeCoverage/2.0", "Expected the native Microsoft coverage collector only.");
        XElement config = collectors[0].Element("Configuration") ?? throw new InvalidDataException("Missing collector configuration.");
        Require(config.Elements().Select(element => element.Name).Distinct().Count() == config.Elements().Count(), "Duplicate collector settings cannot override the validated configuration.");
        Require(config.Element("Format")?.Value == "cobertura" && config.Element("IncludeTestAssembly")?.Value.Equals("false", StringComparison.OrdinalIgnoreCase) == true
            && config.Element("ExcludeAssembliesWithoutSources")?.Value == "None", "Native format or complete-module source settings differ.");
        XElement coverage = config.Element("CodeCoverage") ?? throw new InvalidDataException("Missing CodeCoverage settings.");
        Require(coverage.Elements().Select(element => element.Name).Distinct().Count() == coverage.Elements().Count(), "Duplicate coverage settings cannot override the validated configuration.");
        XElement modules = coverage.Element("ModulePaths") ?? throw new InvalidDataException("Missing full Core module inclusion.");
        string[] expectedModules = shipping ? [@".*[\\/]CIS\.Core\.dll$", @".*[\\/]CIS\.TypeManager\.Build\.dll$"] : [@".*[\\/]CIS\.Core\.dll$"];
        string[] actualModules = modules.Element("Include")?.Elements("ModulePath").Select(element => element.Value).ToArray() ?? [];
        Require((string?)modules.Attribute("mergeDefaults") == "False" && actualModules.Length == expectedModules.Length
            && actualModules.Order(StringComparer.Ordinal).SequenceEqual(expectedModules.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            && modules.Element("Exclude") is { } moduleExclusions && !moduleExclusions.HasElements && string.IsNullOrWhiteSpace(moduleExclusions.Value), "Only the complete Core module may be selected.");
        foreach (string name in new[] { "Sources", "Functions", "Attributes", "CompanyNames", "PublicKeyTokens" })
        {
            XElement element = coverage.Element(name) ?? throw new InvalidDataException("Missing explicit exclusion policy: " + name);
            Require((string?)element.Attribute("mergeDefaults") == "False" && element.Elements().All(child => child.Name == "Exclude" && !child.HasElements && string.IsNullOrWhiteSpace(child.Value))
                && string.IsNullOrWhiteSpace(element.Value), "Executable source/function/attribute or default exclusion is forbidden: " + name);
        }
        Require(coverage.Element("SkipAutoProperties")?.Value.Equals("false", StringComparison.OrdinalIgnoreCase) == true
            && coverage.Element("CollectFromChildProcesses")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true
            && coverage.Element("EnableDynamicManagedInstrumentation")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            "Auto-properties, actual managed instrumentation and child processes must be included.");
    }

    private static void ValidateCopies(string language, string family, JsonArray copies, string coreHash, Dictionary<string, string> pins)
    {
        string suffix = family switch { "unit" => "UnitTests", "integration" => "IntegrationTests", "business" => "BusinessCases", _ => throw new InvalidDataException("Unknown native family.") };
        string directory = Path.Combine(language, "tests", "CIS.Core." + suffix, "bin", "Release", "net10.0");
        var expected = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal) { Path.Combine(directory, "CIS.Core.dll") };
        if (family == "integration") { expected.Add(Path.Combine(directory, "healthcheck-http-host", "CIS.Core.dll")); expected.Add(Path.Combine(directory, "healthcheck-probe", "CIS.Core.dll")); }
        if (family == "business") { expected.Add(Path.Combine(directory, "healthcheck-country-list", "CIS.Core.dll")); }
        var observed = new HashSet<string>(expected.Comparer);
        foreach (JsonNode? copy in copies)
        {
            string path = Path.GetFullPath((string)copy!["path"]!, language);
            Require(observed.Add(path) && expected.Contains(path), "Missing, duplicate or unexpected native host/child Core location.");
            Require((string?)copy["sha256"] == coreHash && Pin(path, pins) == coreHash, "Native host/child Core bytes differ from candidate.");
        }
        Require(expected.SetEquals(observed), "Missing exact Core copy for a native family/child process.");
    }

    internal static void ValidateCollectionCompletion(JsonObject collection)
    {
        // Older native receipts predate this field; an explicit completion claim must be true.
        if (!collection.TryGetPropertyValue("complete", out JsonNode? status)) { return; }
        Require(status is JsonValue value && value.TryGetValue<bool>(out bool complete) && complete,
            "Native collection completion must be JSON Boolean true when present.");
    }

    internal static (long Passed, string CoveragePath) ValidateTrx(string path, string directory)
    {
        XDocument xml = ReadXml(path);
        XElement[] counters = xml.Descendants().Where(element => element.Name.LocalName == "Counters").ToArray();
        Require(counters.Length == 1 && Number(counters[0], "total") > 0 && Number(counters[0], "total") == Number(counters[0], "passed"), "Missing, failed or skipped native test family.");
        XElement[] results = xml.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").ToArray();
        Require(results.LongLength == Number(counters[0], "total") && results.All(element => Required(element, "outcome") == "Passed"), "Native TRX counters do not match every actual passed test result.");
        string[] attachments = xml.Descendants().Where(element => element.Name.LocalName == "A").Select(element => (string?)element.Attribute("href"))
            .OfType<string>().Where(value => value.EndsWith(".cobertura.xml", StringComparison.Ordinal)).ToArray();
        Require(attachments.Length == 1, "Expected one actual Microsoft Cobertura attachment.");
        string filename = attachments[0].Replace('\\', '/').Split('/')[^1];
        string[] files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(file => Path.GetFileName(file) == filename).Order(StringComparer.Ordinal).ToArray();
        Require(files.Length > 0 && files.Select(Hash).Distinct(StringComparer.Ordinal).Count() == 1, "Native Cobertura attachment is missing or conflicting.");
        return (Number(counters[0], "passed"), files[0]);
    }

    private static string PackageCoreHash(string path)
    {
        using ZipArchive package = ZipFile.OpenRead(path);
        XElement metadata = PackageMetadata(package);
        Require(metadata.Elements().Single(element => element.Name.LocalName == "id").Value == "CIS.Core", "Expected CIS.Core package identity.");
        ZipArchiveEntry[] entries = package.Entries.Where(entry => entry.FullName == "lib/net10.0/CIS.Core.dll").ToArray();
        Require(entries.Length == 1, "Expected exactly one net10.0 Core package DLL.");
        using Stream stream = entries[0].Open(); return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    internal static void ValidateCollectorPackage(string path)
    {
        using ZipArchive package = ZipFile.OpenRead(path); XElement metadata = PackageMetadata(package);
        Require(metadata.Elements().Single(element => element.Name.LocalName == "id").Value.Equals("Microsoft.CodeCoverage", StringComparison.OrdinalIgnoreCase)
            && metadata.Elements().Single(element => element.Name.LocalName == "version").Value == "18.10.1", "Collector package identity/version differs.");
    }
    internal static string ValidateCollectorReference(JsonObject collection, string reports, Dictionary<string, string> pins)
    {
        JsonObject tool = collection["collectorPackage"]?.AsObject() ?? throw new InvalidDataException("Missing actual collector package pin.");
        Require((string?)tool["path"] is { Length: > 0 } && (string?)tool["sha256"] is { Length: 64 }, "Collector package pin is incomplete.");
        string path = Path.GetFullPath((string)tool["path"]!, reports);
        Require(File.Exists(path), "Pinned collector package is missing.");
        Require(Pin(path, pins) == (string?)tool["sha256"], "Collector package checksum differs.");
        ValidateCollectorPackage(path);
        return path;
    }
    private static XElement PackageMetadata(ZipArchive package)
    {
        ZipArchiveEntry[] specifications = package.Entries.Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).ToArray();
        Require(specifications.Length == 1, "Expected exactly one native package specification.");
        using Stream stream = specifications[0].Open();
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader).Descendants().Single(element => element.Name.LocalName == "metadata");
    }
    private static string Pin(string path, Dictionary<string, string> pins)
    {
        string hash = Hash(path);
        if (pins.TryGetValue(path, out string? previous)) { Require(previous == hash, "Coverage input changed while reading: " + path); }
        else { pins.Add(path, hash); }
        return hash;
    }
}
