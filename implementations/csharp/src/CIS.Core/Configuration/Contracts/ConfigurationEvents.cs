using System.Runtime.CompilerServices;

namespace CIS.Configuration;

/// <summary>Connects direct-interface event accessors to isolated, per-object publication.</summary>
/// <remarks>Forward ConfigurationChanged add/remove accessors to Add/Remove using this as the source.</remarks>
public static class ConfigurationEvents
{
    private sealed class Handlers
    {
        internal readonly object Gate = new();
        internal EventHandler<ConfigurationChangedEventArgs>? Value;
        internal object? Owner;
    }

    private static readonly ConditionalWeakTable<IConfigurationObject, Handlers> States = new();

    /// <summary>Adds one handler in normal event subscription order.</summary>
    public static void Add(IConfigurationObject source, EventHandler<ConfigurationChangedEventArgs>? handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (handler is null) { return; }
        Handlers state = States.GetValue(source, static _ => new Handlers());
        lock (state.Gate) { state.Value += handler; }
    }

    /// <summary>Removes one matching handler using normal event semantics.</summary>
    public static void Remove(IConfigurationObject source, EventHandler<ConfigurationChangedEventArgs>? handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (handler is null || !States.TryGetValue(source, out Handlers? state)) { return; }
        lock (state.Gate) { state.Value -= handler; }
    }

    internal static void ValidateAndClaim(IConfigurationObject source, object owner)
    {
        EventHandler<ConfigurationChangedEventArgs> probe = static (_, _) => { };
        bool connected = false, removed = false, failed = false;
        Handlers? state = null;
        try
        {
            source.ConfigurationChanged += probe;
            if (States.TryGetValue(source, out state))
            {
                lock (state.Gate) { connected = state.Value?.GetInvocationList().Contains(probe) == true; }
            }
        }
        catch (Exception) { failed = true; }
        try
        {
            source.ConfigurationChanged -= probe;
            if (States.TryGetValue(source, out state))
            {
                lock (state.Gate)
                {
                    removed = state.Value?.GetInvocationList().Contains(probe) != true;
                    state.Value -= probe; // Clean our helper-side probe even for a broken remove accessor.
                }
            }
        }
        catch (Exception) { failed = true; Remove(source, probe); }
        if (!connected || !removed || failed || state is null)
        { throw ConfigurationDiagnostics.Error(source.GetType(), "Configuration.EventWiring", "ConfigurationChanged must forward its accessors to ConfigurationEvents or use ConfigurationObject."); }
        lock (state.Gate)
        {
            if (state.Owner is not null && !ReferenceEquals(state.Owner, owner))
            { throw ConfigurationDiagnostics.Error(source.GetType(), "Configuration.Ownership", "A configuration factory returned an object already owned by another preparation."); }
            state.Owner = owner;
        }
    }

    internal static void Publish(IConfigurationObject source, long revision, Action<Exception>? reportFailure = null)
    {
        if (!States.TryGetValue(source, out Handlers? state)) { return; }
        Delegate[] snapshot;
        lock (state.Gate) { snapshot = state.Value?.GetInvocationList() ?? []; }
        var arguments = new ConfigurationChangedEventArgs(source.GetType(), revision);
        foreach (EventHandler<ConfigurationChangedEventArgs> handler in snapshot)
        {
            try { handler(source, arguments); }
            catch (Exception error)
            {
                try { reportFailure?.Invoke(error); }
                catch (Exception) { /* Observation cannot change the accepted commit or skip later handlers. */ }
            }
        }
    }
}

/// <summary>Optional convenience base; direct IConfigurationObject implementation remains supported.</summary>
public abstract class ConfigurationObject : IConfigurationObject
{
    /// <inheritdoc />
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
    {
        add => ConfigurationEvents.Add(this, value);
        remove => ConfigurationEvents.Remove(this, value);
    }
}
