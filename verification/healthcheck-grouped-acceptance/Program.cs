using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Recovery.HealthcheckAcceptance;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            Require(args.Length > 0, "Use receive, transport-prerequisites or self-controls.");
            var input = Arguments(args[1..]);
            if (args[0] == "self-controls") { return SelfControls.Run(input); }
            Require(OperatingSystem.IsLinux() && RuntimeInformation.FrameworkDescription == ".NET 10.0.12",
                "The analysis consumer runs on its declared Linux subject runtime10.0.12.");
            var frozen = new FrozenNativeEvidence(Take(input, "checker"));
            string sourceRoot = Take(input, "consumer-source"), build = Take(input, "consumer-build");
            JsonObject consumer = ConsumerIdentity.Validate(sourceRoot, build);
            if (args[0] == "transport-prerequisites")
            {
                string candidate = Take(input, "base-candidate"), windows = Take(input, "windows-proof"), linux = Take(input, "linux-proof"), policy = Take(input, "policy");
                string output = Take(input, "output"); Require(input.Count == 0, "Unknown argument.");
                JsonObject result = TransportPrerequisites.ValidateCore(frozen, candidate, windows, linux, policy);
                result["consumer"] = consumer;
                ConsumerIdentity.Postflight(consumer); frozen.Postflight();
                WriteFresh(output, result);
                return 0;
            }
            Require(args[0] == "receive", "Unknown analysis command.");
            string manifest = Take(input, "manifest"), policyPath = Take(input, "policy"), outputRoot = Take(input, "output");
            Require(input.Count == 0, "Unknown argument.");
            JsonObject observation = await CompositeAcceptance.ReceiveAsync(frozen, manifest, policyPath, outputRoot, consumer);
            Console.WriteLine(observation.ToJsonString(Json));
            return (bool?)observation["qualified"] == true ? 0 : 1;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or System.Reflection.TargetInvocationException)
        {
            Console.Error.WriteLine(error.ToString());
            return 2;
        }
    }

    internal static Dictionary<string, string> Arguments(string[] args)
    {
        Require(args.Length % 2 == 0, "Use explicit named argument/value pairs.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        { Require(args[i].StartsWith("--", StringComparison.Ordinal) && values.TryAdd(args[i][2..], args[i + 1]), "Invalid/duplicate argument."); }
        return values;
    }
    internal static string Take(Dictionary<string, string> values, string name)
    {
        Require(values.Remove(name, out string? value) && value is not null && Path.IsPathFullyQualified(value), "Missing absolute input: " + name);
        return Path.GetFullPath(value!);
    }
    internal static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    internal static void WriteFresh(string path, JsonObject result)
    {
        Require(!File.Exists(path), "Preserve an existing analysis result.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(result.ToJsonString(Json) + "\n"); file.Write(bytes); file.Flush(true);
    }
}

internal static class ConsumerIdentity
{
    internal static readonly string[] Sources = ["HealthcheckGroupedAcceptance.csproj", "Program.cs", "FrozenNativeEvidence.cs", "CompositeAcceptance.cs", "SelfControls.cs", "policy.json", "README.md"];

    internal static JsonObject Validate(string root, string receiptPath)
    {
        JsonObject receipt = FrozenNativeEvidence.Read(receiptPath);
        Program.Require((string?)receipt["schema"] == "cis.healthcheck.analysis-consumer-build/v1" && (int?)receipt["exitCode"] == 0
            && (bool?)receipt["outputDrained"] == true && (string?)receipt["sdk"] == "10.0.401"
            && (string?)receipt["compilerPlatform"] == "windows-x64" && (string?)receipt["actualCompileHostRuntime"] == "10.0.12"
            && (bool?)receipt["privateRuntime10_0_12Observed"] == true && (bool?)receipt["timedOut"] == false && (bool?)receipt["capacityHold"] == false
            && receipt["arguments"] is JsonArray arguments && arguments.Any(value => (string?)value is "build" or "-target:Build")
            && arguments.Any(value => Leaf((string)value!) == "HealthcheckGroupedAcceptance.csproj"), "Require the actual successful isolated consumer build receipt, not an approval flag.");
        JsonArray sourcePins = receipt["sourcePins"]!.AsArray();
        Program.Require(sourcePins.Select(pin => (string?)pin!["path"]).Order(StringComparer.Ordinal).SequenceEqual(Sources.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Consumer source/project/policy/README membership differs.");
        foreach (JsonNode? pin in sourcePins)
        { Program.Require(FrozenNativeEvidence.Hash(Path.Combine(root, (string)pin!["path"]!)) == (string?)pin["sha256"], "Consumer source changed since compilation."); }
        JsonArray compiler = receipt["compilerPins"]!.AsArray();
        foreach (string name in new[] { "MSBuild.dll", "csc.dll", "Microsoft.CodeAnalysis.CSharp.dll" })
        { Program.Require(compiler.Any(pin => Leaf((string)pin!["path"]!) == name), "Consumer compiler closure omits " + name); }
        // Original compiler paths identify the actual producing host; they are not fictional live Linux paths.
        foreach (JsonNode? pin in compiler)
        { Program.Require(IsSha((string?)pin!["sha256"]) && !string.IsNullOrWhiteSpace((string?)pin["path"]), "Actual original compiler provenance is missing."); }
        Program.Require(receipt["runtimePins"] is JsonArray { Count: > 0 } runtime && runtime.All(pin => IsSha((string?)pin!["sha256"]))
            && (string?)receipt["foreignToolPinSemantics"] == "Observed actual Windows producer bytes; not live-rehashed from Linux", "Original Windows runtime/compiler pins need their explicit portable provenance meaning.");
        string self = typeof(Program).Assembly.Location;
        JsonArray outputs = receipt["portableOutputPins"]!.AsArray();
        Program.Require(outputs.Count == 4, "Retain exactly DLL/deps/runtimeconfig/PDB consumer outputs.");
        foreach (string file in new[] { self, Path.ChangeExtension(self, ".deps.json"), Path.ChangeExtension(self, ".runtimeconfig.json"), Path.ChangeExtension(self, ".pdb") })
        { Program.Require(outputs.Count(pin => (string?)pin!["name"] == Path.GetFileName(file) && (string?)pin["sha256"] == FrozenNativeEvidence.Hash(file)
            && (long?)pin["bytes"] == new FileInfo(file).Length) == 1, "The relocated consumer differs from its actual compiled outputs."); }
        ValidatePdb(self, root, ["Program.cs", "FrozenNativeEvidence.cs", "CompositeAcceptance.cs", "SelfControls.cs"]);
        string core = typeof(object).Assembly.Location;
        return new JsonObject
        {
            ["schema"] = "cis.healthcheck.analysis-consumer-identity/v1", ["sourceRoot"] = root,
            ["buildReceipt"] = receiptPath, ["buildReceiptSha256"] = FrozenNativeEvidence.Hash(receiptPath),
            ["sourcePins"] = sourcePins.DeepClone(), ["compilerPins"] = compiler.DeepClone(), ["runtimePins"] = receipt["runtimePins"]!.DeepClone(), ["portableOutputPins"] = outputs.DeepClone(),
            ["originalBuildObservation"] = receipt.DeepClone(),
            ["actualFramework"] = RuntimeInformation.FrameworkDescription, ["actualCoreLib"] = core,
            ["actualCoreLibSha256"] = FrozenNativeEvidence.Hash(core), ["actualDotnetHost"] = Environment.ProcessPath,
            ["actualDotnetHostSha256"] = FrozenNativeEvidence.Hash(Environment.ProcessPath!), ["nativeExecutorIsThisConsumer"] = false
        };
    }

    internal static void Postflight(JsonObject identity)
    {
        string root = (string)identity["sourceRoot"]!;
        foreach (JsonNode? pin in identity["sourcePins"]!.AsArray())
        { Program.Require(FrozenNativeEvidence.Hash(Path.Combine(root, (string)pin!["path"]!)) == (string?)pin["sha256"], "Consumer source changed during analysis."); }
        foreach (JsonNode? pin in identity["portableOutputPins"]!.AsArray())
        { string file = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, (string)pin!["name"]!);
            Program.Require(FrozenNativeEvidence.Hash(file) == (string?)pin["sha256"], "Relocated consumer output changed during analysis."); }
        Program.Require(FrozenNativeEvidence.Hash((string)identity["buildReceipt"]!) == (string?)identity["buildReceiptSha256"]
            && FrozenNativeEvidence.Hash((string)identity["actualCoreLib"]!) == (string?)identity["actualCoreLibSha256"]
            && FrozenNativeEvidence.Hash((string)identity["actualDotnetHost"]!) == (string?)identity["actualDotnetHostSha256"], "Consumer build/runtime identity changed during analysis.");
    }

    internal static string Leaf(string path) => path.Replace('\\', '/').Split('/')[^1];
    internal static bool IsSha(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static void ValidatePdb(string assembly, string sourceRoot, string[] maintainedSources)
    {
        using var peStream = File.OpenRead(assembly); using var pe = new PEReader(peStream);
        var entries = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
        Program.Require(entries.Length == 1, "The actual consumer PE has no unique portable PDB identity.");
        CodeViewDebugDirectoryData codeView = pe.ReadCodeViewDebugDirectoryData(entries[0]);
        using var stream = File.OpenRead(Path.ChangeExtension(assembly, ".pdb"));
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream); MetadataReader reader = provider.GetMetadataReader();
        Program.Require(reader.DebugMetadataHeader is not null, "The actual portable PDB metadata header is absent.");
        byte[] id = reader.DebugMetadataHeader!.Id.ToArray();
        Program.Require(id.Length == 20 && codeView.Age == 1 && new Guid(id.AsSpan(0, 16)) == codeView.Guid
            && BitConverter.ToUInt32(id, 16) == entries[0].Stamp, "The actual DLL and portable PDB are unrelated.");
        foreach (string source in maintainedSources)
        {
            Document[] matches = reader.Documents.Select(handle => reader.GetDocument(handle)).Where(document => Leaf(reader.GetString(document.Name)) == source).ToArray();
            Program.Require(matches.Length == 1 && reader.GetGuid(matches[0].HashAlgorithm) == new Guid("8829d00f-11b8-4213-878b-770e8597ac16")
                && reader.GetBlobBytes(matches[0].Hash).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(sourceRoot, source)))),
                "The actual PDB does not bind a maintained compiled source checksum: " + source);
        }
    }
}
