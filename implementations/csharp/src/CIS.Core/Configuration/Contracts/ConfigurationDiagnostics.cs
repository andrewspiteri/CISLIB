namespace CIS.Configuration;

internal static class ConfigurationDiagnostics
{
    internal static CoreConfigurationException Error(Type type, string code, string explanation)
        => new([new ConfigurationIssue(code, type.FullName ?? type.Name, explanation)]);
}
