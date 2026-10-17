using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ExceptionProjectionProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("plain", 1, 1)]
    [DataRow("thrown", 1, 1)]
    [DataRow("inner", 2, 2)]
    [DataRow("aggregate", 4, 2)]
    [DataRow("mutation", 1, 1)]
    [DataRow("null", 0, 0)]
    public async Task ActualExceptionFieldsAreOwnedInFreshUninitializedProcesses(string mode, int nodes, int depth)
    {
        JsonObject actual = await Capture("exception-projection", mode, TestContext);
        Verify(mode, nodes, depth, actual);
    }

    [TestMethod]
    public async Task NullProjectionRequiresItsExplicitExceptionMember()
    {
        JsonObject actual = await Capture("exception-projection", "null", TestContext);
        Verify("null", 0, 0, actual);
        actual.Remove("exception");
        Assert.ThrowsExactly<AssertFailedException>(() => Verify("null", 0, 0, actual));
    }

    private static void Verify(string mode, int nodes, int depth, JsonObject actual)
    {
        Assert.IsTrue(actual.ContainsKey("exception"), "The exception projection member is required.");
        Assert.AreEqual(1, actual["captureCalls"]!.GetValue<int>());
        Assert.AreEqual(nodes, actual["nodes"]!.GetValue<int>());
        Assert.AreEqual(depth, actual["depth"]!.GetValue<int>());
        if (mode == "thrown")
        {
            JsonObject thrown = actual["exception"]!.AsObject();
            CollectionAssert.AreEquivalent(new[] { "type", "message", "stack_trace" }, thrown.Select(pair => pair.Key).ToArray());
            Assert.AreEqual("System.InvalidOperationException", (string?)thrown["type"]);
            Assert.AreEqual("thrown", (string?)thrown["message"]);
            StringAssert.Contains((string)thrown["stack_trace"]!, "ThrowMeasured");
            return;
        }
        string expected = mode switch
        {
            "plain" => """{"type":"System.InvalidOperationException","message":"plain😀\n"}""",
            "inner" => """{"type":"System.InvalidOperationException","message":"outer","inner_exceptions":[{"type":"System.ArgumentException","message":"inner"}]}""",
            "aggregate" => """{"type":"System.AggregateException","message":"aggregate (first) (second) (third)","inner_exceptions":[{"type":"System.Exception","message":"first"},{"type":"System.Exception","message":"second"},{"type":"System.Exception","message":"third"}]}""",
            "mutation" => """{"type":"CIS.Tests.DiscoveryFixture.ExceptionProjectionProcessFixture+MutableException","message":"before","stack_trace":"captured stack"}""",
            "null" => "null",
            _ => throw new InvalidDataException("Unknown exception projection.")
        };
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(expected), actual["exception"]));
        if (mode == "mutation")
        {
            Assert.AreEqual(1, actual["messageReads"]!.GetValue<int>());
            Assert.AreEqual(1, actual["stackReads"]!.GetValue<int>());
        }
    }

    internal static async Task<JsonObject> Capture(string command, string id, TestContext context, string? input = null, string? sentinel = null)
    {
        using var owned = new TemporaryDirectory();
        string[] arguments = input is null ? [command, id] : [command, input, id];
        var environment = new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" };
        if (sentinel is not null) { environment["CIS_PRIVATE_EXCEPTION_TOKEN"] = sentinel; }
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync(arguments, owned.Path, context, input, environment);
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.IsFalse(actual["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.IsFalse(actual.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        if (sentinel is not null) { Assert.IsFalse(actual.ToJsonString().Contains(sentinel, StringComparison.Ordinal)); }
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        return actual;
    }
}
