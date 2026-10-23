using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationUnionQualificationTests
{
    [TestMethod]
    public void CompletedBeforeAbortCreditDefaultsToEmptyAndDeduplicatesTheOtherWitnessCategories()
    {
        var legacy = new NativeAuditResult(["296"], [], 1, 1, new JsonObject());
        Assert.HasCount(0, legacy.CompletedBeforeAbortKilledIds);
        CollectionAssert.AreEqual(new[] { "296" }, legacy.QualifiedKilledIds);

        NativeAuditResult repeated = legacy with
        {
            ReviewedSemanticKilledIds = ["296"],
            CompletedBeforeAbortKilledIds = ["296"]
        };
        CollectionAssert.AreEqual(new[] { "296" }, repeated.QualifiedKilledIds);
        CollectionAssert.AreEqual(legacy.AssertionKilledIds, repeated.AssertionKilledIds);
        Assert.AreEqual(legacy.HostCount, repeated.HostCount);
        Assert.AreEqual(legacy.NormalHostCount, repeated.NormalHostCount);
    }

    [TestMethod]
    [DataRow("NativeHostAborted")]
    [DataRow("NativeCompletionAborted")]
    [DataRow("NativeResultSetIncomplete")]
    [DataRow("NativeControllerMalformed")]
    public void ACompletedEarlierAssertionChangesTheNumeratorOnceAndPreservesEachReleaseHold(string issueCode)
    {
        var fixture = new UnionFixture(10);
        Partition unit = fixture.Partitions[0];
        string id = unit.Definitions[0].Id;
        IntegrityIssue[] held = [new(issueCode, "Explicit synthetic unresolved native failure.", issueCode == "NativeControllerMalformed" ? [] : [id])];
        var beforeAudit = new NativeAuditResult(unit.QualifiedKilledIds.Skip(1).ToArray(), held, 1, 0, new JsonObject());
        fixture.Partitions[0] = unit with { QualifiedKilledIds = beforeAudit.QualifiedKilledIds, IntegrityIssues = beforeAudit.Issues };
        JsonObject before = Evaluate(fixture.Sources, fixture.Partitions, []);

        NativeAuditResult afterAudit = beforeAudit with { CompletedBeforeAbortKilledIds = [id] };
        fixture.Partitions[0] = unit with { QualifiedKilledIds = afterAudit.QualifiedKilledIds, IntegrityIssues = afterAudit.Issues };
        JsonObject after = Evaluate(fixture.Sources, fixture.Partitions, []);

        Assert.AreEqual(29L, (long?)before["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)after["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)before["aggregate"]!["denominator"]);
        Assert.AreEqual(30L, (long?)after["aggregate"]!["denominator"]);
        Assert.IsTrue(JsonNode.DeepEquals(before["rawCounts"], after["rawCounts"]));
        Assert.AreEqual(1L, (long?)before["effectiveCounts"]!["UnqualifiedKilled"]);
        Assert.AreEqual(0L, (long?)after["effectiveCounts"]!["UnqualifiedKilled"]);
        Assert.AreEqual(true, (bool?)after["aggregate"]!["thresholdMet"]);
        Assert.AreEqual(false, (bool?)after["qualified"]);
        Assert.HasCount(1, after["issues"]!.AsArray());
        Assert.IsTrue(JsonNode.DeepEquals(before["issues"], after["issues"]));
        Assert.AreEqual(0, afterAudit.NormalHostCount);
        Assert.AreSame(beforeAudit.Issues, afterAudit.Issues);
    }

    [TestMethod]
    [DataRow(27L, 30L, true)]
    [DataRow(26L, 30L, false)]
    [DataRow(899L, 1000L, false)]
    [DataRow(900L, 1000L, true)]
    [DataRow(0L, 0L, false)]
    [DataRow(0L, 1L, false)]
    [DataRow(1L, 1L, true)]
    [DataRow(8301034833169298227L, long.MaxValue, true)]
    [DataRow(8301034833169298226L, long.MaxValue, false)]
    public void NinetyPercentUsesTheExactPopulationWithoutRoundingOrOverflow(long killed, long denominator, bool expected)
    {
        Assert.AreEqual(expected, MeetsThreshold(killed, denominator));
    }

    [TestMethod]
    public void NumericNativeIdsAndObservedStatusesAreNotCrossReportDefinitionIdentity()
    {
        Definition original = ExampleDefinition();
        Definition independentlyNumbered = original with
        {
            Id = "9876",
            Status = "Killed",
            KilledBy = ["a different native test GUID"],
            StatusReason = "A later observation"
        };
        Assert.AreEqual(original.Identity, independentlyNumbered.Identity);
    }

    [TestMethod]
    [DataRow("source-path")]
    [DataRow("source-bytes")]
    [DataRow("start")]
    [DataRow("end")]
    [DataRow("operator")]
    [DataRow("original")]
    [DataRow("replacement")]
    public void EverySourceBoundDefinitionComponentParticipatesInSupplementalEvidenceIdentity(string change)
    {
        Definition original = ExampleDefinition();
        Definition changed = change switch
        {
            "source-path" => original with { SourcePath = "Healthcheck/Other.cs" },
            "source-bytes" => original with { SourceSha256 = Hash("different source") },
            "start" => original with { Start = original.Start + 1 },
            "end" => original with { End = original.End + 1 },
            "operator" => original with { Mutator = "Statement mutation" },
            "original" => original with { Original = "true" },
            "replacement" => original with { Replacement = ";" },
            _ => throw new InvalidOperationException("Unknown receiving control.")
        };
        Assert.AreNotEqual(original.Identity, changed.Identity);
    }

    [TestMethod]
    public void TheCompleteUnionCanReachNinetyWhileOnePartitionRemainsBelowNinety()
    {
        var fixture = new UnionFixture(10);
        fixture.Status(0, 0, "Survived"); fixture.Status(0, 1, "Survived"); fixture.Status(1, 0, "NoCoverage");
        Adjustment[] reviews = [fixture.Adjustment(0, 0, "ReviewedNoncritical"), fixture.Adjustment(0, 1, "ReviewedNoncritical"), fixture.Adjustment(1, 0, "ReviewedNoncritical")];
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, reviews);
        Assert.AreEqual(true, (bool?)result["complete"]);
        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(90, (int?)result["requiredConservativePercent"]);
        Assert.AreEqual(27L, (long?)result["rawAggregate"]!["killed"]);
        Assert.AreEqual(30L, (long?)result["rawAggregate"]!["denominator"]);
        Assert.AreEqual(27L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
        JsonNode unit = result["partitions"]!.AsArray().Single(item => (string?)item!["group"] == "unit-runtime")!;
        Assert.AreEqual(80d, (double?)unit["rawConservativePercent"]);
        Assert.HasCount(3, result["partitions"]!.AsArray());
        Assert.HasCount(0, result["issues"]!.AsArray());
        Assert.HasCount(0, result["unresolved"]!.AsArray());
    }

    [TestMethod]
    public void ACompleteDisposedPopulationBelowNinetyStillFailsTheThreshold()
    {
        var fixture = new UnionFixture(10);
        var reviews = new List<Adjustment>();
        for (int index = 0; index < 4; index++)
        {
            fixture.Status(0, index, "Survived"); reviews.Add(fixture.Adjustment(0, index, "ReviewedNoncritical"));
        }
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, reviews.ToArray());
        Assert.AreEqual(true, (bool?)result["complete"]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.AreEqual(false, (bool?)result["aggregate"]!["thresholdMet"]);
        Assert.AreEqual(26L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
    }

    [TestMethod]
    public void NativeTimeoutDetectionNeverBecomesAnAssertionKillInTheConservativeNumerator()
    {
        var fixture = new UnionFixture(10);
        fixture.Status(0, 0, "Timeout");
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, [fixture.Adjustment(0, 0, "NonTermination")]);
        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(30L, (long?)result["rawAggregate"]!["nativeDetected"]);
        Assert.AreEqual(29L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(1L, (long?)result["effectiveCounts"]!["Timeout"]);
        Assert.AreEqual(1L, (long?)result["rawCounts"]!["Timeout"]);
    }

    [TestMethod]
    [DataRow("Killed")]
    [DataRow("Timeout")]
    [DataRow("Survived")]
    [DataRow("NoCoverage")]
    public void AnIndividuallyValidatedAssertionRerunCanResolveOnlyItsBoundRawDefinition(string status)
    {
        var fixture = new UnionFixture(10);
        fixture.Status(0, 0, status);
        if (status == "Killed") { fixture.Partitions[0] = fixture.Partitions[0] with { QualifiedKilledIds = fixture.Partitions[0].QualifiedKilledIds.Skip(1).ToArray() }; }
        JsonObject unresolved = Evaluate(fixture.Sources, fixture.Partitions, []);
        Assert.AreEqual(false, (bool?)unresolved["qualified"]);
        Assert.HasCount(1, unresolved["unresolved"]!.AsArray());
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, [fixture.Adjustment(0, 0, "AssertionKill")]);
        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(status == "Killed" ? 30L : 29L, (long?)result["rawAggregate"]!["killed"]);
        Assert.AreEqual(status, fixture.Partitions[0].Definitions[0].Status, "Evaluating evidence must preserve raw history.");
    }

    [TestMethod]
    [DataRow("Survived")]
    [DataRow("NoCoverage")]
    public void ExactEquivalenceRemovesOneDenominatorEntryAndNeverAddsAKill(string status)
    {
        var fixture = new UnionFixture(10); fixture.Status(0, 0, status);
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, [fixture.Adjustment(0, 0, "Equivalent")]);
        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(29L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(29L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(30L, (long?)result["rawAggregate"]!["denominator"]);
        Assert.AreEqual(29L, (long?)result["rawAggregate"]!["killed"]);
        Assert.AreEqual(1L, (long?)result["effectiveCounts"]!["ExcludedEquivalent"]);
        Assert.AreEqual(1L, (long?)result["rawCounts"]![status]);
    }

    [TestMethod]
    [DataRow("Survived")]
    [DataRow("NoCoverage")]
    public void ReviewedNoncriticalDispositionRetainsTheFullDenominator(string status)
    {
        var fixture = new UnionFixture(10); fixture.Status(0, 0, status);
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, [fixture.Adjustment(0, 0, "ReviewedNoncritical")]);
        Assert.AreEqual(true, (bool?)result["qualified"]);
        Assert.AreEqual(29L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(30L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(1L, (long?)result["effectiveCounts"]![status]);
    }

    [TestMethod]
    [DataRow("Killed", true)]
    [DataRow("Timeout", true)]
    [DataRow("Survived", true)]
    [DataRow("NoCoverage", true)]
    [DataRow("RuntimeError", true)]
    [DataRow("Pending", false)]
    public void PassingRawArithmeticDoesNotResolveMissingEvidenceOrDisposition(string status, bool complete)
    {
        var fixture = new UnionFixture(10); fixture.Status(0, 0, status);
        if (status == "Killed") { fixture.Partitions[0] = fixture.Partitions[0] with { QualifiedKilledIds = fixture.Partitions[0].QualifiedKilledIds.Skip(1).ToArray() }; }
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, []);
        Assert.AreEqual(complete, (bool?)result["complete"]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.IsGreaterThan(0, result["unresolved"]!.AsArray().Count);
        Assert.AreEqual(1L, (long?)result["rawCounts"]![status] - (status == "Killed" ? 29L : 0L));
    }

    [TestMethod]
    public void CompileErrorsAndIgnoredDefinitionsRemainVisibleWithoutCreatingAValidPopulation()
    {
        var fixture = new UnionFixture(2);
        for (int group = 0; group < 3; group++) { fixture.Status(group, 0, "CompileError"); fixture.Status(group, 1, "Ignored"); }
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, []);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.AreEqual(0L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(0L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(3L, (long?)result["rawCounts"]!["CompileError"]);
        Assert.AreEqual(3L, (long?)result["rawCounts"]!["Ignored"]);
    }

    [TestMethod]
    public void AGlobalNativeIntegrityFailureCannotBeWaivedByAnUnrelatedDefinitionAdjustment()
    {
        var fixture = new UnionFixture(10); fixture.Status(0, 0, "Survived");
        fixture.Partitions[0] = fixture.Partitions[0] with { IntegrityIssues = [new("MissingHostExit", "Synthetic unassigned native host failure", [])] };
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, [fixture.Adjustment(0, 0, "AssertionKill")]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.HasCount(1, result["issues"]!.AsArray());
        Assert.AreEqual("MissingHostExit", (string?)result["issues"]![0]!["code"]);
    }

    [TestMethod]
    [DataRow("one-assertion", false)]
    [DataRow("all-assertions", true)]
    [DataRow("nontermination-and-assertion", true)]
    [DataRow("equivalent-and-assertion", false)]
    [DataRow("reviewed-and-assertion", false)]
    public void AnAssignedCohortFailureRequiresQualifyingEvidenceForEveryInvolvedDefinition(string mode, bool qualified)
    {
        var fixture = new UnionFixture(10); fixture.Status(0, 0, "Survived"); fixture.Status(0, 1, mode == "nontermination-and-assertion" ? "Timeout" : "Survived");
        fixture.Partitions[0] = fixture.Partitions[0] with { IntegrityIssues = [new("AbortedCohort", "Synthetic shared run failure", ["1", "2"])] };
        var adjustments = new List<Adjustment> { fixture.Adjustment(0, 0, "AssertionKill") };
        if (mode != "one-assertion")
        {
            string kind = mode switch { "all-assertions" => "AssertionKill", "nontermination-and-assertion" => "NonTermination", "equivalent-and-assertion" => "Equivalent", _ => "ReviewedNoncritical" };
            adjustments.Add(fixture.Adjustment(0, 1, kind));
        }
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions, adjustments.ToArray());
        Assert.AreEqual(qualified, (bool?)result["qualified"]);
        Assert.HasCount(qualified ? 0 : 1, result["issues"]!.AsArray());
    }

    [TestMethod]
    [DataRow("missing-partition")]
    [DataRow("duplicate-partition")]
    [DataRow("unknown-partition")]
    [DataRow("missing-source-group")]
    [DataRow("duplicate-source")]
    [DataRow("unknown-source-group")]
    [DataRow("missing-partition-source")]
    [DataRow("extra-partition-source")]
    [DataRow("wrong-partition-source")]
    [DataRow("stale-source-hash")]
    [DataRow("changed-source-text")]
    [DataRow("unknown-definition-source")]
    [DataRow("stale-definition-source-hash")]
    [DataRow("negative-start")]
    [DataRow("reversed-span")]
    [DataRow("span-past-source")]
    [DataRow("original-span-mismatch")]
    [DataRow("duplicate-id")]
    [DataRow("duplicate-definition")]
    [DataRow("unknown-status")]
    [DataRow("unknown-qualified-id")]
    [DataRow("duplicate-qualified-id")]
    [DataRow("unknown-issue-id")]
    [DataRow("invalid-report-hash")]
    public void IncompleteOrConflictingScopeAndDefinitionInputsCannotProduceAnAggregate(string mode)
    {
        var fixture = new UnionFixture(2);
        Partition part = fixture.Partitions[0]; Definition first = part.Definitions[0];
        switch (mode)
        {
            case "missing-partition": fixture.Partitions = fixture.Partitions[..2]; break;
            case "duplicate-partition": fixture.Partitions[2] = part; break;
            case "unknown-partition": fixture.Partitions[0] = part with { Group = "other" }; break;
            case "missing-source-group": fixture.Sources = fixture.Sources[..2]; break;
            case "duplicate-source": fixture.Sources = [.. fixture.Sources, fixture.Sources[0]]; break;
            case "unknown-source-group": fixture.Sources[0] = fixture.Sources[0] with { Group = "other" }; break;
            case "missing-partition-source": fixture.Partitions[0] = part with { Sources = [] }; break;
            case "extra-partition-source": fixture.Partitions[0] = part with { Sources = [.. part.Sources, fixture.Sources[1]] }; break;
            case "wrong-partition-source": fixture.Partitions[0] = part with { Sources = [fixture.Sources[1]] }; break;
            case "stale-source-hash": fixture.Sources[0] = fixture.Sources[0] with { Sha256 = Hash("stale") }; break;
            case "changed-source-text": fixture.Sources[0] = fixture.Sources[0] with { Text = fixture.Sources[0].Text + " " }; break;
            case "unknown-definition-source": part.Definitions[0] = first with { SourcePath = "Healthcheck/Unknown.cs" }; break;
            case "stale-definition-source-hash": part.Definitions[0] = first with { SourceSha256 = Hash("stale") }; break;
            case "negative-start": part.Definitions[0] = first with { Start = -1 }; break;
            case "reversed-span": part.Definitions[0] = first with { End = first.Start - 1 }; break;
            case "span-past-source": part.Definitions[0] = first with { End = fixture.Sources[0].Text.Length + 1 }; break;
            case "original-span-mismatch": part.Definitions[0] = first with { Original = "true" }; break;
            case "duplicate-id": part.Definitions[1] = part.Definitions[1] with { Id = first.Id }; break;
            case "duplicate-definition": part.Definitions[1] = first with { Id = "2" }; break;
            case "unknown-status": part.Definitions[0] = first with { Status = "Detected" }; break;
            case "unknown-qualified-id": fixture.Partitions[0] = part with { QualifiedKilledIds = ["unknown"] }; break;
            case "duplicate-qualified-id": fixture.Partitions[0] = part with { QualifiedKilledIds = ["1", "1"] }; break;
            case "unknown-issue-id": fixture.Partitions[0] = part with { IntegrityIssues = [new("BadAssociation", "Unknown definition", ["missing"])] }; break;
            case "invalid-report-hash": fixture.Partitions[0] = part with { ReportSha256 = "not a checksum" }; break;
            default: throw new InvalidOperationException("Unknown receiving control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Evaluate(fixture.Sources, fixture.Partitions, []));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("conflicting")]
    [DataRow("unknown-definition")]
    [DataRow("wrong-report")]
    [DataRow("wrong-status")]
    [DataRow("unknown-kind")]
    [DataRow("equivalent-killed")]
    [DataRow("equivalent-timeout")]
    [DataRow("assertion-compileerror")]
    [DataRow("assertion-ignored")]
    [DataRow("nontermination-survived")]
    [DataRow("reviewed-timeout")]
    public void SupplementalAdjustmentsCannotBeRepeatedTransferredOrAppliedToAnotherRawStatus(string mode)
    {
        var fixture = new UnionFixture(10); fixture.Status(0, 0, "Survived");
        if (mode == "equivalent-killed") { fixture.Status(0, 0, "Killed"); }
        if (mode is "equivalent-timeout" or "reviewed-timeout") { fixture.Status(0, 0, "Timeout"); }
        if (mode == "assertion-compileerror") { fixture.Status(0, 0, "CompileError"); }
        if (mode == "assertion-ignored") { fixture.Status(0, 0, "Ignored"); }
        Adjustment valid = fixture.Adjustment(0, 0, "AssertionKill");
        Adjustment[] adjustments = mode switch
        {
            "duplicate" => [valid, valid],
            "conflicting" => [valid, valid with { Kind = "Equivalent" }],
            "unknown-definition" => [valid with { DefinitionIdentity = Hash("unknown definition") }],
            "wrong-report" => [valid with { OriginalReportSha256 = fixture.Partitions[1].ReportSha256 }],
            "wrong-status" => [valid with { OriginalStatus = "Timeout" }],
            "unknown-kind" => [valid with { Kind = "Approved" }],
            "equivalent-killed" or "equivalent-timeout" => [valid with { Kind = "Equivalent" }],
            "nontermination-survived" => [valid with { Kind = "NonTermination" }],
            "reviewed-timeout" => [valid with { Kind = "ReviewedNoncritical" }],
            _ => [valid]
        };
        Assert.ThrowsExactly<InvalidDataException>(() => Evaluate(fixture.Sources, fixture.Partitions, adjustments));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeUtf16LocationsPreserveCrLfSurrogatesAndAbsoluteSourceRootBinding(bool backslashes)
    {
        var fixture = new UnionFixture(1); JsonObject report = NativeReport(fixture);
        if (backslashes)
        {
            var changed = new JsonObject();
            foreach ((string key, JsonNode? value) in report["files"]!.AsObject()) { changed.Add(key.Replace('/', '\\'), value!.DeepClone()); }
            report["files"] = changed;
        }
        string before = report.ToJsonString();
        Definition[] parsed = ParseReport(report, "unit-runtime", fixture.Partitions[0].ReportSha256, NativeRoot, fixture.Sources);
        Assert.HasCount(1, parsed);
        Assert.AreEqual(fixture.Partitions[0].Definitions[0].Identity, parsed[0].Identity);
        Assert.AreEqual("false", fixture.Sources[0].Text[parsed[0].Start..parsed[0].End]);
        Assert.AreEqual(before, report.ToJsonString(), "Receiving analysis must not rewrite native history.");
    }

    [TestMethod]
    [DataRow("missing-selected-file")]
    [DataRow("selected-placeholder")]
    [DataRow("selected-source-mismatch")]
    [DataRow("selected-source-hash-mismatch")]
    [DataRow("relative-selected-path")]
    [DataRow("outside-root-selected-path")]
    [DataRow("root-prefix-collision")]
    [DataRow("duplicate-normalized-path")]
    [DataRow("outside-group-real-source")]
    [DataRow("outside-group-mutants")]
    [DataRow("missing-id")]
    [DataRow("duplicate-id")]
    [DataRow("duplicate-definition")]
    [DataRow("unknown-status")]
    [DataRow("zero-line")]
    [DataRow("zero-column")]
    [DataRow("line-past-source")]
    [DataRow("column-past-line")]
    [DataRow("reversed-span")]
    [DataRow("empty-span")]
    [DataRow("missing-replacement")]
    [DataRow("missing-operator")]
    [DataRow("invalid-report-hash")]
    public void MalformedOrMasqueradingNativeReportInputsFailBeforeAggregation(string mode)
    {
        var fixture = new UnionFixture(1); JsonObject report = NativeReport(fixture);
        JsonObject files = report["files"]!.AsObject(); string path = NativeRoot + "/" + fixture.Sources[0].Path;
        JsonObject file = files[path]!.AsObject(), mutant = file["mutants"]![0]!.AsObject();
        JsonObject start = mutant["location"]!["start"]!.AsObject(), end = mutant["location"]!["end"]!.AsObject();
        string reportHash = fixture.Partitions[0].ReportSha256;
        switch (mode)
        {
            case "missing-selected-file": files.Remove(path); break;
            case "selected-placeholder": file["source"] = "File ignored by mutate filter"; break;
            case "selected-source-mismatch": file["source"] = fixture.Sources[0].Text + " "; break;
            case "selected-source-hash-mismatch": fixture.Sources[0] = fixture.Sources[0] with { Sha256 = Hash("stale") }; break;
            case "relative-selected-path": files.Remove(path); files.Add(fixture.Sources[0].Path, file); break;
            case "outside-root-selected-path": files.Remove(path); files.Add("/other/" + fixture.Sources[0].Path, file); break;
            case "root-prefix-collision": files.Remove(path); files.Add(NativeRoot + "-other/" + fixture.Sources[0].Path, file); break;
            case "duplicate-normalized-path": files.Add(path.Replace('/', '\\'), file.DeepClone()); break;
            case "outside-group-real-source": files[NativeRoot + "/" + fixture.Sources[1].Path]!["source"] = fixture.Sources[1].Text; break;
            case "outside-group-mutants": files[NativeRoot + "/" + fixture.Sources[1].Path]!["mutants"]!.AsArray().Add(mutant.DeepClone()); break;
            case "missing-id": mutant.Remove("id"); break;
            case "duplicate-id": file["mutants"]!.AsArray().Add(mutant.DeepClone()); break;
            case "duplicate-definition": JsonObject duplicate = mutant.DeepClone().AsObject(); duplicate["id"] = "2"; file["mutants"]!.AsArray().Add(duplicate); break;
            case "unknown-status": mutant["status"] = "Detected"; break;
            case "zero-line": start["line"] = 0; break;
            case "zero-column": start["column"] = 0; break;
            case "line-past-source": start["line"] = 900; break;
            case "column-past-line": start["column"] = 900; break;
            case "reversed-span": end["column"] = (int)start["column"]! - 1; break;
            case "empty-span": end["column"] = (int)start["column"]!; break;
            case "missing-replacement": mutant.Remove("replacement"); break;
            case "missing-operator": mutant.Remove("mutatorName"); break;
            case "invalid-report-hash": reportHash = "not a checksum"; break;
            default: throw new InvalidOperationException("Unknown receiving control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => ParseReport(report, "unit-runtime", reportHash, NativeRoot, fixture.Sources));
    }

    private const string NativeRoot = "/synthetic/native/src/CIS.Core";

    private static JsonObject NativeReport(UnionFixture fixture)
    {
        var files = new JsonObject();
        for (int group = 0; group < fixture.Sources.Length; group++)
        {
            Source source = fixture.Sources[group]; var mutants = new JsonArray();
            if (group == 0)
            {
                foreach (Definition definition in fixture.Partitions[group].Definitions)
                {
                    mutants.Add(new JsonObject
                    {
                        ["id"] = definition.Id,
                        ["mutatorName"] = definition.Mutator,
                        ["replacement"] = definition.Replacement,
                        ["status"] = definition.Status,
                        ["static"] = false,
                        ["coveredBy"] = new JsonArray(),
                        ["killedBy"] = new JsonArray(),
                        ["location"] = new JsonObject { ["start"] = Position(source.Text, definition.Start), ["end"] = Position(source.Text, definition.End) }
                    });
                }
            }
            files.Add(NativeRoot + "/" + source.Path, new JsonObject
            {
                ["source"] = group == 0 ? source.Text : "File ignored by mutate filter",
                ["language"] = group == 0 ? "cs" : "none",
                ["mutants"] = mutants
            });
        }
        return new JsonObject { ["schemaVersion"] = "2", ["projectRoot"] = NativeRoot, ["files"] = files };
    }

    private static JsonObject Position(string source, int offset)
    {
        int line = 1, start = 0;
        for (int index = 0; index < offset; index++) { if (source[index] == '\n') { line++; start = index + 1; } }
        return new JsonObject { ["line"] = line, ["column"] = offset - start + 1 };
    }

    private sealed class UnionFixture
    {
        internal Source[] Sources { get; set; }
        internal Partition[] Partitions { get; set; }

        internal UnionFixture(int count)
        {
            Sources = new Source[3]; Partitions = new Partition[3];
            for (int group = 0; group < Groups.Length; group++)
            {
                string text = "namespace CIS.Healthcheck;\r\ninternal static class Synthetic" + group + "\r\n{\r\n";
                for (int index = 0; index < count; index++) { text += "    internal const string Marker" + index + " = \"π😀\"; internal const bool Flag" + index + " = false;\r\n"; }
                text += "}\r\n";
                var source = new Source("Healthcheck/Synthetic" + group + ".cs", Hash(text), Groups[group], text);
                Sources[group] = source; var definitions = new Definition[count]; int search = 0;
                for (int index = 0; index < count; index++)
                {
                    int start = text.IndexOf("false", search, StringComparison.Ordinal); search = start + 5;
                    definitions[index] = new((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), source.Path, source.Sha256,
                        start, start + 5, "Boolean mutation", "false", "true", "Killed", ["11111111-1111-4111-8111-111111111111"]);
                }
                Partitions[group] = new(Groups[group], Hash("synthetic raw report " + group), [source], definitions, definitions.Select(item => item.Id).ToArray(), []);
            }
        }

        internal void Status(int group, int index, string status)
        {
            Partition part = Partitions[group]; Definition definition = part.Definitions[index];
            part.Definitions[index] = definition with { Status = status };
            Partitions[group] = part with { QualifiedKilledIds = part.Definitions.Where(item => item.Status == "Killed").Select(item => item.Id).ToArray() };
        }

        internal Adjustment Adjustment(int group, int index, string kind)
        {
            Partition part = Partitions[group]; Definition definition = part.Definitions[index];
            return new(definition.Identity, part.ReportSha256, definition.Status, kind);
        }
    }

    private static Definition ExampleDefinition() => new("1", "Healthcheck/Unit.cs", Hash("false"),
        0, 5, "Boolean mutation", "false", "true", "Survived", []);

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
