using System.Data;
using System.Data.Common;
using CIS.Transactions;

namespace CIS.Messaging.PostgreSql;

public sealed partial class PostgreSqlMessagingStore
{
    private async Task ValidateReplyAnchorAsync(DbConnection connection, DbTransaction transaction, MessageOutgoingRecord record, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, record.ProcessingKey is not null ? $"""
            SELECT true FROM {Processing} WHERE {KeyPredicate} AND claim_xid=pg_current_xact_id()
                AND state IN(0,3) AND reply_id IS NULL AND ordinal=@ordinal AND generation=@generation
            """ : $"""
            SELECT true FROM {Rejections} AS r JOIN {Outbox} AS h ON h.record_id=r.handoff_id
            WHERE r.record_id=@rejection AND r.application=@application AND r.environment=@environment
                AND r.created_xid=pg_current_xact_id() AND r.reply_id IS NULL AND r.message_id=@message
                AND h.route_alias=@route AND @ordinal=1 AND @generation=0
            """);
        if (record.ProcessingKey is { } key)
        {
            AddKey(command, key);
        }
        else
        {
            AddOwner(command, record.Application, record.Environment);
            Parameter(command, "rejection", DbType.Guid, record.RejectionId);
            Parameter(command, "message", DbType.Guid, record.Message.Envelope.InReplyToMessageId);
            Parameter(command, "route", DbType.String, record.Destination.RouteAlias);
        }
        Parameter(command, "ordinal", DbType.Int32, record.DeliveryOrdinal);
        Parameter(command, "generation", DbType.Int32, record.Generation);
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not true)
        {
            throw new InvalidOperationException("Reply staging requires its matching freshly owned canonical claim or separate rejection.");
        }
    }

    /// <inheritdoc />
    public Task<bool> AttachReplyAsync(ILocalTransactionContext context, MessageReplyAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        MessageText.Id(attachment.ReplyRecordId, nameof(attachment.ReplyRecordId));
        MessageText.Range(attachment.Generation, 0, int.MaxValue, nameof(attachment.Generation));
        bool canonical = attachment.ProcessingKey is not null;
        if (canonical == (attachment.RejectionId is not null))
        {
            throw new ArgumentException("Select exactly one canonical Terminal or separate rejection anchor.", nameof(attachment));
        }
        if (canonical)
        {
            if (attachment.ExpectedVersion is null or < 1 || attachment.ExpectedOrdinal is null)
            {
                throw new ArgumentException("A canonical reply attachment requires its freshly secured version and ordinal.", nameof(attachment));
            }
            MessageText.Range(attachment.ExpectedOrdinal.Value, 1, 32, nameof(attachment.ExpectedOrdinal));
        }
        else
        {
            MessageText.Id(attachment.RejectionId!.Value, nameof(attachment.RejectionId));
            if (attachment.ExpectedVersion is not null || attachment.ExpectedOrdinal is not null || attachment.Generation != 0)
            {
                throw new ArgumentException("A separate rejection has no canonical version, ordinal or redrive generation.", nameof(attachment));
            }
        }
        return RunAsync(context, async (connection, transaction, token) =>
        {
            await using var command = Command(connection, transaction, canonical ? $"""
                UPDATE {Processing} AS p SET reply_id=@reply,version=version+1,updated_at=clock_timestamp()
                WHERE p.application=@application AND p.environment=@environment AND p.subscription=@subscription AND p.message_id=@message
                    AND p.state=3 AND p.claim_xid=pg_current_xact_id() AND p.reply_id IS NULL
                    AND p.version=@version AND p.version<9223372036854775807 AND p.ordinal=@ordinal AND p.generation=@generation
                    AND EXISTS(SELECT 1 FROM {Outbox} AS o WHERE o.record_id=@reply AND o.purpose=3 AND o.state=0
                        AND o.staged_xid=pg_current_xact_id() AND o.rejection_id IS NULL
                        AND o.application=p.application AND o.environment=p.environment
                        AND o.processing_application=p.application AND o.processing_environment=p.environment
                        AND o.subscription=p.subscription AND o.original_message_id=p.message_id AND o.reply_to_message_id=p.message_id
                        AND o.ordinal=p.ordinal AND o.generation=p.generation)
                RETURNING true
                """ : $"""
                UPDATE {Rejections} AS r SET reply_id=@reply
                WHERE r.record_id=@rejection AND r.created_xid=pg_current_xact_id() AND r.reply_id IS NULL
                    AND EXISTS(SELECT 1 FROM {Outbox} AS o WHERE o.record_id=@reply AND o.purpose=3 AND o.state=0
                        AND o.staged_xid=pg_current_xact_id() AND o.processing_application IS NULL AND o.rejection_id=r.record_id
                        AND o.application=r.application AND o.environment=r.environment AND o.reply_to_message_id=r.message_id
                        AND o.ordinal=1 AND o.generation=0)
                RETURNING true
                """);
            Parameter(command, "reply", DbType.Guid, attachment.ReplyRecordId);
            if (attachment.ProcessingKey is { } key)
            {
                AddKey(command, key);
                Parameter(command, "version", DbType.Int64, attachment.ExpectedVersion);
                Parameter(command, "ordinal", DbType.Int32, attachment.ExpectedOrdinal);
                Parameter(command, "generation", DbType.Int32, attachment.Generation);
            }
            else
            {
                Parameter(command, "rejection", DbType.Guid, attachment.RejectionId);
            }
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
        }, cancellationToken);
    }
}
