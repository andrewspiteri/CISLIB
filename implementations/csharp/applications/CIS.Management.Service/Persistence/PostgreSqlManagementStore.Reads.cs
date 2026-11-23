using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    public Task<ManagementStoreResult<ManagementApplication>> ReadApplicationAsync(string applicationId,
        CancellationToken cancellationToken = default)
    {
        _ = ManagementSql.Uuid(applicationId, nameof(applicationId));
        return RunAsync(async (context, token) =>
        {
            ManagementApplication? application = await FindApplicationAsync(context, applicationId, token).ConfigureAwait(false);
            return application is null ? Candidate<ManagementApplication>.Refused("management.not_found")
                : Candidate<ManagementApplication>.Existing(application);
        }, cancellationToken);
    }

    public Task<ManagementStoreResult<IReadOnlyList<ManagementApplication>>> ReadApplicationsAsync(string? afterApplicationId,
        int take, CancellationToken cancellationToken = default)
    {
        Page(take);
        Guid? after = afterApplicationId is null ? null : ManagementSql.Uuid(afterApplicationId, nameof(afterApplicationId));
        return RunAsync<IReadOnlyList<ManagementApplication>>(async (context, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await using var command = ManagementSql.Command(connection, transaction, $"""
                SELECT application_id,application,environment,display_name,description,metadata_revision
                FROM {Table("management_application")} {(after is null ? string.Empty : "WHERE application_id>@after")}
                ORDER BY application_id LIMIT @take
                """, ("after", after), ("take", take + 1));
            var rows = new List<ManagementApplication>();
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken).ConfigureAwait(false);
            while (await reader.ReadAsync(sqlToken).ConfigureAwait(false))
            {
                rows.Add(Application(reader));
            }

            bool more = rows.Count > take;
            return Candidate<IReadOnlyList<ManagementApplication>>.Existing(Array.AsReadOnly(rows.Take(take).ToArray()), more);
        }, token).ConfigureAwait(false), cancellationToken);
    }

    public Task<ManagementStoreResult<IReadOnlyList<ManagementInstance>>> ReadInstancesAsync(string applicationId,
        string? afterRegistrationId, int take, CancellationToken cancellationToken = default)
    {
        Guid appId = ManagementSql.Uuid(applicationId, nameof(applicationId));
        Guid? after = afterRegistrationId is null ? null : ManagementSql.Uuid(afterRegistrationId, nameof(afterRegistrationId));
        Page(take);
        return RunAsync<IReadOnlyList<ManagementInstance>>(async (context, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await using var command = ManagementSql.Command(connection, transaction,
                RegistrationSelect + $" WHERE r.application_id=@application_id {(after is null ? string.Empty : "AND r.registration_id>@after")}" +
                " ORDER BY r.registration_id LIMIT @take", ("application_id", appId), ("after", after), ("take", take + 1));
            var rows = new List<ManagementInstance>();
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken).ConfigureAwait(false);
            while (await reader.ReadAsync(sqlToken).ConfigureAwait(false))
            {
                rows.Add(Instance(Registration(reader)));
            }

            bool more = rows.Count > take;
            return Candidate<IReadOnlyList<ManagementInstance>>.Existing(Array.AsReadOnly(rows.Take(take).ToArray()), more);
        }, token).ConfigureAwait(false), cancellationToken);
    }

    /// <summary>Locks bounded candidate metadata before fetching only complete byte-admitted bodies.</summary>
    public Task<ManagementStoreResult<IReadOnlyList<ManagementInventoryEntry>>> ReadInventoryAsync(string applicationId,
        string? afterRegistrationId, int take, int maximumEncodedBytes, CancellationToken cancellationToken = default)
    {
        Guid appId = ManagementSql.Uuid(applicationId, nameof(applicationId));
        Guid? after = afterRegistrationId is null ? null : ManagementSql.Uuid(afterRegistrationId, nameof(afterRegistrationId));
        Page(take);
        if (maximumEncodedBytes < 1 || maximumEncodedBytes > Options.WireLimits.MaximumInventoryResponseBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
        }

        return RunAsync<IReadOnlyList<ManagementInventoryEntry>>(async (context, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            var candidates = new List<InventoryCandidate>();
            await using (var command = ManagementSql.Command(connection, transaction, $"""
                SELECT r.registration_id,r.application_version,r.inventory_bytes,r.inventory_received_at
                FROM {Table("management_registration")} r WHERE r.application_id=@application_id AND r.inventory_body IS NOT NULL
                    {(after is null ? string.Empty : "AND r.registration_id>@after")}
                ORDER BY r.registration_id LIMIT @take FOR SHARE OF r
                """, ("application_id", appId), ("after", after), ("take", take + 1)))
            {
                await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken).ConfigureAwait(false);
                while (await reader.ReadAsync(sqlToken).ConfigureAwait(false))
                {
                    candidates.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2), ManagementSql.Timestamp(reader, 3)));
                }
            }

            // Includes the bounded cursor/wrapper and conservative escaped per-entry provenance.
            long reserved = 8192;
            var selected = new List<InventoryCandidate>();
            foreach (InventoryCandidate candidate in candidates.Take(take))
            {
                long next = candidate.Bytes + 8192;
                if (reserved > maximumEncodedBytes - next)
                {
                    break;
                }

                reserved += next;
                selected.Add(candidate);
            }
            if (selected.Count == 0 && candidates.Count > 0)
            {
                return Candidate<IReadOnlyList<ManagementInventoryEntry>>.Refused("management.response_too_large");
            }

            var entries = new List<ManagementInventoryEntry>(selected.Count);
            foreach (InventoryCandidate candidate in selected)
            {
                await using var read = ManagementSql.Command(connection, transaction,
                    $"SELECT inventory_body FROM {Table("management_registration")} WHERE registration_id=@id", ("id", candidate.Id));
                byte[] body = (byte[])(await read.ExecuteScalarAsync(sqlToken).ConfigureAwait(false))!;
                entries.Add(new(candidate.Id.ToString("D"), candidate.ApplicationVersion, candidate.ReceivedAtUtc,
                    DecodeRequest<ManagementInventorySnapshot>(body)));
            }

            return Candidate<IReadOnlyList<ManagementInventoryEntry>>.Existing(Array.AsReadOnly(entries.ToArray()), selected.Count < candidates.Count);
        }, token).ConfigureAwait(false), cancellationToken);
    }

    /// <summary>Reads the actual application inside the metadata command's borrowed original participant.</summary>
    public Task<ManagementApplication?> FindApplicationAsync(ILocalTransactionContext context, string applicationId,
        CancellationToken cancellationToken = default)
    {
        ManagementSql.RequireContext(context);
        Guid id = ManagementSql.Uuid(applicationId, nameof(applicationId));
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using var command = ManagementSql.Command(connection, transaction, $"""
                SELECT application_id,application,environment,display_name,description,metadata_revision
                FROM {Table("management_application")} WHERE application_id=@id
                """, ("id", id));
            await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Application(reader) : null;
        }, cancellationToken);
    }

    /// <summary>Applies one metadata-only compare-and-swap without completing the application's owner.</summary>
    public Task<ManagementApplication?> TryReplaceMetadataAsync(ILocalTransactionContext context, string applicationId,
        string expectedRevision, ManagementApplicationMetadata metadata, CancellationToken cancellationToken = default)
    {
        ManagementSql.RequireContext(context);
        Guid id = ManagementSql.Uuid(applicationId, nameof(applicationId));
        if (!ManagementWire.IsOrderedInteger(expectedRevision))
        {
            throw new ArgumentException("Use the original positive metadata revision.", nameof(expectedRevision));
        }
        ArgumentNullException.ThrowIfNull(metadata);
        _ = ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, metadata.DisplayName, metadata.Description), Options.WireLimits);
        long revision = long.Parse(expectedRevision, CultureInfo.InvariantCulture);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using var command = ManagementSql.Command(connection, transaction, $"""
                UPDATE {Table("management_application")} SET display_name=@display,description=@description,metadata_revision=metadata_revision+1
                WHERE application_id=@id AND metadata_revision=@revision AND metadata_revision<9223372036854775807
                RETURNING application_id,application,environment,display_name,description,metadata_revision
                """, ("display", metadata.DisplayName), ("description", metadata.Description), ("id", id), ("revision", revision));
            await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false) ? Application(reader) : null;
        }, cancellationToken);
    }

    private static ManagementApplication Application(DbDataReader reader)
    {
        long revision = reader.GetInt64(5);
        return new(reader.GetGuid(0).ToString("D"), reader.GetString(1), reader.GetString(2),
            new(reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)),
            revision.ToString(CultureInfo.InvariantCulture), ManagementSql.ETag(revision));
    }

    private string RegistrationSelect => $"""
        SELECT r.registration_id,r.application_id,a.application,a.environment,r.issuer,r.subject,r.registration_digest,
            r.registration_body,r.instance_evidence,r.registered_at,r.observation_sequence,r.observation_digest,
            r.observation_body,r.observation_received_at,r.inventory_sequence,r.inventory_digest,r.inventory_bytes,
            r.inventory_received_at,r.closed_at,clock_timestamp()
        FROM {Table("management_registration")} r JOIN {Table("management_application")} a ON a.application_id=r.application_id
        """;

    private async Task<RegistrationRow?> ReadRegistrationAsync(DbConnection connection, DbTransaction transaction,
        Guid registrationId, string lockClause, CancellationToken cancellationToken)
    {
        await using var command = ManagementSql.Command(connection, transaction,
            RegistrationSelect + $" WHERE r.registration_id=@id {lockClause}", ("id", registrationId));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Registration(reader) : null;
    }

    private RegistrationRow Registration(DbDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<byte[]>(6),
        DecodeRequest<ManagementRegistrationRequest>(reader.GetFieldValue<byte[]>(7)),
        JsonSerializer.Deserialize<ManagementInstanceEvidence>(reader.GetFieldValue<byte[]>(8), Json)
            ?? throw new ManagementSchemaException("Stored management instance evidence is invalid."),
        ManagementSql.Timestamp(reader, 9), reader.IsDBNull(10) ? null : reader.GetInt64(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<byte[]>(11),
        reader.IsDBNull(12) ? null : DecodeRequest<ManagementObservation>(reader.GetFieldValue<byte[]>(12)),
        reader.IsDBNull(13) ? null : ManagementSql.Timestamp(reader, 13), reader.IsDBNull(14) ? null : reader.GetInt64(14),
        reader.IsDBNull(15) ? null : reader.GetFieldValue<byte[]>(15), reader.GetInt64(16),
        reader.IsDBNull(17) ? null : ManagementSql.Timestamp(reader, 17), reader.IsDBNull(18) ? null : ManagementSql.Timestamp(reader, 18),
        reader.GetDateTime(19));

    private ManagementInstance Instance(RegistrationRow row)
    {
        string lastSeen = row.ObservationReceivedAtUtc ?? row.RegisteredAtUtc;
        DateTime seen = DateTime.ParseExact(lastSeen, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        string presence = row.ClosedAtUtc is not null ? "closed" : row.DatabaseNowUtc - seen > Options.StaleAfter ? "stale" : "active";
        return new(row.Id.ToString("D"), row.ApplicationId.ToString("D"), row.Request.ReportedInstanceId, row.Evidence,
            row.Request.ProcessStartedAtUtc, row.Request.ApplicationVersion, row.Request.ProtocolMajor, row.RegisteredAtUtc,
            presence, lastSeen, row.ClosedAtUtc, row.Observation, row.ObservationReceivedAtUtc,
            row.InventorySequence?.ToString(CultureInfo.InvariantCulture), row.InventoryReceivedAtUtc);
    }

    private T DecodeRequest<T>(byte[] body) where T : class => ManagementWire.DecodeRequest<T>(body, Options.WireLimits)
        ?? throw new ManagementSchemaException("Stored management content does not conform to its declared schema.");

    private static bool Owned(RegistrationRow row, ManagementRegistrationOwner owner)
        => row.Application == owner.Application && row.Environment == owner.Environment
            && row.Issuer == owner.Issuer && row.Subject == owner.Subject;

    private static void Page(int take)
    {
        if (take is < 1 or > 501)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }
    }

    private sealed record InventoryCandidate(Guid Id, string ApplicationVersion, long Bytes, string ReceivedAtUtc);

    private sealed record RegistrationRow(Guid Id, Guid ApplicationId, string Application, string Environment, string Issuer,
        string Subject, byte[] RegistrationDigest, ManagementRegistrationRequest Request, ManagementInstanceEvidence Evidence,
        string RegisteredAtUtc, long? ObservationSequence, byte[]? ObservationDigest, ManagementObservation? Observation,
        string? ObservationReceivedAtUtc, long? InventorySequence, byte[]? InventoryDigest, long InventoryBytes,
        string? InventoryReceivedAtUtc, string? ClosedAtUtc, DateTime DatabaseNowUtc);
}
