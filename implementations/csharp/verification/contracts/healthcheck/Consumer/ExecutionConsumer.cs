// HC00 compile-only facts; actual Heartbeat integration remains a later runtime task.
using CIS.Healthcheck;

namespace HealthcheckContractConsumer;

internal static class ExecutionConsumer
{
    internal static async Task BindAsync(IHealthMonitorFactory factory, CancellationToken waitCancellation)
    {
        await using IConsecutiveFailureMonitor external = factory.CreateConsecutiveFailures(
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 3 });
        await using IHealthExecutionBinding externalBinding = HealthExecutionBinder.Bind(
            external, new HealthExecutionBindingOptions { RegistrationId = Guid.NewGuid() });
        await using IHealthExecutionBinding ownedFailures = HealthExecutionBinder.Bind(
            factory,
            new ConsecutiveFailureMonitorOptions { IsCritical = false, FailureThreshold = 2 },
            new HealthExecutionBindingOptions
            {
                RegistrationId = Guid.NewGuid(),
                Interval = TimeSpan.FromMinutes(1),
                InitialDelay = TimeSpan.Zero,
                DisplayName = "cache-refresh",
            });
        await using IHealthExecutionBinding ownedAvailability = HealthExecutionBinder.Bind(
            factory,
            new AvailabilityMonitorOptions { IsCritical = true, Name = "usable-data" },
            new HealthExecutionBindingOptions
            {
                RegistrationId = Guid.NewGuid(),
                SuccessPublishesUsableData = true,
            });

        DateTimeOffset now = DateTimeOffset.UtcNow;
        HealthFactDisposition registered = ownedFailures.Report(new HealthExecutionFact
        {
            RegistrationId = ownedFailures.RegistrationId,
            Kind = HealthExecutionFactKind.Registered,
            OccurredAtUtc = now,
        });
        HealthFactDisposition outcome = ownedFailures.Report(new HealthExecutionFact
        {
            RegistrationId = ownedFailures.RegistrationId,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.Outcome,
            SourcePhase = HealthExecutionPhase.Callback,
            OccurredAtUtc = now,
            Outcome = HealthExecutionOutcome.Failed,
            EligibleAtUtc = now,
            AdmittedAtUtc = now,
            ConditionStartedAtUtc = now,
            CallbackStartedAtUtc = now,
            WorkCompletedAtUtc = now,
            QueueDelayMilliseconds = 0,
            ConditionDurationMilliseconds = 0,
            CallbackDurationMilliseconds = 0,
            OriginalError = new HealthDiagnostic(code: "CallbackFailed"),
        });
        HealthFactDisposition settled = ownedFailures.Report(new HealthExecutionFact
        {
            RegistrationId = ownedFailures.RegistrationId,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.Settled,
            OccurredAtUtc = now,
            ErrorHandlingStartedAtUtc = now,
            ErrorHandlingCompletedAtUtc = now,
            CleanupCompletedAtUtc = now,
            SettledAtUtc = now,
            ErrorHandlingDurationMilliseconds = 0,
            CleanupDurationMilliseconds = 0,
            TotalDurationMilliseconds = 0,
            ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded,
            CleanupStatus = HealthExecutionStepStatus.Succeeded,
            ErrorHandlingError = null,
            CleanupError = null,
            IntegrationError = null,
        });
        _ = (registered, outcome, settled, externalBinding.MonitorId, ownedAvailability.MonitorId);

        // Releasing an external binding preserves the caller's monitor ownership.
        await externalBinding.DisposeAsync();
        external.ReportSuccess();
        external.Withdraw();
        await external.WaitForDisposalAsync(waitCancellation);
    }

    internal static void ReadExecution(HealthExecutionSnapshot execution)
    {
        _ = (execution.RegistrationId, execution.ExecutionId, execution.Phase,
            execution.EligibleAtUtc, execution.AdmittedAtUtc,
            execution.ConditionStartedAtUtc, execution.CallbackStartedAtUtc,
            execution.WorkCompletedAtUtc, execution.ErrorHandlingStartedAtUtc,
            execution.ErrorHandlingCompletedAtUtc, execution.CleanupCompletedAtUtc,
            execution.SettledAtUtc, execution.QueueDelayMilliseconds,
            execution.ConditionDurationMilliseconds, execution.CallbackDurationMilliseconds,
            execution.ErrorHandlingDurationMilliseconds, execution.CleanupDurationMilliseconds,
            execution.TotalDurationMilliseconds, execution.ErrorHandlingStatus,
            execution.CleanupStatus, execution.OriginalError,
            execution.ErrorHandlingError, execution.CleanupError, execution.IntegrationError);
    }
}
