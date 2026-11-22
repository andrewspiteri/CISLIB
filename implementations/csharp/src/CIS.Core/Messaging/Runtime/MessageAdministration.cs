using System.Runtime.ExceptionServices;
using CIS.Security;
using CIS.Transactions;

namespace CIS.Messaging;

internal sealed class MessageAdministration : IMessageAdministration
{
    private readonly MessagingSnapshot snapshot;
    private readonly IMessagingStore store;
    private readonly ILocalTransactionFactory transactions;
    private readonly LocalApplicationIdentity identity;
    private readonly ICapabilityAuthorizer authorizer;
    private readonly ICallerContextAccessor callers;
    private readonly ISecurityAuditSink audit;
    private readonly MessagingResourceBudget resources;
    private readonly Func<bool>? accepting;
    private readonly Action? wake;
    private readonly string database;

    internal MessageAdministration(MessagingSnapshot snapshot, IMessagingStore store, ILocalTransactionFactory transactions,
        LocalApplicationIdentity identity, ICapabilityAuthorizer authorizer, ICallerContextAccessor callers,
        ISecurityAuditSink audit, MessagingResourceBudget resources, Action? wake = null, Func<bool>? accepting = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(callers);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(resources);
        snapshot.Validate();
        this.snapshot = snapshot;
        this.store = store;
        this.transactions = transactions;
        this.identity = identity;
        this.authorizer = authorizer;
        this.callers = callers;
        this.audit = audit;
        this.resources = resources;
        this.accepting = accepting;
        this.wake = wake;
        database = snapshot.Stores.Single(item => item.Alias == snapshot.Settings.StoreAlias).DatabaseName;
    }

