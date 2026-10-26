using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationSemanticProofTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(9000)]
    public void ExplicitSyntheticReceivingProofBindsSourceAndMethodWithoutInventingOriginalPdbLineEquality(int observedLine)
    {
        var fixture = new Fixture(observedLine);
        string original = fixture.Report.ToJsonString();
        Assert.IsTrue(fixture.Match(out JsonObject? proof));
        Assert.AreEqual("ReviewedUnexpectedCoreException", (string?)proof!["kind"]);
        Assert.AreEqual(fixture.Definition.Identity, (string?)proof["definitionIdentity"]);
        Assert.AreEqual(false, (bool?)proof["pdbLineEqualsOriginalSourceLineClaim"]);
        Assert.AreEqual(false, (bool?)proof["timeoutReclassified"]);
        Assert.AreEqual(original, fixture.Report.ToJsonString());
    }

    [TestMethod]
    [DataRow("report-hash")]
    [DataRow("checked-id")]
    [DataRow("checked-status")]
    [DataRow("checked-source-path")]
    [DataRow("checked-source-hash")]
    [DataRow("checked-start")]
    [DataRow("checked-end")]
    [DataRow("checked-mutator")]
    [DataRow("checked-original")]
    [DataRow("checked-replacement")]
    [DataRow("checked-killedby")]
    [DataRow("checked-extra-killedby")]
    [DataRow("raw-status")]
    [DataRow("raw-id")]
    [DataRow("raw-mutator")]
    [DataRow("raw-replacement")]
    [DataRow("raw-start")]
    [DataRow("raw-end")]
    [DataRow("raw-source")]
    [DataRow("raw-killedby")]
    [DataRow("duplicate-raw-definition")]
    [DataRow("test-guid")]
    [DataRow("test-method")]
    [DataRow("test-source-text")]
    [DataRow("test-source-path")]
    [DataRow("duplicate-test-guid")]
    [DataRow("native-guid")]
    [DataRow("native-method")]
    [DataRow("native-code-file")]
    public void AReviewedCauseCannotApproveAChangedReportDefinitionOrTestIdentity(string mode)
    {
        var fixture = new Fixture();
        switch (mode)
        {
            case "report-hash": fixture.ReportHash = new string('b', 64); break;
            case "checked-id": fixture.Definition = fixture.Definition with { Id = "2" }; break;
            case "checked-status": fixture.Definition = fixture.Definition with { Status = "Timeout" }; break;
            case "checked-source-path": fixture.Definition = fixture.Definition with { SourcePath = "Healthcheck/Other.cs" }; break;
            case "checked-source-hash": fixture.Definition = fixture.Definition with { SourceSha256 = new string('c', 64) }; break;
            case "checked-start": fixture.Definition = fixture.Definition with { Start = fixture.Definition.Start + 1 }; break;
            case "checked-end": fixture.Definition = fixture.Definition with { End = fixture.Definition.End - 1 }; break;
            case "checked-mutator": fixture.Definition = fixture.Definition with { Mutator = "Other mutation" }; break;
            case "checked-original": fixture.Definition = fixture.Definition with { Original = "different" }; break;
            case "checked-replacement": fixture.Definition = fixture.Definition with { Replacement = "false" }; break;
            case "checked-killedby": fixture.Definition = fixture.Definition with { KilledBy = [OtherGuid] }; break;
            case "checked-extra-killedby": fixture.Definition = fixture.Definition with { KilledBy = [Guid, OtherGuid] }; break;
            case "raw-status": fixture.RawDefinition["status"] = "Timeout"; break;
            case "raw-id": fixture.RawDefinition["id"] = "2"; break;
            case "raw-mutator": fixture.RawDefinition["mutatorName"] = "Other mutation"; break;
            case "raw-replacement": fixture.RawDefinition["replacement"] = "false"; break;
            case "raw-start": fixture.RawDefinition["location"]!["start"]!["column"] = 2; break;
            case "raw-end": fixture.RawDefinition["location"]!["end"]!["column"] = 2; break;
            case "raw-source": fixture.CoreFile["source"] = fixture.Source + " "; break;
            case "raw-killedby": fixture.RawDefinition["killedBy"] = new JsonArray(OtherGuid); break;
            case "duplicate-raw-definition": fixture.CoreFile["mutants"]!.AsArray().Add(fixture.RawDefinition.DeepClone()); break;
            case "test-guid": fixture.TestFile["tests"]![0]!["id"] = OtherGuid; break;
            case "test-method": fixture.TestFile["tests"]![0]!["name"] = Method + "Other"; break;
            case "test-source-text": fixture.TestFile["source"] = fixture.TestSource + " "; break;
            case "test-source-path": fixture.Report["testFiles"] = new JsonObject { [CodeFile + "Other"] = fixture.TestFile.DeepClone() }; break;
            case "duplicate-test-guid": fixture.TestFile["tests"]!.AsArray().Add(fixture.TestFile["tests"]![0]!.DeepClone()); break;
            case "native-guid": fixture.NativeGuid = OtherGuid; break;
            case "native-method": fixture.NativeMethod = Method + "Other"; break;
            case "native-code-file": fixture.NativeCodeFile = CodeFile + "Other"; break;
            default: throw new InvalidOperationException("Unknown semantic receiving control.");
        }
        Assert.IsFalse(fixture.Match(out JsonObject? proof));
        Assert.IsNull(proof);
    }

    [TestMethod]
    [DataRow("wrong-exception")]
    [DataRow("timeout")]
    [DataRow("cancellation")]
    [DataRow("wrong-wrapper-method")]
    [DataRow("wrong-core-method")]
    [DataRow("wrong-core-file")]
    [DataRow("wrong-caller")]
    [DataRow("wrong-test-origin")]
    [DataRow("missing-core-frame")]
    [DataRow("zero-line")]
    [DataRow("cleanup-frame")]
    [DataRow("initialization-frame")]
    public void ANormalHostAloneCannotConvertAnUnrelatedOrCleanupExceptionIntoASemanticKill(string mode)
    {
        var fixture = new Fixture();
        switch (mode)
        {
            case "wrong-exception": fixture.Message = fixture.Message.Replace("System.ArgumentException:", "System.NullReferenceException:", StringComparison.Ordinal); break;
            case "timeout": fixture.Message = fixture.Message.Replace("System.ArgumentException:", "System.TimeoutException:", StringComparison.Ordinal); break;
            case "cancellation": fixture.Message = fixture.Message.Replace("System.ArgumentException:", "System.OperationCanceledException:", StringComparison.Ordinal); break;
            case "wrong-wrapper-method": fixture.Message = fixture.Message.Replace(Method, Method + "Other", StringComparison.Ordinal); break;
            case "wrong-core-method": fixture.Stack = fixture.Stack.Replace("Synthetic.Copy(", "Synthetic.Other(", StringComparison.Ordinal); break;
            case "wrong-core-file": fixture.Stack = fixture.Stack.Replace(CoreFilePath, CoreFilePath + "Other", StringComparison.Ordinal); break;
            case "wrong-caller": fixture.Stack = fixture.Stack.Replace("Synthetic.Diagnostics(", "Synthetic.Unrelated(", StringComparison.Ordinal); break;
            case "wrong-test-origin": fixture.Stack = fixture.Stack.Replace(CodeFile, CodeFile + "Other", StringComparison.Ordinal); break;
            case "missing-core-frame": fixture.Stack = string.Join('\n', fixture.Stack.Split('\n').Skip(1)); break;
            case "zero-line": fixture.Stack = fixture.Stack.Replace(":line 123", ":line 0", StringComparison.Ordinal); break;
            case "cleanup-frame": fixture.Stack += "\n at Fixture.DisposeAsync()"; break;
            case "initialization-frame": fixture.Stack += "\n at Fixture.TestInitialize()"; break;
            default: throw new InvalidOperationException("Unknown exception receiving control.");
        }
        Assert.IsFalse(fixture.Match(out _));
    }

    [TestMethod]
    public void SyntheticReviewedCaseCannotEnterTheShippingCatalogOrApproveAbsentEvidenceContext()
    {
        var fixture = new Fixture();
        Assert.IsTrue(fixture.Match(out _));
        Assert.IsFalse(HealthcheckMutationSemanticProofs.TryMatch(fixture.Context(), "1", Guid, Method, CodeFile,
            fixture.Message, fixture.Stack, out _));
        Assert.IsFalse(HealthcheckMutationSemanticProofs.TryMatch(null, "312", Guid, Method, CodeFile,
            fixture.Message, fixture.Stack, out _));
        Assert.IsNull(HealthcheckMutationSemanticProofs.CreateContext(fixture.Report, null, null));
    }

    [TestMethod]
    [DataRow("missing-hash")]
    [DataRow("missing-definitions")]
    [DataRow("invalid-hash")]
    [DataRow("empty-definitions")]
    [DataRow("duplicate-definitions")]
    public void PartialOrAmbiguousOptionalEvidenceContextIsRejected(string mode)
    {
        var fixture = new Fixture();
        string? hash = mode == "missing-hash" ? null : mode == "invalid-hash" ? "unknown" : fixture.ReportHash;
        Definition[]? definitions = mode == "missing-definitions" ? null : mode == "empty-definitions" ? []
            : mode == "duplicate-definitions" ? [fixture.Definition, fixture.Definition] : [fixture.Definition];
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationSemanticProofs.CreateContext(fixture.Report, hash, definitions));
    }

    [TestMethod]
    public void CatalogContainsOnlyTheThreeSourceBound32MeasurementWitnessesAndUnionCountsADefinitionOnce()
    {
        Assert.HasCount(3, HealthcheckMutationSemanticCatalog.Cases);
        Assert.AreEqual(1, HealthcheckMutationSemanticCatalog.Cases.Select(value => value.Definition.Identity).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(HealthcheckMutationSemanticCatalog.Cases.All(value => value.Definition.Id == "312" && value.Definition.Status == "Killed"));
        Assert.AreEqual(3, HealthcheckMutationSemanticCatalog.Cases.Select(value => value.Guid).Distinct(StringComparer.Ordinal).Count());
        var result = new NativeAuditResult(["312"], [], 1, 1, new JsonObject()) { ReviewedSemanticKilledIds = ["312", "312"] };
        CollectionAssert.AreEqual(new[] { "312" }, result.QualifiedKilledIds);
        CollectionAssert.AreEqual(new[] { "312" }, result.AssertionKilledIds);
        Assert.HasCount(2, result.ReviewedSemanticKilledIds);
    }

    private const string Guid = "11111111-1111-4111-8111-111111111111";
    private const string OtherGuid = "22222222-2222-4222-8222-222222222222";
    private const string Method = "CIS.Tests.UnitTests.Healthcheck.SyntheticSemanticFixture.ReceivesExactly32";
    private const string CoreFilePath = "/synthetic/implementations/csharp/src/CIS.Core/Healthcheck/Test.cs";
    private const string TestRelative = "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Fixture.cs";
    private const string CodeFile = "/synthetic/" + TestRelative;

    // This is parser input, not a fabricated product state or native receipt.
    private sealed class Fixture
    {
        internal string Source { get; } = "// scalar\U0001F600\r\n// next\r\nmeasurements.Count > MaximumMeasurements\r\n";
        internal string TestSource { get; } = "// Explicit synthetic receiving input; not executable native evidence.\n";
        internal string ReportHash { get; set; } = new('a', 64);
        internal Definition Definition { get; set; }
        internal HealthcheckMutationSemanticProofs.ReviewedCase Review { get; }
        internal JsonObject Report { get; }
        internal JsonObject CoreFile => Report["files"]![CoreFilePath]!.AsObject();
        internal JsonObject RawDefinition => CoreFile["mutants"]![0]!.AsObject();
        internal JsonObject TestFile => Report["testFiles"]![CodeFile]!.AsObject();
        internal string NativeGuid { get; set; } = Guid;
        internal string NativeMethod { get; set; } = Method;
        internal string NativeCodeFile { get; set; } = CodeFile;
        internal string Message { get; set; } = "Test method " + Method + " threw exception:\r\nSystem.ArgumentException: Synthetic reviewed valid-input rejection.";
        internal string Stack { get; set; }

        internal Fixture(int observedLine = 123)
        {
            string original = "measurements.Count > MaximumMeasurements";
            int start = Source.IndexOf(original, StringComparison.Ordinal);
            Definition = new("1", "Healthcheck/Test.cs", Sha(Source), start, start + original.Length,
                "Equality mutation", original, "measurements.Count >= MaximumMeasurements", "Killed", [Guid]);
            Review = new(ReportHash, Definition, Guid, Method, TestRelative, Sha(TestSource), "System.ArgumentException",
                ["CIS.Healthcheck.Synthetic.Copy", "CIS.Healthcheck.Synthetic..ctor", "CIS.Healthcheck.Synthetic.Diagnostics"],
                "Synthetic typed receiving-case admission review.", "Synthetic independent matcher control; no production qualification.");
            Report = new JsonObject
            {
                ["files"] = new JsonObject
                {
                    [CoreFilePath] = new JsonObject
                    {
                        ["source"] = Source,
                        ["mutants"] = new JsonArray(new JsonObject
                        {
                            ["id"] = "1",
                            ["status"] = "Killed",
                            ["mutatorName"] = Definition.Mutator,
                            ["replacement"] = Definition.Replacement,
                            ["killedBy"] = new JsonArray(Guid),
                            ["location"] = new JsonObject { ["start"] = Position(1), ["end"] = Position(original.Length + 1) }
                        })
                    }
                },
                ["testFiles"] = new JsonObject
                {
                    [CodeFile] = new JsonObject { ["source"] = TestSource, ["tests"] = new JsonArray(new JsonObject { ["id"] = Guid, ["name"] = Method }) }
                }
            };
            Stack = string.Join('\n', Review.CoreCallMethods.Select(value => "at " + value + "() in " + CoreFilePath + ":line " + observedLine))
                + "\nat " + Method + "() in " + CodeFile + ":line " + observedLine;
        }

        internal HealthcheckMutationSemanticProofs.Context Context() => HealthcheckMutationSemanticProofs.CreateContext(Report, ReportHash, [Definition])!;
        internal bool Match(out JsonObject? witness) => HealthcheckMutationSemanticProofs.MatchReviewedCase(Review, Context(), "1",
            NativeGuid, NativeMethod, NativeCodeFile, Message, Stack, out witness);
        private static JsonObject Position(int column) => new() { ["line"] = 3, ["column"] = column };
        private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
