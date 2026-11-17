using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Heartbeat;

public sealed partial class HeartbeatRuntime : IMetricsSource
{
    private readonly MetricsAttachment metricsAttachment = new();
    private HeartbeatMetrics? metrics;
    internal HeartbeatMetrics? CurrentMetrics => Volatile.Read(ref metrics);

    void IMetricsSource.AttachMetrics(MetricsRuntime runtime)
    {
        if (!metricsAttachment.Attach(runtime)) { return; }
        var created = new HeartbeatMetrics(runtime);
        Volatile.Write(ref metrics, created);
    }
}

internal sealed class HeartbeatMetrics
{
    private readonly ITimer duration;
    private readonly IUpDownCounter active;

    internal HeartbeatMetrics(IMetrics metrics)
    {
        duration = metrics.CreateTimer(new("cis.heartbeat.execution.duration", MetricUnit.Seconds,
            "Started Heartbeat work through awaited recovery, cleanup and outcome reporting.", ["outcome"]));
        active = metrics.CreateUpDownCounter(new("cis.heartbeat.active.executions", description: "Started Heartbeat executions not yet settled."));
    }

    internal Operation? Start()
    {
        try
        {
            ITimingScope timing = duration.Start();
            active.Add(1);
            return new(this, timing);
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { return null; }
    }

    internal sealed class Operation(HeartbeatMetrics owner, ITimingScope timing)
    {
        internal void Complete(string outcome)
        {
            try { timing.Stop(new(("outcome", outcome))); }
            catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
            finally
            {
                try { owner.active.Add(-1); }
                catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
            }
        }
    }
}
