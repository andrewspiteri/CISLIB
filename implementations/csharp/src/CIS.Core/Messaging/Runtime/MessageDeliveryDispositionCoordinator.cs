using System.Runtime.ExceptionServices;
using CIS.Security;
using CIS.Transactions;

namespace CIS.Messaging;

// Owns only fresh responsibility bookkeeping after the dispatcher has awaited the original owner.
internal sealed class MessageDeliveryDispositionCoordinator : IMessageDeliveryDispositionCoordinator
{
    private readonly MessagingSnapshot snapshot;
    private readonly IMessagingStore store;
    private readonly ILocalTransactionFactory transactions;
    private readonly LocalApplicationIdentity identity;
    private readonly Action? wake;
    private readonly Func<double> jitter;

    internal MessageDeliveryDispositionCoordinator(MessagingSnapshot snapshot, IMessagingStore store,
        ILocalTransactionFactory transactions, LocalApplicationIdentity identity, Action? wake = null, Func<double>? jitter = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(identity);
        snapshot.Validate();
        this.snapshot = snapshot;
        this.store = store;
        this.transactions = transactions;
        this.identity = identity;
        this.wake = wake;
        this.jitter = jitter ?? Random.Shared.NextDouble;
    }

    public Task<MessageDispositionHandling> HandleAsync(MessageFailedDelivery failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(failure.Message);
        ArgumentNullException.ThrowIfNull(failure.Subscription);
        MessageText.Code(failure.SafeCode, nameof(failure.SafeCode));
        if (!Enum.IsDefined(failure.Kind) || !snapshot.Subscriptions.Any(item => ReferenceEquals(item, failure.Subscription)))
        {
            throw new ArgumentException("Use an exact registered subscription and declared failure kind.", nameof(failure));
        }
        if (cancellationToken.IsCancellationRequested
            || failure.OriginalCompletion?.Outcome is TransactionOutcome.Unknown or TransactionOutcome.Committed
            || failure.Kind is MessageDeliveryFailureKind.PersistenceUnknown or MessageDeliveryFailureKind.InfrastructureUnavailable)
        {
            return Task.FromResult(new MessageDispositionHandling(MessageDispositionHandlingKind.Stop,
                failure.OriginalCompletion, Fault: failure.OriginalFault));
        }
        if (failure.Kind == MessageDeliveryFailureKind.Shutdown)
        {
            return Task.FromResult(new MessageDispositionHandling(MessageDispositionHandlingKind.Requeue,
                failure.OriginalCompletion, Fault: failure.OriginalFault));
        }
        if (failure.Kind == MessageDeliveryFailureKind.Rejected)
        {
            return SecureAsync(failure, rejection: true, cancellationToken);
        }
        MessageProcessingAttempt? attempt = failure.Attempt;
        if (!failure.CanonicalEligible || attempt is null || failure.OriginalCompletion?.Outcome != TransactionOutcome.NotCommitted
            || attempt.Key.Application != identity.Application || attempt.Key.Environment != identity.Environment
            || attempt.Key.Subscription != failure.Subscription.Alias || attempt.Key.MessageId != failure.Message.Envelope.MessageId
            || !attempt.Message.Digest.Equals(failure.Message.Digest))
        {
            return Task.FromResult(new MessageDispositionHandling(MessageDispositionHandlingKind.Stop,
                failure.OriginalCompletion, Fault: failure.OriginalFault));
        }
        return SecureAsync(failure, rejection: false, cancellationToken);
    }

