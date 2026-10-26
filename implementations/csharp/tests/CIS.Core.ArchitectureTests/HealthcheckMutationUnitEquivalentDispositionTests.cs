using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using V = CIS.Verification.HealthcheckMutationDispositionValidation;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationUnitEquivalentDispositionTests
{
    [TestMethod]
    public void CatalogContainsOnlyTheFifteenExactOriginalUnit18SourceClosureReviews()
    {
        V.Review[] entries = HealthcheckMutationUnitEquivalentDispositionCatalog.Entries.ToArray();
        CollectionAssert.AreEqual(new[] { "392", "610", "928", "941", "973", "982", "2588", "2605", "2610", "2646", "2651", "3187", "3214", "3253", "3366" },
            entries.Select(value => value.Definition.Id).ToArray());
        Assert.HasCount(15, entries.Select(value => value.Definition.Identity).Distinct(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(entries.All(value => value.Kind == "Equivalent" && value.Lane == "ubuntu" && value.Group == "unit-runtime"
            && value.ReportSha256 == HealthcheckMutationSemanticCatalog.Unit18ReportSha256 && value.Definition.KilledBy.Length == 0
            && value.Evidence.Length == 4 && value.Closure.Length == 52));
        Assert.HasCount(14, entries.Where(value => value.Definition.Status == "Survived").ToArray());
        CollectionAssert.AreEqual(new[] { "2605" }, entries.Where(value => value.Definition.Status == "NoCoverage").Select(value => value.Definition.Id).ToArray());
        Assert.IsFalse(entries.Any(value => value.Definition.Id is "991" or "1024" or "885" or "886" or "889" or "890"));
    }

    [TestMethod]
    [DataRow("392")]
    [DataRow("610")]
    [DataRow("928")]
    [DataRow("941")]
    [DataRow("973")]
    [DataRow("982")]
    [DataRow("2588")]
    [DataRow("2605")]
    [DataRow("2610")]
    [DataRow("2646")]
    [DataRow("2651")]
    [DataRow("3187")]
    [DataRow("3214")]
    [DataRow("3253")]
    [DataRow("3366")]
    public void EachIndividualDocumentBindsItsActualCurrentSourceAndCannotApplyWithoutTheCompleteUnion(string id)
    {
        V.Review review = Review(id);
        V.BoundRecord bound = V.Bind(Document(review), "ubuntu", [CurrentSource(review)], HealthcheckMutationUnitEquivalentDispositionCatalog.Entries);
        Assert.AreEqual(review, bound.Review);
        Assert.ThrowsExactly<InvalidDataException>(() => V.Complete(bound, []));
        var unit = new Partition("unit-runtime", review.ReportSha256, [CurrentSource(review)], [review.Definition], [], []);
        Assert.ThrowsExactly<InvalidDataException>(() => V.Complete(bound, [unit]));
    }

    [TestMethod]
    [DataRow("windows")]
    [DataRow("old14-report")]
    [DataRow("group")]
    [DataRow("unknown-review")]
    [DataRow("kind")]
    [DataRow("definition-id")]
    [DataRow("identity")]
    [DataRow("operator")]
    [DataRow("replacement")]
    [DataRow("source-hash")]
    [DataRow("source-text")]
    [DataRow("start")]
    [DataRow("status")]
    [DataRow("caller-closure")]
    [DataRow("missing-closure")]
    [DataRow("duplicate-closure")]
    [DataRow("primary")]
    [DataRow("peer")]
    [DataRow("historical-acceptance")]
    [DataRow("current-rebind")]
    [DataRow("missing-evidence")]
    [DataRow("approval-flag")]
    [DataRow("raw-killed")]
    public void AReviewedDefinitionCannotMigrateAcrossReportsLanesOrWeakenItsProofClosure(string mode)
    {
        V.Review review = Review("941");
        JsonObject document = Document(review); Source source = CurrentSource(review); string lane = "ubuntu";
        JsonObject target = document["target"]!.AsObject(), definition = target["definition"]!.AsObject();
        switch (mode)
        {
            case "windows": lane = "windows"; document["osLane"] = lane; break;
            case "old14-report": target["reportSha256"] = "9b027c1973f50a85d5017d0a894ab8cd57f622d3cd2554bda346107424b98301"; break;
            case "group": target["group"] = "pipes"; break;
            case "unknown-review": document["reviewId"] = "unit18.991.equivalent-shipping-closure"; break;
            case "kind": document["kind"] = "ReviewedNoncritical"; break;
            case "definition-id": definition["id"] = "991"; break;
            case "identity": definition["identity"] = new string('0', 64); break;
            case "operator": definition["mutator"] = "Other mutation"; break;
            case "replacement": definition["replacement"] = "false"; break;
            case "source-hash": source = source with { Sha256 = new string('0', 64) }; break;
            case "source-text": source = source with { Text = source.Text + " " }; break;
            case "start": definition["start"] = review.Definition.Start + 1; break;
            case "status": definition["status"] = "NoCoverage"; break;
            case "caller-closure": document["closure"]![0]!["sha256"] = new string('0', 64); break;
            case "missing-closure": document["closure"]!.AsArray().RemoveAt(0); break;
            case "duplicate-closure": document["closure"]!.AsArray().Add(document["closure"]![0]!.DeepClone()); break;
            case "primary": document["evidence"]![0]!["sha256"] = new string('0', 64); break;
            case "peer": document["evidence"]![1]!["sha256"] = new string('0', 64); break;
            case "historical-acceptance": document["evidence"]![2]!["sha256"] = new string('0', 64); break;
            case "current-rebind": document["evidence"]![3]!["sha256"] = new string('0', 64); break;
            case "missing-evidence": document["evidence"]!.AsArray().RemoveAt(3); break;
            case "approval-flag": document["accepted"] = true; break;
            case "raw-killed": definition["status"] = "Killed"; break;
            default: throw new InvalidOperationException("Unknown Unit equivalent control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => V.Bind(document, lane, [source], HealthcheckMutationUnitEquivalentDispositionCatalog.Entries));
    }

    [TestMethod]
    public void TheUnreachableNoCoverageReturnDoesNotAuthorizeOtherNoCoverageDefinitions()
    {
        V.Review review = Review("2605");
        JsonObject document = Document(review);
        document["target"]!["definition"]!["id"] = "2606";
        Assert.ThrowsExactly<InvalidDataException>(() => V.Bind(document, "ubuntu", [CurrentSource(review)], HealthcheckMutationUnitEquivalentDispositionCatalog.Entries));
    }

    private static V.Review Review(string id) => HealthcheckMutationUnitEquivalentDispositionCatalog.Entries.Single(value => value.Definition.Id == id);

    private static Source CurrentSource(V.Review review)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) { directory = directory.Parent; }
        Assert.IsNotNull(directory, "The native architecture tests require their repository build tree.");
        string path = Path.Combine(directory.FullName, "implementations/csharp/src/CIS.Core", review.Definition.SourcePath);
        Assert.AreEqual(review.Definition.SourceSha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        return new(review.Definition.SourcePath, review.Definition.SourceSha256, "unit-runtime", File.ReadAllText(path));
    }

    private static JsonObject Document(V.Review review)
    {
        Definition definition = review.Definition;
        return new JsonObject
        {
            ["schema"] = V.Schema,
            ["reviewId"] = review.Id,
            ["kind"] = review.Kind,
            ["osLane"] = review.Lane,
            ["target"] = new JsonObject
            {
                ["group"] = review.Group,
                ["reportSha256"] = review.ReportSha256,
                ["definition"] = new JsonObject
                {
                    ["id"] = definition.Id,
                    ["identity"] = definition.Identity,
                    ["sourcePath"] = definition.SourcePath,
                    ["sourceSha256"] = definition.SourceSha256,
                    ["start"] = definition.Start,
                    ["end"] = definition.End,
                    ["mutator"] = definition.Mutator,
                    ["original"] = definition.Original,
                    ["replacement"] = definition.Replacement,
                    ["status"] = definition.Status
                }
            },
            ["closure"] = new JsonArray(review.Closure.Select(pin => (JsonNode?)V.Reference(pin)).ToArray()),
            ["evidence"] = new JsonArray(review.Evidence.Select(pin => (JsonNode?)V.Reference(pin)).ToArray())
        };
    }
}
