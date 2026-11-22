using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Messaging.PostgreSql;

/// <summary>PostgreSQL v2 transactional inbox/outbox; it never completes owners or performs broker work.</summary>
/// <remarks>Only original ReadCommitted participants are supported. Callers own authorization, bounded byte admission,
/// completion and cleanup. Constructors and registration perform no database work or schema installation.</remarks>
public sealed partial class PostgreSqlMessagingStore : IMessagingStore
{
    private static readonly MessagingLimits StorageLimits = new(MessagingLimits.MaximumSupportedPayloadBytes, MessagingLimits.MaximumSupportedJsonDepth);
    private readonly PostgreSqlMessagingSettings settings;

    /// <summary>Copies explicit schema, cooperative command limits, retention and optional storage admission.</summary>
    public PostgreSqlMessagingStore(PostgreSqlMessagingOptions? options = null)
    {
        settings = new(options);
    }

    private string Outbox => settings.Table("outbox");
    private string Processing => settings.Table("processing");
    private string Rejections => settings.Table("rejections");
    private string Budget => settings.Table("storage_budget");
    private const string ProcessingColumns = "digest, provenance, ordinal, state, version, generation, updated_at, handoff_id, reply_id, operation_binding, protection_expires, envelope IS NOT NULL, created_at, claim_xid = pg_current_xact_id()";

