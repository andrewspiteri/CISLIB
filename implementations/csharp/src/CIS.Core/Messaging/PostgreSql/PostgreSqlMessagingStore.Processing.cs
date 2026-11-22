using System.Data;
using System.Data.Common;
using CIS.Transactions;

namespace CIS.Messaging.PostgreSql;

public sealed partial class PostgreSqlMessagingStore
{
    /// <inheritdoc />
    public Task<MessageProcessingRecord?> ReadProcessingAsync(ILocalTransactionContext context, MessageProcessingKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return RunAsync(context, async (connection, transaction, token) =>
            (await ReadProcessingRowAsync(connection, transaction, key, false, token).ConfigureAwait(false))?.Record, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MessageProcessingClaim> ClaimProcessingAsync(ILocalTransactionContext context, MessageProcessingAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        return RunAsync(context, async (connection, transaction, token) =>
        {
            bool inserted = false;
            if (attempt.Purpose == MessageRecordPurpose.Application)
            {
                await InitializeBudgetAsync(connection, transaction, attempt.Key.Application, attempt.Key.Environment, token).ConfigureAwait(false);
                inserted = await InsertProcessingAsync(connection, transaction, attempt, token).ConfigureAwait(false);
            }
            ProcessingRow? row;
            try
            {
                // A new statement snapshot after unique arbitration observes a concurrently committed winner.
                row = await ReadProcessingRowAsync(connection, transaction, attempt.Key, true, token).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return new MessageProcessingClaim(MessageClaimDisposition.Corrupt);
            }
            if (row is null)
            {
                return new MessageProcessingClaim(MessageClaimDisposition.InvalidOrdinal);
            }
            MessageClaimDisposition? incompatible = CompareAttempt(row.Record, attempt);
            if (incompatible is { } mismatch)
            {
                return new MessageProcessingClaim(mismatch, row.Record);
            }
            if (inserted)
            {
                return new MessageProcessingClaim(MessageClaimDisposition.Acquired, row.Record);
            }
            if (row.Record.Disposition == MessageProcessingDisposition.Handled)
            {
                return new MessageProcessingClaim(MessageClaimDisposition.HandledDuplicate, row.Record);
            }
            if (row.Record.Disposition == MessageProcessingDisposition.Terminal)
            {
                return new MessageProcessingClaim(MessageClaimDisposition.Terminal, row.Record);
            }
            if (row.Record.Disposition == MessageProcessingDisposition.RetryScheduled)
            {
                if (attempt.Generation < row.Record.Generation || attempt.DeliveryOrdinal < row.Record.DeliveryOrdinal)
                {
                    return new MessageProcessingClaim(MessageClaimDisposition.RetryScheduled, row.Record);
                }
                if (!MatchesScheduled(row.Record, attempt)
                    || !await VerifyHandoffAsync(connection, transaction, row.Record, attempt.Purpose, token).ConfigureAwait(false))
                {
                    return new MessageProcessingClaim(MessageClaimDisposition.InvalidOrdinal, row.Record);
                }
                await using DbCommand command = Command(connection, transaction, $"""
                    UPDATE {Processing} SET state=0, claim_xid=pg_current_xact_id(), version=version+1, updated_at=clock_timestamp()
                    WHERE {KeyPredicate} AND version=@version AND version<9223372036854775807
                    RETURNING version
                    """);
                AddKey(command, attempt.Key);
                Parameter(command, "version", DbType.Int64, row.Record.Version);
                if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not long)
                {
                    return new MessageProcessingClaim(MessageClaimDisposition.Corrupt, row.Record);
                }
                return new MessageProcessingClaim(MessageClaimDisposition.Acquired,
                    (await ReadProcessingRowAsync(connection, transaction, attempt.Key, false, token).ConfigureAwait(false))!.Record);
            }
            // Ready must never be committed without successful finalization. A repeated own claim is unsupported too.
            return new MessageProcessingClaim(MessageClaimDisposition.Corrupt, row.Record);
        }, cancellationToken);
    }

    private async Task<bool> InsertProcessingAsync(DbConnection connection, DbTransaction transaction,
        MessageProcessingAttempt attempt, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            INSERT INTO {Processing}
                (record_id,application,environment,subscription,message_id,digest,envelope,provenance,operation_binding,ordinal,generation,
                 protection_retention_micros,terminal_retention_micros,payload_retention_micros,redrive_horizon_micros,delivery_horizon_micros)
            VALUES (@id,@application,@environment,@subscription,@message,@digest,@envelope,@provenance,@binding,@ordinal,@generation,
                @protection,@terminal,@payload_retention,@redrive_horizon,@delivery_horizon)
            ON CONFLICT (application,environment,subscription,message_id) DO NOTHING RETURNING true
            """);
        AddKey(command, attempt.Key);
        Parameter(command, "id", DbType.Guid, Guid.NewGuid());
        Parameter(command, "digest", DbType.Binary, attempt.Message.Digest.CopyBytes());
        Parameter(command, "envelope", DbType.Binary, NativeBody(attempt.Message));
        Parameter(command, "provenance", DbType.String, Json(attempt.Provenance));
        Parameter(command, "binding", DbType.String, attempt.OperationBinding is null ? null : Json(attempt.OperationBinding));
        Parameter(command, "ordinal", DbType.Int32, attempt.DeliveryOrdinal);
        Parameter(command, "generation", DbType.Int32, attempt.Generation);
        Parameter(command, "protection", DbType.Int64, settings.Retention.InboxProtectionRetention is { } protection ? Microseconds(protection) : null);
        Parameter(command, "terminal", DbType.Int64, settings.Retention.TerminalRetention is { } terminal ? Microseconds(terminal) : null);
        Parameter(command, "payload_retention", DbType.Int64, settings.Retention.PublishedPayloadRetention is { } payload ? Microseconds(payload) : null);
        Parameter(command, "redrive_horizon", DbType.Int64, settings.Retention.SupportedRedriveHorizon is { } redrive ? Microseconds(redrive) : null);
        Parameter(command, "delivery_horizon", DbType.Int64, settings.Retention.MaximumDeliveryAge is { } delivery ? Microseconds(delivery) : null);
        bool inserted = await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
        if (inserted)
        {
            await AccountAsync(connection, transaction, attempt.Key.Application, attempt.Key.Environment, attempt.Message.Length, token).ConfigureAwait(false);
        }
        return inserted;
    }

    private static MessageClaimDisposition? CompareAttempt(MessageProcessingRecord record, MessageProcessingAttempt attempt)
    {
        if (!record.Digest.Equals(attempt.Message.Digest))
        {
            return MessageClaimDisposition.ContentConflict;
        }
        if (!SameEvidence(record, attempt))
        {
            return MessageClaimDisposition.ProvenanceConflict;
        }
        if (attempt.Generation > record.Generation || attempt.Generation == record.Generation && attempt.DeliveryOrdinal > record.DeliveryOrdinal)
        {
            return MessageClaimDisposition.InvalidOrdinal;
        }
        return null;
    }

    private static bool MatchesScheduled(MessageProcessingRecord record, MessageProcessingAttempt attempt)
        => record.Generation == attempt.Generation && record.DeliveryOrdinal == attempt.DeliveryOrdinal
            && record.HandoffRecordId == attempt.HandoffRecordId
            && attempt.Purpose is MessageRecordPurpose.Retry or MessageRecordPurpose.Redrive;

    private async Task<bool> VerifyHandoffAsync(DbConnection connection, DbTransaction transaction,
        MessageProcessingRecord record, MessageRecordPurpose purpose, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            SELECT true FROM {Outbox} WHERE record_id=@id AND application=@application AND environment=@environment
                AND processing_application=@application AND processing_environment=@environment AND subscription=@subscription
                AND original_message_id=@message AND message_id=@message AND purpose=@purpose AND ordinal=@ordinal
                AND generation=@generation AND digest=@digest AND provenance=@provenance AND target_subscription=@subscription
            """);
        AddKey(command, record.Key);
        Parameter(command, "id", DbType.Guid, record.HandoffRecordId);
        Parameter(command, "purpose", DbType.Int16, (short)purpose);
        Parameter(command, "ordinal", DbType.Int32, record.DeliveryOrdinal);
        Parameter(command, "generation", DbType.Int32, record.Generation);
        Parameter(command, "digest", DbType.Binary, record.Digest.CopyBytes());
        Parameter(command, "provenance", DbType.String, Json(record.Provenance));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }

