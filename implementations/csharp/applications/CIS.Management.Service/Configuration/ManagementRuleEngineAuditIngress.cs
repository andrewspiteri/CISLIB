using CIS.Management.Service.Metadata;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Messaging;
using CIS.Security;

namespace CIS.Management.Service.Configuration;

internal static class ManagementRuleEngineAuditIngress
{
    private const string SubscriptionAlias = "management-rule-engine-audit";
    private const string Receive = "management.rule-engine.audit.receive";
    private const string Retry = "management.rule-engine.audit.retry";
    private const int MaximumSources = 64;

    internal static IReadOnlyList<ManagementRuleEngineAuditSource> Capture(IEnumerable<ManagementRuleEngineAuditSource> sources,
        string consumerUserName)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ManagementRuleEngineAuditSource[] selected = sources.Take(MaximumSources + 1).ToArray();
        if (selected.Length > MaximumSources || selected.Any(source => source is null || !Valid(source, consumerUserName))
            || selected.Select(source => source.BrokerUserName).Distinct(StringComparer.Ordinal).Count() != selected.Length
            || selected.GroupBy(source => (source.ProducerApplication, source.ProducerEnvironment))
                .Any(group => group.Select(source => source.ManagedApplication).Distinct(StringComparer.Ordinal).Count() != 1))
        {
            throw new ArgumentException("Select bounded native Rule Engine producers and one managed scope per producer application and environment.", nameof(sources));
        }
        return Array.AsReadOnly(selected);
    }

    internal static MessageRouteDefinition Route()
        => new MessageRouteDefinition<RuleEngineAuditObservation>(ManagementDeliveryOptions.RuleEngineAuditRoute,
            MessageKind.Event, new RuleEngineAuditObservationCodec());

    internal static MessageSubscriptionDefinition Subscription(ManagementDeliveryOptions delivery)
        => new MessageSubscriptionDefinition<RuleEngineAuditObservation>(SubscriptionAlias, "management",
            ManagementDeliveryOptions.RuleEngineAuditRoute, new RuleEngineAuditObservationCodec(),
            services => new Handler(services.GetRequiredService<PostgreSqlManagementStore>(), delivery),
            (_, _, _, _) => ValueTask.FromResult(Target()), [Receive],
            new() { RetryCapabilities = [Retry] },
            (_, observation, _, original, _, _) => ValueTask.FromResult(RetryOriginalIsTrusted(delivery, observation, original)));

    internal static void ConfigureSecurity(SecurityOptions policy, ManagementDeliveryOptions delivery)
    {
        var broker = delivery.Broker;
        var localSubject = new SecuritySubject(broker.SubjectIssuer, broker.UserName);
        var mappings = new List<SecurityIdentityMapping>
        {
            new(localSubject, EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName)
        };
        foreach (ManagementRuleEngineAuditSource source in delivery.RuleEngineAuditSources)
        {
            if (source.BrokerUserName != broker.UserName)
            {
                mappings.Add(new(new(broker.SubjectIssuer, source.BrokerUserName), source.ProducerApplication, source.ProducerEnvironment));
            }
        }
        policy.Profiles.Add(new(broker.SecurityProfile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            ManagementDeliveryOptions.BrokerScheme, mappings));
        if (delivery.RuleEngineAuditSources.Count == 0)
        {
            return;
        }
        foreach (string capability in new[] { Receive, Retry })
        {
            if (!policy.Capabilities.Contains(capability))
            {
                policy.Capabilities.Add(capability);
            }
            // Native retained redelivery requires both ordinary inbound authority and separate retry authority.
            Grant(capability, localSubject);
        }
        foreach (ManagementRuleEngineAuditSource source in delivery.RuleEngineAuditSources)
        {
            if (source.BrokerUserName != broker.UserName)
            {
                Grant(Receive, new(broker.SubjectIssuer, source.BrokerUserName));
            }
        }

        void Grant(string capability, SecuritySubject subject)
        {
            CapabilityTarget target = Target();
            policy.Grants.Add(new(broker.SecurityProfile, capability, target.Application, target.Environment,
                subject: subject, resourceSelection: CapabilityResourceSelection.ExactResource,
                resourceType: target.ResourceType, resourceIdentifier: target.ResourceIdentifier));
        }
    }

    private static bool Valid(ManagementRuleEngineAuditSource source, string consumerUserName)
        => ManagementWire.IsUtf8Text(source.BrokerUserName, 255)
            && ManagementWire.IsScalarText(source.ProducerApplication, 255)
            && ManagementWire.IsScalarText(source.ProducerEnvironment, 255)
            && ManagementWire.IsScalarText(source.ManagedApplication, 255)
            && (source.BrokerUserName != consumerUserName || source.ProducerApplication == EnvironmentOptions.ApplicationName
                && source.ProducerEnvironment == EnvironmentOptions.EnvironmentName);

    private static CapabilityTarget Target()
        => new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName, "audit.ingress", "rule-engine-v1");

    private static bool RetryOriginalIsTrusted(ManagementDeliveryOptions delivery, RuleEngineAuditObservation observation, MessageProvenance original)
        => original.BrokerAlias == delivery.Broker.BrokerAlias && original.VirtualHost == delivery.Broker.VirtualHost
            && original.Subject.Issuer == delivery.Broker.SubjectIssuer && original.Subject.Identifier == original.UserId
            && original.Application == observation.Application && original.Environment == observation.Environment
            && delivery.RuleEngineAuditSources.Any(source => source.BrokerUserName == original.UserId
                && source.ProducerApplication == original.Application && source.ProducerEnvironment == original.Environment);

    private sealed class Handler(PostgreSqlManagementStore store, ManagementDeliveryOptions delivery)
        : IMessageHandler<RuleEngineAuditObservation>
    {
        public Task HandleAsync(MessageHandlerContext context, RuleEngineAuditObservation observation, CancellationToken cancellationToken = default)
        {
            // RM04 binds initial deliveries to native evidence and authenticates retained retries independently.
            bool initial = context.DeliveryOrdinal == 1 && context.Generation == 0;
            SecuritySubject? subject = context.Services.GetRequiredService<ICallerContextAccessor>().Current?.Subject;
            ManagementRuleEngineAuditSource? source = delivery.RuleEngineAuditSources.FirstOrDefault(candidate =>
                candidate.ProducerApplication == context.Envelope.ProducerApplication
                && candidate.ProducerEnvironment == context.Envelope.ProducerEnvironment
                && (!initial || subject is not null && subject.Issuer == delivery.Broker.SubjectIssuer
                    && subject.Identifier == candidate.BrokerUserName));
            if (source is null || observation.Record.Producer.Application != context.Envelope.ProducerApplication
                || observation.Record.Producer.Environment != context.Envelope.ProducerEnvironment)
            {
                throw new InvalidDataException("management.rule-engine.audit_producer_mismatch");
            }
            return store.AcceptRuleEngineExecutionAsync(context.Transaction, observation, source.ManagedApplication, cancellationToken);
        }
    }
}
