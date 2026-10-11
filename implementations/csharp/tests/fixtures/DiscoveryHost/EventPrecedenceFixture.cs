using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

// Owned facts observer; actual activity/wire/runtime integration is not simulated.
internal static class EventPrecedenceFixture
{
    internal static JsonObject Execute(JsonObject row)
    {
        JsonObject input = row["input"]!.DeepClone().AsObject();
        JsonObject metadata = input["metadata"]!.AsObject();
        EnvironmentSnapshot snapshot = EventProjectionFixtureSources.Create(metadata, out EnvironmentSetup setup, out _);
        var store = new OperationScopeStore();
        OperationContextData[] contexts = input["scopes"]!.AsArray().Select(node => Context(node!.AsObject())).ToArray();
        LogEventData data = EventProjectionFixture.Input(input["event"]!.AsObject());
        var owners = new List<IDisposable>();
        CapturedEventFacts? captured;
        JsonObject? before;
        bool headUnchanged;
        int depth;
        try
        {
            foreach (OperationContextData context in contexts) { owners.Add(store.BeginScope(context)); }
            // Actual BeginScope must own input before later DTO/dictionary mutation.
            foreach (OperationContextData context in contexts)
            {
                context.OperationId = "changed";
                if (context.Attributes is Dictionary<string, LogValue> original) { original.Clear(); original["changed"] = LogValue.Null(); }
                context.Attributes = null;
            }
            OperationScopeFrame? head = store.CurrentFrame;
            CapturedScopeContext view = store.Capture();
            depth = view.ScopeDepth;
            captured = EventProjection.Capture(snapshot, view, input["component"]!.GetValue<string>(),
                Enum.Parse<CoreLogLevel>(input["severity"]!.GetValue<string>()), data);
            headUnchanged = ReferenceEquals(head, store.CurrentFrame);
            before = EventProjectionFixture.Project(captured);
        }
        finally
        {
            for (int index = owners.Count - 1; index >= 0; index--) { owners[index].Dispose(); }
        }
        data.EventName = "changed"; data.Message = "changed"; data.OperationId = "changed";
        if (data.Attributes is Dictionary<string, LogValue> eventAttributes) { eventAttributes.Clear(); eventAttributes["changed"] = LogValue.Null(); }
        data.Attributes = new Dictionary<string, LogValue>(); data.MessageTemplate = "changed";
        setup.ApplicationName = "Changed.App"; setup.Logging!.QueueCapacity = 1;
        metadata["service"] = "Changed.App";
        using IDisposable replacement = store.BeginScope(new OperationContextData { OperationId = "changed" });
        JsonObject? after = EventProjectionFixture.Project(captured);
        return new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["projection"] = after,
            ["scopeHeadUnchanged"] = headUnchanged,
            ["capturedScopeDepth"] = depth,
            ["stableAfterMutation"] = JsonNode.DeepEquals(before, after),
            ["callerMutationApplied"] = contexts.All(context => context.OperationId == "changed" && context.Attributes is null)
                && data.EventName == "changed" && metadata["service"]!.GetValue<string>() == "Changed.App"
                && setup.Logging.QueueCapacity == 1 && store.Capture().OperationId == "changed"
        };
    }
    private static OperationContextData Context(JsonObject input) => new()
    {
        OperationId = input["operationId"]?.GetValue<string>(),
        Attributes = input["attributes"]?.AsObject().ToDictionary(pair => pair.Key,
            pair => LogValueFixtureAdapter.Construct(pair.Value!.AsObject(), () => { }), StringComparer.Ordinal)
    };
}