    /// <inheritdoc />
    public Task<bool> CompleteProcessingAsync(ILocalTransactionContext context, MessageProcessingCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(completion.Key);
        if (completion.ExpectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(completion.ExpectedVersion));
        }
        MessageText.Range(completion.ExpectedOrdinal, 1, 32, nameof(completion.ExpectedOrdinal));
        MessageText.Range(completion.Generation, 0, int.MaxValue, nameof(completion.Generation));
        if (completion.ReplyRecordId is { } reply)
        {
            MessageText.Id(reply, nameof(completion.ReplyRecordId));
        }
        ValidateBinding(completion.Key, completion.OperationBinding);
        return RunAsync(context, async (connection, transaction, token) =>
        {
            await using DbCommand command = Command(connection, transaction, $"""
                UPDATE {Processing} SET state=1, reply_id=@reply, version=version+1, updated_at=clock_timestamp(),
                    payload_expires=CASE WHEN payload_retention_micros IS NULL THEN NULL
                        ELSE clock_timestamp()+payload_retention_micros*INTERVAL '1 microsecond' END,
                    protection_expires=CASE WHEN protection_retention_micros IS NULL THEN NULL
                        ELSE clock_timestamp()+protection_retention_micros*INTERVAL '1 microsecond' END
                WHERE {KeyPredicate} AND state=0 AND claim_xid=pg_current_xact_id() AND version=@version
                    AND version<9223372036854775807 AND ordinal=@ordinal AND generation=@generation
                    AND operation_binding IS NOT DISTINCT FROM @binding
                    AND (@reply IS NULL OR EXISTS(SELECT 1 FROM {Outbox} AS reply WHERE reply.record_id=@reply AND reply.purpose=3
                        AND reply.processing_application=@application AND reply.processing_environment=@environment
                        AND reply.subscription=@subscription AND reply.original_message_id=@message))
                RETURNING true
                """);
            AddKey(command, completion.Key);
            Parameter(command, "reply", DbType.Guid, completion.ReplyRecordId);
            Parameter(command, "version", DbType.Int64, completion.ExpectedVersion);
            Parameter(command, "ordinal", DbType.Int32, completion.ExpectedOrdinal);
            Parameter(command, "generation", DbType.Int32, completion.Generation);
            Parameter(command, "binding", DbType.String, completion.OperationBinding is null ? null : Json(completion.OperationBinding));
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MessageDispositionResult> SecureDispositionAsync(ILocalTransactionContext context, MessageDispositionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAttempt(request.Attempt);
        ValidateOutgoing(request.Handoff);
        MessageText.Code(request.SafeCode, nameof(request.SafeCode));
        MessageProcessingAttempt attempt = request.Attempt;
        bool retry = request.Disposition == MessageProcessingDisposition.RetryScheduled;
        if (request.Disposition is not (MessageProcessingDisposition.RetryScheduled or MessageProcessingDisposition.Terminal)
            || request.Handoff.Purpose != (retry ? MessageRecordPurpose.Retry : MessageRecordPurpose.Terminal)
            || request.Handoff.ProcessingKey != attempt.Key || !request.Handoff.Message.Digest.Equals(attempt.Message.Digest)
            || request.Handoff.OriginalProvenance != attempt.Provenance || request.Handoff.Generation != attempt.Generation
            || request.Handoff.DeliveryOrdinal != (retry ? attempt.DeliveryOrdinal + 1 : attempt.DeliveryOrdinal))
        {
            throw new ArgumentException("The failed attempt and its one targeted handoff must agree.", nameof(request));
        }
        return RunAsync(context, async (connection, transaction, token) =>
        {
            await InitializeBudgetAsync(connection, transaction, attempt.Key.Application, attempt.Key.Environment, token).ConfigureAwait(false);
            bool inserted = attempt.Purpose == MessageRecordPurpose.Application
                && await InsertProcessingAsync(connection, transaction, attempt, token).ConfigureAwait(false);
            ProcessingRow? row = await ReadProcessingRowAsync(connection, transaction, attempt.Key, true, token).ConfigureAwait(false);
            if (row is null)
            {
                return new MessageDispositionResult(MessageDispositionResultKind.InvalidOrdinal);
            }
            MessageClaimDisposition? mismatch = CompareAttempt(row.Record, attempt);
            if (mismatch is MessageClaimDisposition.ContentConflict or MessageClaimDisposition.ProvenanceConflict)
            {
                return new MessageDispositionResult(mismatch == MessageClaimDisposition.ContentConflict
                    ? MessageDispositionResultKind.ContentConflict : MessageDispositionResultKind.ProvenanceConflict, row.Record);
            }
            if (mismatch == MessageClaimDisposition.InvalidOrdinal)
            {
                return new MessageDispositionResult(MessageDispositionResultKind.InvalidOrdinal, row.Record);
            }
            if (row.Record.Disposition == MessageProcessingDisposition.Handled)
            {
                return new MessageDispositionResult(MessageDispositionResultKind.AlreadyHandled, row.Record, row.Record.ReplyRecordId);
            }
            if (attempt.Generation < row.Record.Generation || attempt.DeliveryOrdinal < row.Record.DeliveryOrdinal
                || row.Record.Disposition == MessageProcessingDisposition.Terminal)
            {
                return new MessageDispositionResult(MessageDispositionResultKind.AlreadySecured, row.Record, row.Record.HandoffRecordId);
            }
            bool matchingRetry = row.Record.Disposition == MessageProcessingDisposition.RetryScheduled && MatchesScheduled(row.Record, attempt)
                && await VerifyHandoffAsync(connection, transaction, row.Record, attempt.Purpose, token).ConfigureAwait(false);
            if (mismatch is not null || !inserted && !matchingRetry)
            {
                return new MessageDispositionResult(MessageDispositionResultKind.InvalidOrdinal, row.Record);
            }
            await InsertOutgoingAsync(connection, transaction, request.Handoff, token).ConfigureAwait(false);
            await using DbCommand update = Command(connection, transaction, $"""
                UPDATE {Processing} SET state=@state, ordinal=@ordinal, handoff_id=@handoff, safe_code=@code,
                    version=version+1, updated_at=clock_timestamp(),
                    payload_expires=CASE WHEN @state=3 AND payload_retention_micros IS NOT NULL
                        THEN clock_timestamp()+payload_retention_micros*INTERVAL '1 microsecond' ELSE NULL END,
                    terminal_expires=CASE WHEN @state=3 AND terminal_retention_micros IS NOT NULL
                        THEN clock_timestamp()+terminal_retention_micros*INTERVAL '1 microsecond' ELSE terminal_expires END
                WHERE {KeyPredicate} AND version=@version AND version<9223372036854775807 RETURNING true
                """);
            AddKey(update, attempt.Key);
            Parameter(update, "state", DbType.Int16, (short)request.Disposition);
            Parameter(update, "ordinal", DbType.Int32, request.Handoff.DeliveryOrdinal);
            Parameter(update, "handoff", DbType.Guid, request.Handoff.RecordId);
            Parameter(update, "code", DbType.String, request.SafeCode);
            Parameter(update, "version", DbType.Int64, row.Record.Version);
            if (await update.ExecuteScalarAsync(token).ConfigureAwait(false) is not true)
            {
                throw new InvalidDataException("The locked messaging disposition could not be finalized.");
            }
            MessageProcessingRecord current = (await ReadProcessingRowAsync(connection, transaction, attempt.Key, false, token).ConfigureAwait(false))!.Record;
            return new MessageDispositionResult(MessageDispositionResultKind.Secured, current, request.Handoff.RecordId);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordRejectionAsync(ILocalTransactionContext context, MessageRejection rejection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rejection);
        MessageText.Id(rejection.RejectionId, nameof(rejection.RejectionId));
        ValidateOwner(rejection.Application, rejection.Environment);
        MessageText.Alias(rejection.Subscription, nameof(rejection.Subscription));
        MessageText.Code(rejection.SafeCode, nameof(rejection.SafeCode));
        ArgumentNullException.ThrowIfNull(rejection.Message);
        ValidateOutgoing(rejection.Quarantine);
        if (rejection.Quarantine.Purpose != MessageRecordPurpose.Rejection
            || rejection.Quarantine.Application != rejection.Application || rejection.Quarantine.Environment != rejection.Environment
            || rejection.Quarantine.TargetSubscription != rejection.Subscription
            || !rejection.Quarantine.Message.Digest.Equals(rejection.Message.Digest))
        {
            throw new ArgumentException("The separate rejection and quarantine handoff must agree.", nameof(rejection));
        }
        return RunAsync(context, async (connection, transaction, token) =>
        {
            await InitializeBudgetAsync(connection, transaction, rejection.Application, rejection.Environment, token).ConfigureAwait(false);
            await InsertOutgoingAsync(connection, transaction, rejection.Quarantine, token).ConfigureAwait(false);
            await using DbCommand command = Command(connection, transaction, $"""
                INSERT INTO {Rejections}(record_id,application,environment,subscription,message_id,digest,provenance,handoff_id,safe_code,terminal_expires)
                VALUES (@id,@application,@environment,@subscription,@message,@digest,@provenance,@handoff,@code,
                    CASE WHEN @terminal::bigint IS NULL THEN NULL ELSE clock_timestamp()+@terminal*INTERVAL '1 microsecond' END)
                """);
            AddOwner(command, rejection.Application, rejection.Environment);
            Parameter(command, "id", DbType.Guid, rejection.RejectionId);
            Parameter(command, "subscription", DbType.String, rejection.Subscription);
            Parameter(command, "message", DbType.Guid, rejection.Message.Envelope.MessageId);
            Parameter(command, "digest", DbType.Binary, rejection.Message.Digest.CopyBytes());
            Parameter(command, "provenance", DbType.String, rejection.Provenance is null ? null : Json(rejection.Provenance));
            Parameter(command, "handoff", DbType.Guid, rejection.Quarantine.RecordId);
            Parameter(command, "code", DbType.String, rejection.SafeCode);
            Parameter(command, "terminal", DbType.Int64, settings.Retention.TerminalRetention is { } terminal ? Microseconds(terminal) : null);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }
}
