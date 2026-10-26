namespace CIS.Healthcheck.Pipes;

/// <summary>One connection deadline; neither progress nor successive protocol stages reset it.</summary>
internal sealed class HealthPipeBudget : IDisposable
{
    private readonly TimeProvider clock;
    private readonly long started;
    private readonly TimeSpan timeout;
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource linked;

    internal HealthPipeBudget(TimeProvider clock, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        this.clock = clock;
        started = clock.GetTimestamp();
        timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
        deadline = new CancellationTokenSource(timeout, clock);
        linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
    }

    internal CancellationToken Token => linked.Token;

    internal void ThrowIfExpired()
    {
        Token.ThrowIfCancellationRequested();
        // A synchronous snapshot/format operation may occupy the thread while the
        // timer callback is delayed. It still cannot commit a late successful reply.
        if (clock.GetElapsedTime(started) >= timeout)
        {
            throw new OperationCanceledException("The Healthcheck pipe deadline expired.", Token);
        }
    }

    public void Dispose()
    {
        linked.Dispose();
        deadline.Dispose();
    }
}
