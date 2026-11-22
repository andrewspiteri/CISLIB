using System.Data;
using System.Data.Common;
using CIS.Transactions;

namespace CIS.Messaging.PostgreSql;

public sealed partial class PostgreSqlMessagingStore
{
    /// <inheritdoc />
    public Task<MessageRedriveResult> RedriveAsync(ILocalTransactionContext context, MessageRedriveTransition transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(transition.Key);
        ArgumentNullException.ThrowIfNull(transition.Destination);
        MessageText.Code(transition.SafeCode, nameof(transition.SafeCode));
        if (transition.ExpectedVersion < 1 || transition.Destination.ReplyRoute is not null)
        {
            throw new ArgumentException("Redrive selects an inspected terminal version and a configured consumer route.", nameof(transition));
        }
        if (transition.SupportedHorizon is { } horizon)
        {
            MessageText.Duration(horizon, TimeSpan.FromTicks(1), TimeSpan.FromDays(36500), nameof(transition.SupportedHorizon));
        }
        if (transition.SupportedHorizon != settings.Retention.SupportedRedriveHorizon)
        {
            throw new ArgumentException("Redrive uses the selected store's exact current supported horizon.", nameof(transition));
        }
        return RunAsync(context, async (connection, transaction, token) =>
        {
            ProcessingRow? row = await ReadProcessingRowAsync(connection, transaction, transition.Key, true, token).ConfigureAwait(false);
            if (row is null)
            {
                return new MessageRedriveResult(MessageRedriveDisposition.NotFound);
            }
            if (row.Record.Version != transition.ExpectedVersion || row.Record.Generation == int.MaxValue || row.Record.Version == long.MaxValue)
            {
                return new MessageRedriveResult(MessageRedriveDisposition.Conflict, row.Record);
            }
            if (row.Record.Disposition != MessageProcessingDisposition.Terminal)
            {
                return new MessageRedriveResult(MessageRedriveDisposition.NotTerminal, row.Record);
            }
            await using (DbCommand check = Command(connection, transaction, $"""
                SELECT (redrive_horizon_micros IS NULL OR created_at+redrive_horizon_micros*INTERVAL '1 microsecond'>=clock_timestamp())
                    AND (@horizon::bigint IS NULL OR created_at+@horizon*INTERVAL '1 microsecond'>=clock_timestamp())
                FROM {Processing} WHERE {KeyPredicate}
                """))
            {
                AddKey(check, transition.Key);
                Parameter(check, "horizon", DbType.Int64, transition.SupportedHorizon is { } selected ? Microseconds(selected) : null);
                if (await check.ExecuteScalarAsync(token).ConfigureAwait(false) is not true)
                {
                    return new MessageRedriveResult(MessageRedriveDisposition.OutsideHorizon, row.Record);
                }
            }
            EncodedMessage? message = await ReadEncodedAsync(connection, transaction,
                $"SELECT octet_length(envelope),envelope FROM {Processing} WHERE {KeyPredicate}", command => AddKey(command, transition.Key), token).ConfigureAwait(false);
            if (message is null)
            {
                return new MessageRedriveResult(MessageRedriveDisposition.ContentUnavailable, row.Record);
            }
            if (!message.Digest.Equals(row.Record.Digest) || message.Envelope.MessageId != transition.Key.MessageId)
            {
                throw new InvalidDataException("Retained redrive content conflicts with its protected identity.");
            }
            await InitializeBudgetAsync(connection, transaction, transition.Key.Application, transition.Key.Environment, token).ConfigureAwait(false);
            Guid handoff = Guid.NewGuid();
            int generation = checked(row.Record.Generation + 1);
            await InsertOutgoingAsync(connection, transaction, new(handoff, transition.Key.Application, transition.Key.Environment,
                transition.Destination, message, MessageRecordPurpose.Redrive, ProcessingKey: transition.Key, DeliveryOrdinal: 1,
                Generation: generation, DiagnosticCode: transition.SafeCode, OriginalProvenance: row.Record.Provenance,
                TargetSubscription: transition.Key.Subscription), token).ConfigureAwait(false);
            await using DbCommand update = Command(connection, transaction, $"""
                UPDATE {Processing} SET state=2, ordinal=1, generation=@generation, version=version+1, handoff_id=@handoff,
                    safe_code=@code, updated_at=clock_timestamp(), payload_expires=NULL, terminal_expires=NULL,reply_id=NULL
                WHERE {KeyPredicate} AND version=@version RETURNING true
                """);
            AddKey(update, transition.Key);
            Parameter(update, "generation", DbType.Int32, generation);
            Parameter(update, "handoff", DbType.Guid, handoff);
            Parameter(update, "code", DbType.String, transition.SafeCode);
            Parameter(update, "version", DbType.Int64, row.Record.Version);
            if (await update.ExecuteScalarAsync(token).ConfigureAwait(false) is not true)
            {
                throw new InvalidDataException("The locked redrive could not be finalized.");
            }
            return new MessageRedriveResult(MessageRedriveDisposition.Prepared,
                (await ReadProcessingRowAsync(connection, transaction, transition.Key, false, token).ConfigureAwait(false))!.Record, handoff);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageInspectionRecord>> InspectAsync(ILocalTransactionContext context, MessageInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOwner(request.Application, request.Environment);
        MessageText.Range(request.MaximumRecords, 1, 256, nameof(request.MaximumRecords));
        if (request.ProcessingKey is { } key && (key.Application != request.Application || key.Environment != request.Environment))
        {
            throw new ArgumentException("Inspection cannot combine another owner's processing identity.", nameof(request));
        }
        if (request.RouteAlias is { } alias)
        {
            MessageText.Alias(alias, nameof(request.RouteAlias));
        }
        if (request.AfterRecordId is { } after)
        {
            MessageText.Id(after, nameof(request.AfterRecordId));
        }
        return RunAsync<IReadOnlyList<MessageInspectionRecord>>(context, async (connection, transaction, token) =>
        {
            await using DbCommand command = Command(connection, transaction, $"""
                SELECT record_id,message_id,alias,state_name,updated_at,ordinal,generation,safe_code FROM
                (
                    SELECT record_id,message_id,route_alias AS alias,
                        CASE WHEN reply_expired THEN 'outbox.reply_destination_expired' WHEN state=0 THEN 'outbox.pending'
                            WHEN state=1 THEN 'outbox.reserved' WHEN state=2 THEN 'outbox.published' ELSE 'outbox.blocked' END AS state_name,
                        updated_at,ordinal,generation,safe_code
                    FROM {Outbox} WHERE application=@application AND environment=@environment
                        AND (@route::text IS NULL OR route_alias=@route)
                        AND (@subscription::text IS NULL OR subscription=@subscription AND original_message_id=@message)
                        AND (@after::uuid IS NULL OR record_id>@after)
                    UNION ALL
                    SELECT record_id,message_id,subscription,
                        CASE state WHEN 0 THEN 'inbox.ready' WHEN 1 THEN 'inbox.handled' WHEN 2 THEN 'inbox.retry_scheduled' ELSE 'inbox.terminal' END,
                        updated_at,ordinal,generation,safe_code
                    FROM {Processing} WHERE application=@application AND environment=@environment AND @route::text IS NULL
                        AND (@subscription::text IS NULL OR subscription=@subscription AND message_id=@message)
                        AND (@after::uuid IS NULL OR record_id>@after)
                    UNION ALL
                    SELECT record_id,message_id,subscription,'rejection',created_at,1,0,safe_code
                    FROM {Rejections} WHERE application=@application AND environment=@environment AND @route::text IS NULL
                        AND (@subscription::text IS NULL OR subscription=@subscription AND message_id=@message)
                        AND (@after::uuid IS NULL OR record_id>@after)
                ) AS records ORDER BY record_id LIMIT @maximum
                """);
            AddOwner(command, request.Application, request.Environment);
            Parameter(command, "route", DbType.String, request.RouteAlias);
            Parameter(command, "subscription", DbType.String, request.ProcessingKey?.Subscription);
            Parameter(command, "message", DbType.Guid, request.ProcessingKey?.MessageId);
            Parameter(command, "after", DbType.Guid, request.AfterRecordId);
            Parameter(command, "maximum", DbType.Int32, request.MaximumRecords);
            var result = new List<MessageInspectionRecord>();
            await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), Time(reader, 4),
                    reader.GetInt32(5), reader.GetInt32(6), NullableText(reader, 7)));
            }
            return result.AsReadOnly();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MessageMaintenanceCounts> MaintainAsync(ILocalTransactionContext context, MessageMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOwner(request.Application, request.Environment);
        ArgumentNullException.ThrowIfNull(request.Policy);
        MessageText.Range(request.MaximumRecords, 1, 256, nameof(request.MaximumRecords));
        if (!SamePolicy(request.Policy, settings.Retention))
        {
            throw new ArgumentException("Maintenance uses the selected store's exact captured policy.", nameof(request));
        }
        return RunAsync(context, async (connection, transaction, token) =>
        {
            await InitializeBudgetAsync(connection, transaction, request.Application, request.Environment, token).ConfigureAwait(false);
            int cleared = 0;
            int deleted = 0;
            int terminal = 0;
            long bytes = 0;
            int remaining = request.MaximumRecords;
            if (request.Policy.PublishedPayloadRetention is not null)
            {
                (int count, long size) = await ClearOutboxAsync(connection, transaction, request, remaining, token).ConfigureAwait(false);
                cleared += count;
                bytes += size;
                remaining -= count;
                if (remaining > 0)
                {
                    (count, size) = await ClearProcessingAsync(connection, transaction, request, remaining, token).ConfigureAwait(false);
                    cleared += count;
                    bytes += size;
                    remaining -= count;
                }
            }
            if (remaining > 0 && (request.Policy.InboxProtectionRetention is not null || request.Policy.TerminalRetention is not null))
            {
                (int count, long size, int terminals) = await DeleteProcessingAsync(connection, transaction, request, remaining, token).ConfigureAwait(false);
                deleted += count - terminals;
                terminal += terminals;
                bytes += size;
                remaining -= count;
            }
            if (remaining > 0 && request.Policy.TerminalRetention is not null)
            {
                terminal += await DeleteRejectionsAsync(connection, transaction, request, remaining, token).ConfigureAwait(false);
            }
            await AccountAsync(connection, transaction, request.Application, request.Environment, -bytes, token).ConfigureAwait(false);
            return new MessageMaintenanceCounts(cleared, deleted, terminal);
        }, cancellationToken);
    }

    private async Task<(int Count, long Bytes)> ClearOutboxAsync(DbConnection connection, DbTransaction transaction,
        MessageMaintenanceRequest request, int maximum, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            WITH selected AS
            (
                SELECT record_id,octet_length(envelope) AS bytes FROM {Outbox} AS o
                WHERE application=@application AND environment=@environment AND (state=2 OR reply_expired) AND envelope IS NOT NULL
                    AND payload_expires<=clock_timestamp()
                    AND NOT EXISTS(SELECT 1 FROM {Processing} AS p WHERE (p.handoff_id=o.record_id OR p.reply_id=o.record_id) AND p.state<>1)
                    AND NOT EXISTS(SELECT 1 FROM {Rejections} AS r WHERE r.handoff_id=o.record_id OR r.reply_id=o.record_id)
                ORDER BY payload_expires,record_id FOR UPDATE OF o SKIP LOCKED LIMIT @maximum
            ), changed AS
            (
                UPDATE {Outbox} AS o SET envelope=NULL FROM selected WHERE o.record_id=selected.record_id RETURNING selected.bytes
            ) SELECT count(*)::integer,coalesce(sum(bytes),0)::bigint FROM changed
            """);
        AddAdmin(command, request.Application, request.Environment, maximum);
        return await CountsAsync(command, token).ConfigureAwait(false);
    }

    private async Task<(int Count, long Bytes)> ClearProcessingAsync(DbConnection connection, DbTransaction transaction,
        MessageMaintenanceRequest request, int maximum, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            WITH selected AS
            (
                SELECT record_id,octet_length(envelope) AS bytes FROM {Processing} AS p
                WHERE application=@application AND environment=@environment AND state IN (1,3) AND envelope IS NOT NULL
                    AND payload_expires<=clock_timestamp()
                    AND (state=1 OR redrive_horizon_micros IS NOT NULL AND created_at+redrive_horizon_micros*INTERVAL '1 microsecond'<=clock_timestamp())
                    AND NOT EXISTS(SELECT 1 FROM {Outbox} AS o WHERE o.processing_application=p.application
                        AND o.processing_environment=p.environment AND o.subscription=p.subscription AND o.original_message_id=p.message_id AND o.state<>2 AND NOT o.reply_expired)
                ORDER BY payload_expires,record_id FOR UPDATE OF p SKIP LOCKED LIMIT @maximum
            ), changed AS
            (
                UPDATE {Processing} AS p SET envelope=NULL FROM selected WHERE p.record_id=selected.record_id RETURNING selected.bytes
            ) SELECT count(*)::integer,coalesce(sum(bytes),0)::bigint FROM changed
            """);
        AddAdmin(command, request.Application, request.Environment, maximum);
        return await CountsAsync(command, token).ConfigureAwait(false);
    }

    private async Task<(int Count, long Bytes, int Terminal)> DeleteProcessingAsync(DbConnection connection, DbTransaction transaction,
        MessageMaintenanceRequest request, int maximum, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            WITH selected AS
            (
                SELECT record_id FROM {Processing} AS p
                WHERE application=@application AND environment=@environment
                    AND (state=1 AND @success AND protection_expires<=clock_timestamp()
                        OR state=3 AND @terminal AND terminal_expires<=clock_timestamp() AND delivery_horizon_micros IS NOT NULL
                            AND created_at+delivery_horizon_micros*INTERVAL '1 microsecond'<=clock_timestamp()
                            AND redrive_horizon_micros IS NOT NULL AND created_at+redrive_horizon_micros*INTERVAL '1 microsecond'<=clock_timestamp())
                    AND NOT EXISTS(SELECT 1 FROM {Outbox} AS o WHERE o.processing_application=p.application
                        AND o.processing_environment=p.environment AND o.subscription=p.subscription AND o.original_message_id=p.message_id AND o.state<>2 AND NOT o.reply_expired)
                ORDER BY updated_at,record_id FOR UPDATE OF p SKIP LOCKED LIMIT @maximum
            ), changed AS
            (
                DELETE FROM {Processing} AS p USING selected WHERE p.record_id=selected.record_id RETURNING octet_length(p.envelope) AS bytes,p.state
            ) SELECT count(*)::integer,coalesce(sum(bytes),0)::bigint,count(*) FILTER(WHERE state=3)::integer FROM changed
            """);
        AddAdmin(command, request.Application, request.Environment, maximum);
        Parameter(command, "success", DbType.Boolean, request.Policy.InboxProtectionRetention is not null);
        Parameter(command, "terminal", DbType.Boolean, request.Policy.TerminalRetention is not null);
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        await reader.ReadAsync(token).ConfigureAwait(false);
        return (reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2));
    }

    private async Task<int> DeleteRejectionsAsync(DbConnection connection, DbTransaction transaction,
        MessageMaintenanceRequest request, int maximum, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            WITH selected AS
            (
                SELECT r.record_id FROM {Rejections} AS r JOIN {Outbox} AS o ON o.record_id=r.handoff_id
                LEFT JOIN {Outbox} AS reply ON reply.record_id=r.reply_id
                WHERE r.application=@application AND r.environment=@environment AND r.terminal_expires<=clock_timestamp() AND o.state=2
                    AND (r.reply_id IS NULL OR reply.state=2 OR reply.reply_expired)
                ORDER BY r.terminal_expires,r.record_id FOR UPDATE OF r SKIP LOCKED LIMIT @maximum
            ), changed AS
            (
                DELETE FROM {Rejections} AS r USING selected WHERE r.record_id=selected.record_id RETURNING true
            ) SELECT count(*)::integer FROM changed
            """);
        AddAdmin(command, request.Application, request.Environment, maximum);
        return (int)(await command.ExecuteScalarAsync(token).ConfigureAwait(false))!;
    }

