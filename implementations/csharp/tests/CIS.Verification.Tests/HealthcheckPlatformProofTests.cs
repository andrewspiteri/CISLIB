using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using G = CIS.Verification.HealthcheckGroupedMutation;

namespace CIS.Tests.Verification;

// Parser/admission controls only. Their synthetic data supplies no platform execution evidence.
[TestClass]
public sealed class HealthcheckPlatformProofTests
{
    private const string Source = "Healthcheck/Pipes/HealthPipeEndpoint.cs";
    private const string WitnessSource = "tests/CIS.Core.UnitTests/Healthcheck/Reporting/EndpointTests.cs";
    private const string WitnessName = "CIS.Tests.UnitTests.Healthcheck.EndpointTests.ProtectedOwnerBoundary";
    private const string TestText = "class EndpointTests { void ProtectedOwnerBoundary() { } }";
    private const string Location = "{\"start\":{\"line\":1,\"column\":1},\"end\":{\"line\":1,\"column\":3}}";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [TestMethod]
    [DataRow("Survived")]
    [DataRow("NoCoverage")]
    public void AValidatedPlatformProofReviewsTheObligationWithoutChangingChosenLaneCounts(string status)
    {
        G.Candidate candidate = Candidate(); G.Definition miss = Definition(status) with { NativeId = "9" };
        G.PlatformProof proof = Proof(); G.Review review = Review(status);
        G.Definition actual = Definition("Killed");
        G.ValidatePlatformClaims(proof.Claims, [actual], Report(), candidate.Inputs);
        G.PlatformAdmission admission = G.ValidatePlatformCoveredDefinition(miss, review, proof, [actual], "proof-hash");
        G.Definition[] definitions = Enumerable.Range(0, 9).Select(index => Definition("Killed") with { Identity = "kill-" + index, NativeId = index.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Append(miss).ToArray();
        G.Assessment result = G.Assess(candidate, candidate.Sources, definitions, [review], new("Named technical source review", ["reviewer"]), platformAdmissions: [admission]);
        Assert.AreEqual(9, result.Killed); Assert.AreEqual(10, result.RawEligibleDenominator); Assert.AreEqual(10, result.EffectiveDenominator);
        Assert.AreEqual(0, result.ReviewedEquivalent); Assert.AreEqual(90m, result.ConservativeScore); Assert.AreEqual(90m, result.RawConservativeScore);
        Assert.AreEqual(status == "Survived" ? 1 : 0, result.Survived); Assert.AreEqual(status == "NoCoverage" ? 1 : 0, result.NoCoverage);
        Assert.IsTrue(result.Qualified);
    }

    [TestMethod]
    public void APlatformDecisionCannotSupplyItsOwnApprovalOrSmuggleAProofIntoAnotherDecision()
    {
        G.Candidate candidate = Candidate(); G.Definition miss = Definition("NoCoverage"); G.Review review = Review("NoCoverage");
        Assert.ThrowsExactly<InvalidDataException>(() => G.Assess(candidate, candidate.Sources, [miss], [review], new("Named review", ["reviewer"])));
        G.PlatformAdmission wrong = new(miss.Identity, "different-proof", "foreign", "Windows", "host", WitnessSource, WitnessName);
        Assert.ThrowsExactly<InvalidDataException>(() => G.Assess(candidate, candidate.Sources, [miss], [review], new("Named review", ["reviewer"]), platformAdmissions: [wrong]));
        Assert.ThrowsExactly<InvalidDataException>(() => G.Assess(candidate, candidate.Sources, [miss], [review with { Decision = "ReviewedNoncritical" }], new("Named review", ["reviewer"])));
    }

    [TestMethod]
    [DataRow("Timeout")]
    [DataRow("CompileError")]
    [DataRow("Ignored")]
    [DataRow("Pending")]
    [DataRow("RuntimeError")]
    [DataRow("Killed")]
    public void PlatformCoveredCannotRelabelOtherChosenLaneStatuses(string status)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => G.ValidatePlatformCoveredDefinition(Definition(status), Review(status), Proof(), [Definition("Killed")], "proof-hash"));
    }

