using CIS.Healthcheck;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Backends;

/// <summary>Actual primary/namespace qualification plus bounded retained readiness observations; the health getter performs no I/O.</summary>
public sealed class PostgreSqlManagementAuthority : IHealthReadinessSource
{
    private readonly object gate = new();
    private readonly ILocalTransactionFactory factory;
    private readonly ManagementServiceOptions options;
    private readonly TimeProvider clock;
    private ManagementAuthoritySnapshot? current;
    private long lastProbe;
    private int ready;
    private long invalidations;
    private bool stopping;

    public PostgreSqlManagementAuthority(ILocalTransactionFactory factory, ManagementServiceOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        this.factory = factory;
        this.options = options;
        clock = timeProvider ?? TimeProvider.System;
    }

    public bool IsReady
    {
        get
        {
            if (Volatile.Read(ref ready) == 0)
            {
                return false;
            }
            TimeSpan elapsed = clock.GetElapsedTime(Volatile.Read(ref lastProbe));
            return elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(15);
        }
    }

    public ManagementAuthoritySnapshot Current
    {
        get
        {
            lock (gate)
            {
                return current ?? throw new ManagementBackendException("management.authority.unavailable");
            }
        }
    }

    public void MarkUnavailable()
    {
        lock (gate)
        {
            invalidations = unchecked(invalidations + 1);
            Volatile.Write(ref ready, 0);
        }
    }

    public void StopObserving()
    {
        lock (gate)
        {
            stopping = true;
            MarkUnavailable();
        }
    }

    public async Task<ManagementAuthoritySnapshot> ProbeAsync(CancellationToken cancellationToken = default)
    {
        long started = clock.GetTimestamp();
        long generation;
        lock (gate)
        {
            if (stopping)
            {
                throw new ManagementBackendException("management.authority.unavailable");
            }
            generation = invalidations;
        }
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15), clock);
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        try
        {
            ManagementAuthoritySnapshot state;
            TransactionCompletion completion;
            await using (ILocalTransaction owner = await factory.BeginAsync("management", selected.Token).ConfigureAwait(false))
            {
                state = await ReadAsync(owner.Context, false, selected.Token).ConfigureAwait(false);
                completion = await owner.RollbackAsync().ConfigureAwait(false);
            }
            // Publication follows real owner disposal and starts freshness at entry,
            // never at late response/cleanup arrival.
            selected.Token.ThrowIfCancellationRequested();
            TimeSpan elapsed = clock.GetElapsedTime(started);
            if (!completion.RollbackConfirmed || completion.CleanupErrors.Count != 0)
            {
                throw new ManagementBackendException("management.authority.unavailable");
            }
            if (elapsed < TimeSpan.Zero || elapsed >= TimeSpan.FromSeconds(15))
            {
                throw new ManagementBackendException("management.authority.unavailable");
            }
            lock (gate)
            {
                if (stopping || invalidations != generation || current != state || state.RecoveryHold || options.Backend.RecoveryHold)
                {
                    Volatile.Write(ref ready, 0);
                }
                else
                {
                    Volatile.Write(ref lastProbe, started);
                    Volatile.Write(ref ready, 1);
                }
            }
            return state;
        }
        catch
        {
            MarkUnavailable();
            throw;
        }
    }

    /// <summary>Holds namespace/recovery state in the original participant until its owner actually completes.</summary>
    public async Task<ManagementAuthoritySnapshot> ReadAsync(ILocalTransactionContext context, bool requireReady,
        CancellationToken cancellationToken = default)
    {
        ManagementSql.RequireContext(context);
        try
        {
            ManagementAuthoritySnapshot state = await context.UseAdoNetAsync(async (connection, transaction, token) =>
            {
                await using var command = ManagementSql.Command(connection, transaction, $"""
                    SELECT namespace_name,authority_id,control_revision,recovery_epoch,recovery_hold,
                        pg_is_in_recovery(),current_setting('transaction_read_only'),current_setting('fsync'),
                        current_setting('full_page_writes'),current_setting('synchronous_commit'),
                        current_setting('server_version_num')::integer
                    FROM {ManagementSql.Table(options.Persistence.Schema, "management_authority")} WHERE singleton=true FOR SHARE
                    """);
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    throw new ManagementBackendException("management.authority.unavailable");
                }
                if (reader.GetBoolean(5) || reader.GetString(6) != "off" || reader.GetString(7) != "on"
                    || reader.GetString(8) != "on" || reader.GetString(9) != "on" || reader.GetInt32(10) < 180000)
                {
                    throw new ManagementBackendException("management.backend.profile_invalid");
                }
                return new ManagementAuthoritySnapshot(reader.GetString(0), reader.GetGuid(1).ToString("D"),
                    reader.GetString(2), reader.GetInt64(3), reader.GetBoolean(4));
            }, cancellationToken).ConfigureAwait(false);
            if (state.NamespaceName != options.Backend.NamespaceName || state.RecoveryEpoch != options.Backend.ExpectedRecoveryEpoch
                || !ManagementWire.IsUuid(state.AuthorityId) || !ManagementWire.IsScalarText(state.ControlRevision, 256))
            {
                throw new ManagementBackendException("management.authority.mismatch");
            }
            bool held = state.RecoveryHold || options.Backend.RecoveryHold;
            lock (gate)
            {
                if (current is not null && current.AuthorityId != state.AuthorityId)
                {
                    throw new ManagementBackendException("management.authority.mismatch");
                }
                current = state;
            }
            if (held)
            {
                MarkUnavailable();
            }
            if (requireReady && held)
            {
                throw new ManagementBackendException("management.authority.recovery_required");
            }
            return state;
        }
        catch
        {
            MarkUnavailable();
            throw;
        }
    }
}
