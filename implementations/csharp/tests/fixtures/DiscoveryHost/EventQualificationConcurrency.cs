using System.Diagnostics;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

internal static class EventQualificationConcurrency
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    internal static async Task ExecuteAsync(JsonObject row, JsonObject result)
    {
        bool separateStores = row["input"]!["mode"]!.GetValue<string>() == "stores";
        int count = row["input"]!["participants"]!.GetValue<int>();
        int cap = row["input"]!["maxEntryBytes"]!.GetValue<int>();
        OperationScopeStore[] stores = Enumerable.Range(0, separateStores ? count : 1).Select(_ => new OperationScopeStore()).ToArray();
        var handles = new List<IDisposable>();
        using var parentActivity = EventQualificationFixture.StartActivity("parent");
        try
        {
            foreach (OperationScopeStore store in stores) { handles.Add(store.BeginScope(EventQualificationFixture.Context("parent", "parent"))); }
            OperationScopeFrame?[] heads = stores.Select(store => store.CurrentFrame).ToArray();
            TaskCompletionSource[] ready = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<JsonObject>[] workers = Enumerable.Range(0, count).Select(index => Task.Run(async () =>
            {
                try { return await WorkerAsync(stores[separateStores ? index : 0], index, separateStores, cap, ready[index], release.Task).ConfigureAwait(false); }
                catch (Exception error) { ready[index].TrySetException(error); throw; }
            })).ToArray();
            Exception? readinessError = null;
            try
            {
                await Task.WhenAll(ready.Select(signal => signal.Task)).WaitAsync(Deadline).ConfigureAwait(false);
                result["parentWhileBlocked"] = new JsonArray(stores.Select(store => (JsonNode)new JsonObject
                {
                    ["operationId"] = store.Capture().OperationId,
                    ["shared"] = store.Capture().Attributes.MapProperties["shared"].Scalar.Text
                }).ToArray());
                result["parentActivityUnchanged"] = ReferenceEquals(Activity.Current, parentActivity);
            }
            catch (Exception error) { readinessError = error; }
            finally { release.TrySetResult(); }
            // Existing joined-failure handling observes every worker and distinguishes a timeout.
            JsonObject[] joined = await OperationScopeQualificationFixture.JoinAsync(workers, readinessError).ConfigureAwait(false);
            foreach (JsonObject record in joined) { result["records"]!.AsArray().Add(record); }
            result["parentHeadsUnchanged"] = stores.Select((store, index) => ReferenceEquals(store.CurrentFrame, heads[index])).All(value => value);
        }
        finally { for (int index = handles.Count - 1; index >= 0; index--) { handles[index].Dispose(); } }
        result["storesEmpty"] = stores.All(store => store.CurrentFrame is null);
    }

    private static async Task<JsonObject> WorkerAsync(OperationScopeStore store, int index, bool separateStores, int cap, TaskCompletionSource ready, Task release)
    {
        OperationScopeFrame? inherited = store.CurrentFrame;
        Activity? inheritedActivity = Activity.Current;
        var metadata = EventQualificationFixture.Metadata();
        if (separateStores) { metadata.ApplicationName += ".Store" + index; }
        var inputs = new Dictionary<string, LogValue> { ["worker"] = LogValue.Integer(index), ["shared"] = LogValue.String("event-" + index) };
        var data = new LogEventData { EventName = "qualification.event", Message = "worker-" + index, OperationId = index % 2 == 0 ? " " : " event-" + index + " ", Attributes = inputs };
        CapturedEventFacts facts;
        JsonObject sampled;
        using (var activity = new Activity("worker-" + index).SetIdFormat(ActivityIdFormat.W3C).Start())
        using (store.BeginScope(EventQualificationFixture.Context("operation-" + index, "scope-" + index)))
        {
            sampled = EventQualificationFixture.Sample(activity);
            facts = EventQualificationFixture.Capture(metadata, store, data);
            ready.TrySetResult();
            await release.ConfigureAwait(false);
            data.Message = "changed"; data.OperationId = "changed"; inputs.Clear(); metadata.ApplicationName = "changed";
        }
        // Encode only after the creator has disposed its scope and activity.
        var output = new JsonObject { ["records"] = new JsonArray() };
        EventQualificationFixture.Add(output, facts, cap, "worker-" + index, sampled);
        JsonObject record = output["records"]![0]!.AsObject();
        record["scopeRestored"] = ReferenceEquals(store.CurrentFrame, inherited);
        record["activityRestored"] = ReferenceEquals(Activity.Current, inheritedActivity);
        record["callerChanged"] = data.Message == "changed" && inputs.Count == 0 && metadata.ApplicationName == "changed";
        return record.DeepClone().AsObject();
    }
}
