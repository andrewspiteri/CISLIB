using CIS.Messaging.RabbitMQ;
using CIS.Messaging;
using CIS.Audit;

namespace CIS.Management.Service.Metadata;

/// <summary>Explicit immutable selection of the existing durable Audit delivery provider.</summary>
public sealed class ManagementDeliveryOptions
{
    private readonly string subjectIssuer;
    private readonly string password;
    private readonly string? trustedRootCertificatePem;

    public const string AuditRoute = "management-audit";
    public const string BrokerScheme = "management-broker";

    public ManagementDeliveryOptions(RabbitMqOptions broker, bool requireLiveBrokerForReadiness = false,
        long maximumRetainedEnvelopeBytes = 64L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(broker);
        if (maximumRetainedEnvelopeBytes is < 1 or > 1024L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedEnvelopeBytes));
        }
        Broker = broker.Capture();
        subjectIssuer = broker.SubjectIssuer;
        password = broker.Password;
        trustedRootCertificatePem = broker.TrustedRootCertificatePem;
        if (Broker.SecurityProfile != BrokerScheme || !Broker.EndpointNamespaces.Any(item =>
            item.Application == EnvironmentOptions.ApplicationName && item.Environment == EnvironmentOptions.EnvironmentName))
        {
            throw new ArgumentException("The selected management broker profile must include the actual service namespace.", nameof(broker));
        }
        RequireLiveBrokerForReadiness = requireLiveBrokerForReadiness;
        MaximumRetainedEnvelopeBytes = maximumRetainedEnvelopeBytes;
    }

    public RabbitMqSettings Broker
    {
        get;
    }
    public bool RequireLiveBrokerForReadiness
    {
        get;
    }
    public long MaximumRetainedEnvelopeBytes
    {
        get;
    }

    public AuditOptions CreateAuditOptions() => new(AuditRoute);

    public MessagingOptions CreateMessagingOptions() => new()
    {
        StoreAlias = "management",
        RequireLiveBrokerForReadiness = RequireLiveBrokerForReadiness
    };

    /// <summary>The same finite producer graph used by explicit topology provisioning and ordinary runtime validation.</summary>
    public MessagingSnapshot CreateMessagingSnapshot() => new(CreateMessagingOptions(),
        [new MessageRouteDefinition<AuditRecord>(AuditRoute, MessageKind.Event, new AuditRecordCodec(CreateAuditOptions().Limits))],
        [new MessageStoreDefinition("management", "management")]);

    /// <summary>Explicit deployment action; callers deliberately select setup credentials, never runtime auto-provisioning.</summary>
    public Task InstallTopologyAsync(CancellationToken cancellationToken = default)
        => RabbitMqTopology.InstallAsync(CreateBrokerOptions(), CreateMessagingSnapshot(), cancellationToken);

    /// <summary>Returns detached provider inputs; capture never opens a socket or reads a credential file.</summary>
    public RabbitMqOptions CreateBrokerOptions() => new()
    {
        BrokerAlias = Broker.BrokerAlias,
        HostName = Broker.HostName,
        Port = Broker.Port,
        ServerName = Broker.ServerName,
        VirtualHost = Broker.VirtualHost,
        UserName = Broker.UserName,
        Password = password,
        TrustedRootCertificatePem = trustedRootCertificatePem,
        SecurityProfile = Broker.SecurityProfile,
        SubjectIssuer = subjectIssuer,
        Namespace = Broker.Namespace,
        EndpointNamespaces = Broker.EndpointNamespaces.ToArray(),
        MaximumQueueBytes = Broker.MaximumQueueBytes,
        MaximumQueueMessages = Broker.MaximumQueueMessages,
        SafeDeadLetteringVerified = Broker.SafeDeadLetteringVerified,
        ValidateTopologyEquivalence = Broker.ValidateTopologyEquivalence
    };

    /// <summary>Reads trusted bootstrap values, using an environment reference for the broker secret.</summary>
    public static ManagementDeliveryOptions FromConfiguration(IConfiguration configuration, bool setupCredentials = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection broker = configuration.GetSection("Broker");
        string passwordVariable = setupCredentials
            ? broker["SetupPasswordEnvironment"] ?? "CIS_MANAGEMENT_RABBIT_SETUP_PASSWORD"
            : broker["PasswordEnvironment"] ?? "CIS_MANAGEMENT_RABBIT_PASSWORD";
        if (passwordVariable.Length is < 1 or > 128
            || passwordVariable.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Select a bounded broker-password environment reference.");
        }
        string password = System.Environment.GetEnvironmentVariable(passwordVariable)
            ?? throw new ArgumentException("The selected management broker credential is absent.");
        var selected = new RabbitMqOptions
        {
            BrokerAlias = "ManagementAudit",
            HostName = Required(broker, "HostName"),
            Port = broker.GetValue("Port", 5671),
            ServerName = Required(broker, "ServerName"),
            VirtualHost = Required(broker, "VirtualHost"),
            UserName = Required(broker, setupCredentials ? "SetupUserName" : "UserName"),
            Password = password,
            TrustedRootCertificatePem = broker["TrustedRootCertificatePem"],
            SecurityProfile = BrokerScheme,
            SubjectIssuer = broker["SubjectIssuer"] ?? "management-broker",
            Namespace = Required(broker, "Namespace"),
            EndpointNamespaces = [new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                broker["ApplicationNamespaceAlias"] ?? "management")],
            MaximumQueueBytes = broker.GetValue("MaximumQueueBytes", 64L * 1024 * 1024),
            MaximumQueueMessages = broker.GetValue("MaximumQueueMessages", 10000),
            SafeDeadLetteringVerified = broker.GetValue("SafeDeadLetteringVerified", false),
            ValidateTopologyEquivalence = true
        };
        return new(selected, configuration.GetValue("RequireLiveBrokerForReadiness", false),
            configuration.GetValue("MaximumRetainedEnvelopeBytes", 64L * 1024 * 1024));
    }

    private static string Required(IConfiguration section, string key)
        => section[key] ?? throw new ArgumentException("A required management delivery bootstrap setting is absent: " + key);
}
