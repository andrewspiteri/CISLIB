using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionNormalizationMutationQualificationTests
{
    private static readonly Guid Registration = Guid.Parse("c54dcd25-00e7-444a-8cce-4dc5ce34af89");
    private static readonly DateTimeOffset Occurred = new(2026, 10, 21, 14, 30, 0, TimeSpan.FromHours(2));

    public static IEnumerable<object[]> InvalidDurations()
    {
        foreach (string field in new[] { "queue", "condition", "callback", "handler", "cleanup", "total" })
        {
            foreach (double value in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            { yield return [field, value]; }
        }
    }

    [TestMethod]
    [DynamicData(nameof(InvalidDurations))]
    public void DetachedNormalizationRejectsEachInvalidDurationBeforeForwardingItToState(string field, double value)
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => ExecutionFactValidation.Normalize(Settled(field, value), Registration));
        Assert.AreEqual("fact", error.ParamName);
    }

    [TestMethod]
    [DataRow("queue")]
    [DataRow("condition")]
    [DataRow("callback")]
    [DataRow("handler")]
    [DataRow("cleanup")]
    [DataRow("total")]
    public void ZeroIsAnAcceptedMeasuredDurationAndAbsentDurationsStayAbsent(string field)
    {
        HealthExecutionFact normalized = ExecutionFactValidation.Normalize(Settled(field, 0), Registration);
        double?[] observed = [normalized.QueueDelayMilliseconds, normalized.ConditionDurationMilliseconds,
            normalized.CallbackDurationMilliseconds, normalized.ErrorHandlingDurationMilliseconds,
            normalized.CleanupDurationMilliseconds, normalized.TotalDurationMilliseconds];
        Assert.AreEqual(1, observed.Count(value => value.HasValue));
        Assert.AreEqual(0d, observed.Single(value => value.HasValue));
        Assert.AreEqual(Occurred.ToUniversalTime(), normalized.OccurredAtUtc);
        Assert.AreEqual(TimeSpan.Zero, normalized.OccurredAtUtc.Offset);
        Assert.AreEqual(Occurred.ToUniversalTime(), normalized.SettledAtUtc);
        Assert.AreEqual(Registration, normalized.RegistrationId);
        Assert.AreEqual(7L, normalized.ExecutionId);
        Assert.AreEqual(HealthExecutionFactKind.Settled, normalized.Kind);
    }

    [TestMethod]
    [DataRow("foreign-registration")]
    [DataRow("undefined-kind")]
    [DataRow("missing-execution")]
    [DataRow("zero-execution")]
    [DataRow("undefined-phase")]
    [DataRow("undefined-outcome")]
    [DataRow("undefined-handler")]
    [DataRow("undefined-cleanup")]
    [DataRow("missing-outcome")]
    [DataRow("handler-notapplicable-start")]
    [DataRow("handler-notapplicable-duration")]
    [DataRow("handler-notstarted-start")]
    [DataRow("handler-notstarted-complete")]
    [DataRow("handler-notstarted-duration")]
    [DataRow("cleanup-notapplicable-duration")]
    [DataRow("handler-error-notstarted")]
    [DataRow("handler-error-running")]
    [DataRow("handler-error-succeeded")]
    [DataRow("handler-error-notapplicable")]
    [DataRow("cleanup-error-notstarted")]
    [DataRow("cleanup-error-running")]
    [DataRow("cleanup-error-succeeded")]
    [DataRow("cleanup-error-notapplicable")]
    public void DetachedNormalizationRejectsMalformedIdentityOrContextAtItsOwnReceivingBoundary(string scenario)
    {
        Assert.AreEqual(HealthExecutionFactKind.IntegrationFault, ExecutionFactValidation.Normalize(GuardFact("valid"), Registration).Kind);
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => ExecutionFactValidation.Normalize(GuardFact(scenario), Registration));
        Assert.AreEqual("fact", error.ParamName);
    }

    [TestMethod]
    public void DetachedNormalizationPreservesEveryActualInstantAndDurationWithoutReconstructingElapsedTime()
    {
        var diagnostic = AcceptedDiagnostic("observed", "owned text", "detached-type");
        var input = new HealthExecutionFact
        {
            RegistrationId = Registration,
            Kind = HealthExecutionFactKind.Settled,
            ExecutionId = 7,
            OccurredAtUtc = Occurred,
            EligibleAtUtc = Occurred.AddMinutes(-8),
            AdmittedAtUtc = Occurred.AddMinutes(-7),
            ConditionStartedAtUtc = Occurred.AddMinutes(-6),
            CallbackStartedAtUtc = Occurred.AddMinutes(-5),
            WorkCompletedAtUtc = Occurred.AddMinutes(-4),
            ErrorHandlingStartedAtUtc = Occurred.AddMinutes(-3),
            ErrorHandlingCompletedAtUtc = Occurred.AddMinutes(-2),
            CleanupCompletedAtUtc = Occurred.AddMinutes(-1),
            SettledAtUtc = Occurred,
            QueueDelayMilliseconds = 1.25,
            ConditionDurationMilliseconds = 2.5,
            CallbackDurationMilliseconds = 3.75,
            ErrorHandlingDurationMilliseconds = 4.125,
            CleanupDurationMilliseconds = 5.25,
            TotalDurationMilliseconds = 6.5,
            ErrorHandlingStatus = HealthExecutionStepStatus.Failed,
            CleanupStatus = HealthExecutionStepStatus.Failed,
            OriginalError = diagnostic,
            ErrorHandlingError = diagnostic,
            CleanupError = diagnostic,
            IntegrationError = diagnostic
        };
        HealthExecutionFact normalized = ExecutionFactValidation.Normalize(input, Registration);
        Assert.AreNotSame(input, normalized);
        DateTimeOffset?[] times = [normalized.EligibleAtUtc, normalized.AdmittedAtUtc, normalized.ConditionStartedAtUtc,
            normalized.CallbackStartedAtUtc, normalized.WorkCompletedAtUtc, normalized.ErrorHandlingStartedAtUtc,
            normalized.ErrorHandlingCompletedAtUtc, normalized.CleanupCompletedAtUtc, normalized.SettledAtUtc];
        for (int index = 0; index < times.Length; index++)
        {
            Assert.AreEqual(Occurred.AddMinutes(index - 8).ToUniversalTime(), times[index]);
            Assert.AreEqual(TimeSpan.Zero, times[index]!.Value.Offset);
        }
        CollectionAssert.AreEqual(new double?[] { 1.25, 2.5, 3.75, 4.125, 5.25, 6.5 },
            new double?[] { normalized.QueueDelayMilliseconds, normalized.ConditionDurationMilliseconds, normalized.CallbackDurationMilliseconds,
                normalized.ErrorHandlingDurationMilliseconds, normalized.CleanupDurationMilliseconds, normalized.TotalDurationMilliseconds });
        Assert.AreSame(diagnostic, normalized.OriginalError);
        Assert.AreSame(diagnostic, normalized.ErrorHandlingError);
        Assert.AreSame(diagnostic, normalized.CleanupError);
        Assert.AreSame(diagnostic, normalized.IntegrationError);
        Assert.IsNull(normalized.Outcome);
        Assert.IsNull(normalized.SourcePhase);
    }

    private static HealthDiagnostic AcceptedDiagnostic(string? code = null, string? message = null, string? exceptionType = null)
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic(code, message, exceptionType); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid diagnostic constructor arguments must produce a detached diagnostic.");
        Assert.IsNotNull(diagnostic);
        return diagnostic;
    }

    private static HealthExecutionFact Settled(string field, double value) => new()
    {
        RegistrationId = Registration,
        Kind = HealthExecutionFactKind.Settled,
        ExecutionId = 7,
        OccurredAtUtc = Occurred,
        SettledAtUtc = Occurred,
        ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded,
        CleanupStatus = HealthExecutionStepStatus.Succeeded,
        QueueDelayMilliseconds = field == "queue" ? value : null,
        ConditionDurationMilliseconds = field == "condition" ? value : null,
        CallbackDurationMilliseconds = field == "callback" ? value : null,
        ErrorHandlingDurationMilliseconds = field == "handler" ? value : null,
        CleanupDurationMilliseconds = field == "cleanup" ? value : null,
        TotalDurationMilliseconds = field == "total" ? value : null
    };

    private static HealthExecutionFact GuardFact(string scenario) => new()
    {
        RegistrationId = scenario == "foreign-registration" ? Guid.Parse("c54dcd25-00e7-444a-8cce-4dc5ce34af80") : Registration,
        Kind = scenario == "undefined-kind" ? (HealthExecutionFactKind)999
            : scenario is "undefined-outcome" or "missing-outcome" ? HealthExecutionFactKind.Outcome
            : scenario == "missing-execution" ? HealthExecutionFactKind.Eligible : HealthExecutionFactKind.IntegrationFault,
        ExecutionId = scenario == "missing-execution" ? null : scenario == "zero-execution" ? 0 : 7,
        OccurredAtUtc = Occurred,
        EligibleAtUtc = scenario == "missing-execution" ? Occurred : null,
        WorkCompletedAtUtc = scenario is "undefined-outcome" or "missing-outcome" ? Occurred : null,
        SourcePhase = scenario == "undefined-phase" ? (HealthExecutionPhase)999
            : scenario is "undefined-outcome" or "missing-outcome" ? HealthExecutionPhase.Callback : null,
        Outcome = scenario == "undefined-outcome" ? (HealthExecutionOutcome)999 : null,
        ErrorHandlingStatus = scenario == "undefined-handler" ? (HealthExecutionStepStatus)999
            : scenario is "handler-notapplicable-start" or "handler-notapplicable-duration" or "handler-error-notapplicable" ? HealthExecutionStepStatus.NotApplicable
            : scenario is "handler-notstarted-start" or "handler-notstarted-complete" or "handler-notstarted-duration" or "handler-error-notstarted" ? HealthExecutionStepStatus.NotStarted
            : scenario == "handler-error-running" ? HealthExecutionStepStatus.Running
            : scenario == "handler-error-succeeded" ? HealthExecutionStepStatus.Succeeded : null,
        CleanupStatus = scenario == "undefined-cleanup" ? (HealthExecutionStepStatus)999
            : scenario is "cleanup-notapplicable-duration" or "cleanup-error-notapplicable" ? HealthExecutionStepStatus.NotApplicable
            : scenario == "cleanup-error-notstarted" ? HealthExecutionStepStatus.NotStarted
            : scenario == "cleanup-error-running" ? HealthExecutionStepStatus.Running
            : scenario == "cleanup-error-succeeded" ? HealthExecutionStepStatus.Succeeded : null,
        ErrorHandlingStartedAtUtc = scenario is "handler-notapplicable-start" or "handler-notstarted-start" ? Occurred : null,
        ErrorHandlingCompletedAtUtc = scenario == "handler-notstarted-complete" ? Occurred : null,
        ErrorHandlingDurationMilliseconds = scenario is "handler-notapplicable-duration" or "handler-notstarted-duration" ? 0 : null,
        CleanupDurationMilliseconds = scenario == "cleanup-notapplicable-duration" ? 0 : null,
        ErrorHandlingError = scenario.StartsWith("handler-error-", StringComparison.Ordinal) ? AcceptedDiagnostic("handler") : null,
        CleanupError = scenario.StartsWith("cleanup-error-", StringComparison.Ordinal) ? AcceptedDiagnostic("cleanup") : null,
        IntegrationError = AcceptedDiagnostic("receiver")
    };
}
