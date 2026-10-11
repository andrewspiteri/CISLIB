namespace CIS.Configuration;

internal enum ConfigurationSource
{
    Absent,
    Environment,
    Setup,
    Discovery,
    Default
}

internal sealed class ConfigurationSelection<T>
{
    private readonly T value;

    private ConfigurationSelection(ConfigurationSource source, string sourceKey, string? token, T value)
    {
        Source = source;
        SourceKey = sourceKey;
        Token = token;
        this.value = value;
    }

    public ConfigurationSource Source { get; }
    public string SourceKey { get; }
    public string? Token { get; }
    public T Value => Source is ConfigurationSource.Environment or ConfigurationSource.Absent
        ? throw new InvalidOperationException("The selected configuration value is not a typed value.")
        : value;

    public static ConfigurationSelection<T> FromEnvironment(string key, string token) =>
        new(ConfigurationSource.Environment, key, token, default!);

    public static ConfigurationSelection<T> FromValue(ConfigurationSource source, string key, T value) =>
        new(source, key, null, value);

    public static ConfigurationSelection<T> Absent(string key) =>
        new(ConfigurationSource.Absent, key, null, default!);
}
