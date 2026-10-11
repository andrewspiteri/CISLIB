namespace CIS.Configuration;

/// <summary>Describes one configuration problem with a stable code and source key.</summary>
/// <remarks>Callers supplying their own issues must provide safe explanations without raw configuration values.</remarks>
public sealed class ConfigurationIssue
{
    /// <summary>Creates an immutable issue, preserving the supplied strings.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public ConfigurationIssue(string code, string key, string explanation)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(explanation);
        Code = code;
        Key = key;
        Explanation = explanation;
    }

    /// <summary>Gets the stable diagnostic code.</summary>
    public string Code { get; }
    /// <summary>Gets the recognized setting or metadata/setup field responsible for the issue.</summary>
    public string Key { get; }
    /// <summary>Gets guidance that should exclude raw input values and credentials.</summary>
    public string Explanation { get; }
}
