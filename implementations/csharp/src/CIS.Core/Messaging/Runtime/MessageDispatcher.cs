using System.Runtime.ExceptionServices;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Messaging;

internal enum MessageDispatchOutcome
{
    Handled, Duplicate, Retry, Terminal, Unknown, Cancelled
}
internal sealed record MessageDispatchResult(MessageDispatchOutcome Outcome, string SafeCode,
    TransactionCompletion? Completion = null, ExceptionDispatchInfo? Fault = null);

internal sealed class MessageDispatcher : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IServiceProvider root;
    private readonly MessagingSnapshot snapshot;
    private readonly IMessagingStore store;
    private readonly ILocalTransactionFactory transactions;
    private readonly MessagingResourceBudget resources;
    private readonly LocalApplicationIdentity local;
    private readonly TimeProvider clock;
    private readonly MessagingInstrumentation? instrumentation;
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IMessageDeliveryDispositionCoordinator? dispositions;
    private IMessageResponseCoordinator? replies;
    private bool started;
    private bool closing;
    private bool disposed;
    private int active;
    private Task? stopTask;

    internal MessageDispatcher(IServiceProvider root, MessagingSnapshot snapshot, IMessagingStore store,
        ILocalTransactionFactory transactions, MessagingResourceBudget resources, LocalApplicationIdentity local,
        IMessageDeliveryDispositionCoordinator? dispositions = null, TimeProvider? timeProvider = null,
        MessagingInstrumentation? instrumentation = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(local);
        snapshot.Validate();
        if (instrumentation is not null && !ReferenceEquals(instrumentation.RegistrationIdentity, snapshot.RegistrationIdentity))
        {
            throw new ArgumentException("Use the runtime's exact messaging observation snapshot.", nameof(instrumentation));
        }
        this.root = root;
        this.snapshot = snapshot;
        this.store = store;
        this.transactions = transactions;
        this.resources = resources;
        this.local = local;
        this.dispositions = dispositions;
        clock = timeProvider ?? TimeProvider.System;
        this.instrumentation = instrumentation;
    }

    internal void BindDispositions(IMessageDeliveryDispositionCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        lock (gate)
        {
            if (started || closing || dispositions is not null)
            {
                throw new InvalidOperationException("Bind disposition support once before dispatch starts.");
            }
            dispositions = coordinator;
        }
    }

    internal void BindReplies(IMessageResponseCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        lock (gate)
        {
            if (started || closing || replies is not null)
            {
                throw new InvalidOperationException("Bind reply support once before dispatch starts.");
            }
            replies = coordinator;
        }
    }

    internal async Task<MessageDispatchResult> DispatchAsync(IMessageDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        lock (gate)
        {
            if (closing)
            {
                delivery.RequestConsumerStop("messaging.stopping");
                return new(MessageDispatchOutcome.Cancelled, "messaging.stopping");
            }
            started = true;
            active++;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
            IDisposable? work = await AdmitAsync(delivery, 0, linked.Token).ConfigureAwait(false);
            if (work is null)
            {
                return AdmissionRefused(linked.Token);
            }
            using (work)
            {
                using MessageObservation? observation = instrumentation?.Delivery(delivery.SubscriptionAlias);
                MessageDispatchResult result = await DispatchAdmittedAsync(delivery, linked.Token).ConfigureAwait(false);
                observation?.Complete(result.Outcome switch
                {
                    MessageDispatchOutcome.Handled => "handled",
                    MessageDispatchOutcome.Duplicate => "duplicate",
                    MessageDispatchOutcome.Retry => "retry",
                    MessageDispatchOutcome.Terminal => "terminal",
                    MessageDispatchOutcome.Cancelled => "cancelled",
                    _ => "unknown"
                });
                return result;
            }
        }
        finally
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

    private async Task<MessageDispatchResult> DispatchAdmittedAsync(IMessageDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Body.Length is < 1 || delivery.Body.Length > snapshot.Settings.Limits.MaximumEnvelopeBytes)
        {
            return await QuarantineAsync(delivery, "messaging.envelope_size").ConfigureAwait(false);
        }
        bool reserveResponse = delivery.SubscriptionAlias != MessagingSystemSubscriptions.Replies
            && replies is not null && snapshot.Subscriptions.Any(item => item.Alias == delivery.SubscriptionAlias && item.RequestMetadata is not null);
        int bytesRequired = checked(4 * delivery.Body.Length
            + (reserveResponse ? 4 * snapshot.Settings.Limits.MaximumEnvelopeBytes : 0));
        IDisposable? bytes = await AdmitAsync(delivery, bytesRequired, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return AdmissionRefused(cancellationToken);
        }
        using (bytes)
        {
            if (!MessageWire.TryDecode(delivery.Body, out EncodedMessage? message, out _, snapshot.Settings.Limits))
            {
                return await QuarantineAsync(delivery, "messaging.envelope_invalid").ConfigureAwait(false);
            }
            MessageResponseAdmission? response = null;
            if (message!.Envelope.Kind == MessageKind.Request && reserveResponse)
            {
                // Input and prospective response share one pre-decode admission through actual settlement.
                // The underlying lease is idempotent; waiting deliveries retain no partial byte reservation.
                response = new(resources, bytes);
            }
            using (response)
            {
                return await DispatchEnvelopeAsync(delivery, message, cancellationToken, response).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<IDisposable?> AdmitAsync(IMessageDelivery delivery, int bytes, CancellationToken cancellationToken)
    {
        try
        {
            bool reply = resources.ReplyIngressEnabled && delivery.SubscriptionAlias == MessagingSystemSubscriptions.Replies;
            return bytes == 0 ? reply ? await resources.AcquireReplyDeliveryAsync(cancellationToken).ConfigureAwait(false)
                    : await resources.AcquireDeliveryAsync(cancellationToken).ConfigureAwait(false)
                : reply ? await resources.AcquireReplyBytesAsync(bytes, cancellationToken).ConfigureAwait(false)
                    : await resources.AcquireAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var cleanup = new CancellationTokenSource(snapshot.Settings.DrainTimeout, clock);
            await delivery.RequeueAsync(cleanup.Token).ConfigureAwait(false);
            return null;
        }
        catch (MessagingAdmissionCapacityException)
        {
            delivery.RequestConsumerStop("messaging.admission_capacity");
            return null;
        }
    }

    private static MessageDispatchResult AdmissionRefused(CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested ? new(MessageDispatchOutcome.Cancelled, "messaging.admission_cancelled")
            : new(MessageDispatchOutcome.Unknown, "messaging.admission_capacity");

    private async Task<MessageDispatchResult> DispatchEnvelopeAsync(IMessageDelivery delivery, EncodedMessage message,
        CancellationToken cancellationToken, MessageResponseAdmission? responseAdmission)
    {
        DispatchResolution resolution;
        try
        {
            resolution = await ResolveEnvelopeAsync(delivery, message, cancellationToken, responseAdmission).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
        {
            resolution = new(new(MessageDispatchOutcome.Cancelled, "messaging.delivery_cancelled",
                Fault: ExceptionDispatchInfo.Capture(error)), BrokerAction.Requeue);
        }
        // Native acknowledgement faults (including generation loss) remain native faults, never handler retries.
        // All original transaction, disposition and independent DI/security scope cleanup has already settled.
        using var cleanup = new CancellationTokenSource(snapshot.Settings.DrainTimeout, clock);
        switch (resolution.Action)
        {
            case BrokerAction.Acknowledge:
                await delivery.AcknowledgeAsync(cleanup.Token).ConfigureAwait(false);
                break;
            case BrokerAction.Requeue:
                await delivery.RequeueAsync(cleanup.Token).ConfigureAwait(false);
                break;
            case BrokerAction.Quarantine:
                return await QuarantineAsync(delivery, resolution.Result.SafeCode).ConfigureAwait(false);
            default:
                delivery.RequestConsumerStop(resolution.Result.SafeCode);
                break;
        }
        return resolution.Result;
    }

    private async Task<DispatchResolution> ResolveEnvelopeAsync(IMessageDelivery delivery, EncodedMessage message,
        CancellationToken cancellationToken, MessageResponseAdmission? responseAdmission)
    {
        MessageSubscriptionDefinition? subscription = snapshot.Subscriptions.SingleOrDefault(item => item.Alias == delivery.SubscriptionAlias);
        bool replyIngress = delivery.SubscriptionAlias == MessagingSystemSubscriptions.Replies;
        if (subscription is null && !replyIngress)
        {
            return new(new(MessageDispatchOutcome.Unknown, "messaging.subscription_unknown"), BrokerAction.Quarantine);
        }
        MessageNativeEvidence? evidence = await delivery.GetNativeEvidenceAsync(cancellationToken).ConfigureAwait(false);
        DispatchResolution resolution;
        if (evidence is null)
        {
            resolution = subscription is null ? new(new(MessageDispatchOutcome.Unknown, "messaging.reply_untrusted"), BrokerAction.Quarantine)
                : await FailAsync(new(message, subscription, MessageDeliveryFailureKind.Rejected, "messaging.producer_untrusted"), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // This scope establishes native authority before application services or policy are resolved.
            await using (SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
                evidence.Profile, evidence.Authentication, cancellationToken).ConfigureAwait(false))
            {
                CallerContext? caller = scope.Caller;
                if (caller?.Application is null || caller.Environment is null || caller.Subject != evidence.Provenance.Subject)
                {
                    resolution = subscription is null ? new(new(MessageDispatchOutcome.Unknown, "messaging.reply_denied"), BrokerAction.Quarantine)
                        : await FailAsync(new(message, subscription, MessageDeliveryFailureKind.Rejected, "messaging.producer_denied",
                            Services: scope.Services), cancellationToken).ConfigureAwait(false);
                }
                else if (replyIngress)
                {
                    MessageReplyHandling reply = message.Envelope.Kind == MessageKind.Reply && replies is not null
                        ? await replies.ReceiveReplyAsync(message, scope.Services, cancellationToken).ConfigureAwait(false)
                        : MessageReplyHandling.Quarantine;
                    resolution = new(new(MessageDispatchOutcome.Handled, "messaging.reply_received"), reply switch
                    {
                        MessageReplyHandling.Acknowledge => BrokerAction.Acknowledge,
                        MessageReplyHandling.Quarantine => BrokerAction.Quarantine,
                        _ => BrokerAction.Stop
                    });
                }
                else
                {
                    // The native scope's actual mapping is authoritative; producer audit fields never override it.
                    var provenance = new MessageProvenance(evidence.Provenance.BrokerAlias, evidence.Provenance.VirtualHost,
                        caller.Subject, evidence.Provenance.UserId, caller.Application, caller.Environment);
                    resolution = await PrepareAndExecuteAsync(subscription!, message, delivery.Metadata, provenance,
                        caller, scope.Services, cancellationToken, responseAdmission).ConfigureAwait(false);
                }
            }
        }

        return resolution;
    }

    private async Task<DispatchResolution> PrepareAndExecuteAsync(MessageSubscriptionDefinition subscription, EncodedMessage message,
        MessageDeliveryMetadata metadata, MessageProvenance current, CallerContext caller, IServiceProvider services,
        CancellationToken cancellationToken, MessageResponseAdmission? responseAdmission)
    {
        bool retry = metadata.Purpose is MessageRecordPurpose.Retry or MessageRecordPurpose.Redrive;
        bool initial = metadata.Purpose == MessageRecordPurpose.Application;
        MessageRouteDefinition route = snapshot.GetRoute(subscription.RouteAlias);
        if ((!initial && !retry) || metadata.RouteAlias != subscription.RouteAlias || metadata.DeliveryOrdinal is < 1 or > 32
            || metadata.Generation < 0 || initial && (metadata.DeliveryOrdinal != 1 || metadata.Generation != 0)
            || retry && (metadata.TargetSubscription != subscription.Alias || metadata.OutboxRecordId is null || metadata.OutboxRecordId == Guid.Empty)
            || message.Envelope.Contract != subscription.Contract
            || (route.Kind == MessageKind.Event ? message.Envelope.Kind != MessageKind.Event
                : message.Envelope.Kind is not MessageKind.Command and not MessageKind.Request))
        {
            return await FailAsync(new(message, subscription, MessageDeliveryFailureKind.Rejected, "messaging.delivery_metadata_invalid",
                Services: services), cancellationToken).ConfigureAwait(false);
        }
        if (initial && (message.Envelope.ProducerApplication != current.Application || message.Envelope.ProducerEnvironment != current.Environment)
            || retry && (current.Application != local.Application || current.Environment != local.Environment))
        {
            return await FailAsync(new(message, subscription, MessageDeliveryFailureKind.Rejected, "messaging.producer_mismatch",
                Services: services), cancellationToken).ConfigureAwait(false);
        }
        var visitor = new MessageDispatchVisitor(services, caller, message, local, replies, cancellationToken, responseAdmission);
        MessageDispatchPlan plan = await subscription.Accept(visitor).ConfigureAwait(false);
        if (plan.Rejection is { } reason)
        {
            return await FailAsync(new(message, subscription, MessageDeliveryFailureKind.Rejected, reason, Services: services), cancellationToken).ConfigureAwait(false);
        }
        ICapabilityAuthorizer authorizer = services.GetRequiredService<ICapabilityAuthorizer>();
        bool initialReplyAllowed = initial && message.Envelope.Kind == MessageKind.Request
            && await AuthorizeReplyAsync(subscription, message, current, authorizer, cancellationToken).ConfigureAwait(false);
        if (!(await authorizer.AuthorizeAsync(subscription.Capabilities, plan.Target!, cancellationToken).ConfigureAwait(false)).IsAllowed
            || retry && !(await authorizer.AuthorizeAsync(subscription.Settings.RetryCapabilities, plan.Target!, cancellationToken).ConfigureAwait(false)).IsAllowed)
        {
            var denied = new MessageProcessingAttempt(new(local.Application, local.Environment, subscription.Alias, message.Envelope.MessageId),
                message, current, metadata.DeliveryOrdinal, metadata.Generation, plan.Binding);
            return await FailAsync(new(message, subscription, MessageDeliveryFailureKind.Rejected, "messaging.handler_denied",
                denied, Services: services, DeniedReply: initialReplyAllowed ? plan.DeniedReply?.Invoke(denied) : null), cancellationToken).ConfigureAwait(false);
        }
        var key = new MessageProcessingKey(local.Application, local.Environment, subscription.Alias, message.Envelope.MessageId);
        MessageProvenance original = current;
        if (retry)
        {
            MessageProcessingRecord? retained = await ReadPriorAsync(subscription.DatabaseName, key, cancellationToken).ConfigureAwait(false);
            bool future = retained is not null && (metadata.Generation > retained.Generation
                || metadata.Generation == retained.Generation && metadata.DeliveryOrdinal > retained.DeliveryOrdinal);
            bool currentOrdinal = retained is not null && metadata.Generation == retained.Generation
                && metadata.DeliveryOrdinal == retained.DeliveryOrdinal;
            if (retained is null || !retained.Digest.Equals(message.Digest) || future
                || retained.Disposition == MessageProcessingDisposition.RetryScheduled && currentOrdinal && retained.HandoffRecordId != metadata.OutboxRecordId
                || retained.Disposition == MessageProcessingDisposition.Ready
                || retained.OperationBinding != plan.Binding || retained.Provenance.Application != message.Envelope.ProducerApplication
                || retained.Provenance.Environment != message.Envelope.ProducerEnvironment
                || plan.ValidateRetry is { } validate && !await validate(retained, cancellationToken).ConfigureAwait(false))
            {
                return await FailAsync(new(message, subscription, MessageDeliveryFailureKind.Rejected, "messaging.retry_untrusted",
                    Services: services), cancellationToken).ConfigureAwait(false);
            }
            original = retained.Provenance;
        }
        if (message.Envelope.Kind == MessageKind.Request)
        {
            if (!(initial ? initialReplyAllowed : await AuthorizeReplyAsync(subscription, message, original, authorizer, cancellationToken).ConfigureAwait(false)))
            {
                return await FailAsync(new(message, subscription, MessageDeliveryFailureKind.Rejected, "messaging.reply_denied",
                    Services: services), cancellationToken).ConfigureAwait(false);
            }
        }
        var attempt = new MessageProcessingAttempt(key, message, original, metadata.DeliveryOrdinal, metadata.Generation,
            plan.Binding, metadata.Purpose, retry ? metadata.OutboxRecordId : null);
        return await ExecutePlanAsync(subscription, message, plan, attempt, services, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> AuthorizeReplyAsync(MessageSubscriptionDefinition subscription, EncodedMessage message,
        MessageProvenance original, ICapabilityAuthorizer authorizer, CancellationToken token)
    {
        MessageReplyRoute destination = message.Envelope.ReplyRoute!;
        return destination.Application == original.Application && destination.Environment == original.Environment
            && (await authorizer.AuthorizeAsync(subscription.Settings.ReplyCapabilities,
                new(destination.Application, destination.Environment, "messaging.reply", subscription.RequestMetadata!.Alias), token).ConfigureAwait(false)).IsAllowed;
    }

    private async Task<DispatchResolution> ExecutePlanAsync(MessageSubscriptionDefinition subscription, EncodedMessage message,
        MessageDispatchPlan plan, MessageProcessingAttempt attempt, IServiceProvider services, CancellationToken cancellationToken)
    {
        ILocalTransaction? owner = null;
        bool acquired = false;
        MessageInvocation? invocation = null;
        MessageProcessingClaim? claim = null;
        TransactionCompletion? completion = null;
        ExceptionDispatchInfo? fault = null;
        try
        {
            owner = await transactions.BeginAsync(subscription.DatabaseName,
                new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, cancellationToken).ConfigureAwait(false);
            completion = await MessagingTransaction.UseAsync(owner, subscription.DatabaseName, async current =>
            {
                claim = await store.ClaimProcessingAsync(current.Context, attempt, cancellationToken).ConfigureAwait(false);
                if (claim.Disposition is MessageClaimDisposition.HandledDuplicate or MessageClaimDisposition.Terminal or MessageClaimDisposition.RetryScheduled)
                {
                    if (message.Envelope.Kind == MessageKind.Request && claim.Record?.ReplyRecordId is null
                        && claim.Disposition != MessageClaimDisposition.RetryScheduled)
                    {
                        throw new InvalidDataException("A repeated request has no retained secured reply reference.");
                    }
                    return await current.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                if (claim.Disposition != MessageClaimDisposition.Acquired || claim.Record is null)
                {
                    return await current.RollbackAsync().ConfigureAwait(false);
                }
                acquired = true;
                invocation = await plan.Invoke!(current.Context, attempt, cancellationToken).ConfigureAwait(false);
                if (invocation.Kind != MessageInvocationKind.Completed || current.Context.IsRollbackOnly)
                {
                    return await current.RollbackAsync().ConfigureAwait(false);
                }
                if (!await store.CompleteProcessingAsync(current.Context,
                    new(attempt.Key, claim.Record.Version, attempt.DeliveryOrdinal, attempt.Generation, invocation.ReplyRecordId, plan.Binding), cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException("The claimed message could not secure its expected processing completion.");
                }
                return await current.CommitAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            completion = owner?.Completion;
            fault = ExceptionDispatchInfo.Capture(error);
            if (completion?.Outcome == TransactionOutcome.Committed)
            {
                // A post-commit cleanup fault must not schedule business work again.
                fault.Throw();
            }
        }
        if (completion?.Outcome == TransactionOutcome.Committed)
        {
            bool duplicate = !acquired;
            if (duplicate)
            {
                instrumentation?.Disposition(subscription.Alias, "duplicate");
            }
            return new(new(duplicate ? MessageDispatchOutcome.Duplicate : MessageDispatchOutcome.Handled,
                duplicate ? "messaging.duplicate" : "messaging.handled", completion), BrokerAction.Acknowledge);
        }
        MessageDeliveryFailureKind kind = completion?.Outcome == TransactionOutcome.Unknown ? MessageDeliveryFailureKind.PersistenceUnknown
            : cancellationToken.IsCancellationRequested ? MessageDeliveryFailureKind.Shutdown
            : !acquired ? claim is null ? MessageDeliveryFailureKind.InfrastructureUnavailable : MessageDeliveryFailureKind.Rejected
            : invocation?.Kind == MessageInvocationKind.Rejected ? MessageDeliveryFailureKind.Rejected
            : invocation?.Kind == MessageInvocationKind.Terminal ? MessageDeliveryFailureKind.CanonicalTerminal
            : plan.HandlerStarted || invocation?.Kind == MessageInvocationKind.Retry ? MessageDeliveryFailureKind.HandlerFault
            : MessageDeliveryFailureKind.InfrastructureUnavailable;
        string code = invocation?.SafeCode ?? (kind == MessageDeliveryFailureKind.Rejected ? "messaging.claim_rejected"
            : kind == MessageDeliveryFailureKind.PersistenceUnknown ? "messaging.persistence_unknown" : "messaging.delivery_failed");
        return await FailAsync(new(message, subscription, kind, code, attempt,
            acquired && kind is not MessageDeliveryFailureKind.Rejected, completion, fault, services,
            Reply: acquired ? plan.TerminalReply?.Invoke(attempt) : null,
            DeniedReply: kind == MessageDeliveryFailureKind.Rejected && code == "messaging.operation_denied"
                ? plan.DeniedReply?.Invoke(attempt) : null), cancellationToken).ConfigureAwait(false);
    }

    private async Task<MessageProcessingRecord?> ReadPriorAsync(string databaseName, MessageProcessingKey key, CancellationToken cancellationToken)
    {
        ILocalTransaction owner = await transactions.BeginAsync(databaseName,
            new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, cancellationToken).ConfigureAwait(false);
        return await MessagingTransaction.UseAsync(owner, databaseName, async current =>
        {
            MessageProcessingRecord? record = await store.ReadProcessingAsync(current.Context, key, cancellationToken).ConfigureAwait(false);
            await current.RollbackAsync().ConfigureAwait(false);
            return record;
        }).ConfigureAwait(false);
    }

    private async Task<DispatchResolution> FailAsync(MessageFailedDelivery failure, CancellationToken cancellationToken)
    {
        using var cleanup = new CancellationTokenSource(snapshot.Settings.DrainTimeout, clock);
        MessageDispositionHandling handling = dispositions is null
            ? new(MessageDispositionHandlingKind.Stop)
            : await dispositions.HandleAsync(failure, cleanup.Token).ConfigureAwait(false);
        bool secured = handling.Kind == MessageDispositionHandlingKind.ResponsibilityCommitted
            && handling.Completion?.Outcome == TransactionOutcome.Committed;
        if (secured)
        {
            instrumentation?.Disposition(failure.Subscription.Alias, handling.Disposition switch
            {
                MessageProcessingDisposition.Handled => "duplicate",
                MessageProcessingDisposition.RetryScheduled => "retry",
                _ => "terminal"
            });
        }
        MessageDispatchOutcome outcome = secured ? handling.Disposition switch
        {
            MessageProcessingDisposition.Handled => MessageDispatchOutcome.Duplicate,
            MessageProcessingDisposition.RetryScheduled => MessageDispatchOutcome.Retry,
            _ => MessageDispatchOutcome.Terminal
        }
            : failure.Kind == MessageDeliveryFailureKind.Shutdown ? MessageDispatchOutcome.Cancelled : MessageDispatchOutcome.Unknown;
        return new(new(outcome, failure.SafeCode, handling.Completion ?? failure.OriginalCompletion, failure.OriginalFault ?? handling.Fault),
            secured ? BrokerAction.Acknowledge : handling.Kind == MessageDispositionHandlingKind.Requeue ? BrokerAction.Requeue : BrokerAction.Stop);
    }

    private async Task<MessageDispatchResult> QuarantineAsync(IMessageDelivery delivery, string code)
    {
        using var cleanup = new CancellationTokenSource(snapshot.Settings.DrainTimeout, clock);
        bool secured = await delivery.QuarantineMalformedAsync(code, cleanup.Token).ConfigureAwait(false);
        if (!secured)
        {
            delivery.RequestConsumerStop(code);
        }
        return new(secured ? MessageDispatchOutcome.Terminal : MessageDispatchOutcome.Unknown, code);
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
        Exception? fault = null;
        try
        {
            await stopping.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            fault = error;
        }
        await drained.Task.ConfigureAwait(false);
        if (fault is null)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(fault);
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
                stopping.Dispose();
            }
        }
    }

    private enum BrokerAction
    {
        Acknowledge, Requeue, Quarantine, Stop
    }
    private sealed record DispatchResolution(MessageDispatchResult Result, BrokerAction Action);
}
