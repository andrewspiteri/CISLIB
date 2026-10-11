using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CIS;
using CIS.Logging;
using CIS.Logging.Scopes;

internal static class Program
{
    private static async Task Main()
    {
        var cases = new List<object>();
        cases.Add(await InheritedHandleAsync());
        cases.Add(await AbsentHandleAsync());
        cases.Add(await OwnerContinuationAsync());
        cases.Add(LocalOrder());
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "investigation-only-not-conformance-pass",
            runtime = RuntimeInformation.FrameworkDescription,
            os = OperatingSystem.IsWindows() ? "windows" : "linux",
            coreSha256 = Hash(typeof(LogValue).Assembly.Location),
            probeSha256 = Hash(typeof(Program).Assembly.Location),
            publicFacadeInitialized = EnvironmentOptions.IsInitialized,
            cases
        }));
    }

    private static async Task<object> InheritedHandleAsync()
    {
        var store = new OperationScopeStore();
        IDisposable handle = store.BeginScope(new OperationContextData { OperationId = "parent" });
        OperationScopeFrame frame = store.CurrentFrame!;
        var ownerMarker = new AsyncLocal<object?> { Value = new object() };
        ExecutionContext ownerContext = ExecutionContext.Capture()!;
        object marker = ownerMarker.Value!;
        // Await first so the parent and child continuation can both have null Task.CurrentId.
        await Task.Yield();
        int? parentTaskId = Task.CurrentId;
        var child = await Task.Run(async () =>
        {
            await Task.Yield();
            bool inheritedFrame = ReferenceEquals(frame, store.CurrentFrame);
            bool sameExecutionContext = ReferenceEquals(ownerContext, ExecutionContext.Capture());
            bool sameOwnerMarker = ReferenceEquals(marker, ownerMarker.Value);
            int? childTaskId = Task.CurrentId;
            string? error = DisposeError(handle);
            return new { inheritedFrame, sameExecutionContext, sameOwnerMarker, childTaskId,
                disposeError = error, childHeadCleared = store.CurrentFrame is null };
        }).ConfigureAwait(false);
        bool parentHeadUnchanged = ReferenceEquals(frame, store.CurrentFrame);
        string? parentDisposeError = DisposeError(handle);
        bool parentHeadCleared = store.CurrentFrame is null;
        Require(child.inheritedFrame && child.sameExecutionContext && child.sameOwnerMarker,
            "Inherited identity counterexample did not execute.");
        Require(child.disposeError is null && child.childHeadCleared && parentHeadUnchanged,
            "The actual inherited-handle behavior changed.");
        Require(parentDisposeError is null && parentHeadCleared, "Parent cleanup failed.");
        return new { name = "inherited-parent-handle-misuse", parentTaskId, child,
            parentHeadUnchanged, parentDisposeError, parentHeadCleared };
    }

    private static async Task<object> AbsentHandleAsync()
    {
        var store = new OperationScopeStore();
        IDisposable handle = store.BeginScope(new OperationContextData { OperationId = "owner" });
        async Task<object> CallForeign(string phase, string? expectedError)
        {
            bool beforeEmpty = store.CurrentFrame is null;
            await Task.Yield();
            string? error = DisposeError(handle);
            bool afterEmpty = store.CurrentFrame is null;
            Require(beforeEmpty && afterEmpty, "Foreign-flow state changed."); Require(error == expectedError, "Foreign disposal counterexample changed.");
            return new { phase, beforeEmpty, afterEmpty, disposeError = error };
        }
        Task<object> before;
        using (ExecutionContext.SuppressFlow()) { before = Task.Run(() => CallForeign("before-owner-pop", typeof(InvalidOperationException).FullName)); }
        object beforeResult = await before.ConfigureAwait(false);
        Require(store.CurrentFrame is not null, "Foreign call altered parent head.");
        handle.Dispose();
        Task<object> after;
        using (ExecutionContext.SuppressFlow()) { after = Task.Run(() => CallForeign("after-owner-pop", null)); }
        object afterResult = await after.ConfigureAwait(false);
        return new { name = "absent-foreign-handle-before-and-after-owner-pop", beforeResult, afterResult,
            parentHeadCleared = store.CurrentFrame is null };
    }

    private static async Task<object> OwnerContinuationAsync()
    {
        var store = new OperationScopeStore();
        IDisposable handle = store.BeginScope(new OperationContextData { OperationId = "owner" });
        OperationScopeFrame frame = store.CurrentFrame!;
        ExecutionContext initialContext = ExecutionContext.Capture()!;
        int threadBefore = Environment.CurrentManagedThreadId;
        var otherLocal = new AsyncLocal<string?>();
        otherLocal.Value = "a legitimate ambient change";
        bool executionContextChanged = !ReferenceEquals(initialContext, ExecutionContext.Capture());
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ThreadPool.QueueUserWorkItem(_ => resumed.SetResult());
        await resumed.Task.ConfigureAwait(false);
        await Task.Yield();
        int threadAfter = Environment.CurrentManagedThreadId;
        bool sameFrame = ReferenceEquals(frame, store.CurrentFrame);
        string? error = DisposeError(handle);
        Require(executionContextChanged && sameFrame && error is null && store.CurrentFrame is null,
            "Legitimate disposal after ambient changes failed.");
        return new { name = "legitimate-owner-after-await-and-other-asynclocal-change",
            executionContextChanged, sameFrame, threadBefore, threadAfter,
            taskIdAfterAwait = Task.CurrentId, disposeError = error, headCleared = store.CurrentFrame is null };
    }

    private static object LocalOrder()
    {
        var store = new OperationScopeStore();
        IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        IDisposable inner = store.BeginScope(new OperationContextData { OperationId = "inner" });
        OperationScopeFrame head = store.CurrentFrame!;
        string? error = DisposeError(outer);
        bool unchanged = ReferenceEquals(head, store.CurrentFrame);
        inner.Dispose();
        inner.Dispose();
        outer.Dispose();
        outer.Dispose();
        Require(error == typeof(InvalidOperationException).FullName && unchanged && store.CurrentFrame is null,
            "Existing LIFO/repeat behavior failed.");
        return new { name = "normal-order-and-repeat", outOfOrderError = error,
            outOfOrderHeadUnchanged = unchanged, ownerRepeatsHarmless = true, headCleared = store.CurrentFrame is null };
    }

    private static string? DisposeError(IDisposable handle)
    {
        try { handle.Dispose(); return null; }
        catch (InvalidOperationException error) { return error.GetType().FullName; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
