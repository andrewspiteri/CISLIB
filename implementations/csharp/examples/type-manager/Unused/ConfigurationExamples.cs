using CIS.Configuration;

namespace CIS.TypeManagement.Examples.Unused;

// Separate passive data types; the original throwing discovery-only examples stay unchanged.
public sealed class DirectConfiguration : IConfigurationObject
{
    public string Greeting { get; set; } = "default-direct";

    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
    {
        add => ConfigurationEvents.Add(this, value);
        remove => ConfigurationEvents.Remove(this, value);
    }
}

public abstract class ConfigurationExampleBase : ConfigurationObject
{
    public int BatchSize { get; set; } = 1;
}

public sealed class InheritedConfiguration : ConfigurationExampleBase { }
