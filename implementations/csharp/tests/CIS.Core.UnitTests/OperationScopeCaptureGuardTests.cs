using System.Collections;
using CIS.Logging;
using CIS.Logging.Scopes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class OperationScopeCaptureGuardTests
{
    [TestMethod]
    public void IdentifierIsReadBeforeAReentrantCallerMutatesItsDto()
    {
        var input = new OperationContextData { OperationId = "before" };
        input.Attributes = new CallbackMap(() => input.OperationId = "after");
        var store = new OperationScopeStore();
        using IDisposable handle = store.BeginScope(input);
        Assert.AreEqual("before", store.CurrentFrame!.Context.OperationId);
        Assert.AreEqual("after", input.OperationId);
    }

    [TestMethod]
    public void FailedEnumerationDoesNotPublishAPartialScope()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        OperationScopeFrame prior = store.CurrentFrame!;
        Assert.ThrowsExactly<InvalidDataException>(() => store.BeginScope(new OperationContextData
        {
            Attributes = new CallbackMap(() => throw new InvalidDataException("Caller failure."))
        }));
        Assert.AreSame(prior, store.CurrentFrame);
    }

    [TestMethod]
    public void ReentrantPushCannotBeOverwrittenByTheOuterCapture()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        IDisposable? reentrant = null;
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => store.BeginScope(new OperationContextData
            {
                OperationId = "stale",
                Attributes = new CallbackMap(() => reentrant = store.BeginScope(new OperationContextData { OperationId = "newer" }))
            }));
            Assert.AreEqual("newer", store.CurrentFrame!.Context.OperationId);
            Assert.AreEqual("outer", store.CurrentFrame.Parent!.Context.OperationId);
        }
        finally { reentrant?.Dispose(); }
        Assert.AreEqual("outer", store.CurrentFrame!.Context.OperationId);
    }

    [TestMethod]
    public void InitialDepthGuardRunsBeforeCallerContentTraversal()
    {
        var store = new OperationScopeStore();
        var handles = new Stack<IDisposable>();
        int traversals = 0;
        try
        {
            for (int index = 0; index < 32; index++) { handles.Push(store.BeginScope(new OperationContextData())); }
            OperationScopeFrame prior = store.CurrentFrame!;
            Assert.AreEqual(32, prior.Depth);
            Assert.ThrowsExactly<InvalidOperationException>(() => store.BeginScope(new OperationContextData
            {
                Attributes = new CallbackMap(() => traversals++)
            }));
            Assert.AreEqual(0, traversals);
            Assert.AreSame(prior, store.CurrentFrame);
        }
        finally { while (handles.Count > 0) { handles.Pop().Dispose(); } }
        Assert.IsNull(store.CurrentFrame);
    }

    [TestMethod]
    public async Task AHandleAbsentFromTheCallingFlowRejectsDisposalWithoutMutation()
    {
        var store = new OperationScopeStore();
        var ready = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task child = Task.Run(async () =>
        {
            using (IDisposable handle = store.BeginScope(new OperationContextData { OperationId = "child" }))
            {
                ready.SetResult(handle);
                await release.Task.ConfigureAwait(false);
                Assert.AreEqual("child", store.CurrentFrame!.Context.OperationId);
            }
            Assert.IsNull(store.CurrentFrame);
        });
        try
        {
            IDisposable foreign = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsNull(store.CurrentFrame);
            Assert.ThrowsExactly<InvalidOperationException>(foreign.Dispose);
            Assert.IsNull(store.CurrentFrame);
        }
        finally { release.TrySetResult(); }
        await child;
        Assert.IsNull(store.CurrentFrame);
    }

    private sealed class CallbackMap(Action callback) : IReadOnlyDictionary<string, LogValue>
    {
        public LogValue this[string key] => throw new InvalidOperationException("Do not use an indexer during capture.");
        public IEnumerable<string> Keys => throw new InvalidOperationException("Do not enumerate keys separately.");
        public IEnumerable<LogValue> Values => throw new InvalidOperationException("Do not enumerate values separately.");
        public int Count => throw new InvalidOperationException("Do not trust a caller count.");
        public bool ContainsKey(string key) => throw new InvalidOperationException("Do not query a caller map.");
        public bool TryGetValue(string key, out LogValue value) => throw new InvalidOperationException("Do not query a caller map.");
        public IEnumerator<KeyValuePair<string, LogValue>> GetEnumerator()
        {
            callback();
            yield return new("value", LogValue.Null());
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
