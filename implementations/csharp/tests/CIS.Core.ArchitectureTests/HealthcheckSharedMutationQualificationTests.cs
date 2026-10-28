using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Shared = CIS.Verification.HealthcheckSharedMutationModel;
using SharedConfiguration = CIS.Verification.HealthcheckSharedMutationConfiguration;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckSharedMutationQualificationTests
{
    [TestMethod]
    public void FortySixCurrentFilesBecomeThreePartitionsWithoutPoolingReportLocalIds()
    {
        var fixture = new Fixture(fullScope: true);
        string before = JsonSerializer.Serialize(fixture.Batches);

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, []);

        Assert.AreEqual(true, (bool?)result["complete"]);
        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(46L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(46L, (long?)result["aggregate"]!["denominator"]);
        JsonObject[] origins = Objects(result, "origins");
        Assert.HasCount(46, origins);
        Assert.IsTrue(origins.All(origin => (string?)origin["nativeId"] == "0"));
        Assert.AreEqual(46, origins.Select(origin => ((string?)origin["group"], (string?)origin["compositeId"])).Distinct().Count());
        Assert.AreEqual(41, origins.Count(origin => (string?)origin["group"] == "unit-runtime"));
        Assert.AreEqual(4, origins.Count(origin => (string?)origin["group"] == "pipes"));
        Assert.AreEqual(1, origins.Count(origin => (string?)origin["group"] == "aspnet"));
        foreach (JsonObject origin in origins)
        {
            Shared.Batch batch = fixture.Batches.Single(batch => batch.Source.Path == (string?)origin["sourcePath"]);
            Assert.AreEqual(batch.ReportSha256, (string?)origin["originalReportSha256"]);
            Assert.AreEqual(batch.Definitions[0].Identity, (string?)origin["definitionIdentity"]);
        }
        JsonObject[] partitions = Objects(result, "partitions");
        Assert.HasCount(3, partitions);
        foreach (JsonObject partition in partitions)
        {
            Assert.IsFalse(partition.ContainsKey("reportSha256"));
            Assert.IsFalse(partition.ContainsKey("originalReportSha256"));
            string digest = partition["partitionReceiptSha256"]!.GetValue<string>();
            Assert.AreEqual(64, digest.Length);
            Assert.IsFalse(fixture.Batches.Any(batch => batch.ReportSha256 == digest));
        }
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Batches), "Receiving must not mutate original native definitions or statuses.");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(41)]
    [DataRow(45)]
    public void AnyMissingSourceKeepsTheWholeUnionIncompleteWithoutPublishingAPartialScore(int missing)
    {
        var fixture = new Fixture(fullScope: true);
        Shared.Batch[] received = fixture.Batches.Where((_, index) => index != missing).ToArray();

        JsonObject result = Shared.Evaluate(fixture.Sources, received, []);

        Assert.AreEqual(false, (bool?)result["complete"]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.IsNull(result["aggregate"]);
        Assert.HasCount(1, result["missingSources"]!.AsArray());
        Assert.AreEqual(fixture.Sources[missing].Path, (string?)result["missingSources"]![0]);
    }

    [TestMethod]
    public void EmptyDeclarationFilesAddNeitherKillsNorDenominatorToACompleteScope()
    {
        var fixture = new Fixture();
        const string text = "public interface IEmptyHealthContract { }\n";
        var declaration = new Source("Healthcheck/IEmptyHealthContract.cs", Digest(text), "unit-runtime", text);
        Source[] sources = [.. fixture.Sources, declaration];
        Shared.Batch[] batches = [.. fixture.Batches, new(declaration, Digest("empty declaration report"), [], [], [], [])];

        JsonObject result = Shared.Evaluate(sources, batches, []);

        Assert.AreEqual(true, (bool?)result["complete"]);
        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(3L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(3L, (long?)result["aggregate"]!["denominator"]);
    }

    [TestMethod]
    public void AnEntirelyEmptyPopulationDoesNotBecomeOneHundredPercent()
    {
        var fixture = new Fixture();
        Shared.Batch[] batches = fixture.Batches.Select(batch => batch with { Definitions = [], VerifiedKilledIds = [] }).ToArray();

        JsonObject result = Shared.Evaluate(fixture.Sources, batches, []);

        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.AreEqual(false, (bool?)result["aggregate"]!["thresholdMet"]);
        Assert.AreEqual(0L, (long?)result["aggregate"]!["denominator"]);
        Assert.IsNull(result["aggregate"]!["percent"]);
    }

    [TestMethod]
    public void TheFullUnionMayPassWithABelowNinetyPartitionAndReviewedNoncriticalMisses()
    {
        var fixture = new Fixture(definitionsPerSource: 10);
        fixture.Status(0, 0, "Survived");
        fixture.Status(0, 1, "Survived");
        fixture.Status(1, 0, "NoCoverage");
        Shared.Review[] reviews = [fixture.Review(0, 0), fixture.Review(0, 1), fixture.Review(1, 0)];

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, reviews);

        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(27L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(90d, (double?)result["aggregate"]!["percent"]);
        Assert.AreEqual(2L, (long?)result["rawCounts"]!["Survived"]);
        Assert.AreEqual(1L, (long?)result["rawCounts"]!["NoCoverage"]);
        JsonObject unit = Objects(result, "partitions").Single(partition => (string?)partition["group"] == "unit-runtime");
        Assert.AreEqual(80d, (double?)unit["rawConservativePercent"]);
    }

    [TestMethod]
    [DataRow("Survived")]
    [DataRow("NoCoverage")]
    public void PassingArithmeticDoesNotDisposeAnUnreviewedMiss(string status)
    {
        var fixture = new Fixture(definitionsPerSource: 10);
        fixture.Status(0, 0, status);

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, []);

        Assert.AreEqual(true, (bool?)result["aggregate"]!["thresholdMet"]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.HasCount(1, result["unresolved"]!.AsArray());
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
    }

    [TestMethod]
    public void EquivalenceRemovesOnlyItsReviewedDenominatorEntryWithoutAddingAKill()
    {
        var fixture = new Fixture();
        fixture.Status(0, 0, "Survived");

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, [fixture.Review(0, 0, "Equivalent")]);

        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(2L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(2L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(1L, (long?)result["rawCounts"]!["Survived"]);
        Assert.AreEqual(1L, (long?)result["effectiveCounts"]!["ExcludedEquivalent"]);
    }

    [TestMethod]
    public void AnUnverifiedNativeKilledLabelCannotSupplyConservativeCredit()
    {
        var fixture = new Fixture();
        fixture.Batches[0] = fixture.Batches[0] with { VerifiedKilledIds = [] };

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, []);

        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.AreEqual(3L, (long?)result["rawCounts"]!["Killed"]);
        Assert.AreEqual(2L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(1L, (long?)result["effectiveCounts"]!["UnqualifiedKilled"]);
    }

    [TestMethod]
    public void VerifiedTimeoutRerunCreditIsSeparateFromImmutableRawCounts()
    {
        var fixture = new Fixture();
        fixture.Status(0, 0, "Timeout");
        fixture.Batches[0] = fixture.Batches[0] with { VerifiedTimeoutKilledIds = ["0"] };
        string original = JsonSerializer.Serialize(fixture.Batches);

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, []);

        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(2L, (long?)result["rawCounts"]!["Killed"]);
        Assert.AreEqual(1L, (long?)result["rawCounts"]!["Timeout"]);
        Assert.AreEqual(3L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(3L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(0L, (long?)result["effectiveCounts"]!["Timeout"]);
        Assert.AreEqual(original, JsonSerializer.Serialize(fixture.Batches));
    }

    [TestMethod]
    public void ConfirmedNonterminationResolvesOnlyTheDispositionWhileRetainingTheConservativeMiss()
    {
        var fixture = new Fixture(definitionsPerSource: 10);
        fixture.Status(0, 0, "Timeout");
        string original = JsonSerializer.Serialize(fixture.Batches);
        JsonObject before = Shared.Evaluate(fixture.Sources, fixture.Batches, []);

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, [fixture.Review(0, 0, "NonTermination")]);

        Assert.AreEqual(false, (bool?)before["qualified"]);
        Assert.HasCount(1, before["unresolved"]!.AsArray());
        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.HasCount(0, result["unresolved"]!.AsArray());
        Assert.AreEqual(29L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(1L, (long?)result["rawCounts"]!["Timeout"]);
        Assert.AreEqual(1L, (long?)result["effectiveCounts"]!["Timeout"]);
        Assert.AreEqual(30L, (long?)result["rawAggregate"]!["nativeDetected"]);
        Assert.IsTrue(JsonNode.DeepEquals(before["rawCounts"], result["rawCounts"]));
        Assert.IsTrue(JsonNode.DeepEquals(before["aggregate"], result["aggregate"]));
        Assert.HasCount(0, fixture.Batches[0].VerifiedTimeoutKilledIds);
        Assert.AreEqual(original, JsonSerializer.Serialize(fixture.Batches));
    }

    [TestMethod]
    public void ConfirmedNonterminationCannotLiftAConservativePopulationBelowNinety()
    {
        var fixture = new Fixture(definitionsPerSource: 10);
        for (int index = 0; index < 4; index++) { fixture.Status(0, index, "Timeout"); }
        Shared.Review[] reviews = Enumerable.Range(0, 4).Select(index => fixture.Review(0, index, "NonTermination")).ToArray();

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, reviews);

        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.AreEqual(false, (bool?)result["aggregate"]!["thresholdMet"]);
        Assert.AreEqual(26L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(4L, (long?)result["effectiveCounts"]!["Timeout"]);
        Assert.HasCount(0, result["unresolved"]!.AsArray());
    }

    [TestMethod]
    [DataRow("Killed")]
    [DataRow("Survived")]
    [DataRow("NoCoverage")]
    [DataRow("RuntimeError")]
    [DataRow("Pending")]
    [DataRow("CompileError")]
    [DataRow("Ignored")]
    public void NonterminationDispositionCanOnlyBindAnOriginalNativeTimeout(string status)
    {
        var fixture = new Fixture();
        fixture.Status(0, 0, status);

        Assert.ThrowsExactly<InvalidDataException>(() => Shared.Evaluate(fixture.Sources, fixture.Batches,
            [fixture.Review(0, 0, "NonTermination")]));
    }

    [TestMethod]
    [DataRow("stale-report")]
    [DataRow("changed-source")]
    [DataRow("changed-span")]
    [DataRow("duplicate")]
    [DataRow("conflicting-rerun")]
    public void NonterminationRequiresOneExactCurrentSourceAndOriginalReportBinding(string defect)
    {
        var fixture = new Fixture();
        fixture.Status(0, 0, "Timeout");
        Shared.Review review = fixture.Review(0, 0, "NonTermination");
        Definition definition = fixture.Batches[0].Definitions[0];
        Shared.Review[] reviews = defect switch
        {
            "stale-report" => [review with { OriginalReportSha256 = Digest("earlier timeout report") }],
            "changed-source" => [review with { DefinitionIdentity = (definition with { SourceSha256 = Digest("earlier source") }).Identity }],
            "changed-span" => [review with { DefinitionIdentity = (definition with { Start = definition.Start + 1 }).Identity }],
            "duplicate" => [review, review],
            "conflicting-rerun" => [review],
            _ => throw new InvalidOperationException("Unknown nontermination receiving control.")
        };
        if (defect == "conflicting-rerun") { fixture.Batches[0] = fixture.Batches[0] with { VerifiedTimeoutKilledIds = ["0"] }; }

        Assert.ThrowsExactly<InvalidDataException>(() => Shared.Evaluate(fixture.Sources, fixture.Batches, reviews));
    }

    [TestMethod]
    [DataRow("MissingSharedChildEvidence")]
    [DataRow("NativeCleanupFailed")]
    [DataRow("NativeExecutionAborted")]
    public void ConfirmedNonterminationCannotResolveExecutionCleanupOrChildEvidenceHolds(string code)
    {
        var fixture = new Fixture(definitionsPerSource: 10);
        fixture.Status(0, 0, "Timeout");
        fixture.Batches[0] = fixture.Batches[0] with { Issues = [new(code, "Unresolved original ownership failure.", ["0"])] };

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, [fixture.Review(0, 0, "NonTermination")]);

        Assert.AreEqual(true, (bool?)result["aggregate"]!["thresholdMet"]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.AreEqual(29L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
        Assert.IsTrue(Objects(result, "issues").Any(issue => (string?)issue["code"] == code));
        JsonObject hold = Objects(result, "receiverHolds").Single();
        Assert.AreEqual(code, (string?)hold["code"]);
        Assert.AreEqual("0", (string?)hold["nativeIds"]![0]);
    }

    [TestMethod]
    public void NonterminationReviewCannotReplaceMissingOriginalSourceEvidence()
    {
        var fixture = new Fixture();
        fixture.Status(0, 0, "Timeout");

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches[..^1], [fixture.Review(0, 0, "NonTermination")]);

        Assert.AreEqual(false, (bool?)result["complete"]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.IsNull(result["aggregate"]);
        Assert.HasCount(1, result["missingSources"]!.AsArray());
    }

    [TestMethod]
    [DataRow("MissingChildEvidence", false)]
    [DataRow("NativeCleanupFailed", false)]
    [DataRow("NativeExecutionAborted", true)]
    public void PassingScoresAndRefinementsNeverRemoveOriginalExecutionOrChildHolds(string code, bool refined)
    {
        var fixture = new Fixture();
        if (refined)
        {
            fixture.Status(0, 0, "Timeout");
            fixture.Batches[0] = fixture.Batches[0] with { VerifiedTimeoutKilledIds = ["0"] };
        }
        int index = code == "MissingChildEvidence" ? 1 : 0;
        fixture.Batches[index] = fixture.Batches[index] with
        {
            Issues = [new(code, "Synthetic unresolved execution boundary.", refined ? ["0"] : [])]
        };

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, []);

        Assert.AreEqual(true, (bool?)result["aggregate"]!["thresholdMet"]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.IsTrue(Objects(result, "issues").Any(issue => (string?)issue["code"] == code));
    }

    [TestMethod]
    [DataRow("duplicate-source")]
    [DataRow("foreign-source")]
    [DataRow("wrong-group")]
    [DataRow("stale-source")]
    [DataRow("foreign-definition")]
    [DataRow("duplicate-native-id")]
    [DataRow("bad-report-hash")]
    [DataRow("unknown-kill")]
    [DataRow("duplicate-kill")]
    [DataRow("kill-non-killed")]
    [DataRow("refine-non-timeout")]
    [DataRow("duplicate-refinement")]
    [DataRow("unknown-hold-id")]
    public void MalformedOrForeignEvidenceCannotEnterACompletePartition(string defect)
    {
        var fixture = new Fixture();
        Shared.Batch batch = fixture.Batches[0];
        switch (defect)
        {
            case "duplicate-source": fixture.Batches = [.. fixture.Batches, batch]; break;
            case "foreign-source": fixture.Batches[0] = batch with { Source = batch.Source with { Path = "Healthcheck/Foreign.cs" } }; break;
            case "wrong-group": fixture.Batches[0] = batch with { Source = batch.Source with { Group = "pipes" } }; break;
            case "stale-source": fixture.Batches[0] = batch with { Source = batch.Source with { Sha256 = Digest("old source") } }; break;
            case "foreign-definition": fixture.Batches[0] = batch with { Definitions = [batch.Definitions[0] with { SourcePath = "Healthcheck/Foreign.cs" }] }; break;
            case "duplicate-native-id": fixture.Batches[0] = batch with { Definitions = [batch.Definitions[0], batch.Definitions[0]] }; break;
            case "bad-report-hash": fixture.Batches[0] = batch with { ReportSha256 = "not-a-hash" }; break;
            case "unknown-kill": fixture.Batches[0] = batch with { VerifiedKilledIds = ["99"] }; break;
            case "duplicate-kill": fixture.Batches[0] = batch with { VerifiedKilledIds = ["0", "0"] }; break;
            case "kill-non-killed": fixture.Status(0, 0, "Survived"); fixture.Batches[0] = fixture.Batches[0] with { VerifiedKilledIds = ["0"] }; break;
            case "refine-non-timeout": fixture.Batches[0] = batch with { VerifiedTimeoutKilledIds = ["0"] }; break;
            case "duplicate-refinement": fixture.Status(0, 0, "Timeout"); fixture.Batches[0] = fixture.Batches[0] with { VerifiedTimeoutKilledIds = ["0", "0"] }; break;
            case "unknown-hold-id": fixture.Batches[0] = batch with { Issues = [new("NativeFailed", "Unknown selector.", ["99"])] }; break;
            default: throw new InvalidOperationException("Unknown receiving control.");
        }

        Assert.ThrowsExactly<InvalidDataException>(() => Shared.Evaluate(fixture.Sources, fixture.Batches, []));
    }

    [TestMethod]
    [DataRow("stale-report")]
    [DataRow("foreign-definition")]
    [DataRow("unsupported-kind")]
    [DataRow("duplicate-review")]
    public void DispositionsMustBindOneExactCurrentOriginalDefinition(string defect)
    {
        var fixture = new Fixture();
        fixture.Status(0, 0, "Survived");
        Shared.Review review = fixture.Review(0, 0);
        Shared.Review[] reviews = defect switch
        {
            "stale-report" => [review with { OriginalReportSha256 = Digest("another native report") }],
            "foreign-definition" => [review with { DefinitionIdentity = Digest("another definition") }],
            "unsupported-kind" => [review with { Kind = "AssertionKill" }],
            "duplicate-review" => [review, review with { Kind = "Equivalent" }],
            _ => throw new InvalidOperationException("Unknown disposition control.")
        };

        Assert.ThrowsExactly<InvalidDataException>(() => Shared.Evaluate(fixture.Sources, fixture.Batches, reviews));
    }

    [TestMethod]
    [DataRow("RuntimeError", true)]
    [DataRow("Pending", false)]
    public void RuntimeFailuresAndUnfinishedDefinitionsRemainUnqualifiedDespitePassingArithmetic(string status, bool complete)
    {
        var fixture = new Fixture(definitionsPerSource: 10);
        fixture.Status(0, 0, status);

        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, []);

        Assert.AreEqual(complete, (bool?)result["complete"]);
        Assert.AreEqual(true, (bool?)result["aggregate"]!["thresholdMet"]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.HasCount(1, result["unresolved"]!.AsArray());
        Assert.AreEqual(1L, (long?)result["rawCounts"]![status]);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("duplicate")]
    [DataRow("missing-group")]
    public void TheExpectedInventoryMustItselfBeCompleteAndUnambiguous(string defect)
    {
        var fixture = new Fixture();
        Source[] sources = defect switch
        {
            "empty" => [],
            "duplicate" => [.. fixture.Sources, fixture.Sources[0]],
            "missing-group" => fixture.Sources.Where(source => source.Group != "pipes").ToArray(),
            _ => throw new InvalidOperationException("Unknown inventory control.")
        };

        Assert.ThrowsExactly<InvalidDataException>(() => Shared.Evaluate(sources, [], []));
    }

    [TestMethod]
    [DataRow("unit-runtime", 2, 1000)]
    [DataRow("unit-runtime", 2, 15000)]
    [DataRow("unit-runtime", 4, 1000)]
    [DataRow("unit-runtime", 4, 15000)]
    [DataRow("unit-runtime", 8, 1000)]
    [DataRow("unit-runtime", 8, 15000)]
    [DataRow("pipes", 2, 1000)]
    [DataRow("pipes", 2, 15000)]
    [DataRow("pipes", 4, 1000)]
    [DataRow("pipes", 4, 15000)]
    [DataRow("pipes", 8, 1000)]
    [DataRow("pipes", 8, 15000)]
    [DataRow("aspnet", 2, 1000)]
    [DataRow("aspnet", 2, 15000)]
    [DataRow("aspnet", 4, 1000)]
    [DataRow("aspnet", 4, 15000)]
    [DataRow("aspnet", 8, 1000)]
    [DataRow("aspnet", 8, 15000)]
    public void ReviewedSharedExecutionDeltasPreserveTheActualMaintainedScopeAndCohort(string group, int workers, int budget)
    {
        var fixture = new ConfigurationFixture(group);
        fixture.Native["concurrency"] = workers;
        fixture.Native["additional-timeout"] = budget;
        string maintained = fixture.Maintained.ToJsonString(), native = fixture.Native.ToJsonString();

        SharedConfiguration.Validate(group, fixture.Native, fixture.Maintained, fixture.SelectedFiles);

        Assert.AreEqual(maintained, fixture.Maintained.ToJsonString());
        Assert.AreEqual(native, fixture.Native.ToJsonString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EitherTheWholeMaintainedMutateArrayOrTheExactSelectedFullFilesIsAccepted(bool wholeScope)
    {
        var fixture = new ConfigurationFixture("unit-runtime");
        if (wholeScope) { fixture.Native["mutate"] = fixture.Maintained["mutate"]!.DeepClone(); }

        SharedConfiguration.Validate("unit-runtime", fixture.Native, fixture.Maintained, fixture.SelectedFiles);
    }

    [TestMethod]
    [DataRow("perTest")]
    [DataRow("off")]
    [DataRow("omitted")]
    public void UnitCoverageUsesOnlyTheReviewedExplicitModesOrItsPerTestDefault(string mode)
    {
        var fixture = new ConfigurationFixture("unit-runtime");
        if (mode == "omitted") { fixture.Native.Remove("coverage-analysis"); }
        else { fixture.Native["coverage-analysis"] = mode; }

        SharedConfiguration.Validate("unit-runtime", fixture.Native, fixture.Maintained, fixture.SelectedFiles);
    }

    [TestMethod]
    [DataRow("unit-runtime", false)]
    [DataRow("unit-runtime", true)]
    [DataRow("pipes", false)]
    [DataRow("pipes", true)]
    [DataRow("aspnet", false)]
    [DataRow("aspnet", true)]
    public void NativeBailIntentIsAnExplicitBooleanWithoutChangingTheOtherPolicyFields(string group, bool disabled)
    {
        var fixture = new ConfigurationFixture(group);
        fixture.Native["disable-bail"] = disabled;

        SharedConfiguration.Validate(group, fixture.Native, fixture.Maintained, fixture.SelectedFiles);
    }

    [TestMethod]
    [DataRow("pipes", "perTest")]
    [DataRow("pipes", "omitted")]
    [DataRow("pipes", "null")]
    [DataRow("aspnet", "perTest")]
    [DataRow("aspnet", "omitted")]
    [DataRow("aspnet", "null")]
    public void ChildProcessGroupsCannotUseParentCoverageOrAnUnspecifiedCoverageMode(string group, string mode)
    {
        var fixture = new ConfigurationFixture(group);
        if (mode == "omitted") { fixture.Native.Remove("coverage-analysis"); }
        else { fixture.Native["coverage-analysis"] = mode == "null" ? null : JsonValue.Create(mode); }

        Assert.ThrowsExactly<InvalidDataException>(() => SharedConfiguration.Validate(group, fixture.Native, fixture.Maintained, fixture.SelectedFiles));
    }

    [TestMethod]
    [DataRow("ignore-methods")]
    [DataRow("ignored-mutations")]
    [DataRow("exclude")]
    [DataRow("since")]
    [DataRow("unknown-setting")]
    [DataRow("")]
    public void ExtraNativeFieldsCannotHideIgnoredDefinitionsOrChangeMutationSelection(string key)
    {
        var fixture = new ConfigurationFixture("unit-runtime");
        fixture.Native[key] = new JsonArray("*");

        Assert.ThrowsExactly<InvalidDataException>(() => SharedConfiguration.Validate("unit-runtime", fixture.Native, fixture.Maintained, fixture.SelectedFiles));
    }

    [TestMethod]
    [DataRow("source-range")]
    [DataRow("wildcard")]
    [DataRow("empty-scope")]
    [DataRow("foreign-scope")]
    [DataRow("duplicate-scope")]
    [DataRow("different-selected-file")]
    [DataRow("empty-selection")]
    [DataRow("duplicate-selection")]
    [DataRow("foreign-selection")]
    [DataRow("project")]
    [DataRow("test-projects")]
    [DataRow("filter")]
    [DataRow("level")]
    [DataRow("configuration")]
    [DataRow("framework")]
    [DataRow("runner")]
    [DataRow("threshold-high")]
    [DataRow("threshold-low")]
    [DataRow("threshold-break")]
    [DataRow("threshold-extra")]
    [DataRow("threshold-decimal")]
    [DataRow("workers")]
    [DataRow("workers-string")]
    [DataRow("workers-decimal")]
    [DataRow("budget")]
    [DataRow("budget-string")]
    [DataRow("budget-decimal")]
    [DataRow("mixing-enabled")]
    [DataRow("mixing-string")]
    [DataRow("mixing-missing")]
    [DataRow("bail-string")]
    [DataRow("bail-null")]
    [DataRow("bail-missing")]
    [DataRow("coverage-null")]
    [DataRow("coverage-unknown")]
    [DataRow("reporters-empty")]
    [DataRow("reporters-no-json")]
    [DataRow("reporters-duplicate")]
    [DataRow("reporters-html")]
    public void ProspectiveConfigurationCannotChangeRequiredPolicyOrUseMalformedNativeValues(string defect)
    {
        var fixture = new ConfigurationFixture("unit-runtime");
        JsonObject native = fixture.Native;
        switch (defect)
        {
            case "source-range": native["mutate"] = new JsonArray(fixture.SelectedFiles[0] + "{1..5}"); break;
            case "wildcard": native["mutate"] = new JsonArray("Healthcheck/**/*.cs"); break;
            case "empty-scope": native["mutate"] = new JsonArray(); break;
            case "foreign-scope": native["mutate"] = new JsonArray("Healthcheck/Other.cs"); break;
            case "duplicate-scope": native["mutate"] = new JsonArray(fixture.SelectedFiles[0], fixture.SelectedFiles[0]); break;
            case "different-selected-file": native["mutate"] = new JsonArray(fixture.Maintained["mutate"]![1]!.GetValue<string>()); break;
            case "empty-selection": fixture.SelectedFiles = []; break;
            case "duplicate-selection": fixture.SelectedFiles = [fixture.SelectedFiles[0], fixture.SelectedFiles[0]]; break;
            case "foreign-selection": fixture.SelectedFiles = ["Healthcheck/Other.cs"]; break;
            case "project": native["project"] = "Other.csproj"; break;
            case "test-projects": native["test-projects"] = new JsonArray(); break;
            case "filter": native["test-case-filter"] = "FullyQualifiedName~OneConvenientTest"; break;
            case "level": native["mutation-level"] = "Basic"; break;
            case "configuration": native["configuration"] = "Debug"; break;
            case "framework": native["target-framework"] = "net9.0"; break;
            case "runner": native["test-runner"] = "mtp"; break;
            case "threshold-high": native["thresholds"]!["high"] = 99; break;
            case "threshold-low": native["thresholds"]!["low"] = 89; break;
            case "threshold-break": native["thresholds"]!["break"] = 89; break;
            case "threshold-extra": native["thresholds"]!["extra"] = 90; break;
            case "threshold-decimal": native["thresholds"]!["break"] = JsonNode.Parse("90.0"); break;
            case "workers": native["concurrency"] = 3; break;
            case "workers-string": native["concurrency"] = "4"; break;
            case "workers-decimal": native["concurrency"] = JsonNode.Parse("4.0"); break;
            case "budget": native["additional-timeout"] = 10000; break;
            case "budget-string": native["additional-timeout"] = "15000"; break;
            case "budget-decimal": native["additional-timeout"] = JsonNode.Parse("15000.0"); break;
            case "mixing-enabled": native["disable-mix-mutants"] = false; break;
            case "mixing-string": native["disable-mix-mutants"] = "true"; break;
            case "mixing-missing": native.Remove("disable-mix-mutants"); break;
            case "bail-string": native["disable-bail"] = "false"; break;
            case "bail-null": native["disable-bail"] = null; break;
            case "bail-missing": native.Remove("disable-bail"); break;
            case "coverage-null": native["coverage-analysis"] = null; break;
            case "coverage-unknown": native["coverage-analysis"] = "all"; break;
            case "reporters-empty": native["reporters"] = new JsonArray(); break;
            case "reporters-no-json": native["reporters"] = new JsonArray("progress"); break;
            case "reporters-duplicate": native["reporters"] = new JsonArray("json", "json"); break;
            case "reporters-html": native["reporters"] = new JsonArray("json", "html"); break;
            default: throw new InvalidOperationException("Unknown native configuration control.");
        }
        string maintained = fixture.Maintained.ToJsonString(), supplied = native.ToJsonString();

        Assert.ThrowsExactly<InvalidDataException>(() => SharedConfiguration.Validate("unit-runtime", native, fixture.Maintained, fixture.SelectedFiles));

        Assert.AreEqual(maintained, fixture.Maintained.ToJsonString());
        Assert.AreEqual(supplied, native.ToJsonString());
    }

    private static JsonObject[] Objects(JsonObject document, string property) => document[property]!.AsArray().Select(node => node!.AsObject()).ToArray();
    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed class ConfigurationFixture
    {
        internal JsonObject Maintained { get; }
        internal JsonObject Native { get; }
        internal string[] SelectedFiles { get; set; }

        internal ConfigurationFixture(string group)
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            string relative = Path.Combine("planning", "healthcheck", "qualification", "gate-closure", group + ".json");
            while (root is not null && !File.Exists(Path.Combine(root.FullName, relative))) { root = root.Parent; }
            Assert.IsNotNull(root, "The actual maintained qualification configuration must be available in this checkout.");
            JsonObject document = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root.FullName, relative)))!.AsObject();
            Maintained = document["stryker-config"]!.AsObject();
            Native = (JsonObject)Maintained.DeepClone();
            SelectedFiles = [Maintained["mutate"]![0]!.GetValue<string>()];
            Native["mutate"] = new JsonArray(SelectedFiles[0]);
            Native["concurrency"] = 4;
            Native["additional-timeout"] = 15000;
            Native["reporters"] = new JsonArray("json", "dots");
            Native["disable-bail"] = false;
            Native["disable-mix-mutants"] = true;
            Native["coverage-analysis"] = group == "unit-runtime" ? "perTest" : "off";
        }
    }

    private sealed class Fixture
    {
        internal Source[] Sources { get; }
        internal Shared.Batch[] Batches { get; set; }

        internal Fixture(bool fullScope = false, int definitionsPerSource = 1)
        {
            var sources = new List<Source>();
            var batches = new List<Shared.Batch>();
            foreach (string group in Groups)
            {
                int count = fullScope ? group == "unit-runtime" ? 41 : group == "pipes" ? 4 : 1 : 1;
                for (int index = 0; index < count; index++)
                {
                    string suffix = index.ToString("D2", CultureInfo.InvariantCulture);
                    string folder = group == "unit-runtime" ? "Unit" : group == "pipes" ? "Pipes/Pipe" : "AspNetCore/Http";
                    string path = "Healthcheck/" + folder + suffix + ".cs";
                    string text = "public sealed class Probe" + sources.Count.ToString(CultureInfo.InvariantCulture) + " {\n"
                        + string.Concat(Enumerable.Range(0, definitionsPerSource).Select(number => "public bool Value" + number.ToString(CultureInfo.InvariantCulture) + " => false;\n")) + "}\n";
                    var source = new Source(path, Digest(text), group, text);
                    var definitions = new List<Definition>();
                    int offset = 0;
                    for (int number = 0; number < definitionsPerSource; number++)
                    {
                        int start = text.IndexOf("false", offset, StringComparison.Ordinal);
                        definitions.Add(new(number.ToString(CultureInfo.InvariantCulture), path, source.Sha256,
                            start, start + 5, "Boolean mutation", "false", "true", "Killed", ["8069d9ee-a037-48f7-b74c-e73c65300551"]));
                        offset = start + 5;
                    }
                    sources.Add(source);
                    batches.Add(new(source, Digest("original native report: " + path), definitions.ToArray(), definitions.Select(definition => definition.Id).ToArray(), [], []));
                }
            }
            Sources = sources.ToArray();
            Batches = batches.ToArray();
        }

        internal void Status(int batchIndex, int definitionIndex, string status)
        {
            Shared.Batch batch = Batches[batchIndex];
            Definition[] definitions = batch.Definitions.ToArray();
            definitions[definitionIndex] = definitions[definitionIndex] with { Status = status, KilledBy = [] };
            Batches[batchIndex] = batch with
            {
                Definitions = definitions,
                VerifiedKilledIds = definitions.Where(definition => definition.Status == "Killed").Select(definition => definition.Id).ToArray()
            };
        }

        internal Shared.Review Review(int batchIndex, int definitionIndex, string kind = "ReviewedNoncritical") =>
            new(Batches[batchIndex].Definitions[definitionIndex].Identity, Batches[batchIndex].ReportSha256, kind);
    }
}
