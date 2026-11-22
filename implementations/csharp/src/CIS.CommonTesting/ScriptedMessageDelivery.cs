using CIS.Messaging;

namespace CIS.CommonTesting;

/// <summary>The one selected settlement operation for a scripted delivery.</summary>
public enum ScriptedMessageSettlement
{
    /// <summary>A scripted acknowledgement.</summary>
    Acknowledge,
    /// <summary>A scripted requeue without success.</summary>
    Requeue,
    /// <summary>A scripted safe-quarantine decision.</summary>
    Quarantine
}

/// <summary>Controls one actual owned scripted settlement without a timer.</summary>
public sealed class PendingMessageSettlement
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool observeCancellation;
    internal PendingMessageSettlement(bool observeCancellation) => this.observeCancellation = observeCancellation;
    /// <summary>Completes when the selected operation starts.</summary>
    public Task Entered => entered.Task;
    /// <summary>Releases actual work; disposal and unrelated canceled waits do not release it.</summary>
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

/// <summary>Owns one bounded test body, metadata snapshot and explicitly scripted settlement through the public delivery port.</summary>
/// <remarks>Native evidence is always absent: this helper never mints a trusted ticket, caller or authentication result.
/// One settlement attempt is admitted. Repeating the same operation joins it; a different operation fails.
/// False quarantine, cancellation and faults leave CompletedSettlement absent. Disposal joins actual work without replay.</remarks>
public sealed class ScriptedMessageDelivery : IMessageDelivery
{
    private readonly object gate = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private byte[] body;
    private Step? script;
    private Task<bool>? settlement;
    private ScriptedMessageSettlement? attempted;
    private ScriptedMessageSettlement? completed;
    private string? settlementCode;
    private string? stopCode;
    private int stopRequests;
    private bool active;
    private bool closing;

