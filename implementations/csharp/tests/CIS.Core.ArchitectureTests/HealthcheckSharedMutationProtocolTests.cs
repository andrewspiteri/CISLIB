using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Protocol = CIS.Verification.HealthcheckSharedMutationProtocol;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckSharedMutationProtocolTests
{
    [TestMethod]
    public void ACompleteReceiptMayHaveAFalsePerProfileThresholdWithoutLosingItsCurrentInputBindings()
    {
        var fixture = new Fixture();
        string before = fixture.Receipt.ToJsonString();

        Protocol.Input[] inputs = fixture.Validate();

        Assert.IsTrue(inputs.Single(input => input.Path == Fixture.SourcePath).CompareCurrent);
        Assert.IsTrue(inputs.Single(input => input.Path == "tests/Selected.cs").CompareCurrent);
        Assert.IsFalse(inputs.Single(input => input.Path == fixture.TestingPath).CompareCurrent);
        Assert.AreEqual(before, fixture.Receipt.ToJsonString());
    }

    [TestMethod]
    [DataRow("scope")]
    [DataRow("scope-type")]
    [DataRow("approval")]
    [DataRow("unknown-flag")]
    [DataRow("profile-hash")]
    [DataRow("profile-path")]
    [DataRow("old-receipt")]
    [DataRow("environment-hash")]
    [DataRow("empty-roles")]
    [DataRow("unknown-role")]
    [DataRow("duplicate-role")]
    [DataRow("source-role-removed")]
    [DataRow("source-absolute")]
    [DataRow("producer-relative")]
    [DataRow("missing-configuration")]
    [DataRow("missing-producer")]
    [DataRow("closure-index")]
    [DataRow("closure-role")]
    [DataRow("closure-empty-tests")]
    [DataRow("unknown-batch-flag")]
    [DataRow("full-file")]
    [DataRow("postflight")]
    [DataRow("raw-postflight")]
    [DataRow("zero-unit-cases")]
    [DataRow("report-profile")]
    [DataRow("report-role")]
    [DataRow("report-bytes")]
    [DataRow("missing-execution")]
    [DataRow("unknown-execution")]
    [DataRow("duplicate-execution")]
    [DataRow("truncated")]
    [DataRow("execution-error")]
    [DataRow("execution-timeout")]
    [DataRow("mutation-exit")]
    [DataRow("recovery-exit")]
    [DataRow("unknown-helper")]
    [DataRow("raw-counts")]
    [DataRow("raw-score")]
    public void MalformedScopeRoleExecutionAndArtifactReceiptsAreRejected(string defect)
    {
        var fixture = new Fixture();
        fixture.Validate();
        JsonObject firstInput = fixture.Inputs[0]!.AsObject();
        JsonObject execution = fixture.Batch["executions"]![0]!.AsObject();
        switch (defect)
        {
            case "scope": fixture.Receipt["scopeComplete"] = false; break;
            case "scope-type": fixture.Receipt["scopeComplete"] = "true"; break;
            case "approval": fixture.Receipt["qualified"] = true; break;
            case "unknown-flag": fixture.Receipt["approved"] = true; break;
            case "profile-hash": fixture.Receipt["profileSha256"] = Digest("stale"); break;
            case "profile-path": fixture.Receipt["profilePath"] = "profiles/another.json"; break;
            case "old-receipt": fixture.Receipt["verifiedUtc"] = fixture.Now.AddMinutes(-5).ToString("O", CultureInfo.InvariantCulture); break;
            case "environment-hash": fixture.Receipt["environmentSha256"] = "unknown"; break;
            case "empty-roles": firstInput["roles"] = new JsonArray(); break;
            case "unknown-role": firstInput["roles"] = new JsonArray("trusted"); break;
            case "duplicate-role": firstInput["roles"] = new JsonArray("source", "source"); break;
            case "source-role-removed": firstInput["roles"] = new JsonArray("tool"); break;
            case "source-absolute": firstInput["path"] = Path.Combine(fixture.Snapshot, "source.cs"); break;
            case "producer-relative": fixture.Inputs[5]!["path"] = "tooling/Cis.Modules.Testing.dll"; break;
            case "missing-configuration": fixture.Inputs.RemoveAt(2); break;
            case "missing-producer": fixture.Inputs.RemoveAt(5); break;
            case "closure-index": fixture.Receipt["closures"]![0]!["testInputIndices"] = new JsonArray(999); break;
            case "closure-role": fixture.Receipt["closures"]![0]!["testInputIndices"] = new JsonArray(0); break;
            case "closure-empty-tests": fixture.Receipt["closures"]![0]!["testInputIndices"] = new JsonArray(); break;
            case "unknown-batch-flag": fixture.Batch["approved"] = true; break;
            case "full-file": fixture.Batch["fullFile"] = false; break;
            case "postflight": fixture.Batch["fullPostflightReceiptVerified"] = false; break;
            case "raw-postflight": fixture.Batch["rawPostflightRetained"] = true; break;
            case "zero-unit-cases": fixture.Batch["unitPassed"] = 0; break;
            case "report-profile": fixture.Batch["mutationReportPath"] = ".cis/local/testing/incremental/other/reports/a-mutation.json"; break;
            case "report-role": fixture.Batch["unitReportPath"] = fixture.Batch["mutationReportPath"]!.GetValue<string>(); break;
            case "report-bytes": fixture.Batch["unitReportBytes"] = 0; break;
            case "missing-execution": fixture.Batch["executions"]!.AsArray().RemoveAt(0); break;
            case "unknown-execution": execution["role"] = "approved"; break;
            case "duplicate-execution": fixture.Batch["executions"]![1]!["role"] = "unit"; break;
            case "truncated": execution["outputTruncated"] = true; break;
            case "execution-error": execution["hasError"] = true; break;
            case "execution-timeout": execution["status"] = "timed-out"; break;
            case "mutation-exit": fixture.Batch["executions"]![1]!["exitCode"] = 4; break;
            case "recovery-exit": fixture.Batch["executions"]![2]!["exitCode"] = 2; break;
            case "unknown-helper": execution["helperProducerSha256"] = Digest("unrequested helper"); break;
            case "raw-counts": fixture.Batch["raw"]!["killed"] = 2; break;
            case "raw-score": fixture.Batch["raw"]!["score"] = 90; break;
            default: throw new InvalidOperationException("Unknown shared protocol control.");
        }

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate());
    }

    [TestMethod]
    public void AnExplicitRefinementBindsBothRawReportsAndTheCurrentOriginalBaseline()
    {
        var fixture = new Fixture();
        fixture.AddRefinement();

        fixture.Validate();

        Assert.HasCount(1, fixture.Receipt["credits"]!.AsArray());
        Assert.AreEqual(1, (int?)fixture.Receipt["raw"]!["timedOut"]);
        Assert.AreEqual(1, (int?)fixture.Receipt["effective"]!["killed"]);
    }

    [TestMethod]
    [DataRow("unrequested")]
    [DataRow("stale-original")]
    [DataRow("stale-rerun")]
    [DataRow("stale-stage")]
    [DataRow("stale-baseline")]
    [DataRow("missing-report-role")]
    [DataRow("unknown-report-role")]
    [DataRow("duplicate-credit")]
    [DataRow("foreign-credit")]
    [DataRow("missing-killing-test")]
    [DataRow("changed-killing-definition")]
    public void StaleOrUnrequestedRefinementReceiptsCannotSupplyCredit(string defect)
    {
        var fixture = new Fixture();
        fixture.AddRefinement();
        fixture.Validate();
        JsonObject refinement = fixture.Receipt["refinements"]![0]!.AsObject();
        JsonObject binding = refinement["bindings"]![0]!.AsObject();
        JsonObject credit = fixture.Receipt["credits"]![0]!.AsObject();
        switch (defect)
        {
            case "unrequested": fixture.Profiles.RemoveAt(1); break;
            case "stale-original": binding["originalReportSha256"] = Digest("earlier original"); break;
            case "stale-rerun": binding["rerunReportSha256"] = Digest("earlier rerun"); break;
            case "stale-stage": binding["originalStageSha256"] = Digest("earlier stage"); break;
            case "stale-baseline": refinement["baselineBindings"]![0]!["reportSha256"] = Digest("earlier baseline"); break;
            case "missing-report-role": refinement["rerunReports"]!.AsArray().RemoveAt(0); break;
            case "unknown-report-role": refinement["rerunReports"]![0]!["role"] = "accepted"; break;
            case "duplicate-credit": fixture.Receipt["credits"]!.AsArray().Add(credit.DeepClone()); break;
            case "foreign-credit": credit["sourceFile"] = "other.cs"; break;
            case "missing-killing-test": credit["killingTestIds"] = new JsonArray(); break;
            case "changed-killing-definition": credit["killingTestDefinitionSha256"] = new JsonArray("not-a-hash"); break;
            default: throw new InvalidOperationException("Unknown refinement receiving control.");
        }

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate());
    }

    [TestMethod]
    [DataRow("RuntimeError")]
    [DataRow("Pending")]
    [DataRow("Unknown")]
    public void CompletedReceiptCountsCannotConcealNativeInfrastructureOrUnfinishedWork(string status)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Protocol.ValidateRawPopulation(Counts(1, 0), ["Killed", status]));
    }

    [TestMethod]
    public void PinnedRawPopulationMustAgreeWithReceiptCountsWhileIgnoredAndCompileErrorsRemainVisible()
    {
        Protocol.ValidateRawPopulation(Counts(1, 0), ["Killed", "Ignored", "CompileError"]);
        Assert.ThrowsExactly<InvalidDataException>(() => Protocol.ValidateRawPopulation(Counts(1, 0), ["Survived"]));
    }

    [TestMethod]
    [DataRow("{\"scopeComplete\":true,\"scopeComplete\":false}")]
    [DataRow("{\"inputs\":[{\"roles\":[],\"roles\":[\"tool\"]}]}")]
    public void DuplicateJsonKeysAreRejectedBeforeAnyProtocolFieldIsRead(string text)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Protocol.Parse(text));
    }

    [TestMethod]
    public void NativeProcessExitMustAgreeWithTheSharedReceiptAndFailureRemainsUnaccepted()
    {
        var fixture = new Fixture();
        Assert.IsTrue(Protocol.IsVerifiedResponse(fixture.Receipt, 0));
        Assert.ThrowsExactly<InvalidDataException>(() => Protocol.IsVerifiedResponse(fixture.Receipt, 4));
        fixture.Receipt["status"] = "verification-failed";
        fixture.Receipt["exitCode"] = 4;
        fixture.Receipt["scopeComplete"] = false;
        Assert.IsFalse(Protocol.IsVerifiedResponse(fixture.Receipt, 4));
    }

    [TestMethod]
    public void TheCreditDefinitionDigestUsesCurrentNativeIdSpanAndSourceWithoutNormalizingLineEndings()
    {
        const string text = "a false\r\n";
        var source = new Source("Healthcheck/Example.cs", Digest(text), "unit-runtime", text);
        var definition = new Definition("0", source.Path, source.Sha256, 2, 7, "Boolean mutation", "false", "true", "Timeout", []);
        string expected = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            source = text,
            id = "0",
            mutatorName = "Boolean mutation",
            replacement = "true",
            location = new { startLine = 1, startColumn = 3, endLine = 1, endColumn = 8 }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })));

        Assert.AreEqual(expected, Protocol.DefinitionSha256(source, definition));
        Assert.AreNotEqual(expected, Protocol.DefinitionSha256(source, definition with { Id = "1" }));
        Assert.AreNotEqual(expected, Protocol.DefinitionSha256(source, definition with { Replacement = ";" }));
        Assert.ThrowsExactly<InvalidDataException>(() => Protocol.DefinitionSha256(source, definition with { SourceSha256 = Digest("old source") }));
    }

    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static JsonObject Counts(int killed, int timeout) => new()
    {
        ["killed"] = killed,
        ["survived"] = 0,
        ["timedOut"] = timeout,
        ["noCoverage"] = 0,
        ["denominator"] = killed + timeout,
        ["score"] = killed * 100d / (killed + timeout)
    };

    private sealed class Fixture
    {
        internal const string SourcePath = "implementations/csharp/src/CIS.Core/Healthcheck/HealthCollectorRunner.cs";
        internal DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
        internal string Snapshot { get; } = Path.Combine(Path.GetTempPath(), "hc-protocol-controls", "snapshot");
        internal string TestingPath { get; } = Path.Combine(Path.GetTempPath(), "hc-protocol-controls", "tools", "Cis.Modules.Testing.dll");
        internal string HelperPath { get; } = Path.Combine(Path.GetTempPath(), "hc-protocol-controls", "tools", "Cis.Abstractions.dll");
        internal List<Protocol.Profile> Profiles { get; } = [];
        internal JsonObject Receipt { get; }
        internal JsonObject Batch => Receipt["batches"]![0]!.AsObject();
        internal JsonArray Inputs => Receipt["inputs"]!.AsArray();

        internal Fixture()
        {
            string tool = Path.Combine(Path.GetTempPath(), "hc-protocol-controls", "tools", "dotnet");
            JsonObject profile = new()
            {
                ["id"] = "original",
                ["sourceFiles"] = new JsonArray(SourcePath),
                ["testFiles"] = new JsonArray("tests/Selected.cs"),
                ["configFiles"] = new JsonArray("build.props"),
                ["toolFiles"] = new JsonArray(tool),
                ["fullTestFilter"] = "FullyQualifiedName~Healthcheck"
            };
            Profiles.Add(new("profiles/original.json", Digest("original profile"), profile));
            var batch = new JsonObject
            {
                ["sourceFile"] = SourcePath,
                ["closureId"] = Digest("closure"),
                ["unitFingerprint"] = Digest("unit fingerprint"),
                ["mutationFingerprint"] = Digest("mutation fingerprint"),
                ["testFilterSha256"] = Digest("filter"),
                ["fullFile"] = true,
                ["unitReportPath"] = ReportPath("original", "unit"),
                ["unitReportSha256"] = Digest("unit report"),
                ["unitReportBytes"] = 123,
                ["unitStageSha256"] = Digest("unit stage"),
                ["unitFinishedUtc"] = Now.AddSeconds(-1).ToString("O", CultureInfo.InvariantCulture),
                ["unitPassed"] = 1,
                ["mutationReportPath"] = ReportPath("original", "mutation"),
                ["mutationReportSha256"] = Digest("mutation report"),
                ["mutationReportBytes"] = 123,
                ["mutationStageSha256"] = Digest("mutation stage"),
                ["raw"] = Counts(1, 0),
                ["executions"] = Executions("unit", "mutation", "recovery", "postflight"),
                ["fullPostflightReceiptVerified"] = true,
                ["rawPostflightRetained"] = false
            };
            Receipt = new JsonObject
            {
                ["status"] = "verified",
                ["exitCode"] = 0,
                ["schemaVersion"] = 1,
                ["policy"] = "cis-incremental-evidence-v1",
                ["qualified"] = false,
                ["profileId"] = "original",
                ["profilePath"] = Profiles[0].Path,
                ["profileSha256"] = Profiles[0].Sha256,
                ["graphBuildId"] = "current-graph",
                ["runtimeIdentifier"] = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64",
                ["osDescription"] = "Synthetic native lane",
                ["environmentSha256"] = Digest("environment"),
                ["testingProducerSha256"] = Digest("testing producer"),
                ["helperProducerSha256"] = Digest("helper producer"),
                ["verifiedUtc"] = Now.ToString("O", CultureInfo.InvariantCulture),
                ["scopeComplete"] = true,
                ["thresholdMet"] = false,
                ["raw"] = Counts(1, 0),
                ["effective"] = Counts(1, 0),
                ["inputs"] = new JsonArray(Input(SourcePath, "source", "dependency"), Input("tests/Selected.cs", "test"), Input("build.props", "configuration"),
                    Input(Profiles[0].Path, "profile"), Input(tool, "tool"), Input(TestingPath, "producer"), Input(HelperPath, "producer")),
                ["closures"] = new JsonArray(new JsonObject { ["id"] = Digest("closure"), ["testInputIndices"] = new JsonArray(1), ["dependencyInputIndices"] = new JsonArray(0) }),
                ["batches"] = new JsonArray(batch),
                ["refinements"] = new JsonArray(),
                ["credits"] = new JsonArray(),
                ["diagnostics"] = new JsonArray("Current declared evidence.")
            };
            Inputs[3]!["sha256"] = Profiles[0].Sha256;
            Inputs[5]!["sha256"] = Digest("testing producer");
            Inputs[6]!["sha256"] = Digest("helper producer");
        }

        internal Protocol.Input[] Validate() => Protocol.Validate(Protocol.Parse(Receipt.ToJsonString()), Profiles.ToArray(), Snapshot, TestingPath, Digest("testing producer"),
            HelperPath, Digest("helper producer"), OperatingSystem.IsWindows() ? "windows" : "ubuntu", Now.AddSeconds(-1), Now.AddSeconds(1));

        internal void AddRefinement()
        {
            JsonObject profile = (JsonObject)Profiles[0].Value.DeepClone(); profile["id"] = "rerun";
            var rerun = new Protocol.Profile("profiles/rerun.json", Digest("rerun profile"), profile); Profiles.Add(rerun);
            JsonObject input = Input(rerun.Path, "profile"); input["sha256"] = rerun.Sha256; Inputs.Add(input);
            Receipt["raw"] = Counts(0, 1); Batch["raw"] = Counts(0, 1);
            var binding = new JsonObject
            {
                ["sourceFile"] = SourcePath,
                ["originalReportPath"] = Batch["mutationReportPath"]!.DeepClone(),
                ["originalReportSha256"] = Batch["mutationReportSha256"]!.DeepClone(),
                ["originalFingerprint"] = Batch["mutationFingerprint"]!.DeepClone(),
                ["originalStageSha256"] = Batch["mutationStageSha256"]!.DeepClone(),
                ["rerunReportPath"] = ReportPath("rerun", "mutation"),
                ["rerunReportSha256"] = Digest("rerun mutation report"),
                ["rerunFingerprint"] = Digest("rerun mutation fingerprint"),
                ["rerunStageSha256"] = Digest("rerun mutation stage")
            };
            var baseline = new JsonObject
            {
                ["sourceFile"] = SourcePath,
                ["reportPath"] = Batch["unitReportPath"]!.DeepClone(),
                ["reportSha256"] = Batch["unitReportSha256"]!.DeepClone(),
                ["fingerprint"] = Batch["unitFingerprint"]!.DeepClone(),
                ["finishedUtc"] = Batch["unitFinishedUtc"]!.DeepClone()
            };
            Receipt["refinements"]!.AsArray().Add(new JsonObject
            {
                ["rerunProfilePath"] = rerun.Path,
                ["rerunProfileSha256"] = rerun.Sha256,
                ["bindings"] = new JsonArray(binding),
                ["baselineBindings"] = new JsonArray(baseline),
                ["rerunReports"] = new JsonArray(RerunReport("unit"), RerunReport("mutation"))
            });
            Receipt["credits"]!.AsArray().Add(new JsonObject
            {
                ["sourceFile"] = SourcePath,
                ["mutantId"] = "0",
                ["definitionSha256"] = Digest("definition"),
                ["killingTestIds"] = new JsonArray("8069d9ee-a037-48f7-b74c-e73c65300551"),
                ["killingTestDefinitionSha256"] = new JsonArray(Digest("test definition"))
            });
        }

        private JsonObject RerunReport(string role) => new()
        {
            ["sourceFile"] = SourcePath,
            ["role"] = role,
            ["path"] = ReportPath("rerun", role),
            ["sha256"] = Digest("rerun " + role + " report"),
            ["bytes"] = 123,
            ["fingerprint"] = Digest("rerun " + role + " fingerprint"),
            ["stageSha256"] = Digest("rerun " + role + " stage"),
            ["finishedUtc"] = Now.AddSeconds(-2).ToString("O", CultureInfo.InvariantCulture),
            ["executions"] = role == "unit" ? Executions("unit") : Executions("mutation", "recovery", "postflight")
        };
        private static JsonObject Input(string path, params string[] roles) => new()
        {
            ["path"] = path,
            ["sha256"] = Digest(path),
            ["roles"] = new JsonArray(roles.Select(role => (JsonNode?)JsonValue.Create(role)).ToArray())
        };
        private static JsonArray Executions(params string[] roles) => new(roles.Select(role => (JsonNode)new JsonObject
        {
            ["role"] = role,
            ["status"] = "exited",
            ["exitCode"] = role == "mutation" ? 2 : 0,
            ["durationMilliseconds"] = 1,
            ["commandSha256"] = Digest("command: " + role),
            ["helperProducerSha256"] = Digest("helper producer"),
            ["outputTruncated"] = false,
            ["hasError"] = false
        }).ToArray());
        private static string ReportPath(string profile, string role) => ".cis/local/testing/incremental/" + profile + "/reports/a-" + (role == "unit" ? "unit.xml" : "mutation.json");
    }
}
