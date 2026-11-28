using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.Configuration;
using CIS.Hosting;
using CIS.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.RuleEngine;

/// <summary>Registers the Rule Engine with the existing centrally managed application.</summary>
public static class RuleEngineHostingExtensions
{
    /// <summary>Selects Rule Engine for a host with native Central Configuration and explicitly enrolled ruleset roots.</summary>
    public static CISApplicationBuilder AddCisRuleEngine(this CISApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.CentralConfiguration is null
            || builder.Configuration?.Enrollments.Any(item => typeof(RuleEngineConfiguration).IsAssignableFrom(item.ConfigurationType)) != true)
        {
            throw new InvalidOperationException("Select native Central Configuration and explicitly enroll Rule Engine roots before adding Rule Engine.");
        }
        builder.Services.TryAddSingleton(root => new RuleEngineRuntime(root.GetRequiredService<ConfigurationRuntime>(),
            root.GetRequiredService<IManagedWorkAdmission>()));
        builder.Services.TryAddScoped<IRuleEvaluator>(scope => new RuleEvaluator(
            scope.GetRequiredService<RuleEngineRuntime>().SelectAsync,
            scope.GetService<IAuditCapture>(), scope.GetService<IAuditWriter>(),
            scope.GetService<AuditHostingRegistration>()?.Options.Limits, scope.GetRequiredService<TimeProvider>()));
        return builder;
    }
}
