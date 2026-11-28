using CIS.Hosting;
using CIS.Management.Service.Configuration;
using CIS.Operations.Hosting;

namespace CIS.Management.Service;

public static partial class ManagementServiceApplication
{
    static partial void ConfigureConfiguration(CISApplicationBuilder builder, ManagementServiceOptions options)
    {
        builder.Services.AddScoped<ManagementConfigurationTarget>();
        builder.Services.AddScoped<ManagementConfigurationService>();
        if (options.Configuration is not null)
        {
            builder.Services.AddCisOperation(ManagementConfigurationOperations.Catalog());
            builder.Services.AddCisOperation(ManagementConfigurationOperations.Publication());
            builder.Services.AddCisOperation(ManagementConfigurationOperations.Release());
            builder.Services.AddCisOperation(ManagementConfigurationOperations.Assignment());
            builder.Services.AddCisOperation(ManagementConfigurationOperations.Creation());
            if (options.Configuration.Manifest.RuleEngine is { Count: > 0 })
            {
                builder.Services.AddScoped<RuleEngineOperationTarget>();
                builder.Services.AddScoped<ManagementRuleEngineService>();
                builder.Services.AddCisOperation(ManagementRuleEngineOperations.Definition());
                builder.Services.AddCisOperation(ManagementRuleEngineOperations.DeploymentDefinition());
                builder.Services.AddCisOperation(ManagementRuleEngineOperations.CounterResetDefinition());
            }
        }
    }
}
