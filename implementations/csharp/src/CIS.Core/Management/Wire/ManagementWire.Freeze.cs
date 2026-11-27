namespace CIS.Management.Wire;

public static partial class ManagementWire
{
    private static object Freeze(object value) => value switch
    {
        ManagementServiceInfo service => service with
        {
            ProtocolMajors = Copy(service.ProtocolMajors),
            Features = Array.AsReadOnly(service.Features.Select(FreezeFeature).ToArray())
        },
        ManagementCapabilities capabilities => capabilities with
        {
            ImplementedApis = Array.AsReadOnly(capabilities.ImplementedApis.Select(FreezeFeature).ToArray()),
            CallerHints = Copy(capabilities.CallerHints)
        },
        ManagementInventorySnapshot inventory => FreezeInventory(inventory),
        ManagementInventoryEntry entry => FreezeEntry(entry),
        ManagementMetadataResult result => FreezeResult(result),
        ManagementProblem problem => problem with { Result = problem.Result is null ? null : FreezeResult(problem.Result) },
        ManagementPage<ManagementApplication> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementEnvironment> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementInstance> page => page with { Items = Copy(page.Items) },
        ManagementPage<ManagementInventoryEntry> page => page with
        {
            Items = Array.AsReadOnly(page.Items.Select(FreezeEntry).ToArray())
        },
        _ => value
    };

    private static ManagementFeature FreezeFeature(ManagementFeature value)
        => value with
        {
            RequiredCapabilities = Copy(value.RequiredCapabilities)
        };

    private static ManagementInventoryEntry FreezeEntry(ManagementInventoryEntry value)
        => value with
        {
            Inventory = FreezeInventory(value.Inventory)
        };

    private static ManagementInventorySnapshot FreezeInventory(ManagementInventorySnapshot value)
        => value with
        {
            Modules = Array.AsReadOnly(value.Modules.Select(module => module with { Details = module.Details?.Clone() }).ToArray()),
            Categories = Copy(value.Categories),
            Jobs = Array.AsReadOnly(value.Jobs.Select(job => job with
            {
                SupportedModes = Copy(job.SupportedModes),
                Observations = job.Observations is null ? null : Copy(job.Observations)
            }).ToArray())
        };

    private static ManagementMetadataResult FreezeResult(ManagementMetadataResult value)
        => value with
        {
            AttemptCompletion = value.AttemptCompletion is null ? null : value.AttemptCompletion with
            {
                CleanupCodes = value.AttemptCompletion.CleanupCodes is null ? null : Copy(value.AttemptCompletion.CleanupCodes)
            }
        };

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values) => Array.AsReadOnly(values.ToArray());
}
