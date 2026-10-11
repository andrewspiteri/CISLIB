namespace CIS.Configuration;

internal static class ApplicationVersionSelector
{
    public static ApplicationVersionSelection Select(
        ApplicationIdentity identity,
        string? serviceVersion = null,
        IEnvironmentVariableSource? variables = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        variables ??= ProcessEnvironmentVariableSource.Instance;
        ConfigurationSelection<string> selected = ConfigurationValueSelector.Select(
            variables,
            "CORE_SERVICE_VERSION",
            new ConfigurationCandidate<string>(serviceVersion),
            new ConfigurationCandidate<string>(identity.BuildVersion));
        string version = selected.Source == ConfigurationSource.Environment ? selected.Token! : selected.Value;
        return new(identity.BuildVersion, version, selected.SourceKey);
    }
}
