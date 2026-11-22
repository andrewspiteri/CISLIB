using System.Data;
using System.Data.Common;
using CIS.Transactions;

namespace CIS.Messaging.PostgreSql;

public sealed partial class PostgreSqlMessagingStore
{
    /// <inheritdoc />
    public async Task<MessageStageReceipt> StageAsync(ILocalTransactionContext context, MessageOutgoingRecord record, CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        try
        {
            ValidateOutgoing(record);
            await RunAsync(context, async (connection, transaction, token) =>
            {
                await InitializeBudgetAsync(connection, transaction, record.Application, record.Environment, token).ConfigureAwait(false);
                await InsertOutgoingAsync(connection, transaction, record, token).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
            return new(record.RecordId, record.Message.Envelope.MessageId, context.DatabaseName,
                record.Application, record.Environment, record.Message.Digest);
        }
        catch
        {
            try
            {
                context.MarkRollbackOnly(cancellationToken.IsCancellationRequested ? "messaging.stage_cancelled" : "messaging.stage_failed");
            }
            catch (InvalidOperationException)
            {
                // A concurrently completed/misused owner must not replace the original staging fault.
            }
            throw;
        }
    }

    private async Task InsertOutgoingAsync(DbConnection connection, DbTransaction transaction, MessageOutgoingRecord record, CancellationToken token)
    {
        if (record.Purpose == MessageRecordPurpose.Reply)
        {
            await ValidateReplyAnchorAsync(connection, transaction, record, token).ConfigureAwait(false);
        }
        await AccountAsync(connection, transaction, record.Application, record.Environment, record.Message.Length, token).ConfigureAwait(false);
        await using DbCommand command = Command(connection, transaction, $"""
            INSERT INTO {Outbox}
                (record_id, application, environment, message_id, route_alias, target_subscription, reply_application, reply_environment, reply_endpoint,
                 purpose, digest, envelope, encoded_length, processing_application, processing_environment, subscription, original_message_id,
                 ordinal, generation, provenance, eligible_at, safe_code, payload_retention_micros, rejection_id, reply_to_message_id)
            VALUES (@id, @application, @environment, @message, @route, @target_subscription, @reply_application, @reply_environment, @reply_endpoint,
                 @purpose, @digest, @envelope, @length, @processing_application, @processing_environment, @subscription, @original,
                 @ordinal, @generation, @provenance, clock_timestamp() + @delay::bigint * INTERVAL '1 microsecond', @code, @retention, @rejection, @reply_to)
            """);
        AddOwner(command, record.Application, record.Environment);
        Parameter(command, "id", DbType.Guid, record.RecordId);
        Parameter(command, "message", DbType.Guid, record.Message.Envelope.MessageId);
        Parameter(command, "route", DbType.String, record.Destination.RouteAlias);
        Parameter(command, "target_subscription", DbType.String, record.TargetSubscription ?? record.ProcessingKey?.Subscription);
        Parameter(command, "reply_application", DbType.String, record.Destination.ReplyRoute?.Application);
        Parameter(command, "reply_environment", DbType.String, record.Destination.ReplyRoute?.Environment);
        Parameter(command, "reply_endpoint", DbType.Guid, record.Destination.ReplyRoute?.EndpointId);
        Parameter(command, "purpose", DbType.Int16, (short)record.Purpose);
        Parameter(command, "digest", DbType.Binary, record.Message.Digest.CopyBytes());
        Parameter(command, "envelope", DbType.Binary, NativeBody(record.Message));
        Parameter(command, "length", DbType.Int32, record.Message.Length);
        Parameter(command, "processing_application", DbType.String, record.ProcessingKey?.Application);
        Parameter(command, "processing_environment", DbType.String, record.ProcessingKey?.Environment);
        Parameter(command, "subscription", DbType.String, record.ProcessingKey?.Subscription);
        Parameter(command, "original", DbType.Guid, record.ProcessingKey?.MessageId);
        Parameter(command, "rejection", DbType.Guid, record.RejectionId);
        Parameter(command, "reply_to", DbType.Guid,
            record.Purpose == MessageRecordPurpose.Reply ? record.Message.Envelope.InReplyToMessageId : null);
        Parameter(command, "ordinal", DbType.Int32, record.DeliveryOrdinal);
        Parameter(command, "generation", DbType.Int32, record.Generation);
        Parameter(command, "provenance", DbType.String, record.OriginalProvenance is null ? null : Json(record.OriginalProvenance));
        Parameter(command, "delay", DbType.Int64, Microseconds(record.Delay));
        Parameter(command, "code", DbType.String, record.DiagnosticCode);
        Parameter(command, "retention", DbType.Int64, settings.Retention.PublishedPayloadRetention is { } retention ? Microseconds(retention) : null);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageReservation>> ReserveAsync(ILocalTransactionContext context, MessageReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOwner(request.Application, request.Environment);
        MessageText.Id(request.Owner, nameof(request.Owner));
        MessageText.Range(request.MaximumRecords, 1, 32, nameof(request.MaximumRecords));
        TimeSpan duration = MessageText.Duration(request.Duration ?? TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(15), nameof(request.Duration));
        return RunAsync<IReadOnlyList<MessageReservation>>(context, async (connection, transaction, token) =>
        {
            await using DbCommand command = Command(connection, transaction, $"""
                WITH selected AS
                (
                    SELECT record_id FROM {Outbox}
                    WHERE application=@application AND environment=@environment AND envelope IS NOT NULL
                        AND (state=0 AND eligible_at<=clock_timestamp() OR state=1 AND reserved_until<=clock_timestamp())
                        AND reservation_version<9223372036854775807
                    ORDER BY eligible_at, record_id FOR UPDATE SKIP LOCKED LIMIT @maximum
                )
                UPDATE {Outbox} AS outgoing SET state=1, reservation_owner=@owner,
                    reservation_version=outgoing.reservation_version+1,
                    reserved_until=clock_timestamp()+@duration::bigint*INTERVAL '1 microsecond',
                    attempts=LEAST(outgoing.attempts::bigint+1,2147483647)::integer, updated_at=clock_timestamp()
                FROM selected WHERE outgoing.record_id=selected.record_id
                RETURNING outgoing.record_id,outgoing.message_id,outgoing.route_alias,outgoing.reply_application,outgoing.reply_environment,outgoing.reply_endpoint,
                    outgoing.purpose,outgoing.ordinal,outgoing.generation,outgoing.processing_application,outgoing.processing_environment,outgoing.subscription,outgoing.original_message_id,
                    outgoing.digest,outgoing.encoded_length,outgoing.reservation_version,outgoing.reserved_until,outgoing.created_at,outgoing.attempts,outgoing.safe_code,outgoing.target_subscription
                """);
            AddOwner(command, request.Application, request.Environment);
            Parameter(command, "owner", DbType.Guid, request.Owner);
            Parameter(command, "maximum", DbType.Int32, request.MaximumRecords);
            Parameter(command, "duration", DbType.Int64, Microseconds(duration));
            var result = new List<MessageReservation>();
            await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                MessageReplyRoute? reply = reader.IsDBNull(3) ? null : new(reader.GetString(3), reader.GetString(4), reader.GetGuid(5));
                MessageProcessingKey? processing = reader.IsDBNull(9) ? null : new(reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetGuid(12));
                result.Add(new(reader.GetGuid(0), reader.GetGuid(1), request.Application, request.Environment,
                    new(reader.GetString(2), reply), (MessageRecordPurpose)reader.GetInt16(6), reader.GetInt32(7), reader.GetInt32(8), processing,
                    new(reader.GetFieldValue<byte[]>(13)), reader.GetInt32(14), request.Owner, reader.GetInt64(15), Time(reader, 16), Time(reader, 17),
                    reader.GetInt32(18), NullableText(reader, 19), NullableText(reader, 20)));
            }
            return result.AsReadOnly();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EncodedMessage?> ReadReservedMessageAsync(ILocalTransactionContext context, MessageReservation reservation,
        CancellationToken cancellationToken = default)
    {
        ValidateReservation(reservation);
        return RunAsync(context, async (connection, transaction, token) =>
        {
            EncodedMessage? message = await ReadEncodedAsync(connection, transaction, $"""
                SELECT encoded_length,envelope FROM {Outbox}
                WHERE record_id=@id AND application=@application AND environment=@environment
                    AND state=1 AND reservation_owner=@owner AND reservation_version=@version AND reserved_until>clock_timestamp()
                """, command => AddFence(command, reservation), token).ConfigureAwait(false);
            if (message is not null && (message.Length != reservation.EncodedLength || !message.Digest.Equals(reservation.Digest)
                || message.Envelope.MessageId != reservation.MessageId))
            {
                throw new InvalidDataException("Retained messaging body does not match its reservation metadata.");
            }
            return message;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RecordPublicationAsync(ILocalTransactionContext context, MessagePublicationUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateReservation(update.Reservation);
        ArgumentNullException.ThrowIfNull(update.Result);
        MessageText.Duration(update.RetryDelay, TimeSpan.Zero, TimeSpan.FromSeconds(60), nameof(update.RetryDelay));
        bool expiredReply = update.Result.Failure == MessagePublicationFailure.ReplyDestinationExpired;
        if (expiredReply && update.Reservation.Purpose != MessageRecordPurpose.Reply)
        {
            throw new ArgumentException("Only an explicitly classified obsolete reply destination can retire outgoing responsibility.", nameof(update));
        }
        short state = update.Result.Disposition == MessagePublicationDisposition.Confirmed ? (short)MessageOutboxState.Published
            : update.Result.Failure is MessagePublicationFailure.Unroutable or MessagePublicationFailure.Configuration or MessagePublicationFailure.ReplyDestinationExpired
                ? (short)MessageOutboxState.Blocked : (short)MessageOutboxState.Pending;
        return RunAsync(context, async (connection, transaction, token) =>
        {
            await using DbCommand command = Command(connection, transaction, $"""
                UPDATE {Outbox} SET state=@state, reply_expired=@expired, reservation_owner=NULL, reserved_until=NULL,
                    published_at=CASE WHEN @state=2 THEN clock_timestamp() ELSE published_at END,
                    payload_expires=CASE WHEN (@state=2 OR @expired) AND payload_retention_micros IS NOT NULL
                        THEN clock_timestamp()+payload_retention_micros*INTERVAL '1 microsecond' ELSE payload_expires END,
                    eligible_at=clock_timestamp()+@delay::bigint*INTERVAL '1 microsecond', updated_at=clock_timestamp(),
                    safe_code=CASE WHEN @expired THEN 'messaging.reply_destination_expired' ELSE @code END
                WHERE record_id=@id AND application=@application AND environment=@environment AND state=1
                    AND reservation_owner=@owner AND reservation_version=@version AND reserved_until>clock_timestamp()
                    AND (NOT @expired OR purpose=3)
                RETURNING true
                """);
            AddFence(command, update.Reservation);
            Parameter(command, "state", DbType.Int16, state);
            Parameter(command, "expired", DbType.Boolean, expiredReply);
            Parameter(command, "delay", DbType.Int64, Microseconds(update.RetryDelay));
            Parameter(command, "code", DbType.String, update.Result.SafeCode);
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MessageSubmissionObservation> ReadSubmissionAsync(ILocalTransactionContext context, MessageStageReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        MessageText.Id(receipt.RecordId, nameof(receipt.RecordId));
        MessageText.Id(receipt.MessageId, nameof(receipt.MessageId));
        ArgumentNullException.ThrowIfNull(receipt.Digest);
        ValidateOwner(receipt.Application, receipt.Environment);
        if (context.DatabaseName != receipt.DatabaseName)
        {
            throw new ArgumentException("The receipt belongs to another original database alias.", nameof(receipt));
        }
        return RunAsync(context, async (connection, transaction, token) =>
        {
            await using DbCommand command = Command(connection, transaction, $"SELECT message_id,digest FROM {Outbox} WHERE record_id=@id AND application=@application AND environment=@environment");
            AddOwner(command, receipt.Application, receipt.Environment);
            Parameter(command, "id", DbType.Guid, receipt.RecordId);
            await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return MessageSubmissionObservation.NotFound;
            }
            return reader.GetGuid(0) == receipt.MessageId && new MessageDigest(reader.GetFieldValue<byte[]>(1)).Equals(receipt.Digest)
                ? MessageSubmissionObservation.Found : MessageSubmissionObservation.ContentConflict;
        }, cancellationToken);
    }

    private static long Microseconds(TimeSpan value) => checked((value.Ticks + 9) / 10);

    private static void ValidateReservation(MessageReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        MessageText.Id(reservation.RecordId, nameof(reservation.RecordId));
        MessageText.Id(reservation.MessageId, nameof(reservation.MessageId));
        MessageText.Id(reservation.Owner, nameof(reservation.Owner));
        ValidateOwner(reservation.Application, reservation.Environment);
        ArgumentNullException.ThrowIfNull(reservation.Digest);
        ArgumentNullException.ThrowIfNull(reservation.Destination);
        MessageText.Range(reservation.DeliveryOrdinal, 1, 32, nameof(reservation.DeliveryOrdinal));
        MessageText.Range(reservation.Generation, 0, int.MaxValue, nameof(reservation.Generation));
        if (!Enum.IsDefined(reservation.Purpose) || reservation.AttemptCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservation));
        }
        if (reservation.TargetSubscription is { } target)
        {
            MessageText.Alias(target, nameof(reservation.TargetSubscription));
        }
        MessageText.Range(reservation.EncodedLength, 1, StorageLimits.MaximumEnvelopeBytes, nameof(reservation.EncodedLength));
        if (reservation.Version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(reservation.Version));
        }
    }

    private static void AddFence(DbCommand command, MessageReservation reservation)
    {
        AddOwner(command, reservation.Application, reservation.Environment);
        Parameter(command, "id", DbType.Guid, reservation.RecordId);
        Parameter(command, "owner", DbType.Guid, reservation.Owner);
        Parameter(command, "version", DbType.Int64, reservation.Version);
    }
}
