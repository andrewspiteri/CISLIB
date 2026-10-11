using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

internal static class OperationScopeMergeProcessFixture
{
    internal static int Run(string mode)
    {
        JsonObject result = mode switch
        {
            "precedence" => Precedence(),
            "candidates" => Candidates(),
            "inherited-loss" => InheritedLoss(),
            "depth" => Depth(),
            _ => throw new InvalidDataException("Unknown native merged-scope scenario.")
        };
        result["mode"] = mode;
        result["observed"] = new JsonObject
        {
            ["publicFacadeInitialized"] = EnvironmentOptions.IsInitialized,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(OperationScopeMergeProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(OperationScopeStore).Assembly.Location)
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static JsonObject Precedence()
    {
        var store = new OperationScopeStore();
        IDisposable outer = Push(store, "outer", new Dictionary<string, LogValue>
        {
            ["shared"] = LogValue.String("outer"),
            ["Name"] = LogValue.String("upper"),
            ["operation_id"] = LogValue.String("attribute")
        });
        IDisposable inner = Push(store, "inner", new Dictionary<string, LogValue>
        {
            ["shared"] = LogValue.String("inner"),
            ["name"] = LogValue.String("lower")
        });
        CapturedScopeContext view = store.Capture();
        inner.Dispose();
        outer.Dispose();
        return new JsonObject
        {
            ["operationId"] = view.OperationId,
            ["shared"] = view.Attributes.MapProperties["shared"].Scalar.Text,
            ["caseKeys"] = new JsonArray(view.Attributes.MapProperties.Keys.Where(key => key is "Name" or "name").Order(StringComparer.Ordinal).Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()),
            ["ordinaryOperationAttribute"] = view.Attributes.MapProperties["operation_id"].Scalar.Text,
            ["scopeDepth"] = view.ScopeDepth,
            ["nodes"] = view.Attributes.NodeCount,
            ["truncated"] = view.IsTruncated,
            ["retainedAfterDispose"] = view.Attributes.MapProperties["shared"].Scalar.Text == "inner",
            ["storeEmpty"] = store.CurrentFrame is null
        };
    }

    private static JsonObject Candidates()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = Push(store, "outer", Enumerable.Range(0, 1023).ToDictionary(index => "k" + index.ToString(CultureInfo.InvariantCulture), index => LogValue.Integer(index)));
        var attributes = Enumerable.Range(0, 1022).ToDictionary(index => "new" + index.ToString(CultureInfo.InvariantCulture), _ => LogValue.Null());
        attributes.Add("k0", LogValue.String("last-inner"));
        using IDisposable inner = Push(store, "inner", attributes);
        CapturedScopeContext view = store.Capture();
        return new JsonObject
        {
            ["operationId"] = view.OperationId,
            ["first"] = view.Attributes.MapProperties["k0"].Scalar.Text,
            ["lastKept"] = view.Attributes.MapProperties["k1022"].Scalar.Integer,
            ["newKeyOmitted"] = !view.Attributes.MapProperties.ContainsKey("new0"),
            ["scopeInputsTruncated"] = store.CurrentFrame!.Context.IsTruncated || store.CurrentFrame.Parent!.Context.IsTruncated,
            ["count"] = view.Attributes.MapProperties.Count,
            ["nodes"] = view.Attributes.NodeCount,
            ["scopeDepth"] = view.ScopeDepth,
            ["truncated"] = view.IsTruncated
        };
    }

    private static JsonObject InheritedLoss()
    {
        var store = new OperationScopeStore();
        using IDisposable outer = Push(store, "outer", new Dictionary<string, LogValue> { ["same"] = LogValue.Array(Enumerable.Repeat(LogValue.Null(), 1024)) });
        using IDisposable inner = Push(store, null, new Dictionary<string, LogValue> { ["same"] = LogValue.Null() });
        CapturedScopeContext view = store.Capture();
        return new JsonObject
        {
            ["operationId"] = view.OperationId,
            ["kind"] = view.Attributes.MapProperties["same"].Kind.ToString(),
            ["childTruncated"] = view.Attributes.MapProperties["same"].IsTruncated,
            ["count"] = view.Attributes.MapProperties.Count,
            ["nodes"] = view.Attributes.NodeCount,
            ["scopeDepth"] = view.ScopeDepth,
            ["truncated"] = view.IsTruncated
        };
    }

    private static JsonObject Depth()
    {
        var store = new OperationScopeStore();
        var handles = new Stack<IDisposable>();
        int traversals = 0;
        JsonObject result;
        try
        {
            for (int index = 0; index < 32; index++)
            {
                string id = "d" + index.ToString(CultureInfo.InvariantCulture);
                handles.Push(Push(store, id, new Dictionary<string, LogValue> { [id] = LogValue.Integer(index) }));
            }
            OperationScopeFrame prior = store.CurrentFrame!;
            CapturedScopeContext view = store.Capture();
            string? failure = null;
            try
            {
                IDisposable unexpected = store.BeginScope(new OperationContextData
                {
                    OperationId = new string((char)0xd800, 1),
                    Attributes = new CountingMap(() => traversals++)
                });
                unexpected.Dispose();
            }
            catch (InvalidOperationException error) { failure = error.GetType().Name; }
            result = new JsonObject
            {
                ["operationId"] = view.OperationId,
                ["scopeDepth"] = view.ScopeDepth,
                ["nodes"] = view.Attributes.NodeCount,
                ["truncated"] = view.IsTruncated,
                ["rejection"] = failure,
                ["traversals"] = traversals,
                ["unchanged"] = ReferenceEquals(prior, store.CurrentFrame)
            };
        }
        finally { while (handles.Count > 0) { handles.Pop().Dispose(); } }
        result["storeEmpty"] = store.CurrentFrame is null;
        return result;
    }

    private static IDisposable Push(OperationScopeStore store, string? id, IReadOnlyDictionary<string, LogValue> attributes) =>
        store.BeginScope(new OperationContextData { OperationId = id, Attributes = attributes });
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

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
