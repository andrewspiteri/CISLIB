namespace CIS.Configuration;

internal enum ConfigurationParseState
{
    Absent,
    Parsed,
    InvalidToken,
    OutOfRange,
    UnsupportedSchemaVersion,
    DependsOnInvalidSetting
}

internal interface IParsedConfigurationSetting
{
    string Key { get; }
    string SourceKey { get; }
    ConfigurationSource Source { get; }
    ConfigurationParseState State { get; }
    string? InheritedFrom { get; }
}

internal sealed class ParsedConfigurationSetting<T> : IParsedConfigurationSetting
{
    private readonly T value;

    private ParsedConfigurationSetting(string key, ConfigurationSource source, string sourceKey,
        ConfigurationParseState state, T value, string? inheritedFrom = null)
    {
        Key = key;
        Source = source;
        SourceKey = sourceKey;
        State = state;
        this.value = value;
        InheritedFrom = inheritedFrom;
    }

    public string Key { get; }
    public ConfigurationSource Source { get; }
    public string SourceKey { get; }
    public ConfigurationParseState State { get; }
    public string? InheritedFrom { get; }
    public bool HasValue => State == ConfigurationParseState.Parsed;
    public T Value => HasValue ? value : throw new InvalidOperationException("The configuration setting has no parsed value.");

    public static ParsedConfigurationSetting<T> Parsed(string key, ConfigurationSelection<T> source, T value) =>
        new(key, source.Source, source.SourceKey, ConfigurationParseState.Parsed, value);

    public static ParsedConfigurationSetting<T> Unparsed(string key, ConfigurationSelection<T> source, ConfigurationParseState state) =>
        new(key, source.Source, source.SourceKey, state, default!);

    public static ParsedConfigurationSetting<T> Inherit(string key, ParsedConfigurationSetting<T> parent) =>
        new(key, ConfigurationSource.Default, key,
            parent.HasValue ? ConfigurationParseState.Parsed : ConfigurationParseState.DependsOnInvalidSetting,
            parent.HasValue ? parent.Value : default!, parent.Key);
}
