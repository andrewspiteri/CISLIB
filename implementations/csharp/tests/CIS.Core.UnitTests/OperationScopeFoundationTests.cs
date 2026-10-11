using System.Collections.ObjectModel;
using CIS.Logging;
using CIS.Logging.Scopes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class OperationScopeFoundationTests
{
    [TestMethod]
    [DataRow(null, null)]
    [DataRow("", null)]
    [DataRow(" \t\r\n", null)]
    [DataRow("  op-1  ", "op-1")]
    [DataRow("\u2003op-1\u2003", "op-1")]
    [DataRow("op one", "op one")]
    public void OptionalIdentifierIsNormalizedAndEmptyAttributesAreOwned(string? input, string? expected)
    {
        var store = new OperationScopeStore();
        using IDisposable handle = store.BeginScope(new OperationContextData { OperationId = input });
        CapturedOperationContext captured = store.CurrentFrame!.Context;
        Assert.AreEqual(expected, captured.OperationId);
        Assert.AreEqual(LogValueKind.Map, captured.Attributes.Kind);
        Assert.AreEqual(1, captured.Attributes.NodeCount);
        Assert.AreEqual(0, captured.Attributes.MapProperties.Count);
        Assert.IsFalse(captured.IsTruncated);
    }

    [TestMethod]
    [DataRow("ascii")]
    [DataRow("astral")]
    public void IdentifierLimitCountsScalarsRatherThanCodeUnits(string pattern)
    {
        string token = pattern == "ascii" ? "x" : "\U0001F600";
        string accepted = string.Concat(Enumerable.Repeat(token, 255));
        var store = new OperationScopeStore();
        using IDisposable handle = store.BeginScope(new OperationContextData { OperationId = accepted });
        Assert.AreEqual(accepted, store.CurrentFrame!.Context.OperationId);
        OperationScopeFrame prior = store.CurrentFrame;
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            store.BeginScope(new OperationContextData { OperationId = accepted + token }));
        Assert.AreEqual("context", error.ParamName);
        Assert.AreSame(prior, store.CurrentFrame);
        Assert.IsFalse(error.Message.Contains(accepted, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("nul")]
    [DataRow("newline")]
    [DataRow("delete")]
    [DataRow("nel")]
    [DataRow("c1")]
    [DataRow("high-surrogate")]
    [DataRow("low-surrogate")]
    public void MalformedOrControlledIdentifierLeavesThePriorScopeUnchanged(string kind)
    {
        string value = kind switch
        {
            "nul" => "op\0id",
            "newline" => "op\nid",
            "delete" => "op\u007Fid",
            "nel" => "op\u0085id",
            "c1" => "op\u009Fid",
            "high-surrogate" => new string((char)0xD800, 1),
            "low-surrogate" => new string((char)0xDC00, 1),
            _ => throw new InvalidDataException("Unknown native identifier input.")
        };
        var store = new OperationScopeStore();
        using IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        OperationScopeFrame prior = store.CurrentFrame!;
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
        {
            using IDisposable unexpected = store.BeginScope(new OperationContextData { OperationId = value });
        });
        Assert.AreEqual("context", error.ParamName);
        Assert.AreSame(prior, store.CurrentFrame);
    }

    [TestMethod]
    public void CallerDtoMapAndNestedSourceMutationsDoNotChangeTheCapturedScope()
    {
        var sourceItems = new List<LogValue> { LogValue.String("original") };
        LogValue immutableChild = LogValue.Array(sourceItems);
        var mutable = new Dictionary<string, LogValue> { ["value"] = immutableChild };
        var input = new OperationContextData { OperationId = "original", Attributes = new ReadOnlyDictionary<string, LogValue>(mutable) };
        var store = new OperationScopeStore();
        using IDisposable handle = store.BeginScope(input);
        CapturedOperationContext captured = store.CurrentFrame!.Context;
        input.OperationId = "changed";
        input.Attributes = null;
        mutable.Clear();
        mutable["changed"] = LogValue.Boolean(true);
        sourceItems.Clear();
        sourceItems.Add(LogValue.String("changed"));
        Assert.AreEqual("original", captured.OperationId);
        Assert.AreEqual(1, captured.Attributes.MapProperties.Count);
        Assert.AreSame(immutableChild, captured.Attributes.MapProperties["value"]);
        Assert.AreEqual("original", captured.Attributes.MapProperties["value"].ArrayItems.Single().Scalar.Text);
        var owned = (IDictionary<string, LogValue>)captured.Attributes.MapProperties;
        Assert.ThrowsExactly<NotSupportedException>(() => owned.Add("new", LogValue.Null()));
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
    public void EverySupportedValueKindRetainsItsRepresentation(int kind)
    {
        LogValue value = kind switch
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
        var store = new OperationScopeStore();
        using IDisposable scope = store.BeginScope(new OperationContextData { Attributes = new Dictionary<string, LogValue> { ["value"] = value } });
        Assert.AreSame(value, store.CurrentFrame!.Context.Attributes.MapProperties["value"]);
        Assert.AreEqual((LogValueKind)kind, store.CurrentFrame.Context.Attributes.MapProperties["value"].Kind);
    }

    [TestMethod]
    [DataRow(1022, 1023, false)]
    [DataRow(1023, 1024, false)]
    [DataRow(1024, 1024, true)]
    public void ScopeAttributeMapUsesTheExistingRootNodeBudget(int count, int expectedNodes, bool truncated)
    {
        var attributes = Enumerable.Range(0, count).ToDictionary(index => "k" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => LogValue.Null());
        var store = new OperationScopeStore();
        using IDisposable handle = store.BeginScope(new OperationContextData { Attributes = attributes });
        Assert.AreEqual(expectedNodes, store.CurrentFrame!.Context.Attributes.NodeCount);
        Assert.AreEqual(truncated, store.CurrentFrame.Context.IsTruncated);
    }

    [TestMethod]
    public void ScopeRootReprojectsNestedDepthAndPreservesInheritedTruncation()
    {
        LogValue value = LogValue.String("leaf");
        for (int index = 0; index < 7; index++) { value = LogValue.Array([value]); }
        var store = new OperationScopeStore();
        using IDisposable handle = store.BeginScope(new OperationContextData { Attributes = new Dictionary<string, LogValue> { ["nested"] = value } });
        Assert.AreEqual(8, store.CurrentFrame!.Context.Attributes.Depth);
        Assert.IsTrue(store.CurrentFrame.Context.IsTruncated);
        LogValue priorTruncated = LogValue.Array(Enumerable.Repeat(LogValue.Null(), 1024));
        using IDisposable inner = store.BeginScope(new OperationContextData { Attributes = new Dictionary<string, LogValue> { ["nested"] = priorTruncated } });
        Assert.IsTrue(store.CurrentFrame!.Context.IsTruncated);
        Assert.AreEqual(1024, store.CurrentFrame.Context.Attributes.NodeCount);
    }

    [TestMethod]
    public void NullAndInvalidMapsCannotPublishAPartialScope()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        OperationScopeFrame prior = store.CurrentFrame!;
        Assert.ThrowsExactly<ArgumentNullException>(() => store.BeginScope(null!));
        Assert.ThrowsExactly<ArgumentException>(() => store.BeginScope(new OperationContextData { Attributes = new Dictionary<string, LogValue> { ["bad\0key"] = LogValue.Null() } }));
        Assert.ThrowsExactly<ArgumentException>(() => store.BeginScope(new OperationContextData { Attributes = new Dictionary<string, LogValue> { ["value"] = null! } }));
        Assert.AreSame(prior, store.CurrentFrame);
    }

    [TestMethod]
    public async Task ChildWorkInheritsTheImmutableFrameAndOwnPushRestoresItsInheritedHead()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        OperationScopeFrame parent = store.CurrentFrame!;
        await Task.Run(async () =>
        {
            Assert.AreSame(parent, store.CurrentFrame);
            using (store.BeginScope(new OperationContextData { OperationId = "child" }))
            {
                await Task.Yield();
                Assert.AreEqual("child", store.CurrentFrame!.Context.OperationId);
                Assert.AreSame(parent, store.CurrentFrame.Parent);
            }
            Assert.AreSame(parent, store.CurrentFrame);
        }).ConfigureAwait(false);
        Assert.AreSame(parent, store.CurrentFrame);
    }

    [TestMethod]
    public async Task TwoBarrierControlledOperationsAndTwoStoresKeepIndependentHeads()
    {
        var first = new OperationScopeStore();
        var second = new OperationScopeStore();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int participants = 0;
        async Task Run(string id)
        {
            using IDisposable scope = first.BeginScope(new OperationContextData { OperationId = id });
            using IDisposable other = second.BeginScope(new OperationContextData { OperationId = id + "-other" });
            if (Interlocked.Increment(ref participants) == 2) { ready.SetResult(); }
            await release.Task.ConfigureAwait(false);
            Assert.AreEqual(id, first.CurrentFrame!.Context.OperationId);
            Assert.AreEqual(id + "-other", second.CurrentFrame!.Context.OperationId);
        }
        Task a = Task.Run(() => Run("a"));
        Task b = Task.Run(() => Run("b"));
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsNull(first.CurrentFrame);
            Assert.IsNull(second.CurrentFrame);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(a, b);
        Assert.IsNull(first.CurrentFrame);
        Assert.IsNull(second.CurrentFrame);
    }

    [TestMethod]
    public void NormalNestedDisposalRestoresHeadAndRejectsOutOfOrderWithoutMutation()
    {
        var store = new OperationScopeStore();
        IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        IDisposable inner = store.BeginScope(new OperationContextData { OperationId = "inner" });
        OperationScopeFrame prior = store.CurrentFrame!;
        Assert.ThrowsExactly<InvalidOperationException>(outer.Dispose);
        Assert.AreSame(prior, store.CurrentFrame);
        inner.Dispose();
        Assert.AreEqual("outer", store.CurrentFrame!.Context.OperationId);
        inner.Dispose();
        outer.Dispose();
        outer.Dispose();
        Assert.IsNull(store.CurrentFrame);
    }
}
