using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Management.Service.Backends;
using CIS.Management.Wire;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    /// <summary>Resolves one live session's complete immutable release without changing registration presence or authority freshness.</summary>
    public Task<ManagementStoreResult<ManagementResolvedConfiguration>> ReadRegistrationConfigurationAsync(ManagementRegistrationOwner owner,
        string registrationId, string configurationId, string schemaVersion, string? ifNoneMatch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Guid registration = ManagementSql.Uuid(registrationId, nameof(registrationId));
        if (!SnapshotPortableId(configurationId) || !SnapshotSchema(schemaVersion, out long schema)
            || ifNoneMatch is not null && !ManagementWire.IsStrongETag(ifNoneMatch))
        {
            throw new ArgumentException("A canonical configuration target and exact conditional ETag are required.");
        }
        return RunCoreAsync(async (context, state, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            ManagementAuthoritySnapshot confirmed = state ?? throw new ManagementBackendException("management.authority.unavailable");
            // Every catalog/map mutation takes this row first. Hold it through the native read's actual cleanup.
            await using (DbCommand catalog = ManagementSql.Command(connection, transaction, $"""
                SELECT manifest_digest IS NOT NULL FROM {Table("configuration_catalog")} WHERE singleton=true FOR SHARE
                """))
            {
                if (await catalog.ExecuteScalarAsync(sqlToken) is not true)
                {
                    return Candidate<ManagementResolvedConfiguration>.Refused("management.configuration.uninitialized");
                }
            }
            RegistrationRow? native = await ReadRegistrationAsync(connection, transaction, registration, "FOR SHARE OF r", sqlToken);
            if (native is null || !Owned(native, owner))
            {
                return Candidate<ManagementResolvedConfiguration>.Refused("management.configuration.not_found");
            }
            if (native.ClosedAtUtc is not null)
            {
                return Candidate<ManagementResolvedConfiguration>.Refused("management.registration.closed");
            }

            // A single SQL observation chooses the map, frozen cohort, release and BOTH publication payloads.
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT a.generation,b.host_scope,b.binding_id,se.cohort_id,se.supported_body,r.release_id,
                    r.common_publication_id,r.application_publication_id,
                    cp.publication_id,cs.source_id,cd.configuration_id,cd.schema_version,cs.environment,cs.application_id,cp.sha256,cp.payload,
                    ap.publication_id,aps.source_id,ad.configuration_id,ad.schema_version,aps.environment,aps.application_id,ap.sha256,ap.payload
                FROM {Table("configuration_session")} se
                JOIN {Table("configuration_binding")} b ON b.application_id=se.application_id AND b.enabled
                JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id AND d.enabled
                JOIN {Table("configuration_assignment")} a ON a.binding_id=b.binding_id
                LEFT JOIN {Table("configuration_override")} o ON o.binding_id=b.binding_id AND o.cohort_id=se.cohort_id
                JOIN {Table("configuration_release")} r ON r.release_id=COALESCE(o.release_id,a.default_release_id) AND r.binding_id=b.binding_id
                LEFT JOIN {Table("configuration_publication")} cp ON cp.publication_id=r.common_publication_id
                LEFT JOIN {Table("configuration_source")} cs ON cs.source_id=cp.source_id
                LEFT JOIN {Table("configuration_definition")} cd ON cd.definition_id=cs.definition_id
                LEFT JOIN {Table("configuration_publication")} ap ON ap.publication_id=r.application_publication_id
                LEFT JOIN {Table("configuration_source")} aps ON aps.source_id=ap.source_id
                LEFT JOIN {Table("configuration_definition")} ad ON ad.definition_id=aps.definition_id
                WHERE se.registration_id=@registration AND se.application_id=@application
                    AND d.configuration_id=@configuration AND d.schema_version=@schema AND a.generation IS NOT NULL
                """, ("registration", registration), ("application", native.ApplicationId), ("configuration", configurationId), ("schema", schema));
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
            if (!await reader.ReadAsync(sqlToken))
            {
                return Candidate<ManagementResolvedConfiguration>.Refused("management.configuration.not_found");
            }
            string host = reader.GetString(1);
            string binding = reader.GetGuid(2).ToString("D");
            SessionBinding[] supported = JsonSerializer.Deserialize<SessionBinding[]>(reader.GetFieldValue<byte[]>(4), Json)
                ?? throw new ManagementSchemaException("A retained configuration session is unreadable.");
            if (supported.Length > 256 || !supported.Any(item => item.BindingId == binding && item.ConfigurationId == configurationId
                && item.SchemaVersion == schemaVersion && item.HostApplicationName == host))
            {
                return Candidate<ManagementResolvedConfiguration>.Refused("management.configuration.not_found");
            }
            var desired = new ManagementConfigurationDesiredContext(confirmed.AuthorityId, native.ApplicationId.ToString("D"),
                native.Environment, registrationId, configurationId, schemaVersion, host, reader.GetGuid(5).ToString("D"),
                reader.GetInt64(0).ToString(CultureInfo.InvariantCulture), reader.IsDBNull(3) ? null : reader.GetString(3));
            var snapshot = new ManagementConfigurationSnapshot(desired, "\"snapshot-content\"",
                SnapshotSource(reader, 6, 8), SnapshotSource(reader, 7, 16));
            // The existing strict codec checks exact UTF-8 content digests and source/schema/scope before even a 304 can be selected.
            byte[] complete = ManagementConfigurationWire.EncodeResponse(snapshot);
            string etag = "\"configuration.snapshot." + Convert.ToHexStringLower(SHA256.HashData(complete)) + "\"";
            snapshot = snapshot with
            {
                ETag = etag
            };
            _ = ManagementConfigurationWire.EncodeResponse(snapshot);
            return Candidate<ManagementResolvedConfiguration>.Existing(new(ifNoneMatch == etag, ifNoneMatch == etag ? null : snapshot, etag));
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    private static ManagementConfigurationSourceContent SnapshotSource(DbDataReader reader, int reference, int start)
    {
        if (reader.IsDBNull(reference))
        {
            return new("absent");
        }
        if (reader.IsDBNull(start))
        {
            throw new ManagementSchemaException("An assigned configuration publication is absent.");
        }
        string? application = reader.IsDBNull(start + 5) ? null : reader.GetGuid(start + 5).ToString("D");
        return new("present", reader.GetGuid(start).ToString("D"), reader.GetString(start + 6),
            new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(start + 7)), reader.GetGuid(start + 1).ToString("D"),
            reader.GetString(start + 2), reader.GetInt64(start + 3).ToString(CultureInfo.InvariantCulture), reader.GetString(start + 4),
            application is null ? "common" : "application", application);
    }

    internal static bool SnapshotPortableId(string value)
        => value is { Length: >= 1 and <= 128 } && value is not "." and not ".."
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '~' or '-');

    internal static bool SnapshotSchema(string value, out long schema)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out schema) && schema > 0
            && schema.ToString(CultureInfo.InvariantCulture) == value;
}
