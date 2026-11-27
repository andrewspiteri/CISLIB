using CIS.Management.Service.Persistence;
using CIS.Security;

namespace CIS.Management.Service.Configuration;

// The optional application target is native provenance, never a claim, caller-supplied app or inferred ownership.
internal static class ManagementConfigurationAuthorization
{
    internal static async ValueTask<bool> AllowedAsync(ICapabilityAuthorizer authorizer, PostgreSqlManagementStore store,
        string capability, string resourceType, string resourceId, CancellationToken token)
        => await AllowedBundleAsync(authorizer, store, [capability], resourceType, resourceId, token);

    internal static async ValueTask<bool> AllowedBundleAsync(ICapabilityAuthorizer authorizer, PostgreSqlManagementStore store,
        IReadOnlyList<string> capabilities, string resourceType, string resourceId, CancellationToken token)
    {
        if ((await authorizer.AuthorizeAsync(capabilities, new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
            resourceType, resourceId), token)).IsAllowed)
        {
            return true;
        }
        if (capabilities.Count == 0 || capabilities.Any(capability => !Supports(resourceType, capability)))
        {
            return false;
        }
        ManagementStoreResult<string> proof = await store.ReadNativeGenericApplicationAsync(resourceType, resourceId, token);
        return proof.IsKnownCommitted && proof.Value is { } applicationId
            && (await authorizer.AuthorizeAsync(capabilities, new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                "management.application", applicationId), token)).IsAllowed;
    }

    internal static bool Supports(string type, string capability)
        => type == ManagementConfigurationCapabilities.SourceResource && capability is ManagementConfigurationCapabilities.DefinitionsRead
            or ManagementConfigurationCapabilities.ContentRead or ManagementConfigurationCapabilities.Validate
            or ManagementConfigurationCapabilities.Publish or ManagementConfigurationCapabilities.OperationsRead
            || type == ManagementConfigurationCapabilities.BindingResource && capability is ManagementConfigurationCapabilities.DefinitionsRead
                or ManagementConfigurationCapabilities.Preview or ManagementConfigurationCapabilities.ReleaseCreate
                or ManagementConfigurationCapabilities.AssignmentWrite or ManagementConfigurationCapabilities.OperationsRead
            || type == ManagementConfigurationCapabilities.ReleaseResource && capability == ManagementConfigurationCapabilities.DefinitionsRead;
}
