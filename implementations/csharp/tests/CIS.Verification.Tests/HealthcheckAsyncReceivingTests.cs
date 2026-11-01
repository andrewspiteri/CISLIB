using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Grouped = CIS.Verification.HealthcheckGroupedMutation;

namespace CIS.Tests.Verification;

// Synthetic admission records exercise the checker contract, not native mutation evidence.
[TestClass]
public sealed class HealthcheckAsyncReceivingTests
{
    private const string ServerSource = "Healthcheck/Pipes/HealthPipeServer.cs";
    private const string UnitSource = "tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeActiveCancellationBoundaryTests.cs";
    private const string IntegrationSource = "tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckPipeTransportTests.cs";
    private const string UnitWitness = "CIS.Tests.UnitTests.Healthcheck.HealthPipeActiveCancellationBoundaryTests.ClosingTheTransportPrecedesSettlementOfItsActuallyRunningSource";
    private const string IntegrationWitness = "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckPipeTransportTests.DisposalWaitsForAnActuallyRunningSnapshotSource";
    private const string IntegrationId = "integration-disposal";

    [TestMethod]
    [DataRow("unit")]
    [DataRow("integration")]
    [DataRow("both")]
    public void ServerAwaitAcceptsOnlyItsNamedUnitOrIntegrationWitnessAndRecordsTheChosenOne(string selection)
    {
        var fixture = Fixture(selection);
        string original = fixture.Report.ToJsonString();
        JsonArray controls = Admit(fixture);
        Assert.HasCount(2, controls);
        JsonNode server = controls.Single(item => (string?)item!["source"] == ServerSource)!;
        bool integration = selection == "integration";
        Assert.AreEqual(integration ? IntegrationSource : UnitSource, (string?)server["testSource"]);
        Assert.AreEqual(integration ? IntegrationWitness : UnitWitness, (string?)server["testName"]);
        CollectionAssert.AreEqual(new[] { integration ? IntegrationId : "unit-disposal" },
            server["killedBy"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.AreEqual("Killed", (string?)server["status"]);
        Assert.AreEqual(original, fixture.Report.ToJsonString(), "Admission must not rewrite the native report.");
    }

    [TestMethod]
    [DataRow("changed-source")]
    [DataRow("wrong-name")]
    [DataRow("wrong-file")]
    [DataRow("unknown-killer")]
    [DataRow("duplicate-killer")]
    [DataRow("ambiguous-id")]
    [DataRow("ambiguous-file")]
    [DataRow("unrelated-async-test")]
    public void IntegrationAlternativeRejectsTamperedUnrelatedOrAmbiguousNativeWitnesses(string change)
    {
        var fixture = Fixture("integration");
        JsonObject tests = fixture.Report["testFiles"]!.AsObject();
        string key = "/candidate/implementations/csharp/" + IntegrationSource;
        JsonNode file = tests[key]!;
        JsonNode mutant = ServerMutant(fixture);
        switch (change)
        {
            case "changed-source": file["source"] = "different test implementation"; break;
            case "wrong-name": file["tests"]![0]!["name"] = UnitWitness; break;
            case "wrong-file":
                tests.Remove(key);
                tests["/candidate/implementations/csharp/tests/Unrelated.cs"] = file;
                break;
            case "unknown-killer": mutant["killedBy"] = new JsonArray("unknown"); break;
            case "duplicate-killer": mutant["killedBy"] = new JsonArray(IntegrationId, IntegrationId); break;
            case "ambiguous-id":
                tests["/candidate/implementations/csharp/tests/Unrelated.cs"] = TestFile("unrelated source", IntegrationId, "Unrelated.Native.Test");
                break;
            case "ambiguous-file": tests["/another/implementations/csharp/" + IntegrationSource] = file.DeepClone(); break;
            case "unrelated-async-test": file["tests"]![0]!["name"] = "Unrelated.Test.ThatAlsoAwaitsDisposal"; break;
        }
        string original = fixture.Report.ToJsonString();
        Assert.ThrowsExactly<InvalidDataException>(() => Admit(fixture));
        Assert.AreEqual(original, fixture.Report.ToJsonString());
    }

    [TestMethod]
    [DataRow("Survived")]
    [DataRow("NoCoverage")]
    [DataRow("Timeout")]
    public void AnIntegrationWitnessCannotTurnANativeMissIntoAnAsyncKill(string status)
    {
        var fixture = Fixture("integration");
        fixture.Definitions[1] = fixture.Definitions[1] with { Status = status };
        ServerMutant(fixture)["status"] = status;
        Assert.ThrowsExactly<InvalidDataException>(() => Admit(fixture));
        Assert.AreEqual(status, fixture.Definitions[1].Status);
        Assert.AreEqual(status, (string?)ServerMutant(fixture)["status"]);
    }

    [TestMethod]
    [DataRow("operator")]
    [DataRow("replacement")]
    [DataRow("span")]
    [DataRow("native-status")]
    [DataRow("native-operator")]
    [DataRow("native-replacement")]
    [DataRow("native-span")]
    public void IntegrationAdmissionKeepsTheExactNativeStatementDefinition(string change)
    {
        var fixture = Fixture("integration");
        Grouped.Definition definition = fixture.Definitions[1];
        JsonNode mutant = ServerMutant(fixture);
        switch (change)
        {
            case "operator": fixture.Definitions[1] = definition with { Operator = "Block removal mutation" }; break;
            case "replacement": fixture.Definitions[1] = definition with { Replacement = "false" }; break;
            case "span": fixture.Definitions[1] = definition with { Location = Location(3, 1, 3, 2).ToJsonString() }; break;
            case "native-status": mutant["status"] = "Survived"; break;
            case "native-operator": mutant["mutatorName"] = "Block removal mutation"; break;
            case "native-replacement": mutant["replacement"] = "false"; break;
            case "native-span": mutant["location"] = Location(3, 1, 3, 2); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Admit(fixture));
    }

    [TestMethod]
    [DataRow("ReviewedTimeout")]
    [DataRow("NonTermination")]
    public void TimeoutDispositionsRetainTheRawTimeoutFullDenominatorAndZeroKillCredit(string decision)
    {
        Grouped.Candidate candidate = Candidate();
        Grouped.Definition killed = Definition("0", "Killed"), timeout = Definition("1", "Timeout");
        string rationale = decision == "NonTermination"
            ? "The changed loop cannot terminate without its controlled release; the actual native Timeout remains a miss."
            : "The actual native deadline outcome remains a reviewed miss; it supplies neither kill nor denominator credit.";
        var review = new Grouped.Review(candidate.Identity, timeout.Identity, "Timeout", "HC-BOUNDED-LIFETIME", decision,
            "local-reviewer", rationale, "2026-10-30T12:00:00Z");
        Grouped.Assessment result = Grouped.Assess(candidate, candidate.Sources, [killed, timeout], [review],
            new("Maintained technical review authority", ["local-reviewer"]));
        Assert.AreEqual(1, result.Killed);
        Assert.AreEqual(1, result.Timeout);
        Assert.AreEqual(2, result.RawEligibleDenominator);
        Assert.AreEqual(2, result.EffectiveDenominator);
        Assert.AreEqual(0, result.ReviewedEquivalent);
        Assert.AreEqual(50m, result.ConservativeScore);
        Assert.IsFalse(result.Qualified);
        Assert.AreEqual("Timeout", timeout.Status);
    }

    [TestMethod]
    [DataRow("ReviewedTimeout", "Survived")]
    [DataRow("ReviewedTimeout", "NoCoverage")]
    [DataRow("ReviewedTimeout", "Killed")]
    [DataRow("NonTermination", "Survived")]
    [DataRow("NonTermination", "NoCoverage")]
    [DataRow("NonTermination", "Killed")]
    public void ATimeoutDispositionCannotWaiveAnotherRawOutcome(string decision, string status)
    {
        Grouped.Candidate candidate = Candidate();
        Grouped.Definition definition = Definition("0", status);
        var review = new Grouped.Review(candidate.Identity, definition.Identity, status, "HC-BOUNDED-LIFETIME", decision,
            "local-reviewer", "This synthetic wrong-status row must fail rather than approve a surviving or uncovered guard.", "2026-10-30T12:00:00Z");
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.Assess(candidate, candidate.Sources, [definition], [review],
            new("Maintained technical review authority", ["local-reviewer"])));
    }

    private static JsonArray Admit((Grouped.Definition[] Definitions, JsonObject Report, Dictionary<string, string> Sources) fixture)
        => Grouped.ValidateTransportAsyncControls("pipes", fixture.Definitions, fixture.Report, path => fixture.Sources[path]);

    private static JsonNode ServerMutant((Grouped.Definition[] Definitions, JsonObject Report, Dictionary<string, string> Sources) fixture)
        => fixture.Report["files"]!["/candidate/src/CIS.Core/" + ServerSource]!["mutants"]![0]!;

    private static (Grouped.Definition[] Definitions, JsonObject Report, Dictionary<string, string> Sources) Fixture(string selection)
    {
        const string probe = "Healthcheck/Pipes/HealthProbeClient.cs";
        const string probeName = "CIS.Tests.UnitTests.Healthcheck.HealthPipeActiveCancellationBoundaryTests.CallerCancellationAfterARealRequestDoesNotWaitForThePeerToSendAResponse";
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new JsonObject();
        var tests = new JsonObject();
        string unitText = "// frozen unit source\n";
        string integrationText = "// frozen integration source\n";
        sources["implementations/csharp/" + UnitSource] = unitText;
        sources["implementations/csharp/" + IntegrationSource] = integrationText;
        tests["/candidate/implementations/csharp/" + UnitSource] = TestFile(unitText, "probe-cancellation", probeName);
        tests["/candidate/implementations/csharp/" + IntegrationSource] = TestFile(integrationText, IntegrationId, IntegrationWitness);
        if (selection is "unit" or "both")
        {
            tests["/candidate/implementations/csharp/" + UnitSource]!["tests"]!.AsArray()
                .Add(new JsonObject { ["id"] = "unit-disposal", ["name"] = UnitWitness });
        }
        string[] serverKillers = selection == "unit" ? ["unit-disposal"] : selection == "both" ? ["unit-disposal", IntegrationId] : [IntegrationId];
        Grouped.Definition[] definitions = [Add(probe, "0", "await WriteErrorAsync(standardError, \"TimeoutOrCanceled\").ConfigureAwait(false);", ["probe-cancellation"]),
            Add(ServerSource, "1", "await stopCompletion.Task.ConfigureAwait(false);", serverKillers)];
        return (definitions, new JsonObject { ["files"] = files, ["testFiles"] = tests }, sources);

        Grouped.Definition Add(string source, string id, string statement, string[] killers)
        {
            string text = "// frozen shipping source\n    " + statement + "\n";
            sources["implementations/csharp/src/CIS.Core/" + source] = text;
            JsonObject location = Location(2, 5, 2, 5 + statement.Length);
            var definition = new Grouped.Definition(source, "synthetic-source-hash", id, "definition-" + id,
                "Statement mutation", location.ToJsonString(), ";", "Killed");
            files["/candidate/src/CIS.Core/" + source] = new JsonObject
            {
                ["source"] = text,
                ["mutants"] = new JsonArray(new JsonObject
                {
                    ["id"] = id,
                    ["mutatorName"] = definition.Operator,
                    ["location"] = location,
                    ["replacement"] = definition.Replacement,
                    ["status"] = definition.Status,
                    ["killedBy"] = new JsonArray(killers.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray())
                })
            };
            return definition;
        }
    }

    private static JsonObject TestFile(string source, string id, string name) => new()
    {
        ["source"] = source,
        ["tests"] = new JsonArray(new JsonObject { ["id"] = id, ["name"] = name })
    };

    private static JsonObject Location(int line, int column, int endLine, int endColumn) => new()
    {
        ["start"] = new JsonObject { ["line"] = line, ["column"] = column },
        ["end"] = new JsonObject { ["line"] = endLine, ["column"] = endColumn }
    };

    private static Grouped.Candidate Candidate() => new("cis.healthcheck.grouped-candidate/v1", "candidate", "test-platform", "test-host", [], ["Healthcheck/A.cs"], "archive");
    private static Grouped.Definition Definition(string id, string status) => new("Healthcheck/A.cs", "synthetic-source-hash", id, "definition-" + id, "Boolean mutation", "{}", "false", status);
}
