using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

/// <summary>Application-owned PostgreSQL state using only the original named RM-02 transaction capability.</summary>
public sealed partial class PostgreSqlManagementStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly ILocalTransactionFactory factory;

    public PostgreSqlManagementStore(ILocalTransactionFactory factory, ManagementPersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        this.factory = factory;
        Options = options;
    }

    public ManagementPersistenceOptions Options
    {
        get;
    }

    public Task<ManagementStoreResult<ManagementRegistrationReceipt>> RegisterAsync(ManagementRegistrationOwner owner,
        string registrationId, ManagementRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Guid id = ManagementSql.Uuid(registrationId, nameof(registrationId));
        byte[] body = ManagementWire.EncodeRequest(request, Options.WireLimits);
        byte[] digest = SHA256.HashData(body);
        byte[] evidence = JsonSerializer.SerializeToUtf8Bytes(owner.InstanceEvidence, Json);
        return RunAsync(async (context, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            Capacity capacity = await LockCapacityAsync(connection, transaction, sqlToken).ConfigureAwait(false);
            RegistrationRow? existing = await ReadRegistrationAsync(connection, transaction, id, "FOR UPDATE OF r", sqlToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!Owned(existing, owner))
                {
                    return Candidate<ManagementRegistrationReceipt>.Refused("management.registration.conflict");
                }
                if (!CryptographicOperations.FixedTimeEquals(existing.RegistrationDigest, digest))
                {
                    return Candidate<ManagementRegistrationReceipt>.Refused("management.registration.conflict");
                }
                if (existing.ClosedAtUtc is not null)
                {
                    return Candidate<ManagementRegistrationReceipt>.Refused("management.registration.closed");
                }

                return Candidate<ManagementRegistrationReceipt>.Existing(new(registrationId,
                    existing.ApplicationId.ToString("D"), existing.RegisteredAtUtc));
            }

            Guid applicationId;
            await using (var find = ManagementSql.Command(connection, transaction,
                $"SELECT application_id FROM {Table("management_application")} WHERE application=@application AND environment=@environment",
                ("application", owner.Application), ("environment", owner.Environment)))
            {
                object? found = await find.ExecuteScalarAsync(sqlToken).ConfigureAwait(false);
                if (found is Guid known)
                {
                    applicationId = known;
                }
                else
                {
                    if (capacity.Applications >= capacity.MaximumApplications)
                    {
                        return Candidate<ManagementRegistrationReceipt>.Refused("management.capacity.exceeded");
                    }

                    applicationId = Guid.NewGuid();
                    await using var insertApplication = ManagementSql.Command(connection, transaction, $"""
                        INSERT INTO {Table("management_application")}(application_id,application,environment,display_name)
                        VALUES(@id,@application,@environment,@display);
                        UPDATE {Table("management_capacity")} SET application_count=application_count+1 WHERE singleton=true
                        """, ("id", applicationId), ("application", owner.Application), ("environment", owner.Environment), ("display", owner.Application));
                    await insertApplication.ExecuteNonQueryAsync(sqlToken).ConfigureAwait(false);
                }
            }

            if (capacity.Registrations >= capacity.MaximumRegistrations)
            {
                return Candidate<ManagementRegistrationReceipt>.Refused("management.capacity.exceeded");
            }

            await using (var count = ManagementSql.Command(connection, transaction,
                $"SELECT COUNT(*) FROM {Table("management_registration")} WHERE application_id=@application_id", ("application_id", applicationId)))
            {
                long registrations = Convert.ToInt64(await count.ExecuteScalarAsync(sqlToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (registrations >= capacity.MaximumRegistrationsPerApplication)
                {
                    return Candidate<ManagementRegistrationReceipt>.Refused("management.capacity.exceeded");
                }
            }

            string registeredAt;
            await using (var insert = ManagementSql.Command(connection, transaction, $"""
                INSERT INTO {Table("management_registration")}(registration_id,application_id,issuer,subject,registration_digest,
                    registration_body,application_version,instance_evidence,registered_at)
                VALUES(@id,@application_id,@issuer,@subject,@digest,@body,@version,@evidence,clock_timestamp()) RETURNING registered_at
                """, ("id", id), ("application_id", applicationId), ("issuer", owner.Issuer), ("subject", owner.Subject),
                ("digest", digest), ("body", body), ("version", request.ApplicationVersion), ("evidence", evidence)))
            {
                registeredAt = ManagementSql.Timestamp((DateTime)(await insert.ExecuteScalarAsync(sqlToken).ConfigureAwait(false))!);
            }
            await using (var increment = ManagementSql.Command(connection, transaction,
                $"UPDATE {Table("management_capacity")} SET registration_count=registration_count+1 WHERE singleton=true"))
            {
                await increment.ExecuteNonQueryAsync(sqlToken).ConfigureAwait(false);
            }

            return Candidate<ManagementRegistrationReceipt>.Written(new(registrationId, applicationId.ToString("D"), registeredAt));
        }, token).ConfigureAwait(false), cancellationToken);
    }

    public Task<ManagementStoreResult<ManagementReportReceipt>> ReportObservationAsync(ManagementRegistrationOwner owner,
        string registrationId, ManagementObservation observation, CancellationToken cancellationToken = default)
        => ReportAsync(owner, registrationId, ManagementWire.EncodeRequest(observation, Options.WireLimits),
            long.Parse(observation.Sequence, CultureInfo.InvariantCulture), inventory: false, cancellationToken);

    public Task<ManagementStoreResult<ManagementReportReceipt>> ReportInventoryAsync(ManagementRegistrationOwner owner,
        string registrationId, ManagementInventorySnapshot inventory, CancellationToken cancellationToken = default)
        => ReportAsync(owner, registrationId, ManagementWire.EncodeRequest(inventory, Options.WireLimits),
            long.Parse(inventory.Sequence, CultureInfo.InvariantCulture), inventory: true, cancellationToken);

    public Task<ManagementStoreResult<ManagementCloseReceipt>> CloseAsync(ManagementRegistrationOwner owner,
        string registrationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Guid id = ManagementSql.Uuid(registrationId, nameof(registrationId));
        return RunAsync(async (context, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            RegistrationRow? row = await ReadRegistrationAsync(connection, transaction, id, "FOR UPDATE OF r", sqlToken).ConfigureAwait(false);
            if (row is null || !Owned(row, owner))
            {
                return Candidate<ManagementCloseReceipt>.Refused("management.not_found");
            }
            if (row.ClosedAtUtc is { } closed)
            {
                return Candidate<ManagementCloseReceipt>.Existing(new(registrationId, closed));
            }

            await using var close = ManagementSql.Command(connection, transaction,
                $"UPDATE {Table("management_registration")} SET closed_at=clock_timestamp() WHERE registration_id=@id RETURNING closed_at", ("id", id));
            string closedAt = ManagementSql.Timestamp((DateTime)(await close.ExecuteScalarAsync(sqlToken).ConfigureAwait(false))!);
            return Candidate<ManagementCloseReceipt>.Written(new(registrationId, closedAt));
        }, token).ConfigureAwait(false), cancellationToken);
    }

    private Task<ManagementStoreResult<ManagementReportReceipt>> ReportAsync(ManagementRegistrationOwner owner,
        string registrationId, byte[] body, long sequence, bool inventory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Guid id = ManagementSql.Uuid(registrationId, nameof(registrationId));
        byte[] digest = SHA256.HashData(body);
        string stream = inventory ? "inventory" : "observation";
        return RunAsync(async (context, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            Capacity? capacity = inventory ? await LockCapacityAsync(connection, transaction, sqlToken).ConfigureAwait(false) : null;
            RegistrationRow? row = await ReadRegistrationAsync(connection, transaction, id, "FOR UPDATE OF r", sqlToken).ConfigureAwait(false);
            if (row is null || !Owned(row, owner))
            {
                return Candidate<ManagementReportReceipt>.Refused("management.not_found");
            }
            if (row.ClosedAtUtc is not null)
            {
                return Candidate<ManagementReportReceipt>.Refused("management.registration.closed");
            }

            long? accepted = inventory ? row.InventorySequence : row.ObservationSequence;
            byte[]? originalDigest = inventory ? row.InventoryDigest : row.ObservationDigest;
            string? originalReceipt = inventory ? row.InventoryReceivedAtUtc : row.ObservationReceivedAtUtc;
            if (accepted is { } previous)
            {
                if (sequence < previous)
                {
                    return Candidate<ManagementReportReceipt>.Refused("management.sequence.obsolete");
                }
                if (sequence == previous)
                {
                    if (originalDigest is null || !CryptographicOperations.FixedTimeEquals(originalDigest, digest))
                    {
                        return Candidate<ManagementReportReceipt>.Refused("management.sequence.conflict");
                    }

                    return Candidate<ManagementReportReceipt>.Existing(new(registrationId, stream, sequence.ToString(CultureInfo.InvariantCulture),
                        sequence.ToString(CultureInfo.InvariantCulture), "replayed", originalReceipt!));
                }
            }

            long delta = inventory ? body.LongLength - row.InventoryBytes : 0;
            if (inventory && (capacity!.InventoryBytes > capacity.MaximumInventoryBytes - delta))
            {
                return Candidate<ManagementReportReceipt>.Refused("management.capacity.exceeded");
            }

            await using var update = ManagementSql.Command(connection, transaction, $"""
                UPDATE {Table("management_registration")} SET {stream}_sequence=@sequence,{stream}_digest=@digest,
                    {stream}_body=@body,{stream}_received_at=clock_timestamp(){(inventory ? ",inventory_bytes=@bytes" : string.Empty)}
                WHERE registration_id=@id RETURNING {stream}_received_at
                """, ("sequence", sequence), ("digest", digest), ("body", body), ("id", id), ("bytes", body.LongLength));
            string receivedAt = ManagementSql.Timestamp((DateTime)(await update.ExecuteScalarAsync(sqlToken).ConfigureAwait(false))!);
            if (inventory)
            {
                await using var charge = ManagementSql.Command(connection, transaction,
                    $"UPDATE {Table("management_capacity")} SET inventory_bytes=inventory_bytes+@delta WHERE singleton=true", ("delta", delta));
                await charge.ExecuteNonQueryAsync(sqlToken).ConfigureAwait(false);
            }

            return Candidate<ManagementReportReceipt>.Written(new(registrationId, stream, sequence.ToString(CultureInfo.InvariantCulture),
                sequence.ToString(CultureInfo.InvariantCulture), "accepted", receivedAt));
        }, token).ConfigureAwait(false), cancellationToken);
    }

    private string Table(string name) => ManagementSql.Table(Options.Schema, name);

    private async Task<ManagementStoreResult<T>> RunAsync<T>(Func<ILocalTransactionContext, CancellationToken, Task<Candidate<T>>> work,
        CancellationToken cancellationToken)
    {
        ILocalTransaction? transaction = null;
        try
        {
            transaction = await factory.BeginAsync(ManagementSql.DatabaseAlias, new()
            {
                Isolation = TransactionIsolation.ReadCommitted
            }, cancellationToken).ConfigureAwait(false);
            Candidate<T> result = await work(transaction.Context, cancellationToken).ConfigureAwait(false);
            TransactionCompletion completion = result.Write && result.Code is null
                ? await transaction.CommitAsync(cancellationToken).ConfigureAwait(false)
                : await transaction.RollbackAsync().ConfigureAwait(false);
            if (result.Write && completion.Outcome != TransactionOutcome.Committed)
            {
                return new(default, completion.Outcome == TransactionOutcome.Unknown ? "management.transaction_unknown" : "management.not_committed",
                    completion, false);
            }

            return new(result.Value, result.Code, completion, result.PreviouslyCommitted, result.HasMore);
        }
        catch (DbException)
        {
            TransactionCompletion? completion = transaction is null ? null : await transaction.RollbackAsync().ConfigureAwait(false);
            return new(default, "management.unavailable", completion, false);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<Capacity> LockCapacityAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = ManagementSql.Command(connection, transaction, $"""
            SELECT application_count,registration_count,inventory_bytes
            FROM {Table("management_capacity")} WHERE singleton=true FOR UPDATE
            """);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ManagementSchemaException("The installed management capacity policy is absent.");
        }

        return new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2), Options.MaximumApplications,
            Options.MaximumRegistrationsPerApplication, Options.MaximumRetainedRegistrations, Options.MaximumInventoryBytes);
    }

    private sealed record Capacity(int Applications, int Registrations, long InventoryBytes, int MaximumApplications,
        int MaximumRegistrationsPerApplication, int MaximumRegistrations, long MaximumInventoryBytes);

    private sealed record Candidate<T>(T? Value, string? Code, bool Write, bool PreviouslyCommitted, bool HasMore = false)
    {
        internal static Candidate<T> Refused(string code) => new(default, code, false, false);
        internal static Candidate<T> Existing(T value, bool hasMore = false) => new(value, null, false, true, hasMore);
        internal static Candidate<T> Written(T value) => new(value, null, true, false);
    }
}
