using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class OperationScopeQualificationTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray()
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task EverySharedScopeScenarioExecutesAgainstCoreWithBarriers(string id)
    {
        JsonObject catalog = Catalog();
        JsonObject row = Row(catalog, id);
        JsonObject actual = await OperationScopeQualificationFixture.ExecuteAsync(row).ConfigureAwait(false);
        OperationScopeQualificationExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual);
    }

    [TestMethod]
    [DataRow("operation-id")]
    [DataRow("attribute")]
    [DataRow("order")]
    [DataRow("depth")]
    [DataRow("bool-number")]
    [DataRow("missing")]
    public void CompleteScopeComparatorRejectsChangedIdsValuesOrderTypesAndFields(string mutation)
    {
        JsonObject catalog = Catalog();
        JsonObject row = Row(catalog, "SCOPE-CONCURRENT-2");
        JsonObject actual = OperationScopeQualificationExpectations.Expected(row, catalog["nativeBindings"]!.AsObject());
        switch (mutation)
        {
            case "operation-id": actual["children"]![0]!["during"]!["operationId"] = "wrong"; break;
            case "attribute": actual["children"]![0]!["during"]!["attributes"]!["shared"] = "wrong"; break;
            case "order":
                JsonArray children = actual["children"]!.AsArray();
                JsonNode first = children[0]!.DeepClone();
                children[0] = children[1]!.DeepClone();
                children[1] = first;
                break;
            case "depth": actual["children"]![0]!["during"]!["scopeDepth"] = 0; break;
            case "bool-number": actual["parentHeadsUnchanged"] = 1; break;
            case "missing": actual.Remove("parentBefore"); break;
            default: throw new InvalidDataException("Unknown comparator control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => OperationScopeQualificationExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual));
    }

    [TestMethod]
    public async Task JoinedFixtureReportsEveryWorkerFailureWithoutDuplicatingReadiness()
    {
        var first = new InvalidDataException("first");
        var second = new InvalidDataException("second");
        var third = new InvalidDataException("third");
        Task<JsonObject>[] tasks = [Task.FromException<JsonObject>(first), Task.FromException<JsonObject>(second), Task.FromException<JsonObject>(third)];
        AggregateException error = await Assert.ThrowsExactlyAsync<AggregateException>(() => OperationScopeQualificationFixture.JoinAsync(tasks, first));
        Assert.AreEqual(3, error.InnerExceptions.Count);
        Assert.AreSame(first, error.InnerExceptions[0]);
        Assert.AreSame(second, error.InnerExceptions[1]);
        Assert.AreSame(third, error.InnerExceptions[2]);
    }

    [TestMethod]
    public async Task JoinedFixturePreservesAnIndependentControllerFailureAfterWorkersComplete()
    {
        var controller = new InvalidDataException("controller");
        var worker = new InvalidDataException("worker");
        Task<JsonObject>[] tasks = [Task.FromException<JsonObject>(worker), Task.FromResult(new JsonObject())];
        AggregateException error = await Assert.ThrowsExactlyAsync<AggregateException>(() => OperationScopeQualificationFixture.JoinAsync(tasks, controller));
        Assert.AreEqual(2, error.InnerExceptions.Count);
        Assert.AreSame(controller, error.InnerExceptions[0]);
        Assert.AreSame(worker, error.InnerExceptions[1]);
    }

    private static JsonObject Catalog() => QualificationFixtureInputs.ReadLogging("scope-qualification-cases.json");
    private static JsonObject Row(JsonObject catalog, string id) => catalog["cases"]!.AsArray()
        .Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
}
