using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Workflows;

// One complete module/provider bundle; all machine instances reuse these handles.
internal sealed class StateMachineMetrics
{
    private readonly ITimer triggerDuration;
    private readonly IUpDownCounter active;
    private readonly ITimer handlerDuration;

    internal StateMachineMetrics(IMetrics metrics)
    {
        triggerDuration = metrics.CreateTimer(new("cis.statemachine.trigger.duration", MetricUnit.Seconds,
            "One public trigger call through its final outcome and actual cleanup.", ["machine", "outcome", "source", "trigger"]));
        active = metrics.CreateUpDownCounter(new("cis.statemachine.active.attempts",
            description: "State-machine instance gates held through work and cleanup.", allowedDimensionKeys: ["machine"]));
        handlerDuration = metrics.CreateTimer(new("cis.statemachine.handler.duration", MetricUnit.Seconds,
            "One entered handler Execute or Error method through actual settlement.", ["handler", "machine", "outcome", "phase"]));
    }

    internal void GateAcquired(string machine)
    {
        try
        {
            active.Add(1, new(("machine", machine)));
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
    }

    internal void PhaseCompleted(string machine, string handler, PipelinePhase phase, PipelineStatus status, TimeSpan? duration)
    {
        if (duration is not { } elapsed)
        {
            return;
        }

        string? outcome = status switch
        {
            PipelineStatus.Succeeded => "success",
            PipelineStatus.Failed => "error",
            PipelineStatus.Cancelled => "cancelled",
            _ => null,
        };
        if (outcome is null)
        {
            return;
        }

        try
        {
            handlerDuration.Record(elapsed.TotalSeconds, new(("machine", machine), ("handler", handler),
                ("phase", phase == PipelinePhase.Execute ? "execute" : "error"), ("outcome", outcome)));
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
    }

    internal void Completed(StateMachineCallInfo info, StateMachineCompletion completion, bool admitted)
    {
        try
        {
            string? outcome = completion.Outcome switch
            {
                StateTransitionOutcome.Succeeded => "success",
                StateTransitionOutcome.Rejected => "rejected",
                StateTransitionOutcome.Busy => "busy",
                StateTransitionOutcome.Failed => "error",
                StateTransitionOutcome.Cancelled => "cancelled",
                _ => null,
            };
            if (outcome is not null && completion.Duration is { } elapsed)
            {
                triggerDuration.Record(elapsed.TotalSeconds, new(("machine", info.Name), ("source", info.Source),
                    ("trigger", info.Trigger), ("outcome", outcome)));
            }
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
        finally
        {
            if (admitted)
            {
                try
                {
                    active.Add(-1, new(("machine", info.Name)));
                }
                catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
                {
                }
            }
        }
    }
}
