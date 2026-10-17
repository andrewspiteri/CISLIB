using System.Text.Json.Nodes;
using CIS.Logging.Exceptions;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ExceptionContentTests
{
    public static IEnumerable<object[]> MarkerCases => ExceptionContentSources.MarkerCases.Select(id => new object[] { id });

    [TestMethod]
    [DynamicData(nameof(MarkerCases))]
    public void MarkersDescribeOnlyLocallyClippedContentAndExcludeEveryOtherField(string id)
    {
        JsonObject result = ExceptionContentFixture.Execute(id);
        ExceptionContentExpectations.Verify(id, result);
        Assert.IsFalse(result.ToJsonString().Contains(ExceptionContentSources.PrivateSentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("single")]
    [DataRow("inner")]
    [DataRow("aggregate")]
    public void NativeDataSourceAndHelpLinkMutationDoesNotChangeCapturedContent(string kind)
    {
        JsonObject result = ExceptionContentFixture.Execute("data-" + kind);
        ExceptionContentExpectations.Verify("data-" + kind, result);
        Assert.IsFalse(result.ToJsonString().Contains(ExceptionContentSources.PrivateSentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("single", 4)]
    [DataRow("inner", 7)]
    [DataRow("aggregate", 10)]
    public void CallerExceptionsCyclicDataPayloadsAndHeadersAreCollectibleWhileCaptureLives(string kind, int references)
    {
        ExceptionRetention state = ExceptionRetentionFixture.Create(kind);
        Assert.AreEqual(references, state.CallerReferences.Length);
        Assert.IsTrue(ExceptionRetentionFixture.Collect(state));
        string id = "data-" + kind;
        JsonObject result = ExceptionBoundaryFixture.Observe(id, state.Captured);
        Assert.IsTrue(JsonNode.DeepEquals(ExceptionContentExpectations.Tree(id), result["exception"]));
        GC.KeepAlive(state.Captured);
    }

    [TestMethod]
    [DataRow("data")]
    [DataRow("source")]
    [DataRow("help")]
    [DataRow("format")]
    [DataRow("body")]
    [DataRow("headers")]
    [DataRow("environment")]
    public void EveryExcludedGetterTrapIsArmedAndCaptureNeverCallsIt(string member)
    {
        ExceptionContentSource source = ExceptionContentSources.Create("complete");
        Assert.ThrowsExactly<InvalidOperationException>(() => ExceptionContentSources.TouchForbidden(source.Root, member));
        Assert.AreEqual(1, source.Probes[0].ForbiddenCalls);
        source.Probes[0].ResetForbiddenCalls();
        _ = ExceptionProjection.Capture(source.Root);
        Assert.AreEqual(0, source.Probes[0].ForbiddenCalls);
        Assert.AreEqual(1, source.Probes[0].MessageReads);
        Assert.AreEqual(1, source.Probes[0].StackReads);
    }

    [TestMethod]
    [DataRow("root-add")]
    [DataRow("root-remove")]
    [DataRow("child-remove")]
    [DataRow("data-extra")]
    [DataRow("unknown-extra")]
    [DataRow("false-marker")]
    [DataRow("reads-missing")]
    [DataRow("reads-forbidden")]
    public void ExactContentComparatorRejectsMisplacedMissingAndSpuriousMarkersOrFields(string mutation)
    {
        string id = mutation == "root-remove" ? "root-child" : "child-message";
        JsonObject result = ExceptionContentFixture.Execute(id);
        JsonObject root = result["exception"]!.AsObject();
        switch (mutation)
        {
            case "root-add":
                root["truncated"] = true;
                break;
            case "root-remove":
                root.Remove("truncated");
                break;
            case "child-remove":
                root["inner_exceptions"]![0]!.AsObject().Remove("truncated");
                break;
            case "data-extra":
                root["data"] = "private";
                break;
            case "unknown-extra":
                root["headers"] = "private";
                break;
            case "reads-missing":
                result.Remove("reads");
                break;
            case "reads-forbidden":
                result["reads"]![0]!["forbidden"] = 1;
                break;
            case "false-marker":
                root["truncated"] = false;
                break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => ExceptionContentExpectations.Verify(id, result));
    }
    [TestMethod]
    [DataRow("omitted-missing")]
    [DataRow("omitted-read")]
    [DataRow("omitted-label")]
    public void OmittedSourcesKeepTheirIndependentProbeIdentityAndZeroReads(string fault)
    {
        JsonObject result = ExceptionContentFixture.Execute("nodes");
        ExceptionContentExpectations.Verify("nodes", result);
        JsonArray reads = result["reads"]!.AsArray();
        CollectionAssert.AreEqual(new[] { "root", "n0", "n1", "n2", "n3", "n4", "n5", "n6", "n7", "n8", "n9", "n10", "n11", "n12", "n13", "n14", "n15", "n16" }, reads.Select(read => (string)read!["label"]!).ToArray());
        Assert.AreEqual(0, reads[^1]!["message"]!.GetValue<int>());
        Assert.AreEqual(0, reads[^1]!["stack"]!.GetValue<int>());
        switch (fault)
        {
            case "omitted-missing": reads.RemoveAt(reads.Count - 1); break;
            case "omitted-read": reads[^1]!["message"] = 1; break;
            case "omitted-label": reads[^1]!["label"] = "n17"; break;
        }
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => ExceptionContentExpectations.Verify("nodes", result));
        Assert.AreEqual(fault == "omitted-missing" ? "Missing exception read observations." : "Exception field access differs.", error.Message);
    }
}
