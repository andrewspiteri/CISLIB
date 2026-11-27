using System.Globalization;
using System.Text.Json;

namespace CIS.Management.Wire;

public static partial class ManagementSchedulerWire
{
    private static void TriggerReference(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["applicationId", "triggerId"]);
        Id(value, "applicationId");
        Id(value, "triggerId");
    }
    private static void Selection(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["applicationId", "configurationId", "schemaVersion", "releaseId", "assignmentGeneration"]);
        Id(value, "applicationId");
        Portable(value, "configurationId");
        Integer(value, "schemaVersion");
        Id(value, "releaseId");
        Integer(value, "assignmentGeneration");
    }
    private static void ExecutionSettings(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["idleDelayTicks", "maximumDrainTicks", "acquisitionRetryDelayTicks"], "timeoutTicks");
        foreach (string name in new[] { "idleDelayTicks", "maximumDrainTicks", "acquisitionRetryDelayTicks" })
        {
            Integer(value, name);
        }
        Optional(value, "timeoutTicks", _ => Integer(value, "timeoutTicks"));
        _ = value.Deserialize<ManagementSchedulerExecutionSettings>(Json)!.ToRuntime();
    }
    private static void InputMapping(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["targetPath", "kind"], "sourcePath", "constantJson", "transformation", "parametersJson");
        Pointer(value, "targetPath");
        Token(value, "kind");
        Optional(value, "sourcePath", _ => Pointer(value, "sourcePath"));
        Optional(value, "constantJson", item => Raw(item, limits.MaximumPayloadBytes, limits));
        Optional(value, "transformation", item => Contract(item, closed, limits));
        Optional(value, "parametersJson", item => Raw(item, limits.MaximumPayloadBytes, limits, objectOnly: true));
        switch (Text(value, "kind"))
        {
            case "field":
                Require(Has(value, "sourcePath") && !Has(value, "constantJson") && !Has(value, "transformation") && !Has(value, "parametersJson"));
                break;
            case "constant":
                Require(Has(value, "constantJson") && !Has(value, "sourcePath") && !Has(value, "transformation") && !Has(value, "parametersJson"));
                break;
            case "transform":
                Require(Has(value, "sourcePath") && Has(value, "transformation") && !Has(value, "constantJson"));
                break;
            default:
                Require(!closed);
                break;
        }
    }
    private static void ResourceMapping(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["kind"], "constant", "eventPath", "resolver", "parametersJson");
        Token(value, "kind");
        Optional(value, "constant", _ => Bytes(value, "constant", 512));
        Optional(value, "eventPath", _ => Pointer(value, "eventPath"));
        Optional(value, "resolver", item => Contract(item, closed, limits));
        Optional(value, "parametersJson", item => Raw(item, limits.MaximumPayloadBytes, limits, objectOnly: true));
        switch (Text(value, "kind"))
        {
            case "constant":
                Require(Has(value, "constant") && !Has(value, "eventPath") && !Has(value, "resolver") && !Has(value, "parametersJson"));
                break;
            case "field":
                Require(Has(value, "eventPath") && !Has(value, "constant") && !Has(value, "resolver") && !Has(value, "parametersJson"));
                break;
            case "resolver":
                Require(Has(value, "resolver") && !Has(value, "constant"));
                break;
            default:
                Require(!closed);
                break;
        }
    }
    private static void Retry(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["enabled", "maximumAttempts", "initialDelayMs", "maximumDelayMs"]);
        bool enabled = Boolean(value, "enabled");
        int attempts = Number(value, "maximumAttempts", 1, ManagementSchedulerLimits.MaximumAttempts);
        long initial = Milliseconds(value, "initialDelayMs", 60000);
        long maximum = Milliseconds(value, "maximumDelayMs", 60000);
        Require(initial >= 5000 && initial <= maximum && (enabled || attempts == 1));
    }
    private static void Calendar(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["form", "missedRunPolicy"], "delayMs", "localTime", "timeZoneId", "weekdays",
            "maximumCatchUp", "catchUpWindowMs", "initialDelayMs", "runImmediately", "failurePolicy");
        Token(value, "form");
        Token(value, "missedRunPolicy");
        string form = Text(value, "form");
        string policy = Text(value, "missedRunPolicy");
        Optional(value, "delayMs", _ => CalendarMilliseconds(value, "delayMs"));
        Optional(value, "catchUpWindowMs", _ => CalendarMilliseconds(value, "catchUpWindowMs"));
        Optional(value, "initialDelayMs", _ => CalendarMilliseconds(value, "initialDelayMs", zero: true));
        Optional(value, "maximumCatchUp", _ => Number(value, "maximumCatchUp", 1, 10000));
        Optional(value, "runImmediately", _ => Boolean(value, "runImmediately"));
        Optional(value, "failurePolicy", _ => Token(value, "failurePolicy"));
        Optional(value, "localTime", item =>
        {
            string text = Text(item);
            Require(text.Length == 16 && TimeOnly.TryParseExact(text, "HH:mm:ss.fffffff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        });
        Optional(value, "timeZoneId", _ => Bytes(value, "timeZoneId", 128));
        if (Has(value, "weekdays"))
        {
            var weekdays = new HashSet<int>();
            foreach (JsonElement day in Items(value, "weekdays", 7, 1).EnumerateArray())
            {
                Require(day.ValueKind == JsonValueKind.Number && day.TryGetInt32(out int number) && number is >= 0 and <= 6);
                Require(weekdays.Add(day.GetInt32()));
            }
        }
        if (closed)
        {
            Require(form is "fixedDelay" or "daily" or "weekly");
            Require(policy is "skip" or "runLatestOnce" or "catchUpBounded");
            Require(!Has(value, "failurePolicy") || Text(value, "failurePolicy") is "pause" or "continue");
        }
        if (form == "fixedDelay")
        {
            Require(Has(value, "delayMs") && !Has(value, "localTime") && !Has(value, "timeZoneId") && !Has(value, "weekdays")
                && !Has(value, "maximumCatchUp") && !Has(value, "catchUpWindowMs"));
            Require(!Has(value, "initialDelayMs") || (Text(value, "initialDelayMs") == "0")
                == (Has(value, "runImmediately") && Boolean(value, "runImmediately")));
        }
        else if (form is "daily" or "weekly")
        {
            Require(Has(value, "localTime") && Has(value, "timeZoneId") && !Has(value, "delayMs")
                && !Has(value, "initialDelayMs") && (!Has(value, "failurePolicy") || Text(value, "failurePolicy") == "pause")
                && (!Has(value, "runImmediately") || !Boolean(value, "runImmediately")));
            Require((form == "weekly") == Has(value, "weekdays"));
            string zone = Text(value, "timeZoneId");
            Require(zone == "UTC" || zone.Contains('/') && !zone.Split('/').Any(part => part is "" or "." or ".."));
            Require(zone.All(character => char.IsAsciiLetterOrDigit(character) || character is '/' or '_' or '-' or '+'));
            Require((policy == "catchUpBounded") == (Has(value, "maximumCatchUp") && Has(value, "catchUpWindowMs")));
            Require(policy == "catchUpBounded" || !Has(value, "maximumCatchUp") && !Has(value, "catchUpWindowMs"));
        }
    }
    private static decimal CalendarMilliseconds(JsonElement value, string name, bool zero = false)
    {
        string text = Text(value, name);
        string[] parts = text.Split('.');
        Require(parts.Length is 1 or 2 && ManagementWire.IsOrderedInteger(parts[0], allowZero: true));
        if (parts.Length == 2)
        {
            Require(parts[1].Length is >= 1 and <= 4 && parts[1][^1] != '0'
                && parts[1].All(character => character is >= '0' and <= '9'));
        }
        Require(decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal milliseconds)
            && milliseconds >= (zero ? 0 : 0.0001m) && milliseconds <= 4233600000m);
        return milliseconds;
    }
    private static void TriggerDraft(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["name", "provider"], "configuration", "enabled");
        Bytes(value, "name", 256);
        CapabilityKey(Field(value, "provider"), closed, limits);
        Require(!closed || Text(Field(value, "provider"), "kind") == "triggerProvider");
        Optional(value, "configuration", item => Selection(item, closed, limits));
        Optional(value, "enabled", _ => Boolean(value, "enabled"));
    }
    private static void TaskDraft(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["name", "trigger", "job", "resource", "inputMappings", "jobConfiguration"],
            "triggerConfiguration", "executionProfileId", "retry", "enabled", "executionSettings");
        Bytes(value, "name", 256);
        TriggerReference(Field(value, "trigger"), closed, limits);
        CapabilityKey(Field(value, "job"), closed, limits);
        Require(!closed || Text(Field(value, "job"), "kind") == "job");
        ResourceMapping(Field(value, "resource"), closed, limits);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement mapping in Items(value, "inputMappings", ManagementSchedulerLimits.MaximumMappings).EnumerateArray())
        {
            InputMapping(mapping, closed, limits);
            Require(targets.Add(Text(mapping, "targetPath")));
        }
        Selection(Field(value, "jobConfiguration"), closed, limits);
        Optional(value, "triggerConfiguration", item => Selection(item, closed, limits));
        Optional(value, "executionProfileId", _ => Scalar(value, "executionProfileId", 64));
        Optional(value, "retry", item => Retry(item, closed, limits));
        Optional(value, "enabled", _ => Boolean(value, "enabled"));
        Optional(value, "executionSettings", item => ExecutionSettings(item, closed, limits));
    }
    private static void TriggerCommand(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "triggerId", "definition", "reason"]);
        Version(value);
        Id(value, "triggerId");
        TriggerDraft(Field(value, "definition"), true, limits);
        Bytes(value, "reason", 1024);
    }
    private static void TaskCommand(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "taskId", "definition", "reason"], "expectedTriggerETag", "previewDigest");
        Version(value);
        Id(value, "taskId");
        TaskDraft(Field(value, "definition"), true, limits);
        Bytes(value, "reason", 1024);
        Optional(value, "expectedTriggerETag", _ => ETag(value, "expectedTriggerETag"));
        Optional(value, "previewDigest", _ => Digest(value, "previewDigest"));
    }
    private static void StateCommand(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "intent", "reason"]);
        Version(value);
        Require(IsStateIntent(Text(value, "intent"), ManagementSchedulerStateTarget.Task)
            || IsStateIntent(Text(value, "intent"), ManagementSchedulerStateTarget.Run));
        Bytes(value, "reason", 1024);
    }
    private static void TaskPreviewCommand(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "definition"], "candidateJobPayloadJson", "candidateTriggerPayloadJson", "taskId");
        Version(value);
        TaskDraft(Field(value, "definition"), true, limits);
        Optional(value, "candidateJobPayloadJson", item => Raw(item, limits.MaximumPayloadBytes, limits, objectOnly: true));
        Optional(value, "candidateTriggerPayloadJson", item => Raw(item, limits.MaximumPayloadBytes, limits, objectOnly: true));
        Optional(value, "taskId", _ => Id(value, "taskId"));
    }
    private static void CalendarPreviewCommand(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "calendar", "fromUtc"], "count");
        Version(value);
        Calendar(Field(value, "calendar"), true, limits);
        Time(value, "fromUtc");
        Optional(value, "count", _ => Number(value, "count", 1, 100));
    }
    private static void EventCommand(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "eventId", "occurredAtUtc", "contract", "payloadJson"], "scheduledAtUtc", "correlationId", "causationId");
        Version(value);
        Id(value, "eventId");
        Time(value, "occurredAtUtc");
        Contract(Field(value, "contract"), true, limits);
        Raw(Field(value, "payloadJson"), limits.MaximumPayloadBytes, limits);
        EventTimes(value);
    }
    private static void Manual(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "eventId", "reason"]);
        Version(value);
        Id(value, "eventId");
        Bytes(value, "reason", 1024);
    }
    private static void Progress(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, [], "completed", "total", "message");
        Optional(value, "completed", _ => Integer(value, "completed", zero: true));
        Optional(value, "total", _ => Integer(value, "total", zero: true));
        Optional(value, "message", _ => Bytes(value, "message", 1024));
        Require(!Has(value, "completed") || !Has(value, "total")
            || long.Parse(Text(value, "completed"), CultureInfo.InvariantCulture) <= long.Parse(Text(value, "total"), CultureInfo.InvariantCulture));
    }
    private static void AttemptReport(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "sequence", "observedAtUtc", "phase"], "businessOutcome", "businessCompletion",
            "callbackSettled", "cleanupSucceeded", "progress", "summaryJson", "durationMs");
        Version(value);
        Integer(value, "sequence");
        Time(value, "observedAtUtc");
        Token(value, "phase");
        Require(Text(value, "phase") is "entered" or "progress" or "settled" or "interrupted");
        Optional(value, "businessOutcome", _ => Token(value, "businessOutcome"));
        Optional(value, "businessCompletion", _ => Scalar(value, "businessCompletion", 64));
        Optional(value, "callbackSettled", _ => Boolean(value, "callbackSettled"));
        Optional(value, "cleanupSucceeded", _ => Boolean(value, "cleanupSucceeded"));
        Optional(value, "progress", item => Progress(item, true, limits));
        Optional(value, "summaryJson", item => Raw(item, limits.MaximumSummaryBytes, limits, objectOnly: true));
        Optional(value, "durationMs", _ => Integer(value, "durationMs", zero: true));
    }
    private static void AttemptCommand(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "attemptId", "registrationId", "role", "executionContract", "build", "ownershipGeneration"], "acquisitionId", "effectivePolicy");
        Version(value);
        Id(value, "attemptId");
        Id(value, "registrationId");
        Require(Text(value, "role") is "execution" or "recovery");
        Contract(Field(value, "executionContract"), true, limits);
        Build(Field(value, "build"), true, limits);
        Integer(value, "ownershipGeneration");
        Optional(value, "acquisitionId", _ => Id(value, "acquisitionId"));
        Optional(value, "effectivePolicy", _ => Scalar(value, "effectivePolicy", 1024));
    }
    private static void EventTimes(JsonElement value)
    {
        Optional(value, "scheduledAtUtc", _ => Time(value, "scheduledAtUtc"));
        Optional(value, "correlationId", _ => Scalar(value, "correlationId", 128));
        Optional(value, "causationId", _ => Id(value, "causationId"));
    }
}
