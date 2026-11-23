using CIS.Management;

namespace CIS.CommonTesting;

/// <summary>Selects one current logical admission observation without running freshness, authority or lifecycle policy.</summary>
/// <remarks>This explicit test control retains no state history. Changed handlers run synchronously outside its lock;
/// their original faults remain visible to Set's caller. It never cancels admitted work or proves native bootstrap.</remarks>
public sealed class ManualManagedWorkAdmission : IManagedWorkAdmission
{
    private readonly object gate = new();
    private ManagedWorkAdmissionSnapshot snapshot = new(ManagedWorkAdmissionState.AwaitingBootstrap, false, 0);
    private long checks;

    /// <summary>Gets the saturating count of logical admission checks.</summary>
    public long AdmissionChecks
    {
        get
        {
            lock (gate)
            {
                return checks;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<ManagedWorkAdmissionChangedEventArgs>? Changed;

    /// <summary>Publishes exact selected local facts; this helper does not infer or validate remote authority.</summary>
    public void Set(ManagedWorkAdmissionSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            snapshot = value;
        }
        Changed?.Invoke(this, new(value));
    }

    /// <inheritdoc />
    public bool TryAdmit()
    {
        lock (gate)
        {
            checks = checks == long.MaxValue ? checks : checks + 1;
            return snapshot.AdmissionAllowed;
        }
    }

    /// <inheritdoc />
    public ManagedWorkAdmissionSnapshot CaptureSnapshot()
    {
        lock (gate)
        {
            return snapshot;
        }
    }
}
