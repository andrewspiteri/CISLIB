using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CIS.Messaging;
using CIS.Messaging.RabbitMQ;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace CIS.Tests.IntegrationTests.Messaging;

// Only the integration run's typed owner can provide service endpoints and credentials.
internal sealed class RabbitMqMessageFixture : IAsyncDisposable
{
    private readonly RabbitFixtureInformation information;
    private readonly string rootPem;
    private readonly ServiceProvider provider;
    private readonly SharedTopology ownership;
    private readonly Func<MessagingSnapshot, MessagingSnapshot>? snapshotTransform;
    private readonly Action<IServiceCollection>? serviceSetup;
    private readonly Action<SecurityOptions>? securitySetup;
    private int disposed;

    private RabbitMqMessageFixture(OwnedRabbitService service, bool withSubscription, int maximumQueueMessages,
        bool safeQuarantine, bool withRequests, bool wrongHost, string? topologyNamespace = null, bool allowNoSubscribers = false,
        SharedTopology? ownership = null, Func<MessagingSnapshot, MessagingSnapshot>? transformSnapshot = null,
        Action<IServiceCollection>? configureServices = null, Action<SecurityOptions>? configureSecurity = null,
        bool ownsService = false)
    {
        information = service.CaptureInformation();
        rootPem = service.RootPem;
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
        this.ownership = ownership ?? new(service, ownsService);
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
    internal RabbitFixtureInformation Information => information;
    internal string RootPem => rootPem;
    internal OwnedRabbitService Service => ownership.Service;
    internal string EventExchange => RabbitMqTopology.GetRouteExchange(Options.Capture(), "events");
    internal string Queue => RabbitMqTopology.GetSubscriptionQueue(Options.Capture(), Local.Application, Local.Environment, "A");
    internal string QuarantineQueue => Options.Namespace + ".quarantine.local.A";

    internal void TrackReplyEndpoint(Guid endpointId)
    {
        if (endpointId == Guid.Empty)
        {
            throw new ArgumentException("Track only an endpoint observed from the newly owned process.", nameof(endpointId));
        }
        string queue = RabbitMqTopology.GetReplyQueue(Options.Capture(), new(Local.Application, Local.Environment, endpointId));
        lock (ownership)
        {
            if (ownership.ReplyQueues.Contains(queue, StringComparer.Ordinal))
            {
                return;
            }
            if (ownership.ReplyQueues.Count == 16)
            {
                throw new InvalidOperationException("The owned fixture permits at most sixteen tracked reply endpoints.");
            }
            ownership.ReplyQueues.Add(queue);
        }
    }
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
        => new(ownership.Service, Snapshot.Subscriptions.Count != 0, Options.MaximumQueueMessages,
            Options.SafeDeadLetteringVerified, withRequests, false, Options.Namespace, Snapshot.GetRoute("events").AllowNoSubscribers,
            ownership, transformSnapshot ?? snapshotTransform, configureServices ?? serviceSetup, configureSecurity ?? securitySetup);

    internal static async Task<RabbitMqMessageFixture> CreateAsync(bool withSubscription = true, int maximumQueueMessages = 10_000,
        bool safeQuarantine = true, bool withRequests = false, bool wrongHost = false, bool installTopology = true,
        bool allowNoSubscribers = false, Func<MessagingSnapshot, MessagingSnapshot>? transformSnapshot = null,
        Action<IServiceCollection>? configureServices = null, Action<SecurityOptions>? configureSecurity = null,
        bool dedicatedBroker = false)
    {
        OwnedRabbitService service = dedicatedBroker ? await OwnedRabbitService.CreateDedicatedAsync()
            : await IntegrationServices.GetRabbitAsync();
        RabbitMqMessageFixture? fixture = null;
        try
        {
            fixture = new RabbitMqMessageFixture(service, withSubscription, maximumQueueMessages, safeQuarantine,
                withRequests, wrongHost, allowNoSubscribers: allowNoSubscribers, transformSnapshot: transformSnapshot,
                configureServices: configureServices, configureSecurity: configureSecurity, ownsService: dedicatedBroker);
            if (installTopology)
            {
                await RabbitMqTopology.InstallAsync(fixture.SetupOptions, fixture.Snapshot);
            }
            return fixture;
        }
        catch (Exception original)
        {
            try
            {
                if (fixture is not null)
                {
                    await fixture.DisposeAsync();
                }
                else if (dedicatedBroker)
                {
                    await service.DisposeAsync();
                }
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Provider setup and its owned cleanup both failed.", original, cleanup);
            }
            throw;
        }
    }

    private RabbitMqOptions CreateOptions(string role)
    {
        RabbitFixturePrincipal principal = information.Principals[role];
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
        RabbitFixturePrincipal principal = information.Principals[role];
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

    internal async Task RestartOwnedBrokerApplicationAsync()
    {
        await ownership.Service.RestartApplicationAsync();
    }

    internal async Task<long> TotalQueuedAsync(string queue)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var report = await ownership.Service.Container.ExecAsync(["rabbitmqctl", "list_queues",
            "-p", information.VirtualHost, "name", "messages", "--formatter", "json"], deadline.Token);
        if (report.ExitCode != 0 || report.Stdout.Length > 65_536)
        {
            throw new InvalidOperationException("The bounded owned-broker queue observation failed.");
        }
        using JsonDocument document = JsonDocument.Parse(report.Stdout);
        return document.RootElement.EnumerateArray().Single(item => item.GetProperty("name").GetString() == queue)
            .GetProperty("messages").GetInt64();
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
                    ownership.ClientLease.Dispose();
                    if (ownership.OwnsService)
                    {
                        await ownership.Service.DisposeAsync();
                    }
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
        // An interrupted test may have observed a changed mapping. Administrative cleanup follows
        // the current owned endpoint; the production transport keeps its originally captured one.
        await using IConnection cleanup = await ownership.Service.CreateConnectionFactory(information.Principals["setup"]).CreateConnectionAsync();
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

    private sealed class SharedTopology(OwnedRabbitService service, bool ownsService)
    {
        internal OwnedRabbitService Service { get; } = service;
        internal bool OwnsService { get; } = ownsService;
        internal IDisposable ClientLease { get; } = service.AcquireClientLease();
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

}
