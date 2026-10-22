using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionPayloadMutationQualificationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("eligible")]
    [DataRow("admitted")]
    [DataRow("condition")]
    [DataRow("callback")]
    [DataRow("handler-start")]
    [DataRow("handler-end")]
    [DataRow("cleanup-end")]
    [DataRow("queue-duration")]
    [DataRow("condition-duration")]
    [DataRow("callback-duration")]
    [DataRow("handler-duration")]
    [DataRow("cleanup-duration")]
    [DataRow("original-error")]
    [DataRow("handler-error")]
    [DataRow("cleanup-error")]
    [DataRow("integration-error")]
    public async Task RepeatedOutcomeMayEnrichEachMissingContextFieldWithoutRecountingTheOutcome(string field)
    {
        await using var scope = new PayloadScope();
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(Outcome(scope.Id)));
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        HealthExecutionFact enrichment = Outcome(scope.Id, field);
        Assert.AreEqual(HealthFactDisposition.Duplicate, scope.Binding.Report(enrichment));
        HealthSnapshot after = scope.Service.CaptureSnapshot();
        Assert.IsTrue(after.Revision > before.Revision);
        HealthOutcomeSnapshot accounting = after.Monitors.Single().Outcomes!;
        Assert.AreEqual(1L, accounting.Failed);
        Assert.AreEqual(0L, accounting.Successful);
        Assert.AreEqual(Epoch, after.Monitors.Single().LastObservationAtUtc);
        HealthExecutionSnapshot execution = accounting.Execution!;
        object? actual = field switch
        {
            "eligible" => execution.EligibleAtUtc,
            "admitted" => execution.AdmittedAtUtc,
            "condition" => execution.ConditionStartedAtUtc,
            "callback" => execution.CallbackStartedAtUtc,
            "handler-start" => execution.ErrorHandlingStartedAtUtc,
            "handler-end" => execution.ErrorHandlingCompletedAtUtc,
            "cleanup-end" => execution.CleanupCompletedAtUtc,
            "queue-duration" => execution.QueueDelayMilliseconds,
            "condition-duration" => execution.ConditionDurationMilliseconds,
            "callback-duration" => execution.CallbackDurationMilliseconds,
            "handler-duration" => execution.ErrorHandlingDurationMilliseconds,
            "cleanup-duration" => execution.CleanupDurationMilliseconds,
            "original-error" => execution.OriginalError?.Code,
            "handler-error" => execution.ErrorHandlingError?.Code,
            "cleanup-error" => execution.CleanupError?.Code,
            "integration-error" => execution.IntegrationError?.Code,
            _ => throw new AssertFailedException("Unknown payload field.")
        };
        object expected = field.EndsWith("duration", StringComparison.Ordinal) ? 0d
            : field.EndsWith("error", StringComparison.Ordinal) ? "owned-reason" : Epoch.AddMinutes(-1);
        Assert.AreEqual(expected, actual);
        Assert.AreEqual(HealthFactDisposition.Duplicate, scope.Binding.Report(Outcome(scope.Id, field)));
        Assert.AreEqual(after.Revision, scope.Service.CaptureSnapshot().Revision);
    }

    [TestMethod]
    [DataRow("original", "code")]
    [DataRow("original", "message")]
    [DataRow("original", "type")]
    [DataRow("original", "truncated")]
    [DataRow("handler", "code")]
    [DataRow("handler", "message")]
    [DataRow("handler", "type")]
    [DataRow("handler", "truncated")]
    [DataRow("cleanup", "code")]
    [DataRow("cleanup", "message")]
    [DataRow("cleanup", "type")]
    [DataRow("cleanup", "truncated")]
    public async Task DetachedDiagnosticEqualityUsesEveryPublishedFieldAndRejectsContradictionsAtomically(string slot, string changed)
    {
        await using var scope = new PayloadScope();
        var original = new HealthDiagnostic("code", new string('x', 1024), "exception-type");
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(DiagnosticOutcome(scope.Id, slot, original)));
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        Assert.AreEqual(HealthFactDisposition.Duplicate, scope.Binding.Report(DiagnosticOutcome(scope.Id, slot,
            new HealthDiagnostic("code", new string('x', 1024), "exception-type"))));
        HealthDiagnostic conflicting = changed switch
        {
            "code" => new("changed", original.Message, original.ExceptionType),
            "message" => new(original.Code, "changed", original.ExceptionType),
            "type" => new(original.Code, original.Message, "changed"),
            "truncated" => new(original.Code, new string('x', 1025), original.ExceptionType),
            _ => throw new AssertFailedException("Unknown diagnostic field.")
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Binding.Report(DiagnosticOutcome(scope.Id, slot, conflicting)));
        HealthSnapshot after = scope.Service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.AreEqual(1L, after.Monitors.Single().Outcomes!.Failed);
        HealthExecutionSnapshot execution = after.Monitors.Single().Outcomes!.Execution!;
        HealthDiagnostic retained = slot switch { "original" => execution.OriginalError!, "handler" => execution.ErrorHandlingError!, _ => execution.CleanupError! };
        Assert.AreEqual("code", retained.Code);
        Assert.AreEqual(original.Message, retained.Message);
        Assert.AreEqual("exception-type", retained.ExceptionType);
        Assert.IsFalse(retained.IsTruncated);
    }

    [TestMethod]
    public async Task IntegrationDiagnosticsReplaceOnlyTheirOwnObservationAndSurviveUnrelatedFacts()
    {
        await using var scope = new PayloadScope();
        HealthExecutionFact Fault(string code) => new()
        {
            RegistrationId = scope.Id,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.IntegrationFault,
            OccurredAtUtc = Epoch,
            IntegrationError = new HealthDiagnostic(code)
        };
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(Fault("first")));
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(Outcome(scope.Id)));
        Assert.AreEqual("first", scope.Service.CaptureSnapshot().Monitors.Single().Outcomes!.Execution!.IntegrationError!.Code);
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(Fault("second")));
        HealthOutcomeSnapshot accounting = scope.Service.CaptureSnapshot().Monitors.Single().Outcomes!;
        Assert.AreEqual("second", accounting.Execution!.IntegrationError!.Code);
        Assert.AreEqual(1L, accounting.Failed);
        Assert.AreEqual(HealthExecutionOutcome.Failed, accounting.LastOutcome);
        Assert.AreEqual(HealthFactDisposition.Duplicate, scope.Binding.Report(new HealthExecutionFact
        {
            RegistrationId = scope.Id,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch,
            WorkCompletedAtUtc = Epoch,
            SourcePhase = HealthExecutionPhase.Callback,
            Outcome = HealthExecutionOutcome.Failed,
            IntegrationError = new("first")
        }));
        HealthSnapshot replaced = scope.Service.CaptureSnapshot();
        Assert.AreEqual("first", replaced.Monitors.Single().Outcomes!.Execution!.IntegrationError!.Code);
        Assert.AreEqual(HealthFactDisposition.Duplicate, scope.Binding.Report(Outcome(scope.Id)));
        HealthSnapshot duplicate = scope.Service.CaptureSnapshot();
        Assert.AreEqual("first", duplicate.Monitors.Single().Outcomes!.Execution!.IntegrationError!.Code);
        Assert.AreEqual(replaced.Revision, duplicate.Revision);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MergedSecondaryContextRejectsAnUnreportedHandlerStatusAndPreservesTheAcceptedPayload(bool hasRetainedContext)
    {
        await using var scope = new PayloadScope();
        if (hasRetainedContext) { Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(Outcome(scope.Id))); }
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        var incomplete = new HealthExecutionFact
        {
            RegistrationId = scope.Id,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.IntegrationFault,
            OccurredAtUtc = Epoch,
            ErrorHandlingStartedAtUtc = Epoch,
            IntegrationError = new("observer")
        };
        if (hasRetainedContext) { Assert.ThrowsExactly<InvalidOperationException>(() => scope.Binding.Report(incomplete)); }
        else { Assert.ThrowsExactly<ArgumentException>(() => scope.Binding.Report(incomplete)); }
        HealthSnapshot rejected = scope.Service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, rejected.Revision);
        Assert.AreEqual(hasRetainedContext ? 1L : 0L, rejected.Monitors.Single().Outcomes!.Failed);
        Assert.IsNull(rejected.Monitors.Single().Outcomes!.Execution?.ErrorHandlingStartedAtUtc);
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(new HealthExecutionFact
        {
            RegistrationId = scope.Id,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.ErrorHandlingStarted,
            OccurredAtUtc = Epoch,
            ErrorHandlingStartedAtUtc = Epoch,
            ErrorHandlingStatus = HealthExecutionStepStatus.Running
        }));
        Assert.AreEqual(Epoch, scope.Service.CaptureSnapshot().Monitors.Single().Outcomes!.Execution!.ErrorHandlingStartedAtUtc);
    }

    private static HealthExecutionFact Outcome(Guid id, string? field = null) => new()
    {
        RegistrationId = id,
        Kind = HealthExecutionFactKind.Outcome,
        ExecutionId = 1,
        OccurredAtUtc = Epoch,
        Outcome = HealthExecutionOutcome.Failed,
        SourcePhase = HealthExecutionPhase.Callback,
        WorkCompletedAtUtc = Epoch,
        EligibleAtUtc = field == "eligible" ? Epoch.AddMinutes(-1) : null,
        AdmittedAtUtc = field == "admitted" ? Epoch.AddMinutes(-1) : null,
        ConditionStartedAtUtc = field == "condition" ? Epoch.AddMinutes(-1) : null,
        CallbackStartedAtUtc = field == "callback" ? Epoch.AddMinutes(-1) : null,
        ErrorHandlingStartedAtUtc = field == "handler-start" ? Epoch.AddMinutes(-1) : null,
        ErrorHandlingCompletedAtUtc = field == "handler-end" ? Epoch.AddMinutes(-1) : null,
        CleanupCompletedAtUtc = field == "cleanup-end" ? Epoch.AddMinutes(-1) : null,
        QueueDelayMilliseconds = field == "queue-duration" ? 0 : null,
        ConditionDurationMilliseconds = field == "condition-duration" ? 0 : null,
        CallbackDurationMilliseconds = field == "callback-duration" ? 0 : null,
        ErrorHandlingDurationMilliseconds = field == "handler-duration" ? 0 : null,
        CleanupDurationMilliseconds = field == "cleanup-duration" ? 0 : null,
        ErrorHandlingStatus = field == "handler-start" ? HealthExecutionStepStatus.Running
            : field is "handler-end" or "handler-duration" or "handler-error" ? HealthExecutionStepStatus.Failed : null,
        CleanupStatus = field is "cleanup-end" or "cleanup-duration" or "cleanup-error" ? HealthExecutionStepStatus.Failed : null,
        OriginalError = field == "original-error" ? new("owned-reason") : null,
        ErrorHandlingError = field == "handler-error" ? new("owned-reason") : null,
        CleanupError = field == "cleanup-error" ? new("owned-reason") : null,
        IntegrationError = field == "integration-error" ? new("owned-reason") : null
    };

    private static HealthExecutionFact DiagnosticOutcome(Guid id, string slot, HealthDiagnostic diagnostic) => new()
    {
        RegistrationId = id,
        Kind = HealthExecutionFactKind.Outcome,
        ExecutionId = 1,
        OccurredAtUtc = Epoch,
        Outcome = HealthExecutionOutcome.Failed,
        SourcePhase = HealthExecutionPhase.Callback,
        WorkCompletedAtUtc = Epoch,
        ErrorHandlingStatus = slot == "handler" ? HealthExecutionStepStatus.Failed : null,
        CleanupStatus = slot == "cleanup" ? HealthExecutionStepStatus.Failed : null,
        OriginalError = slot == "original" ? diagnostic : null,
        ErrorHandlingError = slot == "handler" ? diagnostic : null,
        CleanupError = slot == "cleanup" ? diagnostic : null
    };

    private sealed class PayloadScope : IAsyncDisposable
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal HealthcheckService Service { get; } = new(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "payload-owner", InstanceId = Guid.NewGuid() });
        internal IHealthExecutionBinding Binding { get; }
        internal PayloadScope() => Binding = HealthExecutionBinder.Bind(Service,
            new ConsecutiveFailureMonitorOptions { FailureThreshold = 1, IsCritical = true },
            new HealthExecutionBindingOptions { RegistrationId = Id });
        public async ValueTask DisposeAsync() { await Binding.DisposeAsync(); await Service.DisposeAsync(); }
    }
}
