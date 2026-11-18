using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Workflows;

// One complete module-lifetime bundle; accepted executions capture its reference once.
internal sealed class PipelineMetrics
{
    private readonly ITimer executionDuration;
    private readonly IUpDownCounter active;
    private readonly ITimer stepDuration;

    internal PipelineMetrics(IMetrics metrics)
    {
        executionDuration = metrics.CreateTimer(new("cis.pipeline.execution.duration", MetricUnit.Seconds,
            "Accepted pipeline execution through activation, unwind and owned cleanup.", ["pipeline", "outcome"]));
        active = metrics.CreateUpDownCounter(new("cis.pipeline.active.executions",
            description: "Accepted pipeline executions not yet settled.", allowedDimensionKeys: ["pipeline"]));
        stepDuration = metrics.CreateTimer(new("cis.pipeline.step.duration", MetricUnit.Seconds,
            "One entered forward or error-handler method through its actual completion.", ["pipeline", "step", "phase", "outcome"]));
    }

    internal void Started(string name)
    {
        try
        {
            active.Add(1, new(("pipeline", name)));
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
            // Telemetry cannot change admission or execution.
        }
    }

    internal void PhaseCompleted(string name, string alias, PipelinePhase phase, PipelineStatus status, TimeSpan elapsed)
    {
        string? outcome = Outcome(status);
        if (outcome is null)
        {
            return;
        }

        try
        {
            stepDuration.Record(elapsed.TotalSeconds, new(("pipeline", name), ("step", alias),
                ("phase", phase == PipelinePhase.Execute ? "execute" : "error"), ("outcome", outcome)));
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
    }

    internal void Completed(string name, PipelineStatus status, TimeSpan? elapsed)
    {
        try
        {
            string? outcome = Outcome(status);
            if (outcome is not null && elapsed is { } measured)
            {
                executionDuration.Record(measured.TotalSeconds, new(("pipeline", name), ("outcome", outcome)));
            }
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
        finally
        {
            try
            {
                active.Add(-1, new(("pipeline", name)));
            }
            catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
            {
            }
        }
    }

    private static string? Outcome(PipelineStatus status) => status switch
    {
        PipelineStatus.Succeeded => "success",
        PipelineStatus.Failed => "error",
        PipelineStatus.Cancelled => "cancelled",
        _ => null,
    };
}
