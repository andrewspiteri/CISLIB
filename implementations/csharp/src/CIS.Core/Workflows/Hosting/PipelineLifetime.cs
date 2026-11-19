namespace CIS.Workflows.Hosting;

// A module-local admission count. The owner never retains or calls an execution.
internal sealed class PipelineLifetime : IPipelineLifetime, IAsyncDisposable
{
    private readonly object gate = new();
    private bool closed;
    private int active;
    private TaskCompletionSource? drained;

    public void Enter()
    {
        lock (gate)
        {
            if (closed)
            {
                throw new InvalidOperationException("Workflow admission is closed because the host is stopping.");
            }

            if (active == 0)
            {
                drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            active = checked(active + 1);
        }
    }

    public void Exit()
    {
        TaskCompletionSource? completion = null;
        lock (gate)
        {
            if (--active == 0)
            {
                completion = drained;
                drained = null;
            }
        }

        completion?.TrySetResult();
    }

    internal void BeginStopping()
    {
        lock (gate)
        {
            closed = true;
        }
    }

    internal Task StopAsync()
    {
        lock (gate)
        {
            closed = true;
            return active == 0 ? Task.CompletedTask : drained!.Task;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}

// Captures only an owner actually constructed by the one native provider. A late
// native Build failure must not require resolving services from that failed root.
internal sealed class PipelineHostingRegistration
{
    private readonly object gate = new();
    private PipelineLifetime? owner;
    private bool stopping;

    internal PipelineLifetime? Owner => Volatile.Read(ref owner);

    internal PipelineLifetime CreateOwner()
    {
        lock (gate)
        {
            var created = new PipelineLifetime();
            if (stopping)
            {
                created.BeginStopping();
            }

            Volatile.Write(ref owner, created);
            return created;
        }
    }

    internal void BeginStopping()
    {
        lock (gate)
        {
            stopping = true;
            owner?.BeginStopping();
        }
    }

    internal Task StopAsync()
    {
        lock (gate)
        {
            stopping = true;
            return owner?.StopAsync() ?? Task.CompletedTask;
        }
    }
}
