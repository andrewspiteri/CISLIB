using CIS.Configuration;

namespace CIS.Management.Hosting;

// One known facade composition bridge. Module admission observers only read cheap retained facts;
// actual network/materialization remains in Configuration's existing serialized owned work.
internal sealed class ManagementConfigurationReconciler : IManagedStateReconciler, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly ConfigurationRuntime configuration;
    private readonly ManagedWorkAdmission admission;
    private ConfigurationSnapshotTarget target;
    private ConfigurationSnapshotTarget? requested;
    private Task? rebind;
    private Exception? rebindFailure;
    private bool closing;
    private bool gapObserved;

    internal ManagementConfigurationReconciler(ConfigurationRuntime configuration, ManagedWorkAdmission admission,
        ConfigurationSnapshotTarget target)
    {
        this.configuration = configuration;
        this.admission = admission;
        this.target = target;
        admission.Changed += AdmissionChanged;
    }

    public bool IsReady => configuration.CaptureCentralReadiness().IsReady;

    internal void RequireGap()
    {
        lock (gate)
        {
            if (closing || gapObserved)
            {
                return;
            }
            gapObserved = true;
            configuration.RequireCentralReconciliation();
            _ = configuration.ReconcileCentralAsync();
        }
    }

    private void AdmissionChanged(object? sender, ManagedWorkAdmissionChangedEventArgs change)
    {
        if (change.Snapshot.ReasonCode is "management.control.expired" or "management.control.clock_gap"
            or "management.control.clock_unavailable" or "management.control.revalidation_required")
        {
            RequireGap();
        }
    }

    public Task<bool> ReconcileAsync(ManagementControlConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (closing || confirmation.AuthorityId != target.AuthorityId)
            {
                return Task.FromResult(false);
            }
            if (rebindFailure is not null)
            {
                return Task.FromException<bool>(rebindFailure);
            }
            if (confirmation.RegistrationId != target.RegistrationId)
            {
                requested = new(target.AuthorityId, target.ApplicationId, target.Environment, confirmation.RegistrationId,
                    target.HostApplicationName, target.CohortId);
                gapObserved = true;
                if (rebind is null || rebind.IsCompleted)
                {
                    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    rebind = completion.Task;
                    if (!ThreadPool.UnsafeQueueUserWorkItem(static work =>
                    {
                        _ = work.Owner.RebindAsync(work.Completion);
                    }, (Owner: this, Completion: completion), preferLocal: false))
                    {
                        completion.TrySetResult();
                    }
                }
                return Task.FromResult(false);
            }
            bool recovered = gapObserved;
            gapObserved = false;
            ConfigurationCentralReadiness readiness = configuration.CaptureCentralReadiness();
            if (recovered)
            {
                _ = configuration.ReconcileCentralAfterRecoveryAsync();
            }
            else if (!readiness.IsReady)
            {
                _ = configuration.ReconcileCentralAsync();
            }
            return Task.FromResult(readiness.IsReady && readiness.Target == target);
        }
    }

    private async Task RebindAsync(TaskCompletionSource completion)
    {
        try
        {
            while (true)
            {
                ConfigurationSnapshotTarget next;
                lock (gate)
                {
                    if (closing || requested is null)
                    {
                        break;
                    }
                    next = requested;
                    requested = null;
                }
                _ = await configuration.RebindSnapshotTargetAsync(next).ConfigureAwait(false);
                lock (gate)
                {
                    target = next;
                }
            }
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            lock (gate)
            {
                rebindFailure = error;
            }
            completion.TrySetException(error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? work;
        lock (gate)
        {
            closing = true;
            work = rebind;
        }
        admission.Changed -= AdmissionChanged;
        if (work is not null)
        {
            await work.ConfigureAwait(false);
        }
    }
}
