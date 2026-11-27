using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Management;
using CIS.Management.Wire;
using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementSchedulerWireTests
{
    public static IEnumerable<object[]> IndependentCases()
    {
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllBytes(CatalogPath()));
        foreach (JsonElement row in catalog.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [row.GetProperty("id").GetString()!, row.GetProperty("schema").GetString()!,
                row.GetProperty("direction").GetString()!, row.GetProperty("json").GetString()!, row.GetProperty("valid").GetBoolean()];
        }
    }

    [TestMethod]
    [DynamicData(nameof(IndependentCases))]
    public void IndependentlyAuthoredPortableCasesExerciseTheActualCodec(string id, string schema,
        string direction, string json, bool valid)
    {
        byte[] bytes = Bytes(json);
        object? value = (schema, direction) switch
        {
            ("CapabilityDefinition", "response") => Response<ManagementCapabilityDefinition>(bytes),
            ("CapabilityParticipation", "response") => Response<ManagementCapabilityParticipation>(bytes),
            ("CommandCapabilityDefinition", "request") => Request<ManagementCapabilityDefinition>(bytes),
            ("CommandJobExecutionSupport", "request") => Request<ManagementJobExecutionSupport>(bytes),
            ("CommandMessageContract", "request") => Request<MessageContract>(bytes),
            ("CommandMessageOperationReference", "request") => Request<MessageOperationReference>(bytes),
            ("CommandSchedulerCalendar", "request") => Request<ManagementSchedulerCalendar>(bytes),
            ("CommandSchedulerExecutionSettings", "request") => Request<ManagementSchedulerExecutionSettings>(bytes),
            ("CommandSchedulerInputMapping", "request") => Request<ManagementSchedulerInputMapping>(bytes),
            ("CommandSchedulerResourceMapping", "request") => Request<ManagementSchedulerResourceMapping>(bytes),
            ("CommandSchedulerTaskDraft", "request") => Request<ManagementSchedulerTaskDraft>(bytes),
            ("CommandWorkerBuildReference", "request") => Request<ManagementWorkerBuildReference>(bytes),
            ("JobExecutionSupport", "response") => Response<ManagementJobExecutionSupport>(bytes),
            ("MessageContract", "response") => Response<MessageContract>(bytes),
            ("MessageOperationReference", "response") => Response<MessageOperationReference>(bytes),
            ("OperationReference", "response") => Response<ManagementOperationReference>(bytes),
            ("OperationRejection", "response") => Response<ManagementOperationRejection>(bytes),
            ("SchedulerAttempt", "response") => Response<ManagementSchedulerAttempt>(bytes),
            ("SchedulerAttemptCommand", "request") => Request<ManagementSchedulerAttemptCommand>(bytes),
            ("SchedulerAttemptReport", "request") => Request<ManagementSchedulerAttemptReport>(bytes),
            ("SchedulerCalendar", "response") => Response<ManagementSchedulerCalendar>(bytes),
            ("SchedulerCapturedSettings", "response") => Response<ManagementSchedulerCapturedSettings>(bytes),
            ("SchedulerEventCommand", "request") => Request<ManagementSchedulerEventCommand>(bytes),
            ("SchedulerEventReceipt", "response") => Response<ManagementSchedulerEventReceipt>(bytes),
            ("SchedulerHistoryQuery", "request") => Request<ManagementSchedulerHistoryQuery>(bytes),
            ("SchedulerOperationResult", "response") => Response<ManagementSchedulerOperationResult>(bytes),
            ("SchedulerRun", "response") => Response<ManagementSchedulerRun>(bytes),
            ("SchedulerRunCancellationCommand", "request") => ManagementSchedulerWire.DecodeStateCommand(bytes, ManagementSchedulerStateTarget.Run),
            ("SchedulerRunCapsule", "response") => Response<ManagementSchedulerRunCapsule>(bytes),
            ("SchedulerRunDetail", "response") => Response<ManagementSchedulerRunDetail>(bytes),
            ("SchedulerTaskCommand", "request") => Request<ManagementSchedulerTaskCommand>(bytes),
            ("SchedulerTaskPreview", "response") => Response<ManagementSchedulerTaskPreview>(bytes),
            ("SchedulerTaskPreviewCommand", "request") => Request<ManagementSchedulerTaskPreviewCommand>(bytes),
            ("SchedulerTaskStateCommand", "request") => ManagementSchedulerWire.DecodeStateCommand(bytes, ManagementSchedulerStateTarget.Task),
            ("SchedulerTriggerStateCommand", "request") => ManagementSchedulerWire.DecodeStateCommand(bytes, ManagementSchedulerStateTarget.Trigger),
            ("TransactionFacts", "response") => Response<ManagementTransactionFacts>(bytes),
            ("WorkerBuildReference", "response") => Response<ManagementWorkerBuildReference>(bytes),
            _ => throw new AssertFailedException("Select an explicit declared codec branch for " + schema)
        };

        Assert.AreEqual(valid, value is not null, id);
    }

    [TestMethod]
    public void FramingRejectsDuplicateDecodedNamesMalformedUtf8TrailingTokensAndDepth()
    {
        foreach (byte[] bytes in new[]
        {
            Bytes("{\"name\":\"demo.input\",\"version\":1,\"vers\\u0069on\":2}"),
            Bytes("{\"name\":\"demo.input\",\"version\":1} {}"),
            Bytes("{\"name\":\"\\uD800\",\"version\":1}"),
            new byte[] { 0xef, 0xbb, 0xbf, (byte)'{', (byte)'}' },
            new byte[] { (byte)'{', (byte)'"', 0xff, (byte)'"', (byte)':', (byte)'1', (byte)'}' }
        })
        {
            Assert.IsNull(Request<MessageContract>(bytes));
        }
        string nested = new string('[', 17) + "0" + new string(']', 17);
        JsonObject command = Node("scheduler-event-command-retains-correlation-scalars");
        command["payloadJson"] = nested;
        Assert.IsNull(Request<ManagementSchedulerEventCommand>(Bytes(command.ToJsonString())));
    }

    [TestMethod]
    public void NestedCommandsAreClosedButFutureResponseValuesRemainExplicit()
    {
        JsonObject command = Node("scheduler-task-command-passive-exact-settings");
        command["definition"]!["job"]!["futureAuthority"] = true;
        Assert.IsNull(Request<ManagementSchedulerTaskCommand>(Bytes(command.ToJsonString())));

        ManagementCapabilityDefinition? definition = Response<ManagementCapabilityDefinition>(
            Bytes(Case("scheduler-capability-future-kind-is-inert-response-data")));
        Assert.IsNotNull(definition);
        Assert.IsFalse(definition.IsSupported);
        ManagementSchedulerRun? run = Response<ManagementSchedulerRun>(
            Bytes(Case("scheduler-unknown-response-state-never-defaults-success")));
        Assert.IsNotNull(run);
        Assert.IsFalse(run.KnownState.IsKnown);
        Assert.AreEqual("9007199254740993", Response<ManagementSchedulerRun>(
            Bytes(Case("scheduler-run-attempt-counter-is-lossless")))!.AttemptCount);

        JsonObject build = Node("scheduler-worker-build-actual-registration-fields");
        build["futureObservation"] = new JsonObject { ["supported"] = false };
        Assert.IsNotNull(Response<ManagementWorkerBuildReference>(Bytes(build.ToJsonString())));
        Assert.IsNull(Request<ManagementWorkerBuildReference>(Bytes(build.ToJsonString())));
    }

    [TestMethod]
    public void PayloadAndCompleteNestedCapsuleLimitsApplyToUtf8BytesIndependently()
    {
        JsonObject command = Node("scheduler-event-command-retains-correlation-scalars");
        command["payloadJson"] = "{\"value\":\"" + new string('é', 30) + "\"}";
        Assert.IsNull(ManagementSchedulerWire.DecodeRequest<ManagementSchedulerEventCommand>(Bytes(command.ToJsonString()),
            out ManagementWireFailure payloadFailure, new ManagementSchedulerWireLimits(maximumPayloadBytes: 64)));
        Assert.AreEqual(ManagementWireFailure.TooLarge, payloadFailure);

        JsonObject capsule = Node("scheduler-capsule-existing-command-and-original-business-key");
        Assert.IsNotNull(Response<ManagementSchedulerRunCapsule>(Bytes(capsule.ToJsonString())));
        Assert.IsNull(ManagementSchedulerWire.DecodeResponse<ManagementSchedulerRunCapsule>(Bytes(capsule.ToJsonString()),
            out ManagementWireFailure capsuleFailure, new ManagementSchedulerWireLimits(maximumCapsuleBytes: 64)));
        Assert.AreEqual(ManagementWireFailure.TooLarge, capsuleFailure);

        JsonObject detail = Node("scheduler-run-detail-attempt-page-has-continuation");
        detail["capturedContentAccess"] = "allowed";
        detail["capsule"] = capsule;
        Assert.IsNull(ManagementSchedulerWire.DecodeResponse<ManagementSchedulerRunDetail>(Bytes(detail.ToJsonString()),
            out ManagementWireFailure nestedFailure, new ManagementSchedulerWireLimits(maximumCapsuleBytes: 64)));
        Assert.AreEqual(ManagementWireFailure.TooLarge, nestedFailure);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ManagementSchedulerWireLimits(maximumBodyBytes: 1048577));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ManagementSchedulerWireLimits(maximumPayloadBytes: 65537));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ManagementSchedulerWireLimits(maximumJsonDepth: 17));
    }

    [TestMethod]
    public void CapsuleRetainsNativeCommandFramingAndExactOriginalBusinessOperation()
    {
        JsonObject capsule = Node("scheduler-capsule-existing-command-and-original-business-key");
        ManagementSchedulerRunCapsule? decoded = Response<ManagementSchedulerRunCapsule>(Bytes(capsule.ToJsonString()));
        Assert.IsNotNull(decoded);
        Assert.AreEqual(decoded.InputEnvelopeJson, Response<ManagementSchedulerRunCapsule>(
            ManagementSchedulerWire.EncodeResponse(decoded))!.InputEnvelopeJson);

        capsule["originalOperation"]!["key"] = "another-original-key";
        Assert.IsNull(Response<ManagementSchedulerRunCapsule>(Bytes(capsule.ToJsonString())));
        capsule = Node("scheduler-capsule-existing-command-and-original-business-key");
        capsule["inputEnvelopeJson"] = "{}";
        Assert.IsNull(Response<ManagementSchedulerRunCapsule>(Bytes(capsule.ToJsonString())));

        capsule = Node("scheduler-capsule-existing-command-and-original-business-key");
        capsule["jobSettings"]!["payloadJson"] = "{\"changed\":true}";
        Assert.IsNull(Response<ManagementSchedulerRunCapsule>(Bytes(capsule.ToJsonString())));

        JsonObject detail = Node("scheduler-run-detail-attempt-page-has-continuation");
        detail["capturedContentAccess"] = "allowed";
        detail["capsule"] = Node("scheduler-capsule-existing-command-and-original-business-key");
        Assert.IsNotNull(Response<ManagementSchedulerRunDetail>(Bytes(detail.ToJsonString())));
        detail["capsule"]!["resourceId"] = "another-resource";
        Assert.IsNull(Response<ManagementSchedulerRunDetail>(Bytes(detail.ToJsonString())));
    }

    [TestMethod]
    public void ProtectedContentCannotTravelUnderUnknownOrRedactedAccess()
    {
        JsonObject detail = Node("scheduler-run-detail-attempt-page-has-continuation");
        detail["capturedContentAccess"] = "futureAccess";
        Assert.IsNotNull(Response<ManagementSchedulerRunDetail>(Bytes(detail.ToJsonString())));
        detail["capsule"] = Node("scheduler-capsule-existing-command-and-original-business-key");
        Assert.IsNull(Response<ManagementSchedulerRunDetail>(Bytes(detail.ToJsonString())));

        detail.Remove("capsule");
        detail["capturedContentAccess"] = "allowed";
        Assert.IsNull(Response<ManagementSchedulerRunDetail>(Bytes(detail.ToJsonString())));
    }

    [TestMethod]
    public void RoutePreviewAndStateChecksDoNotTurnCalendarDataIntoTaskAuthority()
    {
        var calendar = new ManagementSchedulerTaskPreview(true, [], [], true,
            ["2026-11-25T01:00:00.0000000Z"], "previewOnly");
        Assert.IsTrue(ManagementSchedulerWire.IsCalendarPreview(calendar, 1));
        Assert.IsFalse(ManagementSchedulerWire.IsTaskPreview(calendar));
        Assert.IsFalse(ManagementSchedulerWire.IsCalendarPreview(calendar, 0));
        ManagementSchedulerTaskDraft definition = Request<ManagementSchedulerTaskCommand>(
            Bytes(Case("scheduler-task-command-passive-exact-settings")))!.Definition;
        var task = new ManagementSchedulerTaskPreview(true, [], [], true, calendar.NextOccurrencesUtc, "previewOnly",
            new string('a', 64), TriggerETag: "\"trigger-v1\"", Preconditions: [definition.JobConfiguration], Definition: definition);
        Assert.IsTrue(ManagementSchedulerWire.IsTaskPreview(task));
        Assert.IsFalse(ManagementSchedulerWire.IsTaskPreview(task, existingTask: true));
        Assert.IsTrue(ManagementSchedulerWire.IsTaskPreview(task with
        {
            TaskETag = "\"task-v1\""
        }, existingTask: true));
        Assert.IsFalse(ManagementSchedulerWire.IsCalendarPreview(task));
        var missingSelection = new ManagementSchedulerTaskPreview(true, [], [], true, [], "previewOnly",
            new string('a', 64), TriggerETag: "\"trigger-v1\"", Preconditions: [], Definition: definition);
        Assert.IsFalse(ManagementSchedulerWire.IsTaskPreview(missingSelection));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementSchedulerWire.EncodeResponse(missingSelection));
        Assert.IsNotNull(Response<ManagementSchedulerTaskPreview>(ManagementSchedulerWire.EncodeResponse(calendar)));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementSchedulerWire.EncodeResponse(calendar with { DraftDigest = new string('a', 64) }));

        byte[] activate = ManagementSchedulerWire.EncodeRequest(new ManagementSchedulerStateCommand(1, "activate", "Activate the task."));
        Assert.IsNotNull(ManagementSchedulerWire.DecodeStateCommand(activate, ManagementSchedulerStateTarget.Task));
        Assert.IsNull(ManagementSchedulerWire.DecodeStateCommand(activate, ManagementSchedulerStateTarget.Trigger));
        Assert.IsNull(ManagementSchedulerWire.DecodeStateCommand(activate, ManagementSchedulerStateTarget.Run));
    }

    [TestMethod]
    public void DecodedPagesAreReadOnlyAndUnrelatedTypesAreProgrammingErrors()
    {
        var page = new JsonObject { ["items"] = new JsonArray(Node("scheduler-capability-logical-job-definition")) };
        ManagementPage<ManagementCapabilityDefinition>? decoded = Response<ManagementPage<ManagementCapabilityDefinition>>(Bytes(page.ToJsonString()));
        Assert.IsNotNull(decoded);
        Assert.IsTrue(((ICollection<ManagementCapabilityDefinition>)decoded.Items).IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => ManagementSchedulerWire.DecodeResponse<Uri>(Bytes("{}")));
        Assert.ThrowsExactly<NotSupportedException>(() => ManagementSchedulerWire.EncodeResponse(new Uri("https://example.test")));
        Assert.ThrowsExactly<NotSupportedException>(() => ManagementSchedulerWire.DecodeResponse<ManagementSchedulerStateCommand>(Bytes("{}")));
    }

    [TestMethod]
    public void FixedDelayUsesOnlyItsFiniteImmediateBoundaryAndRetryCannotBecomeATightLoop()
    {
        foreach (string ignoredPolicy in new[] { "skip", "runLatestOnce", "catchUpBounded" })
        {
            var calendar = new ManagementSchedulerCalendar("fixedDelay", ignoredPolicy, DelayMs: "5");
            Assert.IsNotNull(Request<ManagementSchedulerCalendar>(ManagementSchedulerWire.EncodeRequest(calendar)));
        }
        var immediate = new ManagementSchedulerCalendar("fixedDelay", "skip", DelayMs: "5", InitialDelayMs: "0", RunImmediately: true);
        Assert.IsNotNull(Request<ManagementSchedulerCalendar>(ManagementSchedulerWire.EncodeRequest(immediate)));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementSchedulerWire.EncodeRequest(immediate with { RunImmediately = false }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementSchedulerWire.EncodeRequest(immediate with { InitialDelayMs = "1" }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementSchedulerWire.EncodeRequest(immediate with { MaximumCatchUp = 1, CatchUpWindowMs = "5" }));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementSchedulerWire.EncodeRequest(new ManagementSchedulerRetryPolicy(true, 3, "4999", "60000")));
        Assert.IsNotNull(Request<ManagementSchedulerRetryPolicy>(ManagementSchedulerWire.EncodeRequest(ManagementSchedulerRetryPolicy.Automatic())));
    }

    private static T? Request<T>(byte[] bytes) where T : class => ManagementSchedulerWire.DecodeRequest<T>(bytes);
    private static T? Response<T>(byte[] bytes) where T : class => ManagementSchedulerWire.DecodeResponse<T>(bytes);
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static JsonObject Node(string id) => JsonNode.Parse(Case(id))!.AsObject();

    private static string Case(string id)
    {
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllBytes(CatalogPath()));
        return catalog.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id)
            .GetProperty("json").GetString()!;
    }

    private static string CatalogPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "management", "v1", "scheduler-cases.json");
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("The independently authored scheduler wire cases are required.");
    }
}
