using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Full collection observes actual caller ownership under an explicitly selected budget.
public sealed partial class ExceptionContentTests
{
    [TestMethod]
    [TestCategory("ExtendedStress")]
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

}
