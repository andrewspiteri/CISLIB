using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Validation = CIS.Verification.HealthcheckMutationDispositionValidation;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationDispositionTests
{
    [TestMethod]
    [DataRow("Equivalent", "Survived")]
    [DataRow("Equivalent", "NoCoverage")]
    [DataRow("ReviewedNoncritical", "Survived")]
    [DataRow("ReviewedNoncritical", "NoCoverage")]
    public void ReviewedRecordsBindExactSourceAndPreserveTheSpecifiedAccounting(string kind, string status)
    {
        var fixture = new Fixture(kind, status);
        string document = fixture.Document.ToJsonString();
        Validation.BoundRecord bound = fixture.Bind();
        Adjustment adjustment = Validation.Complete(bound, fixture.Partitions());
        JsonObject result = Evaluate(fixture.Sources, fixture.Partitions(), [adjustment]);
        Assert.AreEqual(kind, adjustment.Kind);
        Assert.AreEqual(2L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(kind == "Equivalent" ? 2L : 3L, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(3L, (long?)result["rawAggregate"]!["denominator"]);
        Assert.AreEqual(2L, (long?)result["rawAggregate"]!["killed"]);
        Assert.AreEqual(document, fixture.Document.ToJsonString());
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("unknown-review")]
    [DataRow("lane")]
    [DataRow("group")]
    [DataRow("report")]
    [DataRow("id")]
    [DataRow("identity")]
    [DataRow("source-path")]
    [DataRow("source-hash")]
    [DataRow("start")]
    [DataRow("end")]
    [DataRow("mutator")]
    [DataRow("original")]
    [DataRow("replacement")]
    [DataRow("status")]
    [DataRow("approval-flag")]
    [DataRow("missing-field")]
    [DataRow("live-source-hash")]
    [DataRow("live-source-text")]
    public void AReviewNameOrNumericIdCannotSubstituteForTheExactLiveBinding(string mode)
    {
        var fixture = new Fixture();
        JsonObject target = fixture.Document["target"]!.AsObject(), definition = target["definition"]!.AsObject();
        switch (mode)
        {
            case "schema": fixture.Document["schema"] = "unknown"; break;
            case "unknown-review": fixture.Document["reviewId"] = "unreviewed"; break;
            case "lane": fixture.Document["osLane"] = "windows"; break;
            case "group": target["group"] = "pipes"; break;
            case "report": target["reportSha256"] = Hash('f'); break;
            case "id": definition["id"] = "999"; break;
            case "identity": definition["identity"] = Hash('f'); break;
            case "source-path": definition["sourcePath"] = "Healthcheck/Other.cs"; break;
            case "source-hash": definition["sourceSha256"] = Hash('f'); break;
            case "start": definition["start"] = 1; break;
            case "end": definition["end"] = 1; break;
            case "mutator": definition["mutator"] = "Other mutation"; break;
            case "original": definition["original"] = "other"; break;
            case "replacement": definition["replacement"] = "other"; break;
            case "status": definition["status"] = "Killed"; break;
            case "approval-flag": fixture.Document["accepted"] = true; break;
            case "missing-field": definition.Remove("sourceSha256"); break;
            case "live-source-hash": fixture.Sources[0] = fixture.Sources[0] with { Sha256 = Hash('f') }; break;
            case "live-source-text": fixture.Sources[0] = fixture.Sources[0] with { Text = "changed" }; break;
            default: throw new InvalidOperationException("Unknown disposition binding control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Bind());
    }

    [TestMethod]
    [DataRow("missing-closure")]
    [DataRow("extra-closure")]
    [DataRow("duplicate-closure")]
    [DataRow("changed-caller")]
    [DataRow("changed-source-size")]
    [DataRow("changed-source-path")]
    [DataRow("missing-peer")]
    [DataRow("changed-peer")]
    [DataRow("changed-proposal")]
    [DataRow("duplicate-evidence")]
    public void CompleteCallerContractAndIndependentEvidencePinsAreRequired(string mode)
    {
        var fixture = new Fixture();
        JsonArray closure = fixture.Document["closure"]!.AsArray(), evidence = fixture.Document["evidence"]!.AsArray();
        switch (mode)
        {
            case "missing-closure": closure.RemoveAt(1); break;
            case "extra-closure": closure.Add(Validation.Reference(new Validation.Pin("contracts/extra.md", Hash('f'), 1))); break;
            case "duplicate-closure": closure.Add(closure[0]!.DeepClone()); break;
            case "changed-caller": closure[1]!["sha256"] = Hash('f'); break;
            case "changed-source-size": closure[0]!["bytes"] = 999L; break;
            case "changed-source-path": closure[0]!["path"] = "implementations/csharp/src/CIS.Core/Healthcheck/Other.cs"; break;
            case "missing-peer": evidence.RemoveAt(1); break;
            case "changed-peer": evidence[1]!["sha256"] = Hash('f'); break;
            case "changed-proposal": evidence[0]!["sha256"] = Hash('f'); break;
            case "duplicate-evidence": evidence.Add(evidence[0]!.DeepClone()); break;
            default: throw new InvalidOperationException("Unknown disposition closure control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Bind());
    }

    [TestMethod]
    [DataRow("AssertionKill")]
    [DataRow("NonTermination")]
    [DataRow("Approved")]
    [DataRow("Qualified")]
    public void AReceiptCannotRequestAnUnsupportedProofKind(string kind)
    {
        var fixture = new Fixture();
        fixture.Document["kind"] = kind;
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Bind());
    }

    [TestMethod]
    [DataRow("traversal")]
    [DataRow("absolute")]
    [DataRow("backslash")]
    [DataRow("unknown-flag")]
    [DataRow("bad-sha")]
    [DataRow("negative-size")]
    public void EvidenceReferenceHasAStrictPortableIdentity(string mode)
    {
        JsonObject reference = Validation.Reference(new Validation.Pin(".artifacts/review/data.json", Hash('a'), 12));
        switch (mode)
        {
            case "traversal": reference["path"] = "../data.json"; break;
            case "absolute": reference["path"] = "C:/data.json"; break;
            case "backslash": reference["path"] = ".artifacts\\data.json"; break;
            case "unknown-flag": reference["qualified"] = true; break;
            case "bad-sha": reference["sha256"] = "none"; break;
            case "negative-size": reference["bytes"] = -1L; break;
            default: throw new InvalidOperationException("Unknown reference control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Validation.ReadReference(reference));
    }

    [TestMethod]
    [DataRow("missing-group")]
    [DataRow("duplicate-group")]
    [DataRow("changed-report")]
    [DataRow("changed-status")]
    [DataRow("different-id")]
    public void ApplicationRequiresTheActualCompleteThreeReportTarget(string mode)
    {
        var fixture = new Fixture();
        Validation.BoundRecord bound = fixture.Bind();
        Partition[] partitions = fixture.Partitions();
        switch (mode)
        {
            case "missing-group": partitions = partitions[..2]; break;
            case "duplicate-group": partitions[2] = partitions[1]; break;
            case "changed-report": partitions[0] = partitions[0] with { ReportSha256 = Hash('f') }; break;
            case "changed-status": partitions[0] = partitions[0] with { Definitions = [fixture.Definition with { Status = "Killed" }] }; break;
            case "different-id": partitions[0] = partitions[0] with { Definitions = [fixture.Definition with { Id = "999" }] }; break;
            default: throw new InvalidOperationException("Unknown complete-union control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Validation.Complete(bound, partitions));
    }

    [TestMethod]
    [DataRow("Equivalent")]
    [DataRow("ReviewedNoncritical")]
    public void AValidDispositionNeverWaivesGlobalNativeOrChildFailures(string kind)
    {
        var fixture = new Fixture(kind);
        Partition[] partitions = fixture.Partitions();
        partitions[0] = partitions[0] with { IntegrityIssues = [new("NativeHostMalformed", "Explicit synthetic global failure", [])] };
        Adjustment adjustment = Validation.Complete(fixture.Bind(), partitions);
        JsonObject result = Evaluate(fixture.Sources, partitions, [adjustment]);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.HasCount(1, result["issues"]!.AsArray());
        Assert.AreEqual("NativeHostMalformed", (string?)result["issues"]![0]!["code"]);
    }

    [TestMethod]
    public void MaintainedCatalogRetainsOnlyReviewedOriginalSurvivorsWithoutEquivalentExclusions()
    {
        Assert.HasCount(8, HealthcheckMutationDispositionCatalog.Entries);
        CollectionAssert.AreEquivalent(new[] { "200", "230", "241", "245", "255", "260", "267", "274" },
            HealthcheckMutationDispositionCatalog.Entries.Select(entry => entry.Definition.Id).ToArray());
        Assert.IsTrue(HealthcheckMutationDispositionCatalog.Entries.All(entry => entry.Kind == "ReviewedNoncritical"
            && entry.Definition.Status == "Survived" && entry.Definition.KilledBy.Length == 0));
        Validation.Review review = HealthcheckMutationDispositionCatalog.Entries[0];
        Assert.AreEqual("ReviewedNoncritical", review.Kind);
        Assert.AreEqual("200", review.Definition.Id);
        Assert.AreEqual("Survived", review.Definition.Status);
        Assert.AreEqual("e977dbcf1eae38b88f333417369e35362525134986c3a11c2daea2b3a2485c46", review.Definition.Identity);
        Assert.HasCount(4, review.Closure);
        Assert.HasCount(2, review.Evidence);
        Assert.AreNotEqual(review.Evidence[0].Path, review.Evidence[1].Path);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RenamingAPrimaryReviewCannotSupplyAnIndependentReview(bool copiedBody)
    {
        var fixture = new Fixture();
        Validation.Pin[] evidence = copiedBody
            ? [fixture.Review.Evidence[0], fixture.Review.Evidence[1] with { Sha256 = fixture.Review.Evidence[0].Sha256 }]
            : [fixture.Review.Evidence[0]];
        fixture.Document["evidence"] = new JsonArray(evidence.Select(pin => (JsonNode)Validation.Reference(pin)).ToArray());
        Validation.Review review = fixture.Review with { Evidence = evidence };
        Assert.ThrowsExactly<InvalidDataException>(() => Validation.Bind(fixture.Document, "ubuntu", fixture.Sources, [review]));
    }

    private static string Hash(char value) => new(value, 64);
    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // Typed synthetic receiving controls test the parser/binder, not actual
    // mutation outcomes or the authority of an externally supplied JSON flag.
    private sealed class Fixture
    {
        internal Definition Definition { get; }
        internal Validation.Review Review { get; }
        internal Source[] Sources { get; }
        internal JsonObject Document { get; }

        internal Fixture(string kind = "ReviewedNoncritical", string status = "Survived")
        {
            Sources = [new("Healthcheck/Test.cs", Sha("guard"), "unit-runtime", "guard"),
                new("Healthcheck/Pipes/Test.cs", Sha("pipe"), "pipes", "pipe"),
                new("Healthcheck/AspNetCore/Test.cs", Sha("http"), "aspnet", "http")];
            Definition = new("1", Sources[0].Path, Sources[0].Sha256, 0, 5, "Boolean mutation", "guard", "false", status, []);
            Review = new("synthetic.review", kind, "ubuntu", "unit-runtime", Hash('a'), Definition,
                [new("implementations/csharp/src/CIS.Core/Healthcheck/Test.cs", Sources[0].Sha256, 5), new("contracts/caller.md", Hash('b'), 3)],
                [new(".artifacts/proposal.json", Hash('c'), 10), new(".artifacts/peer.json", Hash('d'), 20)],
                "Explicit synthetic receiving rationale; no actual qualification claim.");
            Document = new JsonObject
            {
                ["schema"] = Validation.Schema,
                ["reviewId"] = Review.Id,
                ["kind"] = kind,
                ["osLane"] = "ubuntu",
                ["target"] = new JsonObject
                {
                    ["group"] = Review.Group,
                    ["reportSha256"] = Review.ReportSha256,
                    ["definition"] = new JsonObject
                    {
                        ["id"] = Definition.Id,
                        ["identity"] = Definition.Identity,
                        ["sourcePath"] = Definition.SourcePath,
                        ["sourceSha256"] = Definition.SourceSha256,
                        ["start"] = Definition.Start,
                        ["end"] = Definition.End,
                        ["mutator"] = Definition.Mutator,
                        ["original"] = Definition.Original,
                        ["replacement"] = Definition.Replacement,
                        ["status"] = Definition.Status
                    }
                },
                ["closure"] = new JsonArray(Review.Closure.Select(pin => (JsonNode)Validation.Reference(pin)).ToArray()),
                ["evidence"] = new JsonArray(Review.Evidence.Select(pin => (JsonNode)Validation.Reference(pin)).ToArray())
            };
        }

        internal Validation.BoundRecord Bind() => Validation.Bind(Document, "ubuntu", Sources, [Review]);
        internal Partition[] Partitions() => Sources.Select((source, index) =>
        {
            Definition definition = index == 0 ? Definition : new Definition((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                source.Path, source.Sha256, 0, source.Text.Length, "Boolean mutation", source.Text, "false", "Killed", []);
            return new Partition(source.Group, index == 0 ? Review.ReportSha256 : Hash((char)('a' + index)), [source], [definition],
                index == 0 ? [] : [definition.Id], []);
        }).ToArray();
    }
}
