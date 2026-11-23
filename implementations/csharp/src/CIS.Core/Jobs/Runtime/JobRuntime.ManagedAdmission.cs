using CIS.Management;

namespace CIS.Jobs;

public sealed partial class JobRuntime
{
    internal const string ManagedAdmissionClosed = "jobs.management_admission.closed";
    private readonly IManagedWorkAdmission? managedAdmission;
    private int managedAdmissionDetached;
    internal IManagedWorkAdmission? ManagedAdmission => managedAdmission;

    // No token is retained: expiry/local prerequisites are evaluated at every actual finite boundary.
    internal bool CanAdmitManagedWork() => managedAdmission?.TryAdmit() ?? true;

    internal async Task WaitForManagedAdmissionAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long observed = ChangeVersion;
            if (CanAdmitManagedWork())
            {
                return;
            }
            await WaitForChangeAsync(observed, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ManagedAdmissionChanged(object? sender, ManagedWorkAdmissionChangedEventArgs args)
    {
        // Publications are observations, not reusable permission. Use the existing bounded native
        // condition signal for both duty/capacity and hosting activation waiters, without new work.
        lock (gate)
        {
            if (!closing)
            {
                PulseUnderGate();
            }
        }
    }
}
