using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.CompatibilityTests.Healthcheck.HealthcheckCompatibilityInputs;

namespace CIS.Tests.CompatibilityTests.Healthcheck;

[TestClass]
public sealed class HealthcheckHeartbeatCompatibilityTests
{
    [TestMethod]
    public void HeartbeatFactsAppendValuesWithoutChangingTheFrozenPhaseOrOutcomeEnums()
    {
        FrozenInputs();
        JsonObject frozenExecution = FrozenExecutionSchema();
        CollectionAssert.AreEqual(frozenExecution["properties"]!["phase"]!["enum"]!.AsArray()
            .Select(item => item!.GetValue<string>()).ToArray(), Enum.GetNames<HealthExecutionPhase>());
        foreach (string name in new[] { nameof(HealthExecutionFactKind.Registered), nameof(HealthExecutionFactKind.Eligible),
            nameof(HealthExecutionFactKind.Admitted), nameof(HealthExecutionFactKind.ConditionStarted), nameof(HealthExecutionFactKind.CallbackStarted),
            nameof(HealthExecutionFactKind.Outcome), nameof(HealthExecutionFactKind.ErrorHandlingStarted), nameof(HealthExecutionFactKind.ErrorHandlingCompleted),
            nameof(HealthExecutionFactKind.CleanupCompleted), nameof(HealthExecutionFactKind.Settled), nameof(HealthExecutionFactKind.Retired),
            nameof(HealthExecutionFactKind.IntegrationFault) })
        {
            JsonNode originalEnum = Api()["types"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == nameof(HealthExecutionFactKind))!;
            Assert.IsTrue(originalEnum["members"]!.AsArray().Any(member => member!.GetValue<string>() == name + "=" + (int)Enum.Parse<HealthExecutionFactKind>(name)));
        }
        Assert.AreEqual(12, (int)HealthExecutionFactKind.Dispatched);
        Assert.AreEqual(13, (int)HealthExecutionFactKind.Suppressed);
        CollectionAssert.AreEqual(new[] { "Succeeded", "Failed", "Skipped", "ExpectedCancellation" }, Enum.GetNames<HealthExecutionOutcome>());
    }

    [TestMethod]
    public async Task ActualNewExecutionOutputRetainsTheFrozenSchemasRequiredShapeAndPhaseVocabulary()
    {
        FrozenInputs();
        await using var service = new HealthcheckService(new HealthcheckOptions(), Identity(), new FrozenClock());
        await using IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service,
            new ConsecutiveFailureMonitorOptions { IsCritical = false, FailureThreshold = 2 }, new() { RegistrationId = Guid.NewGuid() });
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Dispatched,
            OccurredAtUtc = FrozenClock.Instant, DispatchedAtUtc = FrozenClock.Instant, DispatchDelayMilliseconds = 3
        });
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = FrozenClock.Instant, WorkCompletedAtUtc = FrozenClock.Instant,
            SourcePhase = HealthExecutionPhase.Callback, Outcome = HealthExecutionOutcome.Succeeded,
            ConditionResult = true, CallbackResult = true
        });
        HealthReportBuffer formatted = HealthReportFormatter.FormatSnapshot(service.CaptureSnapshot(), HealthReportView.Details, new());
        Assert.IsFalse(formatted.IsError);
        JsonObject execution = JsonNode.Parse(formatted.Utf8Bytes.Span)!["monitors"]![0]!["outcomes"]!["execution"]!.AsObject();
        JsonObject frozen = FrozenExecutionSchema();
        Assert.IsFalse(frozen.ContainsKey("additionalProperties"), "The preserved schema permits unknown additive execution fields.");
        foreach (JsonNode? required in frozen["required"]!.AsArray())
        { Assert.IsTrue(execution.ContainsKey(required!.GetValue<string>()), "A frozen required execution property is missing."); }
        Assert.IsTrue(frozen["properties"]!["phase"]!["enum"]!.AsArray().Any(value => value!.GetValue<string>() == execution["phase"]!.GetValue<string>()));
        foreach (string added in new[] { "dispatchedAtUtc", "dispatchDelayMs", "conditionResult", "callbackResult" })
        {
            Assert.IsTrue(execution.ContainsKey(added));
            Assert.IsFalse(frozen["properties"]!.AsObject().ContainsKey(added));
        }
        // This checks compatibility with the actual preserved schema, not execution of an unavailable historical reader binary.
        FrozenInputs();
    }

    private static JsonObject FrozenExecutionSchema() => Read(Path.Combine(Repository(), "compatibility/csharp/healthcheck/hc00-originals/report.schema.json"))
        ["$defs"]!["execution"]!.AsObject();

    private static HealthApplicationIdentity Identity() => new()
    {
        Name = "heartbeat-compatibility-fixture",
        InstanceId = Guid.Parse("10000000-0000-0000-0000-000000000001")
    };

    private sealed class FrozenClock : TimeProvider
    {
        internal static readonly DateTimeOffset Instant = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Instant;
    }
}
