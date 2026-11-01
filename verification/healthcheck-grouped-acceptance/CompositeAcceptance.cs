using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Recovery.HealthcheckAcceptance.Program;

namespace CIS.Recovery.HealthcheckAcceptance;

/// <summary>Actual transport proof admission shared with the separately pinned child operator.</summary>
public static class TransportPrerequisites
{
    /// <summary>Validates original native evidence; does not run tests, change outcomes or award scope/score credit.</summary>
    public static JsonObject Validate(string frozenCheckerPath, string baseCandidateRoot, string windowsProofPath,
        string linuxPermissionManifestPath, string policyPath, string consumerSourceRoot, string consumerBuildReceipt)
    {
        Require(OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription == ".NET 10.0.12", "The shared consumer API requires the actual declared Linux runtime.");
        JsonObject consumer = ConsumerIdentity.Validate(consumerSourceRoot, consumerBuildReceipt);
        var frozen = new FrozenNativeEvidence(frozenCheckerPath);
        JsonObject result = ValidateCore(frozen, baseCandidateRoot, windowsProofPath, linuxPermissionManifestPath, policyPath);
        result["consumer"] = consumer; ConsumerIdentity.Postflight(consumer); frozen.Postflight();
        return result;
    }

    internal static JsonObject ValidateCore(FrozenNativeEvidence frozen, string root, string windows, string linux, string policyPath)
    {
        JsonObject policy = CompositeAcceptance.Policy(frozen.ReadPinned(policyPath));
        object candidate = frozen.Candidate(root);
        frozen.Call("VerifyInputs", Path.Combine(root, "workspace"), candidate);
        frozen.Call("ValidatePilot", root, candidate);
        object tools = frozen.Call("ReadToolchain", root, candidate)!;
        FrozenNativeEvidence.GroupEvidence group = frozen.Windows(windows, root, candidate, tools);
        ValidateWindowsControls(frozen, root, candidate, group, policy);
        JsonObject permission = ValidateLinuxPermissionProof(frozen, root, candidate, linux, policy);
        return new JsonObject
        {
            ["schema"] = "cis.healthcheck.transport-prerequisites/v2", ["commonInputsSha256"] = frozen.Common(candidate),
            ["windowsWholePipes"] = group.Provenance.DeepClone(), ["windowsProofPath"] = windows,
            ["windowsProofSha256"] = FrozenNativeEvidence.Hash(windows), ["linuxPermissionProof"] = permission,
            ["supplementaryKilledAddedToModule"] = 0, ["supplementaryDefinitionsAddedToDenominator"] = 0,
            ["fullModuleQualification"] = false
        };
    }

