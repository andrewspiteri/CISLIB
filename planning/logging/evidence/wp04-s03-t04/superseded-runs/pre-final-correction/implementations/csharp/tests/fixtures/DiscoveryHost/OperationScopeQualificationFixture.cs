using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

// Executes only actual Core scopes. This is a development observer, not a runtime substitute.
internal static class OperationScopeQualificationFixture
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        string mode = row["input"]!["scenario"]!.GetValue<string>();
        int count = row["input"]!["participantsOrLevels"]!.GetValue<int>();
        OperationScopeStore[] stores = Enumerable.Range(0, mode == "stores" ? count : 1)
            .Select(_ => new OperationScopeStore()).ToArray();
        var parents = new List<IDisposable>();
        var result = new JsonObject { ["caseId"] = row["id"]!.DeepClone() };
        try
        {
            for (int index = 0; index < stores.Length; index++)
            {
                string id = mode == "stores" ? "store-" + index : "parent";
                parents.Add(stores[index].BeginScope(Context(id, ("root", LogValue.String(id)), ("shared", LogValue.String(id)))));
            }
            OperationScopeFrame?[] parentHeads = stores.Select(store => store.CurrentFrame).ToArray();
            result["parentBefore"] = Views(stores);
            if (mode == "nested")
            {
                var barrier = new Gate();
                Task<JsonObject> worker = Start(barrier, () => NestedAsync(stores[0], count,
                    row["input"]!["blankLastId"]!.GetValue<bool>(), barrier));
                Exception? readinessError = null;
                try
                {
                    await barrier.Ready.Task.WaitAsync(Deadline).ConfigureAwait(false);
                    result["parentWhileChildrenBlocked"] = Views(stores);
                }
                catch (Exception error) { readinessError = error; }
                finally { barrier.Release.TrySetResult(); }
                JsonObject[] joined = await JoinAsync([worker], readinessError).ConfigureAwait(false);
                JsonObject nested = joined[0];
                foreach ((string key, JsonNode? value) in nested) { result[key] = value?.DeepClone(); }
            }
            else
            {
                using var cancellation = new CancellationTokenSource();
                var mutable = new Dictionary<string, LogValue>();
                var nestedMap = new Dictionary<string, LogValue> { ["leaf"] = LogValue.String("original") };
                var sourceItems = new List<LogValue> { LogValue.Map(nestedMap), LogValue.Null() };
                LogValue ownedPayload = LogValue.Map(new Dictionary<string, LogValue>
                {
                    ["items"] = LogValue.Array(sourceItems),
                    ["flag"] = LogValue.Boolean(true)
                });
                mutable["payload"] = ownedPayload;
                if (mode == "mutation")
                {
                    nestedMap["leaf"] = LogValue.String("changed-before-scope");
                    sourceItems[0] = LogValue.Null();
                    result["ownedNestedValueBeforeBeginScope"] = LogValueFixtureAdapter.Project(ownedPayload);
                }
                var input = new OperationContextData
                {
                    OperationId = " before ",
                    Attributes = mode == "mutation" && row["input"]!["readOnlyWrapper"]!.GetValue<bool>()
                        ? new ReadOnlyDictionary<string, LogValue>(mutable) : mutable
                };
                int workers = mode is "inherit" or "concurrent" ? count : mode == "stores" ? 2 : 1;
                Gate[] gates = Enumerable.Range(0, workers).Select(_ => new Gate()).ToArray();
                Task<JsonObject>[] tasks = gates.Select((gate, index) => Start(gate, () => mode switch
                {
                    "inherit" => InheritAsync(stores[0], index, gate),
                    "concurrent" => ConcurrentAsync(stores[0], index, gate),
                    "stores" => StoresAsync(stores, index, gate),
                    "cancellation" => CancellationAsync(stores[0], cancellation.Token,
                        row["input"]!["cancelAwait"]!.GetValue<bool>(), gate),
                    "mutation" => MutationAsync(stores[0], input, gate),
                    "invalid-order" => InvalidOrderAsync(stores[0], count, gate),
                    _ => throw new InvalidDataException("Unknown scope qualification scenario.")
                })).ToArray();
                Exception? readinessError = null;
                try
                {
                    await Task.WhenAll(gates.Select(gate => gate.Ready.Task)).WaitAsync(Deadline).ConfigureAwait(false);
                    result["parentWhileChildrenBlocked"] = Views(stores);
                    if (mode == "cancellation") { cancellation.Cancel(); }
                    if (mode == "mutation")
                    {
                        input.OperationId = "changed";
                        input.Attributes = new Dictionary<string, LogValue> { ["new"] = LogValue.String("changed") };
                        mutable.Clear();
                        mutable["new"] = LogValue.String("changed");
                        nestedMap["leaf"] = LogValue.String("changed");
                        sourceItems[0] = LogValue.Null();
                        result["ownedNestedValueAfterCallerMutation"] = LogValueFixtureAdapter.Project(ownedPayload);
                        result["callerInputsActuallyMutated"] = input.OperationId == "changed"
                            && mutable.ContainsKey("new") && !mutable.ContainsKey("payload")
                            && sourceItems[0].Kind == LogValueKind.Null && nestedMap["leaf"].Scalar.Text == "changed";
                    }
                }
                catch (Exception error) { readinessError = error; }
                finally { foreach (Gate gate in gates.Reverse()) { gate.Release.TrySetResult(); } }
                JsonObject[] children = await JoinAsync(tasks, readinessError).ConfigureAwait(false);
                result["children"] = new JsonArray(children.Select(child => (JsonNode)child).ToArray());
            }
            result["parentAfterChildren"] = Views(stores);
            result["parentHeadsUnchanged"] = stores.Select((store, index) => ReferenceEquals(store.CurrentFrame, parentHeads[index])).All(value => value);
        }
        finally { for (int index = parents.Count - 1; index >= 0; index--) { parents[index].Dispose(); } }
        result["storesEmptyAfterOwnerCleanup"] = stores.All(store => store.CurrentFrame is null);
        return result;
    }

    internal static async Task<JsonObject[]> JoinAsync(Task<JsonObject>[] tasks, Exception? readinessError)
    {
        Task<JsonObject[]> allWorkers = Task.WhenAll(tasks);
        try
        {
            JsonObject[] result = await allWorkers.WaitAsync(Deadline).ConfigureAwait(false);
            if (readinessError is not null) { ExceptionDispatchInfo.Capture(readinessError).Throw(); }
            return result;
        }
        catch (Exception joinError)
        {
            var errors = allWorkers.IsCompleted
                ? new List<Exception>(allWorkers.Exception?.InnerExceptions ?? [joinError])
                : tasks.Where(task => task.IsFaulted).SelectMany(task => task.Exception!.InnerExceptions).ToList();
            if (!allWorkers.IsCompleted)
            {
                // Observe eventual faults; timeout is a failed fixture, not proof of joined cleanup.
                _ = allWorkers.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                errors.Add(joinError);
            }
            if (readinessError is not null && !errors.Any(error => ReferenceEquals(error, readinessError))) { errors.Insert(0, readinessError); }
            throw new AggregateException("Scope fixture readiness/completed worker failures; timeout may leave pending workers.", errors);
        }
    }

    private static Task<JsonObject> Start(Gate gate, Func<Task<JsonObject>> action) => Task.Run(async () =>
    {
        try { return await action().ConfigureAwait(false); }
        catch (Exception error) { gate.Ready.TrySetException(error); throw; }
    });

    private static async Task<JsonObject> NestedAsync(OperationScopeStore store, int count, bool blankLast, Gate gate)
    {
        var handles = new List<IDisposable>();
        var previous = new List<OperationScopeFrame?>();
        var restoredIds = new JsonArray();
        var restoredDepths = new JsonArray();
        var result = new JsonObject();
        bool exact = true;
        try
        {
            for (int index = 1; index <= count; index++)
            {
                previous.Add(store.CurrentFrame);
                handles.Add(store.BeginScope(Context(blankLast && index == count ? " " : "nested-" + index,
                    ("shared", LogValue.String("nested-" + index)), ("level", LogValue.Integer(index)))));
            }
            result["during"] = View(store.Capture());
            await gate.WaitAsync().ConfigureAwait(false);
        }
        finally
        {
            for (int index = handles.Count - 1; index >= 0; index--)
            {
                handles[index].Dispose();
                exact &= ReferenceEquals(store.CurrentFrame, previous[index]);
                handles[index].Dispose();
                exact &= ReferenceEquals(store.CurrentFrame, previous[index]);
                restoredIds.Add(store.Capture().OperationId);
                restoredDepths.Add(store.Capture().ScopeDepth);
            }
        }
        result["restoredIds"] = restoredIds;
        result["restoredDepths"] = restoredDepths;
        result["exactParentsRestored"] = exact;
        return result;
    }

    private static async Task<JsonObject> InheritAsync(OperationScopeStore store, int index, Gate gate)
    {
        OperationScopeFrame? parent = store.CurrentFrame;
        var result = new JsonObject { ["inherited"] = View(store.Capture()) };
        using (store.BeginScope(Context("child-" + index, ("shared", LogValue.String("child-" + index)), ("tag", LogValue.Integer(index)))))
        {
            result["during"] = View(store.Capture());
            await gate.WaitAsync().ConfigureAwait(false);
        }
        result["restored"] = View(store.Capture());
        result["exactInheritedHeadRestored"] = ReferenceEquals(store.CurrentFrame, parent);
        return result;
    }

    private static async Task<JsonObject> ConcurrentAsync(OperationScopeStore store, int index, Gate gate)
    {
        OperationScopeFrame? parent = store.CurrentFrame;
        var result = new JsonObject();
        using (store.BeginScope(Context("operation-" + index, ("shared", LogValue.String("operation-" + index)), ("tag", LogValue.Integer(index)))))
        {
            using (store.BeginScope(Context("inner-" + index, ("shared", LogValue.String("inner-" + index)))))
            {
                result["during"] = View(store.Capture());
                await gate.WaitAsync().ConfigureAwait(false);
            }
            result["afterInner"] = View(store.Capture());
        }
        result["restored"] = View(store.Capture());
        result["exactInheritedHeadRestored"] = ReferenceEquals(store.CurrentFrame, parent);
        return result;
    }

    private static async Task<JsonObject> StoresAsync(OperationScopeStore[] stores, int worker, Gate gate)
    {
        OperationScopeFrame?[] inherited = stores.Select(store => store.CurrentFrame).ToArray();
        var handles = new List<IDisposable>();
        var result = new JsonObject();
        try
        {
            for (int index = 0; index < stores.Length; index++)
            {
                string id = "worker-" + worker + "-store-" + index;
                handles.Add(stores[index].BeginScope(Context(id, ("shared", LogValue.String(id)))));
            }
            result["during"] = Views(stores);
            await gate.WaitAsync().ConfigureAwait(false);
        }
        finally { for (int index = handles.Count - 1; index >= 0; index--) { handles[index].Dispose(); } }
        result["restored"] = Views(stores);
        result["exactInheritedHeadsRestored"] = stores.Select((store, index) => ReferenceEquals(store.CurrentFrame, inherited[index])).All(value => value);
        return result;
    }

    private static async Task<JsonObject> CancellationAsync(OperationScopeStore store, CancellationToken token, bool cancelAwait, Gate gate)
    {
        OperationScopeFrame? parent = store.CurrentFrame;
        var result = new JsonObject();
        bool cancelled = false;
        bool matched = false;
        try
        {
            using (store.BeginScope(Context("cancelled", ("shared", LogValue.String("cancelled")))))
            {
                result["during"] = View(store.Capture());
                if (cancelAwait)
                {
                    Task cancellableWait = gate.Release.Task.WaitAsync(token);
                    gate.Ready.TrySetResult();
                    await cancellableWait.ConfigureAwait(false);
                }
                else
                {
                    gate.Ready.TrySetResult();
                    await gate.Release.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
            }
        }
        catch (OperationCanceledException error) { cancelled = true; matched = error.CancellationToken == token; }
        result["callerCancellationObserved"] = cancelled;
        result["cancellationTokenMatched"] = matched;
        result["restored"] = View(store.Capture());
        result["exactInheritedHeadRestored"] = ReferenceEquals(store.CurrentFrame, parent);
        return result;
    }

    private static async Task<JsonObject> MutationAsync(OperationScopeStore store, OperationContextData input, Gate gate)
    {
        OperationScopeFrame? parent = store.CurrentFrame;
        var result = new JsonObject();
        using (store.BeginScope(input))
        {
            result["before"] = View(store.Capture());
            await gate.WaitAsync().ConfigureAwait(false);
            result["after"] = View(store.Capture());
        }
        result["restored"] = View(store.Capture());
        result["exactInheritedHeadRestored"] = ReferenceEquals(store.CurrentFrame, parent);
        return result;
    }

    private static async Task<JsonObject> InvalidOrderAsync(OperationScopeStore store, int count, Gate gate)
    {
        OperationScopeFrame? parent = store.CurrentFrame;
        var handles = new List<IDisposable>();
        var result = new JsonObject();
        try
        {
            for (int index = 1; index <= count; index++)
            {
                handles.Add(store.BeginScope(Context("order-" + index, ("shared", LogValue.String("order-" + index)), ("level", LogValue.Integer(index)))));
            }
            await gate.WaitAsync().ConfigureAwait(false);
            OperationScopeFrame? head = store.CurrentFrame;
            result["during"] = View(store.Capture());
            try { handles[0].Dispose(); }
            catch (InvalidOperationException error)
            {
                result["error"] = "invalid-disposal-order";
                result["nativeError"] = error.GetType().Name;
                result["nativeMessage"] = error.Message;
            }
            result["headUnchanged"] = ReferenceEquals(head, store.CurrentFrame);
            result["afterError"] = View(store.Capture());
        }
        finally { for (int index = handles.Count - 1; index >= 0; index--) { handles[index].Dispose(); } }
        result["restored"] = View(store.Capture());
        result["exactInheritedHeadRestored"] = ReferenceEquals(store.CurrentFrame, parent);
        return result;
    }

    private static OperationContextData Context(string id, params (string Key, LogValue Value)[] attributes) => new()
    {
        OperationId = id,
        Attributes = attributes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
    };

    private static JsonArray Views(IEnumerable<OperationScopeStore> stores) => new(stores.Select(store => (JsonNode)View(store.Capture())).ToArray());

    private static JsonObject View(CapturedScopeContext view) => new()
    {
        ["operationId"] = view.OperationId,
        ["scopeDepth"] = view.ScopeDepth,
        ["isTruncated"] = view.IsTruncated,
        ["attributes"] = LogValueFixtureAdapter.Project(view.Attributes)
    };

    private sealed class Gate
    {
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal async Task WaitAsync()
        {
            Ready.TrySetResult();
            await Release.Task.ConfigureAwait(false);
        }
    }
}
