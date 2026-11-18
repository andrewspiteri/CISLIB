using CIS.Configuration;

namespace CIS.TypeManagement.Fixtures.Unused;

public sealed class DirectConfiguration : IConfigurationObject
{
    public string Greeting { get; set; } = "fixture-default";
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
    {
        add => ConfigurationEvents.Add(this, value);
        remove => ConfigurationEvents.Remove(this, value);
    }
}

public sealed class InheritedConfiguration : ConfigurationObject
{
    public int BatchSize { get; set; } = 42;
}
