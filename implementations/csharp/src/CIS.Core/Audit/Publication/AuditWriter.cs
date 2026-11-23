using CIS.Messaging;
using CIS.Security;
using CIS.Transactions;

namespace CIS.Audit;

// The existing Messaging ports retain transaction and delivery ownership. This owner only admits
// and joins its added call boundary; no completion token or queued copy can stand in for settlement.
internal sealed class AuditWriter : IAuditWriter, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AuditOptions options;
    private readonly LocalApplicationIdentity identity;
    private readonly IMessageOutbox outbox;
    private readonly IMessagingClient client;
    private bool accepting;
    private bool closing;
    private int active;

    internal AuditWriter(AuditOptions options, LocalApplicationIdentity identity, IMessageOutbox outbox, IMessagingClient client)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(client);

        this.options = options;
        this.identity = identity;
        this.outbox = outbox;
        this.client = client;
    }

    public async Task<AuditStageResult> StageAsync(ILocalTransactionContext context, CapturedAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IDisposable? operation = null;
        try
        {
            ArgumentNullException.ThrowIfNull(record);
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryEnter(out operation))
            {
                return RefuseStage(context, record.AuditId, NotAccepting());
            }
            ValidateContext(context);
            if (ValidateRecord(record.Record) is { } refusal)
            {
                return RefuseStage(context, record.AuditId, refusal);
            }

            Task<MessageStageResult> pending = outbox.PublishAsync(context, options.RouteAlias, record.Record,
                Diagnostics(record.Record), cancellationToken)
                ?? throw new InvalidOperationException("The Messaging outbox returned no staging operation.");
            MessageStageResult result = await pending.ConfigureAwait(false)
                ?? throw new InvalidOperationException("The Messaging outbox returned no staging result.");
            if (result.Disposition != MessageStageDisposition.Staged)
            {
                MessageOutbox.Poison(context, "audit.stage_refused");
            }
            return new(record.AuditId, result);
        }
        catch
        {
            MessageOutbox.Poison(context, cancellationToken.IsCancellationRequested ? "audit.stage_cancelled" : "audit.stage_failed");
            throw;
        }
        finally
        {
            // Staging faults and their rollback-only cleanup remain admitted until they settle.
            operation?.Dispose();
        }
    }

    public async Task<AuditSubmissionResult> SubmitAsync(CapturedAuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryEnter(out IDisposable? operation))
        {
            return RefuseSubmission(record.AuditId, NotAccepting());
        }
        using (operation)
        {
            if (ValidateRecord(record.Record) is { } refusal)
            {
                return RefuseSubmission(record.AuditId, refusal);
            }

            Task<MessageSubmission> pending = client.PublishAsync(options.RouteAlias, record.Record,
                Diagnostics(record.Record), cancellationToken)
                ?? throw new InvalidOperationException("The Messaging client returned no submission operation.");
            MessageSubmission result = await pending.ConfigureAwait(false)
                ?? throw new InvalidOperationException("The Messaging client returned no submission result.");
            // Never recheck cancellation here: an actually established late commit is still committed.
            return new(record.AuditId, result);
        }
    }

    internal void BeginAccepting()
    {
        lock (gate)
        {
            if (closing)
            {
                throw new InvalidOperationException("A closed Audit writer cannot restart.");
            }
            accepting = true;
        }
    }

    internal void BeginClose()
    {
        lock (gate)
        {
            accepting = false;
            closing = true;
            Settle();
        }
    }

    internal Task DrainAsync()
    {
        BeginClose();
        return drained.Task;
    }

    public ValueTask DisposeAsync() => new(DrainAsync());

    private bool TryEnter(out IDisposable? operation)
    {
        lock (gate)
        {
            if (!accepting || closing)
            {
                operation = null;
                return false;
            }
            active++;
            operation = new PublicationLease(this);
            return true;
        }
    }

    private void Exit()
    {
        lock (gate)
        {
            active--;
            Settle();
        }
    }

    private void Settle()
    {
        if (closing && active == 0)
        {
            drained.TrySetResult();
        }
    }

    private MessageError? ValidateRecord(AuditRecord record)
    {
        if (record.Producer.Application != identity.Application || record.Producer.Environment != identity.Environment)
        {
            return new("audit.producer_mismatch", "Audit source identity differs from the configured local producer.");
        }
        if (record.OriginalMemory.Length > options.Limits.MaximumPayloadBytes)
        {
            return new("audit.record_too_large", "The complete audit payload exceeds the selected byte limit.");
        }
        if (!MessageWire.ValidateJson(record.OriginalMemory.Span, options.Limits.MaximumJsonDepth))
        {
            return new("audit.record_invalid", "The audit payload does not fit the selected JSON profile.");
        }
        return null;
    }

    private static void ValidateContext(ILocalTransactionContext context)
    {
        if (context.Isolation != TransactionIsolation.ReadCommitted || context.IsRollbackOnly)
        {
            throw new InvalidOperationException("Audit staging requires the original non-poisoned ReadCommitted participant.");
        }
    }

    private static MessageSendOptions Diagnostics(AuditRecord record)
    {
        string? correlation = record.CorrelationId;
        if (correlation is not null && MessageText.Utf8.GetByteCount(correlation) > 128)
        {
            correlation = null;
        }
        return new(correlationId: correlation);
    }

    private static MessageError NotAccepting()
        => new("audit.not_accepting", "Audit publication admission is not open.");

    private static AuditStageResult RefuseStage(ILocalTransactionContext context, Guid auditId, MessageError error)
    {
        MessageOutbox.Poison(context, "audit.stage_refused");
        return new(auditId, new(MessageStageDisposition.Unavailable, error: error));
    }

    private static AuditSubmissionResult RefuseSubmission(Guid auditId, MessageError error)
        => new(auditId, new(MessageSubmissionOutcome.NotSubmitted, error: error));

    private sealed class PublicationLease(AuditWriter owner) : IDisposable
    {
        private AuditWriter? owner = owner;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Exit();
    }
}
