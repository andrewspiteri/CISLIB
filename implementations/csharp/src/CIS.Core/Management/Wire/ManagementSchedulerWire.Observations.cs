using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.Messaging;

namespace CIS.Management.Wire;

public static partial class ManagementSchedulerWire
{
    private static void CapturedSettings(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["selection", "payloadJson", "sha256"]);
        Selection(Field(value, "selection"), closed, limits);
        byte[] bytes = Raw(Field(value, "payloadJson"), limits.MaximumPayloadBytes, limits, objectOnly: true);
        Digest(value, "sha256");
        Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == Text(value, "sha256"));
    }
    private static void ExecutionIdentity(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["application", "environment", "issuer", "subject", "bindingId", "profileId"]);
        Scalar(value, "application", 255);
        Scalar(value, "environment", 255);
        Scalar(value, "issuer", 1024);
        Scalar(value, "subject", 1024);
        Scalar(value, "bindingId", 1024);
        Scalar(value, "profileId", 64);
    }
    private static void Actor(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["kind", "issuer", "subject"], "displayName", "application", "environment");
        Token(value, "kind");
        Scalar(value, "issuer", 1024);
        Scalar(value, "subject", 1024);
        Optional(value, "displayName", _ => Bytes(value, "displayName", 256));
        Optional(value, "application", _ => Scalar(value, "application", 255));
        Optional(value, "environment", _ => Scalar(value, "environment", 255));
    }
    private static void Trigger(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["applicationId", "triggerId", "revision", "eTag", "definition", "state", "configuredBy", "changedAtUtc"], "calendar", "waitingReason");
        Id(value, "applicationId");
        Id(value, "triggerId");
        Integer(value, "revision");
        ETag(value, "eTag");
        TriggerDraft(Field(value, "definition"), closed, limits);
        Token(value, "state");
        Actor(Field(value, "configuredBy"), closed, limits);
        Time(value, "changedAtUtc");
        Optional(value, "calendar", item => Calendar(item, closed, limits));
        Optional(value, "waitingReason", _ => Bytes(value, "waitingReason", 128));
    }
    private static void Task(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["applicationId", "taskId", "desiredRevision", "eTag", "definition", "state", "configuredBy", "changedAtUtc"], "activeRevision", "waitingReason");
        Id(value, "applicationId");
        Id(value, "taskId");
        Integer(value, "desiredRevision");
        Optional(value, "activeRevision", _ => Integer(value, "activeRevision"));
        ETag(value, "eTag");
        TaskDraft(Field(value, "definition"), closed, limits);
        Token(value, "state");
        Actor(Field(value, "configuredBy"), closed, limits);
        Time(value, "changedAtUtc");
        Optional(value, "waitingReason", _ => Bytes(value, "waitingReason", 128));
    }
    private static void Preview(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["valid", "issues", "affectedTasks", "impactFullyDisclosed", "nextOccurrencesUtc", "activationState"],
            "draftDigest", "taskETag", "triggerETag", "preconditions", "definition");
        bool valid = Boolean(value, "valid");
        _ = Boolean(value, "impactFullyDisclosed");
        Token(value, "activationState");
        JsonElement issues = Items(value, "issues", 128);
        foreach (JsonElement issue in issues.EnumerateArray())
        {
            Shape(issue, closed, ["path", "code", "message"]);
            Bytes(issue, "path", 1024, empty: true);
            Bytes(issue, "code", 128);
            Bytes(issue, "message", 1024);
        }
        var affected = new HashSet<(string App, string Task)>();
        foreach (JsonElement task in Items(value, "affectedTasks", ManagementSchedulerLimits.MaximumTasks).EnumerateArray())
        {
            Shape(task, closed, ["applicationId", "taskId", "name", "revision"]);
            Id(task, "applicationId");
            Id(task, "taskId");
            Bytes(task, "name", 256);
            Integer(task, "revision");
            Require(affected.Add((Text(task, "applicationId"), Text(task, "taskId"))));
        }
        foreach (JsonElement instant in Items(value, "nextOccurrencesUtc", 100).EnumerateArray())
        {
            Require(ManagementWire.IsTimestamp(Text(instant)));
        }
        Optional(value, "draftDigest", _ => Digest(value, "draftDigest"));
        Optional(value, "taskETag", _ => ETag(value, "taskETag"));
        Optional(value, "triggerETag", _ => ETag(value, "triggerETag"));
        Optional(value, "definition", item => TaskDraft(item, closed, limits));
        Optional(value, "preconditions", items =>
        {
            Require(items.ValueKind == JsonValueKind.Array && items.GetArrayLength() <= 16);
            var selections = new HashSet<ManagementSchedulerSettingsSelection>();
            foreach (JsonElement item in items.EnumerateArray())
            {
                Selection(item, closed, limits);
                Require(selections.Add(item.Deserialize<ManagementSchedulerSettingsSelection>(Json)!));
            }
        });
        if (valid)
        {
            Require(issues.GetArrayLength() == 0);
            int context = new[] { "draftDigest", "definition", "triggerETag", "preconditions" }.Count(name => Has(value, name));
            Require(context is 0 or 4);
            Require(!Has(value, "taskETag") || context == 4);
            Require(context == 0 || IsTaskPreview(value.Deserialize<ManagementSchedulerTaskPreview>(Json)!));
        }
    }
    private static void EventReceipt(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["applicationId", "triggerId", "eventId", "disposition", "acceptedAtUtc"], "runIds", "runCount");
        Id(value, "applicationId");
        Id(value, "triggerId");
        Id(value, "eventId");
        Token(value, "disposition");
        Time(value, "acceptedAtUtc");
        Require(Has(value, "runIds") == Has(value, "runCount"));
        if (Has(value, "runIds"))
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement id in Items(value, "runIds", ManagementSchedulerLimits.MaximumFanout).EnumerateArray())
            {
                Require(ManagementWire.IsUuid(Text(id)) && ids.Add(Text(id)));
            }
            if (Has(value, "runCount"))
            {
                Require(Number(value, "runCount", 0, ManagementSchedulerLimits.MaximumFanout) == ids.Count);
            }
        }
        else
        {
            Optional(value, "runCount", _ => Number(value, "runCount", 0, ManagementSchedulerLimits.MaximumFanout));
        }
    }
    private static void Capsule(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Require(Utf8.GetByteCount(value.GetRawText()) <= limits.MaximumCapsuleBytes, ManagementWireFailure.TooLarge);
        Shape(value, closed, ["runId", "applicationId", "taskId", "taskRevision", "trigger", "eventId", "job", "resourceId",
            "inputEnvelopeJson", "executionSettings", "executionContract", "originalOperation", "jobSettings", "executionIdentity", "configuredBy", "producer"],
            "triggerSettings", "requestedBy", "scheduledAtUtc", "eventOccurredAtUtc", "eventReceivedAtUtc", "correlationId", "causationId");
        foreach (string name in new[] { "runId", "applicationId", "taskId", "eventId" })
        {
            Id(value, name);
        }
        Integer(value, "taskRevision");
        TriggerReference(Field(value, "trigger"), closed, limits);
        CapabilityKey(Field(value, "job"), closed, limits);
        Bytes(value, "resourceId", 512);
        ExecutionSettings(Field(value, "executionSettings"), closed, limits);
        Contract(Field(value, "executionContract"), closed, limits);
        BusinessOperation(Field(value, "originalOperation"), closed, limits);
        string raw = Text(value, "inputEnvelopeJson");
        Require(raw.Length <= limits.MaximumPayloadBytes + MessagingLimits.MaximumMetadataBytes, ManagementWireFailure.TooLarge);
        byte[] encoded = Utf8.GetBytes(raw);
        bool framed = MessageWire.TryDecode(encoded, out EncodedMessage? message, out MessageWireFailure failure,
            new MessagingLimits(limits.MaximumPayloadBytes, limits.MaximumJsonDepth));
        Require(framed, failure == MessageWireFailure.TooLarge ? ManagementWireFailure.TooLarge : ManagementWireFailure.Invalid);
        Require(message!.Envelope.Kind == MessageKind.Command
            && message.Envelope.Operation == Field(value, "originalOperation").Deserialize<MessageOperationReference>(Json));
        CapturedSettings(Field(value, "jobSettings"), closed, limits);
        Optional(value, "triggerSettings", item => CapturedSettings(item, closed, limits));
        ExecutionIdentity(Field(value, "executionIdentity"), closed, limits);
        Actor(Field(value, "configuredBy"), closed, limits);
        Actor(Field(value, "producer"), closed, limits);
        Optional(value, "requestedBy", item => Actor(item, closed, limits));
        Optional(value, "eventOccurredAtUtc", _ => Time(value, "eventOccurredAtUtc"));
        Optional(value, "eventReceivedAtUtc", _ => Time(value, "eventReceivedAtUtc"));
        EventTimes(value);
    }
    private static void Run(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["applicationId", "runId", "taskId", "taskRevision", "trigger", "eventId", "job", "resourceId", "state", "createdAtUtc", "attemptCount"],
            "businessOutcome", "completedAtUtc", "waitingReason", "cancellationRequested", "scheduledAtUtc", "nextAttemptAtUtc", "correlationId", "causationId");
        foreach (string name in new[] { "applicationId", "runId", "taskId", "eventId" })
        {
            Id(value, name);
        }
        Integer(value, "taskRevision");
        TriggerReference(Field(value, "trigger"), closed, limits);
        CapabilityKey(Field(value, "job"), closed, limits);
        Bytes(value, "resourceId", 512);
        Token(value, "state");
        Time(value, "createdAtUtc");
        Integer(value, "attemptCount", zero: true);
        Optional(value, "businessOutcome", _ => Token(value, "businessOutcome"));
        Optional(value, "completedAtUtc", _ => Time(value, "completedAtUtc"));
        Optional(value, "waitingReason", _ => Bytes(value, "waitingReason", 128));
        Optional(value, "cancellationRequested", _ => Boolean(value, "cancellationRequested"));
        Optional(value, "nextAttemptAtUtc", _ => Time(value, "nextAttemptAtUtc"));
        EventTimes(value);
    }
    private static void Attempt(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["runId", "attemptId", "number", "role", "state", "admittedAtUtc", "build", "executor", "registrationId", "effectivePolicy", "sequence"],
            "enteredAtUtc", "settledAtUtc", "businessCompletion", "callbackSettled", "cleanupSucceeded", "summaryJson", "acquisitionId", "durationMs");
        Id(value, "runId");
        Id(value, "attemptId");
        Integer(value, "number");
        Token(value, "role");
        Token(value, "state");
        Time(value, "admittedAtUtc");
        Build(Field(value, "build"), closed, limits);
        ExecutionIdentity(Field(value, "executor"), closed, limits);
        Id(value, "registrationId");
        Scalar(value, "effectivePolicy", 1024);
        Integer(value, "sequence");
        Optional(value, "enteredAtUtc", _ => Time(value, "enteredAtUtc"));
        Optional(value, "settledAtUtc", _ => Time(value, "settledAtUtc"));
        Optional(value, "businessCompletion", _ => Scalar(value, "businessCompletion", 64));
        Optional(value, "callbackSettled", _ => Boolean(value, "callbackSettled"));
        Optional(value, "cleanupSucceeded", _ => Boolean(value, "cleanupSucceeded"));
        Optional(value, "summaryJson", item => Raw(item, limits.MaximumSummaryBytes, limits, objectOnly: true));
        Optional(value, "acquisitionId", _ => Id(value, "acquisitionId"));
        Optional(value, "durationMs", _ => Integer(value, "durationMs", zero: true));
    }
    private static void AttemptAdmission(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["disposition"], "reasonCode", "attempt");
        Token(value, "disposition");
        Optional(value, "reasonCode", _ => Bytes(value, "reasonCode", 128));
        Optional(value, "attempt", item => Attempt(item, closed, limits));
        if (Text(value, "disposition") is "admitted" or "replayed")
        {
            Require(Has(value, "attempt"));
        }
    }
    private static void RunDetail(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["run", "attempts", "capturedContentAccess"], "capsule", "nextAttemptCursor");
        Run(Field(value, "run"), closed, limits);
        string runId = Text(Field(value, "run"), "runId");
        var attempts = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement attempt in Items(value, "attempts", limits.MaximumPageSize).EnumerateArray())
        {
            Attempt(attempt, closed, limits);
            Require(Text(attempt, "runId") == runId && attempts.Add(Text(attempt, "attemptId")));
        }
        Token(value, "capturedContentAccess");
        bool allowed = Text(value, "capturedContentAccess") == "allowed";
        Require(allowed == Has(value, "capsule"));
        Optional(value, "capsule", item =>
        {
            Capsule(item, closed, limits);
            JsonElement run = Field(value, "run");
            foreach (string name in new[] { "applicationId", "runId", "taskId", "taskRevision", "eventId", "resourceId" })
            {
                Require(Text(item, name) == Text(run, name));
            }
            Require(Field(item, "trigger").Deserialize<ManagementSchedulerTriggerReference>(Json)
                == Field(run, "trigger").Deserialize<ManagementSchedulerTriggerReference>(Json));
            Require(Field(item, "job").Deserialize<ManagementCapabilityKey>(Json)
                == Field(run, "job").Deserialize<ManagementCapabilityKey>(Json));
        });
        Optional(value, "nextAttemptCursor", Cursor);
    }
    private static void Event(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["applicationId", "triggerId", "eventId", "occurredAtUtc", "receivedAtUtc", "contract", "producer", "payloadAccess"],
            "payloadJson", "sourceSettings", "scheduledAtUtc", "correlationId", "causationId");
        Id(value, "applicationId");
        Id(value, "triggerId");
        Id(value, "eventId");
        Time(value, "occurredAtUtc");
        Time(value, "receivedAtUtc");
        Contract(Field(value, "contract"), closed, limits);
        Actor(Field(value, "producer"), closed, limits);
        Token(value, "payloadAccess");
        bool allowed = Text(value, "payloadAccess") == "allowed";
        Require(allowed == Has(value, "payloadJson") && (allowed || !Has(value, "sourceSettings")));
        Optional(value, "payloadJson", item => Raw(item, limits.MaximumPayloadBytes, limits));
        Optional(value, "sourceSettings", item => CapturedSettings(item, closed, limits));
        EventTimes(value);
    }
    private static void History(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, [], "taskId", "executionScope", "jobId", "eventId", "resourceId", "fromUtc", "untilUtc", "state",
            "applicationVersion", "buildId", "executionContract", "triggerApplicationId", "triggerId");
        Optional(value, "taskId", _ => Id(value, "taskId"));
        Optional(value, "executionScope", _ => Bytes(value, "executionScope", 256));
        Optional(value, "jobId", _ => Bytes(value, "jobId", 128));
        Optional(value, "eventId", _ => Id(value, "eventId"));
        Optional(value, "triggerApplicationId", _ => Id(value, "triggerApplicationId"));
        Optional(value, "triggerId", _ => Id(value, "triggerId"));
        Optional(value, "resourceId", _ => Bytes(value, "resourceId", 512));
        Optional(value, "fromUtc", _ => Time(value, "fromUtc"));
        Optional(value, "untilUtc", _ => Time(value, "untilUtc"));
        Optional(value, "state", _ => Token(value, "state"));
        Optional(value, "applicationVersion", _ => Bytes(value, "applicationVersion", 512));
        Optional(value, "buildId", _ => Bytes(value, "buildId", 512));
        Optional(value, "executionContract", item => Contract(item, closed, limits));
        bool task = Has(value, "taskId");
        bool job = Has(value, "executionScope") && Has(value, "jobId");
        bool source = Has(value, "eventId") && Has(value, "triggerApplicationId") && Has(value, "triggerId");
        Require((task ? 1 : 0) + (job ? 1 : 0) + (source ? 1 : 0) == 1);
        Require(task ? !Has(value, "executionScope") && !Has(value, "jobId") && !Has(value, "eventId") && !Has(value, "triggerApplicationId") && !Has(value, "triggerId")
            : job ? !Has(value, "taskId") && !Has(value, "eventId") && !Has(value, "triggerApplicationId") && !Has(value, "triggerId")
            : !Has(value, "taskId") && !Has(value, "executionScope") && !Has(value, "jobId"));
        if (Has(value, "fromUtc") && Has(value, "untilUtc"))
        {
            Require(string.CompareOrdinal(Text(value, "fromUtc"), Text(value, "untilUtc")) <= 0);
        }
    }
    private static void ReportReceipt(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["registrationId", "stream", "submittedSequence", "acceptedSequence", "disposition", "receivedAtUtc"]);
        Id(value, "registrationId");
        Token(value, "stream");
        Integer(value, "submittedSequence");
        Integer(value, "acceptedSequence");
        Token(value, "disposition");
        Time(value, "receivedAtUtc");
    }
}
