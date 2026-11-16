using CIS.Healthcheck;
using CIS.Healthcheck.Internal;

namespace CIS.Heartbeat.Healthcheck;

/// <summary>Binds recurring execution facts to existing Healthcheck policy APIs.</summary>
public static class HeartbeatHealthcheckExtensions
{
    /// <summary>Registers work through its actual owner and atomically binds the selected monitor before it can run.</summary>
    /// <remarks>Valid duplicates reuse the original binding. Monitor factories and policy options are not retained by idle scheduler entries.</remarks>
    public static void Register(this IHeartbeatService service, IHeartbeatSubscriber subscriber,
        HeartbeatRegistrationOptions options, HeartbeatHealthcheckOptions monitoring,
        Func<CancellationToken, Task<bool>>? condition = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(monitoring);
        if (service is not IHeartbeatOwnerFacade facade) { throw new ArgumentException("Use the actual owner-bound Heartbeat service.", nameof(service)); }
        HeartbeatRegistrationOptions schedule = options.ValidatedCopy();
        int selections = (monitoring.Monitor is null ? 0 : 1) + (monitoring.ConsecutiveFailures is null ? 0 : 1) + (monitoring.Availability is null ? 0 : 1);
        if (selections != 1) { throw new ArgumentException("Select exactly one monitor or policy.", nameof(monitoring)); }
        if (monitoring.Monitor is not null)
        {
            if (monitoring.Monitor is not IHealthStateAccess || monitoring.Monitor is not (IConsecutiveFailureMonitor or IAvailabilityMonitor))
            { throw new ArgumentException("Use a suitable monitor from this Core implementation.", nameof(monitoring)); }
            if (monitoring.Monitor is IAvailabilityMonitor && !monitoring.SuccessPublishesUsableData)
            { throw new ArgumentException("Availability requires explicit usable-data publication.", nameof(monitoring)); }
        }
        else
        {
            if (monitoring.MonitorFactory is not IHealthStateAccess) { throw new ArgumentException("Supply the Core monitor factory for a typed policy.", nameof(monitoring)); }
            HealthExecutionBinder.ValidatePolicy(monitoring.ConsecutiveFailures ?? (HealthMonitorOptions)monitoring.Availability!, monitoring.SuccessPublishesUsableData);
        }
        facade.Owner.Runtime.Register(facade.Owner, subscriber, schedule, condition, registrationId =>
        {
            var bindingOptions = new HealthExecutionBindingOptions
            {
                RegistrationId = registrationId,
                Interval = schedule.Interval,
                InitialDelay = schedule.InitialDelay,
                DisplayName = schedule.Name,
                SuccessPublishesUsableData = monitoring.SuccessPublishesUsableData
            };
            IHealthExecutionBinding binding = monitoring.Monitor is not null
                ? HealthExecutionBinder.Bind(monitoring.Monitor, bindingOptions)
                : monitoring.ConsecutiveFailures is not null
                    ? HealthExecutionBinder.Bind(monitoring.MonitorFactory!, monitoring.ConsecutiveFailures, bindingOptions)
                    : HealthExecutionBinder.Bind(monitoring.MonitorFactory!, monitoring.Availability!, bindingOptions);
            return new HeartbeatHealthcheckObserver(binding);
        });
    }
}