    /// <inheritdoc />
    public Task<MessageStoreObservation> ObserveAsync(ILocalTransactionContext context, MessageObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOwner(request.Application, request.Environment);
        MessageText.Range(request.MaximumRecords, 1, 256, nameof(request.MaximumRecords));
        return RunAsync(context, async (connection, transaction, token) =>
        {
            await using DbCommand command = Command(connection, transaction, $"""
                WITH observation AS (SELECT clock_timestamp() AS at), bounded_records AS
                (
                    SELECT created_at,eligible_at FROM {Outbox} WHERE application=@application AND environment=@environment AND state<>2 AND NOT reply_expired
                    ORDER BY created_at,record_id LIMIT @witness
                ) SELECT observation.at,count(bounded_records.created_at)::integer,
                    CASE WHEN count(bounded_records.created_at)=0 THEN 0::double precision
                        WHEN count(bounded_records.created_at) FILTER(WHERE bounded_records.eligible_at<=observation.at)=0 THEN NULL
                        ELSE greatest(0,extract(epoch FROM observation.at-min(bounded_records.created_at) FILTER(WHERE bounded_records.eligible_at<=observation.at)))::double precision END
                FROM observation LEFT JOIN bounded_records ON true GROUP BY observation.at
                """);
            AddOwner(command, request.Application, request.Environment);
            Parameter(command, "witness", DbType.Int32, request.MaximumRecords + 1);
            await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            await reader.ReadAsync(token).ConfigureAwait(false);
            int count = reader.GetInt32(1);
            bool complete = count <= request.MaximumRecords;
            return new MessageStoreObservation(Time(reader, 0), complete ? count : null,
                reader.IsDBNull(2) ? null : reader.GetDouble(2), complete);
        }, cancellationToken);
    }

    private static bool SamePolicy(MessageRetentionPolicy left, MessageRetentionPolicy right)
        => left.PublishedPayloadRetention == right.PublishedPayloadRetention && left.InboxProtectionRetention == right.InboxProtectionRetention
            && left.TerminalRetention == right.TerminalRetention && left.SupportedRedriveHorizon == right.SupportedRedriveHorizon
            && left.MaximumDeliveryAge == right.MaximumDeliveryAge;

    private static void AddAdmin(DbCommand command, string application, string environment, int maximum)
    {
        AddOwner(command, application, environment);
        Parameter(command, "maximum", DbType.Int32, maximum);
    }

    private static async Task<(int Count, long Bytes)> CountsAsync(DbCommand command, CancellationToken token)
    {
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        await reader.ReadAsync(token).ConfigureAwait(false);
        return (reader.GetInt32(0), reader.GetInt64(1));
    }
}
