using CIS.Security;
using CIS.Transactions;

namespace CIS.Messaging;

internal interface IMessageRequestCoordinator
{
    Task<MessageRequestStage<TResponse>> StageRequestAsync<TRequest, TResponse>(ILocalTransactionContext context,
        MessageRequest<TRequest> request, CancellationToken cancellationToken);
    Task<MessageRequestResult<TResponse>> RequestAsync<TRequest, TResponse>(MessageRequest<TRequest> request,
        CancellationToken cancellationToken);
    Task<MessageRequestResult<TResponse>> WaitForReplyAsync<TResponse>(MessageRequestTicket<TResponse> ticket,
        CancellationToken cancellationToken);
}

internal sealed class MessageOutbox : IMessageOutbox
{
    private readonly object gate = new();
    private readonly MessagingSnapshot snapshot;
    private readonly IMessagingStore store;
    private readonly MessagingResourceBudget resources;
    private readonly LocalApplicationIdentity identity;
    private readonly TimeProvider clock;
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IMessageRequestCoordinator? requests;
    private bool closing;
    private int active;

    internal MessageOutbox(MessagingSnapshot snapshot, IMessagingStore store, MessagingResourceBudget resources,
        LocalApplicationIdentity identity, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(identity);
        snapshot.Validate();
        this.snapshot = snapshot;
        this.store = store;
        this.resources = resources;
        this.identity = identity;
        clock = timeProvider ?? TimeProvider.System;
        DatabaseName = snapshot.Stores.Single(item => item.Alias == snapshot.Settings.StoreAlias).DatabaseName;
    }

    internal string DatabaseName { get; }
    internal IMessageRequestCoordinator? Requests => Volatile.Read(ref requests);

    internal void BindRequests(IMessageRequestCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        lock (gate)
        {
            if (closing || active != 0 || requests is not null)
            {
                throw new InvalidOperationException("Bind request support once before messaging work starts.");
            }
            Volatile.Write(ref requests, coordinator);
        }
    }

    public Task<MessageStageResult> PublishAsync<T>(ILocalTransactionContext context, string routeAlias, T value,
        MessageSendOptions? options = null, CancellationToken cancellationToken = default)
        => StageAsync(context, routeAlias, MessageKind.Event, value, options, cancellationToken);

    public Task<MessageStageResult> SendAsync<T>(ILocalTransactionContext context, string routeAlias, T value,
        MessageSendOptions? options = null, CancellationToken cancellationToken = default)
        => StageAsync(context, routeAlias, MessageKind.Command, value, options, cancellationToken);

    public Task<MessageRequestStage<TResponse>> StageRequestAsync<TRequest, TResponse>(ILocalTransactionContext context,
        MessageRequest<TRequest> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Requests is { } coordinator
            ? coordinator.StageRequestAsync<TRequest, TResponse>(context, request, cancellationToken)
            : Task.FromResult(new MessageRequestStage<TResponse>(MessageStageDisposition.Unavailable,
                error: new("messaging.requests_unavailable", "Typed request support is not selected.")));
    }

