using System.Text;
using CIS.Security;

namespace CIS.Messaging.RabbitMQ;

/// <summary>An exact application/environment namespace with an explicit bounded physical broker alias.</summary>
public sealed record RabbitMqEndpointNamespace
{
    /// <summary>Copies trusted configuration; names never originate in a request header.</summary>
    public RabbitMqEndpointNamespace(string application, string environment, string alias)
    {
        var target = new CapabilityTarget(application, environment);
        Application = target.Application;
        Environment = target.Environment;
        Alias = RabbitMqText.Name(alias, nameof(alias), 64);
    }

    /// <summary>Gets the actual application identity.</summary>
    public string Application { get; }
    /// <summary>Gets the actual environment identity.</summary>
    public string Environment { get; }
    /// <summary>Gets the explicitly selected ASCII namespace alias.</summary>
    public string Alias { get; }
}

/// <summary>Passive startup input. Credentials are application owned and must never be logged.</summary>
public sealed class RabbitMqOptions
{
    /// <summary>Gets or sets the finite logical broker alias.</summary>
    public string BrokerAlias { get; set; } = "RabbitMQ";
    /// <summary>Gets or sets the configured trusted broker host.</summary>
    public string HostName { get; set; } = "localhost";
    /// <summary>Gets or sets its TLS port.</summary>
    public int Port { get; set; } = 5671;
    /// <summary>Gets or sets the exact expected TLS certificate hostname.</summary>
    public string ServerName { get; set; } = "localhost";
    /// <summary>Gets or sets the application-owned virtual host.</summary>
    public string VirtualHost { get; set; } = "/";
    /// <summary>Gets or sets the distinct publishing principal.</summary>
    public string UserName { get; set; } = "";
    /// <summary>Gets or sets application-owned secret input, never diagnostic data.</summary>
    public string Password { get; set; } = "";
    /// <summary>Gets or sets an optional bounded public PEM root; null selects ordinary system trust.</summary>
    public string? TrustedRootCertificatePem { get; set; }
    /// <summary>Gets or sets the Native/Inbound profile using the private broker evidence projector.</summary>
    public string SecurityProfile { get; set; } = "MessagingBroker";
    /// <summary>Gets or sets the trusted broker-domain alias; capture qualifies it with the actual virtual host.</summary>
    public string SubjectIssuer { get; set; } = "rabbitmq.local";
    /// <summary>Gets or sets the finite prefix owned by this application topology.</summary>
    public string Namespace { get; set; } = "cis.messaging";
    /// <summary>Gets or sets the exact allowed recipient namespaces, at most sixteen.</summary>
    public IReadOnlyList<RabbitMqEndpointNamespace> EndpointNamespaces { get; set; } = [];
    /// <summary>Gets or sets the maximum accepted queue bytes, with reject-publish overflow.</summary>
    public long MaximumQueueBytes { get; set; } = 67_108_864;
    /// <summary>Gets or sets a finite queue record bound.</summary>
    public int MaximumQueueMessages { get; set; } = 10_000;
    /// <summary>Gets or sets verified deployment selection of quorum at-least-once dead lettering.</summary>
    /// <remarks>Explicit setup and actual deployment validation establish this prerequisite; passive existence checks do not prove it.</remarks>
    public bool SafeDeadLetteringVerified { get; set; }
    /// <summary>Gets or sets explicit startup equivalence checks using narrow configure rights on owned resources.</summary>
    public bool ValidateTopologyEquivalence { get; set; } = true;
    /// <summary>Copies finite setup without opening a connection or reading files.</summary>
    public RabbitMqSettings Capture() => new(this);
}

