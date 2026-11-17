using System.Collections.ObjectModel;
using CIS.Heartbeat;

namespace CIS.Configuration;

internal sealed class ConfigurationSettings
{
    private ConfigurationSettings(string hostApplicationName, IConfigurationStore store, ConfigurationNotificationMode notificationMode,
        TimeSpan? pollingInterval, bool? isHealthCritical,
        IReadOnlyDictionary<Type, Func<IConfigurationObject>> factories, IReadOnlyDictionary<Type, Action<IConfigurationObject>> validators)
    {
        HostApplicationName = hostApplicationName;
        Store = store;
        NotificationMode = notificationMode;
        PollingInterval = pollingInterval;
        IsHealthCritical = isHealthCritical;
        Factories = factories;
        Validators = validators;
    }

    internal string HostApplicationName { get; }
    internal IConfigurationStore Store { get; }
    internal ConfigurationNotificationMode NotificationMode { get; }
    internal TimeSpan? PollingInterval { get; }
    internal bool? IsHealthCritical { get; }
    internal IReadOnlyDictionary<Type, Func<IConfigurationObject>> Factories { get; }
    internal IReadOnlyDictionary<Type, Action<IConfigurationObject>> Validators { get; }

    internal static ConfigurationSettings Capture(ConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string hostApplicationName = options.HostApplicationName;
        IConfigurationStore store = options.Store;
        TimeSpan? interval = options.PollingInterval;
        bool? isHealthCritical = options.IsHealthCritical;
        ConfigurationNotificationMode mode;
        try { mode = store.NotificationMode; }
        catch (Exception)
        { throw Error("Reading the store notification guarantee failed."); }
        if (!Enum.IsDefined(mode)) { throw Error("The store notification guarantee is undefined."); }
        if (interval is { } selected && (selected <= TimeSpan.Zero || selected > HeartbeatRegistrationOptions.MaximumDuration))
        { throw Error("Polling requires a positive interval no greater than HeartbeatRegistrationOptions.MaximumDuration."); }
        if (mode != ConfigurationNotificationMode.Reliable && interval is null)
        { throw Error("This store guarantee requires an explicit positive polling/reconciliation interval."); }
        return new(hostApplicationName, store, mode, interval, isHealthCritical,
            Snapshot(options.Factories), Snapshot(options.Validators));
    }

    private static IReadOnlyDictionary<Type, T> Snapshot<T>(IReadOnlyDictionary<Type, T> source) where T : Delegate
    {
        if (source is null) { throw Error("Factory and validator dictionaries must be nonnull."); }
        var copy = new Dictionary<Type, T>();
        try
        {
            foreach ((Type type, T callback) in source)
            {
                if (type is null || callback is null || !typeof(IConfigurationObject).IsAssignableFrom(type))
                { throw Error("Factory and validator entries require an exact configuration type and a nonnull callback."); }
                copy.Add(type, callback);
            }
        }
        catch (Exception) { throw Error("Factory and validator dictionaries require unique exact configuration types and nonnull callbacks."); }
        return new ReadOnlyDictionary<Type, T>(copy);
    }

    private static CoreConfigurationException Error(string explanation)
        => ConfigurationDiagnostics.Error(typeof(ConfigurationOptions), "Configuration.Options", explanation);
}
