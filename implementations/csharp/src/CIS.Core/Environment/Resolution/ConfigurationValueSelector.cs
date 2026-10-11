namespace CIS.Configuration;

internal static class ConfigurationValueSelector
{
    public static ConfigurationSelection<T> Select<T>(
        IEnvironmentVariableSource variables,
        string key,
        ConfigurationCandidate<T> setup = default,
        ConfigurationCandidate<T> discovered = default,
        ConfigurationCandidate<T> fallback = default)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ConfigurationSetting setting = ConfigurationSettings.Find(key);
        string? token = Nonblank(variables.GetValue(setting.Key));
        if (token is not null)
        {
            return ConfigurationSelection<T>.FromEnvironment(setting.Key, token);
        }

        if (setting.Alias is { } alias)
        {
            token = Nonblank(variables.GetValue(alias));
            if (token is not null)
            {
                return ConfigurationSelection<T>.FromEnvironment(alias, token);
            }
        }

        if (setup.HasValue)
        {
            return ConfigurationSelection<T>.FromValue(ConfigurationSource.Setup, setting.SetupKey, setup.Value);
        }

        if (discovered.HasValue)
        {
            return ConfigurationSelection<T>.FromValue(ConfigurationSource.Discovery, setting.DiscoveryKey, discovered.Value);
        }

        return fallback.HasValue
            ? ConfigurationSelection<T>.FromValue(ConfigurationSource.Default, setting.Key, fallback.Value)
            : ConfigurationSelection<T>.Absent(setting.Key);
    }

    private static string? Nonblank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
