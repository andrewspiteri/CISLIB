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
        string? selected = Nonblank(variables.GetValue("CORE_SERVICE_VERSION"));
        if (selected is not null)
        {
            return new(identity.BuildVersion, selected, "CORE_SERVICE_VERSION");
        }

        selected = Nonblank(variables.GetValue("DD_VERSION"));
        if (selected is not null)
        {
            return new(identity.BuildVersion, selected, "DD_VERSION");
        }

        selected = Nonblank(serviceVersion);
        return selected is not null
            ? new(identity.BuildVersion, selected, "EnvironmentSetup.ServiceVersion")
            : new(identity.BuildVersion, identity.BuildVersion, "BuildVersion");
    }

    private static string? Nonblank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
