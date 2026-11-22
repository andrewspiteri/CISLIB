namespace CIS.Coordination;

// One coalesced library-owned refresh, not a queue, timer or retained observation history.
internal sealed class ObservationRefresh(Action refresh) : IAsyncDisposable
{
    private readonly object gate = new();
    private Task worker = Task.CompletedTask;
    private bool dirty;
    private bool running;
    private bool stopping;

    internal void Request()
    {
        lock (gate)
        {
            if (stopping)
            {
                return;
            }

            dirty = true;
            if (!running)
            {
                running = true;
                worker = Task.Run(Run, CancellationToken.None);
            }
        }
    }

    private void Run()
    {
        while (true)
        {
            lock (gate)
            {
                if (stopping || !dirty)
                {
                    running = false;
                    return;
                }

                dirty = false;
            }

            try
            {
                refresh();
            }
            catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error))
            {
                // Optional diagnostics cannot change an operation or prevent owner settlement.
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            stopping = true;
            dirty = false;
            return new(worker);
        }
    }
}

internal sealed class ObservationLogBudget(TimeProvider time)
{
    private readonly object gate = new();
    private int remaining = 8;
    private long? last;

    internal bool TryTake()
    {
        lock (gate)
        {
            long now = time.GetTimestamp();
            if (remaining == 0 || last is { } previous && time.GetElapsedTime(previous, now) < TimeSpan.FromSeconds(30))
            {
                return false;
            }

            remaining--;
            last = now;
            return true;
        }
    }
}