    private async Task<MessageDispositionHandling> SecureAsync(MessageFailedDelivery failure, bool rejection, CancellationToken token)
    {
        ILocalTransaction? owner = null;
        TransactionCompletion? completion = null;
        ExceptionDispatchInfo? fault = failure.OriginalFault;
        MessageProcessingDisposition? disposition = null;
        Guid? handoff = null;
        bool secured = false;
        bool rejectConflict = false;
        try
        {
            owner = await transactions.BeginAsync(failure.Subscription.DatabaseName,
                new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The transaction factory returned no disposition owner.");
            if (owner.Context.DatabaseName != failure.Subscription.DatabaseName || owner.Context.Isolation != TransactionIsolation.ReadCommitted)
            {
                throw new InvalidOperationException("Disposition bookkeeping requires the registered database and ReadCommitted isolation.");
            }

            if (rejection)
            {
                handoff = Guid.NewGuid();
                var outgoing = new MessageOutgoingRecord(handoff.Value, identity.Application, identity.Environment,
                    new(failure.Subscription.RouteAlias), failure.Message, MessageRecordPurpose.Rejection,
                    DiagnosticCode: failure.SafeCode, OriginalProvenance: failure.Attempt?.Provenance,
                    TargetSubscription: failure.Subscription.Alias);
                await store.RecordRejectionAsync(owner.Context, new(Guid.NewGuid(), identity.Application, identity.Environment,
                    failure.Subscription.Alias, failure.Message, failure.SafeCode, outgoing, failure.Attempt?.Provenance), token).ConfigureAwait(false);
                secured = true;
            }
            else
            {
                MessageProcessingAttempt attempt = failure.Attempt!;
                bool exhausted = attempt.DeliveryOrdinal >= failure.Subscription.Settings.MaximumDeliveryOrdinals;
                bool terminal = exhausted || failure.Kind == MessageDeliveryFailureKind.CanonicalTerminal;
                disposition = terminal ? MessageProcessingDisposition.Terminal : MessageProcessingDisposition.RetryScheduled;
                string code = exhausted ? "messaging.retry.exhausted" : failure.SafeCode;
                var outgoing = new MessageOutgoingRecord(Guid.NewGuid(), identity.Application, identity.Environment,
                    new(failure.Subscription.RouteAlias), failure.Message,
                    terminal ? MessageRecordPurpose.Terminal : MessageRecordPurpose.Retry,
                    terminal ? TimeSpan.Zero : RetryDelay(failure.Subscription.Settings, attempt.DeliveryOrdinal),
                    attempt.Key, terminal ? attempt.DeliveryOrdinal : checked(attempt.DeliveryOrdinal + 1), attempt.Generation,
                    code, attempt.Provenance, failure.Subscription.Alias);
                MessageDispositionResult result = await store.SecureDispositionAsync(owner.Context,
                    new(attempt, disposition.Value, outgoing, code), token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The store returned no disposition result.");
                rejectConflict = result.Disposition is MessageDispositionResultKind.ContentConflict
                    or MessageDispositionResultKind.ProvenanceConflict or MessageDispositionResultKind.InvalidOrdinal;
                if (rejectConflict)
                {
                    completion = await owner.RollbackAsync().ConfigureAwait(false);
                }
                else if (result.Disposition is MessageDispositionResultKind.Secured
                    or MessageDispositionResultKind.AlreadyHandled or MessageDispositionResultKind.AlreadySecured)
                {
                    secured = true;
                    handoff = result.HandoffRecordId;
                    disposition = result.Record?.Disposition ?? disposition;
                    if (result.Disposition == MessageDispositionResultKind.Secured && terminal && failure.Reply is not null)
                    {
                        // Part B supplies safe response staging, never a second business invocation.
                        MessageText.Id(await failure.Reply(owner.Context, token).ConfigureAwait(false), "replyRecordId");
                    }
                }
                else
                {
                    throw new InvalidOperationException("The store returned an undeclared disposition result.");
                }
            }
            if (secured)
            {
                completion = await owner.CommitAsync(token).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            fault ??= ExceptionDispatchInfo.Capture(error);
            secured = false;
        }
        finally
        {
            if (owner is not null)
            {
                try
                {
                    await owner.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    fault ??= ExceptionDispatchInfo.Capture(error);
                }
                completion ??= owner.Completion;
            }
        }
        if (rejectConflict && completion?.Outcome == TransactionOutcome.NotCommitted && !token.IsCancellationRequested)
        {
            // One bounded separate rejection after the failed canonical transition has actually rolled back.
            return await SecureAsync(failure with { SafeCode = "messaging.delivery.conflict", Reply = null }, rejection: true, token).ConfigureAwait(false);
        }
        if (secured && completion?.Outcome == TransactionOutcome.Committed)
        {
            NotifyPublisher();
            return new(MessageDispositionHandlingKind.ResponsibilityCommitted, completion, disposition, handoff, fault);
        }
        return new(MessageDispositionHandlingKind.Stop, completion, Fault: fault);
    }

    private TimeSpan RetryDelay(MessageSubscriptionSettings settings, int ordinal)
    {
        MessageText.Range(ordinal, 1, settings.MaximumDeliveryOrdinals - 1, nameof(ordinal));
        double sample = jitter();
        if (!double.IsFinite(sample) || sample is < 0 or > 1)
        {
            throw new InvalidOperationException("The retry jitter source must return a finite unit sample.");
        }
        double ticks = settings.RetryDelays[ordinal - 1].Ticks * (1 + ((sample * 2) - 1) * settings.RetryJitterRatio);
        return TimeSpan.FromTicks(Math.Max(1, checked((long)ticks)));
    }

    private void NotifyPublisher()
    {
        try
        {
            wake?.Invoke();
        }
        catch (Exception)
        {
            // A hint cannot undo known committed responsibility; the existing publisher poll remains authoritative.
        }
    }
}
