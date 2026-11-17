namespace CIS.Configuration;

/// <summary>Explicit host/bootstrap inputs copied once during preparation.</summary>
public sealed class ConfigurationOptions
{
    /// <summary>Creates options using a fixed host-project identity and host-owned adapter.</summary>
    public ConfigurationOptions(string hostApplicationName, IConfigurationStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostApplicationName);
        ArgumentNullException.ThrowIfNull(store);
        if (hostApplicationName == "CommonConfiguration")
        { throw new ArgumentException("The common configuration scope is reserved.", nameof(hostApplicationName)); }
        HostApplicationName = hostApplicationName;
        Store = store;
    }

    /// <summary>Gets the fixed storage scope; EnvironmentOptions display overrides cannot redirect it.</summary>
    public string HostApplicationName { get; }
    /// <summary>Gets the host-owned bootstrap adapter.</summary>
    public IConfigurationStore Store { get; }
    /// <summary>Gets an explicit positive interval when polling/reconciliation is needed; there is no default.</summary>
    public TimeSpan? PollingInterval { get; init; }
    /// <summary>Gets the explicit monitor criticality required when Healthcheck is connected.</summary>
    public bool? IsHealthCritical { get; init; }
    /// <summary>Gets exceptional exact-type default factories. Factories must return fresh passive objects without I/O.</summary>
    public IReadOnlyDictionary<Type, Func<IConfigurationObject>> Factories { get; init; }
        = new Dictionary<Type, Func<IConfigurationObject>>();
    /// <summary>Gets optional synchronous cross-property validators, run on detached complete candidates.</summary>
    public IReadOnlyDictionary<Type, Action<IConfigurationObject>> Validators { get; init; }
        = new Dictionary<Type, Action<IConfigurationObject>>();
}
