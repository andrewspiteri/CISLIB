using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging.Exceptions;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ExceptionBoundaryTests
{
    public static IEnumerable<object[]> CultureCases =>
        ExceptionBoundarySources.Cases.SelectMany(id =>
            new[] { "en-US", "fr-FR", "ar-SA" }.Select(culture => new object[] { id, culture }));

    [TestMethod]
    [DynamicData(nameof(CultureCases))]
    public void ExactAdjacentAndMixedBoundariesPreserveTheRequiredFieldsAcrossCultures(string id, string culture)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo beforeUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            ExceptionBoundarySource source = ExceptionBoundarySources.Create(id);
            CapturedException root = ExceptionProjection.Capture(source.Root)!;
            ExceptionBoundaryFixture.Verify(id, ExceptionBoundaryFixture.Observe(id, root));
            Assert.AreEqual(source.Root.GetType().FullName, root.Type);
            foreach (CapturedException node in ExceptionBoundaryFixture.Preorder(root))
            {
                Assert.IsTrue(node.Message.EnumerateRunes().Count() <= 4096);
                Assert.IsTrue(node.StackTrace is null || node.StackTrace.EnumerateRunes().Count() <= 16384);
            }
            string[] parts = id.Split('-');
            if (parts[0] is "message" or "stack")
            {
                int count = int.Parse(parts[2], CultureInfo.InvariantCulture);
                string expected = ExceptionBoundarySources.Text(parts[1], Math.Min(count, ExceptionBoundarySources.Limit(parts[0])));
                Assert.AreEqual(expected, parts[0] == "message" ? root.Message : root.StackTrace);
                Assert.AreEqual(root.IsTruncated, root.HasTruncatedContent);
            }
            else if (!root.HasTruncatedContent)
            {
                Assert.IsTrue(ExceptionBoundaryFixture.Preorder(root).All(node => !node.IsTruncated));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
            CultureInfo.CurrentUICulture = beforeUi;
        }
    }

    [TestMethod]
    [DataRow("nodes-16")]
    [DataRow("nodes-1000")]
    [DataRow("depth-5")]
    [DataRow("depth-64")]
    [DataRow("mixed-over")]
    [DataRow("depth-sibling")]
    public void TraversalNeverReadsAccessorsOfOmittedNodes(string id)
    {
        ExceptionBoundarySource source = ExceptionBoundarySources.Create(id);
        string[] retained = ExceptionBoundaryFixture.Expected(id)["preorder"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        foreach (ExceptionReadProbe probe in source.Probes)
        {
            probe.FailIfRead = !retained.Contains(probe.Label, StringComparer.Ordinal);
        }
        CapturedException root = ExceptionProjection.Capture(source.Root)!;
        ExceptionBoundaryFixture.Verify(id, ExceptionBoundaryFixture.Observe(id, root));
        foreach (ExceptionReadProbe probe in source.Probes)
        {
            Assert.AreEqual(probe.FailIfRead ? 0 : 1, probe.MessageReads, probe.Label);
            Assert.AreEqual(probe.FailIfRead ? 0 : 1, probe.StackReads, probe.Label);
        }
    }

    [TestMethod]
    [DataRow("mixed-exact")]
    [DataRow("mixed-over")]
    [DataRow("depth-sibling")]
    [DataRow("shared")]
    public void EveryOwnedNodeHasIndependentlyMeasuredShapeAndRepeatedOccurrencesRemainDistinct(string id)
    {
        CapturedException root = ExceptionProjection.Capture(ExceptionBoundarySources.Create(id).Root)!;
        foreach (CapturedException node in ExceptionBoundaryFixture.Preorder(root))
        {
            (int nodes, int depth) = ExceptionBoundaryFixture.Measure(node);
            Assert.AreEqual(nodes, node.NodeCount);
            Assert.AreEqual(depth, node.Depth);
            Assert.AreEqual(ExceptionBoundaryFixture.Preorder(node).Any(child => child.IsTruncated), node.HasTruncatedContent);
        }
        if (id == "shared")
        {
            Assert.AreEqual(15, root.InnerExceptions.Distinct(ReferenceEqualityComparer.Instance).Count());
        }
    }

    [TestMethod]
    [DataRow("nodes-1000")]
    [DataRow("depth-64")]
    [DataRow("mixed-over")]
    public async Task ConcurrentCapturesKeepIndependentTraversalBudgets(string id)
    {
        JsonObject[] results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => ExceptionBoundaryFixture.Execute(id))));
        foreach (JsonObject result in results)
        {
            ExceptionBoundaryFixture.Verify(id, result);
        }
    }

    [TestMethod]
    public void NullOverrideMessageAndExplicitEmptyStackKeepAValidRequiredShape()
    {
        ExceptionBoundarySource source = ExceptionBoundarySources.Create("depth-1");
        source.Probes[0].Message = null;
        source.Probes[0].Stack = string.Empty;
        CapturedException root = ExceptionProjection.Capture(source.Root)!;
        Assert.AreEqual(string.Empty, root.Message);
        Assert.AreEqual(string.Empty, root.StackTrace);
        Assert.IsFalse(root.HasTruncatedContent);
    }

    [TestMethod]
    [DataRow("message", "invalid-retained")]
    [DataRow("stack", "invalid-retained")]
    [DataRow("message", "invalid-omitted")]
    [DataRow("stack", "invalid-omitted")]
    [DataRow("message", "astral-exact")]
    [DataRow("stack", "astral-exact")]
    [DataRow("message", "astral-over")]
    [DataRow("stack", "astral-over")]
    public void ScalarBoundariesNeverSplitPairsAndOnlyRetainedUnitsAreValidated(string textField, string shape)
    {
        int limit = ExceptionBoundarySources.Limit(textField);
        bool over = shape is "invalid-omitted" or "astral-over";
        string suffix = shape.StartsWith("invalid", StringComparison.Ordinal) ? "\ud800" : "😀";
        string text = new string('x', over ? limit : limit - 1) + suffix;
        ExceptionBoundarySource source = ExceptionBoundarySources.Create("depth-1");
        if (textField == "message")
        {
            source.Probes[0].Message = text;
        }
        else
        {
            source.Probes[0].Stack = text;
        }
        if (shape == "invalid-retained")
        {
            ArgumentException failure = Assert.ThrowsExactly<ArgumentException>(() => ExceptionProjection.Capture(source.Root));
            Assert.AreEqual("value", failure.ParamName);
            return;
        }
        CapturedException root = ExceptionProjection.Capture(source.Root)!;
        string actual = (textField == "message" ? root.Message : root.StackTrace)!;
        Assert.AreEqual(limit, actual.EnumerateRunes().Count());
        Assert.AreEqual(over, root.IsTruncated);
        Assert.AreEqual(over ? new string('x', limit) : text, actual);
    }

    [TestMethod]
    public void MessageAndStackAllowancesApplySeparatelyToEveryRetainedNode()
    {
        ExceptionBoundarySource source = ExceptionBoundarySources.Create("depth-3");
        foreach (ExceptionReadProbe probe in source.Probes)
        {
            probe.Message = ExceptionBoundarySources.Text("astral", 4097);
            probe.Stack = ExceptionBoundarySources.Text("combining", 16385);
        }
        CapturedException root = ExceptionProjection.Capture(source.Root)!;
        Assert.AreEqual(3, root.NodeCount);
        foreach (CapturedException node in ExceptionBoundaryFixture.Preorder(root))
        {
            Assert.AreEqual(4096, node.Message.EnumerateRunes().Count());
            Assert.AreEqual(16384, node.StackTrace!.EnumerateRunes().Count());
            Assert.IsTrue(node.IsTruncated);
        }
    }

    [TestMethod]
    [DataRow("nodes")]
    [DataRow("depth")]
    [DataRow("cachedNodes")]
    [DataRow("cachedDepth")]
    [DataRow("hasTruncatedContent")]
    [DataRow("preorder")]
    [DataRow("exception")]
    public void BoundaryComparatorRejectsChangedRootCountsOrderAndLoss(string member)
    {
        JsonObject actual = ExceptionBoundaryFixture.Execute("mixed-over");
        if (member == "exception")
        {
            // Whole-tree loss remains true through b; removing only root loss must still fail.
            actual["exception"]!.AsObject().Remove("truncated");
            Assert.ThrowsExactly<InvalidDataException>(() => ExceptionBoundaryFixture.Verify("mixed-over", actual));
            return;
        }
        actual[member] = member switch
        {
            "hasTruncatedContent" => JsonValue.Create(false),
            "preorder" => new JsonArray(JsonValue.Create("changed")),
            _ => JsonValue.Create(-1)
        };
        Assert.ThrowsExactly<InvalidDataException>(() => ExceptionBoundaryFixture.Verify("mixed-over", actual));
    }
}
