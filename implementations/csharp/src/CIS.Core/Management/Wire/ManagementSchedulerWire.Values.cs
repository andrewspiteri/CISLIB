using System.Globalization;
using System.Text.Json;
using CIS.Messaging;
using CIS.Operations;

namespace CIS.Management.Wire;

public static partial class ManagementSchedulerWire
{
    private delegate void Validator(JsonElement value, bool closed, ManagementSchedulerWireLimits limits);

    // Closed, explicit DTO dispatch. This table does not read or interpret a schema document.
    private static readonly IReadOnlyDictionary<Type, Validator> Validators = new Dictionary<Type, Validator>
    {
        [typeof(MessageContract)] = Contract,
        [typeof(MessageOperationReference)] = BusinessOperation,
        [typeof(ManagementOperationReference)] = AdministrativeOperation,
        [typeof(ManagementTransactionFacts)] = Transaction,
        [typeof(ManagementOperationRejection)] = Rejection,
        [typeof(ManagementWorkerBuildReference)] = Build,
        [typeof(ManagementJobExecutionSupport)] = ExecutionSupport,
        [typeof(ManagementCapabilityKey)] = CapabilityKey,
        [typeof(ManagementConfigurationShape)] = ShapeDeclaration,
        [typeof(ManagementConfigurationContractReference)] = ConfigurationContract,
        [typeof(ManagementFormDescription)] = Form,
        [typeof(ManagementCapabilityDefinition)] = Capability,
        [typeof(ManagementCapabilityParticipation)] = Participation,
        [typeof(ManagementCapabilityQuery)] = CapabilityQuery,
        [typeof(ManagementAdministrativeAction)] = AdministrativeAction,
        [typeof(ManagementConfigurationActions)] = ConfigurationActions,
        [typeof(ManagementConfigurationCohortChoice)] = Cohort,
        [typeof(ManagementReportReceipt)] = ReportReceipt,
        [typeof(ManagementSchedulerTriggerReference)] = TriggerReference,
        [typeof(ManagementSchedulerSettingsSelection)] = Selection,
        [typeof(ManagementSchedulerCapturedSettings)] = CapturedSettings,
        [typeof(ManagementSchedulerExecutionSettings)] = ExecutionSettings,
        [typeof(ManagementSchedulerExecutionIdentity)] = ExecutionIdentity,
        [typeof(ManagementSchedulerActor)] = Actor,
        [typeof(ManagementSchedulerInputMapping)] = InputMapping,
        [typeof(ManagementSchedulerResourceMapping)] = ResourceMapping,
        [typeof(ManagementSchedulerRetryPolicy)] = Retry,
        [typeof(ManagementSchedulerCalendar)] = Calendar,
        [typeof(ManagementSchedulerTriggerDraft)] = TriggerDraft,
        [typeof(ManagementSchedulerTaskDraft)] = TaskDraft,
        [typeof(ManagementSchedulerTrigger)] = Trigger,
        [typeof(ManagementSchedulerTask)] = Task,
        [typeof(ManagementSchedulerTriggerCommand)] = TriggerCommand,
        [typeof(ManagementSchedulerTaskCommand)] = TaskCommand,
        [typeof(ManagementSchedulerStateCommand)] = StateCommand,
        [typeof(ManagementSchedulerTaskPreviewCommand)] = TaskPreviewCommand,
        [typeof(ManagementSchedulerCalendarPreviewCommand)] = CalendarPreviewCommand,
        [typeof(ManagementSchedulerTaskPreview)] = Preview,
        [typeof(ManagementSchedulerEventCommand)] = EventCommand,
        [typeof(ManagementSchedulerEventReceipt)] = EventReceipt,
        [typeof(ManagementSchedulerManualRunCommand)] = Manual,
        [typeof(ManagementSchedulerRunCapsule)] = Capsule,
        [typeof(ManagementSchedulerRun)] = Run,
        [typeof(ManagementSchedulerAttempt)] = Attempt,
        [typeof(ManagementSchedulerProgress)] = Progress,
        [typeof(ManagementSchedulerAttemptReport)] = AttemptReport,
        [typeof(ManagementSchedulerAttemptCommand)] = AttemptCommand,
        [typeof(ManagementSchedulerAttemptAdmission)] = AttemptAdmission,
        [typeof(ManagementSchedulerRunDetail)] = RunDetail,
        [typeof(ManagementSchedulerEvent)] = Event,
        [typeof(ManagementSchedulerHistoryQuery)] = History,
        [typeof(ManagementSchedulerDecision)] = Decision,
        [typeof(ManagementSchedulerOperationResult)] = OperationResult,
        [typeof(ManagementSchedulerProblem)] = Problem,
        [typeof(ManagementPage<ManagementCapabilityDefinition>)] = PageOf<ManagementCapabilityDefinition>,
        [typeof(ManagementPage<ManagementCapabilityParticipation>)] = PageOf<ManagementCapabilityParticipation>,
        [typeof(ManagementPage<ManagementConfigurationCohortChoice>)] = PageOf<ManagementConfigurationCohortChoice>,
        [typeof(ManagementPage<ManagementSchedulerTrigger>)] = PageOf<ManagementSchedulerTrigger>,
        [typeof(ManagementPage<ManagementSchedulerTask>)] = PageOf<ManagementSchedulerTask>,
        [typeof(ManagementPage<ManagementSchedulerRun>)] = PageOf<ManagementSchedulerRun>,
        [typeof(ManagementPage<ManagementSchedulerAttempt>)] = PageOf<ManagementSchedulerAttempt>
    };

