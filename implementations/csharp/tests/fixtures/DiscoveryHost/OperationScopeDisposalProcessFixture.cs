using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

internal static class OperationScopeDisposalProcessFixture
{
    internal static async Task<int> RunAsync(string mode)
    {
        JsonObject result = mode switch
        {
            "lifo" => Lifo(),
            "repeat" => Repeat(),
            "await" => await AwaitAsync(),
            "absent" => await AbsentAsync(),
            _ => throw new InvalidDataException("Unknown native scope-disposal scenario.")
        };
        result["mode"] = mode;
        result["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(OperationScopeDisposalProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(OperationScopeStore).Assembly.Location)
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static JsonObject Lifo()
    {
        var store = new OperationScopeStore();
        IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        IDisposable middle = store.BeginScope(new OperationContextData { OperationId = "middle" });
        IDisposable inner = store.BeginScope(new OperationContextData { OperationId = "inner" });
        OperationScopeFrame prior = store.CurrentFrame!;
        InvalidOperationException? error = Error(outer);
        bool unchanged = ReferenceEquals(prior, store.CurrentFrame);
        inner.Dispose();
        string? first = store.Capture().OperationId;
        middle.Dispose();
        string? second = store.Capture().OperationId;
        outer.Dispose();
        return new JsonObject
        {
            ["error"] = error?.GetType().Name,
            ["message"] = error?.Message,
            ["headUnchanged"] = unchanged,
            ["restoredIds"] = new JsonArray(first, second, store.Capture().OperationId),
            ["storeEmpty"] = store.CurrentFrame is null
        };
    }

    private static JsonObject Repeat()
    {
        var store = new OperationScopeStore();
        IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        var input = new OperationContextData { OperationId = "same" };
        IDisposable completed = store.BeginScope(input);
        completed.Dispose();
        IDisposable replacement = store.BeginScope(input);
        OperationScopeFrame prior = store.CurrentFrame!;
        completed.Dispose();
        completed.Dispose();
        bool replacementUntouched = ReferenceEquals(prior, store.CurrentFrame);
        replacement.Dispose();
        bool repeatRestored = store.Capture().OperationId == "outer";
        outer.Dispose();
        outer.Dispose();
        bool ownerRepeatsHarmless = repeatRestored && store.CurrentFrame is null;
        IDisposable next = store.BeginScope(new OperationContextData { OperationId = "next" });
        OperationScopeFrame nextFrame = store.CurrentFrame!;
        outer.Dispose();
        bool nextUntouched = ReferenceEquals(nextFrame, store.CurrentFrame);
        next.Dispose();
        return new JsonObject
        {
            ["sameValueReplacementUntouched"] = replacementUntouched,
            ["ownerRepeatsHarmless"] = ownerRepeatsHarmless,
            ["newOperationUntouched"] = nextUntouched,
            ["storeEmpty"] = store.CurrentFrame is null
        };
    }

    private static async Task<JsonObject> AwaitAsync()
    {
        var store = new OperationScopeStore();
        IDisposable owner = store.BeginScope(new OperationContextData { OperationId = "owner" });
        OperationScopeFrame frame = store.CurrentFrame!;
        var unrelated = new AsyncLocal<string?> { Value = "before" };
        await Task.Yield();
        unrelated.Value = "after";
        bool preserved = ReferenceEquals(frame, store.CurrentFrame);
        bool[] child = await Task.Run(async () =>
        {
            bool inherited = ReferenceEquals(frame, store.CurrentFrame);
            using (store.BeginScope(new OperationContextData { OperationId = "child" })) { await Task.Yield(); }
            return new[] { inherited, ReferenceEquals(frame, store.CurrentFrame) };
        }).ConfigureAwait(false);
        bool parentUnchanged = ReferenceEquals(frame, store.CurrentFrame);
        owner.Dispose();
        await Task.Yield();
        owner.Dispose();
        return new JsonObject
        {
            ["ownerFramePreserved"] = preserved,
            ["childInherited"] = child[0],
            ["childOwnPopRestoredParent"] = child[1],
            ["parentAfterChildUnchanged"] = parentUnchanged,
            ["ownerRepeatsHarmless"] = store.CurrentFrame is null,
            ["storeEmpty"] = store.CurrentFrame is null
        };
    }

    private static async Task<JsonObject> AbsentAsync()
    {
        var store = new OperationScopeStore();
        IDisposable outer = store.BeginScope(new OperationContextData { OperationId = "outer" });
        IDisposable inner = store.BeginScope(new OperationContextData { OperationId = "inner" });
        OperationScopeFrame creatorHead = store.CurrentFrame!;
        InvalidOperationException? order = Error(outer);
        Task<JsonObject> foreign;
        using (ExecutionContext.SuppressFlow())
        {
            foreign = Task.Run(() =>
            {
                OperationScopeFrame? before = store.CurrentFrame;
                InvalidOperationException? error = Error(outer);
                return new JsonObject
                {
                    ["error"] = error?.GetType().Name,
                    ["message"] = error?.Message,
                    ["foreignHeadUnchanged"] = before is null && store.CurrentFrame is null,
                    ["absentGuardStillRejects"] = order is not null && error is not null
                };
            });
        }
        JsonObject result = await foreign.ConfigureAwait(false);
        result["creatorHeadUnchanged"] = ReferenceEquals(creatorHead, store.CurrentFrame);
        inner.Dispose();
        outer.Dispose();
        result["storeEmpty"] = store.CurrentFrame is null;
        return result;
    }

    private static InvalidOperationException? Error(IDisposable handle)
    {
        try { handle.Dispose(); return null; }
        catch (InvalidOperationException error) { return error; }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
