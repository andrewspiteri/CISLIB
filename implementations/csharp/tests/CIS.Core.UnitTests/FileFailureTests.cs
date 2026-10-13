using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileFailureTests
{
    [TestMethod]
    [DataRow("delete-jsonl-directory")]
    [DataRow("delete-jsonl-damaged-directory")]
    [DataRow("delete-human-directory")]
    [DataRow("render-empty")]
    [DataRow("render-active")]
    public async Task FilePolicyFailureStopsGrowthAndPreservesHealthyConsole(string mode)
    {
        using var root = new TemporaryDirectory(); JsonObject actual = await FileFailureFixture.ExecuteAsync(mode, root.Path); FileFailureExpectations.Validate(mode, actual);
        if (mode.Contains("-damaged-", StringComparison.Ordinal))
        {
            string metadata = actual["before"]!.AsObject().Single(entry => entry.Key.EndsWith(".cis.inventory.json", StringComparison.Ordinal)).Value!.GetValue<string>();
            JsonObject inventory = JsonNode.Parse(Convert.FromBase64String(metadata))!.AsObject(); JsonNode oldest = inventory["segments"]![0]!;
            Assert.AreEqual(1L, oldest["id"]!.GetValue<long>()); Assert.AreEqual("damaged", oldest["state"]!.GetValue<string>());
            Assert.AreEqual(".cis-log-" + inventory["groupId"]!.GetValue<string>() + "-00000000000000000001.damaged", oldest["fileName"]!.GetValue<string>());
        }
    }

    [TestMethod]
    [DataRow("code")]
    [DataRow("growth")]
    [DataRow("console")]
    [DataRow("abandoned")]
    [DataRow("release")]
    [DataRow("private")]
    public async Task FailureMatcherRejectsLostPolicyFacts(string corruption)
    {
        using var root = new TemporaryDirectory(); const string mode = "delete-jsonl-directory"; JsonObject actual = await FileFailureFixture.ExecuteAsync(mode, root.Path); FileFailureExpectations.Validate(mode, actual);
        switch (corruption)
        {
            case "code": actual["fault"]!["outputs"]![1]!["faultCode"] = "WriteFailed"; break;
            case "growth": actual["namesAfter"]!.AsArray().Add("new.closed"); break;
            case "console": actual["final"]!["outputs"]![0]!["written"] = 0L; break;
            case "abandoned": actual["final"]!["outputs"]![1]!["abandoned"] = 0L; break;
            case "release": actual["leaseReleased"] = false; break;
            default: actual["error"]!["message"] = "private filesystem content"; break;
        }
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => FileFailureExpectations.Validate(mode, actual));
        Assert.AreEqual(corruption switch { "code" => "terminal fault code", "growth" => "no further growth/preimage", "console" => "console progress", "abandoned" => "file loss accounting", "release" => "owned handle release", _ => "safe fixed error" }, error.Message);
    }

    [TestMethod]
    public async Task ArbitraryWriterExceptionDataCannotForgeAFilePolicyCode()
    {
        var error = new IOException("private path"); error.Data["FaultCode"] = "RenderTooLarge";
        var queue = new OutputEventQueue(1, _ => throw error);
        try { Assert.IsTrue(queue.TryEnqueue(new EncodedEvent([], false))); IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => queue.Completion.WaitAsync(TimeSpan.FromSeconds(10))); Assert.AreSame(error, observed); Assert.AreEqual("WriteFailed", queue.CaptureAccounting().LastFaultCode); }
        finally { queue.Complete(); try { await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)); } catch (IOException observed) when (ReferenceEquals(observed, error)) { } }
    }
}
