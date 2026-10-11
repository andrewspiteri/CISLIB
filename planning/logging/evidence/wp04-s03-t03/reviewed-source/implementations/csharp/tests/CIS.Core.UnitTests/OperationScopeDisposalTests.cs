using CIS.Logging;
using CIS.Logging.Scopes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class OperationScopeDisposalTests
{
    [TestMethod]
    [DataRow(2)]
    [DataRow(8)]
    [DataRow(32)]
    public void EveryActiveAncestorRejectsDisposalWithoutChangingTheCapturedScope(int depth)
    {
        var store = new OperationScopeStore();
        var handles = new List<IDisposable>();
        var frames = new List<OperationScopeFrame>();
        try
        {
            for (int index = 0; index < depth; index++)
            {
                handles.Add(store.BeginScope(new OperationContextData
                {
                    OperationId = "scope-" + index,
                    Attributes = new Dictionary<string, LogValue> { ["key-" + index] = LogValue.Integer(index) }
                }));
                frames.Add(store.CurrentFrame!);
            }
            OperationScopeFrame head = store.CurrentFrame!;
            CapturedScopeContext snapshot = store.Capture();
            for (int index = 0; index < depth - 1; index++)
            {
                Assert.ThrowsExactly<InvalidOperationException>(handles[index].Dispose);
                Assert.AreSame(head, store.CurrentFrame);
                Assert.AreEqual(snapshot.OperationId, store.Capture().OperationId);
                Assert.AreEqual(depth, store.Capture().ScopeDepth);
                Assert.AreEqual(depth, store.Capture().Attributes.MapProperties.Count);
                Assert.AreEqual((long)index, store.Capture().Attributes.MapProperties["key-" + index].Scalar.Integer);
            }
        }
        finally
        {
            for (int index = handles.Count - 1; index >= 0; index--)
            {
                handles[index].Dispose();
                if (index == 0) { Assert.IsNull(store.CurrentFrame); }
                else { Assert.AreSame(frames[index - 1], store.CurrentFrame); }
            }
        }
        Assert.IsNull(store.CurrentFrame);
    }

    [TestMethod]
    public void RepeatedAncestorDisposalErrorsPreserveTheHeadAndAllowLaterLifoCleanup()
    {
        var store = new OperationScopeStore();
        IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        OperationScopeFrame outerFrame = store.CurrentFrame!;
        IDisposable inner = store.BeginScope(new OperationContextData { OperationId = "inner" });
        OperationScopeFrame innerFrame = store.CurrentFrame!;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.ThrowsExactly<InvalidOperationException>(outer.Dispose);
            Assert.AreSame(innerFrame, store.CurrentFrame);
        }
        inner.Dispose();
        Assert.AreSame(outerFrame, store.CurrentFrame);
        outer.Dispose();
        Assert.IsNull(store.CurrentFrame);
        outer.Dispose();
        Assert.IsNull(store.CurrentFrame);
    }

    [TestMethod]
    public void CompletedInnerHandleCanRepeatWhileANewerIdenticalScopeRemainsActive()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        var input = new OperationContextData { OperationId = "same" };
        IDisposable completed = store.BeginScope(input);
        completed.Dispose();
        using IDisposable replacement = store.BeginScope(input);
        OperationScopeFrame replacementFrame = store.CurrentFrame!;
        completed.Dispose();
        completed.Dispose();
        Assert.AreSame(replacementFrame, store.CurrentFrame);
        Assert.AreEqual("same", store.Capture().OperationId);
        Assert.AreEqual(2, store.Capture().ScopeDepth);
    }

    [TestMethod]
    public void CompletedRootHandleCanRepeatAfterTheStoreIsUsedForAnotherOperation()
    {
        var store = new OperationScopeStore();
        IDisposable completed = store.BeginScope(new OperationContextData { OperationId = "same" });
        completed.Dispose();
        using IDisposable replacement = store.BeginScope(new OperationContextData { OperationId = "same" });
        OperationScopeFrame replacementFrame = store.CurrentFrame!;
        completed.Dispose();
        Assert.AreSame(replacementFrame, store.CurrentFrame);
        Assert.AreEqual(1, store.Capture().ScopeDepth);
    }

    [TestMethod]
    public async Task OwnerCanDisposeAcrossAwaitAndUnrelatedAmbientContextChanges()
    {
        var store = new OperationScopeStore();
        IDisposable handle = store.BeginScope(new OperationContextData { OperationId = "owner" });
        OperationScopeFrame frame = store.CurrentFrame!;
        var unrelated = new AsyncLocal<string?> { Value = "changed" };
        await Task.Yield();
        unrelated.Value = "changed-again";
        Assert.AreSame(frame, store.CurrentFrame);
        handle.Dispose();
        Assert.IsNull(store.CurrentFrame);
        await Task.Yield();
        handle.Dispose();
        Assert.IsNull(store.CurrentFrame);
    }

    [TestMethod]
    public async Task DisposingAnOwnedChildScopeAndRepeatingItLeavesTheInheritedParentHead()
    {
        var store = new OperationScopeStore();
        using IDisposable parent = store.BeginScope(new OperationContextData { OperationId = "parent" });
        OperationScopeFrame inherited = store.CurrentFrame!;
        await Task.Run(async () =>
        {
            IDisposable child = store.BeginScope(new OperationContextData { OperationId = "child" });
            await Task.Yield();
            child.Dispose();
            Assert.AreSame(inherited, store.CurrentFrame);
            child.Dispose();
            Assert.AreSame(inherited, store.CurrentFrame);
        });
        Assert.AreSame(inherited, store.CurrentFrame);
    }

    [TestMethod]
    public void IndependentStoresAllowInterleavedDisposalInTheirOwnLifoOrders()
    {
        var first = new OperationScopeStore();
        var second = new OperationScopeStore();
        IDisposable a = first.BeginScope(new OperationContextData { OperationId = "a" });
        IDisposable b = second.BeginScope(new OperationContextData { OperationId = "b" });
        IDisposable innerA = first.BeginScope(new OperationContextData { OperationId = "inner-a" });
        OperationScopeFrame firstHead = first.CurrentFrame!;
        Assert.ThrowsExactly<InvalidOperationException>(a.Dispose);
        Assert.AreSame(firstHead, first.CurrentFrame);
        b.Dispose();
        Assert.IsNull(second.CurrentFrame);
        Assert.AreSame(firstHead, first.CurrentFrame);
        innerA.Dispose();
        a.Dispose();
        b.Dispose();
        Assert.IsNull(first.CurrentFrame);
        Assert.IsNull(second.CurrentFrame);
    }

    [TestMethod]
    public async Task FailedLifoAttemptPreservesTheNeverCompletedAbsentFrameGuard()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        using IDisposable inner = store.BeginScope(new OperationContextData { OperationId = "inner" });
        OperationScopeFrame head = store.CurrentFrame!;
        InvalidOperationException order = Assert.ThrowsExactly<InvalidOperationException>(outer.Dispose);
        Assert.AreEqual("Dispose operation scopes in LIFO order.", order.Message);
        Assert.AreSame(head, store.CurrentFrame);
        Task foreign;
        using (ExecutionContext.SuppressFlow())
        {
            foreign = Task.Run(() =>
            {
                Assert.IsNull(store.CurrentFrame);
                InvalidOperationException absent = Assert.ThrowsExactly<InvalidOperationException>(outer.Dispose);
                Assert.AreEqual("The operation scope is not active in the current asynchronous context.", absent.Message);
                Assert.IsNull(store.CurrentFrame);
            });
        }
        await foreign;
        Assert.AreSame(head, store.CurrentFrame);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NeverCompletedAbsentHandleRejectsWithoutChangingEmptyOrUnrelatedHeads(bool ownChildScope)
    {
        var store = new OperationScopeStore();
        using IDisposable owner = store.BeginScope(new OperationContextData { OperationId = "owner" });
        OperationScopeFrame ownerFrame = store.CurrentFrame!;
        Task foreign;
        using (ExecutionContext.SuppressFlow())
        {
            foreign = Task.Run(async () =>
            {
                using IDisposable? child = ownChildScope
                    ? store.BeginScope(new OperationContextData { OperationId = "unrelated" }) : null;
                OperationScopeFrame? prior = store.CurrentFrame;
                await Task.Yield();
                InvalidOperationException absent = Assert.ThrowsExactly<InvalidOperationException>(owner.Dispose);
                Assert.AreEqual("The operation scope is not active in the current asynchronous context.", absent.Message);
                Assert.AreSame(prior, store.CurrentFrame);
                Assert.AreEqual(ownChildScope ? "unrelated" : null, store.Capture().OperationId);
            });
        }
        await foreign;
        Assert.AreSame(ownerFrame, store.CurrentFrame);
    }
}