    internal static void ValidateContext(ILocalTransactionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Isolation != TransactionIsolation.ReadCommitted)
        {
            throw new NotSupportedException("PostgreSQL messaging v2 requires the original ReadCommitted participant.");
        }
        if (context.IsRollbackOnly)
        {
            throw new InvalidOperationException("A rollback-only participant cannot perform messaging store work.");
        }
    }

    private Task<T> RunAsync<T>(ILocalTransactionContext context,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ValidateContext(context);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand check = Command(connection, transaction, $"SELECT version FROM {settings.Table("schema_version")} WHERE singleton");
            if (await check.ExecuteScalarAsync(token).ConfigureAwait(false) is not int version || version != PostgreSqlMessagingSchema.Version)
            {
                throw new InvalidOperationException("The selected messaging schema version is unsupported.");
            }
            return await work(connection, transaction, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    private DbCommand Command(DbConnection connection, DbTransaction transaction, string sql)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = settings.CommandTimeoutSeconds;
        command.CommandText = sql;
        return command;
    }

    private static void Parameter(DbCommand command, string name, DbType type, object? value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static void AddOwner(DbCommand command, string application, string environment)
    {
        Parameter(command, "application", DbType.String, application);
        Parameter(command, "environment", DbType.String, environment);
    }

    private static void AddKey(DbCommand command, MessageProcessingKey key)
    {
        AddOwner(command, key.Application, key.Environment);
        Parameter(command, "subscription", DbType.String, key.Subscription);
        Parameter(command, "message", DbType.Guid, key.MessageId);
    }

    private static string KeyPredicate => "application = @application AND environment = @environment AND subscription = @subscription AND message_id = @message";

    private static void ValidateOwner(string application, string environment) => _ = new CapabilityTarget(application, environment);

    private static TimeSpan ValidateDelay(TimeSpan value) => MessageText.Duration(value, TimeSpan.Zero, TimeSpan.FromDays(36500), nameof(value));

    private static void ValidateAttempt(MessageProcessingAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(attempt.Key);
        ArgumentNullException.ThrowIfNull(attempt.Message);
        ArgumentNullException.ThrowIfNull(attempt.Provenance);
        MessageText.Range(attempt.DeliveryOrdinal, 1, 32, nameof(attempt.DeliveryOrdinal));
        MessageText.Range(attempt.Generation, 0, int.MaxValue, nameof(attempt.Generation));
        if (attempt.Key.MessageId != attempt.Message.Envelope.MessageId
            || attempt.Purpose is not (MessageRecordPurpose.Application or MessageRecordPurpose.Retry or MessageRecordPurpose.Redrive)
            || attempt.Purpose == MessageRecordPurpose.Application && (attempt.DeliveryOrdinal != 1 || attempt.Generation != 0 || attempt.HandoffRecordId is not null)
            || attempt.Purpose != MessageRecordPurpose.Application && (attempt.HandoffRecordId is null || attempt.HandoffRecordId == Guid.Empty))
        {
            throw new ArgumentException("The receiving identity, purpose and trusted ordinal metadata must agree.", nameof(attempt));
        }
        if (attempt.Provenance.Application != attempt.Message.Envelope.ProducerApplication
            || attempt.Provenance.Environment != attempt.Message.Envelope.ProducerEnvironment)
        {
            throw new ArgumentException("Original verified producer evidence must match immutable producer metadata.", nameof(attempt));
        }
        ValidateBinding(attempt.Key, attempt.OperationBinding);
    }

    private static void ValidateBinding(MessageProcessingKey key, MessageOperationBinding? binding)
    {
        if (binding is not null && (binding.Identity.Application != key.Application || binding.Identity.Environment != key.Environment))
        {
            throw new ArgumentException("An operation binding belongs to the captured receiving application and environment.", nameof(binding));
        }
    }

    private static void ValidateOutgoing(MessageOutgoingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        MessageText.Id(record.RecordId, nameof(record.RecordId));
        ValidateOwner(record.Application, record.Environment);
        ArgumentNullException.ThrowIfNull(record.Message);
        ArgumentNullException.ThrowIfNull(record.Destination);
        ValidateDelay(record.Delay);
        _ = new MessagePublication(record.Message, record.Destination, record.Purpose, record.DeliveryOrdinal,
            record.RecordId, record.Generation, record.TargetSubscription ?? record.ProcessingKey?.Subscription, record.DiagnosticCode);
        if (record.TargetSubscription is { } target)
        {
            MessageText.Alias(target, nameof(record.TargetSubscription));
            if (record.ProcessingKey is { } processing && target != processing.Subscription)
            {
                throw new ArgumentException("A canonical handoff targets exactly its retained subscription.", nameof(record));
            }
        }
        if (record.Message.Length > StorageLimits.MaximumEnvelopeBytes)
        {
            throw new ArgumentException("The envelope exceeds the supported PostgreSQL storage bound.", nameof(record));
        }
        if (record.ProcessingKey is { } key)
        {
            if (key.Application != record.Application || key.Environment != record.Environment
                || (record.Purpose == MessageRecordPurpose.Reply ? record.Message.Envelope.InReplyToMessageId != key.MessageId
                    : record.Message.Envelope.MessageId != key.MessageId))
            {
                throw new ArgumentException("The outgoing handoff must preserve its exact logical receiving identity.", nameof(record));
            }
        }
        if (record.Purpose is MessageRecordPurpose.Retry or MessageRecordPurpose.Terminal or MessageRecordPurpose.Redrive
            && (record.ProcessingKey is null || record.OriginalProvenance is null))
        {
            throw new ArgumentException("An internal handoff requires the retained processing key and original provenance.", nameof(record));
        }
        if (record.RejectionId is { } rejection)
        {
            MessageText.Id(rejection, nameof(record.RejectionId));
            if (record.Purpose != MessageRecordPurpose.Reply || record.ProcessingKey is not null)
            {
                throw new ArgumentException("A rejection anchor belongs only to a canonical-free reply.", nameof(record));
            }
        }
        if (record.Purpose == MessageRecordPurpose.Reply
            && ((record.ProcessingKey is null) == (record.RejectionId is null) || record.Message.Envelope.Kind != MessageKind.Reply))
        {
            throw new ArgumentException("A reply requires exactly one canonical or rejection anchor.", nameof(record));
        }
        if (record.Purpose is not (MessageRecordPurpose.Reply or MessageRecordPurpose.Rejection)
            && record.Message.Envelope.Kind == MessageKind.Reply)
        {
            throw new ArgumentException("A reply envelope requires an anchored reply or canonical-free rejection purpose.", nameof(record));
        }
        if (record.Purpose == MessageRecordPurpose.Rejection && record.ProcessingKey is not null)
        {
            throw new ArgumentException("A rejection must not reserve or reference canonical processing.", nameof(record));
        }
        if (record.Purpose == MessageRecordPurpose.Rejection && record.TargetSubscription is null)
        {
            throw new ArgumentException("A canonical-free rejection requires its configured quarantine subscription.", nameof(record));
        }
    }

    private static string Json<T>(T value)
    {
        string json = JsonSerializer.Serialize(value);
        if (MessageText.Utf8.GetByteCount(json) > 65536)
        {
            throw new ArgumentException("Retained messaging evidence exceeds its fixed storage bound.", nameof(value));
        }
        return json;
    }

    private static T ReadJson<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("Retained messaging metadata is corrupt.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Retained messaging metadata is corrupt.");
        }
    }

    private static byte[] NativeBody(EncodedMessage message)
    {
        if (!MemoryMarshal.TryGetArray(message.BytesMemory, out ArraySegment<byte> segment)
            || segment.Offset != 0 || segment.Count != segment.Array!.Length)
        {
            throw new InvalidOperationException("Messaging wire ownership is inconsistent.");
        }
        return segment.Array!;
    }

    private static DateTimeOffset Time(DbDataReader reader, int ordinal) => new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    private static Guid? NullableId(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static string? NullableText(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private async Task InitializeBudgetAsync(DbConnection connection, DbTransaction transaction, string application, string environment, CancellationToken token)
    {
        await using (DbCommand insert = Command(connection, transaction, $"""
            INSERT INTO {Budget}(application, environment, maximum_bytes) VALUES (@application, @environment, @maximum)
            ON CONFLICT (application, environment) DO NOTHING
            """))
        {
            AddOwner(insert, application, environment);
            Parameter(insert, "maximum", DbType.Int64, settings.MaximumRetainedEnvelopeBytes);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        await using DbCommand check = Command(connection, transaction, $"SELECT maximum_bytes FROM {Budget} WHERE application=@application AND environment=@environment");
        AddOwner(check, application, environment);
        object? value = await check.ExecuteScalarAsync(token).ConfigureAwait(false);
        long? persisted = value is long maximum ? maximum : null;
        if (persisted != settings.MaximumRetainedEnvelopeBytes)
        {
            throw new InvalidOperationException("Messaging storage capacity mode differs from the owner's immutable deployed mode.");
        }
    }

    private async Task AccountAsync(DbConnection connection, DbTransaction transaction, string application, string environment, long delta, CancellationToken token)
    {
        if (settings.MaximumRetainedEnvelopeBytes is null || delta == 0)
        {
            return;
        }
        await using DbCommand command = Command(connection, transaction, $"""
            UPDATE {Budget} SET retained_bytes = retained_bytes + @delta
            WHERE application=@application AND environment=@environment
                AND CASE WHEN @delta >= 0 THEN retained_bytes <= maximum_bytes - @delta
                    ELSE retained_bytes >= -@delta END
            RETURNING true
            """);
        AddOwner(command, application, environment);
        Parameter(command, "delta", DbType.Int64, delta);
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not true)
        {
            throw new MessageStorageCapacityException();
        }
    }

    private sealed record ProcessingRow(MessageProcessingRecord Record, DateTimeOffset CreatedAtUtc, bool OwnClaim);

    private async Task<ProcessingRow?> ReadProcessingRowAsync(DbConnection connection, DbTransaction transaction,
        MessageProcessingKey key, bool locked, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction,
            $"SELECT {ProcessingColumns} FROM {Processing} WHERE {KeyPredicate}" + (locked ? " FOR UPDATE" : ""));
        AddKey(command, key);
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }
        var record = new MessageProcessingRecord(key, new(reader.GetFieldValue<byte[]>(0)), ReadJson<MessageProvenance>(reader.GetString(1)),
            reader.GetInt32(2), (MessageProcessingDisposition)reader.GetInt16(3), reader.GetInt64(4), reader.GetInt32(5), Time(reader, 6),
            NullableId(reader, 7), NullableId(reader, 8), reader.IsDBNull(9) ? null : ReadJson<MessageOperationBinding>(reader.GetString(9)),
            reader.IsDBNull(10) ? null : Time(reader, 10), reader.GetBoolean(11));
        return new(record, Time(reader, 12), reader.GetBoolean(13));
    }

    private static bool SameEvidence(MessageProcessingRecord record, MessageProcessingAttempt attempt)
        => record.Provenance == attempt.Provenance && record.OperationBinding == attempt.OperationBinding;

    private async Task<EncodedMessage?> ReadEncodedAsync(DbConnection connection, DbTransaction transaction,
        string sql, Action<DbCommand> parameters, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, sql);
        parameters(command);
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.IsDBNull(0))
        {
            return null;
        }
        int length = reader.GetInt32(0);
        if (length is < 1 || length > StorageLimits.MaximumEnvelopeBytes)
        {
            throw new InvalidDataException("Retained messaging envelope length is corrupt.");
        }
        byte[] body = reader.GetFieldValue<byte[]>(1);
        if (!MessageWire.TryDecode(body, out EncodedMessage? encoded, out _, StorageLimits))
        {
            throw new InvalidDataException("Retained messaging envelope is corrupt.");
        }
        return encoded;
    }
}
