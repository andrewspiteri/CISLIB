using System.Text.Encodings.Web;
using CIS.Audit;
using CIS.Hosting;
using CIS.Management.Service.Metadata;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Messaging.PostgreSql;
using CIS.Messaging.RabbitMQ;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Operations.PostgreSql;
using CIS.Security;
using CIS.Management.Service.Backends;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using CIS.Management.Service.Configuration;

namespace CIS.Management.Service;

public static partial class ManagementServiceApplication
{
    static partial void ConfigureMetadataSecurity(SecurityOptions policy, ManagementServiceOptions options)
    {
        if (options.Configuration is not null)
        {
            foreach (string capability in ManagementConfigurationCapabilities.All)
            {
                if (!policy.Capabilities.Contains(capability))
                {
                    policy.Capabilities.Add(capability);
                }
            }
        }
        if (options.Configuration?.Manifest.RuleEngine is { Count: > 0 })
        {
            foreach (string capability in new[]
            {
                ManagementRuleEngineOperations.Read, ManagementRuleEngineOperations.Save, ManagementRuleEngineOperations.ReadOperation,
                ManagementRuleEngineOperations.ReadDeployment, ManagementRuleEngineOperations.Deploy,
                ManagementRuleEngineOperations.ReadExecutions, ManagementRuleEngineOperations.ReadExecutionDetail,
                ManagementRuleEngineOperations.ReadCounters, ManagementRuleEngineOperations.ResetCounters
            })
            {
                if (!policy.Capabilities.Contains(capability))
                {
                    policy.Capabilities.Add(capability);
                }
            }
        }
        ManagementRuleEngineAuditIngress.ConfigureSecurity(policy, options.Delivery!);
    }

    static partial void ConfigureMetadata(CISApplicationBuilder builder, ManagementServiceOptions options)
    {
        ManagementDeliveryOptions delivery = options.Delivery!;
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BrokerHttpAuthentication>(
            ManagementDeliveryOptions.BrokerScheme, _ => { });
        builder.Services.AddCisRabbitMqMessaging(delivery.CreateBrokerOptions());
        builder.Services.AddSingleton<IMessagingStore>(new PostgreSqlMessagingStore(new()
        {
            Schema = options.MessagingSchema,
            MaximumRetainedEnvelopeBytes = delivery.MaximumRetainedEnvelopeBytes
        }));
        builder.Services.AddCisMessageStore(new("management", "management"));
        if (delivery.RuleEngineAuditSources.Count > 0)
        {
            builder.Services.AddCisMessageRoute(ManagementRuleEngineAuditIngress.Route());
            builder.Services.AddCisMessageSubscription(ManagementRuleEngineAuditIngress.Subscription(delivery));
        }
        builder.UseAudit(delivery.CreateAuditOptions());
        builder.UseMessaging(delivery.CreateMessagingOptions());
        builder.Services.AddSingleton<IOperationJournal>(root => new PostgreSqlManagementJournal(
            new PostgreSqlOperationJournal(new()
            {
                Schema = options.OperationsSchema
            }),
            root.GetRequiredService<PostgreSqlManagementAuthority>()));
        builder.Services.AddCisOperation(ManagementMetadataDefinition.Create());
        ConfigureConfiguration(builder, options);
        builder.Services.AddCisOperations();
        builder.Services.AddScoped<ManagementMetadataTarget>();
        builder.Services.AddScoped<ManagementMetadataService>();
    }

    // Native broker evidence is projected by the existing Rabbit provider. This scheme never authenticates HTTP callers.
    private sealed class BrokerHttpAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
