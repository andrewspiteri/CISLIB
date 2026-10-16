using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

internal static class OperationScopeProcessFixture
{
    internal static async Task<int> RunAsync(string mode)
    {
        JsonObject result = mode switch
        {
            "copy" => Copy(),
            "inherit" => await InheritAsync(),
            "isolated" => await IsolatedAsync(),
            _ => throw new InvalidDataException("Unknown native scope scenario.")
        };
        result["mode"] = mode;
        result["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(OperationScopeProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(OperationScopeStore).Assembly.Location)
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static JsonObject Copy()
    {
        var mutable = new Dictionary<string, LogValue> { ["value"] = LogValue.String("original") };
        var context = new OperationContextData { OperationId = " original ", Attributes = mutable };
        var store = new OperationScopeStore();
        IDisposable handle = store.BeginScope(context);
        CapturedOperationContext captured = store.CurrentFrame!.Context;
        context.OperationId = "changed";
        context.Attributes = null;
        mutable.Clear();
        mutable["changed"] = LogValue.Boolean(true);
        handle.Dispose();
        return new JsonObject
        {
            ["operationId"] = captured.OperationId,
            ["value"] = captured.Attributes.MapProperties["value"].Scalar.Text,
            ["nodes"] = captured.Attributes.NodeCount,
            ["depth"] = captured.Attributes.Depth,
            ["truncated"] = captured.IsTruncated,
            ["cleared"] = store.CurrentFrame is null
        };
    }

    private static async Task<JsonObject> InheritAsync()
    {
        var store = new OperationScopeStore();
        IDisposable parent = store.BeginScope(new OperationContextData { OperationId = "parent" });
        OperationScopeFrame inherited = store.CurrentFrame!;
        JsonObject result = await Task.Run(async () =>
        {
            bool inheritedCorrectly = ReferenceEquals(store.CurrentFrame, inherited);
            string? childId;
            using (store.BeginScope(new OperationContextData { OperationId = "child" }))
            {
                await Task.Yield();
                childId = store.CurrentFrame!.Context.OperationId;
            }
            return new JsonObject { ["childInherited"] = inheritedCorrectly, ["childId"] = childId, ["restored"] = ReferenceEquals(store.CurrentFrame, inherited) };
        }).ConfigureAwait(false);
        result["parentUnchanged"] = ReferenceEquals(store.CurrentFrame, inherited);
        parent.Dispose();
        result["cleared"] = store.CurrentFrame is null;
        return result;
    }

    private static async Task<JsonObject> IsolatedAsync()
    {
        var first = new OperationScopeStore();
        var second = new OperationScopeStore();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int participants = 0;
        async Task<string[]> Run(string id)
        {
            using IDisposable a = first.BeginScope(new OperationContextData { OperationId = id });
            using IDisposable b = second.BeginScope(new OperationContextData { OperationId = id + "-other" });
            if (Interlocked.Increment(ref participants) == 2) { ready.SetResult(); }
            await release.Task.ConfigureAwait(false);
            return [first.CurrentFrame!.Context.OperationId!, second.CurrentFrame!.Context.OperationId!];
        }
        Task<string[]> one = Task.Run(() => Run("a"));
        Task<string[]> two = Task.Run(() => Run("b"));
        bool parentEmpty;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            parentEmpty = first.CurrentFrame is null && second.CurrentFrame is null;
        }
        finally { release.TrySetResult(); }
        string[][] values = await Task.WhenAll(one, two);
        return new JsonObject
        {
            ["first"] = new JsonArray(values[0][0], values[1][0]),
            ["second"] = new JsonArray(values[0][1], values[1][1]),
            ["parentEmpty"] = parentEmpty,
            ["afterEmpty"] = first.CurrentFrame is null && second.CurrentFrame is null
        };
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
