namespace CIS.Management.Service.Configuration;

internal sealed partial class ManagementConfigurationService
{
    internal Task<ManagementConfigurationResponse> CreateConfigurationAsync(ManagementApplication application, string key,
        string ifMatch, ManagementConfigurationCreationCommand command, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(ManagementConfigurationOperations.CreationName, "management.application", application.ApplicationId, key);
        return Execute(reference, new ManagementCreationInput(application, ifMatch, command), "creation", command.Reason,
            (context, owned, cancellation) => store.CreateConfigurationAsync(context, owned, cancellation), token);
    }

    internal Task<ManagementConfigurationResponse> LookupCreationAsync(ManagementApplication application, string key, CancellationToken token)
        => Lookup(Bind(ManagementConfigurationOperations.CreationName, "management.application", application.ApplicationId, key, lookup: true), token);
}
