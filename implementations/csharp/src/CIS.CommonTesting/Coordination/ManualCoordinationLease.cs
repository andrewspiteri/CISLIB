using CIS.Coordination;

namespace CIS.CommonTesting;

/// <summary>A manually controlled local lease for consumer tests using only public coordination contracts.</summary>
/// <remarks>This helper has no authority, automatic expiry, renewal, database access or business-task ownership.
/// Tests explicitly confirm a grant or end ownership; direct consumers still own and drain their actual work.</remarks>
public sealed class ManualCoordinationLease : ICoordinationLease
{
    private readonly object gate = new();
    private readonly CancellationTokenSource ownershipLost = new();
    private readonly CancellationToken ownershipLostToken;
    private readonly TaskCompletionSource<CoordinationLeaseTermination> termination = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource cancellationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CoordinationMutationResult releaseResult;
    private CoordinationGrant grant;
    private CoordinationLeaseState state = CoordinationLeaseState.Owned;
    private CoordinationLossReason? lossReason;
    private TimeSpan? drainBudget;
    private Task<CoordinationMutationResult>? releaseTask;
    private Exception? cancellationException;
    private bool disposed;

    /// <summary>Supplies an initial test grant and one fixed exact-release result, successful by default.</summary>
    public ManualCoordinationLease(CoordinationGrant grant, CoordinationMutationResult? releaseResult = null)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (releaseResult?.Grant is not null)
        {
            throw new ArgumentException("A scripted release result cannot supply a renewed grant.", nameof(releaseResult));
        }

        this.grant = grant;
        this.releaseResult = releaseResult ?? new(CoordinationMutationOutcome.Succeeded);
        ownershipLostToken = ownershipLost.Token;
    }

    /// <inheritdoc />
    public CoordinationGrant Grant
    {
        get
        {
            lock (gate)
            {
                return grant;
            }
        }
    }

    /// <inheritdoc />
    public CoordinationLeaseState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    /// <inheritdoc />
    public CoordinationLossReason? LossReason
    {
        get
        {
            lock (gate)
            {
                return lossReason;
            }
        }
    }

    /// <inheritdoc />
    public CancellationToken OwnershipLost => ownershipLostToken;

    /// <inheritdoc />
    public Task<CoordinationLeaseTermination> Termination => termination.Task;

    /// <summary>Gets actual settlement of cancellation callbacks after the first terminal transition.</summary>
    public Task CancellationCompletion => cancellationCompletion.Task;

    /// <summary>Gets the single retained cancellation-callback fault, without replacing the scripted release result.</summary>
    public Exception? CancellationException
    {
        get
        {
            lock (gate)
            {
                return cancellationException;
            }
        }
    }

    /// <summary>Gets the first finite requested drain budget; tests decide when that budget expires.</summary>
    public TimeSpan? DrainBudget
    {
        get
        {
            lock (gate)
            {
                return drainBudget;
            }
        }
    }

    /// <summary>Updates only the expiry observation of the same exact grant; terminal handles cannot revive.</summary>
    public bool ConfirmGrant(CoordinationGrant confirmedGrant)
    {
        ArgumentNullException.ThrowIfNull(confirmedGrant);
        lock (gate)
        {
            if (confirmedGrant.AuthorityId != grant.AuthorityId
                || confirmedGrant.Acquisition != grant.Acquisition
                || confirmedGrant.Generation != grant.Generation)
            {
                throw new ArgumentException("A confirmation must preserve the exact grant identity.", nameof(confirmedGrant));
            }
            if (state is CoordinationLeaseState.Lost or CoordinationLeaseState.Released)
            {
                return false;
            }

            grant = confirmedGrant;
            return true;
        }
    }

    /// <inheritdoc />
    public void BeginDraining(TimeSpan? drainBudget = null)
    {
        TimeSpan selected = drainBudget ?? TimeSpan.FromSeconds(30);
        if (selected <= TimeSpan.Zero || selected > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(drainBudget));
        }

        lock (gate)
        {
            if (state == CoordinationLeaseState.Owned)
            {
                state = CoordinationLeaseState.Draining;
                this.drainBudget = selected;
            }
        }
    }

    /// <summary>Ends ownership once with an explicit test reason and awaits actual cancellation-callback settlement.</summary>
    public Task LoseAsync(CoordinationLossReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        bool changed;
        lock (gate)
        {
            changed = EndOwnership(CoordinationLeaseState.Lost, reason);
        }
        if (changed)
        {
            _ = CompleteCancellationAsync();
        }

        return cancellationCompletion.Task;
    }

    /// <inheritdoc />
    public Task<CoordinationMutationResult> ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<CoordinationMutationResult>(cancellationToken);
        }

        bool changed;
        TaskCompletionSource<CoordinationMutationResult> completion;
        lock (gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<CoordinationMutationResult>(cancellationToken);
            }
            if (releaseTask is not null)
            {
                return releaseTask;
            }

            changed = EndOwnership(CoordinationLeaseState.Released, null);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            releaseTask = completion.Task;
        }

        _ = CompleteReleaseAsync(completion, changed);
        return completion.Task;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync().ConfigureAwait(false);
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        ownershipLost.Dispose();
    }

    private bool EndOwnership(CoordinationLeaseState terminalState, CoordinationLossReason? reason)
    {
        if (state is CoordinationLeaseState.Lost or CoordinationLeaseState.Released)
        {
            return false;
        }

        state = terminalState;
        lossReason = reason;
        termination.TrySetResult(new(terminalState, reason));
        return true;
    }

    private async Task CompleteReleaseAsync(TaskCompletionSource<CoordinationMutationResult> completion, bool changed)
    {
        if (changed)
        {
            await CompleteCancellationAsync().ConfigureAwait(false);
        }
        else
        {
            await cancellationCompletion.Task.ConfigureAwait(false);
        }

        completion.TrySetResult(releaseResult);
    }

    private async Task CompleteCancellationAsync()
    {
        try
        {
            await ownershipLost.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                cancellationException = exception;
            }
        }
        finally
        {
            cancellationCompletion.TrySetResult();
        }
    }
}