    private static readonly HashSet<Type> Commands =
    [
        typeof(ManagementSchedulerTriggerCommand), typeof(ManagementSchedulerTaskCommand),
        typeof(ManagementSchedulerStateCommand), typeof(ManagementSchedulerTaskPreviewCommand),
        typeof(ManagementSchedulerCalendarPreviewCommand), typeof(ManagementSchedulerEventCommand),
        typeof(ManagementSchedulerManualRunCommand), typeof(ManagementSchedulerAttemptReport), typeof(ManagementSchedulerAttemptCommand)
    ];

    private static readonly HashSet<Type> Fragments =
    [
        typeof(MessageContract), typeof(MessageOperationReference), typeof(ManagementOperationReference),
        typeof(ManagementWorkerBuildReference), typeof(ManagementJobExecutionSupport), typeof(ManagementCapabilityKey),
        typeof(ManagementConfigurationShape), typeof(ManagementConfigurationContractReference), typeof(ManagementFormDescription),
        typeof(ManagementCapabilityDefinition), typeof(ManagementCapabilityQuery), typeof(ManagementSchedulerTriggerReference),
        typeof(ManagementSchedulerSettingsSelection), typeof(ManagementSchedulerExecutionSettings),
        typeof(ManagementSchedulerInputMapping), typeof(ManagementSchedulerResourceMapping),
        typeof(ManagementSchedulerRetryPolicy), typeof(ManagementSchedulerCalendar), typeof(ManagementSchedulerTriggerDraft),
        typeof(ManagementSchedulerTaskDraft), typeof(ManagementSchedulerProgress), typeof(ManagementSchedulerHistoryQuery)
    ];

    private static void Supported(Type type, bool request)
    {
        if (!Validators.ContainsKey(type) || (request ? !Commands.Contains(type) && !Fragments.Contains(type) : Commands.Contains(type)))
        {
            throw new NotSupportedException("Select a fixed declared scheduler/catalog request, response or portable fragment.");
        }
    }