    /// <summary>Copies the body only after checking its selected finite byte bound; malformed and empty bodies remain valid test input.</summary>
    /// <remarks>Choose an explicit larger supported limit to test input oversized relative to a consumer's smaller selected limit.</remarks>
    public ScriptedMessageDelivery(ReadOnlyMemory<byte> body, string subscriptionAlias, MessageDeliveryMetadata metadata,
        MessagingLimits? limits = null)
    {
        MessagingLimits selected = limits ?? new();
        if (body.Length > selected.MaximumEnvelopeBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(body));
        }
        SubscriptionAlias = new MessageDestination(subscriptionAlias).RouteAlias;
        ArgumentNullException.ThrowIfNull(metadata);
        // Claimed metadata may intentionally be invalid, but cannot introduce unbounded test records.
        Bounded(metadata.RouteAlias, nameof(metadata.RouteAlias));
        if (metadata.TargetSubscription is { } target)
        {
            Bounded(target, nameof(metadata.TargetSubscription));
        }
        if (metadata.SafeCode is { } code)
        {
            Bounded(code, nameof(metadata.SafeCode));
        }
        Metadata = metadata with
        {
        };
        this.body = body.ToArray();
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Body
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(closing, this);
                return body;
            }
        }
    }
    /// <inheritdoc />
    public string SubscriptionAlias
    {
        get;
    }
    /// <inheritdoc />
    public MessageDeliveryMetadata Metadata
    {
        get;
    }
    /// <summary>Gets the selected attempt, which does not itself claim native settlement.</summary>
    public ScriptedMessageSettlement? AttemptedSettlement
    {
        get
        {
            lock (gate)
            {
                return attempted;
            }
        }
    }
    /// <summary>Gets the successfully completed script operation, or absent when unacknowledged.</summary>
    public ScriptedMessageSettlement? CompletedSettlement
    {
        get
        {
            lock (gate)
            {
                return completed;
            }
        }
    }
    /// <summary>Gets the first bounded visible consumer-stop reason, without a history or join.</summary>
    public string? ConsumerStopCode
    {
        get
        {
            lock (gate)
            {
                return stopCode;
            }
        }
    }
    /// <summary>Gets the saturating stop-request count; repeated requests never perform settlement.</summary>
    public int ConsumerStopRequests
    {
        get
        {
            lock (gate)
            {
                return stopRequests;
            }
        }
    }

    /// <summary>Scripts the one successful acknowledgement.</summary>
    public void EnqueueAcknowledge() => Add(new(ScriptedMessageSettlement.Acknowledge, true, null, null));
    /// <summary>Scripts the one successful requeue.</summary>
    public void EnqueueRequeue() => Add(new(ScriptedMessageSettlement.Requeue, true, null, null));
    /// <summary>Scripts a quarantine decision; false never acknowledges or requeues.</summary>
    public void EnqueueQuarantine(bool responsibilitySecured) => Add(new(ScriptedMessageSettlement.Quarantine, responsibilitySecured, null, null));
    /// <summary>Scripts an original programming fault, including an OCE that must remain a faulted task.</summary>
    public void EnqueueException(ScriptedMessageSettlement operation, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(operation, false, exception, null));
    }
    /// <summary>Scripts one held settlement. The quarantine result is used only for Quarantine.</summary>
    public PendingMessageSettlement EnqueuePending(ScriptedMessageSettlement operation,
        bool quarantineResponsibilitySecured = false, bool observeCancellation = true)
    {
        var pending = new PendingMessageSettlement(observeCancellation);
        Add(new(operation, operation != ScriptedMessageSettlement.Quarantine || quarantineResponsibilitySecured, null, pending));
        return pending;
    }

    private void Add(Step value)
    {
        if (!Enum.IsDefined(value.Operation))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (script is not null || settlement is not null)
            {
                throw new InvalidOperationException("One delivery permits exactly one scripted settlement attempt.");
            }
            script = value;
        }
    }

    /// <inheritdoc />
    public ValueTask<MessageNativeEvidence?> GetNativeEvidenceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            return ValueTask.FromResult<MessageNativeEvidence?>(null);
        }
    }
    /// <inheritdoc />
    public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
        => SettleAsync(ScriptedMessageSettlement.Acknowledge, null, cancellationToken);
    /// <inheritdoc />
    public Task RequeueAsync(CancellationToken cancellationToken = default)
        => SettleAsync(ScriptedMessageSettlement.Requeue, null, cancellationToken);
    /// <inheritdoc />
    public Task<bool> QuarantineMalformedAsync(string safeCode, CancellationToken cancellationToken = default)
        => SettleAsync(ScriptedMessageSettlement.Quarantine, Bounded(safeCode, nameof(safeCode)), cancellationToken);

    private Task<bool> SettleAsync(ScriptedMessageSettlement operation, string? safeCode, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(token);
        }
        Step selected;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (closing)
            {
                return Task.FromException<bool>(new ObjectDisposedException(nameof(ScriptedMessageDelivery)));
            }
            if (settlement is not null)
            {
                return attempted == operation && settlementCode == safeCode ? settlement
                    : Task.FromException<bool>(new InvalidOperationException("Another settlement operation already owns this delivery."));
            }
            if (script is null || script.Operation != operation)
            {
                return Task.FromException<bool>(new InvalidOperationException("The requested delivery settlement is not scripted."));
            }
            selected = script;
            script = null;
            attempted = operation;
            settlementCode = safeCode;
            settlement = completion.Task;
            active = true;
        }
        _ = ExecuteAsync(completion, selected, token);
        return completion.Task;
    }

    private async Task ExecuteAsync(TaskCompletionSource<bool> completion, Step selected, CancellationToken token)
    {
        Exception? error = selected.Fault;
        bool canceled = false;
        if (error is null)
        {
            try
            {
                if (selected.Pending is { } pending)
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
            active = false;
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
                completed = selected.Result ? selected.Operation : null;
                completion.TrySetResult(selected.Result);
            }
            FinishDisposal();
        }
    }

    /// <inheritdoc />
    public void RequestConsumerStop(string safeCode)
    {
        Bounded(safeCode, nameof(safeCode));
        lock (gate)
        {
            stopCode ??= safeCode;
            if (stopRequests < int.MaxValue)
            {
                stopRequests++;
            }
        }
    }

    /// <summary>Closes admission and joins the selected actual operation; it neither settles nor releases held work.</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closing = true;
            script = null;
            FinishDisposal();
            return new(drained.Task);
        }
    }

    private void FinishDisposal()
    {
        if (closing && !active)
        {
            body = [];
            drained.TrySetResult();
        }
    }

    private static string Bounded(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length > 128)
        {
            throw new ArgumentException("Test delivery metadata is bounded to 128 characters per field.", name);
        }
        return value;
    }

    private sealed record Step(ScriptedMessageSettlement Operation, bool Result, Exception? Fault, PendingMessageSettlement? Pending);
}
