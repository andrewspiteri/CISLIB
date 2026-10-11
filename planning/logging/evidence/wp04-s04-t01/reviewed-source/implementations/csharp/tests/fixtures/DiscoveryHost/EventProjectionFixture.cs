using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Exceptions;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

// Development transport for actual projection facts, not a complete wire record or runtime substitute.
internal static class EventProjectionFixture
{
    internal static JsonObject Execute(JsonObject row)
    {
        JsonObject input = row["input"]!.DeepClone().AsObject();
        JsonObject metadata = input["metadata"]!.AsObject();
        EnvironmentSnapshot snapshot = EventProjectionFixtureSources.Create(metadata, out EnvironmentSetup setup, out _);
        var store = new OperationScopeStore();
        JsonObject scope = input["scope"]!.AsObject();
        LogEventData data = Input(input["event"]!.AsObject());
        IDisposable owner = store.BeginScope(new OperationContextData
        {
            OperationId = scope["operationId"]?.GetValue<string>(),
            Attributes = Attributes(scope["attributes"]?.AsObject())
        });
        bool headUnchanged;
        CapturedEventFacts? captured;
        JsonObject? before;
        try
        {
            OperationScopeFrame? head = store.CurrentFrame;
            captured = EventProjection.Capture(snapshot, store.Capture(), input["component"]!.GetValue<string>(),
                Enum.Parse<CoreLogLevel>(input["severity"]!.GetValue<string>()), data);
            headUnchanged = ReferenceEquals(head, store.CurrentFrame);
            before = Project(captured);
        }
        finally { owner.Dispose(); }
        data.EventName = "changed"; data.Message = "changed"; data.MessageTemplate = "changed";
        data.OperationId = "changed";
        if (data.Attributes is Dictionary<string, LogValue> original) { original.Clear(); original["new"] = LogValue.String("changed"); }
        data.Attributes = new Dictionary<string, LogValue>(); data.Exception = new Exception("changed");
        setup.ApplicationName = "Changed.App"; setup.EnvironmentName = "changed";
        setup.Logging!.FilePath = "changed/private.jsonl"; setup.Logging.QueueCapacity = 1;
        metadata["service"] = "Changed.App";
        using IDisposable replacement = store.BeginScope(new OperationContextData { OperationId = "changed" });
        JsonObject? after = Project(captured);
        return new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["projection"] = after,
            ["scopeHeadUnchanged"] = headUnchanged,
            ["stableAfterMutation"] = JsonNode.DeepEquals(before, after),
            ["callerMutationApplied"] = data.EventName == "changed" && setup.Logging.QueueCapacity == 1
                && metadata["service"]!.GetValue<string>() == "Changed.App" && store.Capture().OperationId == "changed"
        };
    }

    internal static LogEventData Input(JsonObject input) => new()
    {
        EventName = input["eventName"]!.GetValue<string>(),
        Message = input["message"]!.GetValue<string>(),
        MessageTemplate = input["messageTemplate"]?.GetValue<string>(),
        OperationId = input["operationId"]?.GetValue<string>(),
        Attributes = Attributes(input["attributes"]?.AsObject()),
        Exception = input["exception"] is JsonObject error && error["typeTag"]!.GetValue<string>() == "operation"
            ? new InvalidOperationException(error["message"]!.GetValue<string>()) : null
    };

    private static Dictionary<string, LogValue>? Attributes(JsonObject? input) => input?.ToDictionary(pair => pair.Key,
        pair => LogValueFixtureAdapter.Construct(pair.Value!.AsObject(), () => { }), StringComparer.Ordinal);

    internal static JsonObject? Project(CapturedEventFacts? facts)
    {
        if (facts is null) { return null; }
        var result = new JsonObject
        {
            ["schema_version"] = facts.SchemaVersion,
            ["status"] = facts.Status,
            ["event_name"] = facts.EventName,
            ["message"] = facts.Message,
            ["service"] = facts.Service,
            ["env"] = facts.Environment,
            ["version"] = facts.Version,
            ["component"] = facts.Component,
            ["resource"] = LogValueFixtureAdapter.Project(facts.Resource),
            ["attributes"] = LogValueFixtureAdapter.Project(facts.Attributes)
        };
        if (facts.MessageTemplate is not null) { result["message_template"] = facts.MessageTemplate; }
        if (facts.OperationId is not null) { result["operation_id"] = facts.OperationId; }
        if (facts.Exception is { } error) { result["exception"] = Exception(error); }
        if (facts.IsTruncated) { result["truncated"] = true; }
        return result;
    }
    private static JsonObject Exception(CapturedException error)
    {
        var result = new JsonObject { ["type"] = error.Type, ["message"] = error.Message };
        if (error.StackTrace is not null) { result["stack_trace"] = error.StackTrace; }
        if (error.InnerExceptions.Count > 0) { result["inner_exceptions"] = new JsonArray(error.InnerExceptions.Select(child => (JsonNode)Exception(child)).ToArray()); }
        if (error.IsTruncated) { result["truncated"] = true; }
        return result;
    }
}
