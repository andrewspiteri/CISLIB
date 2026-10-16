using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ExceptionBoundaryProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Cases => ExceptionBoundarySources.Cases.Select(id => new object[] { id });

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task ActualExceptionBoundaryCasesRunInFreshUninitializedProcesses(string id)
    {
        JsonObject actual = await ExceptionProjectionProcessTests.Capture("exception-boundary", id, TestContext);
        ExceptionBoundaryExpectations.Verify(id, actual);
        ExceptionBoundaryExpectations.VerifyReads(id, actual);
        CheckShape(actual);
    }

    // Traverse emitted JSON independently of Core's exception graph and cached measurements.
    internal static void CheckShape(JsonObject actual, bool requirePreorder = true)
    {
        var pending = new Stack<(JsonNode Node, int Depth)>();
        if (actual["exception"] is JsonNode root) { pending.Push((root, 1)); }
        var labels = new List<string>();
        int maximum = 0;
        bool loss = false;
        string[] allowed = ["type", "message", "stack_trace", "inner_exceptions", "truncated"];
        while (pending.TryPop(out var item))
        {
            Assert.IsTrue(item.Node.AsObject().All(pair => allowed.Contains(pair.Key, StringComparer.Ordinal)));
            Assert.IsFalse(string.IsNullOrWhiteSpace((string?)item.Node["type"]));
            string message = (string)item.Node["message"]!;
            Assert.IsTrue(message.EnumerateRunes().Count() <= 4096);
            labels.Add(message);
            maximum = Math.Max(maximum, item.Depth);
            Assert.IsTrue(labels.Count <= 16 && maximum <= 4);
            if (item.Node["stack_trace"] is JsonNode stack) { Assert.IsTrue(stack.GetValue<string>().EnumerateRunes().Count() <= 16384); }
            if (item.Node["truncated"] is JsonNode marker) { Assert.IsTrue(marker.GetValue<bool>()); loss = true; }
            if (item.Node["inner_exceptions"] is JsonArray children)
            {
                foreach (JsonNode? child in children.Reverse()) { pending.Push((child!, item.Depth + 1)); }
            }
        }
        Assert.AreEqual(labels.Count, actual["nodes"]!.GetValue<int>());
        Assert.AreEqual(labels.Count, actual["cachedNodes"]!.GetValue<int>());
        Assert.AreEqual(maximum, actual["depth"]!.GetValue<int>());
        Assert.AreEqual(maximum, actual["cachedDepth"]!.GetValue<int>());
        Assert.AreEqual(loss, actual["hasTruncatedContent"]!.GetValue<bool>());
        if (requirePreorder) { CollectionAssert.AreEqual(labels.ToArray(), actual["preorder"]!.AsArray().Select(value => (string)value!).ToArray()); }
    }
}
