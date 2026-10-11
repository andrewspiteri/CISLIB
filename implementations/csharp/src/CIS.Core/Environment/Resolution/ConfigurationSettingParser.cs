using System.Collections.Frozen;
using System.Globalization;
using CIS.Logging.Configuration;

namespace CIS.Configuration;

internal static class ConfigurationSettingParser
{
    private static readonly FrozenDictionary<string, ConfigurationOutputFormat> Formats =
        new Dictionary<string, ConfigurationOutputFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["human"] = ConfigurationOutputFormat.Human,
            ["jsonl"] = ConfigurationOutputFormat.JsonLines
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    private static readonly FrozenDictionary<string, ConfigurationConsoleStream> Streams =
        new Dictionary<string, ConfigurationConsoleStream>(StringComparer.OrdinalIgnoreCase)
        {
            ["stderr"] = ConfigurationConsoleStream.StandardError,
            ["stdout"] = ConfigurationConsoleStream.StandardOutput
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    private static readonly FrozenDictionary<string, ConfigurationHumanDetail> Details =
        new Dictionary<string, ConfigurationHumanDetail>(StringComparer.OrdinalIgnoreCase)
        {
            ["normal"] = ConfigurationHumanDetail.Normal,
            ["verbose"] = ConfigurationHumanDetail.Verbose
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static ParsedConfigurationSetting<string> Text(string key, ConfigurationSelection<string> selected) =>
        selected.Source == ConfigurationSource.Absent
            ? ParsedConfigurationSetting<string>.Unparsed(key, selected, ConfigurationParseState.Absent)
            : ParsedConfigurationSetting<string>.Parsed(key, selected,
                selected.Source == ConfigurationSource.Environment ? selected.Token! : selected.Value);

    public static ParsedConfigurationSetting<ConfigurationSeverity> Severity(string key, ConfigurationSelection<ConfigurationSeverity> selected)
    {
        if (selected.Source == ConfigurationSource.Environment)
        {
            return SeverityTokenParser.Parse(selected.Token, out ConfigurationSeverity? value) == ConfigurationTokenState.Valid
                ? ParsedConfigurationSetting<ConfigurationSeverity>.Parsed(key, selected, value!.Value)
                : ParsedConfigurationSetting<ConfigurationSeverity>.Unparsed(key, selected, ConfigurationParseState.InvalidToken);
        }

        return TypedEnum(key, selected);
    }

    public static ParsedConfigurationSetting<ConfigurationOutputFormat> Format(string key, ConfigurationSelection<ConfigurationOutputFormat> selected) => Token(key, selected, Formats);
    public static ParsedConfigurationSetting<ConfigurationConsoleStream> Stream(string key, ConfigurationSelection<ConfigurationConsoleStream> selected) => Token(key, selected, Streams);
    public static ParsedConfigurationSetting<ConfigurationHumanDetail> Detail(string key, ConfigurationSelection<ConfigurationHumanDetail> selected) => Token(key, selected, Details);

    public static ParsedConfigurationSetting<int> Integer(string key, ConfigurationSelection<int> selected)
    {
        if (selected.Source != ConfigurationSource.Environment)
        {
            return selected.Source == ConfigurationSource.Absent
                ? ParsedConfigurationSetting<int>.Unparsed(key, selected, ConfigurationParseState.Absent)
                : ParsedConfigurationSetting<int>.Parsed(key, selected, selected.Value);
        }

        ConfigurationParseState state = ParseLong(selected.Token!, out long value);
        if (state == ConfigurationParseState.Parsed && value is < int.MinValue or > int.MaxValue)
        {
            state = ConfigurationParseState.OutOfRange;
        }

        return state == ConfigurationParseState.Parsed
            ? ParsedConfigurationSetting<int>.Parsed(key, selected, (int)value)
            : ParsedConfigurationSetting<int>.Unparsed(key, selected, state);
    }

    public static ParsedConfigurationSetting<long> LongInteger(string key, ConfigurationSelection<long> selected)
    {
        if (selected.Source != ConfigurationSource.Environment)
        {
            return selected.Source == ConfigurationSource.Absent
                ? ParsedConfigurationSetting<long>.Unparsed(key, selected, ConfigurationParseState.Absent)
                : ParsedConfigurationSetting<long>.Parsed(key, selected, selected.Value);
        }

        ConfigurationParseState state = ParseLong(selected.Token!, out long value);
        return state == ConfigurationParseState.Parsed
            ? ParsedConfigurationSetting<long>.Parsed(key, selected, value)
            : ParsedConfigurationSetting<long>.Unparsed(key, selected, state);
    }

    public static ParsedConfigurationSetting<int> Schema(string key, ConfigurationSelection<int> selected)
    {
        ParsedConfigurationSetting<int> integer = Integer(key, selected);
        return integer.HasValue && integer.Value != 1
            ? ParsedConfigurationSetting<int>.Unparsed(key, selected, ConfigurationParseState.UnsupportedSchemaVersion)
            : integer;
    }

    private static ParsedConfigurationSetting<T> TypedEnum<T>(string key, ConfigurationSelection<T> selected) where T : struct, Enum =>
        selected.Source == ConfigurationSource.Absent
            ? ParsedConfigurationSetting<T>.Unparsed(key, selected, ConfigurationParseState.Absent)
            : Enum.IsDefined(selected.Value)
                ? ParsedConfigurationSetting<T>.Parsed(key, selected, selected.Value)
                : ParsedConfigurationSetting<T>.Unparsed(key, selected, ConfigurationParseState.InvalidToken);

    private static ParsedConfigurationSetting<T> Token<T>(string key, ConfigurationSelection<T> selected, FrozenDictionary<string, T> tokens) where T : struct, Enum =>
        selected.Source == ConfigurationSource.Environment
            ? tokens.TryGetValue(selected.Token!, out T value)
                ? ParsedConfigurationSetting<T>.Parsed(key, selected, value)
                : ParsedConfigurationSetting<T>.Unparsed(key, selected, ConfigurationParseState.InvalidToken)
            : TypedEnum(key, selected);

    private static ConfigurationParseState ParseLong(string token, out long value)
    {
        value = 0;
        ReadOnlySpan<char> digits = token.AsSpan();
        if (digits.Length > 0 && digits[0] is '+' or '-')
        {
            digits = digits[1..];
        }

        if (digits.Length == 0)
        {
            return ConfigurationParseState.InvalidToken;
        }

        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9')
            {
                return ConfigurationParseState.InvalidToken;
            }
        }

        return long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
            ? ConfigurationParseState.Parsed
            : ConfigurationParseState.OutOfRange;
    }
}
