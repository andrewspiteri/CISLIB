using CIS.Startup;
using CIS.TypeManagement;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Metrics.Examples.MetricApi;

// The application's real startup hold is used only by --self-test. Normal startup
// proceeds directly, without an external service or a simulated provider.
internal sealed class BootstrapGate(bool held)
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        if (held) { await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
    }
    internal void Release() => release.TrySetResult();
}

internal sealed class OrderService : IStartable
{
    private readonly BootstrapGate gate;
    private readonly ICounter completed;
    private readonly ITimer duration;
    internal bool Primed { get; private set; }
    internal ITypeManager Prepared { get; }

    public OrderService(BootstrapGate gate, ITypeManager prepared, IMetrics metrics)
    {
        this.gate = gate;
        Prepared = prepared;
        completed = metrics.CreateCounter(new("app.orders.completed", allowedDimensionKeys: ["outcome"]));
        duration = metrics.CreateTimer(new("app.orders.duration", MetricUnit.Seconds,
            allowedDimensionKeys: ["operation", "outcome"]));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Primed = true;
    }

    internal OrderReceipt Submit(string id)
    {
        using ITimingScope elapsed = duration.Start(new(("operation", "submit")));
        try
        {
            if (!Primed) { throw new InvalidOperationException("Required order initialization is incomplete."); }
            var receipt = new OrderReceipt(id, "accepted");
            completed.Add(1, new(("outcome", "success")));
            elapsed.Stop(new(("outcome", "success")));
            return receipt;
        }
        catch
        {
            elapsed.Stop(new(("outcome", "error")));
            throw;
        }
    }
}

internal sealed class OrderReader(OrderService orders) : IStartable
{
    internal bool Started { get; private set; }
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!orders.Primed) { throw new InvalidOperationException("Sequential priming did not finish before its reader."); }
        Started = true;
        return Task.CompletedTask;
    }
    internal OrderReceipt Submit(string id) => orders.Submit(id);
}

internal sealed record OrderReceipt(string Id, string Status);
