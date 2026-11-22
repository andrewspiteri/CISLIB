using System.Data.Common;
using System.Runtime.ExceptionServices;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Coordination.PostgreSql;

/// <summary>One PostgreSQL writer authority using the application's existing explicit transaction factory.</summary>
/// <remarks>Construction is passive. The application owns its data source, explicit installation and host drain.
/// Each physical operation owns and settles a short ReadCommitted transaction; it never retries an uncertain commit.</remarks>
public sealed partial class PostgreSqlCoordinationProvider : ICoordinationProvider, ICoordinationGuard
{
    private readonly ILocalTransactionFactory transactions;
    private readonly PostgreSqlCoordinationSettings settings;

    /// <summary>Captures one named database and expected installed authority without resolving or opening a data source.</summary>
    public PostgreSqlCoordinationProvider(ILocalTransactionFactory transactions, string databaseName, PostgreSqlCoordinationOptions options)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        this.transactions = transactions;
        settings = new(databaseName, options);
    }

    /// <inheritdoc />
    public Guid AuthorityId => settings.AuthorityId;

    /// <inheritdoc />
    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        await using ILocalTransaction owner = await BeginAsync(cancellationToken).ConfigureAwait(false);
        await owner.Context.UseAdoNetAsync(CheckAuthorityAsync, cancellationToken).ConfigureAwait(false);
        await owner.RollbackAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CoordinationAcquireResult> AcquireAsync(CoordinationAcquireRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AuthorityExecution<CoordinationAcquireResult> execution = await ExecuteOwnedAsync(async context =>
            await context.UseAdoNetAsync(async (connection, transaction, token) =>
            {
                await CheckAuthorityAsync(connection, transaction, token).ConfigureAwait(false);
                await LockOrCreateAsync(connection, transaction, request.Acquisition.Key, token).ConfigureAwait(false);
                AuthorityRow row = await ReadRowAsync(connection, transaction, request.Acquisition.Key, false, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The serialized coordination key is missing.");
                DateTimeOffset now = await ClockAsync(connection, transaction, token).ConfigureAwait(false);
                if (!row.Released && row.ExpiresAtUtc > now)
                {
                    return new CoordinationAcquireResult(CoordinationAcquireOutcome.Busy, observedOwner:
                        new(row.OwnerApplication!, row.InstanceId!, row.Generation!.Value, row.ExpiresAtUtc!.Value, now));
                }
                CoordinationGrant grant = await WriteAcquisitionAsync(connection, transaction, request, now, token).ConfigureAwait(false);
                TimeSpan remaining = grant.ExpiresAtUtc - await ClockAsync(connection, transaction, token).ConfigureAwait(false);
                if (remaining <= TimeSpan.Zero)
                {
                    context.MarkRollbackOnly("coordination.no_remaining_validity");
                    return new CoordinationAcquireResult(CoordinationAcquireOutcome.Unavailable);
                }
                return new CoordinationAcquireResult(CoordinationAcquireOutcome.Acquired, grant, remaining);
            }, cancellationToken).ConfigureAwait(false), commit: true, cancellationToken).ConfigureAwait(false);
        return execution.Outcome switch
        {
            TransactionOutcome.Committed => execution.Result!,
            TransactionOutcome.Unknown => new(CoordinationAcquireOutcome.Indeterminate, reasonCode: "coordination.commit_unknown"),
            _ => new(CoordinationAcquireOutcome.Unavailable, reasonCode: "coordination.not_committed")
        };
    }

    /// <inheritdoc />
    public Task<CoordinationMutationResult> RenewAsync(CoordinationGrant grant, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        CoordinationText.Duration(leaseDuration, nameof(leaseDuration));
        return MutateAsync(grant, leaseDuration, Mutation.Renew, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CoordinationMutationResult> ReleaseAsync(CoordinationGrant grant, CancellationToken cancellationToken = default)
        => MutateAsync(grant, TimeSpan.Zero, Mutation.Release, cancellationToken);

    /// <summary>Explicitly removes only this exact released/expired record, retaining the independent durable allocator.</summary>
    /// <remarks>No worker, broad sweep or automatic history reset is selected. A live record is left unchanged.</remarks>
    public Task<CoordinationMutationResult> PruneAsync(CoordinationGrant grant, CancellationToken cancellationToken = default)
        => MutateAsync(grant, TimeSpan.Zero, Mutation.Prune, cancellationToken);

    /// <inheritdoc />
    public async Task<CoordinationObservation> ResolveAsync(CoordinationAcquisition acquisition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        AuthorityExecution<CoordinationObservation> execution = await ExecuteOwnedAsync(async context =>
            await context.UseAdoNetAsync(async (connection, transaction, token) =>
            {
                await CheckAuthorityAsync(connection, transaction, token).ConfigureAwait(false);
                AuthorityRow? row = await ReadRowAsync(connection, transaction, acquisition.Key, true, token).ConfigureAwait(false);
                DateTimeOffset now = await ClockAsync(connection, transaction, token).ConfigureAwait(false);
                if (row is null || row.Released || !row.Matches(acquisition) || row.ExpiresAtUtc <= now)
                {
                    return new CoordinationObservation(CoordinationObservationOutcome.NotObserved);
                }
                var grant = new CoordinationGrant(AuthorityId, acquisition, row.Generation!.Value, row.ExpiresAtUtc!.Value);
                return new CoordinationObservation(CoordinationObservationOutcome.FoundLive, grant, grant.ExpiresAtUtc - now);
            }, cancellationToken).ConfigureAwait(false), commit: false, cancellationToken).ConfigureAwait(false);
        return execution.Result ?? new(CoordinationObservationOutcome.Unavailable);
    }

    private async Task<CoordinationMutationResult> MutateAsync(CoordinationGrant grant, TimeSpan duration,
        Mutation mutation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (grant.AuthorityId != AuthorityId)
        {
            return new(CoordinationMutationOutcome.NotCurrent, rejectionReason: CoordinationRejectionReason.WrongAuthority);
        }
        AuthorityExecution<CoordinationMutationResult> execution = await ExecuteOwnedAsync(async context =>
            await context.UseAdoNetAsync(async (connection, transaction, token) =>
            {
                await CheckAuthorityAsync(connection, transaction, token).ConfigureAwait(false);
                AuthorityRow? row = await ReadRowAsync(connection, transaction, grant.Key, false, token).ConfigureAwait(false);
                DateTimeOffset now = await ClockAsync(connection, transaction, token).ConfigureAwait(false);
                CoordinationRejectionReason reason = row?.Rejection(grant, now) ?? CoordinationRejectionReason.Missing;
                if (mutation == Mutation.Prune && row is not null && row.Matches(grant.Acquisition) && row.Generation == grant.Generation
                    && (row.Released || row.ExpiresAtUtc <= now))
                {
                    return await ChangeRecordAsync(connection, transaction, grant, mutation, now, token).ConfigureAwait(false);
                }
                if (mutation == Mutation.Release && reason == CoordinationRejectionReason.Expired)
                {
                    return await ChangeRecordAsync(connection, transaction, grant, mutation, now, token).ConfigureAwait(false);
                }
                if (reason != CoordinationRejectionReason.None)
                {
                    return new CoordinationMutationResult(CoordinationMutationOutcome.NotCurrent, rejectionReason: reason);
                }
                if (mutation == Mutation.Prune)
                {
                    // A live exact record is not eligible for pruning; no successor is modified.
                    return new CoordinationMutationResult(CoordinationMutationOutcome.Unavailable, reasonCode: "coordination.prune_live");
                }
                if (mutation == Mutation.Release)
                {
                    return await ChangeRecordAsync(connection, transaction, grant, mutation, now, token).ConfigureAwait(false);
                }
                CoordinationGrant renewed = await WriteRenewalAsync(connection, transaction, grant, duration, now, token).ConfigureAwait(false);
                TimeSpan remaining = renewed.ExpiresAtUtc - await ClockAsync(connection, transaction, token).ConfigureAwait(false);
                if (remaining <= TimeSpan.Zero)
                {
                    context.MarkRollbackOnly("coordination.no_remaining_validity");
                    return new CoordinationMutationResult(CoordinationMutationOutcome.Unavailable);
                }
                return new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded, renewed, remaining);
            }, cancellationToken).ConfigureAwait(false), commit: true, cancellationToken).ConfigureAwait(false);
        return execution.Outcome switch
        {
            TransactionOutcome.Committed => execution.Result!,
            TransactionOutcome.Unknown => new(CoordinationMutationOutcome.Indeterminate, reasonCode: "coordination.commit_unknown"),
            _ => new(CoordinationMutationOutcome.Unavailable, reasonCode: "coordination.not_committed")
        };
    }

    private async Task<CoordinationMutationResult> ChangeRecordAsync(DbConnection connection, DbTransaction transaction,
        CoordinationGrant grant, Mutation mutation, DateTimeOffset now, CancellationToken token)
    {
        string sql = mutation == Mutation.Prune
            ? $"DELETE FROM {settings.Grants} WHERE {ExactPredicate} AND (released OR expires_at <= @now)"
            : $"UPDATE {settings.Grants} SET released = true WHERE {ExactPredicate} AND NOT released";
        await using DbCommand command = Command(connection, transaction, sql);
        AddGrant(command, grant);
        if (mutation == Mutation.Prune)
        {
            Parameter(command, "now", System.Data.DbType.DateTimeOffset, now);
        }
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("The locked exact grant was not changed exactly once.");
        }
        return new(CoordinationMutationOutcome.Succeeded);
    }

    private async Task<ILocalTransaction> BeginAsync(CancellationToken token)
    {
        ILocalTransaction owner = await transactions.BeginAsync(settings.DatabaseName,
            new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The transaction factory returned no coordination owner.");
        if (owner.Context.DatabaseName != settings.DatabaseName || owner.Context.Isolation != TransactionIsolation.ReadCommitted)
        {
            await owner.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("Coordination requires the exact registered ReadCommitted database owner.");
        }
        return owner;
    }

    private async Task<AuthorityExecution<T>> ExecuteOwnedAsync<T>(Func<ILocalTransactionContext, Task<T>> work,
        bool commit, CancellationToken token) where T : class
    {
        ILocalTransaction? owner = null;
        T? result = null;
        TransactionOutcome outcome = TransactionOutcome.NotCommitted;
        bool commitEntered = false;
        ExceptionDispatchInfo? primary = null;
        try
        {
            owner = await BeginAsync(token).ConfigureAwait(false);
            result = await work(owner.Context).ConfigureAwait(false);
            if (commit)
            {
                commitEntered = true;
                outcome = (await owner.CommitAsync(token).ConfigureAwait(false)).Outcome;
            }
            else
            {
                await owner.RollbackAsync().ConfigureAwait(false);
            }
        }
        catch (Exception error) when (IsInfrastructure(error) || commitEntered && error is OperationCanceledException)
        {
            outcome = commitEntered ? TransactionOutcome.Unknown : TransactionOutcome.NotCommitted;
            result = null;
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            if (owner is not null)
            {
                try
                {
                    await owner.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error) when (IsInfrastructure(error))
                {
                    // Published commit/read evidence survives a cleanup diagnostic. RM-02 normally records
                    // these itself; an external public factory may instead throw from disposal.
                }
                catch (Exception error)
                {
                    primary ??= ExceptionDispatchInfo.Capture(error);
                }
            }
        }
        primary?.Throw();
        if (outcome == TransactionOutcome.NotCommitted && result is null)
        {
            // Drivers can surface a transient native failure while acknowledging cancellation.
            // Honor it only after actual cleanup and before any commit evidence exists.
            token.ThrowIfCancellationRequested();
        }
        return new(result, outcome);
    }

    private static bool IsInfrastructure(Exception error) => error is DbException database
        && (database.IsTransient || database.SqlState == "2200H") || error is IOException or TimeoutException;

    private sealed record AuthorityExecution<T>(T? Result, TransactionOutcome Outcome) where T : class;

    private enum Mutation
    {
        Renew,
        Release,
        Prune
    }
}
