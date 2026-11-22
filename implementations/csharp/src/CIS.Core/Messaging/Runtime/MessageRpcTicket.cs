namespace CIS.Messaging;

internal interface IMessageLocalWaiter
{
    MessageRequestReference Reference { get; }
    MessageRequestDefinition Definition { get; }
    MessageReplyHandling Receive(EncodedMessage reply);
    void Cancel();
    Task Cleanup { get; }
}

internal sealed record MessageReplyCompletion<T>(MessageExchangeOutcome Outcome, MessageResponse<T>? Response = null);

// The dictionary owns a finite live ticket; after cleanup only application-owned ticket/reference data remains.
internal sealed class MessageRpcTicket<T> : MessageRequestTicket<T>, IMessageLocalWaiter
{
    private readonly object gate = new();
    private readonly MessageRequestCoordinator owner;
    private readonly IMessageCodec<T> codec;
    private readonly MessagingResourceBudget resources;
    private readonly MessagingLimits limits;
    private readonly IDisposable requestLease;
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource local = new();
    private readonly CancellationToken deadlineToken;
    private readonly CancellationToken localToken;
    private readonly CancellationTokenRegistration expiry;
    private readonly TaskCompletionSource stageDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource notification = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MessageObservation? observation;
    private readonly TaskCompletionSource cleaned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IDisposable? responseLease;
    private EncodedMessage? retainedWire;
    private MessageReplyCompletion<T>? received;
    private MessageExchangeOutcome? terminalOutcome;
    private MessageExchangeOutcome? cancellationOutcome;
    private bool closed;
    private bool waiting;

    internal MessageRpcTicket(MessageRequestCoordinator owner, MessageRequestReference reference,
        MessageRequestDefinition definition, IMessageCodec<T> codec, MessagingResourceBudget resources,
        MessagingLimits limits, TimeProvider clock, long entry, TimeSpan timeout, IDisposable requestLease,
        MessageObservation? observation) : base(reference)
    {
        this.owner = owner;
        Definition = definition;
        this.codec = codec;
        this.resources = resources;
        this.limits = limits;
        this.requestLease = requestLease;
        this.observation = observation;
        TimeSpan remaining = timeout - clock.GetElapsedTime(entry, clock.GetTimestamp());
        deadline = new(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, clock);
        deadlineToken = deadline.Token;
        localToken = local.Token;
        expiry = deadlineToken.UnsafeRegister(static state => ((MessageRpcTicket<T>)state!).Expire(), this);
        if (remaining <= TimeSpan.Zero)
        {
            Expire();
        }
    }

    public MessageRequestDefinition Definition { get; }
    internal MessageRequestCoordinator Owner => owner;
    internal CancellationToken DeadlineToken => deadlineToken;
    internal CancellationToken LocalToken => localToken;
    internal bool HasRetainedResponse
    {
        get
        {
            lock (gate)
            {
                return retainedWire is not null;
            }
        }
    }
    public Task Cleanup => cleaned.Task;
    internal void StagingSettled() => stageDone.TrySetResult();

    internal bool BeginWait()
    {
        lock (gate)
        {
            if (waiting)
            {
                throw new InvalidOperationException("Await one reply wait at a time for a provisional ticket.");
            }
            waiting = true;
            return !closed;
        }
    }

    internal void EndWait()
    {
        lock (gate)
        {
            waiting = false;
        }
    }

    internal MessageExchangeOutcome CancellationOutcome
    {
        get
        {
            lock (gate)
            {
                return cancellationOutcome ?? (deadlineToken.IsCancellationRequested ? MessageExchangeOutcome.TimedOut : MessageExchangeOutcome.Cancelled);
            }
        }
    }

    public MessageReplyHandling Receive(EncodedMessage reply)
    {
        lock (gate)
        {
            if (closed || received is not null)
            {
                return MessageReplyHandling.Acknowledge;
            }
        }
        MessageReplyDecode decoded = MessageReplyProtocol.TryDecode(reply, Reference, Definition, codec, limits, out MessageResponse<T>? response);
        if (decoded == MessageReplyDecode.Unmatched)
        {
            return MessageReplyHandling.Quarantine;
        }
        MessageExchangeOutcome outcome = decoded == MessageReplyDecode.Valid ? MessageExchangeOutcome.ResponseReceived : MessageExchangeOutcome.InvalidResponse;
        IDisposable? encoded = null;
        if (decoded == MessageReplyDecode.Valid && !resources.TryAcquireBytes(checked(2 * reply.Length), out encoded))
        {
            outcome = MessageExchangeOutcome.Overloaded;
            response = null;
        }
        lock (gate)
        {
            if (closed || received is not null)
            {
                encoded?.Dispose();
                return MessageReplyHandling.Acknowledge;
            }
            responseLease = encoded;
            retainedWire = encoded is null ? null : reply;
            received = new(outcome, response);
            notification.TrySetResult();
        }
        return MessageReplyHandling.Acknowledge;
    }

    internal Task WaitAsync(CancellationToken cancellationToken) => notification.Task.WaitAsync(cancellationToken);

    internal MessageReplyCompletion<T> TakeResponse()
    {
        lock (gate)
        {
            MessageReplyCompletion<T> value = received ?? new(terminalOutcome ?? MessageExchangeOutcome.Cancelled);
            Close(value.Outcome, cancel: false);
            return value;
        }
    }

    internal void Consume(MessageExchangeOutcome outcome) => Close(outcome, cancel: false);
    public void Cancel() => Close(MessageExchangeOutcome.Cancelled, cancel: true);

    private void Expire()
    {
        Close(MessageExchangeOutcome.TimedOut, cancel: true);
    }

    private void Close(MessageExchangeOutcome outcome, bool cancel)
    {
        IDisposable? encoded;
        lock (gate)
        {
            if (closed)
            {
                return;
            }
            closed = true;
            if (outcome is MessageExchangeOutcome.Cancelled or MessageExchangeOutcome.TimedOut)
            {
                cancellationOutcome = outcome;
            }
            encoded = responseLease;
            responseLease = null;
            retainedWire = null;
            received = null;
            terminalOutcome = outcome;
            notification.TrySetResult();
        }
        encoded?.Dispose();
        Task cancellation = cancel ? local.CancelAsync() : Task.CompletedTask;
        // One tracked cleanup per admitted ticket; custom disposal cannot block the host's admission cutoff.
        _ = Task.Run(() => CleanupAsync(cancellation, outcome));
    }

    private async Task CleanupAsync(Task cancellation, MessageExchangeOutcome outcome)
    {
        Exception? fault = null;
        try
        {
            try
            {
                await cancellation.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                fault = error;
            }
            await stageDone.Task.ConfigureAwait(false);
            await expiry.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            fault ??= error;
        }
        try
        {
            deadline.Dispose();
        }
        catch (Exception error)
        {
            fault ??= error;
        }
        try
        {
            local.Dispose();
        }
        catch (Exception error)
        {
            fault ??= error;
        }
        try
        {
            observation?.Complete(MessageRequestCoordinator.OutcomeName(outcome));
            observation?.Dispose();
        }
        catch (Exception error)
        {
            fault ??= error;
        }
        try
        {
            requestLease.Dispose();
        }
        catch (Exception error)
        {
            fault ??= error;
        }
        if (fault is null)
        {
            cleaned.TrySetResult();
        }
        else
        {
            cleaned.TrySetException(fault);
        }
        owner.Remove(this, fault);
    }

    public override async ValueTask DisposeAsync()
    {
        Cancel();
        await Cleanup.ConfigureAwait(false);
    }
}