    [TestMethod]
    [DataRow("status")]
    [DataRow("source")]
    [DataRow("source-hash")]
    [DataRow("operator")]
    [DataRow("span")]
    [DataRow("replacement")]
    [DataRow("requirement")]
    public void AForeignDefinitionCannotReuseAnIdentityForDifferentEvidence(string change)
    {
        G.Definition actual = Definition("Killed"); G.Review review = Review("NoCoverage");
        actual = change switch
        {
            "status" => actual with { Status = "Timeout" },
            "source" => actual with { Source = "Healthcheck/Pipes/Other.cs" },
            "source-hash" => actual with { SourceSha256 = "changed" },
            "operator" => actual with { Operator = "String mutation" },
            "span" => actual with { Location = Location.Replace("\"column\":3", "\"column\":4", StringComparison.Ordinal) },
            "replacement" => actual with { Replacement = "{}" },
            _ => actual
        };
        if (change == "requirement") { review = review with { Requirement = "a different obligation" }; }
        Assert.ThrowsExactly<InvalidDataException>(() => G.ValidatePlatformCoveredDefinition(Definition("NoCoverage"), review, Proof(), [actual], "proof-hash"));
    }

    [TestMethod]
    [DataRow("not-killed")]
    [DataRow("raw-status")]
    [DataRow("raw-operator")]
    [DataRow("raw-span")]
    [DataRow("raw-replacement")]
    [DataRow("no-killer")]
    [DataRow("duplicate-killer")]
    [DataRow("wrong-source-bytes")]
    [DataRow("wrong-test")]
    [DataRow("ambiguous-test-id")]
    [DataRow("scheduling-only")]
    [DataRow("missing-requirement")]
    public void RealProductWitnessMembershipAndCurrentSourceBytesAreRequired(string change)
    {
        G.Definition definition = Definition("Killed"); JsonObject report = Report(); G.PlatformClaim claim = Proof().Claims[0];
        JsonObject native = report["files"]![Source]!["mutants"]![0]!.AsObject();
        JsonObject test = report["testFiles"]![WitnessSource]!.AsObject();
        switch (change)
        {
            case "not-killed": definition = definition with { Status = "Survived" }; break;
            case "raw-status": native["status"] = "Timeout"; break;
            case "raw-operator": native["mutatorName"] = "Boolean mutation"; break;
            case "raw-span": native["location"]!["end"]!["column"] = 4; break;
            case "raw-replacement": native["replacement"] = "{}"; break;
            case "no-killer": native["killedBy"] = new JsonArray(); break;
            case "duplicate-killer": native["killedBy"] = new JsonArray("test-id", "test-id"); break;
            case "wrong-source-bytes": test["source"] = TestText + " // changed"; break;
            case "wrong-test": test["tests"]![0]!["name"] = "UnrelatedCase"; break;
            case "ambiguous-test-id": test["tests"]!.AsArray().Add(test["tests"]![0]!.DeepClone()); break;
            case "scheduling-only": claim = claim with { WitnessName = "CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode" }; break;
            case "missing-requirement": claim = claim with { Requirement = " " }; break;
            default: throw new InvalidOperationException("Unknown control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => G.ValidatePlatformClaims([claim], [definition], report, Candidate().Inputs));
    }

    [TestMethod]
    public void ADefinitionCannotBeClaimedTwiceInThePortableReceipt()
    {
        G.PlatformClaim claim = Proof().Claims[0];
        Assert.ThrowsExactly<InvalidDataException>(() => G.ValidatePlatformClaims([claim, claim], [Definition("Killed")], Report(), Candidate().Inputs));
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("schema")]
    [DataRow("common-inputs")]
    [DataRow("group")]
    [DataRow("platform")]
    [DataRow("runtime")]
    [DataRow("operating-system")]
    [DataRow("identity")]
    [DataRow("producer")]
    [DataRow("helper")]
    public void AProofHeaderCannotDowngradeTheSchemaOrClaimAnotherInputToolOrPlatformContext(string change)
    {
        G.Candidate candidate = Candidate();
        G.PlatformProof proof = Proof() with
        {
            CommonInputsSha256 = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(candidate.Inputs, Json))),
            Platform = "Microsoft Windows 10.0.control; X64; .NET 10.0.12",
            ExecutionIdentity = new JsonObject { ["operatingSystem"] = "windows", ["userSid"] = "user", ["ownerSid"] = "owner" }
        };
        proof = change switch
        {
            "schema" => proof with { Schema = "unknown" },
            "common-inputs" => proof with { CommonInputsSha256 = "stale" },
            "group" => proof with { Group = "pilot" },
            "platform" => proof with { Platform = "Ubuntu; X64; .NET 10.0.12" },
            "runtime" => proof with { Platform = "Microsoft Windows; X64; .NET 10.0.14" },
            "producer" => proof with { ProducerSha256 = "other" },
            "helper" => proof with { HelperSha256 = "other" },
            _ => proof
        };
        if (change == "operating-system") { proof.ExecutionIdentity["operatingSystem"] = "linux"; }
        if (change == "identity") { proof.ExecutionIdentity["ownerSid"] = null; }
        if (change == "valid") { G.ValidatePlatformProofHeader(proof, candidate, "checker-hash", "helper-hash"); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => G.ValidatePlatformProofHeader(proof, candidate, "checker-hash", "helper-hash")); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("tool-hash")]
    [DataRow("executable")]
    [DataRow("runtime")]
    [DataRow("cli")]
    [DataRow("override")]
    [DataRow("config")]
    [DataRow("output")]
    [DataRow("sdk-environment-missing")]
    [DataRow("sdk-environment-unpinned")]
    public void ForeignNativeCommandCannotChangePinnedRuntimeToolsOrHiddenSelection(string change)
    {
        const string msbuild = "C:\\subject\\sdk\\10.0.401\\MSBuild.dll";
        var tools = new G.Toolchain("cis.healthcheck.grouped-toolchain/v1", "Windows", "host", "C:\\subject\\dotnet.exe", "C:\\tool\\dotnet.exe",
            "10.0.401", "10.0.12", "8.0.31", [new("C:\\native\\Stryker.CLI.dll", "cli"), new("C:\\native\\Stryker.Core.dll", "core"),
                new(msbuild, new string('a', 64)), new("C:\\subject\\dotnet.exe", new string('b', 64))], []);
        string[] arguments = ["exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", "C:\\native\\Stryker.CLI.dll", "--config-file", "C:\\run\\profiles\\pipes.json", "--output", "C:\\run\\groups\\pipes\\native", "--skip-version-check"];
        if (change == "runtime") { arguments[2] = "8.0.32"; }
        if (change == "cli") { arguments[5] = "C:\\other\\Stryker.CLI.dll"; }
        if (change == "override") { arguments = [.. arguments, "--mutate", "Other.cs"]; }
        if (change == "config") { arguments[7] = "C:\\run\\profiles\\http.json"; }
        if (change == "output") { arguments[9] = "C:\\other\\groups\\pipes\\native"; }
        JsonObject run = new()
        {
            ["strykerCliSha256"] = "cli",
            ["strykerCoreSha256"] = change == "tool-hash" ? "other" : "core",
            ["environment"] = new JsonObject { ["MSBUILD_EXE_PATH"] = msbuild, ["PATH"] = "C:\\subject;C:\\Windows" },
            ["commandLog"] = new JsonArray(new JsonObject
            {
                ["name"] = "native",
                ["executable"] = change == "executable" ? tools.SubjectDotnet : tools.ToolDotnet,
                ["arguments"] = JsonSerializer.SerializeToNode(arguments)
            })
        };
        if (change == "sdk-environment-missing") { run["environment"]!.AsObject().Remove("MSBUILD_EXE_PATH"); }
        if (change == "sdk-environment-unpinned") { run["environment"]!["MSBUILD_EXE_PATH"] = "C:\\other\\sdk\\10.0.401\\MSBuild.dll"; }
        if (change == "valid") { G.ValidatePlatformNativeCommand(run, tools); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => G.ValidatePlatformNativeCommand(run, tools)); }
    }

    [TestMethod]
    [DataRow("valid-windows")]
    [DataRow("linux-identity-property")]
    [DataRow("cohort")]
    [DataRow("pilot")]
    [DataRow("environment")]
    [DataRow("execution-identity")]
    public void OnlyTheDeclaredPlatformConditionalCompilationPropertyMayDiffer(string change)
    {
        var group = new G.Group("pipes", [Source], [], "filter", "perTest");
        var properties = new Dictionary<string, string>(group.BuildProperties, StringComparer.Ordinal) { ["CisLinuxIdentityQualification"] = "false" };
        JsonObject identity = new() { ["operatingSystem"] = "windows", ["userSid"] = "S-1-5-21-control", ["ownerSid"] = "S-1-5-21-control" };
        JsonObject run = new() { ["buildProperties"] = JsonSerializer.SerializeToNode(properties, Json), ["environment"] = JsonSerializer.SerializeToNode(properties, Json), ["executionIdentity"] = identity.DeepClone() };
        if (change == "linux-identity-property") { run["buildProperties"]!["CisLinuxIdentityQualification"] = "true"; }
        if (change == "cohort") { run["buildProperties"]!["CisHealthcheckMutationCohort"] = "false"; }
        if (change == "pilot") { run["buildProperties"]!["CisHealthcheckPilotCohort"] = "true"; }
        if (change == "environment") { run["environment"]!["CisMutationTesting"] = "false"; }
        if (change == "execution-identity") { run["executionIdentity"]!["userSid"] = "different"; }
        if (change == "valid-windows") { G.ValidatePlatformContext(run, group, identity); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => G.ValidatePlatformContext(run, group, identity)); }
    }

    [TestMethod]
    [DataRow("valid-current-membership")]
    [DataRow("changed-input")]
    [DataRow("reordered-inputs")]
    [DataRow("wrong-producer")]
    [DataRow("wrong-original-digest")]
    [DataRow("missing-sid")]
    public void CompositePortableHeaderBindsCurrentMembershipWithoutAHistoricalCountOrRewritingItsDigest(string change)
    {
        G.Candidate anchor = Candidate();
        G.Candidate foreign = anchor with { Identity = "foreign-candidate", Platform = "Microsoft Windows 10.0.26100; X64; .NET 10.0.12", Host = "windows-host" };
        string digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(foreign.Inputs, new JsonSerializerOptions(Json) { NewLine = "\r\n" })));
        G.PlatformProof proof = Proof() with
        {
            CommonInputsSha256 = digest,
            Platform = foreign.Platform,
            ExecutionIdentity = new JsonObject { ["operatingSystem"] = "windows", ["userSid"] = "S-1-5-21-control", ["ownerSid"] = "S-1-5-21-control" }
        };
        if (change == "changed-input") { foreign = foreign with { Inputs = [foreign.Inputs[0] with { Sha256 = "changed" }, foreign.Inputs[1]] }; }
        if (change == "reordered-inputs") { foreign = foreign with { Inputs = foreign.Inputs.Reverse().ToArray() }; }
        if (change == "wrong-producer") { proof = proof with { ProducerSha256 = "caller-provided-other-checker" }; }
        if (change == "wrong-original-digest") { proof = proof with { CommonInputsSha256 = "caller-provided-digest" }; }
        if (change == "missing-sid") { proof.ExecutionIdentity.Remove("ownerSid"); }
        if (change == "valid-current-membership")
        {
            Assert.AreNotEqual(1580, anchor.Inputs.Length);
            G.ValidateCompositeWindowsHeader(proof, foreign, anchor, "checker-hash", "helper-hash");
            Assert.AreEqual(digest, proof.CommonInputsSha256);
        }
        else { Assert.ThrowsExactly<InvalidDataException>(() => G.ValidateCompositeWindowsHeader(proof, foreign, anchor, "checker-hash", "helper-hash")); }
    }

    [TestMethod]
    [DataRow("valid-reviewed-policy")]
    [DataRow("missing-unix")]
    [DataRow("missing-windows")]
    [DataRow("timeout-credit")]
    [DataRow("lower-threshold")]
    [DataRow("changed-source")]
    [DataRow("uncaptured-witness")]
    [DataRow("same-count-unix-role")]
    [DataRow("same-count-windows-role")]
    [DataRow("same-count-source-swap")]
    [DataRow("same-count-method-swap")]
    [DataRow("same-count-witness-swap")]
    [DataRow("same-count-witness-source-swap")]
    [DataRow("same-count-child-case-swap")]
    public void CompositePolicyRetainsAllCurrentSourceBoundObligationsAndConservativeRules(string change)
    {
        string language = LanguageRoot();
        JsonObject policy = JsonNode.Parse(File.ReadAllText(Path.Combine(language, "verification/mutation/healthcheck/composite-policy.json")))!.AsObject();
        JsonNode[] obligations = policy["linuxPermissionObligations"]!.AsArray().Concat(policy["windowsRequiredControls"]!.AsArray()).Select(row => row!).ToArray();
        string[] paths = obligations.Select(row => "src/CIS.Core/" + (string)row["source"]!)
            .Concat(obligations.SelectMany(row => row["allowedWitnesses"]!.AsArray()).Select(witness => (string)witness!["source"]!))
            .Distinct(StringComparer.Ordinal).ToArray();
        var candidate = Candidate() with
        {
            Inputs = paths.Select(path => new G.Input("implementations/csharp/" + path,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(language, path)))))).ToArray()
        };
        if (change == "missing-unix") { policy["linuxPermissionObligations"]!.AsArray().RemoveAt(0); }
        if (change == "missing-windows") { policy["windowsRequiredControls"]!.AsArray().RemoveAt(0); }
        if (change == "timeout-credit") { policy["timeoutKillCredit"] = true; }
        if (change == "lower-threshold") { policy["conservativeThreshold"] = 89; }
        if (change == "changed-source") { policy["linuxPermissionObligations"]![0]!["sourceSha256"] = "changed"; }
        if (change == "uncaptured-witness") { policy["windowsRequiredControls"]![0]!["allowedWitnesses"]![0]!["source"] = "tests/Other.cs"; }
        if (change == "same-count-unix-role") { policy["linuxPermissionObligations"]![0]!["obligation"] = "easierUnixControl"; }
        if (change == "same-count-windows-role") { policy["windowsRequiredControls"]![0]!["obligation"] = "easierWindowsControl"; }
        if (change == "same-count-source-swap")
        {
            JsonNode other = policy["linuxPermissionObligations"]![4]!;
            policy["linuxPermissionObligations"]![0]!["source"] = other["source"]!.DeepClone();
            policy["linuxPermissionObligations"]![0]!["sourceSha256"] = other["sourceSha256"]!.DeepClone();
        }
        if (change == "same-count-method-swap") { policy["windowsRequiredControls"]![0]!["location"] = policy["windowsRequiredControls"]![2]!["location"]!.DeepClone(); }
        if (change == "same-count-witness-swap") { policy["windowsRequiredControls"]![0]!["allowedWitnesses"] = policy["windowsRequiredControls"]![1]!["allowedWitnesses"]!.DeepClone(); }
        if (change == "same-count-witness-source-swap") { policy["windowsRequiredControls"]![0]!["allowedWitnesses"]![0]!["source"] = policy["windowsRequiredControls"]![1]!["allowedWitnesses"]![0]!["source"]!.DeepClone(); }
        if (change == "same-count-child-case-swap") { policy["childCases"]![0] = policy["childCases"]![2]!.DeepClone(); }
        string ReadSource(string relative) => File.ReadAllText(Path.Combine(language, relative["implementations/csharp/".Length..]));
        if (change == "valid-reviewed-policy") { G.ValidateCompositePolicy(candidate, policy, ReadSource); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => G.ValidateCompositePolicy(candidate, policy, ReadSource)); }
    }

    [TestMethod]
    [DataRow("distinct-contexts")]
    [DataRow("legacy-key-collision")]
    [DataRow("missing-context")]
    public void CompositeChildPassingContextsRemainDistinctFromRestorationHealth(string change)
    {
        // Shape admission only; these synthetic records claim no actual TRX execution.
        JsonObject baseline = new() { ["path"] = "/synthetic/baseline.trx", ["command"] = new JsonObject { ["name"] = "baseline-CIS.Core.IntegrationTests" } };
        JsonObject restored = new() { ["path"] = "/synthetic/restored.trx", ["command"] = new JsonObject { ["name"] = "restored-CIS.Core.IntegrationTests" } };
        JsonObject context = new() { ["restored"] = true, ["baselineContext"] = baseline, ["restoredContext"] = restored };
        if (change == "legacy-key-collision")
        {
            context.Remove("restoredContext"); context["restored"] = restored;
        }
        if (change == "missing-context") { context.Remove("restoredContext"); }
        if (change == "distinct-contexts")
        {
            Assert.AreSame(baseline, G.CompositeChildPassingContext(context, "baseline"));
            Assert.AreSame(restored, G.CompositeChildPassingContext(context, "restored"));
            Assert.IsTrue(context["restored"]!.GetValue<bool>());
        }
        else { Assert.ThrowsExactly<InvalidDataException>(() => G.CompositeChildPassingContext(context, "baseline")); }
    }

    private static string LanguageRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        { if (File.Exists(Path.Combine(directory.FullName, "CisLib.slnx"))) { return directory.FullName; } }
        throw new DirectoryNotFoundException("Run the maintained Tool tests inside the C# checkout.");
    }

    private static G.Candidate Candidate() => new("cis.healthcheck.grouped-candidate/v1", "candidate", "Ubuntu; X64; .NET 10.0.12", "host",
        [new("implementations/csharp/src/CIS.Core/" + Source, "source-hash"), new("implementations/csharp/" + WitnessSource, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(TestText))))], [Source], "archive-hash");
    private static G.Definition Definition(string status) => new(Source, "source-hash", "1", "definition", "Statement mutation", Location, ";", status);
    private static G.Review Review(string status) => new("candidate", "definition", status, "HC-PIPE-01", "PlatformCovered", "reviewer",
        "The exact source-bound critical obligation is demonstrated by its current real Windows native witness; chosen-lane outcome remains unchanged.", "2026-10-29T12:00:00Z")
    { PlatformProof = new("/owned/proof.json", "proof-hash") };
    private static G.PlatformProof Proof() => new("cis.healthcheck.platform-native-proof/v1", "foreign-candidate", "common-inputs", "Microsoft Windows; X64; .NET 10.0.12", "windows-host",
        new JsonObject { ["operatingSystem"] = "windows" }, "pipes", [], [], [new("definition", WitnessSource, WitnessName, "HC-PIPE-01")], "checker-hash", "helper-hash", DateTimeOffset.UtcNow);
    private static JsonObject Report() => new()
    {
        ["files"] = new JsonObject
        {
            [Source] = new JsonObject
            {
                ["source"] = "class Endpoint {}",
                ["mutants"] = new JsonArray(new JsonObject
                {
                    ["id"] = "1",
                    ["status"] = "Killed",
                    ["mutatorName"] = "Statement mutation",
                    ["replacement"] = ";",
                    ["location"] = JsonNode.Parse(Location),
                    ["killedBy"] = new JsonArray("test-id")
                })
            }
        },
        ["testFiles"] = new JsonObject { [WitnessSource] = new JsonObject { ["source"] = TestText, ["tests"] = new JsonArray(new JsonObject { ["id"] = "test-id", ["name"] = WitnessName }) } }
    };
}
