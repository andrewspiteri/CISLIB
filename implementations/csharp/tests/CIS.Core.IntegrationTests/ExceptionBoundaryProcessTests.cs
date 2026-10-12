using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
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
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        using var owned = new TemporaryDirectory();
        ProcessResult child = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "exception-boundary", id],
            owned.Path, TimeSpan.FromSeconds(15), new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" });
        Assert.AreEqual(0, child.ExitCode, child.StandardError);
        Assert.AreEqual(string.Empty, child.StandardError);
        TestContext.WriteLine(child.StandardOutput);
        JsonObject actual = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(id, actual["caseId"]!.GetValue<string>());
        ExceptionBoundaryExpectations.Verify(id, actual);
        ExceptionBoundaryExpectations.VerifyReads(id, actual);
        (int nodes, int depth) = Measure(actual["exception"]!);
        Assert.IsTrue(nodes <= 16 && depth <= 4);
        Assert.AreEqual(nodes, actual["nodes"]!.GetValue<int>());
        Assert.AreEqual(depth, actual["depth"]!.GetValue<int>());
        Assert.AreEqual(nodes, actual["cachedNodes"]!.GetValue<int>());
        Assert.AreEqual(depth, actual["cachedDepth"]!.GetValue<int>());
        JsonNode observed = actual["observed"]!;
        Assert.IsFalse(observed["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.IsFalse(child.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    private static (int Nodes, int Depth) Measure(JsonNode root)
    {
        var pending = new Queue<(JsonNode Node, int Depth)>();
        pending.Enqueue((root, 1));
        int nodes = 0;
        int depth = 0;
        while (pending.TryDequeue(out var item))
        {
            nodes++;
            depth = Math.Max(depth, item.Depth);
            Assert.IsNotNull(item.Node["type"]!.GetValue<string>());
            Assert.IsTrue(item.Node["message"]!.GetValue<string>().EnumerateRunes().Count() <= 4096);
            if (item.Node["stack_trace"] is JsonNode stack)
            {
                Assert.IsTrue(stack.GetValue<string>().EnumerateRunes().Count() <= 16384);
            }
            if (item.Node["truncated"] is JsonNode truncated)
            {
                Assert.IsTrue(truncated.GetValue<bool>());
            }
            if (item.Node["inner_exceptions"] is JsonArray children)
            {
                foreach (JsonNode? child in children)
                {
                    pending.Enqueue((child!, item.Depth + 1));
                }
            }
        }
        return (nodes, depth);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
