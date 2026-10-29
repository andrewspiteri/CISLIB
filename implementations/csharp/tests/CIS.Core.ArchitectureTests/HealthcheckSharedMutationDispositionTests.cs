using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Reader = CIS.Verification.HealthcheckSharedMutationDispositions;
using Shared = CIS.Verification.HealthcheckSharedMutationModel;
using Validation = CIS.Verification.HealthcheckMutationDispositionValidation;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckSharedMutationDispositionTests
{
    [TestMethod]
    [DataRow("ReviewedNoncritical", 4L)]
    [DataRow("Equivalent", 3L)]
    public void ExactMaintainedReviewBindsItsPerFileOriginalWithoutInventingKills(string kind, long denominator)
    {
        var fixture = new Fixture(kind);
        string before = JsonSerializer.Serialize(fixture.Batches);
        var observations = new JsonArray();

        Shared.Review[] reviews = Reader.Bind([fixture.Bound], fixture.Batches, observations);
        JsonObject result = Shared.Evaluate(fixture.Sources, fixture.Batches, reviews);

        Assert.HasCount(1, reviews);
        Assert.AreEqual(fixture.Definition.Identity, reviews[0].DefinitionIdentity);
        Assert.AreEqual(fixture.Report, reviews[0].OriginalReportSha256);
        Assert.AreEqual(3L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(denominator, (long?)result["aggregate"]!["denominator"]);
        Assert.AreEqual(4L, (long?)result["rawAggregate"]!["denominator"]);
        Assert.AreEqual(0, observations[0]!["numeratorCredit"]!.GetValue<int>());
        Assert.IsFalse(observations[0]!["globalIntegrityIssuesWaived"]!.GetValue<bool>());
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Batches));
    }

    [TestMethod]
    [DataRow("report")]
    [DataRow("group")]
    [DataRow("source")]
    [DataRow("native-id")]
    [DataRow("span")]
    [DataRow("operator")]
    [DataRow("replacement")]
    [DataRow("status")]
    [DataRow("killed-by")]
    public void ASourceReviewCannotMigrateToAnotherOriginalNativeBinding(string difference)
    {
        var fixture = new Fixture();
        Shared.Batch original = fixture.Batches[0];
        Definition changed = fixture.Definition;
        switch (difference)
        {
            case "report": original = original with { ReportSha256 = Sha("another original report") }; break;
            case "group": original = original with { Source = original.Source with { Group = "pipes" } }; break;
            case "source": original = original with { Source = original.Source with { Sha256 = Sha("changed source") } }; break;
            case "native-id": changed = changed with { Id = "18" }; break;
            case "span": changed = changed with { End = changed.End - 1 }; break;
            case "operator": changed = changed with { Mutator = "Another mutation" }; break;
            case "replacement": changed = changed with { Replacement = "different" }; break;
            case "status": changed = changed with { Status = "Timeout" }; break;
            case "killed-by": changed = changed with { KilledBy = [Guid.Empty.ToString()] }; break;
            default: throw new InvalidOperationException("Unknown original binding control.");
        }
        original = original with { Definitions = [original.Definitions[0], changed] };
        Shared.Batch[] batches = [original, .. fixture.Batches.Skip(1)];

        Assert.ThrowsExactly<InvalidDataException>(() => Reader.Bind([fixture.Bound], batches, new JsonArray()));
    }

    [TestMethod]
    public void OneDefinitionCannotReceiveDuplicateOrConflictingReviewKinds()
    {
        var fixture = new Fixture();
        var alternate = fixture.Bound with { Review = fixture.Bound.Review with { Id = "another.review", Kind = "Equivalent" } };
        Assert.ThrowsExactly<InvalidDataException>(() => Reader.Bind([fixture.Bound, alternate], fixture.Batches, new JsonArray()));
        Assert.ThrowsExactly<InvalidDataException>(() => Reader.Bind([fixture.Bound], [fixture.Batches[0], .. fixture.Batches], new JsonArray()));
        Assert.ThrowsExactly<InvalidDataException>(() => Reader.Bind([fixture.Bound], [], new JsonArray()));
    }

    [TestMethod]
    public void AReviewedMissDoesNotWaiveARealReceiverHoldOrCompleteMissingSources()
    {
        var fixture = new Fixture();
        Shared.Batch[] held = [fixture.Batches[0] with { Issues = [new("actual-execution", "Actual native work is unresolved.", [])] }, .. fixture.Batches.Skip(1)];
        Shared.Review[] reviews = Reader.Bind([fixture.Bound], held, new JsonArray());
        JsonObject result = Shared.Evaluate(fixture.Sources, held, reviews);
        Assert.AreEqual(false, (bool?)result["qualified"]);
        Assert.HasCount(1, result["receiverHolds"]!.AsArray());
        Assert.AreEqual(3L, (long?)result["aggregate"]!["numerator"]);
        Assert.AreEqual(4L, (long?)result["aggregate"]!["denominator"]);

        JsonObject incomplete = Shared.Evaluate(fixture.Sources, [held[0]], reviews);
        Assert.AreEqual(false, (bool?)incomplete["complete"]);
        Assert.AreEqual(false, (bool?)incomplete["qualified"]);
        Assert.IsNull(incomplete["aggregate"]);
    }

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // Synthetic model inputs exercise the review-to-original adapter. They do
    // not constitute native execution evidence or maintain a real disposition.
    private sealed class Fixture
    {
        internal string Report { get; } = Sha("original unit report");
        internal Source[] Sources { get; }
        internal Definition Definition { get; }
        internal Validation.BoundRecord Bound { get; }
        internal Shared.Batch[] Batches { get; }
        internal Fixture(string kind = "ReviewedNoncritical")
        {
            Sources = [new("Healthcheck/Test.cs", Sha("guard"), "unit-runtime", "guard"),
                new("Healthcheck/Pipes/Test.cs", Sha("pipe"), "pipes", "pipe"),
                new("Healthcheck/AspNetCore/Test.cs", Sha("http"), "aspnet", "http")];
            Definition = new("17", Sources[0].Path, Sources[0].Sha256, 0, 5, "Boolean mutation", "guard", "false", "Survived", []);
            var review = new Validation.Review("synthetic.review", kind, "ubuntu", "unit-runtime", Report, Definition,
                [new("implementations/csharp/src/CIS.Core/Healthcheck/Test.cs", Sources[0].Sha256, 5)],
                [new(".artifacts/primary.md", Sha("primary"), 7), new(".artifacts/independent.md", Sha("independent"), 11)],
                "Synthetic exact-binding control; no real native review is approved.");
            JsonObject document = JsonSerializer.SerializeToNode(new
            {
                schema = Validation.Schema,
                reviewId = review.Id,
                kind,
                osLane = review.Lane,
                target = new
                {
                    group = review.Group,
                    reportSha256 = Report,
                    definition = new
                    {
                        id = Definition.Id,
                        identity = Definition.Identity,
                        sourcePath = Definition.SourcePath,
                        sourceSha256 = Definition.SourceSha256,
                        start = Definition.Start,
                        end = Definition.End,
                        mutator = Definition.Mutator,
                        original = Definition.Original,
                        replacement = Definition.Replacement,
                        status = Definition.Status
                    }
                },
                closure = review.Closure.Select(Validation.Reference),
                evidence = review.Evidence.Select(Validation.Reference)
            })!.AsObject();
            Bound = Validation.Bind(document, "ubuntu", Sources, [review]);
            Batches = Sources.Select((source, index) =>
            {
                var killed = new Definition("0", source.Path, source.Sha256, 0, source.Text.Length, "Statement mutation", source.Text, ";", "Killed", []);
                return new Shared.Batch(source, index == 0 ? Report : Sha(source.Path + " original report"),
                    index == 0 ? [killed, Definition] : [killed], ["0"], [], []);
            }).ToArray();
        }
    }
}