    internal static JsonObject ValidateLinuxPermissionProof(FrozenNativeEvidence frozen, string baseRoot, object anchor,
        string path, JsonObject policy)
    {
        JsonObject proof = frozen.ReadPinned(path);
        Require((string?)proof["schema"] == "cis.healthcheck.linux-permission-native-proof/v1" && (bool?)proof["complete"] == true,
            "The bounded Linux permission native proof is absent/incomplete.");
        string root = Absolute(proof, "candidateRoot"); object candidate = frozen.Candidate(root);
        Require(frozen.Common(candidate) == frozen.Common(anchor) && (string?)proof["originalCommonInputs"] == frozen.Common(anchor)
            && (string?)proof["candidateIdentity"] == FrozenNativeEvidence.Property<string>(candidate, "Identity"), "Linux permission proof uses different original inputs.");
        frozen.Call("VerifyInputs", Path.Combine(root, "workspace"), candidate); frozen.Call("ValidatePilot", root, candidate);
        object tools = frozen.Call("ReadToolchain", root, candidate)!;
        Require((string?)proof["actualExecutionIdentity"]?["operatingSystem"] == "linux"
            && (uint?)proof["actualExecutionIdentity"]?["uid"] == 0 && (uint?)proof["actualExecutionIdentity"]?["effectiveUid"] == 0,
            "Linux permission proof requires the actual UID0/effectiveUID0 context.");
        Require((string?)proof["platform"] == FrozenNativeEvidence.Property<string>(candidate, "Platform")
            && (string?)proof["host"] == FrozenNativeEvidence.Property<string>(candidate, "Host")
            && (string?)proof["actualExecutionIdentity"]?["host"] == FrozenNativeEvidence.Property<string>(candidate, "Host"), "The Linux control actual host/platform differs from its original candidate.");
        ValidatePermissionCompletion(proof);
        foreach (string name in new[] { "checker", "originalHelper", "executedRelocatedHelper" })
        {
            JsonNode pin = proof[name]!; string file = (string)pin["path"]!;
            Require(FrozenNativeEvidence.Hash(file) == (name == "checker" ? FrozenNativeEvidence.CheckerSha : FrozenNativeEvidence.HelperSha)
                && (string?)pin["sha256"] == FrozenNativeEvidence.Hash(file), "Linux control executed another checker/helper.");
        }
        Require((int?)proof["frozenNativeCandidateSourceCount"] == 1580 && (bool?)proof["noModuleMutationPopulationOrScoreCredit"] == true,
            "Permission proof cannot supply module population or score credit.");
        Require((int?)proof["budgetSeconds"] == 600 && (int?)proof["restorationReserveSeconds"] == 90 && (int?)proof["nativeCeilingSeconds"] == 450
            && (double?)proof["seconds"] is > 0 and <= 600 && (int?)proof["nativeExitCode"] == 0
            && (bool?)proof["outputDrained"] == true && (bool?)proof["restored"] == true && (bool?)proof["cleanupHealthy"] == true
            && proof["failures"] is JsonArray { Count: 0 }, "Linux native control failed its declared execution/restoration bounds.");
        foreach (JsonNode? pin in proof["files"]!.AsArray())
        {
            string file = (string)pin!["path"]!; Require(Path.IsPathFullyQualified(file) && FrozenNativeEvidence.Hash(file) == (string?)pin["sha256"]
                && new FileInfo(file).Length == (long?)pin["bytes"], "A Linux control source/tool/native/identity artifact changed.");
            frozen.PinFile(file);
        }
        void Pinned(string file) => Require(proof["files"]!.AsArray().Any(pin => (string?)pin!["path"] == file && (string?)pin["sha256"] == FrozenNativeEvidence.Hash(file)), "Linux proof omits a required actual artifact: " + file);
        foreach (string file in new[] { Path.Combine(root, "candidate.json"), Path.Combine(root, "candidate.zip"), Path.Combine(root, "toolchain.json") }) { Pinned(file); }
        ValidatePermissionOperatorBuild(frozen, proof);
        object group = frozen.Group(candidate, "pipes"); frozen.Call("ValidateContext", proof, group); frozen.Call("ValidateSdkMsBuildEnvironment", proof, tools);
        string configPath = Absolute(proof, "configurationPath"), reportPath = Absolute(proof, "reportPath"); Pinned(configPath); Pinned(reportPath);
        JsonObject config = frozen.ReadPinned(configPath)["stryker-config"]!.AsObject();
        Require((string?)config["mutation-level"] == "Complete" && (string?)config["coverage-analysis"] == "perTest"
            && (string?)config["test-runner"] == "vstest" && (string?)config["configuration"] == "Release"
            && (string?)config["target-framework"] == "net10.0" && (int?)config["concurrency"] == 2
            && (bool?)config["disable-bail"] == true && (int?)config["additional-timeout"] == 10000,
            "Linux permission control changed its declared fixed native profile.");
        Require(config["test-projects"]!.Deserialize<string[]>(Json)!.SequenceEqual(new[] { "../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj" }, StringComparer.Ordinal), "Linux permission native tests must use the declared Unit-only context.");
        string[] requiredCases = policy["linuxPermissionCases"]!.Deserialize<string[]>(Json)!;
        string[] selectedCases = ((string)config["test-case-filter"]!).Split('|').Select(item => item.Replace("FullyQualifiedName=", "", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        Require(selectedCases.SequenceEqual(requiredCases.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Linux physical native case selection differs.");
        JsonArray obligations = policy["linuxPermissionObligations"]!.AsArray();
        string[] sources = obligations.Select(item => (string)item!["source"]!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] selectors = sources.Select(source =>
        {
            string text = frozen.Source(baseRoot, anchor, FrozenNativeEvidence.Language + "src/CIS.Core/" + source);
            JsonNode[] definitions = obligations.Where(item => (string?)item!["source"] == source).Select(item => item!).ToArray();
            int start = definitions.Min(item => Point(text, JsonNode.Parse((string)item["location"]!)!, "start"));
            int end = definitions.Max(item => Point(text, JsonNode.Parse((string)item["location"]!)!, "end"));
            return source + "{" + start + ".." + end + "}";
        }).Order(StringComparer.Ordinal).ToArray();
        Require(config["mutate"]!.Deserialize<string[]>(Json)!.Order(StringComparer.Ordinal).SequenceEqual(selectors, StringComparer.Ordinal), "Only the three source-bound Unix permission spans may be selected.");
        JsonObject report = frozen.ReadPinned(reportPath);
        ValidateControlCommand(proof, FrozenNativeEvidence.Node(tools).AsObject(), configPath, Path.GetDirectoryName(Path.GetDirectoryName(reportPath)!)!);
        Array definitions = (Array)frozen.Call("Definitions", report, sources, FrozenNativeEvidence.Property<object>(candidate, "Inputs"))!;
        Require(!definitions.Cast<object>().Any(item => FrozenNativeEvidence.Property<string>(item, "Status") is "RuntimeError" or "Pending"), "Linux native control is incomplete or has runtime errors.");
        Require(definitions.Cast<object>().Count(item => FrozenNativeEvidence.Property<string>(item, "Status") is "Killed" or "Survived" or "NoCoverage" or "Timeout") == 6,
            "Only the six exact permission definitions can supply the bounded native control.");
        string baseline = Absolute(proof, "baselineTrxPath"), restored = Absolute(proof, "restoredTrxPath"); Pinned(baseline); Pinned(restored);
        Require((int)frozen.Call("PassingTrx", baseline)! == 5 && (int)frozen.Call("PassingTrx", restored)! == 5, "Exactly five real baseline/restored Unit cases must pass without skips.");
        JsonArray claims = proof["claims"]!.AsArray();
        Require(claims.Count == 6 && claims.Select(item => (string?)item!["obligation"]).Distinct(StringComparer.Ordinal).Count() == 6, "All six distinct Linux permission obligations are mandatory.");
        foreach (JsonNode? expected in obligations)
        {
            JsonNode claim = claims.Single(item => (string?)item!["obligation"] == (string?)expected!["obligation"])!;
            object actual = definitions.Cast<object>().Single(item => FrozenNativeEvidence.Property<string>(item, "Identity") == (string?)expected!["definitionIdentity"]);
            ValidateTuple(actual, expected!.AsObject()); Require(FrozenNativeEvidence.Property<string>(actual, "Status") == "Killed"
                && (string?)claim["definitionIdentity"] == (string?)expected["definitionIdentity"], "Each Linux permission definition must actually be Killed; Timeout cannot substitute.");
            foreach (string field in new[] { "source", "sourceSha256", "operator", "replacement", "requirement" })
            { Require((string?)claim[field] == (string?)expected[field], "Linux claim differs from the actual required tuple: " + field); }
            Require((string?)claim["status"] == "Killed" && JsonNode.DeepEquals(claim["location"], JsonNode.Parse((string)expected["location"]!)), "Linux claim relabelled a native tuple/outcome.");
            Witness(frozen, baseRoot, anchor, report, actual, (string)claim["witnessSource"]!, (string)claim["witnessName"]!, [baseline, restored], expected["allowedWitnesses"]!.AsArray());
        }
        JsonObject receipt = (JsonObject)frozen.Call("ValidateLinuxIdentityJournals", root, candidate, group, proof["linuxIdentity"]!.AsObject())!;
        Require(JsonNode.DeepEquals(receipt, proof["linuxIdentity"]), "Linux role infrastructure/cleanup receipt differs from its actual current journals.");
        frozen.Call("ValidateLinuxIdentityControl", definitions, report, (Func<string, string>)(relative => frozen.Source(baseRoot, anchor, relative)));
        frozen.Call("ValidateLinuxServerRecoveryControl", definitions, report, (Func<string, string>)(relative => frozen.Source(baseRoot, anchor, relative)));
        return new JsonObject { ["path"] = path, ["sha256"] = FrozenNativeEvidence.Hash(path), ["nativeCandidateIdentity"] = FrozenNativeEvidence.Property<string>(candidate, "Identity"),
            ["commonInputsSha256"] = frozen.Common(candidate), ["claims"] = claims.DeepClone(), ["moduleKilledAdded"] = 0, ["moduleDenominatorAdded"] = 0,
            ["actualExecutionIdentity"] = proof["actualExecutionIdentity"]!.DeepClone(), ["platform"] = proof["platform"]!.DeepClone(), ["host"] = proof["host"]!.DeepClone() };
    }

    internal static void ValidatePermissionCompletion(JsonObject proof)
    {
        Require((int?)proof["budgetSeconds"] == 600 && (int?)proof["restorationReserveSeconds"] == 90 && (int?)proof["nativeCeilingSeconds"] == 450
            && (double?)proof["seconds"] is > 0 and <= 600 && (int?)proof["nativeExitCode"] == 0
            && (bool?)proof["nativeOutputDrained"] == true && (bool?)proof["outputDrained"] == true && (bool?)proof["restored"] == true
            && (bool?)proof["cleanupHealthy"] == true && proof["failures"] is JsonArray { Count: 0 },
            "All actual native, total-budget, restoration, cleanup and infrastructure observations must pass.");
    }

    private static void ValidatePermissionOperatorBuild(FrozenNativeEvidence frozen, JsonObject proof)
    {
        string receiptPath = Absolute(proof, "operatorBuildReceiptPath");
        Require((string?)proof["operatorBuildReceiptSha256"] == FrozenNativeEvidence.Hash(receiptPath)
            && proof["files"]!.AsArray().Any(pin => (string?)pin!["path"] == receiptPath), "The actual separate permission-operator build receipt is missing or changed.");
        JsonObject receipt = frozen.ReadPinned(receiptPath);
        Require((string?)receipt["schema"] == "cis.healthcheck.linux-permission-operator-build/v1" && (string?)receipt["compilerPlatform"] == "windows-x64"
            && (string?)receipt["sdk"] == "10.0.401" && (string?)receipt["actualCompileHostRuntime"] == "10.0.12"
            && (int?)receipt["exitCode"] == 0 && (bool?)receipt["outputDrained"] == true && (bool?)receipt["privateRuntime10_0_12Observed"] == true
            && (bool?)receipt["timedOut"] == false && (bool?)receipt["capacityHold"] == false
            && receipt["arguments"]!.AsArray().Any(value => (string?)value == "-target:Build"), "Require the actual successful isolated original Windows operator compilation.");
        JsonArray files = proof["files"]!.AsArray(), sourcePins = receipt["sourcePins"]!.AsArray();
        string[] membership = ["Program.cs", "LinuxPermissionControl.csproj", "requirements.json", "README.md"];
        Require(sourcePins.Select(pin => (string)pin!["path"]!).Order(StringComparer.Ordinal).SequenceEqual(membership.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Linux operator build-source membership differs.");
        string projectPath = (string)files.Single(pin => ConsumerIdentity.Leaf((string)pin!["path"]!) == "LinuxPermissionControl.csproj")!["path"]!;
        string sourceRoot = Path.GetDirectoryName(projectPath)!;
        foreach (JsonNode? pin in sourcePins)
        {
            string path = Path.Combine(sourceRoot, (string)pin!["path"]!);
            Require(files.Any(item => (string?)item!["path"] == path && (string?)item["sha256"] == (string?)pin["sha256"])
                && FrozenNativeEvidence.Hash(path) == (string?)pin["sha256"], "Linux operator source/data differs from its actual compiled source closure.");
        }
        foreach (string name in new[] { "MSBuild.dll", "csc.dll", "Microsoft.CodeAnalysis.CSharp.dll" })
        { Require(receipt["compilerPins"]!.AsArray().Any(pin => ConsumerIdentity.Leaf((string)pin!["path"]!) == name && ConsumerIdentity.IsSha((string?)pin["sha256"])), "Missing observed original Windows compiler pin."); }
        Require(receipt["runtimePins"] is JsonArray { Count: > 0 } runtime && runtime.All(pin => ConsumerIdentity.IsSha((string?)pin!["sha256"]))
            && (string?)receipt["foreignToolPinSemantics"] == "Observed actual Windows producer bytes; not live-rehashed from Linux", "Original producer-runtime/compiler provenance meaning is missing.");
        JsonArray outputs = receipt["portableOutputPins"]!.AsArray();
        string[] outputNames = ["CIS.Recovery.LinuxPermissionControl.dll", "CIS.Recovery.LinuxPermissionControl.deps.json", "CIS.Recovery.LinuxPermissionControl.runtimeconfig.json", "CIS.Recovery.LinuxPermissionControl.pdb", "CIS.Core.TestSupport.dll", "requirements.json"];
        Require(outputs.Select(pin => (string)pin!["name"]!).Order(StringComparer.Ordinal).SequenceEqual(outputNames.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Require all six distinct actual portable permission-operator outputs.");
        foreach (JsonNode? pin in outputs)
        { Require(files.Any(item => ConsumerIdentity.Leaf((string)item!["path"]!) == (string?)pin!["name"] && (string?)item["sha256"] == (string?)pin["sha256"]
            && (long?)item["bytes"] == (long?)pin["bytes"]), "A relocated executed operator output differs from the retained original build."); }
        string assembly = (string)files.Single(pin => ConsumerIdentity.Leaf((string)pin!["path"]!) == "CIS.Recovery.LinuxPermissionControl.dll")!["path"]!;
        ConsumerIdentity.ValidatePdb(assembly, sourceRoot, ["Program.cs"]);
    }

    internal static void ValidateControlCommand(JsonObject proof, JsonObject tools, string config, string nativeOutput)
    {
        JsonNode command = proof["commandLog"]!.AsArray().Single(item => (string?)item!["name"] == "native")!;
        string cli = (string)tools["files"]!.AsArray().Single(pin => Path.GetFileName((string)pin!["path"]!) == "Stryker.CLI.dll")!["path"]!;
        Require((string?)command["executable"] == (string?)tools["toolDotnet"] && (int?)command["exitCode"] == 0 && (bool?)command["outputDrained"] == true
            && (double?)command["timeoutSeconds"] is > 0 and <= 450 && (double?)command["seconds"] is > 0 and <= 450
            && command["arguments"]!.Deserialize<string[]>(Json)!.SequenceEqual(new[] { "exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", cli,
                "--config-file", config, "--output", nativeOutput, "--skip-version-check" }, StringComparer.Ordinal),
            "The actual bounded native command/runtime/config/output/exit differs from the fixed control recipe.");
    }

    internal static void ValidateWindowsControls(FrozenNativeEvidence frozen, string root, object candidate, FrozenNativeEvidence.GroupEvidence group, JsonObject policy)
    {
        foreach (JsonNode? expected in policy["windowsRequiredControls"]!.AsArray())
        {
            object actual = group.Definitions.Cast<object>().Single(item => FrozenNativeEvidence.Property<string>(item, "Identity") == (string?)expected!["definitionIdentity"]);
            ValidateTuple(actual, expected!.AsObject()); Require(FrozenNativeEvidence.Property<string>(actual, "Status") == "Killed", "A mandatory actual Windows async/ownership/two-slot control was not Killed.");
            JsonArray allowed = expected["allowedWitnesses"]!.AsArray(); bool found = false;
            foreach (JsonNode? witness in allowed)
            {
                try
                {
                    string source = (string)witness!["source"]!;
                    string project = source.Split('/')[1];
                    Witness(frozen, root, candidate, group.Report, actual, source, (string)witness["name"]!,
                        [Path.Combine(group.CandidateRoot, "baseline-" + project + ".trx"), Path.Combine(group.CandidateRoot, "restored-" + project + ".trx")], allowed);
                    found = true; break;
                }
                catch (InvalidDataException) { }
            }
            Require(found, "The exact Windows control lacks a required actual named native killer in both original passing contexts.");
        }
    }

    internal static void ValidateTuple(object actual, JsonObject expected)
    {
        foreach ((string field, string property) in new[] { ("definitionIdentity", "Identity"), ("source", "Source"), ("sourceSha256", "SourceSha256"), ("operator", "Operator"), ("location", "Location"), ("replacement", "Replacement") })
        { Require(FrozenNativeEvidence.Property<string>(actual, property) == (string?)expected[field], "The actual permission tuple differs: " + field); }
    }
    internal static int Point(string source, JsonNode location, string end)
    {
        int line = (int)location[end]!["line"]!, column = (int)location[end]!["column"]!, offset = 0;
        Require(line > 0 && column > 0, "Invalid native span.");
        for (int i = 1; i < line; i++) { offset = source.IndexOf('\n', offset) + 1; Require(offset > 0, "Span line escaped source."); }
        int point = checked(offset + column - 1), lineEnd = source.IndexOf('\n', offset);
        Require(point <= (lineEnd < 0 ? source.Length : lineEnd), "Span column escaped its source line."); return point;
    }
    internal static string Absolute(JsonObject json, string name)
    { string path = (string?)json[name] ?? throw new InvalidDataException("Missing proof artifact path: " + name); Require(Path.IsPathFullyQualified(path), "Proof paths must be absolute."); return Path.GetFullPath(path); }

    internal static void Witness(FrozenNativeEvidence frozen, string root, object candidate, JsonObject report, object definition,
        string source, string name, string[] trxs, JsonArray allowed)
    {
        Require(allowed.Any(item => (string?)item!["source"] == source && (string?)item["name"] == name), "A Linux critical definition lacks its substantive native case witness.");
        string relative = FrozenNativeEvidence.Property<string>(definition, "Source"), id = FrozenNativeEvidence.Property<string>(definition, "NativeId");
        JsonNode file = report["files"]!.AsObject().Single(item => CorePath(item.Key) == relative).Value!;
        JsonNode native = file["mutants"]!.AsArray().Single(item => (string?)item!["id"] == id)!;
        string[] killed = native["killedBy"]!.Deserialize<string[]>(Json)!; Require(killed.Length > 0 && killed.Distinct(StringComparer.Ordinal).Count() == killed.Length, "Missing/ambiguous native killedBy identity.");
        JsonNode testFile = report["testFiles"]!.AsObject().Single(item => LanguagePath(item.Key) == source).Value!;
        Require((string?)testFile["source"] == frozen.Source(root, candidate, FrozenNativeEvidence.Language + source), "Native witness test source differs from the frozen input.");
        JsonNode[] witnesses = testFile["tests"]!.AsArray().Where(item => (string?)item!["name"] == name && killed.Contains((string?)item["id"], StringComparer.Ordinal)).Select(item => item!).ToArray();
        Require(witnesses.Length > 0, "The required current test did not actually kill this definition.");
        foreach (JsonNode witness in witnesses)
        {
            string uuid = (string)witness["id"]!;
            Require(report["testFiles"]!.AsObject().SelectMany(item => item.Value!["tests"]!.AsArray()).Count(item => (string?)item!["id"] == uuid) == 1, "Native witness UUID is ambiguous.");
            foreach (string trx in trxs)
            {
                XDocument document = XDocument.Load(trx); XNamespace ns = document.Root!.Name.Namespace;
                XElement test = document.Descendants(ns + "UnitTest").Single(item => (string?)item.Attribute("id") == uuid);
                XElement method = test.Descendants(ns + "TestMethod").Single();
                Require((string?)method.Attribute("className") + "." + (string?)method.Attribute("name") == name
                    && ((string?)method.Attribute("codeBase"))?.Replace('\\', '/').EndsWith("/" + source.Split('/')[1] + "/bin/Release/net10.0/" + source.Split('/')[1] + ".dll", StringComparison.Ordinal) == true,
                    "Native control UUID/name/actual project codeBase differs from its real TRX.");
                Require(document.Descendants(ns + "UnitTestResult").Count(item => (string?)item.Attribute("testId") == uuid && (string?)item.Attribute("outcome") == "Passed") == 1, "Native witness did not uniquely pass in both control contexts.");
            }
        }
    }
    private static string CorePath(string path) { string value = path.Replace('\\', '/'); int point = value.LastIndexOf("/src/CIS.Core/", StringComparison.Ordinal); return point < 0 ? value : value[(point + 14)..]; }
    private static string LanguagePath(string path)
    {
        string value = path.Replace('\\', '/'); int point = value.LastIndexOf("/implementations/csharp/", StringComparison.Ordinal);
        value = point < 0 ? value : value[(point + 24)..];
        var parts = new List<string>(); foreach (string part in value.Split('/')) { if (part == "..") { Require(parts.Count > 0, "Native test path escaped language root."); parts.RemoveAt(parts.Count - 1); } else if (part != ".") { parts.Add(part); } }
        return string.Join('/', parts);
    }
}

internal static class CompositeAcceptance
{
    internal static readonly Dictionary<string, string> Declared = new(StringComparer.Ordinal) { ["pilot"] = "linux", ["runtime"] = "linux", ["http"] = "linux", ["pipes"] = "windows" };
    internal static JsonObject Policy(JsonObject policy)
    {
        Require(FrozenNativeEvidence.Digest(policy.ToJsonString()) == "7c1d5005e998e7b275472a8a39f68001f28fdc5e425fc159b095bc3329cd3ca6",
            "Use the exact reviewed prospective policy, including all six obligations and seven actual Windows controls.");
        Require((string?)policy["schema"] == "cis.healthcheck.grouped-analysis-policy/v2" && (string?)policy["nativeExecutorSha256"] == FrozenNativeEvidence.CheckerSha
            && (string?)policy["nativeHelperSha256"] == FrozenNativeEvidence.HelperSha, "Unknown prospective policy or native executor.");
        JsonObject mapping = policy["wholeGroupPlatforms"]!.AsObject();
        Require(mapping.Count == 4 && Declared.All(item => (string?)mapping[item.Key] == item.Value), "Choose exactly one declared platform for each whole disjoint group; no best-status selection.");
        Require((int?)policy["conservativeThreshold"] == 90 && (bool?)policy["timeoutKillCredit"] == false
            && (int?)policy["supplementaryScopeCredit"] == 0 && policy["linuxPermissionObligations"] is JsonArray { Count: 6 }, "The adopted complete-scope arithmetic/Unix obligations changed.");
        return policy;
    }

    internal static async Task<JsonObject> ReceiveAsync(FrozenNativeEvidence frozen, string manifestPath, string policyPath, string output, JsonObject consumer)
    {
        Require(!Directory.Exists(output) && !File.Exists(output), "Use a fresh analysis output; original receipts are immutable.");
        JsonObject policy = Policy(frozen.ReadPinned(policyPath)), manifest = frozen.ReadPinned(manifestPath);
        Require((string?)manifest["schema"] == "cis.healthcheck.composite-native-inputs/v2", "Do not relabel old v1 receiving as mixed-platform admission.");
        string anchorRoot = TransportPrerequisites.Absolute(manifest, "baseCandidateRoot"); object anchor = frozen.Candidate(anchorRoot);
        frozen.Call("VerifyInputs", Path.Combine(anchorRoot, "workspace"), anchor); frozen.Call("ValidatePilot", anchorRoot, anchor);
        object mainTools = frozen.Call("ReadToolchain", anchorRoot, anchor)!;
        JsonArray origins = manifest["groups"]!.AsArray(); ValidateSelection(origins);
        var groups = new List<FrozenNativeEvidence.GroupEvidence>();
        foreach (JsonNode? origin in origins)
        {
            string name = (string)origin!["group"]!, platform = (string)origin["platform"]!;
            groups.Add(platform == "windows" ? frozen.Windows(TransportPrerequisites.Absolute(origin.AsObject(), "proof"), anchorRoot, anchor, mainTools)
                : frozen.Local(TransportPrerequisites.Absolute(origin.AsObject(), "candidateRoot"), name, anchor));
        }
        TransportPrerequisites.ValidateWindowsControls(frozen, anchorRoot, anchor, groups.Single(group => group.Name == "pipes"), policy);
        Require(groups.SelectMany(group => group.Sources).Distinct(StringComparer.Ordinal).Count() == groups.Sum(group => group.Sources.Length), "Whole-group source scopes overlap.");
        string[] expectedSources = FrozenNativeEvidence.Property<string[]>(anchor, "Sources");
        Require(groups.SelectMany(group => group.Sources).Order(StringComparer.Ordinal).SequenceEqual(expectedSources.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Complete dynamic Healthcheck scope, including zero-definition leaves, is missing.");
        string linuxProofPath = TransportPrerequisites.Absolute(manifest, "linuxPermissionProof");
        JsonObject permission = TransportPrerequisites.ValidateLinuxPermissionProof(frozen, anchorRoot, anchor, linuxProofPath, policy);
        string authorityPath = TransportPrerequisites.Absolute(manifest, "reviewAuthority"); frozen.PinFile(authorityPath);
        string authoritySource = frozen.Source(anchorRoot, anchor, FrozenNativeEvidence.Language + "verification/mutation/healthcheck/review-authority.json");
        Require(File.ReadAllText(authorityPath) == authoritySource, "Technical review authority is not the immutable maintained authority.");
        object authority = frozen.Decode("Authority", JsonNode.Parse(authoritySource)!);
        var reviews = new JsonArray();
        foreach (JsonNode? pathNode in manifest["reviewFiles"]!.AsArray())
        {
            string path = pathNode!.GetValue<string>(); Require(Path.IsPathFullyQualified(path), "Review files must be explicit absolute paths."); frozen.PinFile(path);
            foreach (JsonNode? row in JsonNode.Parse(File.ReadAllText(path))!.AsArray()) { reviews.Add(row!.DeepClone()); }
        }
        ValidateReviewOwnership(groups, reviews);
        var assessments = new List<JsonObject>();
        foreach (IGrouping<string, FrozenNativeEvidence.GroupEvidence> origin in groups.GroupBy(group => FrozenNativeEvidence.Property<string>(group.Candidate, "Identity"), StringComparer.Ordinal))
        {
            var selected = origin.ToArray(); object candidate = selected[0].Candidate;
            Array definitions = frozen.ArrayOf("Definition", selected.SelectMany(group => group.Definitions.Cast<object>()));
            JsonArray rows = new(reviews.Where(row => (string?)row!["candidateIdentity"] == origin.Key).Select(row => row!.DeepClone()).ToArray());
            Array admissions = PlatformAdmissions(frozen, rows, definitions, permission, policy);
            object assessment = frozen.Call("Assess", candidate, selected.SelectMany(group => group.Sources).ToArray(), definitions, frozen.DecodeArray("Review", rows), authority, Array.Empty<string>(), admissions)!;
            assessments.Add(FrozenNativeEvidence.Node(assessment).AsObject());
        }
        JsonObject total = Sum(assessments);
        string childrenPath = TransportPrerequisites.Absolute(manifest, "childControls");
        string windowsProofPath = TransportPrerequisites.Absolute(origins.Single(origin => (string?)origin!["group"] == "pipes")!.AsObject(), "proof");
        JsonObject childOperator = ValidateChildOperator(frozen, TransportPrerequisites.Absolute(manifest, "childOperatorManifest"), childrenPath, anchorRoot, anchor, policy, permission, consumer, windowsProofPath);
        JsonArray children = await Children(frozen, anchorRoot, anchor, mainTools, childrenPath, output);
        ConsumerIdentity.Postflight(consumer); frozen.Postflight();
        JsonObject result = new()
        {
            ["schema"] = "cis.healthcheck.grouped-acceptance/v2", ["commonNativeInputsSha256"] = frozen.Common(anchor),
            ["nativeSourceInventory"] = JsonSerializer.SerializeToNode(expectedSources, Json), ["recipeSha256"] = FrozenNativeEvidence.Hash(policyPath),
            ["consumer"] = consumer, ["originalNativeExecutorSha256"] = FrozenNativeEvidence.CheckerSha,
            ["groups"] = new JsonArray(groups.Select(group => (JsonNode)group.Provenance.DeepClone()).ToArray()),
            ["originPartialAssessments"] = new JsonArray(assessments.Select(value => (JsonNode)value.DeepClone()).ToArray()),
            ["linuxPermissionProof"] = permission, ["actualChildControls"] = children, ["childOperator"] = childOperator, ["assessment"] = total,
            ["reviews"] = reviews, ["qualified"] = total["qualified"]!.DeepClone(), ["failedG02Reclassified"] = false,
            ["physicalRemoteElevatedAndReleaseQualification"] = "separate pending obligations", ["observedInputPins"] = JsonSerializer.SerializeToNode(frozen.Observed, Json)
        };
        Directory.CreateDirectory(output); WriteFresh(Path.Combine(output, "results.json"), result);
        return result;
    }

    internal static void ValidateSelection(JsonArray origins)
    {
        Require(origins.Count == 4 && origins.Select(origin => (string?)origin!["group"]).Distinct(StringComparer.Ordinal).Count() == 4
            && origins.All(origin => origin is JsonObject item && item["group"] is JsonValue && Declared.TryGetValue((string)item["group"]!, out string? platform) && (string?)item["platform"] == platform),
            "Missing, duplicate, partial or differently assigned native groups cannot supply union credit.");
    }
    private static void ValidateReviewOwnership(IEnumerable<FrozenNativeEvidence.GroupEvidence> groups, JsonArray reviews)
    {
        Require(reviews.Select(row => (string?)row!["definitionIdentity"]).Distinct(StringComparer.Ordinal).Count() == reviews.Count, "Duplicate review/definition identity.");
        foreach (JsonNode? review in reviews)
        {
            Require(groups.Count(group => FrozenNativeEvidence.Property<string>(group.Candidate, "Identity") == (string?)review!["candidateIdentity"]
                && group.Definitions.Cast<object>().Any(definition => FrozenNativeEvidence.Property<string>(definition, "Identity") == (string?)review["definitionIdentity"])) == 1,
                "A review belongs to an unselected native origin or a different whole-group outcome.");
        }
    }
    private static Array PlatformAdmissions(FrozenNativeEvidence frozen, JsonArray reviews, Array definitions, JsonObject permission, JsonObject policy)
    {
        var obligations = policy["linuxPermissionObligations"]!.AsArray().Select(item => (string)item!["definitionIdentity"]!).ToHashSet(StringComparer.Ordinal);
        RequirePlatformReviews(reviews, definitions.Cast<object>().Where(definition => FrozenNativeEvidence.Property<string>(definition, "Status") is "Survived" or "NoCoverage"
            && obligations.Contains(FrozenNativeEvidence.Property<string>(definition, "Identity"))).Select(definition => FrozenNativeEvidence.Property<string>(definition, "Identity")));
        var accepted = new List<object>();
        foreach (JsonNode? review in reviews.Where(item => (string?)item!["decision"] == "PlatformCovered"))
        {
            string identity = (string)review!["definitionIdentity"]!;
            JsonNode obligation = policy["linuxPermissionObligations"]!.AsArray().Single(item => (string?)item!["definitionIdentity"] == identity)!;
            JsonNode claim = permission["claims"]!.AsArray().Single(item => (string?)item!["definitionIdentity"] == identity)!;
            object chosen = definitions.Cast<object>().Single(item => FrozenNativeEvidence.Property<string>(item, "Identity") == identity);
            Require(FrozenNativeEvidence.Property<string>(chosen, "Status") is "Survived" or "NoCoverage"
                && (string?)review["rawStatus"] == FrozenNativeEvidence.Property<string>(chosen, "Status")
                && (string?)review["requirement"] == (string?)obligation["requirement"]
                && (string?)review["platformProof"]?["path"] == (string?)permission["path"]
                && (string?)review["platformProof"]?["sha256"] == (string?)permission["sha256"], "PlatformCovered must bind the actual Linux proof and unchanged Windows S/NC obligation; never Timeout.");
            var admission = new JsonObject { ["definitionIdentity"] = identity, ["proofSha256"] = permission["sha256"]!.DeepClone(),
                ["originCandidateIdentity"] = permission["nativeCandidateIdentity"]!.DeepClone(), ["platform"] = permission["platform"]!.DeepClone(),
                ["host"] = permission["host"]!.DeepClone(), ["witnessSource"] = claim["witnessSource"]!.DeepClone(), ["witnessName"] = claim["witnessName"]!.DeepClone() };
            accepted.Add(frozen.Decode("PlatformAdmission", admission));
        }
        return frozen.ArrayOf("PlatformAdmission", accepted);
    }

    internal static void RequirePlatformReviews(JsonArray reviews, IEnumerable<string> requiredIdentities)
    {
        foreach (string identity in requiredIdentities)
        {
            JsonNode?[] matches = reviews.Where(review => (string?)review!["definitionIdentity"] == identity).ToArray();
            Require(matches.Length == 1 && (string?)matches[0]!["decision"] == "PlatformCovered",
                "A selected S/NC Unix permission obligation requires its unique actual platform-proof review; genuine Linux product kills forbid Equivalent/noncritical substitution.");
        }
    }
    internal static JsonObject Sum(IEnumerable<JsonObject> originAssessments)
    {
        JsonObject[] origins = originAssessments.ToArray(); var holds = new List<string>();
        foreach (JsonObject assessment in origins)
        {
            // Frozen Assess always emits its incomplete-inventory hold FIRST when receivedSources is partial.
            // Preserve that original observation above; the separately checked disjoint full union resolves only this structural hold.
            string[] local = assessment["holds"]!.Deserialize<string[]>(Json)!;
            Require((bool?)assessment["sourceInventoryComplete"] != false || local.FirstOrDefault() == "Complete current Healthcheck source inventory has not executed.",
                "The frozen reader's structural inventory hold changed; do not silently drop another hold.");
            holds.AddRange(local.Skip((bool?)assessment["sourceInventoryComplete"] == false ? 1 : 0));
        }
        int Count(string key) => origins.Sum(origin => origin[key]!.GetValue<int>());
        int killed = Count("killed"), denominator = Count("rawEligibleDenominator"), equivalent = Count("reviewedEquivalent"), effective = denominator - equivalent;
        Require(Count("effectiveDenominator") == effective && effective > 0, "Conservative denominator arithmetic differs.");
        decimal score = 100m * killed / effective;
        if (score < 90) { holds.Add("The complete disjoint conservative score is below90percent."); }
        return new JsonObject { ["killed"] = killed, ["survived"] = Count("survived"), ["noCoverage"] = Count("noCoverage"), ["timeout"] = Count("timeout"),
            ["compileError"] = Count("compileError"), ["ignored"] = Count("ignored"), ["rawEligibleDenominator"] = denominator,
            ["reviewedEquivalent"] = equivalent, ["effectiveDenominator"] = effective, ["conservativeScore"] = score,
            ["rawConservativeScore"] = 100m * killed / denominator, ["timeoutKillCredit"] = 0, ["supplementaryKilledAdded"] = 0,
            ["completeDisjointScope"] = true, ["holds"] = JsonSerializer.SerializeToNode(holds, Json), ["qualified"] = holds.Count == 0 };
    }

    internal static JsonObject ChildPrerequisiteSummary(JsonObject receipt) => new()
    {
        ["schema"] = receipt["schema"]!.DeepClone(), ["commonInputsSha256"] = receipt["commonInputsSha256"]!.DeepClone(),
        ["windowsProofSha256"] = receipt["windowsProofSha256"]!.DeepClone(),
        ["linuxPermissionProof"] = new JsonObject { ["sha256"] = receipt["linuxPermissionProof"]!["sha256"]!.DeepClone() },
        ["consumer"] = new JsonObject { ["buildReceiptSha256"] = receipt["consumer"]!["buildReceiptSha256"]!.DeepClone() }
    };

    internal static JsonObject ReadChildRecord(FrozenNativeEvidence frozen, JsonObject manifest, string field, string directory, string name)
    {
        Require(manifest[field] is JsonObject, "A child producer record reference is missing: " + field);
        JsonObject pin = manifest[field]!.AsObject(); string file = (string?)pin["path"] ?? "";
        Require(Path.IsPathFullyQualified(file) && Path.GetFullPath(file) == Path.Combine(Path.GetFullPath(directory), name)
            && File.Exists(file), "Child record must be its original named, owned file: " + field);
        long bytes = new FileInfo(file).Length;
        Require(bytes is > 0 and <= 262144 && (long?)pin["bytes"] == bytes && (string?)pin["sha256"] == FrozenNativeEvidence.Hash(file), "A bounded child producer record changed: " + field);
        Require(manifest["actualEvidencePins"] is JsonArray pins && pins.Any(item => JsonNode.DeepEquals(item, pin)), "Child record is absent from the final evidence closure.");
        return frozen.ReadPinned(file);
    }

    internal static JsonObject ValidateChildPrerequisiteRecords(FrozenNativeEvidence frozen, JsonObject manifest, string directory,
        string common, string windowsPath, string windowsSha, string permissionSha, string consumerSha)
    {
        JsonObject before = ReadChildRecord(frozen, manifest, "transportPrerequisitesRecord", directory, "transport-prerequisites-before.json");
        JsonObject after = ReadChildRecord(frozen, manifest, "postflightTransportPrerequisitesRecord", directory, "transport-prerequisites-after.json");
        foreach ((string stage, JsonObject receipt) in new[] { ("transportPrerequisites", before), ("postflightTransportPrerequisites", after) })
        {
            Require((string?)receipt["schema"] == "cis.healthcheck.transport-prerequisites/v2" && (string?)receipt["commonInputsSha256"] == common
                && (string?)receipt["windowsProofPath"] == windowsPath && (string?)receipt["windowsProofSha256"] == windowsSha
                && (string?)receipt["linuxPermissionProof"]?["sha256"] == permissionSha && (string?)receipt["consumer"]?["buildReceiptSha256"] == consumerSha,
                "Actual child prerequisite record uses different source/proof/consumer inputs.");
            Require(JsonNode.DeepEquals(manifest[stage], ChildPrerequisiteSummary(receipt)), "A compact prerequisite summary differs from its full pinned record.");
        }
        Require(JsonNode.DeepEquals(before, after), "Child proof/consumer admission changed between its full before/after records.");
        return new JsonObject { ["before"] = manifest["transportPrerequisitesRecord"]!.DeepClone(), ["after"] = manifest["postflightTransportPrerequisitesRecord"]!.DeepClone(), ["fullRecordsIdentical"] = true };
    }

    internal static void ValidateChildExecutionRecord(JsonObject manifest, JsonObject execution, string candidateIdentity)
    {
        ValidateChildCompletion(execution, candidateIdentity, executionRecord: true);
        string[] names = ["baseline-CIS.Core.IntegrationTests", "native", "restored-build", "restored-CIS.Core.IntegrationTests", "audit-http-host", "audit-probe"];
        Require(execution["commands"] is JsonArray commands && commands.Count == names.Length
            && commands.Select(command => (string?)command!["name"]).SequenceEqual(names, StringComparer.Ordinal), "All six actual child phases and durable guard outcomes are required in order.");
        foreach (JsonNode? item in execution["commands"]!.AsArray())
        {
            JsonObject command = item!.AsObject(); bool native = (string?)command["name"] == "native";
            double start = (double?)command["startedElapsedSeconds"] ?? -1, timeout = (double?)command["timeoutSeconds"] ?? -1, seconds = (double?)command["seconds"] ?? -1;
            Require((int?)command["exitCode"] == 0 && (int?)command["processId"] > 0 && (bool?)command["outputDrained"] == true
                && (bool?)command["cleanupHealthy"] == true && (bool?)command["monitorCompleted"] == true && command.ContainsKey("capacityFailure") && command.ContainsKey("failure")
                && command["capacityFailure"] is null && command["failure"] is null
                && command["arguments"] is JsonArray { Count: > 0 } && Path.IsPathFullyQualified((string?)command["executable"] ?? ""), "An actual child phase/process/capacity guard failed or was not observed.");
            Require(start >= 0 && seconds >= 0 && start + seconds <= 600 && timeout > 0 && timeout <= 600 - start - (native ? 90 : 0)
                && (int?)command["restorationReserveSeconds"] == (native ? 90 : 0), "A child phase extended the original total/restoration deadline.");
            Require((long?)command["peakOwnedBytes"] is > 0 and <= 2147483648L && (long?)command["peakChildBytes"] is >= 0 and <= 134217728L,
                "An actual child phase exceeded its owned/child storage cap.");
        }
        Require((long?)execution["aggregateLimitBytes"] == 2147483648L && (long?)execution["freeReserveBytes"] == 2147483648L
            && (long?)execution["childLimitBytes"] == 134217728L && (long?)execution["retainedLimitBytes"] == 268435456L
            && (long?)execution["peakOwnedBytes"] is > 0 and <= 2147483648L && (long?)execution["peakChildBytes"] is >= 0 and <= 134217728L
            && (long?)execution["retainedBytesBeforeManifest"] is >= 0 and <= 267911168L, "Child capacity limits, actual peaks or retained allowance failed.");
        Require(execution["failureRecords"] is JsonArray { Count: 0 }, "Durable child failure records forbid admission.");
        foreach (string field in new[] { "candidateIdentity", "nativeCompleted", "restored", "processCleanupHealthy", "temporaryCleanup", "complete", "budgetSeconds", "restorationReserveSeconds" })
        { Require(JsonNode.DeepEquals(manifest[field], execution[field]), "Final child summary differs from its durable execution record: " + field); }
        Require((double?)execution["seconds"] <= (double?)manifest["seconds"], "Final child summary predates its durable execution terminal.");
    }

    internal static void ValidateChildCommandArguments(JsonNode command, string executable, string[] expected) =>
        Require((string?)command["executable"] == executable && command["arguments"]!.Deserialize<string[]>(Json)!.SequenceEqual(expected, StringComparer.Ordinal),
            "An actual child phase changed its native/checker/audit arguments.");

    internal static void ValidateChildPhaseRecords(FrozenNativeEvidence frozen, JsonObject manifest, JsonObject execution, string directory)
    {
        int index = 0;
        foreach (JsonNode? command in execution["commands"]!.AsArray())
        {
            JsonObject observedCommand = command!.AsObject();
            Require(observedCommand["record"] is JsonObject, "An observed child phase has no durable record reference.");
            JsonObject pin = observedCommand["record"]!.AsObject(); string file = (string?)pin["path"] ?? "";
            string name = "phase-" + (++index).ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "-" + (string)observedCommand["name"]! + ".json";
            Require(Path.IsPathFullyQualified(file) && Path.GetFullPath(file) == Path.Combine(Path.GetFullPath(directory), name)
                && File.Exists(file) && new FileInfo(file).Length is > 0 and <= 262144 && (long?)pin["bytes"] == new FileInfo(file).Length
                && (string?)pin["sha256"] == FrozenNativeEvidence.Hash(file) && manifest["actualEvidencePins"]!.AsArray().Any(item => JsonNode.DeepEquals(item, pin)), "A durable child phase record is absent/changed.");
            JsonObject original = frozen.ReadPinned(file), observed = (JsonObject)observedCommand.DeepClone(); observed.Remove("record");
            Require(JsonNode.DeepEquals(original, observed), "Terminal child command differs from its actual durable phase/guard record.");
        }
    }

    private static JsonObject ValidateChildOperator(FrozenNativeEvidence frozen, string path, string rowsPath, string root, object candidate, JsonObject policy, JsonObject permission, JsonObject consumer, string windowsProofPath)
    {
        JsonObject manifest = frozen.ReadPinned(path);
        ValidateChildCompletion(manifest, FrozenNativeEvidence.Property<string>(candidate, "Identity"));
        object childTools = frozen.Call("ReadToolchain", root, candidate)!;
        frozen.Call("ValidateSdkMsBuildEnvironment", manifest, childTools);
        JsonObject expectedProperties = new() { ["CisMutationTesting"] = "true", ["CisHealthcheckMutationCohort"] = "true", ["CisHealthcheckPilotCohort"] = "false", ["CisLinuxIdentityQualification"] = "true" };
        Require(JsonNode.DeepEquals(manifest["buildProperties"], expectedProperties), "The child native build context differs from its actual Linux permission prerequisite.");
        JsonArray pins = new(manifest["supplementalPins"]!.AsArray().Concat(manifest["actualEvidencePins"]!.AsArray()).Concat(manifest["baselineBinaries"]!.AsArray()).Select(pin => pin!.DeepClone()).ToArray());
        foreach (JsonNode? pin in pins)
        {
            string file = (string)pin!["path"]!; Require(Path.IsPathFullyQualified(file) && FrozenNativeEvidence.Hash(file) == (string?)pin["sha256"] && new FileInfo(file).Length == (long?)pin["bytes"], "A child producer/source/tool/native/restored artifact changed."); frozen.PinFile(file);
        }
        Require((string?)manifest["childControls"]?["path"] == rowsPath && (string?)manifest["childControls"]?["sha256"] == FrozenNativeEvidence.Hash(rowsPath)
            && (long?)manifest["childControls"]?["bytes"] == new FileInfo(rowsPath).Length, "Both original child rows must be the exact v4 operator output.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!, identity = FrozenNativeEvidence.Property<string>(candidate, "Identity");
        JsonObject prerequisites = ValidateChildPrerequisiteRecords(frozen, manifest, directory, frozen.Common(candidate), windowsProofPath,
            FrozenNativeEvidence.Hash(windowsProofPath), (string)permission["sha256"]!, (string)consumer["buildReceiptSha256"]!);
        JsonObject execution = ReadChildRecord(frozen, manifest, "executionRecord", directory, "execution-record.json");
        ValidateChildExecutionRecord(manifest, execution, identity);
        ValidateChildPhaseRecords(frozen, manifest, execution, directory);
        string[] selected = policy["childSelections"]!.AsArray().Select(item =>
        {
            string source = (string)item!["source"]!, statement = (string)item["statement"]!, text = frozen.Source(root, candidate, FrozenNativeEvidence.Language + "src/CIS.Core/" + source);
            int start = text.IndexOf(statement, StringComparison.Ordinal); Require(start >= 0 && start == text.LastIndexOf(statement, StringComparison.Ordinal), "The actual child-body statement changed or became ambiguous.");
            return source + "{" + start + ".." + (start + statement.Length) + "}";
        }).ToArray();
        Require(manifest["selectors"]!.Deserialize<string[]>(Json)!.SequenceEqual(selected, StringComparer.Ordinal), "Child controls must select exactly the two reviewed actual source statements.");
        string configPath = (string)manifest["actualEvidencePins"]!.AsArray().Single(pin => ConsumerIdentity.Leaf((string)pin!["path"]!) == "stryker-config.json")!["path"]!;
        JsonObject config = frozen.ReadPinned(configPath)["stryker-config"]!.AsObject();
        string expectedFilter = string.Join('|', policy["childCases"]!.AsArray().Select(item => "FullyQualifiedName=" + item!.GetValue<string>()));
        Require((string?)manifest["filter"] == expectedFilter && (string?)config["test-case-filter"] == expectedFilter && (int?)config["concurrency"] == 2
            && (string?)config["coverage-analysis"] == "off" && (string?)config["mutation-level"] == "Complete" && (bool?)config["disable-bail"] == true
            && config["mutate"]!.Deserialize<string[]>(Json)!.SequenceEqual(selected, StringComparer.Ordinal), "Child v4 native context changed its actual cases, two spans, worker/coverage/bail settings.");
        JsonArray rows = JsonNode.Parse(File.ReadAllText(rowsPath))!.AsArray(); ValidateChildRows(rows);
        JsonObject binding = frozen.ReadPinned((string)rows[0]!["nativeBinding"]!); string language = (string)binding["languageRoot"]!, profile = (string)binding["profileId"]!;
        string projectPath = Path.Combine(language, "tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj"), host = (string)consumer["actualDotnetHost"]!;
        string cli = (string)FrozenNativeEvidence.Node(childTools)["files"]!.AsArray().Single(pin => ConsumerIdentity.Leaf((string)pin!["path"]!) == "Stryker.CLI.dll")!["path"]!;
        string sessionPath = (string)rows[0]!["session"]!, attemptRoot = Path.GetDirectoryName(Path.GetDirectoryName(sessionPath))!, childRoot = Path.GetDirectoryName(attemptRoot)!;
        JsonArray commands = execution["commands"]!.AsArray();
        ValidateChildCommandArguments(commands[1]!, host, ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", frozen.CheckerPath, "healthcheck-mutation-run",
            "--language-root", language, "--profile-id", profile, "--dotnet", host, "--stryker-cli", cli, "--stryker-core", Path.Combine(Path.GetDirectoryName(cli)!, "Stryker.Core.dll"),
            "--child-root", childRoot, "--maximum-child-bytes", "134217728", "--", "--config-file", configPath, "--output", Path.GetDirectoryName(configPath)!, "--skip-version-check", "--log-to-file"]);
        ValidateChildCommandArguments(commands[2]!, host, ["build", projectPath, "-c", "Release", "--no-restore", "--no-incremental"]);
        foreach (string phase in new[] { "baseline", "restored" })
        {
            string trx = Path.Combine(Path.GetDirectoryName(path)!, phase + "-CIS.Core.IntegrationTests.trx"); frozen.PinFile(trx);
            Require((int)frozen.Call("PassingTrx", trx)! == 3 && pins.Any(pin => (string?)pin!["path"] == trx), "Exactly three actual passing baseline/restored child cases are mandatory.");
            XDocument document = XDocument.Load(trx); XNamespace ns = document.Root!.Name.Namespace;
            string[] names = document.Descendants(ns + "UnitTest").Select(test => test.Descendants(ns + "TestMethod").Single()).Select(method => (string?)method.Attribute("className") + "." + (string?)method.Attribute("name")).ToArray();
            Require(names.Order(StringComparer.Ordinal).SequenceEqual(policy["childCases"]!.Deserialize<string[]>(Json)!.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Actual child native TRX membership differs.");
            JsonNode command = commands.Single(item => (string?)item!["name"] == phase + "-CIS.Core.IntegrationTests")!;
            Require(FrozenNativeEvidence.OriginalTestAssembly(command, "CIS.Core.IntegrationTests", expectedFilter, host, false)
                == Path.Combine(language, "tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll"), "Actual child baseline/restoration ran a different original project.");
        }
        foreach (JsonNode? row in rows)
        {
            string kind = (string)row!["kind"]!;
            ValidateChildCommandArguments(commands.Single(command => (string?)command!["name"] == "audit-" + kind)!, host,
                ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", frozen.CheckerPath, "healthcheck-mutation-children", "audit", "--session", (string)row["session"]!,
                 "--output", Path.Combine(directory, "audit-" + kind), "--require-kind", kind, "--require-active", ((int)row["activeMutant"]!).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }
        string buildPath = TransportPrerequisites.Absolute(manifest, "operatorBuildReceiptPath"); JsonObject build = frozen.ReadPinned(buildPath);
        Require((string?)manifest["operatorBuildReceiptSha256"] == FrozenNativeEvidence.Hash(buildPath) && pins.Any(pin => (string?)pin!["path"] == buildPath)
            && (string?)build["schema"] == "cis.healthcheck.child-operator-build/v4" && (string?)build["compilerPlatform"] == "windows-x64"
            && (string?)build["sdk"] == "10.0.401" && (string?)build["actualCompileHostRuntime"] == "10.0.12" && (int?)build["exitCode"] == 0
            && (bool?)build["outputDrained"] == true && (bool?)build["privateRuntime10_0_12Observed"] == true && (bool?)build["timedOut"] == false && (bool?)build["capacityHold"] == false
            && build["arguments"]!.AsArray().Any(item => (string?)item == "-target:Build"), "Require the actual separately compiled child-v4 producer, not an arbitrary completion flag.");
        string sourceRoot = TransportPrerequisites.Absolute(manifest, "operatorSourceRoot"); JsonArray sources = build["sourcePins"]!.AsArray();
        Require(sources.Select(pin => (string)pin!["path"]!).Order(StringComparer.Ordinal).SequenceEqual(new[] { "ChildOperator.csproj", "Program.cs", "README.md" }, StringComparer.Ordinal), "The actual child producer source/project/README membership differs.");
        foreach (JsonNode? pin in sources)
        {
            string file = Path.Combine(sourceRoot, (string)pin!["path"]!);
            Require(FrozenNativeEvidence.Hash(file) == (string?)pin["sha256"] && pins.Any(item => (string?)item!["path"] == file), "The actual child source changed after compilation.");
        }
        string[] outputNames = ["CIS.Recovery.ChildOperatorV4.dll", "CIS.Recovery.ChildOperatorV4.deps.json", "CIS.Recovery.ChildOperatorV4.pdb", "CIS.Recovery.ChildOperatorV4.runtimeconfig.json"];
        Require(build["portableOutputPins"]!.AsArray().Select(pin => (string)pin!["name"]!).Order(StringComparer.Ordinal).SequenceEqual(outputNames.Order(StringComparer.Ordinal), StringComparer.Ordinal), "The four actual portable child producer outputs are mandatory.");
        foreach (JsonNode? pin in build["portableOutputPins"]!.AsArray())
        { Require(pins.Any(item => ConsumerIdentity.Leaf((string)item!["path"]!) == (string?)pin!["name"] && (string?)item["sha256"] == (string?)pin["sha256"] && (long?)item["bytes"] == (long?)pin["bytes"]), "Relocated child outputs differ from the original build."); }
        foreach (string name in new[] { "MSBuild.dll", "csc.dll", "Microsoft.CodeAnalysis.CSharp.dll" })
        { Require(build["compilerPins"]!.AsArray().Any(pin => ConsumerIdentity.Leaf((string)pin!["path"]!) == name && ConsumerIdentity.IsSha((string?)pin["sha256"])), "Missing original child compiler provenance."); }
        Require(build["runtimePins"] is JsonArray { Count: > 0 } runtime && runtime.All(pin => ConsumerIdentity.IsSha((string?)pin!["sha256"]))
            && (string?)build["foreignToolPinSemantics"] == "Observed actual Windows producer bytes; not live-rehashed from Linux", "The actual original child producer runtime/compiler pin meaning is missing.");
        string assembly = (string)pins.Single(pin => ConsumerIdentity.Leaf((string)pin!["path"]!) == "CIS.Recovery.ChildOperatorV4.dll")!["path"]!;
        ConsumerIdentity.ValidatePdb(assembly, sourceRoot, ["Program.cs"]);
        return new JsonObject { ["manifestPath"] = path, ["manifestSha256"] = FrozenNativeEvidence.Hash(path), ["buildReceiptPath"] = buildPath,
            ["buildReceiptSha256"] = FrozenNativeEvidence.Hash(buildPath), ["originalBuildObservation"] = build, ["prerequisiteRecords"] = prerequisites,
            ["executionRecord"] = manifest["executionRecord"]!.DeepClone(), ["moduleScopeCredit"] = 0 };
    }

    internal static void ValidateChildCompletion(JsonObject manifest, string candidateIdentity, bool executionRecord = false)
    {
        Require((string?)manifest["schema"] == (executionRecord ? "cis.recovery.child-operator-execution/v4" : "cis.recovery.child-operator/v4")
            && (int?)manifest["recordRevision"] == 2 && (bool?)manifest["complete"] == true
            && (string?)manifest["candidateIdentity"] == candidateIdentity
            && (bool?)manifest["nativeCompleted"] == true && (bool?)manifest["restored"] == true && (bool?)manifest["processCleanupHealthy"] == true
            && manifest["failures"] is JsonArray { Count: 0 } && (string?)manifest["temporaryCleanup"] == "owned short temp removed"
            && (int?)manifest["budgetSeconds"] == 600 && (int?)manifest["restorationReserveSeconds"] == 90 && (double?)manifest["seconds"] is > 0 and <= 600
            && (bool?)manifest["noUnionScopeOrScoreCredit"] == true, "Actual v4 child execution, restoration, cleanup or budget is incomplete/unhealthy.");
    }

    internal static void ValidateChildRows(JsonArray rows) => Require(rows.Count == 2 && rows.Select(row => (string?)row!["kind"]).Order(StringComparer.Ordinal)
        .SequenceEqual(new[] { "http-host", "probe" }, StringComparer.Ordinal), "Both actual child-body propagation controls are mandatory.");
    private static async Task<JsonArray> Children(FrozenNativeEvidence frozen, string root, object candidate, object tools, string path, string output)
    {
        frozen.PinFile(path); JsonArray rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        ValidateChildRows(rows);
        var results = new JsonArray(); Assembly assembly = frozen.Method("CandidateAt", 1).DeclaringType!.Assembly;
        Type protocol = assembly.GetTypes().Single(type => type.Name == "HealthcheckMutationChildProtocol"), sessionType = protocol.GetNestedType("Session", BindingFlags.Public | BindingFlags.NonPublic)!;
        Type audit = assembly.GetTypes().Single(type => type.Name == "HealthcheckChildMutationVerification");
        foreach (JsonNode? row in rows)
        {
            object child = frozen.Decode("ChildControl", row!); string sessionPath = (string)row!["session"]!; frozen.PinFile(sessionPath);
            object session = JsonSerializer.Deserialize(File.ReadAllText(sessionPath), sessionType, Json)!;
            object selection = frozen.Call("ValidateChildBinding", child, session, candidate, tools)!;
            string[] sources = ((IEnumerable)selection).Cast<object>().Select(item => FrozenNativeEvidence.Property<string>(item, "Source")).Order(StringComparer.Ordinal).ToArray();
            string reportPath = (string)row["nativeReport"]!; frozen.PinFile(reportPath);
            Array definitions = (Array)frozen.Call("Definitions", frozen.ReadPinned(reportPath), sources, FrozenNativeEvidence.Property<object>(candidate, "Inputs"))!;
            object active = definitions.Cast<object>().Single(item => FrozenNativeEvidence.Property<string>(item, "Identity") == (string?)row["definitionIdentity"]
                && FrozenNativeEvidence.Property<string>(item, "NativeId") == ((int)row["activeMutant"]!).ToString(System.Globalization.CultureInfo.InvariantCulture));
            string expected = (string?)row["kind"] == "http-host" ? "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs" : "Healthcheck/Pipes/HealthProbeClient.cs";
            Require(FrozenNativeEvidence.Property<string>(active, "Source") == expected && FrozenNativeEvidence.Property<string>(active, "Status") is "Killed" or "Survived", "Child proof did not execute its actual shipping body.");
            frozen.Call("ValidateChildDefinitionSelection", active, selection, frozen.Source(root, candidate, FrozenNativeEvidence.Language + "src/CIS.Core/" + expected));
            string destination = Path.Combine(output, "children", (string)row["kind"]!);
            MethodInfo method = audit.GetMethod("RunAsync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            int code = await (Task<int>)method.Invoke(null, new object[] { new[] { "audit", "--session", sessionPath, "--output", destination, "--require-kind", (string)row["kind"]!, "--require-active", ((int)row["activeMutant"]!).ToString(System.Globalization.CultureInfo.InvariantCulture) } })!;
            Require(code == 0, "Actual child source selection, activation, protocol or cleanup audit failed.");
            results.Add(new JsonObject { ["kind"] = row["kind"]!.DeepClone(), ["definitionIdentity"] = row["definitionIdentity"]!.DeepClone(),
                ["nativeReportSha256"] = FrozenNativeEvidence.Hash(reportPath), ["sessionSha256"] = FrozenNativeEvidence.Hash(sessionPath),
                ["actualAuditSha256"] = FrozenNativeEvidence.Hash(Path.Combine(destination, "results.json")), ["moduleScopeCredit"] = 0 });
        }
        return results;
    }
}