    public async Task<MessageInspectResult> InspectAsync(MessageInspectQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (accepting?.Invoke() == false)
        {
            return new(MessageAdministrationDisposition.Unavailable);
        }
        if (!resources.TryAcquireDelivery(out IDisposable? admission))
        {
            return new(MessageAdministrationDisposition.Unavailable);
        }
        using IDisposable? work = admission;
        MessageSubscriptionDefinition? subscription = query.Subscription is null ? null : GetSubscription(query.Subscription);
        if (query.RouteAlias is { } route)
        {
            snapshot.GetRoute(route);
            if (subscription is not null && subscription.RouteAlias != route)
            {
                throw new ArgumentException("The exact subscription and route filters must agree.", nameof(query));
            }
        }
        MessageAdministrationDisposition authorization = await AuthorizeAsync(snapshot.Settings.InspectionCapabilities,
            StoreTarget(), cancellationToken).ConfigureAwait(false);
        if (authorization != MessageAdministrationDisposition.Executed)
        {
            return new(authorization);
        }
        MessageProcessingKey? key = subscription is null ? null : new(identity.Application, identity.Environment, subscription.Alias, query.MessageId!.Value);
        (InspectionPage page, TransactionCompletion completion) = await RunAsync(async (context, token) =>
        {
            IReadOnlyList<MessageInspectionRecord> records = await store.InspectAsync(context,
                new(identity.Application, identity.Environment, query.MaximumRecords, key, query.RouteAlias, query.AfterRecordId), token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The store returned no inspection page.");
            if (records.Count > query.MaximumRecords)
            {
                throw new InvalidOperationException("The store exceeded the bounded inspection page.");
            }
            MessageProcessingRecord? processing = key is null ? null : await store.ReadProcessingAsync(context, key, token).ConfigureAwait(false);
            MessageProcessingInspection? safe = processing is null ? null : new(processing.Version, processing.Disposition,
                processing.DeliveryOrdinal, processing.Generation, processing.HasPayload);
            return new InspectionPage(Array.AsReadOnly(records.ToArray()), safe);
        }, cancellationToken).ConfigureAwait(false);
        return new(authorization, page.Records, page.Processing, completion);
    }

    public async Task<MessageAdminRedriveResult> RedriveAsync(MessageRedriveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (accepting?.Invoke() == false)
        {
            return new(MessageAdministrationDisposition.Unavailable);
        }
        if (!resources.TryAcquireDelivery(out IDisposable? admission))
        {
            return new(MessageAdministrationDisposition.Unavailable);
        }
        using IDisposable? work = admission;
        MessageSubscriptionDefinition subscription = GetSubscription(command.Subscription);
        var target = new CapabilityTarget(identity.Application, identity.Environment, "messaging.subscription", subscription.Alias);
        MessageAdministrationDisposition authorization = await AuthorizeAsync(subscription.Settings.RedriveCapabilities, target, cancellationToken).ConfigureAwait(false);
        if (authorization != MessageAdministrationDisposition.Executed)
        {
            return new(authorization);
        }
        CallerContext caller = callers.Current ?? throw new InvalidOperationException("Administrative work requires an established current caller.");
        const int bytesRequired = 2 * (MessagingLimits.MaximumSupportedPayloadBytes + MessagingLimits.MaximumMetadataBytes);
        if (snapshot.Settings.MaximumEncodedBufferBytes < bytesRequired || !resources.TryAcquireBytes(bytesRequired, out IDisposable? bytes))
        {
            return new(MessageAdministrationDisposition.Unavailable);
        }
        using IDisposable? body = bytes;
        var transition = new MessageRedriveTransition(new(identity.Application, identity.Environment, subscription.Alias, command.MessageId),
            command.ExpectedVersion, new(subscription.RouteAlias), snapshot.Settings.Retention.SupportedRedriveHorizon, command.SafeCode);
        MessageRedriveResult? candidate = null;
        TransactionCompletion? completion = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            await RunAsync((context, token) => store.RedriveAsync(context, transition, token), cancellationToken,
                (value, fact) =>
                {
                    candidate = value;
                    completion = fact;
                }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
        }
        bool committed = completion?.Outcome == TransactionOutcome.Committed;
        if (committed && candidate?.Disposition == MessageRedriveDisposition.Prepared)
        {
            NotifyPublisher();
        }
        await AuditAsync(new(caller, subscription.Settings.RedriveCapabilities, target,
            committed ? SecurityOperationOutcome.Completed : SecurityOperationOutcome.Failed,
            committed ? command.SafeCode : "messaging.redrive.unconfirmed", command.MessageId.ToString("D")), primary, cancellationToken).ConfigureAwait(false);
        MessageRedriveResult result = candidate ?? throw new InvalidOperationException("The store returned no redrive result.");
        return new(authorization, result.Disposition, result.HandoffRecordId, result.Record?.Generation, completion);
    }

    public async Task<MessageAdminMaintenanceResult> MaintainAsync(int maximumRecords = 256, CancellationToken cancellationToken = default)
    {
        MessageText.Range(maximumRecords, 1, 256, nameof(maximumRecords));
        if (accepting?.Invoke() == false)
        {
            return new(MessageAdministrationDisposition.Unavailable);
        }
        if (!resources.TryAcquireDelivery(out IDisposable? admission))
        {
            return new(MessageAdministrationDisposition.Unavailable);
        }
        using IDisposable? work = admission;
        CapabilityTarget target = StoreTarget();
        MessageAdministrationDisposition authorization = await AuthorizeAsync(snapshot.Settings.MaintenanceCapabilities, target, cancellationToken).ConfigureAwait(false);
        if (authorization != MessageAdministrationDisposition.Executed)
        {
            return new(authorization);
        }
        CallerContext caller = callers.Current ?? throw new InvalidOperationException("Administrative work requires an established current caller.");
        MessageMaintenanceCounts? candidate = null;
        TransactionCompletion? completion = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            await RunAsync(async (context, token) =>
            {
                MessageMaintenanceCounts counts = await store.MaintainAsync(context,
                    new(identity.Application, identity.Environment, snapshot.Settings.Retention, maximumRecords), token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The store returned no maintenance counts.");
                if (counts.PayloadsCleared < 0 || counts.ProcessingDeleted < 0 || counts.TerminalDeleted < 0
                    || (long)counts.PayloadsCleared + counts.ProcessingDeleted + counts.TerminalDeleted > maximumRecords)
                {
                    throw new InvalidOperationException("The store returned invalid bounded maintenance counts.");
                }
                return counts;
            }, cancellationToken, (value, fact) =>
            {
                candidate = value;
                completion = fact;
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
        }
        await AuditAsync(new(caller, snapshot.Settings.MaintenanceCapabilities, target,
            completion?.Outcome == TransactionOutcome.Committed ? SecurityOperationOutcome.Completed : SecurityOperationOutcome.Failed,
            completion?.Outcome == TransactionOutcome.Committed ? "messaging.maintenance.completed" : "messaging.maintenance.unconfirmed"), primary, cancellationToken).ConfigureAwait(false);
        return new(authorization, candidate, completion);
    }

    private async Task<MessageAdministrationDisposition> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (capabilities.Count == 0)
        {
            return MessageAdministrationDisposition.Disabled;
        }
        AuthorizationDecision decision = await authorizer.AuthorizeAsync(capabilities, target, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The authorizer returned no complete decision.");
        token.ThrowIfCancellationRequested();
        return decision.IsAllowed ? MessageAdministrationDisposition.Executed : MessageAdministrationDisposition.Denied;
    }

    private async Task<(T Value, TransactionCompletion Completion)> RunAsync<T>(
        Func<ILocalTransactionContext, CancellationToken, Task<T>> work, CancellationToken token,
        Action<T, TransactionCompletion?>? settled = null)
    {
        ILocalTransaction? owner = null;
        T value = default!;
        TransactionCompletion? completion = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            owner = await transactions.BeginAsync(database, new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The transaction factory returned no administrative owner.");
            if (owner.Context.DatabaseName != database || owner.Context.Isolation != TransactionIsolation.ReadCommitted)
            {
                throw new InvalidOperationException("Administrative work requires the selected store database and ReadCommitted isolation.");
            }
            value = await work(owner.Context, token).ConfigureAwait(false);
            if (value is null)
            {
                throw new InvalidOperationException("The store returned no administrative result.");
            }
            completion = await owner.CommitAsync(token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
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
                    primary ??= ExceptionDispatchInfo.Capture(error);
                }
            }
        }
        completion ??= owner?.Completion;
        settled?.Invoke(value, completion);
        primary?.Throw();
        return (value, completion ?? throw new InvalidOperationException("The administrative owner did not publish completion."));
    }

    private MessageSubscriptionDefinition GetSubscription(string alias) => snapshot.Subscriptions.SingleOrDefault(item => item.Alias == alias)
        ?? throw new ArgumentException("Use a registered receiving subscription.", nameof(alias));

    private CapabilityTarget StoreTarget() => new(identity.Application, identity.Environment, "messaging.store", snapshot.Settings.StoreAlias);

    private async Task AuditAsync(SecurityOperationAudit record, ExceptionDispatchInfo? primary, CancellationToken token)
    {
        try
        {
            await audit.OperationCompletedAsync(record, token).ConfigureAwait(false);
        }
        catch (Exception) when (primary is not null)
        {
            primary!.Throw();
        }
        primary?.Throw();
    }

    private void NotifyPublisher()
    {
        try
        {
            wake?.Invoke();
        }
        catch (Exception)
        {
            // A lost hint does not change committed responsibility; the existing publisher poll remains available.
        }
    }

    private sealed record InspectionPage(IReadOnlyList<MessageInspectionRecord> Records, MessageProcessingInspection? Processing);
}
