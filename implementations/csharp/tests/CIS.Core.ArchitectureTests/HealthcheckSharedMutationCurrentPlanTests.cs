using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Comparison = CIS.Verification.HealthcheckSharedMutationCurrentPlan;
using Protocol = CIS.Verification.HealthcheckSharedMutationProtocol;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckSharedMutationCurrentPlanTests
{
    [TestMethod]
    public void UnchangedCurrentSelectionMayReuseDespiteAnUnrelatedExistingInputChange()
    {
        var fixture = new Fixture();
        string before = fixture.Original.ToJsonString();
        fixture.CurrentHashes["tests/Unrelated.cs"] = new('b', 64);
        Comparison.Plan plan = Comparison.Read(fixture.PlanJson().ToJsonString(), 0, [Fixture.Source]);
        Comparison.Decision decision = fixture.Compare(plan).Single();
        Assert.IsTrue(decision.Reusable);
        Assert.HasCount(0, decision.Reasons);
        Assert.AreEqual(before, fixture.Original.ToJsonString());
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("test")]
    [DataRow("dependency")]
    [DataRow("missing")]
    public void AChangedOrMissingCurrentClosureInputCannotReuseEvenWithMatchingClaimedFingerprints(string change)
    {
        var fixture = new Fixture();
        string path = change switch { "source" => Fixture.Source, "test" => Fixture.Test, _ => Fixture.Dependency };
        if (change == "missing") { fixture.CurrentHashes.Remove(path); }
        else { fixture.CurrentHashes[path] = new('b', 64); }
        Comparison.Decision decision = fixture.Compare(fixture.Plan()).Single();
        Assert.IsFalse(decision.Reusable);
        CollectionAssert.Contains(decision.Reasons, "CurrentInputChanged:" + path);
    }

    [TestMethod]
    [DataRow("unit")]
    [DataRow("mutation")]
    [DataRow("filter")]
    [DataRow("tests")]
    [DataRow("dependencies")]
    public void CurrentGraphAndFingerprintDifferencesCannotBeRebasedOrRelabeled(string change)
    {
        var fixture = new Fixture();
        Comparison.Batch batch = fixture.Plan().Batches.Single();
        batch = change switch
        {
            "unit" => batch with { UnitFingerprint = new('c', 64) },
            "mutation" => batch with { MutationFingerprint = new('c', 64) },
            "filter" => batch with { Filter = "FullyQualifiedName~Other." },
            "tests" => batch with { Tests = [Fixture.Test, "tests/NewRelevant.cs"] },
            _ => batch with { Dependencies = [Fixture.Source, Fixture.Test, Fixture.Dependency, "src/NewDependency.cs"] },
        };
        Assert.IsFalse(fixture.Compare(new("current-graph", [batch])).Single().Reusable);
    }

    [TestMethod]
    public void ANewTestFileHoldsReuseEvenWhenTheOldClosureAndFingerprintsAreUnchanged()
    {
        var fixture = new Fixture();
        Comparison.Decision decision = Comparison.Compare(fixture.Original, fixture.Inputs, fixture.Plan(), fixture.CurrentHashes,
            [Fixture.Test], [Fixture.Test, "tests/NewRelevant.cs"]).Single();
        Assert.IsFalse(decision.Reusable);
        CollectionAssert.Contains(decision.Reasons, "TestDiscoveryInventoryChanged");
    }

    [TestMethod]
    public void OneChangedSourceDoesNotInvalidateASeparateExactlyReproducedBatch()
    {
        var fixture = new Fixture();
        const string other = "src/Other.cs";
        string sha = new('a', 64);
        var inputs = fixture.Inputs.Append(new Protocol.Input(other, sha, ["source", "dependency"], true)).ToArray();
        fixture.CurrentHashes[other] = sha;
        fixture.CurrentHashes[Fixture.Source] = new('b', 64);
        JsonObject second = fixture.Original["batches"]![0]!.DeepClone().AsObject();
        second["sourceFile"] = other;
        second["closureId"] = "other-closure";
        fixture.Original["batches"]!.AsArray().Add(second);
        fixture.Original["closures"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "other-closure",
            ["testInputIndices"] = new JsonArray(1),
            ["dependencyInputIndices"] = new JsonArray(4, 1, 2)
        });
        Comparison.Batch first = fixture.Plan().Batches.Single();
        var plan = new Comparison.Plan("current-graph", [first, first with { SourceFile = other, Dependencies = [other, Fixture.Test, Fixture.Dependency] }]);
        Comparison.Decision[] decisions = Comparison.Compare(fixture.Original, inputs, plan, fixture.CurrentHashes, [Fixture.Test], [Fixture.Test]);
        Assert.IsFalse(decisions.Single(decision => decision.SourceFile == Fixture.Source).Reusable);
        Assert.IsTrue(decisions.Single(decision => decision.SourceFile == other).Reusable);
    }

    [TestMethod]
    [DataRow("status")]
    [DataRow("exit")]
    [DataRow("qualified")]
    [DataRow("scope-type")]
    [DataRow("unknown")]
    [DataRow("selector")]
    [DataRow("fingerprint")]
    [DataRow("duplicate-source")]
    [DataRow("duplicate-test")]
    [DataRow("escape")]
    public void MalformedOrUnfinishedCurrentPlansAreRejected(string change)
    {
        var fixture = new Fixture();
        JsonObject plan = fixture.PlanJson();
        JsonObject batch = plan["batches"]![0]!.AsObject();
        switch (change)
        {
            case "status": plan["status"] = "recovery-required"; break;
            case "exit": plan["exitCode"] = 2; break;
            case "qualified": plan["qualified"] = true; break;
            case "scope-type": plan["scopeComplete"] = "false"; break;
            case "unknown": plan["selectionVerified"] = true; break;
            case "selector": batch["selection"] = "legacy"; break;
            case "fingerprint": batch["unitFingerprint"] = "old"; break;
            case "duplicate-source": plan["batches"]!.AsArray().Add(batch.DeepClone()); break;
            case "duplicate-test": batch["testFiles"]!.AsArray().Add(Fixture.Test); break;
            case "escape": batch["dependencyFiles"]![0] = "../outside.cs"; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Comparison.Read(plan.ToJsonString(), 0, [Fixture.Source]));
    }

    private sealed class Fixture
    {
        internal const string Source = "src/Subject.cs", Test = "tests/Selected.cs", Dependency = "src/Dependency.cs";
        private const string Filter = "FullyQualifiedName~Selected.";
        private static readonly string Sha = new('a', 64);
        internal Protocol.Input[] Inputs { get; } = [new(Source, Sha, ["source", "dependency"], true),
            new(Test, Sha, ["test", "dependency"], true), new(Dependency, Sha, ["dependency"], true),
            new("tests/Unrelated.cs", Sha, ["test"], true)];
        internal Dictionary<string, string> CurrentHashes { get; } = new(StringComparer.Ordinal)
        { [Source] = Sha, [Test] = Sha, [Dependency] = Sha, ["tests/Unrelated.cs"] = Sha };
        internal JsonObject Original { get; } = new()
        {
            ["batches"] = new JsonArray(new JsonObject
            {
                ["sourceFile"] = Source,
                ["closureId"] = "closure",
                ["unitFingerprint"] = Sha,
                ["mutationFingerprint"] = Sha,
                ["testFilterSha256"] = Digest(Filter)
            }),
            ["closures"] = new JsonArray(new JsonObject
            {
                ["id"] = "closure",
                ["testInputIndices"] = new JsonArray(1),
                ["dependencyInputIndices"] = new JsonArray(0, 1, 2)
            }),
        };
        internal Comparison.Plan Plan() => new("current-graph", [new(Source, "graph", [Test], Filter, Sha, Sha, [Source, Test, Dependency])]);
        internal Comparison.Decision[] Compare(Comparison.Plan plan) => Comparison.Compare(Original, Inputs, plan, CurrentHashes, [Test, "tests/Unrelated.cs"], [Test, "tests/Unrelated.cs"]);
        internal JsonObject PlanJson() => JsonSerializer.SerializeToNode(new
        {
            status = "planned",
            exitCode = 0,
            profileId = "synthetic",
            graphBuildId = "current-graph",
            statePath = ".cis/local/state.json",
            layer = "all",
            scopeComplete = false,
            qualified = false,
            executedUnitBatches = 0,
            executedMutationBatches = 0,
            reusedUnitBatches = 0,
            reusedMutationBatches = 0,
            pendingUnitBatches = 1,
            pendingMutationBatches = 1,
            killed = 0,
            survived = 0,
            timedOut = 0,
            noCoverage = 0,
            knownDenominator = 0,
            knownScore = (double?)null,
            completeScopeScore = (double?)null,
            thresholdMet = false,
            durationMilliseconds = 0,
            retainedBytes = 0,
            batches = new[] { new { id = "file-synthetic", sourceFile = Source, selection = "graph", testFiles = new[] { Test }, testFilter = Filter,
                unitFingerprint = Sha, mutationFingerprint = Sha, unitCurrent = false, mutationCurrent = false, dependencyFiles = new[] { Source, Test, Dependency } } },
            diagnostics = System.Array.Empty<string>(),
        })!.AsObject();
        private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })));
    }
}