internal sealed class HeartbeatHealthcheckObserver(IHealthExecutionBinding binding) : IHeartbeatObserver
{
    public void Report(HeartbeatNotification fact)
    {
        bool execution = fact.ExecutionId.HasValue;
        binding.Report(new HealthExecutionFact
        {
            RegistrationId = fact.RegistrationId,
            Kind = fact.Kind switch
            {
                HeartbeatEvent.Registered => HealthExecutionFactKind.Registered,
                HeartbeatEvent.Eligible => HealthExecutionFactKind.Eligible,
                HeartbeatEvent.Admitted => HealthExecutionFactKind.Admitted,
                HeartbeatEvent.Dispatched => HealthExecutionFactKind.Dispatched,
                HeartbeatEvent.ConditionStarted => HealthExecutionFactKind.ConditionStarted,
                HeartbeatEvent.CallbackStarted => HealthExecutionFactKind.CallbackStarted,
                HeartbeatEvent.ErrorHandlingStarted => HealthExecutionFactKind.ErrorHandlingStarted,
                HeartbeatEvent.ErrorHandlingCompleted => HealthExecutionFactKind.ErrorHandlingCompleted,
                HeartbeatEvent.CleanupCompleted => HealthExecutionFactKind.CleanupCompleted,
                HeartbeatEvent.Outcome => HealthExecutionFactKind.Outcome,
                HeartbeatEvent.Suppressed => HealthExecutionFactKind.Suppressed,
                HeartbeatEvent.Settled => HealthExecutionFactKind.Settled,
                HeartbeatEvent.Retired => HealthExecutionFactKind.Retired,
                _ => HealthExecutionFactKind.IntegrationFault
            },
            OccurredAtUtc = fact.OccurredAtUtc,
            ExecutionId = fact.ExecutionId,
            SourcePhase = Phase(fact.SourcePhase),
            Outcome = fact.Outcome switch
            {
                HeartbeatResult.Succeeded => HealthExecutionOutcome.Succeeded,
                HeartbeatResult.Failed => HealthExecutionOutcome.Failed,
                HeartbeatResult.Skipped => HealthExecutionOutcome.Skipped,
                HeartbeatResult.ExpectedCancellation => HealthExecutionOutcome.ExpectedCancellation,
                _ => null
            },
            EligibleAtUtc = fact.EligibleAtUtc,
            AdmittedAtUtc = fact.AdmittedAtUtc,
            DispatchedAtUtc = fact.DispatchedAtUtc,
            ConditionStartedAtUtc = fact.ConditionStartedAtUtc,
            CallbackStartedAtUtc = fact.CallbackStartedAtUtc,
            WorkCompletedAtUtc = fact.WorkCompletedAtUtc,
            ErrorHandlingStartedAtUtc = fact.ErrorHandlingStartedAtUtc,
            ErrorHandlingCompletedAtUtc = fact.ErrorHandlingCompletedAtUtc,
            CleanupCompletedAtUtc = fact.CleanupCompletedAtUtc,
            SettledAtUtc = fact.SettledAtUtc,
            SuppressedAtUtc = fact.SuppressedAtUtc,
            SuppressedPhase = Phase(fact.SuppressedPhase),
            SuppressionReason = fact.SuppressedAtUtc.HasValue
                ? fact.TargetUnavailable ? HealthExecutionSuppressionReason.TargetUnavailable : HealthExecutionSuppressionReason.ApplicationStopping : null,
            ConditionResult = fact.ConditionResult,
            CallbackResult = fact.CallbackResult,
            ExpectedCancellationPhase = Phase(fact.ExpectedCancellationPhase),
            QueueDelayMilliseconds = fact.QueueDelayMilliseconds,
            DispatchDelayMilliseconds = fact.DispatchDelayMilliseconds,
            ConditionDurationMilliseconds = fact.ConditionDurationMilliseconds,
            CallbackDurationMilliseconds = fact.CallbackDurationMilliseconds,
            ErrorHandlingDurationMilliseconds = fact.ErrorHandlingDurationMilliseconds,
            CleanupDurationMilliseconds = fact.CleanupDurationMilliseconds,
            TotalDurationMilliseconds = fact.TotalDurationMilliseconds,
            ErrorHandlingStatus = execution ? (HealthExecutionStepStatus)(int)fact.ErrorHandlingStatus : null,
            CleanupStatus = execution ? (HealthExecutionStepStatus)(int)fact.CleanupStatus : null,
            OriginalError = Diagnostic(fact.OriginalError),
            ErrorHandlingError = Diagnostic(fact.ErrorHandlingError),
            CleanupError = Diagnostic(fact.CleanupError),
            IntegrationError = Diagnostic(fact.IntegrationError)
        });
    }

    public ValueTask FlushAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => binding.DisposeAsync();
    private static HealthExecutionPhase? Phase(HeartbeatPhase? phase) => phase switch
    {
        HeartbeatPhase.Condition => HealthExecutionPhase.Condition,
        HeartbeatPhase.Callback => HealthExecutionPhase.Callback,
        _ => null
    };
    private static HealthDiagnostic? Diagnostic(Exception? error) => error is null ? null : HealthDiagnostic.FromException(error);
}
