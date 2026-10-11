namespace CIS.Configuration;

// Internal safe description, before the complete public diagnostic contract.
internal sealed class ConfigurationProblem(string code, string key, string explanation)
{
    public string Code { get; } = code;
    public string Key { get; } = key;
    public string Explanation { get; } = explanation;
}
