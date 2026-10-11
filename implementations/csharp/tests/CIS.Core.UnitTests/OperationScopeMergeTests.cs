using System.Collections;
using System.Globalization;
using CIS.Logging;
using CIS.Logging.Scopes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class OperationScopeMergeTests
{
    [TestMethod]
    public void EmptyCaptureIsAnImmutableEmptyViewAndDoesNotChangeTheStore()
    {
        var store = new OperationScopeStore();
        CapturedScopeContext view = store.Capture();
        Assert.IsNull(view.OperationId);
        Assert.AreEqual(0, view.ScopeDepth);
        Assert.AreEqual(1, view.Attributes.NodeCount);
        Assert.AreEqual(1, view.Attributes.Depth);
        Assert.AreEqual(0, view.Attributes.MapProperties.Count);
        Assert.IsFalse(view.IsTruncated);
        Assert.IsNull(store.CurrentFrame);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, LogValue>)view.Attributes.MapProperties).Add("changed", LogValue.Null()));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    [DataRow(10)]
    public void InnerValuesReplaceOuterValuesAsWholeSupportedValues(int kind)
    {
        var store = new OperationScopeStore();
        using IDisposable outer = Push(store, "outer", new Dictionary<string, LogValue>
        {
            ["same"] = LogValue.String("outer"),
            ["outer-only"] = LogValue.Boolean(true)
        });
        LogValue value = Value(kind);
        using IDisposable inner = Push(store, "inner", new Dictionary<string, LogValue>
        {
            ["same"] = value,
            ["inner-only"] = LogValue.Integer(1)
        });
        OperationScopeFrame prior = store.CurrentFrame!;
        CapturedScopeContext view = store.Capture();
        Assert.AreSame(value, view.Attributes.MapProperties["same"]);
        Assert.AreEqual((LogValueKind)kind, view.Attributes.MapProperties["same"].Kind);
        Assert.IsTrue(view.Attributes.MapProperties["outer-only"].Scalar.Boolean);
        Assert.AreEqual(1L, view.Attributes.MapProperties["inner-only"].Scalar.Integer);
        Assert.AreEqual(3, view.Attributes.MapProperties.Count);
        Assert.AreEqual("inner", view.OperationId);
        Assert.AreEqual(2, view.ScopeDepth);
        Assert.IsFalse(view.IsTruncated);
        Assert.AreSame(prior, store.CurrentFrame);
    }

    [TestMethod]
    public void OrdinalCaseSensitiveAndReservedLookingKeysRemainOrdinaryAttributes()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = Push(store, "actual", new Dictionary<string, LogValue>
        {
            ["Name"] = LogValue.String("upper"),
            ["operation_id"] = LogValue.String("attribute"),
            ["service"] = LogValue.String("attribute-service"),
            ["trace_id"] = LogValue.String("attribute-trace")
        });
        using IDisposable inner = Push(store, null, new Dictionary<string, LogValue> { ["name"] = LogValue.Null() });
        CapturedScopeContext view = store.Capture();
        Assert.AreEqual(5, view.Attributes.MapProperties.Count);
        Assert.AreEqual("upper", view.Attributes.MapProperties["Name"].Scalar.Text);
        Assert.AreEqual(LogValueKind.Null, view.Attributes.MapProperties["name"].Kind);
        Assert.AreEqual("actual", view.OperationId);
        Assert.AreEqual("attribute", view.Attributes.MapProperties["operation_id"].Scalar.Text);
    }

    [TestMethod]
    [DataRow("outer", "middle", "inner", "inner")]
    [DataRow("outer", null, null, "outer")]
    [DataRow("outer", "middle", "   ", "middle")]
    [DataRow(null, " middle ", "", "middle")]
    [DataRow(null, null, null, null)]
    public void OperationIdUsesTheInnermostNonblankCapturedValue(string? outerId, string? middleId, string? innerId, string? expected)
    {
        var store = new OperationScopeStore();
        using IDisposable outer = Push(store, outerId);
        using IDisposable middle = Push(store, middleId);
        using IDisposable inner = Push(store, innerId);
        Assert.AreEqual(expected, store.Capture().OperationId);
        Assert.AreEqual(3, store.Capture().ScopeDepth);
    }

    [TestMethod]
    public void CapturedViewSurvivesDisposalAndLaterReplacementWithoutRetainingMutableInput()
    {
        var store = new OperationScopeStore();
        var input = new Dictionary<string, LogValue> { ["same"] = LogValue.String("original") };
        IDisposable outer = Push(store, "outer", new Dictionary<string, LogValue> { ["outer"] = LogValue.Boolean(true) });
        IDisposable inner = Push(store, "inner", input);
        CapturedScopeContext view = store.Capture();
        input.Clear();
        inner.Dispose();
        using (Push(store, "replacement", new Dictionary<string, LogValue> { ["same"] = LogValue.String("replacement") }))
        {
            Assert.AreEqual("replacement", store.Capture().Attributes.MapProperties["same"].Scalar.Text);
        }
        outer.Dispose();
        Assert.IsNull(store.CurrentFrame);
        Assert.AreEqual("inner", view.OperationId);
        Assert.AreEqual(2, view.ScopeDepth);
        Assert.AreEqual("original", view.Attributes.MapProperties["same"].Scalar.Text);
        Assert.IsTrue(view.Attributes.MapProperties["outer"].Scalar.Boolean);
    }

    [TestMethod]
    [DataRow(0, 1, false)]
    [DataRow(1, 2, false)]
    [DataRow(1022, 1023, false)]
    [DataRow(1023, 1024, false)]
    [DataRow(1024, 1024, true)]
    [DataRow(2046, 1024, true)]
    public void TwoIndividuallyCompleteScopesHaveAnIndependentMergedRootBudget(int count, int expectedNodes, bool truncated)
    {
        var store = new OperationScopeStore();
        int first = count / 2;
        using IDisposable outer = Push(store, null, Scalars(0, first));
        using IDisposable inner = Push(store, null, Scalars(first, count - first));
        Assert.IsFalse(store.CurrentFrame!.Context.IsTruncated);
        Assert.IsFalse(store.CurrentFrame.Parent!.Context.IsTruncated);
        CapturedScopeContext view = store.Capture();
        Assert.AreEqual(expectedNodes, view.Attributes.NodeCount);
        Assert.AreEqual(Math.Min(count, 1023), view.Attributes.MapProperties.Count);
        Assert.AreEqual(truncated, view.IsTruncated);
        Assert.AreEqual(count == 0 ? 1 : 2, view.Attributes.Depth);
        for (int index = 0; index < Math.Min(count, 1023); index++)
        {
            Assert.IsTrue(view.Attributes.MapProperties.ContainsKey("k" + index.ToString(CultureInfo.InvariantCulture)));
        }
    }

    [TestMethod]
    public void OverridesAreResolvedBeforeTheMergedNodeProjection()
    {
        var store = new OperationScopeStore();
        LogValue large = LogValue.Array(Enumerable.Repeat(LogValue.Null(), 1022));
        using IDisposable outer = Push(store, null, new Dictionary<string, LogValue> { ["same"] = large });
        Assert.IsFalse(store.CurrentFrame!.Context.IsTruncated);
        using IDisposable inner = Push(store, null, new Dictionary<string, LogValue>
        {
            ["same"] = LogValue.Null(),
            ["later"] = LogValue.Boolean(true)
        });
        CapturedScopeContext view = store.Capture();
        Assert.AreEqual(3, view.Attributes.NodeCount);
        Assert.IsFalse(view.IsTruncated);
        Assert.AreEqual(LogValueKind.Null, view.Attributes.MapProperties["same"].Kind);
        Assert.IsTrue(view.Attributes.MapProperties.ContainsKey("later"));
    }

    [TestMethod]
    [DataRow(510, 1023, false)]
    [DataRow(511, 1024, true)]
    public void SharedImmutableSubtreesCountForEveryMergedContainment(int elements, int nodes, bool truncated)
    {
        var store = new OperationScopeStore();
        LogValue shared = LogValue.Array(Enumerable.Repeat(LogValue.Null(), elements));
        using IDisposable outer = Push(store, null, new Dictionary<string, LogValue> { ["a"] = shared });
        using IDisposable inner = Push(store, null, new Dictionary<string, LogValue> { ["b"] = shared });
        CapturedScopeContext view = store.Capture();
        Assert.AreEqual(nodes, view.Attributes.NodeCount);
        Assert.AreEqual(truncated, view.IsTruncated);
        Assert.AreSame(shared, view.Attributes.MapProperties["a"]);
        Assert.AreEqual(truncated ? elements - 1 : elements, view.Attributes.MapProperties["b"].ArrayItems.Count);
        Assert.AreEqual(truncated, view.Attributes.MapProperties["b"].IsTruncated);
    }

    [TestMethod]
    public void ReplacingAllRetainedTruncatedContentDoesNotEraseScopeCaptureLoss()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = Push(store, "outer", new Dictionary<string, LogValue>
        {
            ["same"] = LogValue.Array(Enumerable.Repeat(LogValue.Null(), 1024))
        });
        Assert.IsTrue(store.CurrentFrame!.Context.IsTruncated);
        using IDisposable inner = Push(store, null, new Dictionary<string, LogValue> { ["same"] = LogValue.Boolean(false) });
        CapturedScopeContext view = store.Capture();
        Assert.AreEqual(2, view.Attributes.NodeCount);
        Assert.AreEqual("outer", view.OperationId);
        Assert.IsFalse(view.Attributes.MapProperties["same"].IsTruncated);
        Assert.IsTrue(view.IsTruncated);
    }

    [TestMethod]
    public void ALastPositionInnerDuplicateWinsEvenAfterAllNewCandidateKeysAreOmitted()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = Push(store, "outer", Scalars(0, 1023));
        var attributes = Enumerable.Range(0, 1022).ToDictionary(index => "new" + index.ToString(CultureInfo.InvariantCulture), _ => LogValue.Null());
        attributes.Add("k0", LogValue.String("last-inner"));
        using IDisposable inner = Push(store, "inner", attributes);
        Assert.IsFalse(store.CurrentFrame!.Context.IsTruncated);
        CapturedScopeContext view = store.Capture();
        Assert.AreEqual("last-inner", view.Attributes.MapProperties["k0"].Scalar.Text);
        Assert.AreEqual(1023, view.Attributes.MapProperties.Count);
        Assert.AreEqual(1024, view.Attributes.NodeCount);
        Assert.IsFalse(view.Attributes.MapProperties.ContainsKey("new0"));
        Assert.IsTrue(view.IsTruncated);
        Assert.AreEqual("inner", view.OperationId);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(31)]
    [DataRow(32)]
    public void DepthBoundaryCaptureAndThirtyThirdRejectionLeaveTheChainUnchanged(int depth)
    {
        var store = new OperationScopeStore();
        var handles = new Stack<IDisposable>();
        int traversals = 0;
        try
        {
            for (int index = 0; index < depth; index++) { handles.Push(Push(store, "d" + index.ToString(CultureInfo.InvariantCulture))); }
            OperationScopeFrame? prior = store.CurrentFrame;
            CapturedScopeContext view = store.Capture();
            Assert.AreEqual(depth, view.ScopeDepth);
            Assert.AreEqual(depth == 0 ? null : "d" + (depth - 1).ToString(CultureInfo.InvariantCulture), view.OperationId);
            Assert.AreSame(prior, store.CurrentFrame);
            if (depth == 32)
            {
                Assert.ThrowsExactly<InvalidOperationException>(() => store.BeginScope(new OperationContextData
                {
                    OperationId = new string((char)0xd800, 1),
                    Attributes = new CountingMap(() => traversals++)
                }));
                Assert.AreEqual(0, traversals);
                Assert.AreSame(prior, store.CurrentFrame);
                Assert.AreEqual(32, store.Capture().ScopeDepth);
            }
        }
        finally { while (handles.Count > 0) { handles.Pop().Dispose(); } }
        Assert.IsNull(store.CurrentFrame);
    }

    [TestMethod]
    public async Task CapturesFollowChildFlowAndIndependentStoresWithoutChangingInheritedViews()
    {
        var first = new OperationScopeStore();
        var second = new OperationScopeStore();
        using IDisposable parent = Push(first, "parent", new Dictionary<string, LogValue> { ["same"] = LogValue.String("parent") });
        CapturedScopeContext prior = first.Capture();
        await Task.Run(async () =>
        {
            using IDisposable child = Push(first, "child", new Dictionary<string, LogValue> { ["same"] = LogValue.String("child") });
            using IDisposable other = Push(second, "other", new Dictionary<string, LogValue> { ["same"] = LogValue.String("other") });
            await Task.Yield();
            Assert.AreEqual("child", first.Capture().Attributes.MapProperties["same"].Scalar.Text);
            Assert.AreEqual("other", second.Capture().Attributes.MapProperties["same"].Scalar.Text);
            Assert.AreEqual(2, first.Capture().ScopeDepth);
            Assert.AreEqual(1, second.Capture().ScopeDepth);
        });
        Assert.AreEqual("parent", first.Capture().OperationId);
        Assert.AreEqual("parent", prior.Attributes.MapProperties["same"].Scalar.Text);
        Assert.AreEqual(0, second.Capture().ScopeDepth);
    }

    [TestMethod]
    [DataRow(6, false)]
    [DataRow(7, true)]
    public void MergedProjectionPreservesDepthEightAndExistingDepthLoss(int wrappers, bool truncated)
    {
        LogValue nested = LogValue.String("leaf");
        for (int index = 0; index < wrappers; index++) { nested = LogValue.Array([nested]); }
        var store = new OperationScopeStore();
        using IDisposable outer = Push(store, null, new Dictionary<string, LogValue> { ["nested"] = nested });
        Assert.AreEqual(truncated, store.CurrentFrame!.Context.IsTruncated);
        using IDisposable inner = Push(store, null, new Dictionary<string, LogValue> { ["plain"] = LogValue.Boolean(true) });
        CapturedScopeContext view = store.Capture();
        Assert.AreEqual(8, view.Attributes.Depth);
        Assert.AreEqual(9, view.Attributes.NodeCount);
        Assert.AreEqual(truncated, view.IsTruncated);
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("tr-TR")]
    [DataRow("fr-FR")]
    public void KeyPrecedenceIsOrdinalAcrossCultures(string cultureName)
    {
        CultureInfo prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            var store = new OperationScopeStore();
            using IDisposable outer = Push(store, " Id ", new Dictionary<string, LogValue>
            {
                ["I"] = LogValue.Integer(1),
                ["i"] = LogValue.Integer(2)
            });
            using IDisposable inner = Push(store, null, new Dictionary<string, LogValue>
            {
                ["\u0130"] = LogValue.Integer(3),
                ["\u0131"] = LogValue.Integer(4),
                ["I"] = LogValue.Integer(5)
            });
            CapturedScopeContext view = store.Capture();
            Assert.AreEqual("Id", view.OperationId);
            Assert.AreEqual(4, view.Attributes.MapProperties.Count);
            Assert.AreEqual(5L, view.Attributes.MapProperties["I"].Scalar.Integer);
            Assert.AreEqual(2L, view.Attributes.MapProperties["i"].Scalar.Integer);
            Assert.AreEqual(3L, view.Attributes.MapProperties["\u0130"].Scalar.Integer);
            Assert.AreEqual(4L, view.Attributes.MapProperties["\u0131"].Scalar.Integer);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [TestMethod]
    public void RepeatedKeysBeyondTheCandidateLimitRemainOmittedWhileRetainedDuplicatesStillWin()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = Push(store, "outer", Scalars(0, 1023));
        using IDisposable middle = Push(store, "middle", new Dictionary<string, LogValue> { ["outside"] = LogValue.String("middle") });
        using IDisposable inner = Push(store, "inner", new Dictionary<string, LogValue>
        {
            ["outside"] = LogValue.String("inner"),
            ["k0"] = LogValue.String("kept-final")
        });
        CapturedScopeContext view = store.Capture();
        Assert.AreEqual(1023, view.Attributes.MapProperties.Count);
        Assert.IsFalse(view.Attributes.MapProperties.ContainsKey("outside"));
        Assert.AreEqual("kept-final", view.Attributes.MapProperties["k0"].Scalar.Text);
        Assert.AreEqual("inner", view.OperationId);
        Assert.IsTrue(view.IsTruncated);
    }

    private static IDisposable Push(OperationScopeStore store, string? id, IReadOnlyDictionary<string, LogValue>? attributes = null) =>
        store.BeginScope(new OperationContextData { OperationId = id, Attributes = attributes });

    private static Dictionary<string, LogValue> Scalars(int start, int count) => Enumerable.Range(start, count)
        .ToDictionary(index => "k" + index.ToString(CultureInfo.InvariantCulture), _ => LogValue.Null());

    private static LogValue Value(int kind) => kind switch
    {
        0 => LogValue.Null(),
        1 => LogValue.Boolean(true),
        2 => LogValue.String("text"),
        3 => LogValue.Number(1.5),
        4 => LogValue.Integer(42),
        5 => LogValue.IntegerString("9007199254740992"),
        6 => LogValue.DecimalString("1.2300"),
        7 => LogValue.Uuid(Guid.Empty),
        8 => LogValue.Timestamp(DateTimeOffset.UnixEpoch),
        9 => LogValue.Array([LogValue.Null()]),
        10 => LogValue.Map(new Dictionary<string, LogValue> { ["nested"] = LogValue.Null() }),
        _ => throw new InvalidDataException("Unknown kind.")
    };

    private sealed class CountingMap(Action callback) : IReadOnlyDictionary<string, LogValue>
    {
        public LogValue this[string key] => throw new InvalidOperationException("Do not query caller content.");
        public IEnumerable<string> Keys => throw new InvalidOperationException("Do not query caller content.");
        public IEnumerable<LogValue> Values => throw new InvalidOperationException("Do not query caller content.");
        public int Count => throw new InvalidOperationException("Do not query caller content.");
        public bool ContainsKey(string key) => throw new InvalidOperationException("Do not query caller content.");
        public bool TryGetValue(string key, out LogValue value) => throw new InvalidOperationException("Do not query caller content.");
        public IEnumerator<KeyValuePair<string, LogValue>> GetEnumerator()
        {
            callback();
            yield return new("unused", LogValue.Null());
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