    internal MessagePreparedStage? Prepare<T>(string routeAlias, MessageKind kind, T value, MessageSendOptions? options,
        CancellationToken cancellationToken, out MessageStageResult? failure)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (closing)
            {
                failure = new(MessageStageDisposition.Unavailable, error: new("messaging.stopping", "Messaging admission is closed."));
                return null;
            }
            active++;
        }
        IDisposable? bytes = null;
        try
        {
            if (!resources.TryAcquireBytes(checked(4 * snapshot.Settings.Limits.MaximumEnvelopeBytes), out bytes))
            {
                Exit();
                failure = new(MessageStageDisposition.Overloaded, error: new("messaging.buffer_capacity", "Messaging byte capacity is occupied."));
                return null;
            }
            MessageRouteDefinition<T> route = snapshot.GetRoute(routeAlias) as MessageRouteDefinition<T>
                ?? throw new ArgumentException("The value type must match the registered route codec.", nameof(value));
            if (route.Kind != kind || route.Codec.Contract != route.Contract)
            {
                throw new ArgumentException("The pattern or mutable codec contract disagrees with its captured route.", nameof(routeAlias));
            }
            options ??= new();
            if (options.Operation is { } operation && !snapshot.Requests.Any(item => item.RouteAlias == routeAlias
                && item.Mode == MessageRequestMode.DurableCommand && item.OperationName == operation.OperationName
                && item.RequestType == typeof(T)))
            {
                throw new ArgumentException("An operation reference requires its registered typed durable-command binding.", nameof(options));
            }
            byte[] payload = route.Codec.Encode(value) ?? throw new InvalidOperationException("The codec returned no owned JSON payload.");
            if (payload.Length > snapshot.Settings.Limits.MaximumPayloadBytes)
            {
                throw new ArgumentException("The encoded payload exceeds the selected limit.", nameof(value));
            }
            if (!route.Codec.TryDecode(route.Contract, payload, out _))
            {
                throw new InvalidOperationException("The codec cannot read its current encoded payload contract.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = new MessageEnvelope(Guid.NewGuid(), kind, route.Contract, identity.Application, identity.Environment,
                clock.GetUtcNow(), payload, null, null, null, options.Operation, options.CorrelationId, options.CausationId,
                options.Headers, validatedPayload: false);
            EncodedMessage message = MessageWire.Encode(envelope, snapshot.Settings.Limits);
            var record = new MessageOutgoingRecord(Guid.NewGuid(), identity.Application, identity.Environment,
                new(routeAlias), message);
            failure = null;
            return new(this, record, bytes!, Exit);
        }
        catch
        {
            bytes?.Dispose();
            Exit();
            throw;
        }
    }

    internal async Task<MessageStageReceipt> StagePreparedAsync(ILocalTransactionContext context, MessagePreparedStage prepared,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!ReferenceEquals(prepared.Owner, this) || prepared.IsDisposed)
            {
                throw new ArgumentException("The prepared stage belongs to this admitted outbox until disposal.", nameof(prepared));
            }
            ValidateContext(context);
            cancellationToken.ThrowIfCancellationRequested();
            MessageStageReceipt receipt = await store.StageAsync(context, prepared.Record, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The store returned no staging receipt.");
            if (receipt.RecordId != prepared.Record.RecordId || receipt.MessageId != prepared.Record.Message.Envelope.MessageId
                || receipt.DatabaseName != DatabaseName || receipt.Application != identity.Application
                || receipt.Environment != identity.Environment || !prepared.Record.Message.Digest.Equals(receipt.Digest))
            {
                throw new InvalidOperationException("The store returned a receipt for different immutable staged responsibility.");
            }
            return receipt;
        }
        catch
        {
            Poison(context, "messaging.stage_failed");
            throw;
        }
    }

    internal Task StopAsync()
    {
        lock (gate)
        {
            closing = true;
            if (active == 0)
            {
                drained.TrySetResult();
            }
            return drained.Task;
        }
    }

    internal static void Poison(ILocalTransactionContext? context, string code)
    {
        try
        {
            context?.MarkRollbackOnly(code);
        }
        catch
        {
            // Misused/disposed contexts cannot replace the original staging failure.
        }
    }

    private async Task<MessageStageResult> StageAsync<T>(ILocalTransactionContext context, string routeAlias, MessageKind kind,
        T value, MessageSendOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            ValidateContext(context);
            using MessagePreparedStage? prepared = Prepare(routeAlias, kind, value, options, cancellationToken, out MessageStageResult? failure);
            if (prepared is null)
            {
                Poison(context, "messaging.stage_refused");
                return failure!;
            }
            return new(MessageStageDisposition.Staged, await StagePreparedAsync(context, prepared, cancellationToken).ConfigureAwait(false));
        }
        catch (MessageStorageCapacityException)
        {
            Poison(context, "messaging.storage_capacity");
            return new(MessageStageDisposition.Overloaded,
                error: new("messaging.storage_capacity", "Configured durable messaging storage capacity is occupied."));
        }
        catch
        {
            Poison(context, cancellationToken.IsCancellationRequested ? "messaging.stage_cancelled" : "messaging.stage_failed");
            throw;
        }
    }

    private void ValidateContext(ILocalTransactionContext context)
    {
        if (context.DatabaseName != DatabaseName || context.Isolation != TransactionIsolation.ReadCommitted || context.IsRollbackOnly)
        {
            throw new InvalidOperationException("Staging needs the original active non-poisoned ReadCommitted database context.");
        }
    }

    private void Exit()
    {
        lock (gate)
        {
            active--;
            if (closing && active == 0)
            {
                drained.TrySetResult();
            }
        }
    }
}

internal sealed class MessagePreparedStage(MessageOutbox owner, MessageOutgoingRecord record, IDisposable bytes, Action release) : IDisposable
{
    private IDisposable? bytes = bytes;
    internal MessageOutbox Owner { get; } = owner;
    internal MessageOutgoingRecord Record { get; } = record;
    internal bool IsDisposed => Volatile.Read(ref bytes) is null;

    public void Dispose()
    {
        IDisposable? held = Interlocked.Exchange(ref bytes, null);
        if (held is not null)
        {
            held.Dispose();
            release();
        }
    }
}
