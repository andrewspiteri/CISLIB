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
        object[] cases = [await AsyncHelperAsync(), StaleContext(), await DetachedChildAsync(), await SiblingMisuseAsync()];
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

    private static async Task<object> AsyncHelperAsync()
    {
        var store = new OperationScopeStore();
        IDisposable parent = store.BeginScope(new OperationContextData { OperationId = "parent" });
        OperationScopeFrame parentFrame = store.CurrentFrame!;
        async Task<(IDisposable Handle, OperationScopeFrame Frame)> OpenInsideHelper()
        {
            IDisposable handle = store.BeginScope(new OperationContextData { OperationId = "helper" });
            OperationScopeFrame frame = store.CurrentFrame!;
            await Task.Yield();
            Require(ReferenceEquals(frame, store.CurrentFrame), "Helper lost its own frame before return.");
            return (handle, frame);
        }
        var returned = await OpenInsideHelper().ConfigureAwait(false);
        bool callerStillHasParent = ReferenceEquals(parentFrame, store.CurrentFrame);
        bool returnedFrameIsAbsent = !ReferenceEquals(returned.Frame, store.CurrentFrame);
        string? error = DisposeError(returned.Handle);
        bool rejectedWithoutMutation = ReferenceEquals(parentFrame, store.CurrentFrame);
        parent.Dispose();
        Require(callerStillHasParent && returnedFrameIsAbsent && rejectedWithoutMutation
            && error == typeof(InvalidOperationException).FullName && store.CurrentFrame is null,
            "Async helper return characterization differs.");
        // The returned helper handle is intentionally unusable in the caller; this process ends the experiment.
        return new { name = "async-helper-return-does-not-install-scope-in-caller", callerStillHasParent,
            returnedFrameIsAbsent, disposeError = error, rejectedWithoutMutation, callerHeadCleared = store.CurrentFrame is null };
    }

    private static object StaleContext()
    {
        var store = new OperationScopeStore();
        IDisposable handle = store.BeginScope(new OperationContextData { OperationId = "stale" });
        OperationScopeFrame frame = store.CurrentFrame!;
        ExecutionContext stale = ExecutionContext.Capture()!;
        handle.Dispose();
        bool ownerEmptyBefore = store.CurrentFrame is null;
        bool staleFrameRestored = false;
        bool staleViewCleared = false;
        string? error = null;
        ExecutionContext.Run(stale, _ =>
        {
            staleFrameRestored = ReferenceEquals(frame, store.CurrentFrame);
            error = DisposeError(handle);
            staleViewCleared = store.CurrentFrame is null;
        }, null);
        bool ownerEmptyAfter = store.CurrentFrame is null;
        Require(ownerEmptyBefore && staleFrameRestored && staleViewCleared && error is null && ownerEmptyAfter,
            "Stale restored context characterization differs.");
        return new { name = "completed-handle-in-explicitly-restored-stale-context", ownerEmptyBefore,
            staleFrameRestored, disposeError = error, staleViewCleared, ownerEmptyAfter };
    }

    private static async Task<object> DetachedChildAsync()
    {
        var store = new OperationScopeStore();
        IDisposable parent = store.BeginScope(new OperationContextData { OperationId = "parent" });
        OperationScopeFrame inherited = store.CurrentFrame!;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<object> child = Task.Run(async () =>
        {
            ready.SetResult();
            await release.Task.ConfigureAwait(false);
            bool childKeepsInheritedFrame = ReferenceEquals(inherited, store.CurrentFrame);
            string? inheritedId = store.Capture().OperationId;
            using (store.BeginScope(new OperationContextData { OperationId = "child" }))
            {
                await Task.Yield();
                Require(store.Capture().OperationId == "child", "Child own scope missing.");
            }
            bool ownPopRestoresInheritedFrame = ReferenceEquals(inherited, store.CurrentFrame);
            Require(childKeepsInheritedFrame && inheritedId == "parent" && ownPopRestoresInheritedFrame,
                "Detached child inheritance characterization differs.");
            return (object)new { childKeepsInheritedFrame, inheritedId, ownPopRestoresInheritedFrame };
        });
        bool ownerEmptyBeforeRelease;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            parent.Dispose();
            ownerEmptyBeforeRelease = store.CurrentFrame is null;
        }
        finally { release.TrySetResult(); }
        object childResult = await child.WaitAsync(TimeSpan.FromSeconds(10));
        bool ownerStillEmpty = store.CurrentFrame is null;
        Require(ownerEmptyBeforeRelease && ownerStillEmpty, "Child altered creator state.");
        return new { name = "unawaited-child-lives-past-parent-scope-then-joined-for-observation",
            ownerEmptyBeforeRelease, childResult, ownerStillEmpty };
    }

    private static async Task<object> SiblingMisuseAsync()
    {
        var store = new OperationScopeStore();
        IDisposable parent = store.BeginScope(new OperationContextData { OperationId = "parent" });
        OperationScopeFrame inherited = store.CurrentFrame!;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int participants = 0;
        async Task<object> Misuse()
        {
            if (Interlocked.Increment(ref participants) == 2) { ready.SetResult(); }
            await release.Task.ConfigureAwait(false);
            bool inheritedHead = ReferenceEquals(inherited, store.CurrentFrame);
            string? error = DisposeError(parent);
            bool childViewCleared = store.CurrentFrame is null;
            Require(inheritedHead && error is null && childViewCleared, "Sibling illegal disposal observation differs.");
            return new { inheritedHead, disposeError = error, childViewCleared };
        }
        Task<object> first = Task.Run(Misuse);
        Task<object> second = Task.Run(Misuse);
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { release.TrySetResult(); }
        object[] children = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        bool parentHeadUnchanged = ReferenceEquals(inherited, store.CurrentFrame);
        parent.Dispose();
        Require(parentHeadUnchanged && store.CurrentFrame is null, "Sibling misuse altered creator head.");
        return new { name = "two-sibling-inherited-handle-misuses", children,
            parentHeadUnchanged, ownerHeadCleared = store.CurrentFrame is null };
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
