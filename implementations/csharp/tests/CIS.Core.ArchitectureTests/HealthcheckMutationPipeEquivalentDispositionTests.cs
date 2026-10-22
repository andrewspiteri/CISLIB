using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using V = CIS.Verification.HealthcheckMutationDispositionValidation;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationPipeEquivalentDispositionTests
{
    [TestMethod]
    public void UbuntuCatalogHasOnlyTheTwentyTwoIndividuallyReviewedOriginalDefinitions()
    {
        V.Review[] reviews = HealthcheckMutationPipeEquivalentDispositionCatalog.Entries.ToArray();
        CollectionAssert.AreEquivalent(new[] { "2729", "2735", "2736", "2737", "2738", "2739", "2740", "2742", "2743", "2744", "2745", "2746", "2747", "2748", "2750", "2751", "2752", "2754", "2756", "2757", "2759", "2760" },
            reviews.Select(value => value.Definition.Id).ToArray());
        Assert.HasCount(22, reviews.Select(value => value.Definition.Identity).Distinct().ToArray());
        Assert.IsTrue(reviews.All(value => value.Kind == "Equivalent" && value.Lane == "ubuntu" && value.Group == "pipes"
            && value.Definition.Status == "Survived" && value.Definition.KilledBy.Length == 0 && value.Evidence.Length == 5));
        Assert.IsFalse(reviews.Any(value => value.Definition.Id is "2728" or "2730" or "2731" or "2732" or "2733"));
    }

    [TestMethod]
    [DataRow("2729")]
    [DataRow("2735")]
    [DataRow("2736")]
    [DataRow("2737")]
    [DataRow("2738")]
    [DataRow("2739")]
    [DataRow("2740")]
    [DataRow("2742")]
    [DataRow("2743")]
    [DataRow("2744")]
    [DataRow("2745")]
    [DataRow("2746")]
    [DataRow("2747")]
    [DataRow("2748")]
    [DataRow("2750")]
    [DataRow("2751")]
    [DataRow("2752")]
    [DataRow("2754")]
    [DataRow("2756")]
    [DataRow("2757")]
    [DataRow("2759")]
    [DataRow("2760")]
    public void AnIndividualReviewBindsItsActualShippingSourceAndExactDefinition(string id)
    {
        V.Review review = Review(id);
        V.BoundRecord result = V.Bind(Document(review), "ubuntu", [Source(review)], HealthcheckMutationDispositionCatalog.Entries);
        Assert.AreEqual(review, result.Review);
        Assert.AreEqual(review.Definition.Original, Source(review).Text[review.Definition.Start..review.Definition.End]);
    }

    [TestMethod]
    [DataRow("windows")]
    [DataRow("guard")]
    [DataRow("caller")]
    [DataRow("peer")]
    [DataRow("runtime")]
    [DataRow("status")]
    [DataRow("active-constructor")]
    public void UbuntuReviewCannotMigrateToAnotherDomainOrWeakenItsProof(string mode)
    {
        V.Review review = Review("2729"); JsonObject document = Document(review); Source source = Source(review); string lane = "ubuntu";
        switch (mode)
        {
            case "windows": lane = "windows"; document["osLane"] = lane; break;
            case "guard": source = source with { Text = source.Text.Replace("OperatingSystem.IsWindows()", "!OperatingSystem.IsWindows()", StringComparison.Ordinal) }; break;
            case "caller": document["closure"]![1]!["sha256"] = new string('0', 64); break;
            case "peer": document["evidence"]!.AsArray().RemoveAt(2); break;
            case "runtime": document["evidence"]![3]!["sha256"] = new string('0', 64); break;
            case "status": document["target"]!["definition"]!["status"] = "NoCoverage"; break;
            case "active-constructor": document["target"]!["definition"]!["id"] = "2730"; break;
            default: throw new InvalidOperationException("Unknown individual equivalent control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => V.Bind(document, lane, [source], HealthcheckMutationDispositionCatalog.Entries));
    }

    private static V.Review Review(string id) => HealthcheckMutationPipeEquivalentDispositionCatalog.Entries.Single(value => value.Definition.Id == id);
    private static Source Source(V.Review review)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) { directory = directory.Parent; }
        Assert.IsNotNull(directory, "The native architecture test must run from the repository build tree.");
        string path = Path.Combine(directory.FullName, "implementations/csharp/src/CIS.Core", review.Definition.SourcePath);
        Assert.AreEqual(review.Definition.SourceSha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        return new(review.Definition.SourcePath, review.Definition.SourceSha256, "pipes", File.ReadAllText(path));
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
