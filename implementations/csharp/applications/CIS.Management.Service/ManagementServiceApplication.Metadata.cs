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
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CIS.Management.Service;

public static partial class ManagementServiceApplication
{
    static partial void ConfigureMetadataSecurity(SecurityOptions policy, ManagementServiceOptions options)
    {
        RabbitMqSettings broker = options.Delivery!.Broker;
        policy.Profiles.Add(new(broker.SecurityProfile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            ManagementDeliveryOptions.BrokerScheme,
            [new(new(broker.SubjectIssuer, broker.UserName), EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName)]));
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
        builder.UseAudit(delivery.CreateAuditOptions());
        builder.UseMessaging(delivery.CreateMessagingOptions());
        builder.Services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal(new()
        {
            Schema = options.OperationsSchema
        }));
        builder.Services.AddCisOperation(ManagementMetadataDefinition.Create());
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
