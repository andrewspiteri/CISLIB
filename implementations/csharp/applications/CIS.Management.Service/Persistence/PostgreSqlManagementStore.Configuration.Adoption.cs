using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    /// <summary>Replaces only the actual native owner's complete status, without refreshing registration presence.</summary>
    public Task<ManagementStoreResult<ManagementReportReceipt>> ReportConfigurationStatusAsync(ManagementRegistrationOwner owner,
        string registrationId, ManagementConfigurationStatusReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Guid registration = ManagementSql.Uuid(registrationId, nameof(registrationId));
        byte[] body = ManagementConfigurationWire.EncodeRequest(report);
        ManagementConfigurationStatusReport captured = ManagementConfigurationWire.DecodeRequest<ManagementConfigurationStatusReport>(body)
            ?? throw new ArgumentException("A complete bounded status is required.", nameof(report));
        long sequence = long.Parse(captured.Sequence, CultureInfo.InvariantCulture);
        byte[] digest = SHA256.HashData(body);
        long bytes = checked(128 + body.LongLength);
        return RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            long available;
            // The original owner holds catalog/quota before registration, as registration and cleanup do.
            await using (DbCommand catalog = ManagementSql.Command(connection, transaction, $"""
                SELECT quota_bytes-used_bytes FROM {Table("configuration_catalog")}
                WHERE singleton=true AND manifest_digest IS NOT NULL FOR UPDATE
                """))
            {
                object? value = await catalog.ExecuteScalarAsync(sqlToken);
                if (value is not long remaining)
                {
                    return Candidate<ManagementReportReceipt>.Refused("management.configuration.uninitialized");
                }
                available = remaining;
            }
            RegistrationRow? native = await ReadRegistrationAsync(connection, transaction, registration, "FOR UPDATE OF r", sqlToken);
            if (native is null || !Owned(native, owner))
            {
                return Candidate<ManagementReportReceipt>.Refused("management.registration.not_found");
            }
            if (native.ClosedAtUtc is not null)
            {
                return Candidate<ManagementReportReceipt>.Refused("management.registration.closed");
            }
            SessionBinding[] supported;
            await using (DbCommand session = ManagementSql.Command(connection, transaction, $"""
                SELECT supported_body FROM {Table("configuration_session")}
                WHERE registration_id=@id AND application_id=@application FOR UPDATE
                """, ("id", registration), ("application", native.ApplicationId)))
            {
                if (await session.ExecuteScalarAsync(sqlToken) is not byte[] supportedBody)
                {
                    return Candidate<ManagementReportReceipt>.Refused("management.configuration.not_found");
                }
                supported = JsonSerializer.Deserialize<SessionBinding[]>(supportedBody, Json)
                    ?? throw new ManagementSchemaException("A retained configuration session is unreadable.");
            }
            if (supported.Length > 256 || captured.Entries.Any(entry => !supported.Any(binding =>
                binding.ConfigurationId == entry.ConfigurationId && binding.SchemaVersion == entry.SchemaVersion)))
            {
                return Candidate<ManagementReportReceipt>.Refused("management.configuration.unsupported");
            }
            long previousBytes = 0;
            await using (DbCommand previous = ManagementSql.Command(connection, transaction, $"""
                SELECT sequence,digest,received_at,logical_bytes FROM {Table("configuration_adoption")} WHERE registration_id=@id FOR UPDATE
                """, ("id", registration)))
            await using (DbDataReader reader = await previous.ExecuteReaderAsync(sqlToken))
            {
                if (await reader.ReadAsync(sqlToken))
                {
                    long accepted = reader.GetInt64(0);
                    if (sequence < accepted)
                    {
                        return Candidate<ManagementReportReceipt>.Refused("management.sequence.obsolete");
                    }
                    if (sequence == accepted)
                    {
                        if (!CryptographicOperations.FixedTimeEquals(reader.GetFieldValue<byte[]>(1), digest))
                        {
                            return Candidate<ManagementReportReceipt>.Refused("management.sequence.conflict");
                        }
                        return Candidate<ManagementReportReceipt>.Existing(new(registrationId, "configuration-status", captured.Sequence,
                            captured.Sequence, "replayed", ManagementSql.Timestamp(reader, 2)));
                    }
                    previousBytes = reader.GetInt64(3);
                }
            }
            long delta = bytes - previousBytes;
            if (delta > available)
            {
                return Candidate<ManagementReportReceipt>.Refused("management.capacity.exceeded");
            }
            string received;
            await using (DbCommand replace = ManagementSql.Command(connection, transaction, $"""
                INSERT INTO {Table("configuration_adoption")}(registration_id,sequence,digest,body,observed_at,received_at,logical_bytes)
                VALUES(@id,@sequence,@digest,@body,@observed::timestamptz,clock_timestamp(),@bytes)
                ON CONFLICT(registration_id) DO UPDATE SET sequence=EXCLUDED.sequence,digest=EXCLUDED.digest,body=EXCLUDED.body,
                    observed_at=EXCLUDED.observed_at,received_at=EXCLUDED.received_at,logical_bytes=EXCLUDED.logical_bytes
                RETURNING received_at
                """, ("id", registration), ("sequence", sequence), ("digest", digest), ("body", body),
                    ("observed", captured.ObservedAtUtc), ("bytes", bytes)))
            {
                received = ManagementSql.Timestamp((DateTime)(await replace.ExecuteScalarAsync(sqlToken))!);
            }
            await using (DbCommand charge = ManagementSql.Command(connection, transaction,
                $"UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@delta WHERE singleton=true", ("delta", delta)))
            {
                await charge.ExecuteNonQueryAsync(sqlToken);
            }
            return Candidate<ManagementReportReceipt>.Written(new(registrationId, "configuration-status", captured.Sequence,
                captured.Sequence, "accepted", received));
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    /// <summary>Projects current assignment and two independent freshness facts; historical acceptance never pins content.</summary>
    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>>> ReadConfigurationAdoptionAsync(
        string applicationId, string? after, int take, CancellationToken cancellationToken = default)
        => ReadConfigurationAdoptionCoreAsync(applicationId, after, take, null, cancellationToken);

    internal Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>>> ReadBindingConfigurationAdoptionAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return ReadConfigurationAdoptionCoreAsync(binding.ApplicationId, after, take, binding, cancellationToken);
    }

    private Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>>> ReadConfigurationAdoptionCoreAsync(
        string applicationId, string? after, int take, ManagementConfigurationBindingResource? scope,
        CancellationToken cancellationToken)
    {
        Guid application = ManagementSql.Uuid(applicationId, nameof(applicationId));
        Page(take);
        if (after is not null && (after.Length > 1024 || after.Any(character => character is < '!' or > '~')))
        {
            throw new ArgumentException("Use the original bounded page position.", nameof(after));
        }
        return RunCoreAsync<IReadOnlyList<ManagementConfigurationAdoptionRow>>(async (context, state, token) =>
            await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await using (DbCommand catalog = ManagementSql.Command(connection, transaction,
                $"SELECT manifest_digest IS NOT NULL FROM {Table("configuration_catalog")} WHERE singleton=true FOR SHARE"))
            {
                if (await catalog.ExecuteScalarAsync(sqlToken) is not true)
                {
                    return Candidate<IReadOnlyList<ManagementConfigurationAdoptionRow>>.Refused("management.configuration.uninitialized");
                }
            }
            if (scope is not null)
            {
                ManagementConfigurationBindingResource? actual;
                await using (DbCommand binding = ManagementSql.Command(connection, transaction,
                    ConfigurationBindingSelect + "WHERE b.binding_id=@binding FOR SHARE OF b", ("binding", Guid.Parse(scope.BindingId))))
                await using (DbDataReader reader = await binding.ExecuteReaderAsync(sqlToken))
                {
                    actual = await reader.ReadAsync(sqlToken) ? ReadConfigurationBinding(reader) : null;
                }
                if (actual is null || !actual.Enabled || actual.ApplicationId != applicationId || actual.Environment != scope.Environment
                    || actual.ConfigurationId != scope.ConfigurationId || actual.SchemaVersion != scope.SchemaVersion
                    || actual.HostApplicationName != scope.HostApplicationName)
                {
                    return Candidate<IReadOnlyList<ManagementConfigurationAdoptionRow>>.Refused("management.configuration.not_found");
                }
            }
            var candidates = new List<AdoptionCandidate>();
            await using (DbCommand select = ManagementSql.Command(connection, transaction, $"""
                WITH targets AS (
                    SELECT b.binding_id,b.host_scope,d.configuration_id,d.schema_version,a.environment,m.generation,m.default_release_id,
                        cohort.cohort_id
                    FROM {Table("configuration_binding")} b
                    JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id AND d.enabled
                    JOIN {Table("management_application")} a ON a.application_id=b.application_id
                    JOIN {Table("configuration_assignment")} m ON m.binding_id=b.binding_id
                    CROSS JOIN LATERAL (
                        SELECT NULL::text AS cohort_id
                        UNION SELECT o.cohort_id FROM {Table("configuration_override")} o WHERE o.binding_id=b.binding_id
                        UNION SELECT c.cohort_id FROM {Table("configuration_cohort")} c WHERE c.application_id=b.application_id
                        UNION SELECT s.cohort_id FROM {Table("configuration_session")} s WHERE s.application_id=b.application_id
                    ) cohort
                    WHERE b.application_id=@application AND b.enabled
                        AND (CAST(@binding AS uuid) IS NULL OR (b.binding_id=CAST(@binding AS uuid)
                            AND d.configuration_id=@configuration AND d.schema_version=CAST(@version AS bigint)))
                ), recipients AS (
                    SELECT t.*,s.registration_id,COALESCE(o.release_id,t.default_release_id) desired_release,
                        s.registration_id IS NOT NULL AND EXISTS (
                            SELECT 1 FROM jsonb_array_elements(convert_from(s.supported_body,'UTF8')::jsonb) binding
                            WHERE binding->>'bindingId'=t.binding_id::text AND binding->>'hostApplicationName'=t.host_scope
                                AND binding->>'configurationId'=t.configuration_id AND binding->>'schemaVersion'=t.schema_version::text
                        ) target_eligible,
                        t.configuration_id||'/'||t.schema_version::text||'/'||COALESCE(t.cohort_id,'')||'/'||COALESCE(s.registration_id::text,'') position
                    FROM targets t
                    LEFT JOIN {Table("configuration_override")} o ON o.binding_id=t.binding_id AND o.cohort_id=t.cohort_id
                    LEFT JOIN {Table("configuration_session")} s ON s.application_id=@application AND s.cohort_id IS NOT DISTINCT FROM t.cohort_id
                        AND EXISTS (SELECT 1 FROM jsonb_array_elements(convert_from(s.supported_body,'UTF8')::jsonb) binding
                            WHERE binding->>'configurationId'=t.configuration_id AND binding->>'schemaVersion'=t.schema_version::text)
                )
                SELECT x.position,x.configuration_id,x.schema_version,x.environment,x.cohort_id,x.registration_id,x.desired_release,x.generation,
                    r.registration_body,r.registered_at,r.observation_received_at,r.closed_at,
                    a.logical_bytes,a.received_at,clock_timestamp(),x.target_eligible
                FROM recipients x
                LEFT JOIN {Table("management_registration")} r ON r.registration_id=x.registration_id
                LEFT JOIN {Table("configuration_adoption")} a ON a.registration_id=x.registration_id
                WHERE (@after IS NULL OR x.position COLLATE "C">@after) ORDER BY x.position COLLATE "C" LIMIT @take
                """, ("application", application), ("after", after), ("take", take + 1),
                ("binding", scope is null ? null : Guid.Parse(scope.BindingId)), ("configuration", scope?.ConfigurationId),
                ("version", scope is null ? null : long.Parse(scope.SchemaVersion, CultureInfo.InvariantCulture))))
            await using (DbDataReader reader = await select.ExecuteReaderAsync(sqlToken))
            {
                while (await reader.ReadAsync(sqlToken))
                {
                    candidates.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2).ToString(CultureInfo.InvariantCulture),
                        reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetGuid(5),
                        reader.IsDBNull(6) ? null : reader.GetGuid(6).ToString("D"), reader.IsDBNull(7) ? null : reader.GetInt64(7).ToString(CultureInfo.InvariantCulture),
                        reader.IsDBNull(8) ? null : DecodeRequest<ManagementRegistrationRequest>(reader.GetFieldValue<byte[]>(8)),
                        reader.IsDBNull(9) ? null : reader.GetDateTime(9), reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                        !reader.IsDBNull(11), reader.IsDBNull(12) ? 0 : reader.GetInt64(12), reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                        reader.GetDateTime(14), reader.GetBoolean(15)));
                }
            }
            var rows = new List<ManagementConfigurationAdoptionRow>();
            long remainingReadBytes = 1024 * 1024;
            // Exact encoded entries have their own allowance. Reserve the finite ASCII cursor
            // (at most 2048 bytes), wrapper and separators rather than guessing Unicode expansion.
            int remainingEncodedBytes = 1024 * 1024 - 8192;
            foreach (AdoptionCandidate candidate in candidates.Take(take))
            {
                // Admit a whole bounded report before loading its body. This is page-owned, never a status cache.
                if (candidate.ReportBytes > remainingReadBytes)
                {
                    break;
                }
                remainingReadBytes -= candidate.ReportBytes;
                ManagementConfigurationStatusReport? report = null;
                if (candidate.ReportBytes != 0)
                {
                    await using DbCommand read = ManagementSql.Command(connection, transaction,
                        $"SELECT body FROM {Table("configuration_adoption")} WHERE registration_id=@id", ("id", candidate.Registration));
                    byte[]? body = await read.ExecuteScalarAsync(sqlToken) as byte[];
                    if (body is not null)
                    {
                        report = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationStatusReport>(body)
                            ?? throw new ManagementSchemaException("A retained configuration report is unreadable.");
                    }
                }
                ManagementConfigurationStatusEntry? reported = report?.Entries.SingleOrDefault(entry =>
                    entry.ConfigurationId == candidate.Configuration && entry.SchemaVersion == candidate.Schema);
                ManagementConfigurationVersion? desired = candidate.Release is null ? null : new(candidate.Release, candidate.Generation!);
                string registrationFreshness = candidate.Registration is null ? "noInstances" : candidate.Closed ? "offline"
                    : candidate.Now - (candidate.LastPresence ?? candidate.Registered!.Value) > Options.StaleAfter ? "stale" : "fresh";
                string adoptionFreshness = candidate.Registration is null ? "noInstances" : reported is null ? "unknown"
                    : candidate.Now - candidate.Received!.Value > Options.StaleAfter ? "stale" : "fresh";
                var entry = new ManagementConfigurationAdoptionEntry(applicationId, candidate.Environment, candidate.Configuration,
                    candidate.Schema, registrationFreshness, adoptionFreshness, candidate.Registration is null ? "noInstances"
                        : !candidate.TargetEligible ? "unknown" : AdoptionConvergence(desired, reported), candidate.Cohort, candidate.Registration?.ToString("D"),
                    candidate.Request?.ReportedInstanceId, candidate.Request?.ApplicationVersion, desired, reported,
                    reported is null ? null : report!.ObservedAtUtc, reported is null ? null : ManagementSql.Timestamp(candidate.Received!.Value));
                int encodedBytes = ManagementConfigurationWire.EncodeResponse(new ManagementPage<ManagementConfigurationAdoptionEntry>([entry])).Length;
                if (encodedBytes > remainingEncodedBytes)
                {
                    break;
                }
                remainingEncodedBytes -= encodedBytes;
                rows.Add(new(candidate.Position, entry));
            }
            if (rows.Count == 0 && candidates.Count != 0)
            {
                return Candidate<IReadOnlyList<ManagementConfigurationAdoptionRow>>.Refused("management.configuration.response_too_large");
            }
            return Candidate<IReadOnlyList<ManagementConfigurationAdoptionRow>>.Existing(rows.AsReadOnly(), rows.Count < candidates.Count);
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    internal static string AdoptionConvergence(ManagementConfigurationVersion? desired, ManagementConfigurationStatusEntry? reported)
    {
        if (reported is null || !reported.KnownState.IsKnown || !reported.KnownUpdateMode.IsKnown)
        {
            return "unknown";
        }
        if (reported.State == "Faulted" || !reported.LiveStateValid && reported.LastSuccessful is not null)
        {
            return "faulted";
        }
        if (desired is null || reported.Desired != desired)
        {
            return "pending";
        }
        return reported.State switch
        {
            "Applied" or "AppliedUnchanged" when reported.LiveStateValid && reported.Applied is { } accepted
                && accepted.ReleaseId == desired.ReleaseId && accepted.Generation == desired.Generation => "applied",
            "Rejected" => "rejected",
            "PendingRestart" => "pendingRestart",
            "Pending" => "pending",
            _ => "unknown"
        };
    }

    private sealed record AdoptionCandidate(string Position, string Configuration, string Schema, string Environment, string? Cohort,
        Guid? Registration, string? Release, string? Generation, ManagementRegistrationRequest? Request,
        DateTime? Registered, DateTime? LastPresence, bool Closed, long ReportBytes, DateTime? Received, DateTime Now, bool TargetEligible);
}
