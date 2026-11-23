using CIS.Coordination;
using CIS.Healthcheck;
using CIS.Jobs.Hosting;
using CIS.Metrics;
using Microsoft.Extensions.Logging;
using MetricTimer = CIS.Metrics.ITimer;

namespace CIS.Jobs;

internal sealed class JobInstrumentation : IJobRuntimeObserver, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly JobRuntime runtime;
    private readonly MetricsAttachment attachment = new();
    private readonly ObservationRefresh refresh;
    private readonly ObservationLogBudget rejected;
    private readonly IReadOnlyDictionary<(string Scope, string Job), ObservationLogBudget> failures;
    private readonly ILogger? logger;
    private JobMetrics? metrics;
    private IConditionMonitor? health;
    private bool activated;
    private bool stopping;
    private Task healthCreation = Task.CompletedTask;

    internal JobInstrumentation(JobsComposition composition, JobRuntime runtime, TimeProvider? clock = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(runtime);
        RegistrationIdentity = composition.RegistrationIdentity;
        this.runtime = runtime;
        TimeProvider time = clock ?? TimeProvider.System;
        rejected = new(time);
        failures = runtime.Catalog.Bindings.Keys.ToDictionary(key => key, _ => new ObservationLogBudget(time));
        refresh = new(RefreshCore);
        try
        {
            logger = loggerFactory?.CreateLogger("CIS.Jobs");
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }

        runtime.AttachObserver(this);
    }

    internal object RegistrationIdentity
    {
        get;
    }

    internal void AttachMetrics(MetricsRuntime owner)
    {
        if (attachment.Attach(owner))
        {
            Volatile.Write(ref metrics, new JobMetrics(owner, runtime));
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
            health = service?.CreateCondition(new()
            {
                Name = "Required jobs",
                IsCritical = isCritical,
                InitiallyFlagged = false
            });
            RefreshHealth();
        }
        finally
        {
            creation.TrySetResult();
        }
    }

    internal void RefreshHealth() => refresh.Request();

    public IJobRuntimeObserver CaptureInvocationObserver() => new CapturedInvocation(this, Volatile.Read(ref metrics));

    public void InvocationStarted(JobExecutionMode mode) => refresh.Request();

    public void InvocationCompleted(JobExecutionMode mode, string outcome, double? durationSeconds)
        => CompleteInvocation(Volatile.Read(ref metrics), mode, outcome, durationSeconds);

    private void CompleteInvocation(JobMetrics? captured, JobExecutionMode mode, string outcome, double? durationSeconds)
    {
        if (durationSeconds is { } seconds)
        {
            Attempt(() => captured?.Invocation(mode, outcome, seconds));
        }

        refresh.Request();
    }

    public void Transition(JobRuntimeSnapshot observation)
    {
        Attempt(() =>
        {
            bool failed = observation.State is JobLocalState.Faulted or JobLocalState.Lost;
            if (logger is null || observation.State is JobLocalState.Running or JobLocalState.Idle or JobLocalState.Standby or JobLocalState.Waiting
                || failed && (!failures.TryGetValue((observation.Target.ExecutionScope, observation.Target.JobId), out ObservationLogBudget? budget)
                    || !budget.TryTake()))
            {
                return;
            }

            LogLevel level = failed ? LogLevel.Warning : LogLevel.Debug;
            if (logger.IsEnabled(level))
            {
                logger.Log(level, new EventId(19100, "jobs.transition"),
                    "Job {Scope}/{Job}/{Resource} in {Mode} became {State}; revision {Revision}, outcome {Outcome}.",
                    observation.Target.ExecutionScope, observation.Target.JobId, observation.Target.ResourceId,
                    observation.Target.Mode, observation.State, observation.ConfigurationRevision, observation.LastOutcome);
            }
        });
        refresh.Request();
    }

    public void ConfigurationRejected(JobConfigurationRejection rejection)
    {
        Attempt(() =>
        {
            if (logger is not null && rejected.TryTake())
            {
                logger.LogWarning(new EventId(19101, "jobs.configuration_rejected"),
                    "Job configuration revision {Revision} was rejected ({Reason}).", rejection.Revision, rejection.ReasonCode);
            }
        });
        refresh.Request();
    }

    public void ConfigurationChanged() => refresh.Request();

    private void RefreshCore()
    {
        bool unhealthy = runtime.CaptureHealth().Unhealthy
            || runtime.RejectedConfiguration is { } refusal && refusal.Revision > runtime.Configuration.Revision;
        if (unhealthy)
        {
            health?.Flag(new("jobs.required_duty_unhealthy", "An enabled duty faulted, an unfinished callback lost ownership, or the latest configuration was rejected."));
        }
        else
        {
            health?.Clear();
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

    private sealed class CapturedInvocation(JobInstrumentation owner, JobMetrics? captured) : IJobRuntimeObserver
    {
        public void InvocationStarted(JobExecutionMode mode) => owner.refresh.Request();
        public void InvocationCompleted(JobExecutionMode mode, string outcome, double? durationSeconds)
            => owner.CompleteInvocation(captured, mode, outcome, durationSeconds);
        public void Transition(JobRuntimeSnapshot observation)
        {
        }
    }
}

internal sealed class JobMetrics
{
    private readonly MetricTimer duration;

    internal JobMetrics(MetricsRuntime metrics, JobRuntime runtime)
    {
        duration = metrics.CreateTimer(new("cis.jobs.invocation.duration", MetricUnit.Seconds,
            "One completed callback through actual owned execution-scope cleanup.", ["mode", "outcome"]));
        metrics.RegisterObservableGauge(new("cis.jobs.active.invocations", description: "Actual unfinished callbacks including scope cleanup.",
            allowedDimensionKeys: ["mode"]), () =>
        {
            JobRuntimeStatus state = runtime.CaptureStatus();
            return [new(state.ActiveContinuousInvocations, new(("mode", "continuous"))),
                new(state.ActiveScheduledInvocations, new(("mode", "scheduled")))];
        });
        metrics.RegisterObservableGauge(new("cis.jobs.pending.dispatches", description: "Bounded locally pending scheduled dispatches."),
            () => [new(runtime.CaptureStatus().PendingDispatches)]);
    }

    internal void Invocation(JobExecutionMode mode, string outcome, double seconds)
        => duration.Record(seconds, new(("mode", mode == JobExecutionMode.Continuous ? "continuous" : "scheduled"), ("outcome", outcome)));
}
