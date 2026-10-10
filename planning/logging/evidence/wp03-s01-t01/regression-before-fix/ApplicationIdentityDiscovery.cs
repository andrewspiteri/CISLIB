using System.Reflection;

namespace CIS.Configuration;

internal static class ApplicationIdentityDiscovery
{
    public static ApplicationIdentity Discover(
        ApplicationIdentityInput input,
        IApplicationAssemblyMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        metadata ??= RuntimeApplicationAssemblyMetadata.Instance;
        string? applicationName = Nonblank(input.ApplicationName);
        string? buildVersion = Nonblank(input.BuildVersion);

        if (applicationName is null || buildVersion is null)
        {
            Assembly? assembly = input.ApplicationAssembly ?? OptionalMetadataDiscovery.Read(metadata.GetEntryAssembly);
            if (assembly is not null && assembly != typeof(ApplicationIdentityDiscovery).Assembly)
            {
                applicationName ??= Nonblank(OptionalMetadataDiscovery.Read(() => metadata.GetName(assembly)));
                buildVersion ??= Nonblank(OptionalMetadataDiscovery.Read(() => metadata.GetInformationalVersion(assembly)))
                    ?? Nonblank(OptionalMetadataDiscovery.Read(() => metadata.GetVersion(assembly)));
            }
        }

        return new ApplicationIdentity(
            applicationName,
            buildVersion ?? "unknown",
            Nonblank(input.CommitId),
            Nonblank(input.BuildId));
    }

    private static string? Nonblank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
