namespace CIS.Configuration;

internal sealed class ConfigurationSetting(string key, string? alias, string setupKey, string discoveryKey)
{
    public string Key { get; } = key;
    public string? Alias { get; } = alias;
    public string SetupKey { get; } = setupKey;
    public string DiscoveryKey { get; } = discoveryKey;
}