    private static void Value(Type type, JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
        => Validators[type](value, closed, limits);

    private static void Shape(JsonElement value, bool closed, string[] required, params string[] optional)
    {
        Require(value.ValueKind == JsonValueKind.Object);
        foreach (string name in required)
        {
            Require(value.TryGetProperty(name, out JsonElement field) && field.ValueKind != JsonValueKind.Null);
        }
        foreach (JsonProperty property in value.EnumerateObject())
        {
            bool known = required.Contains(property.Name, StringComparer.Ordinal) || optional.Contains(property.Name, StringComparer.Ordinal);
            Require(!closed || known);
            Require(!known || property.Value.ValueKind != JsonValueKind.Null);
        }
    }

    private static JsonElement Field(JsonElement value, string name)
    {
        Require(value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out _));
        return value.GetProperty(name);
    }
    private static string Text(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.String);
        return value.GetString()!;
    }
    private static string Text(JsonElement value, string name) => Text(Field(value, name));
    private static bool Has(JsonElement value, string name) => value.TryGetProperty(name, out _);
    private static void Optional(JsonElement value, string name, Action<JsonElement> action)
    {
        if (value.TryGetProperty(name, out JsonElement item))
        {
            action(item);
        }
    }
    private static void Bytes(JsonElement value, string name, int maximum, bool empty = false)
        => Require(ManagementWire.IsUtf8Text(Text(value, name), maximum, empty));
    private static void Scalar(JsonElement value, string name, int maximum, bool empty = false)
        => Require(ManagementWire.IsScalarText(Text(value, name), maximum, empty));
    private static void Id(JsonElement value, string name) => Require(ManagementWire.IsUuid(Text(value, name)));
    private static void Integer(JsonElement value, string name, bool zero = false)
        => Require(ManagementWire.IsOrderedInteger(Text(value, name), zero));
    private static void Time(JsonElement value, string name) => Require(ManagementWire.IsTimestamp(Text(value, name)));
    private static void ETag(JsonElement value, string name) => Require(ManagementWire.IsStrongETag(Text(value, name)));
    private static void Portable(JsonElement value, string name) => Require(ManagementWire.IsOperationKey(Text(value, name)));
    private static void Digest(JsonElement value, string name) => Require(ManagementConfigurationWire.IsSha256(Text(value, name)));
    private static void Token(JsonElement value, string name)
    {
        string text = Text(value, name);
        Require(text.Length is >= 1 and <= 128 && text.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'));
    }
    private static bool Boolean(JsonElement value, string name)
    {
        JsonElement field = Field(value, name);
        Require(field.ValueKind is JsonValueKind.True or JsonValueKind.False);
        return field.GetBoolean();
    }
    private static int Number(JsonElement value, string name, int minimum, int maximum)
    {
        JsonElement field = Field(value, name);
        Require(field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out _));
        int number = field.GetInt32();
        Require(number >= minimum && number <= maximum);
        return number;
    }
    private static void Version(JsonElement value)
    {
        JsonElement version = Field(value, "commandVersion");
        Require(version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int number) && number == 1,
            ManagementWireFailure.UnsupportedVersion);
    }
    private static JsonElement Items(JsonElement value, string name, int maximum, int minimum = 0)
    {
        JsonElement items = Field(value, name);
        Require(items.ValueKind == JsonValueKind.Array && items.GetArrayLength() >= minimum && items.GetArrayLength() <= maximum);
        return items;
    }
    private static long Milliseconds(JsonElement value, string name, long maximum = 4233600000, bool zero = false)
    {
        Integer(value, name, zero);
        long number = long.Parse(Text(value, name), CultureInfo.InvariantCulture);
        Require(number <= maximum);
        return number;
    }
    private static void Pointer(JsonElement value, string name)
    {
        Bytes(value, name, 1024);
        string path = Text(value, name);
        Require(path.StartsWith('/'));
        for (int index = 0; index < path.Length; index++)
        {
            if (path[index] == '~')
            {
                Require(++index < path.Length && path[index] is '0' or '1');
            }
        }
    }

    private static byte[] Raw(JsonElement value, int maximum, ManagementSchedulerWireLimits limits, bool objectOnly = false)
    {
        string text = Text(value);
        Require(text.Length <= maximum, ManagementWireFailure.TooLarge);
        byte[] bytes = Utf8.GetBytes(text);
        Require(bytes.Length <= maximum, ManagementWireFailure.TooLarge);
        Require(MessageWire.ValidateJson(bytes, limits.MaximumJsonDepth));
        if (objectOnly)
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            Require(document.RootElement.ValueKind == JsonValueKind.Object);
        }
        return bytes;
    }

    private static void Contract(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["name", "version"]);
        _ = new MessageContract(Text(value, "name"), Number(value, "version", 1, int.MaxValue));
    }
    private static void BusinessOperation(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["operationName", "requestedScope", "key"]);
        _ = new MessageOperationReference(Text(value, "operationName"), Text(value, "requestedScope"), Text(value, "key"));
    }
    private static void AdministrativeOperation(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["name", "scope", "key"]);
        OperationText.Alias(Text(value, "name"), "name");
        OperationText.Utf8(Text(value, "scope"), 256, "scope");
        Portable(value, "key");
    }
    private static void Build(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["applicationVersion", "buildId", "handlerId"], "sourceRevision");
        foreach (string name in new[] { "applicationVersion", "buildId", "handlerId" })
        {
            Bytes(value, name, 512);
        }
        Optional(value, "sourceRevision", _ => Bytes(value, "sourceRevision", 128));
    }
    private static void ExecutionSupport(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["currentContract", "supportedStarts", "supportedRecoveries"], "checkpointContract", "operationName");
        Contract(Field(value, "currentContract"), closed, limits);
        var current = Field(value, "currentContract").Deserialize<MessageContract>(Json)!;
        var starts = new HashSet<MessageContract>();
        foreach (JsonElement item in Items(value, "supportedStarts", 16, 1).EnumerateArray())
        {
            Contract(item, closed, limits);
            Require(starts.Add(item.Deserialize<MessageContract>(Json)!));
        }
        Require(starts.Contains(current));
        var recoveries = new HashSet<MessageContract>();
        foreach (JsonElement item in Items(value, "supportedRecoveries", 16).EnumerateArray())
        {
            Contract(item, closed, limits);
            Require(recoveries.Add(item.Deserialize<MessageContract>(Json)!));
        }
        Optional(value, "checkpointContract", item => Contract(item, closed, limits));
        Optional(value, "operationName", _ => OperationText.Alias(Text(value, "operationName"), "operationName"));
    }
    private static void PageOf<T>(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["items"], "nextCursor");
        foreach (JsonElement item in Items(value, "items", limits.MaximumPageSize).EnumerateArray())
        {
            Value(typeof(T), item, closed, limits);
        }
        Optional(value, "nextCursor", Cursor);
    }
    private static void Cursor(JsonElement value)
    {
        string text = Text(value);
        Require(text.Length is >= 1 and <= 2048 && text.All(character => character is >= '!' and <= '~'));
    }
}
