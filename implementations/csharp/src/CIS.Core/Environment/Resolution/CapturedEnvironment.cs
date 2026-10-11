using System.Collections.Frozen;

namespace CIS.Configuration;

internal sealed class CapturedEnvironment : IEnvironmentVariableSource
{
    private readonly FrozenDictionary<string, string?> values;

    private CapturedEnvironment(Dictionary<string, string?> values)
    {
        this.values = values.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static CapturedEnvironment Capture(IEnvironmentVariableSource? source = null)
    {
        source ??= ProcessEnvironmentVariableSource.Instance;
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (ConfigurationSetting setting in ConfigurationSettings.All)
        {
            values.Add(setting.Key, source.GetValue(setting.Key));
            if (setting.Alias is { } alias)
            {
                values.Add(alias, source.GetValue(alias));
            }
        }

        return new CapturedEnvironment(values);
    }

    public string? GetValue(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return values.GetValueOrDefault(name);
    }
}
