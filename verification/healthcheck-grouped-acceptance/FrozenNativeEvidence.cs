using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Recovery.HealthcheckAcceptance.Program;

namespace CIS.Recovery.HealthcheckAcceptance;

internal sealed class FrozenNativeEvidence
{
    internal const string CheckerSha = "0bbadb285a453a098fb33a6635b8f5cefe4f45368ab40509e557d267689d7fbe";
    internal const string HelperSha = "a84ca81bf306eae633e42c679ea62445b3fd2cd5a1cc585640049e0059305154";
    internal const string Language = "implementations/csharp/";
    private readonly Type grouped;
    private readonly Assembly checker;
    internal string CheckerPath { get; }
    internal List<Pin> Observed { get; } = [];
    internal sealed record Pin(string Path, string Sha256);
    internal sealed record GroupEvidence(string Name, string CandidateRoot, object Candidate, object Group, object Tools,
        JsonObject Run, JsonObject Report, Array Definitions, string[] Sources, JsonObject Provenance);

    internal FrozenNativeEvidence(string path)
    {
        CheckerPath = Path.GetFullPath(path);
        Require(Hash(CheckerPath) == CheckerSha && Hash(Path.Combine(Path.GetDirectoryName(path)!, "CIS.Core.TestSupport.dll")) == HelperSha, "Use the original tested native executor0bb/a84; this consumer has a separate identity.");
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(path)!, "CIS.Core.TestSupport.dll"));
        checker = AssemblyLoadContext.Default.LoadFromAssemblyPath(CheckerPath);
        grouped = checker.GetType("CIS.Verification.HealthcheckGroupedMutation", true)!;
        foreach ((string name, int count) in new[] { ("CandidateAt", 1), ("CommonInputs", 1), ("ReadToolchain", 2), ("Definitions", 3), ("Assess", 7), ("ValidateExecution", 1) })
        { _ = Method(name, count); }
        PinFile(CheckerPath); PinFile(Path.Combine(Path.GetDirectoryName(path)!, "CIS.Core.TestSupport.dll"));
    }
    internal object? Call(string name, params object?[] args)
    {
        try { return Method(name, args.Length).Invoke(null, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    internal MethodInfo Method(string name, int count) => grouped.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(method => method.Name == name && method.GetParameters().Length == count);
    internal Type Nested(string name) => grouped.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic) ?? throw new InvalidDataException("Frozen native schema type missing: " + name);
    internal object Decode(string name, JsonNode node) => JsonSerializer.Deserialize(node.ToJsonString(), Nested(name), Json)!;
    internal Array DecodeArray(string name, JsonNode node) => (Array)JsonSerializer.Deserialize(node.ToJsonString(), Nested(name).MakeArrayType(), Json)!;
    internal Array ArrayOf(string name, IEnumerable<object> values)
    {
        object[] items = values.ToArray(); Array array = Array.CreateInstance(Nested(name), items.Length);
        for (int i = 0; i < items.Length; i++) { array.SetValue(items[i], i); }
        return array;
    }
    internal static T Property<T>(object value, string name) => (T)(value.GetType().GetProperty(name)!.GetValue(value) ?? throw new InvalidDataException("Missing native property: " + name));
    internal static JsonNode Node(object value) => JsonSerializer.SerializeToNode(value, value.GetType(), Json)!;
    internal static string Hash(string path) { using Stream source = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(source)); }
    internal static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    internal JsonObject ReadPinned(string path) { string hash = Hash(path); JsonObject result = Read(path); Require(Hash(path) == hash, "Input changed while reading."); Observed.Add(new(path, hash)); return result; }
    internal void PinFile(string path) => Observed.Add(new(path, Hash(path)));
    internal void Postflight() { foreach (Pin pin in Observed) { Require(Hash(pin.Path) == pin.Sha256, "A native/analysis input changed during receiving: " + pin.Path); } }
    internal string Common(object candidate) => (string)Call("CommonInputs", candidate)!;
    internal string CommonWithNewLine(object candidate, string newLine)
    {
        Require(newLine is "\n" or "\r\n", "Only the original declared JSON line endings are supported.");
        var options = new JsonSerializerOptions((JsonSerializerOptions)grouped.GetField("Json", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!) { NewLine = newLine };
        object inputs = Property<object>(candidate, "Inputs");
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(inputs, inputs.GetType(), options)));
    }

    internal JsonObject ValidatePortableWindowsHeader(JsonObject proof, object foreign, object anchor)
    {
        string platform = (string?)proof["platform"] ?? "";
        Require((string?)proof["schema"] == "cis.healthcheck.platform-native-proof/v1" && (string?)proof["group"] == "pipes"
            && platform.StartsWith("Microsoft Windows ", StringComparison.Ordinal) && platform.EndsWith("; X64; .NET 10.0.12", StringComparison.Ordinal)
            && (string?)proof["executionIdentity"]?["operatingSystem"] == "windows"
            && !string.IsNullOrWhiteSpace((string?)proof["executionIdentity"]?["userSid"]) && !string.IsNullOrWhiteSpace((string?)proof["executionIdentity"]?["ownerSid"]),
            "Platform proof is not the original Windows x64/runtime/SID native context.");
        Require((string?)proof["producerSha256"] == CheckerSha && (string?)proof["helperSha256"] == HelperSha,
            "Portable evidence must retain the original frozen native producer/helper bytes.");
        Require((string?)proof["originCandidateIdentity"] == Property<string>(foreign, "Identity") && platform == Property<string>(foreign, "Platform")
            && (string?)proof["host"] == Property<string>(foreign, "Host"), "Original proof/candidate platform identity differs.");
        object[] original = ((IEnumerable)Property<object>(foreign, "Inputs")).Cast<object>().ToArray();
        object[] current = ((IEnumerable)Property<object>(anchor, "Inputs")).Cast<object>().ToArray();
        Require(original.SequenceEqual(current), "Platform production/test/build/config input paths, SHA values or original order differ.");
        string producerDigest = CommonWithNewLine(foreign, "\r\n"), consumerDigest = CommonWithNewLine(anchor, "\n");
        Require((string?)proof["commonInputsSha256"] == producerDigest, "Original Windows common-input digest does not match its exact typed CRLF producer serialization.");
        return new JsonObject
        {
            ["originalProducerCommonInputsSha256"] = proof["commonInputsSha256"]!.DeepClone(), ["originalProducerJsonNewLine"] = "CRLF",
            ["consumerCommonInputsSha256"] = consumerDigest, ["consumerJsonNewLine"] = "LF", ["inputCount"] = original.Length,
            ["exactOrderedInputTuplesEqual"] = true, ["originalProofDigestRewritten"] = false
        };
    }
    internal object Candidate(string root)
    {
        PinFile(Path.Combine(root, "candidate.json")); PinFile(Path.Combine(root, "candidate.zip"));
        object candidate = Call("CandidateAt", root)!;
        Call("VerifyPlatformArchive", Path.Combine(root, "candidate.zip"), Property<object>(candidate, "Inputs"));
        return candidate;
    }
    internal object Group(object candidate, string name) => ((IEnumerable)Call("Groups", candidate)!).Cast<object>().Single(value => Property<string>(value, "Name") == name);
    internal string Source(string root, object candidate, string relative) => (string)Call("CandidateSource", root, candidate, CanonicalSourcePath(relative))!;
    internal static string CanonicalSourcePath(string relative)
    {
        string path = relative.Replace('\\', '/');
        Require(path.StartsWith(Language, StringComparison.Ordinal) && !path.StartsWith('/') && !path.Contains(':') && !path.Any(char.IsControl),
            "Reported source must be a relative path within implementations/csharp.");
        var parts = new List<string>();
        foreach (string part in path.Split('/'))
        {
            if (part is "" or ".") { continue; }
            if (part == "..")
            {
                Require(parts.Count > 2, "Reported source escaped implementations/csharp.");
                parts.RemoveAt(parts.Count - 1);
            }
            else { parts.Add(part); }
        }
        Require(parts.Count > 2, "Reported source does not identify a captured input.");
        return string.Join('/', parts);
    }

    internal GroupEvidence Local(string root, string name, object anchor)
    {
        object candidate = Candidate(root); Require(Common(candidate) == Common(anchor), "Native source/test/build/config membership or bytes differ across origins.");
        Call("VerifyInputs", Path.Combine(root, "workspace"), candidate); Call("ValidatePilot", root, candidate);
        object tools = Call("ReadToolchain", root, candidate)!; PinFile(Path.Combine(root, "toolchain.json"));
        foreach (JsonNode? pin in Node(tools)["files"]!.AsArray()) { PinFile((string)pin!["path"]!); }
        object group = Group(candidate, name); string[] sources = Property<string[]>(group, "Sources");
        string directory = Path.Combine(root, "groups", name); JsonObject run = ReadPinned(Path.Combine(directory, "run.json"));
        Require((string?)run["candidateIdentity"] == Property<string>(candidate, "Identity") && (string?)run["group"] == name
            && (string?)run["platform"] == Property<string>(candidate, "Platform") && (string?)run["host"] == Property<string>(candidate, "Host")
            && (string?)run["executionIdentity"]?["operatingSystem"] == "linux", "The selected native Linux group provenance differs.");
        Call("ValidateExecution", run); Call("ValidateContext", run, group); Call("ValidateSdkMsBuildEnvironment", run, tools);
        string profile = Path.Combine(root, "profiles", name + ".json"); JsonObject config = ReadPinned(profile);
        Require(config.ToJsonString() == ((JsonObject)Call("Configuration", group)!).ToJsonString() && (string?)run["configSha256"] == Hash(profile), "Stable whole-group recipe changed.");
        Require((string?)run["toolchainSha256"] == Hash(Path.Combine(root, "toolchain.json")), "Native toolchain binding changed.");
        ValidateLocalCommand(name, run, tools);
        string reportPath = Path.Combine(directory, "native/reports/mutation-report.json"); JsonObject report = ReadPinned(reportPath);
        Require((string?)run["reportSha256"] == Hash(reportPath) && JsonNode.DeepEquals(run["sources"], Node(sources)), "Raw report/source membership changed.");
        ValidateTrxs(directory, group, run);
        Array definitions = (Array)Call("Definitions", report, sources, Property<object>(candidate, "Inputs"))!;
        Call("ValidateTransportAsyncControls", name, definitions, report, (Func<string, string>)(relative => Source(root, candidate, relative)));
        Require(run["linuxIdentity"] is null, "The selected Linux pilot/runtime/HTTP groups cannot carry Pipes identity evidence.");
        JsonObject provenance = Provenance(root, candidate, Path.Combine(directory, "run.json"), reportPath);
        provenance["reportedTestSourceAliases"] = ValidateTestMappings(directory, root, candidate, group, tools, run, report, windows: false);
        return new(name, root, candidate, group, tools, run, report, definitions, sources, provenance);
    }

    internal GroupEvidence Windows(string proofPath, string sourceRoot, object anchor, object mainTools)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(proofPath))!; JsonObject proof = ReadPinned(proofPath);
        object typed = Decode("PlatformProof", proof);
        string[] required = ["candidate.json", "candidate.zip", "toolchain.json", "profile.json", "run.json", "report.json", "native.log", "tools.zip", "baseline-CIS.Core.UnitTests.trx", "restored-CIS.Core.UnitTests.trx", "baseline-CIS.Core.IntegrationTests.trx", "restored-CIS.Core.IntegrationTests.trx"];
        JsonArray files = proof["files"]!.AsArray(); Require(files.Select(pin => (string)pin!["path"]!).Order(StringComparer.Ordinal).SequenceEqual(required.Order(StringComparer.Ordinal)), "Whole Windows portable evidence membership differs.");
        foreach (JsonNode? pin in files) { string path = Contained(directory, (string)pin!["path"]!); Require(Hash(path) == (string?)pin["sha256"], "Portable Windows artifact changed."); PinFile(path); }
        object candidate = Candidate(directory); JsonObject formattingBoundary = ValidatePortableWindowsHeader(proof, candidate, anchor);
        object group = Group(anchor, "pipes"); JsonObject run = Read(Path.Combine(directory, "run.json"));
        Call("ValidateExecution", run); Call("ValidatePlatformContext", run, group, proof["executionIdentity"]!.AsObject());
        Require(run["linuxIdentity"] is null && (string?)run["platform"] == Property<string>(candidate, "Platform") && (string?)run["host"] == Property<string>(candidate, "Host")
            && (string?)run["candidateIdentity"] == Property<string>(candidate, "Identity") && (string?)run["group"] == "pipes", "The whole Windows native run is not this original platform candidate.");
        Require(Read(Path.Combine(directory, "profile.json")).ToJsonString() == ((JsonObject)Call("Configuration", group)!).ToJsonString(), "Whole Windows Pipes profile differs from the declared native recipe.");
        Require((string?)run["reportSha256"] == Hash(Path.Combine(directory, "report.json")) && (string?)run["configSha256"] == Hash(Path.Combine(directory, "profile.json"))
            && (string?)run["toolchainSha256"] == Hash(Path.Combine(directory, "toolchain.json")), "Windows run/report/profile/toolchain binding differs.");
        object tools = Decode("Toolchain", Read(Path.Combine(directory, "toolchain.json")));
        JsonObject toolJson = Node(tools).AsObject();
        Require((string?)toolJson["schema"] == "cis.healthcheck.grouped-toolchain/v1" && (string?)toolJson["platform"] == Property<string>(candidate, "Platform")
            && (string?)toolJson["host"] == Property<string>(candidate, "Host") && (string?)toolJson["sdk"] == "10.0.401"
            && (string?)toolJson["subjectRuntime"] == "10.0.12" && (string?)toolJson["toolRuntime"] == "8.0.31"
            && toolJson["files"] is JsonArray { Count: > 0 } && toolJson["adapters"] is JsonArray { Count: > 0 }, "Original Windows tool/runtime/platform context differs.");
        Call("ValidatePlatformNativeCommand", run, tools);
        Call("ValidatePlatformTools", Path.Combine(directory, "tools.zip"), Property<object>(typed, "Tools"), tools, mainTools, CheckerSha, HelperSha);
        ValidateTrxs(directory, group, run);
        JsonObject report = Read(Path.Combine(directory, "report.json")); string[] sources = Property<string[]>(group, "Sources");
        Require(JsonNode.DeepEquals(run["sources"], Node(sources)), "Windows report must supply the ENTIRE Pipes source group.");
        Array definitions = (Array)Call("Definitions", report, sources, Property<object>(candidate, "Inputs"))!;
        Require(!definitions.Cast<object>().Any(item => Property<string>(item, "Status") is "RuntimeError" or "Pending")
            && ((string[])Call("NativePopulationHolds", definitions)!).Length == 0, "Whole Windows native execution is incomplete, failed or has unresolved compiler/exclusion outcomes.");
        Call("ValidateTransportAsyncControls", "pipes", definitions, report, (Func<string, string>)(relative => Source(sourceRoot, anchor, relative)));
        Call("ValidatePlatformClaims", Property<object>(typed, "Claims"), definitions, report, Property<object>(candidate, "Inputs"));
        JsonObject provenance = Provenance(directory, candidate, Path.Combine(directory, "run.json"), Path.Combine(directory, "report.json"));
        provenance["reportedTestSourceAliases"] = ValidateTestMappings(directory, sourceRoot, anchor, group, tools, run, report, windows: true);
        provenance["commonInputFormattingBoundary"] = formattingBoundary;
        return new("pipes", directory, candidate, group, tools, run, report, definitions, sources, provenance);
    }

    private JsonArray ValidateTestMappings(string bundle, string sourceRoot, object candidate, object group, object tools, JsonObject run, JsonObject report, bool windows)
    {
        var phases = new List<Dictionary<string, string>>();
        foreach (string phase in new[] { "baseline", "restored" })
        {
            var cases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string projectPath in Property<string[]>(group, "Projects"))
            {
                string project = Path.GetFileNameWithoutExtension(projectPath);
                JsonNode command = run["commandLog"]!.AsArray().Single(item => (string?)item!["name"] == phase + "-" + project)!;
                string expectedAssembly = OriginalTestAssembly(command, project, Property<string>(group, "Filter"), Property<string>(tools, "SubjectDotnet"), windows);
                XDocument document = XDocument.Load(Path.Combine(bundle, phase + "-" + project + ".trx")); XNamespace ns = document.Root!.Name.Namespace;
                foreach (XElement test in document.Descendants(ns + "UnitTest"))
                {
                    XElement method = test.Descendants(ns + "TestMethod").Single(); string id = (string)test.Attribute("id")!;
                    Require(string.Equals(((string?)method.Attribute("codeBase"))?.Replace('\\', '/'), expectedAssembly, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                        && cases.TryAdd(id, (string?)method.Attribute("className") + "." + (string?)method.Attribute("name")), "Native test UUID/actual command assembly origin is ambiguous.");
                }
            }
            phases.Add(cases);
        }
        var observed = new Dictionary<string, string>(StringComparer.Ordinal); var aliases = new JsonArray();
        foreach ((string path, JsonNode? file) in report["testFiles"]!.AsObject())
        {
            string relative = Language + (string)Call("LanguageReportPath", path)!, canonical = CanonicalSourcePath(relative);
            string source = Source(sourceRoot, candidate, relative);
            Require((string?)file!["source"] == source, "An original native test-source byte changed.");
            if (relative != canonical)
            {
                object pin = ((IEnumerable)Property<object>(candidate, "Inputs")).Cast<object>().Single(item => Property<string>(item, "Path") == canonical);
                aliases.Add(new JsonObject { ["originalReportedPath"] = path, ["reportedRelativePath"] = relative,
                    ["canonicalCandidateInputPath"] = canonical, ["capturedSourceSha256"] = Property<string>(pin, "Sha256"), ["rawReportPathRewritten"] = false });
            }
            foreach (JsonNode? test in file["tests"]!.AsArray())
            {
                string id = (string)test!["id"]!, name = (string)test["name"]!;
                Require(observed.TryAdd(id, name) && phases.All(cases => cases.TryGetValue(id, out string? found) && found == name), "Raw test UUID/FQName does not uniquely match BOTH actual contexts.");
            }
        }
        Require(observed.Count > 0 && phases.All(cases => cases.Count == observed.Count), "The whole native test population differs from its actual original baseline/restored contexts.");
        return aliases;
    }

    internal static string OriginalTestAssembly(JsonNode command, string project, string filter, string host, bool windows)
    {
        string[] args = command["arguments"]!.Deserialize<string[]>(Json)!;
        string Normalize(string path) => path.Replace('\\', '/');
        StringComparison comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Require(string.Equals(Normalize((string)command["executable"]!), Normalize(host), comparison) && args.Length == 12
            && args[0] == "test" && args.Skip(2).Take(8).SequenceEqual(new[] { "-c", "Release", "--no-build", "--no-restore", "--filter", filter, "--logger", "trx;LogFileName=" + (string)command["name"]! + ".trx" }, StringComparer.Ordinal)
            && args[10] == "--results-directory", "An original native baseline/restored command changed its host, case filter or fixed build context.");
        string path = Normalize(args[1]);
        Require((windows ? path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1..3] == ":/" : path.StartsWith('/'))
            && !path.Split('/').Any(part => part is "." or "..") && path.EndsWith("/" + project + "/" + project + ".csproj", comparison), "The original native test project path is relative, ambiguous or belongs to another project.");
        return path[..path.LastIndexOf('/')] + "/bin/Release/net10.0/" + project + ".dll";
    }

    private void ValidateTrxs(string root, object group, JsonObject run)
    {
        JsonArray pins = run["nativeTrxPins"]!.AsArray();
        foreach (string project in Property<string[]>(group, "Projects"))
        {
            foreach (string phase in new[] { "baseline", "restored" })
            {
                string name = phase + "-" + Path.GetFileNameWithoutExtension(project) + ".trx", path = Path.Combine(root, name);
                Require((string?)pins.Single(pin => (string?)pin!["path"] == name)!["sha256"] == Hash(path), "Original baseline/restored TRX changed.");
                Call("PassingTrx", path); PinFile(path);
            }
        }
    }
    internal static string Contained(string root, string relative)
    {
        Require(!Path.IsPathFullyQualified(relative), "Portable artifact membership must be relative.");
        string path = Path.GetFullPath(Path.Combine(root, relative));
        Require(path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Artifact escaped its owned bundle.");
        return path;
    }
    private static void ValidateLocalCommand(string group, JsonObject run, object tools)
    {
        JsonObject tool = Node(tools).AsObject();
        JsonArray files = tool["files"]!.AsArray();
        string cli = (string)files.Single(pin => Path.GetFileName((string)pin!["path"]!) == "Stryker.CLI.dll")!["path"]!;
        string cliHash = (string)files.Single(pin => Path.GetFileName((string)pin!["path"]!) == "Stryker.CLI.dll")!["sha256"]!;
        string coreHash = (string)files.Single(pin => Path.GetFileName((string)pin!["path"]!) == "Stryker.Core.dll")!["sha256"]!;
        Require((string?)run["strykerCliSha256"] == cliHash && (string?)run["strykerCoreSha256"] == coreHash, "A selected native group changed the actual native producer.");
        JsonNode command = run["commandLog"]!.AsArray().Single(value => (string?)value!["name"] == "native")!;
        string[] args = command["arguments"]!.Deserialize<string[]>(Json)!;
        Require((string?)command["executable"] == (string?)tool["toolDotnet"] && args.Length == (group == "pilot" ? 12 : 11)
            && args.Take(6).SequenceEqual(new[] { "exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", cli }, StringComparer.Ordinal)
            && args[6] == "--config-file" && args[8] == "--output" && args[10] == "--skip-version-check"
            && (group != "pilot" || args[11] == "--log-to-file"), "Native producer command has undeclared arguments or runtime changes.");
        string suffix = "/profiles/" + group + ".json";
        Require(args[7].EndsWith(suffix, StringComparison.Ordinal) && args[9] == args[7][..^suffix.Length] + "/groups/" + group + "/native", "Native config/output do not belong to one original group execution.");
    }
    internal static JsonObject Provenance(string root, object candidate, string run, string report) => new()
    {
        ["nativeCandidateIdentity"] = Property<string>(candidate, "Identity"), ["originalPlatform"] = Property<string>(candidate, "Platform"),
        ["originalHost"] = Property<string>(candidate, "Host"), ["owningEvidenceRoot"] = root, ["rawReportPath"] = report,
        ["rawReportSha256"] = Hash(report), ["originalRunPath"] = run, ["originalRunSha256"] = Hash(run), ["nativeExecutorSha256"] = CheckerSha,
        ["nativeHelperSha256"] = HelperSha, ["relabelledAsConsumerExecution"] = false
    };
}
