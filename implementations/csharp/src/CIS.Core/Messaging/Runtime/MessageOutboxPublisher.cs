using System.Runtime.ExceptionServices;
using CIS.Security;
using CIS.Transactions;

namespace CIS.Messaging;

internal sealed record MessagePublicationAttempt(Guid RecordId, bool Published, bool Deferred, bool Stale,
    MessagePublicationResult? Result = null, TransactionCompletion? ReadCompletion = null,
    TransactionCompletion? SettlementCompletion = null, bool UpdatePrepared = false);

internal sealed record MessagePumpResult(int Reserved, TransactionCompletion? ReservationCompletion,
    IReadOnlyList<MessagePublicationAttempt> Attempts, bool Unavailable = false)
{
    internal int Attempted => Attempts.Count(item => item.Published);
    internal int Confirmed => Attempts.Count(item => item.Published && item.Result?.Disposition == MessagePublicationDisposition.Confirmed);
    internal int Rejected => Attempts.Count(item => item.Published && item.Result?.Disposition == MessagePublicationDisposition.Rejected);
    internal int Unknown => Attempts.Count(item => item.Published && item.Result?.Disposition == MessagePublicationDisposition.Unknown);
}

// A bounded publication operation, not a worker framework. Hosting invokes it and owns the poll loop.
internal sealed class MessageOutboxPublisher : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly MessagingSnapshot snapshot;
    private readonly IMessagingStore store;
    private readonly ILocalTransactionFactory transactions;
    private readonly IMessagePublisher publisher;
    private readonly MessagingResourceBudget resources;
    private readonly LocalApplicationIdentity identity;
    private readonly TimeProvider clock;
    private readonly MessagingInstrumentation? instrumentation;
    private readonly string databaseName;
    private readonly Guid reservationOwner = Guid.NewGuid();
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closing;
    private bool pumping;
    private bool waiting;
    private bool disposed;
    private int active;
    private Task? stopTask;

    internal MessageOutboxPublisher(MessagingSnapshot snapshot, IMessagingStore store, ILocalTransactionFactory transactions,
        IMessagePublisher publisher, MessagingResourceBudget resources, LocalApplicationIdentity identity, TimeProvider? timeProvider = null,
        MessagingInstrumentation? instrumentation = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(identity);
        snapshot.Validate();
        if (instrumentation is not null && !ReferenceEquals(instrumentation.RegistrationIdentity, snapshot.RegistrationIdentity))
        {
            throw new ArgumentException("Use the runtime's exact messaging observation snapshot.", nameof(instrumentation));
        }
        this.snapshot = snapshot;
        this.store = store;
        this.transactions = transactions;
        this.publisher = publisher;
        this.resources = resources;
        this.identity = identity;
        clock = timeProvider ?? TimeProvider.System;
        this.instrumentation = instrumentation;
        databaseName = snapshot.Stores.Single(item => item.Alias == snapshot.Settings.StoreAlias).DatabaseName;
    }

    internal async Task<MessagePumpResult> PumpAsync(CancellationToken cancellationToken = default)
    {
        if (!Enter(isPump: true))
        {
            return new(0, null, [], Unavailable: true);
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
            return await PumpCoreAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            Exit(isPump: true);
        }
    }

    internal void Wake()
    {
        lock (gate)
        {
            if (!closing && wake.CurrentCount == 0)
            {
                wake.Release();
            }
        }
    }

    internal async Task WaitForWorkAsync(CancellationToken cancellationToken = default)
    {
        if (!Enter(isPump: false))
        {
            return;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
            _ = await wake.WaitAsync(snapshot.Settings.IdlePollInterval, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            Exit(isPump: false);
        }
    }

    internal Task StopAsync()
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (stopTask is not null)
            {
                return stopTask;
            }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            stopTask = completion.Task;
            closing = true;
            if (active == 0)
            {
                drained.TrySetResult();
            }
        }
        _ = StopCoreAsync(completion);
        return completion.Task;
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        ExceptionDispatchInfo? fault = null;
        try
        {
            await stopping.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            fault = ExceptionDispatchInfo.Capture(error);
        }
        await drained.Task.ConfigureAwait(false);
        if (fault is null)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(fault.SourceException);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true;
                wake.Dispose();
                stopping.Dispose();
            }
        }
    }

    private async Task<MessagePumpResult> PumpCoreAsync(CancellationToken cancellationToken)
    {
        (IReadOnlyList<MessageReservation> Rows, TransactionCompletion Completion) reserved = await UseAsync(async owner =>
        {
            IReadOnlyList<MessageReservation> rows = await store.ReserveAsync(owner.Context,
                new(identity.Application, identity.Environment, reservationOwner, snapshot.Settings.PublisherBatchSize,
                    snapshot.Settings.ReservationDuration), cancellationToken).ConfigureAwait(false);
            if (rows.Count > snapshot.Settings.PublisherBatchSize)
            {
                throw new InvalidOperationException("The store exceeded its bounded metadata reservation batch.");
            }
            var ids = new HashSet<Guid>();
            foreach (MessageReservation row in rows)
            {
                if (row.RecordId == Guid.Empty || row.MessageId == Guid.Empty || row.Owner != reservationOwner
                    || row.Version < 1 || row.Application != identity.Application || row.Environment != identity.Environment
                    || row.Digest is null || !ids.Add(row.RecordId))
                {
                    throw new InvalidOperationException("The store returned inconsistent reservation ownership or immutable metadata.");
                }
            }
            TransactionCompletion completion = await owner.CommitAsync(cancellationToken).ConfigureAwait(false);
            return (rows, completion);
        }, cancellationToken).ConfigureAwait(false);
        if (reserved.Completion.Outcome != TransactionOutcome.Committed || reserved.Rows.Count == 0)
        {
            return new(reserved.Rows.Count, reserved.Completion, []);
        }
        var results = new MessagePublicationAttempt[reserved.Rows.Count];
        int next = -1;
        Task[] workers = Enumerable.Range(0, Math.Min(snapshot.Settings.PublisherConcurrency, reserved.Rows.Count))
            .Select(_ => WorkAsync()).ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
        return new(reserved.Rows.Count, reserved.Completion, Array.AsReadOnly(results));

        async Task WorkAsync()
        {
            int index;
            while ((index = Interlocked.Increment(ref next)) < reserved.Rows.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[index] = await PublishOneAsync(reserved.Rows[index], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<MessagePublicationAttempt> PublishOneAsync(MessageReservation reservation, CancellationToken cancellationToken)
    {
        if (reservation.EncodedLength < 1 || reservation.EncodedLength > snapshot.Settings.Limits.MaximumEnvelopeBytes)
        {
            return await SettleAsync(reservation, new(MessagePublicationDisposition.Rejected, MessagePublicationFailure.Configuration,
                "messaging.outbox_shape_invalid"), published: false, deferred: false, null).ConfigureAwait(false);
        }
        if (!resources.TryAcquirePublication(out IDisposable? publicationSlot))
        {
            return await DeferAsync(reservation).ConfigureAwait(false);
        }
        using (publicationSlot)
        {
            if (!resources.TryAcquireBytes(checked(2 * reservation.EncodedLength), out IDisposable? bytes))
            {
                return await DeferAsync(reservation).ConfigureAwait(false);
            }
            using (bytes)
            {
                (EncodedMessage? Body, TransactionCompletion Completion) loaded = await UseAsync(async owner =>
                {
                    EncodedMessage? body = await store.ReadReservedMessageAsync(owner.Context, reservation, cancellationToken).ConfigureAwait(false);
                    TransactionCompletion completion = await owner.RollbackAsync().ConfigureAwait(false);
                    return (body, completion);
                }, cancellationToken).ConfigureAwait(false);
                if (loaded.Body is null)
                {
                    return new(reservation.RecordId, false, false, true, ReadCompletion: loaded.Completion);
                }
                if (loaded.Body.Length != reservation.EncodedLength || !loaded.Body.Digest.Equals(reservation.Digest)
                    || loaded.Body.Envelope.MessageId != reservation.MessageId)
                {
                    return await SettleAsync(reservation, new(MessagePublicationDisposition.Rejected, MessagePublicationFailure.Configuration,
                        "messaging.outbox_content_invalid"), false, false, loaded.Completion).ConfigureAwait(false);
                }
                var publication = new MessagePublication(loaded.Body, reservation.Destination, reservation.Purpose,
                    reservation.DeliveryOrdinal, reservation.RecordId, reservation.Generation,
                    reservation.TargetSubscription ?? reservation.ProcessingKey?.Subscription, reservation.DiagnosticCode);
                using var timeout = new CancellationTokenSource(snapshot.Settings.PublicationTimeout, clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                MessagePublicationResult result;
                using MessageObservation? observation = instrumentation?.Publish(reservation.Destination.RouteAlias);
                try
                {
                    result = await publisher.PublishAsync(publication, linked.Token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The adapter returned no settled publication evidence.");
                    observation?.Complete(result.Disposition switch
                    {
                        MessagePublicationDisposition.Confirmed => "confirmed",
                        MessagePublicationDisposition.Rejected => "rejected",
                        _ => "unknown"
                    });
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    observation?.Complete("cancelled");
                    result = new(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient, "messaging.publication_cancelled");
                }
                catch
                {
                    observation?.Complete("failed");
                    try
                    {
                        _ = await SettleAsync(reservation, new(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient,
                            "messaging.publication_fault"), true, false, loaded.Completion).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Preserve the programming fault; an unsettled reservation remains recoverable after expiry.
                    }
                    throw;
                }
                return await SettleAsync(reservation, result, true, false, loaded.Completion).ConfigureAwait(false);
            }
        }
    }

    private Task<MessagePublicationAttempt> DeferAsync(MessageReservation reservation)
        => SettleAsync(reservation, new(MessagePublicationDisposition.Rejected, MessagePublicationFailure.Transient,
            "messaging.publisher_capacity"), false, true, null);

    private async Task<MessagePublicationAttempt> SettleAsync(MessageReservation reservation, MessagePublicationResult result,
        bool published, bool deferred, TransactionCompletion? readCompletion)
    {
        using var cleanup = new CancellationTokenSource(snapshot.Settings.DrainTimeout, clock);
        (bool Updated, TransactionCompletion Completion) settled = await UseAsync(async owner =>
        {
            TimeSpan delay = result.Failure == MessagePublicationFailure.Transient ? Backoff(reservation.AttemptCount) : TimeSpan.Zero;
            bool updated = await store.RecordPublicationAsync(owner.Context, new(reservation, result, delay), cleanup.Token).ConfigureAwait(false);
            TransactionCompletion completion = await owner.CommitAsync(cleanup.Token).ConfigureAwait(false);
            return (updated, completion);
        }, cleanup.Token).ConfigureAwait(false);
        return new(reservation.RecordId, published, deferred, !settled.Updated, result,
            readCompletion, settled.Completion, settled.Updated);
    }

    private TimeSpan Backoff(int attempt)
    {
        double seconds = Math.Min(snapshot.Settings.MaximumPublicationBackoff.TotalSeconds,
            Math.Pow(2, Math.Clamp(attempt - 1, 0, 16)));
        return TimeSpan.FromSeconds(seconds * (0.8 + Random.Shared.NextDouble() * 0.2));
    }

    private async Task<T> UseAsync<T>(Func<ILocalTransaction, Task<T>> work, CancellationToken cancellationToken)
    {
        ILocalTransaction owner = await transactions.BeginAsync(databaseName,
            new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, cancellationToken).ConfigureAwait(false);
        return await MessagingTransaction.UseAsync(owner, databaseName, work).ConfigureAwait(false);
    }

    private bool Enter(bool isPump)
    {
        lock (gate)
        {
            if (closing)
            {
                return false;
            }
            if (isPump ? pumping : waiting)
            {
                throw new InvalidOperationException("One host owner invokes each messaging pump or idle wait sequentially.");
            }
            if (isPump)
            {
                pumping = true;
            }
            else
            {
                waiting = true;
            }
            active++;
            return true;
        }
    }

    private void Exit(bool isPump)
    {
        lock (gate)
        {
            if (isPump)
            {
                pumping = false;
            }
            else
            {
                waiting = false;
            }
            active--;
            if (closing && active == 0)
            {
                drained.TrySetResult();
            }
        }
    }
}