/// <summary>One immutable restart-required provider snapshot.</summary>
public sealed class RabbitMqSettings
{
    internal RabbitMqSettings(RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        BrokerAlias = RabbitMqText.Name(options.BrokerAlias, nameof(options.BrokerAlias), 64);
        HostName = RabbitMqText.Text(options.HostName, nameof(options.HostName), 255);
        ServerName = RabbitMqText.Text(options.ServerName, nameof(options.ServerName), 255);
        if (options.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Port));
        }
        Port = options.Port;
        VirtualHost = RabbitMqText.Text(options.VirtualHost, nameof(options.VirtualHost), 255);
        UserName = RabbitMqText.Text(options.UserName, nameof(options.UserName), 255);
        if (string.IsNullOrEmpty(options.Password) || options.Password.Length > 4096)
        {
            throw new ArgumentException("Provide a bounded application-owned broker credential.", nameof(options.Password));
        }
        Password = options.Password;
        SecurityProfile = RabbitMqText.Name(options.SecurityProfile, nameof(options.SecurityProfile), 64);
        SubjectIssuer = RabbitMqText.Name(options.SubjectIssuer, nameof(options.SubjectIssuer), 64)
            + "/vhost/" + Uri.EscapeDataString(VirtualHost);
        Namespace = RabbitMqText.Name(options.Namespace, nameof(options.Namespace), 64);
        if (options.TrustedRootCertificatePem is { } pem && (pem.Length is < 1 or > 16_384
            || !pem.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal) || pem.Contains("PRIVATE KEY", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Supply one bounded public PEM root.", nameof(options.TrustedRootCertificatePem));
        }
        TrustedRootCertificatePem = options.TrustedRootCertificatePem;
        if (options.MaximumQueueBytes is < 1 or > 268_435_456 || options.MaximumQueueMessages is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaximumQueueBytes), "Select finite queue limits.");
        }
        MaximumQueueBytes = options.MaximumQueueBytes;
        MaximumQueueMessages = options.MaximumQueueMessages;
        ArgumentNullException.ThrowIfNull(options.EndpointNamespaces);
        if (options.EndpointNamespaces.Count is < 1 or > 16)
        {
            throw new ArgumentException("Register one to sixteen exact endpoint namespaces.", nameof(options.EndpointNamespaces));
        }
        var namespaces = options.EndpointNamespaces.ToArray();
        if (namespaces.Length is < 1 or > 16 || namespaces.Any(item => item is null)
            || namespaces.Select(item => (item.Application, item.Environment)).Distinct().Count() != namespaces.Length
            || namespaces.Select(item => item.Alias).Distinct(StringComparer.Ordinal).Count() != namespaces.Length)
        {
            throw new ArgumentException("Register one to sixteen distinct exact endpoint namespaces.", nameof(options.EndpointNamespaces));
        }
        EndpointNamespaces = Array.AsReadOnly(namespaces);
        SafeDeadLetteringVerified = options.SafeDeadLetteringVerified;
        ValidateTopologyEquivalence = options.ValidateTopologyEquivalence;
    }

    /// <summary>Gets the finite broker alias.</summary>
    public string BrokerAlias { get; }
    /// <summary>Gets the configured trusted host.</summary>
    public string HostName { get; }
    /// <summary>Gets its TLS port.</summary>
    public int Port { get; }
    /// <summary>Gets the expected TLS hostname.</summary>
    public string ServerName { get; }
    /// <summary>Gets the exact virtual host.</summary>
    public string VirtualHost { get; }
    /// <summary>Gets the configured principal name.</summary>
    public string UserName { get; }
    /// <summary>Gets the selected Native/Inbound profile.</summary>
    public string SecurityProfile { get; }
    /// <summary>Gets the configured broker/vhost-qualified issuer.</summary>
    public string SubjectIssuer { get; }
    /// <summary>Gets the bounded physical resource prefix.</summary>
    public string Namespace { get; }
    /// <summary>Gets the immutable recipient namespace allowlist.</summary>
    public IReadOnlyList<RabbitMqEndpointNamespace> EndpointNamespaces { get; }
    /// <summary>Gets the queue byte bound.</summary>
    public long MaximumQueueBytes { get; }
    /// <summary>Gets the queue record bound.</summary>
    public int MaximumQueueMessages { get; }
    /// <summary>Gets verified safe native quarantine selection.</summary>
    public bool SafeDeadLetteringVerified { get; }
    /// <summary>Gets explicit startup equivalence selection.</summary>
    public bool ValidateTopologyEquivalence { get; }
    internal string Password { get; }
    internal string? TrustedRootCertificatePem { get; }

    internal RabbitMqEndpointNamespace GetNamespace(string application, string environment)
        => EndpointNamespaces.SingleOrDefault(item => item.Application == application && item.Environment == environment)
            ?? throw new ArgumentException("The reply endpoint namespace is not permitted.");
}

internal static class RabbitMqText
{
    internal static string Text(string value, string name, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumBytes || value.Any(char.IsControl)
            || new UTF8Encoding(false, true).GetByteCount(value) > maximumBytes)
        {
            throw new ArgumentException("Provide bounded valid text without controls.", name);
        }
        return value;
    }

    internal static string Name(string value, string name, int maximum)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length is < 1 || value.Length > maximum
            || value.Any(character => character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z')
                and not (>= '0' and <= '9') and not '.' and not '_' and not '-'))
        {
            throw new ArgumentException("Use a bounded ASCII resource alias.", name);
        }
        return value;
    }
}
