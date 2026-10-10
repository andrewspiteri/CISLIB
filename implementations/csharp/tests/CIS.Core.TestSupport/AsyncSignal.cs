namespace CIS.TestSupport;

/// <summary>A one-shot completion signal with a finite wait and asynchronous continuations.</summary>
public sealed class AsyncSignal
{
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsSet => completion.Task.IsCompletedSuccessfully;

    public void Set() => completion.TrySetResult();

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive finite deadline is required.");
        }

        return completion.Task.WaitAsync(timeout, cancellationToken);
    }
}

/// <summary>A one-shot barrier; every participant must arrive before any wait completes.</summary>
public sealed class AsyncBarrier
{
    private readonly AsyncSignal released = new();
    private int remaining;

    public AsyncBarrier(int participants)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(participants);
        remaining = participants;
    }

    public Task SignalAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Validate the deadline before consuming a participant slot.
        Task wait = released.WaitAsync(timeout, cancellationToken);
        int count = Interlocked.Decrement(ref remaining);
        if (count < 0)
        {
            throw new InvalidOperationException("The barrier has already received every participant.");
        }

        if (count == 0)
        {
            released.Set();
        }

        return wait;
    }
}
