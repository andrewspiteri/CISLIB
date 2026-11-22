using CIS.Messaging;

namespace CIS.CommonTesting;

/// <summary>Bounded publication metadata, without retained payloads, native handles or authentication evidence.</summary>
public sealed record ScriptedMessagePublication(Guid RecordId, Guid MessageId, string RouteAlias,
    MessageRecordPurpose Purpose, int DeliveryOrdinal, int Generation, string? TargetSubscription,
    int EncodedLength, MessageDigest Digest);

/// <summary>Controls one owned scripted publication without a timer or native broker.</summary>
public sealed class PendingMessagePublication
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool observeCancellation;

    internal PendingMessagePublication(bool observeCancellation) => this.observeCancellation = observeCancellation;
    /// <summary>Completes when the scripted operation actually starts.</summary>
    public Task Entered => entered.Task;
    /// <summary>Releases owned work; cancellation or disposal does not release an ignoring script.</summary>
    public void Release() => release.TrySetResult();

    internal async Task WaitAsync(CancellationToken token)
    {
        entered.TrySetResult();
        if (observeCancellation)
        {
            await release.Task.WaitAsync(token).ConfigureAwait(false);
        }
        else
        {
            await release.Task.ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
    }
}

/// <summary>Supplies explicit bounded publication facts through the public publisher port.</summary>
/// <remarks>Scripted Confirmed/Rejected/Unknown values prove no broker acceptance, SQL atomicity or authentication.
/// Disposal closes admission and joins actual started scripts; it does not release held work.</remarks>
public sealed class ScriptedMessagePublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Queue<Step> scripts = new();
    private readonly List<ScriptedMessagePublication> calls = [];
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int scriptCapacity;
    private readonly int recordingCapacity;
    private int active;
    private bool closing;

    /// <summary>Bounds queued scripts and retained metadata independently; create a fresh helper when recordings are full.</summary>
    public ScriptedMessagePublisher(int scriptCapacity = 16, int recordingCapacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scriptCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scriptCapacity, 65_536);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordingCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordingCapacity, 65_536);
        this.scriptCapacity = scriptCapacity;
        this.recordingCapacity = recordingCapacity;
    }

    /// <summary>Gets a detached finite metadata snapshot; message bodies are never recorded.</summary>
    public IReadOnlyList<ScriptedMessagePublication> Calls
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(calls.ToArray());
            }
        }
    }

    /// <summary>Gets the number of scripts not yet admitted.</summary>
    public int PendingScripts
    {
        get
        {
            lock (gate)
            {
                return scripts.Count;
            }
        }
    }

    /// <summary>Queues one exact immutable acceptance result.</summary>
    public void EnqueueResult(MessagePublicationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Add(new(result, null, null));
    }

    /// <summary>Queues an original programming fault; a scripted OCE remains faulted rather than becoming cancellation.</summary>
    public void EnqueueException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(null, exception, null));
    }

    /// <summary>Queues held work followed by an exact result. Ignoring scripts settle only after Release.</summary>
    public PendingMessagePublication EnqueuePendingResult(MessagePublicationResult result, bool observeCancellation = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        var pending = new PendingMessagePublication(observeCancellation);
        Add(new(result, null, pending));
        return pending;
    }

    private void Add(Step step)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (scripts.Count == scriptCapacity)
            {
                throw new InvalidOperationException("The configured publication script capacity has been reached.");
            }
            scripts.Enqueue(step);
        }
    }

    /// <inheritdoc />
    public Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<MessagePublicationResult>(cancellationToken);
        }
        Step step;
        var completion = new TaskCompletionSource<MessagePublicationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (closing)
            {
                return Task.FromException<MessagePublicationResult>(new ObjectDisposedException(nameof(ScriptedMessagePublisher)));
            }
            if (scripts.Count == 0 || calls.Count == recordingCapacity)
            {
                return Task.FromException<MessagePublicationResult>(new InvalidOperationException("A script and recording slot are required before publication admission."));
            }
            step = scripts.Dequeue();
            calls.Add(new(publication.OutboxRecordId, publication.Message.Envelope.MessageId, publication.Destination.RouteAlias,
                publication.Purpose, publication.DeliveryOrdinal, publication.Generation, publication.TargetSubscription,
                publication.Message.Length, publication.Message.Digest));
            active++;
        }
        _ = ExecuteAsync(completion, step, cancellationToken);
        return completion.Task;
    }

    private async Task ExecuteAsync(TaskCompletionSource<MessagePublicationResult> completion, Step step, CancellationToken token)
    {
        Exception? error = step.Fault;
        bool canceled = false;
        if (error is null)
        {
            try
            {
                if (step.Pending is { } pending)
                {
                    await pending.WaitAsync(token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException fault) when (token.IsCancellationRequested && fault.CancellationToken == token)
            {
                canceled = true;
            }
            catch (Exception fault)
            {
                error = fault;
            }
        }
        lock (gate)
        {
            active--;
            if (error is not null)
            {
                completion.TrySetException(error);
            }
            else if (canceled)
            {
                completion.TrySetCanceled(token);
            }
            else
            {
                completion.TrySetResult(step.Result!);
            }
            if (closing && active == 0)
            {
                drained.TrySetResult();
            }
        }
    }

    /// <summary>Closes script/call admission and joins all started work; queued scripts are released without execution.</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closing = true;
            scripts.Clear();
            if (active == 0)
            {
                drained.TrySetResult();
            }
            return new(drained.Task);
        }
    }

    private sealed record Step(MessagePublicationResult? Result, Exception? Fault, PendingMessagePublication? Pending);
}
