using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal sealed partial class HealthcheckPackageVerification
{
    internal const string PreparedInputSchema = "cis.healthcheck.prepared-inputs/v1";
    // Projects already verified preparation facts into the existing package and
    // coverage consumers' input format. It does not run another producer build.
    private static int WritePreparedInputs(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["language-root", "verification-root", "package", "output"], []);
        string language = input.PathOf("language-root"), prepared = input.PathOf("verification-root"),
            package = input.PathOf("package"), output = input.PathOf("output");
        ValidatePreparedOutput(language, output);
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Use an empty prepared-input output directory.");
        JsonObject projection = ReadPreparedInputs(language, prepared, package);
        Directory.CreateDirectory(output);
        string destination = Path.Combine(output, "producer.json");
        using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, projection, new JsonSerializerOptions { WriteIndented = true }); }
        Console.WriteLine("Verified prepared Healthcheck consumer inputs: " + destination);
        return 0;
    }

    internal static void ValidatePreparedOutput(string language, string output)
    {
        string repo = Path.GetFullPath("../..", language);
        string relativeOutput = Path.GetRelativePath(repo, output).Replace('\\', '/');
        Require(Path.IsPathRooted(relativeOutput) || relativeOutput.StartsWith("../", StringComparison.Ordinal)
            || relativeOutput.StartsWith(".artifacts/", StringComparison.Ordinal), "Keep projected evidence outside maintained inputs or under .artifacts.");
    }

    internal static void ValidatePreparedProducer(JsonObject producer, string language, string package)
    {
        string? schema = producer["schema"] is JsonValue value && value.TryGetValue<string>(out string? selected) ? selected : null;
        bool marked = producer.ContainsKey("preparedSummary") || producer.ContainsKey("sourceManifest") || producer.ContainsKey("sourceInputScope")
            || schema?.StartsWith("cis.healthcheck.prepared-inputs/", StringComparison.Ordinal) == true;
        if (!marked) { return; }
        Require(schema == PreparedInputSchema, "Prepared producer markers require the exact current schema; legacy downgrade is forbidden.");
        JsonObject summary = producer["preparedSummary"]?.AsObject() ?? throw new InvalidDataException("Current producer omits its prepared summary pin.");
        Require((string?)summary["path"] is { Length: > 0 } && Path.IsPathFullyQualified((string)summary["path"]!)
            && (string?)summary["sha256"] is { Length: 64 }, "Current producer summary pin is malformed.");
        string path = (string)summary["path"]!;
        Require(Path.GetFileName(path) == "summary.json" && Hash(path) == (string?)summary["sha256"], "Current producer prepared summary changed.");
        JsonObject observed = ReadPreparedInputs(language, Path.GetDirectoryName(path)!, package);
        Require(JsonNode.DeepEquals(producer, observed), "Current producer projection differs from its pinned preparation, package or maintained inputs.");
    }

    private static JsonObject ReadPreparedInputs(string language, string prepared, string package)
    {
        string repo = Path.GetFullPath("../..", language);
        Require(SamePath(language, Path.Combine(repo, "implementations/csharp")), "Use the maintained C# language root.");
        string summaryPath = Path.Combine(prepared, "summary.json"), summaryHash = Hash(summaryPath);
        JsonObject summary = ReadPinned(summaryPath, summaryHash, "Prepared summary changed.");
        Require((int?)summary["exitCode"] == 0 && ((string?)summary["profile"] == "Developer"
            || (string?)summary["profile"] == "Preparation" && (string?)summary["scope"] == "prepared-inputs"),
            "Use successful current Preparation or Developer inputs.");
        Require(summary["issues"] is JsonArray { Count: 0 } && summary["stages"] is JsonArray stages
            && stages.Count > 0 && stages.All(stage => stage is JsonObject item && (string?)item["status"] == "Passed"), "Preparation contains unfinished stages or issues.");
        foreach (string stage in new[] { "build", "pack", "architecture-inspection" })
        { Require(summary["stages"]!.AsArray().Count(item => item is JsonObject value && (string?)value["id"] == stage && (string?)value["status"] == "Passed") == 1, "Missing successful preparation stage: " + stage); }
        Require((string?)summary["source"]?["inputScope"] == SourceInventory.VerificationScope, "Prepared source scope differs from maintained verification inputs.");
        string manifest = PreparedReference(prepared, summary["source"]!["inputInventory"]!);
        string manifestHash = Hash(manifest);
        SourceInventoryEntry[] sources = JsonSerializer.Deserialize<SourceInventoryEntry[]>(File.ReadAllBytes(manifest))
            ?? throw new InvalidDataException("Prepared source manifest is empty.");
        SourceReplay.ValidateInventory(sources);
        CheckSources();
        string selectedPackage = PreparedReference(prepared, summary["candidatePackage"]!);
        Require(SamePath(package, selectedPackage), "The supplied package is not the explicitly prepared candidate.");
        string packageHash = Hash(package);
        string inspectionPath = PreparedReference(prepared, summary["architectureInspection"]!);
        string inspectionHash = Hash(inspectionPath);
        JsonObject inspection = ReadPinned(inspectionPath, inspectionHash, "Prepared inspection changed.");
        Require((string?)inspection["schema"] == "cis.architecture-inspection/v1"
            && (int?)inspection["allowlists"]?["violations"] == 0 && (int?)inspection["ownership"]?["violations"] == 0,
            "Prepared architecture inspection did not pass its actual policies.");
        JsonObject inspectedPackage = inspection["package"]?.AsObject() ?? throw new InvalidDataException("Prepared inspection omits its package pin.");
        Require((string?)inspectedPackage["path"] is { Length: > 0 } && SamePath((string)inspectedPackage["path"]!, package) && (string?)inspectedPackage["sha256"] == packageHash,
            "Prepared inspection identifies another package.");
        string core = Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"), coreHash;
        using (ZipArchive archive = ZipFile.OpenRead(package))
        {
            ZipArchiveEntry[] entries = archive.Entries.Where(entry => entry.FullName == "lib/net10.0/CIS.Core.dll").ToArray();
            Require(entries.Length == 1, "The prepared package must contain exactly one Core DLL.");
            using Stream stream = entries[0].Open(); coreHash = Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        JsonArray assemblies = inspection["assemblies"]?.AsArray() ?? throw new InvalidDataException("Prepared inspection omits its assembly facts.");
        JsonObject[] observedCore = assemblies.OfType<JsonObject>()
            .Where(assembly => (string?)assembly["project"] == "src/CIS.Core/CIS.Core.csproj").ToArray();
        Require(observedCore.Length == 1 && (string?)observedCore[0]["facts"]?["sha256"] == coreHash && Hash(core) == coreHash,
            "Current Release Core, packed Core and prepared inspection bytes differ.");
        const string prefix = "implementations/csharp/";
        string[] structural = ["src/CIS.Core/CIS.Core.csproj", "global.json", "Directory.Build.props", "Directory.Packages.props"];
        SourceInventoryEntry[] projected = sources.Where(pin => pin.Path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(pin => new SourceInventoryEntry(pin.Path[prefix.Length..], pin.Sha256))
            .Where(pin => pin.Path.StartsWith("src/CIS.Core/", StringComparison.Ordinal) && pin.Path.EndsWith(".cs", StringComparison.Ordinal)
                || structural.Contains(pin.Path, StringComparer.Ordinal)).OrderBy(pin => pin.Path, StringComparer.Ordinal).ToArray();
        string[] expected = Directory.EnumerateFiles(Path.Combine(language, "src/CIS.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(language, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => Path.GetRelativePath(language, path).Replace('\\', '/')).Concat(structural).Order(StringComparer.Ordinal).ToArray();
        Require(projected.Select(pin => pin.Path).SequenceEqual(expected, StringComparer.Ordinal), "Prepared manifest omits required Core source or structural inputs.");
        CheckSources();
        Require(Hash(summaryPath) == summaryHash && Hash(manifest) == manifestHash && Hash(inspectionPath) == inspectionHash
            && Hash(package) == packageHash && Hash(core) == coreHash, "Prepared inputs changed during projection.");
        return JsonSerializer.SerializeToNode(new
        {
            schema = PreparedInputSchema,
            packageSha256 = packageHash,
            coreSha256 = coreHash,
            sourceInputs = projected,
            preparedSummary = new { path = summaryPath, sha256 = summaryHash },
            sourceManifest = new { path = manifest, sha256 = manifestHash },
            architectureInspection = new { path = inspectionPath, sha256 = inspectionHash },
            sourceInputScope = SourceInventory.VerificationScope,
            scope = "Projection of verified current preparation facts; no additional build or native tests. The supplied source/package pins do not independently prove the producer process or full toolchain.",
            testsExecuted = false,
            releaseQualified = false
        })!.AsObject();

        void CheckSources()
        {
            Require(SourceInventory.VerificationPaths(repo).SequenceEqual(sources.Select(pin => pin.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal),
                "Maintained input membership changed after preparation.");
            SourceReplay.AssertInputs(repo, sources);
        }
    }

    private static string PreparedReference(string root, JsonNode? reference)
    {
        Require(reference is JsonObject, "Prepared artifact pin is missing or malformed.");
        string relative = (string?)reference!["path"] ?? throw new InvalidDataException("Prepared artifact path is missing.");
        string path = SourceReplay.InputPath(root, relative);
        Require(SamePath(path, CompilerChecks.VerifiedReference(root, reference!)), "Prepared artifact path differs from its contained pin.");
        return path;
    }
}
