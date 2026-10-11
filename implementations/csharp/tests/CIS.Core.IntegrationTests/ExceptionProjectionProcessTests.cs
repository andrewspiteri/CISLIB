using System.Security.Cryptography;
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
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        using var owned = new TemporaryDirectory();
        ProcessResult child = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "exception-projection", mode],
            owned.Path, TimeSpan.FromSeconds(15), new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" });
        Assert.AreEqual(0, child.ExitCode, child.StandardError);
        Assert.AreEqual(string.Empty, child.StandardError);
        TestContext.WriteLine(child.StandardOutput);
        JsonObject actual = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(mode, actual["caseId"]!.GetValue<string>());
        Assert.AreEqual(1, actual["captureCalls"]!.GetValue<int>());
        Assert.AreEqual(nodes, actual["nodes"]!.GetValue<int>());
        Assert.AreEqual(depth, actual["depth"]!.GetValue<int>());
        VerifyProjection(mode, actual);
        JsonNode observed = actual["observed"]!;
        Assert.IsFalse(observed["publicFacadeInitialized"]!.GetValue<bool>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.IsFalse(child.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    private static void VerifyProjection(string mode, JsonObject actual)
    {
        if (mode == "null")
        {
            Assert.IsNull(actual["exception"]);
            return;
        }
        JsonNode value = actual["exception"]!;
        Assert.IsNull(value["truncated"]);
        if (mode == "aggregate")
        {
            Assert.AreEqual("System.AggregateException", value["type"]!.GetValue<string>());
            Assert.AreEqual("aggregate (first) (second) (third)", value["message"]!.GetValue<string>());
            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, value["inner_exceptions"]!.AsArray().Select(item => item!["message"]!.GetValue<string>()).ToArray());
            Assert.IsTrue(value["inner_exceptions"]!.AsArray().All(item => item!["type"]!.GetValue<string>() == "System.Exception"));
        }
        else if (mode == "mutation")
        {
            Assert.AreEqual("before", value["message"]!.GetValue<string>());
            Assert.AreEqual("captured stack", value["stack_trace"]!.GetValue<string>());
            Assert.AreEqual(1, actual["messageReads"]!.GetValue<int>());
            Assert.AreEqual(1, actual["stackReads"]!.GetValue<int>());
        }
        else
        {
            Assert.AreEqual("System.InvalidOperationException", value["type"]!.GetValue<string>());
            string expected = mode == "plain" ? "plain😀\n" : mode;
            if (mode == "inner")
            {
                expected = "outer";
                Assert.AreEqual(1, value["inner_exceptions"]!.AsArray().Count);
                Assert.AreEqual("System.ArgumentException", value["inner_exceptions"]![0]!["type"]!.GetValue<string>());
                Assert.AreEqual("inner", value["inner_exceptions"]![0]!["message"]!.GetValue<string>());
            }
            Assert.AreEqual(expected, value["message"]!.GetValue<string>());
            if (mode == "thrown")
            {
                Assert.IsTrue(value["stack_trace"]!.GetValue<string>().Contains("ThrowMeasured", StringComparison.Ordinal));
            }
            else
            {
                Assert.IsNull(value["stack_trace"]);
            }
        }
        Assert.IsNull(value["Data"]);
        Assert.IsNull(value["data"]);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
