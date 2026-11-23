namespace CIS.Management.Hosting;

// Passive metadata for this one known owner, including factory construction during failed Build cleanup.
internal sealed class ManagementRegistrationLifetime
{
    private readonly object gate = new();
    private TaskCompletionSource construction = Completed();
    private ManagementRegistration? runtime;
    private Task? stop;
    private int constructors;
    private bool closing;

    internal IDisposable EnterConstruction()
    {
        lock (gate)
        {
            if (closing)
            {
                throw new InvalidOperationException("Management registration admission is closed before construction.");
            }
            if (constructors++ == 0)
            {
                construction = Completion();
            }
        }
        return new Construction(this);
    }

    internal void Bind(ManagementRegistration value)
    {
        bool close;
        lock (gate)
        {
            if (runtime is not null && !ReferenceEquals(runtime, value))
            {
                throw new InvalidOperationException("Select one complete management registration owner.");
            }
            runtime = value;
            close = closing;
        }
        if (close)
        {
            value.BeginStopping();
        }
    }

    internal void BeginStopping()
    {
        ManagementRegistration? current;
        lock (gate)
        {
            closing = true;
            current = runtime;
        }
        current?.BeginStopping();
    }

    // Call only after native REST cutoff; cancellation is a signal, never a settlement certificate.
    internal Task StopAsync()
    {
        BeginStopping();
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (stop is null)
            {
                completion = Completion();
                stop = completion.Task;
            }
            operation = stop;
        }
        if (completion is not null)
        {
            _ = StopCoreAsync(completion);
        }
        return operation;
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            Task pending;
            lock (gate)
            {
                pending = construction.Task;
            }
            await pending.ConfigureAwait(false);
            ManagementRegistration? current;
            lock (gate)
            {
                current = runtime;
            }
            if (current is not null)
            {
                await current.StopAsync().ConfigureAwait(false);
            }
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private void ExitConstruction()
    {
        lock (gate)
        {
            if (--constructors == 0)
            {
                construction.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource Completed()
    {
        TaskCompletionSource result = Completion();
        result.SetResult();
        return result;
    }

    private sealed class Construction(ManagementRegistrationLifetime owner) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.ExitConstruction();
            }
        }
    }
}
