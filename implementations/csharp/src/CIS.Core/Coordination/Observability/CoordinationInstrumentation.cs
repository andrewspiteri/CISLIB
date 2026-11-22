using CIS.Coordination.Hosting;
using CIS.Healthcheck;
using CIS.Metrics;
using Microsoft.Extensions.Logging;
using MetricTimer = CIS.Metrics.ITimer;

namespace CIS.Coordination;

internal sealed class CoordinationInstrumentation : ICoordinationRuntimeObserver, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CoordinationRuntime runtime;
    private readonly MetricsAttachment attachment = new();
    private readonly ObservationRefresh refresh;
    private readonly ObservationLogBudget failures;
    private readonly ObservationLogBudget losses;
    private readonly ILogger? logger;
    private CoordinationMetrics? metrics;
    private IAvailabilityMonitor? health;
    private bool activated;
    private bool stopping;
    private Task healthCreation = Task.CompletedTask;
    private bool? reportedAvailability;

    internal CoordinationInstrumentation(CoordinationComposition composition, CoordinationRuntime runtime,
        TimeProvider? clock = null, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(runtime);
        RegistrationIdentity = composition.RegistrationIdentity;
        this.runtime = runtime;
        TimeProvider time = clock ?? TimeProvider.System;
        failures = new(time);
        losses = new(time);
        refresh = new(RefreshCore);
        try
        {
            logger = loggerFactory?.CreateLogger("CIS.Coordination");
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }

        runtime.SetObserver(this);
    }

    internal object RegistrationIdentity
    {
        get;
    }

    internal void AttachMetrics(MetricsRuntime owner)
    {
        if (attachment.Attach(owner))
        {
            Volatile.Write(ref metrics, new CoordinationMetrics(owner, runtime));
        }
    }

    internal void ActivateHealth(HealthcheckService? service, bool isCritical)
    {
        TaskCompletionSource creation;
        lock (gate)
        {
            if (activated || stopping)
            {
                return;
            }

            activated = true;
            creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            healthCreation = creation.Task;
        }

        try
        {
            health = service?.CreateAvailability(new()
            {
                Name = "Coordination authority",
                IsCritical = isCritical
            });
            RefreshHealth();
        }
        finally
        {
            creation.TrySetResult();
        }
    }

    internal void RefreshHealth() => refresh.Request();

    public ICoordinationRuntimeObserver CaptureOperationObserver() => new CapturedOperation(this, Volatile.Read(ref metrics));

    public void OperationCompleted(CoordinationRuntimeOperation operation, CoordinationRuntimeOperationOutcome outcome, TimeSpan duration)
        => CompleteOperation(Volatile.Read(ref metrics), operation, outcome, duration);

    private void CompleteOperation(CoordinationMetrics? captured, CoordinationRuntimeOperation operation,
        CoordinationRuntimeOperationOutcome outcome, TimeSpan duration)
    {
        // Only the sealed library Metrics runtime records here; logging and health run independently.
        Attempt(() => captured?.Operation(operation, outcome, duration));
        refresh.Request();
    }

    public void Transition(CoordinationLeaseSnapshot snapshot)
    {
        if (snapshot.State == CoordinationLeaseState.Lost && snapshot.LossReason is { } reason)
        {
            Attempt(() => Volatile.Read(ref metrics)?.Loss(reason));
        }

        Attempt(() =>
        {
            bool lost = snapshot.State == CoordinationLeaseState.Lost;
            if (logger is null || lost && !losses.TryTake())
            {
                return;
            }

            LogLevel level = lost ? LogLevel.Warning : LogLevel.Debug;
            if (logger.IsEnabled(level))
            {
                logger.Log(level, new EventId(19000, "coordination.transition"),
                    "Coordination {State} for {Scope}/{Duty}/{Resource}; instance {Instance}, acquisition {Acquisition}, generation {Generation}, reason {Reason}.",
                    snapshot.State, snapshot.Acquisition.Key.ExecutionScope, snapshot.Acquisition.Key.DutyId,
                    snapshot.Acquisition.Key.ResourceId, snapshot.Acquisition.InstanceId, snapshot.Acquisition.AcquisitionId,
                    snapshot.Grant?.Generation, snapshot.LossReason);
            }
        });
        refresh.Request();
    }

    internal CoordinationMetrics? CaptureMetrics() => Volatile.Read(ref metrics);

    internal void GuardRejected(CoordinationMetrics? captured, CoordinationRejectionReason reason)
        => Attempt(() => captured?.GuardRejected(reason));

    private void RefreshCore()
    {
        bool? available = runtime.GetSnapshot(take: 1).AuthorityAvailable;
        if (available is { } known)
        {
            Attempt(() =>
            {
                if (known)
                {
                    health?.ReportPublished();
                }
                else
                {
                    health?.Invalidate(new("coordination.authority_unavailable", "The last authority operation could not establish availability."));
                }
            });
        }

        if (available != reportedAvailability && available is { } state)
        {
            bool? previous = reportedAvailability;
            reportedAvailability = state;
            Attempt(() =>
            {
                if (logger is not null && (!state || previous == false) && failures.TryTake())
                {
                    logger.Log(state ? LogLevel.Debug : LogLevel.Warning, new EventId(19001, "coordination.availability"),
                        "Coordination authority availability changed to {Available}.", state);
                }
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task creating;
        lock (gate)
        {
            stopping = true;
            creating = healthCreation;
        }

        await creating.ConfigureAwait(false);
        await refresh.DisposeAsync().ConfigureAwait(false);
        if (health is { } monitor)
        {
            await monitor.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void Attempt(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
    }

    private sealed class CapturedOperation(CoordinationInstrumentation owner, CoordinationMetrics? captured) : ICoordinationRuntimeObserver
    {
        public void OperationCompleted(CoordinationRuntimeOperation operation, CoordinationRuntimeOperationOutcome outcome, TimeSpan duration)
            => owner.CompleteOperation(captured, operation, outcome, duration);

        public void Transition(CoordinationLeaseSnapshot snapshot)
        {
        }
    }
}

internal sealed class CoordinationMetrics
{
    private readonly MetricTimer duration;
    private readonly ICounter losses;
    private readonly ICounter rejections;

    internal CoordinationMetrics(MetricsRuntime metrics, CoordinationRuntime runtime)
    {
        duration = metrics.CreateTimer(new("cis.coordination.operation.duration", MetricUnit.Seconds,
            "One physical authority call from provider invocation through actual return.", ["operation", "outcome"]));
        losses = metrics.CreateCounter(new("cis.coordination.ownership.losses", description: "One terminal local ownership loss.",
            allowedDimensionKeys: ["reason"]));
        rejections = metrics.CreateCounter(new("cis.coordination.guard.rejections", description: "One rejected protected-operation admission.",
            allowedDimensionKeys: ["reason"]));
        metrics.RegisterObservableGauge(new("cis.coordination.active.leases", description: "Locally Owned or Draining handles."),
            () => [new(runtime.GetSnapshot(take: 1).OwnedLeases)]);
    }

    internal void Operation(CoordinationRuntimeOperation operation, CoordinationRuntimeOperationOutcome outcome, TimeSpan elapsed)
        => duration.Record(elapsed.TotalSeconds, new(("operation", operation switch
        {
            CoordinationRuntimeOperation.Acquire => "acquire",
            CoordinationRuntimeOperation.Renew => "renew",
            CoordinationRuntimeOperation.Release => "release",
            _ => "resolve"
        }), ("outcome", outcome switch
        {
            CoordinationRuntimeOperationOutcome.Succeeded => "succeeded",
            CoordinationRuntimeOperationOutcome.Busy => "busy",
            CoordinationRuntimeOperationOutcome.Refused => "refused",
            CoordinationRuntimeOperationOutcome.Unavailable => "unavailable",
            CoordinationRuntimeOperationOutcome.Indeterminate => "indeterminate",
            CoordinationRuntimeOperationOutcome.NotObserved => "not_observed",
            CoordinationRuntimeOperationOutcome.Cancelled => "cancelled",
            _ => "failed"
        })));

    internal void Loss(CoordinationLossReason reason) => losses.Add(1, new(("reason", reason switch
    {
        CoordinationLossReason.Expired => "expired",
        CoordinationLossReason.Superseded => "superseded",
        CoordinationLossReason.RenewalDeadlineExceeded => "renewal_deadline_exceeded",
        CoordinationLossReason.AuthorityUnavailable => "authority_unavailable",
        CoordinationLossReason.ShutdownDeadlineExceeded => "shutdown_deadline_exceeded",
        _ => "caller_cancelled"
    })));

    internal void GuardRejected(CoordinationRejectionReason reason) => rejections.Add(1, new(("reason", reason switch
    {
        CoordinationRejectionReason.Missing => "missing",
        CoordinationRejectionReason.Released => "released",
        CoordinationRejectionReason.Expired => "expired",
        CoordinationRejectionReason.Superseded => "superseded",
        CoordinationRejectionReason.WrongAuthority => "wrong_authority",
        CoordinationRejectionReason.WrongDatabase => "wrong_database",
        CoordinationRejectionReason.InvalidTransaction => "invalid_transaction",
        _ => "authority_unavailable"
    })));
}
