using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CIS.Messaging;
using CIS.Messaging.RabbitMQ;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace CIS.Tests.IntegrationTests.Messaging;

// Uses only the explicitly provisioned newly owned broker/vhost; no localhost/default-credential fallback.
internal sealed class RabbitMqMessageFixture : IAsyncDisposable
{
    private readonly ConnectionFile information;
    private readonly string rootPem;
    private readonly ServiceProvider provider;
    private readonly SharedTopology ownership;
    private readonly Func<MessagingSnapshot, MessagingSnapshot>? snapshotTransform;
    private readonly Action<IServiceCollection>? serviceSetup;
    private readonly Action<SecurityOptions>? securitySetup;
    private int disposed;

    private RabbitMqMessageFixture(ConnectionFile information, string rootPem, bool withSubscription, int maximumQueueMessages,
        bool safeQuarantine, bool withRequests, bool wrongHost, string? topologyNamespace = null, bool allowNoSubscribers = false,
        SharedTopology? ownership = null, Func<MessagingSnapshot, MessagingSnapshot>? transformSnapshot = null,
        Action<IServiceCollection>? configureServices = null, Action<SecurityOptions>? configureSecurity = null)
    {
        this.information = information;
        this.rootPem = rootPem;
        this.ownership = ownership ?? new();
        snapshotTransform = transformSnapshot;
        serviceSetup = configureServices;
        securitySetup = configureSecurity;
        Local = LocalApplicationIdentity.Capture();
        Options = CreateOptions("service");
        Options.Namespace = topologyNamespace ?? information.Namespace + "." + Guid.NewGuid().ToString("N")[..8];
        Options.MaximumQueueMessages = maximumQueueMessages;
        Options.SafeDeadLetteringVerified = safeQuarantine;
        if (wrongHost)
        {
            Options.ServerName = "untrusted.invalid";
        }
        var codec = new FixtureCodec();
        MessageRouteDefinition[] routes =
        [
            new MessageRouteDefinition<int>("events", MessageKind.Event, codec, allowNoSubscribers),
            new MessageRouteDefinition<int>("commands", MessageKind.Command, codec)
        ];
        MessageSubscriptionDefinition[] subscriptions = withSubscription
            ? [new MessageSubscriptionDefinition<int>("A", "Ledger", "events", codec, _ => new IgnoreHandler(),
                (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(Local.Application, Local.Environment)), ["fixture.handle"])]
            : [];
        MessageRequestDefinition[] requests = withRequests
            ? [new MessageRequestDefinition<int, int>("Query", "commands", codec, codec,
                MessageRequestMode.Query, Local.Application, Local.Environment)]
            : [];
        var initial = new MessagingSnapshot(new MessagingOptions(), routes, [new("Messaging", "Ledger")], subscriptions, requests);
        Snapshot = transformSnapshot is null ? initial : transformSnapshot(initial)
            ?? throw new InvalidOperationException("The explicit fixture snapshot transform returned null.");
        Snapshot.Validate();
        RabbitMqSettings selected = Options.Capture();
        var security = new SecurityOptions { PolicyVersion = "rm04-native-fixture" };
        security.Profiles.Add(new(selected.SecurityProfile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            authenticationScheme: "cis-rabbitmq", identityMappings:
            [
                new(new(selected.SubjectIssuer, information.Principals["service"].User), Local.Application, Local.Environment),
                new(new(selected.SubjectIssuer, information.Principals["other"].User), Local.Application, Local.Environment)
            ]));
        security.Capabilities.Add("fixture.handle");
        security.Grants.Add(new(selected.SecurityProfile, "fixture.handle", Local.Application, Local.Environment,
            subject: new(selected.SubjectIssuer, information.Principals["service"].User)));
        configureSecurity?.Invoke(security);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Snapshot);
        services.AddCisSecurity(security);
        services.AddCisRabbitMqMessaging(Options);
        configureServices?.Invoke(services);
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Transport = provider.GetRequiredService<RabbitMqTransport>();
        if (ownership is not null)
        {
            Interlocked.Increment(ref ownership.Owners);
        }
    }

    internal RabbitMqOptions Options
    {
        get;
    }
    internal MessagingSnapshot Snapshot
    {
        get;
    }
    internal LocalApplicationIdentity Local
    {
        get;
    }
    internal RabbitMqTransport Transport
    {
        get;
    }
    internal IServiceProvider Services => provider;
    internal string ServiceUser => information.Principals["service"].User;
    internal string OtherUser => information.Principals["other"].User;
    internal string EventExchange => RabbitMqTopology.GetRouteExchange(Options.Capture(), "events");
    internal string Queue => RabbitMqTopology.GetSubscriptionQueue(Options.Capture(), Local.Application, Local.Environment, "A");
    internal string QuarantineQueue => Options.Namespace + ".quarantine.local.A";
    internal RabbitMqOptions SetupOptions
    {
        get
        {
            RabbitMqOptions setup = CreateOptions("setup");
            setup.Namespace = Options.Namespace;
            setup.MaximumQueueMessages = Options.MaximumQueueMessages;
            setup.SafeDeadLetteringVerified = Options.SafeDeadLetteringVerified;
            return setup;
        }
    }

    internal RabbitMqMessageFixture CreateSibling(bool withRequests = true,
        Func<MessagingSnapshot, MessagingSnapshot>? transformSnapshot = null,
        Action<IServiceCollection>? configureServices = null, Action<SecurityOptions>? configureSecurity = null)
        => new(information, rootPem, Snapshot.Subscriptions.Count != 0, Options.MaximumQueueMessages,
            Options.SafeDeadLetteringVerified, withRequests, false, Options.Namespace, Snapshot.GetRoute("events").AllowNoSubscribers,
            ownership, transformSnapshot ?? snapshotTransform, configureServices ?? serviceSetup, configureSecurity ?? securitySetup);

    internal static async Task<RabbitMqMessageFixture> CreateAsync(bool withSubscription = true, int maximumQueueMessages = 10_000,
        bool safeQuarantine = true, bool withRequests = false, bool wrongHost = false, bool installTopology = true,
        bool allowNoSubscribers = false, Func<MessagingSnapshot, MessagingSnapshot>? transformSnapshot = null,
        Action<IServiceCollection>? configureServices = null, Action<SecurityOptions>? configureSecurity = null)
    {
        string? path = Environment.GetEnvironmentVariable("CIS_RM04_RABBITMQ_CONNECTION_FILE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Assert.Inconclusive("Select the private CIS_RM04_RABBITMQ_CONNECTION_FILE for the explicitly owned isolated fixture.");
        }
        if (!Path.IsPathFullyQualified(path!))
        {
            throw new InvalidDataException("The explicitly selected broker connection-file path must be absolute.");
        }
        var file = new FileInfo(path!);
        if (file.Length is < 1 or > 32_768)
        {
            throw new InvalidDataException("The private fixture descriptor exceeds its bound.");
        }
        ConnectionFile information = JsonSerializer.Deserialize<ConnectionFile>(await File.ReadAllBytesAsync(path!),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Missing private fixture descriptor.");
        string certificatePath = Path.GetFullPath(Path.Combine(file.DirectoryName!, information.RootCertificateFile));
        string ownedDirectory = Path.GetFullPath(file.DirectoryName!) + Path.DirectorySeparatorChar;
        if (!certificatePath.StartsWith(ownedDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || new FileInfo(certificatePath).Length is < 1 or > 16_384 || information.Namespace.Length > 55
            || information.Port is < 1 or > 65535 || information.Host != "127.0.0.1"
            || information.ServerName != "localhost" || information.VirtualHost == "/")
        {
            throw new InvalidDataException("The private descriptor does not select the bounded owned loopback fixture.");
        }
        foreach (string role in new[] { "service", "other", "rogue", "setup" })
        {
            if (!information.Principals.TryGetValue(role, out Principal? principal)
                || string.IsNullOrWhiteSpace(principal.User) || string.IsNullOrWhiteSpace(principal.Password))
            {
                throw new InvalidDataException("A fresh fixture principal is missing.");
            }
        }
        var fixture = new RabbitMqMessageFixture(information, await File.ReadAllTextAsync(certificatePath),
            withSubscription, maximumQueueMessages, safeQuarantine, withRequests, wrongHost, allowNoSubscribers: allowNoSubscribers,
            transformSnapshot: transformSnapshot, configureServices: configureServices, configureSecurity: configureSecurity);
        try
        {
            if (installTopology)
            {
                await RabbitMqTopology.InstallAsync(fixture.SetupOptions, fixture.Snapshot);
            }
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private RabbitMqOptions CreateOptions(string role)
    {
        Principal principal = information.Principals[role];
        return new()
        {
            HostName = information.Host,
            Port = information.Port,
            ServerName = information.ServerName,
            VirtualHost = information.VirtualHost,
            UserName = principal.User,
            Password = principal.Password,
            Namespace = information.Namespace,
            SubjectIssuer = "rm04-fixture",
            TrustedRootCertificatePem = rootPem,
            EndpointNamespaces = [new(Local.Application, Local.Environment, "local")]
        };
    }

    internal async Task<IConnection> ConnectAsync(string role = "setup")
    {
        Principal principal = information.Principals[role];
        var factory = new ConnectionFactory
        {
            HostName = information.Host,
            Port = information.Port,
            VirtualHost = information.VirtualHost,
            UserName = principal.User,
            Password = principal.Password,
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            ContinuationTimeout = TimeSpan.FromSeconds(5),
            MaxInboundMessageBodySize = 1_064_960,
            Ssl = new SslOption
            {
                Enabled = true,
                ServerName = information.ServerName,
                CertificateValidationCallback = (_, certificate, _, errors) =>
                {
                    if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != 0)
                    {
                        return false;
                    }
                    using X509Certificate2 root = X509Certificate2.CreateFromPem(rootPem);
                    using X509Certificate2 leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(root);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.DisableCertificateDownloads = true;
                    chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.1"));
                    return chain.Build(leaf);
                }
            }
        };
        return await factory.CreateConnectionAsync();
    }

    internal EncodedMessage Message(int value = 1, MessageKind kind = MessageKind.Event, string? claimedProducer = null)
        => MessageWire.Encode(new(Guid.NewGuid(), kind, new("fixture.number", 1),
            claimedProducer ?? Local.Application, Local.Environment, DateTimeOffset.UtcNow, JsonSerializer.SerializeToUtf8Bytes(value)));

    internal MessagePublication Publication(EncodedMessage? message = null, string route = "events")
        => new(message ?? Message(), new(route), MessageRecordPurpose.Application, 1, Guid.NewGuid());

    internal async Task PublishRawAsync(ReadOnlyMemory<byte> body, string role = "other", string? userId = null,
        bool includeUserId = true, string? exchange = null, string routingKey = "")
    {
        await using IConnection connection = await ConnectAsync(role);
        await using IChannel channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        await channel.BasicPublishAsync(exchange ?? EventExchange, routingKey, true, new BasicProperties
        {
            Persistent = true,
            UserId = includeUserId ? userId ?? information.Principals[role].User : null
        }, body);
    }

    internal async Task RestartOwnedBrokerAsync()
    {
        if (information.ContainerId.Length != 64 || information.ContainerId.Any(character => !Uri.IsHexDigit(character))
            || string.IsNullOrWhiteSpace(information.Owner))
        {
            throw new InvalidDataException("An exact newly owned broker identity is required before restart.");
        }
        ProcessResult inspection = await DockerAsync(["inspect", "--format", "{{json .Config.Labels}}", information.ContainerId]);
        Dictionary<string, string>? labels = JsonSerializer.Deserialize<Dictionary<string, string>>(inspection.StandardOutput);
        if (labels is null || !labels.TryGetValue("cislib.change", out string? change) || change != "CIS-0019"
            || !labels.TryGetValue("cislib.owner", out string? owner) || owner != information.Owner)
        {
            throw new InvalidDataException("The selected container is not the recorded fresh fixture owner.");
        }
        await DockerAsync(["restart", "--time", "5", information.ContainerId]);
        await UntilAsync(async () =>
        {
            try
            {
                await using IConnection connection = await ConnectAsync();
                return connection.IsOpen;
            }
            catch (BrokerUnreachableException)
            {
                return false;
            }
        }, TimeSpan.FromSeconds(60));
    }

    internal async Task<long> TotalQueuedAsync(string queue)
    {
        ProcessResult report = await DockerAsync(["exec", information.ContainerId, "rabbitmqctl", "list_queues",
            "-p", information.VirtualHost, "name", "messages", "--formatter", "json"]);
        using JsonDocument document = JsonDocument.Parse(report.StandardOutput);
        return document.RootElement.EnumerateArray().Single(item => item.GetProperty("name").GetString() == queue)
            .GetProperty("messages").GetInt64();
    }

    private async Task<ProcessResult> DockerAsync(string[] arguments)
    {
        string executable = information.DockerExecutableWindowsPath;
        if (!OperatingSystem.IsWindows() && executable.Length > 3 && executable[1] == ':')
        {
            executable = "/mnt/" + char.ToLowerInvariant(executable[0]) + executable[2..].Replace('\\', '/');
        }
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)
            || string.IsNullOrWhiteSpace(information.DockerConfigWindowsPath))
        {
            throw new InvalidDataException("The explicitly verified isolated Docker client is unavailable.");
        }
        var environment = new Dictionary<string, string?>();
        foreach (string name in new[] { "DOCKER_AUTH_CONFIG", "DOCKER_CONTEXT", "DOCKER_HOST", "DOCKER_CONFIG",
            "DOCKER_TLS", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "DOCKER_API_VERSION", "DOCKER_CUSTOM_HEADERS",
            "DOCKER_CONTENT_TRUST", "DOCKER_CONTENT_TRUST_SERVER", "DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE",
            "DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE" })
        {
            environment.Add(name, null);
        }
        ProcessResult result = await ProcessRunner.RunAsync(executable,
            ["--config", information.DockerConfigWindowsPath, "--host", "npipe:////./pipe/dockerDesktopLinuxEngine", .. arguments],
            AppContext.BaseDirectory, TimeSpan.FromSeconds(90), environment, maximumCapturedBytesPerStream: 16_384);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("The bounded owned-fixture Docker operation failed.");
        }
        return result;
    }

    internal static async Task UntilAsync(Func<Task<bool>> predicate, TimeSpan? timeout = null)
    {
        long started = TimeProvider.System.GetTimestamp();
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(20);
        while (!await predicate())
        {
            if (TimeProvider.System.GetElapsedTime(started) >= limit)
            {
                Assert.Fail("The explicit fixture observation did not settle within its bounded deadline.");
            }
            await Task.Delay(200);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        var failures = new List<Exception>();
        try
        {
            await provider.DisposeAsync();
        }
        catch (Exception error)
        {
            failures.Add(error);
            lock (ownership)
            {
                ownership.DrainFailed = true;
            }
        }
        if (Transport.ReplyEndpoint is { } endpoint)
        {
            lock (ownership)
            {
                ownership.ReplyQueues.Add(RabbitMqTopology.GetReplyQueue(Options.Capture(), endpoint));
            }
        }
        if (Interlocked.Decrement(ref ownership.Owners) == 0)
        {
            try
            {
                if (!ownership.DrainFailed)
                {
                    await DeleteNewOwnedTopologyAsync();
                }
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        if (failures.Count != 0)
        {
            throw new AggregateException("The owned provider fixture failed after actual drain/cleanup.", failures);
        }
    }

    private async Task DeleteNewOwnedTopologyAsync()
    {
        string prefix = Options.Namespace;
        string expected = information.Namespace + ".";
        if (!prefix.StartsWith(expected, StringComparison.Ordinal) || prefix.Length != expected.Length + 8
            || prefix[expected.Length..].Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Only the exact newly generated per-case namespace may be removed.");
        }
        var queues = new List<string> { prefix + ".quarantine.local.replies" };
        var exchanges = new List<string> { prefix + ".reply.local", prefix + ".native-quarantine.local.replies" };
        foreach (MessageRouteDefinition route in Snapshot.Routes)
        {
            exchanges.Add(RabbitMqTopology.GetRouteExchange(Options.Capture(), route.Alias));
        }
        foreach (MessageSubscriptionDefinition subscription in Snapshot.Subscriptions)
        {
            queues.Add(RabbitMqTopology.GetSubscriptionQueue(Options.Capture(), Local.Application, Local.Environment, subscription.Alias));
            queues.Add(prefix + ".quarantine.local." + subscription.Alias);
            exchanges.Add(prefix + ".retry.local." + subscription.Alias);
            exchanges.Add(prefix + ".terminal.local." + subscription.Alias);
            exchanges.Add(prefix + ".native-quarantine.local." + subscription.Alias);
        }
        lock (ownership)
        {
            queues.AddRange(ownership.ReplyQueues);
        }
        await using IConnection cleanup = await ConnectAsync();
        foreach (string queue in queues.Distinct(StringComparer.Ordinal))
        {
            await using IChannel channel = await cleanup.CreateChannelAsync();
            try
            {
                QueueDeclareOk existing = await channel.QueueDeclarePassiveAsync(queue);
                if (existing.ConsumerCount != 0)
                {
                    throw new InvalidOperationException("An exact owned fixture queue still has a consumer after every owner drained.");
                }
                // Quorum queues reject both conditional delete flags. The fresh per-case namespace,
                // completed owner drain and passive check bound this fixture-only unconditional delete.
                await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false);
            }
            catch (OperationInterruptedException error) when (error.ShutdownReason?.ReplyCode == 404)
            {
                // Partial failed setup or normal endpoint expiry can leave a known owned name absent.
            }
        }
        foreach (string exchange in exchanges.Distinct(StringComparer.Ordinal))
        {
            await using IChannel channel = await cleanup.CreateChannelAsync();
            try
            {
                await channel.ExchangeDeleteAsync(exchange, true);
            }
            catch (OperationInterruptedException error) when (error.ShutdownReason?.ReplyCode == 404)
            {
                // The exact known owned resource was never installed or has already expired.
            }
        }
    }

    private sealed class SharedTopology
    {
        internal int Owners = 1;
        internal bool DrainFailed;
        internal List<string> ReplyQueues { get; } = [];
    }

    private sealed class FixtureCodec : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new("fixture.number", 1);
        public byte[] Encode(int value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            value = JsonSerializer.Deserialize<int>(json.Span);
            return contract == Contract;
        }
    }

    private sealed class IgnoreHandler : IMessageHandler<int>
    {
        public Task HandleAsync(MessageHandlerContext context, int value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ConnectionFile
    {
        public ConnectionFile()
        {
        }
        public string Host { get; set; } = "";
        public int Port
        {
            get; set;
        }
        public string ServerName { get; set; } = "";
        public string VirtualHost { get; set; } = "";
        public string RootCertificateFile { get; set; } = "";
        public string Namespace { get; set; } = "";
        public Dictionary<string, Principal> Principals { get; set; } = [];
        public string ContainerId { get; set; } = "";
        public string Owner { get; set; } = "";
        public string DockerExecutableWindowsPath { get; set; } = "";
        public string DockerConfigWindowsPath { get; set; } = "";
    }

    private sealed class Principal
    {
        public Principal()
        {
        }
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
