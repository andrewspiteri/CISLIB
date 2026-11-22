using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CIS.TestSupport.Messaging;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Volumes;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace CIS.Tests.IntegrationTests.Infrastructure.Containers;

internal sealed class OwnedRabbitService : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly PrivateContainerDirectory files = new();
    private readonly Dictionary<string, RabbitFixturePrincipal> principals = [];
    private readonly List<string> passwordHashes = [];
    private readonly IAsyncDisposable? dedicatedSlot;
    private IVolume? volume;
    private RabbitMqContainer? container;
    private string host = "";
    private int port;
    private int activeClients;
    private bool closing;
    private Task? disposal;

    private OwnedRabbitService(IAsyncDisposable? dedicatedSlot)
    {
        this.dedicatedSlot = dedicatedSlot;
        OwnerId = Guid.NewGuid().ToString("N");
        Namespace = "cis.tc." + OwnerId;
        VirtualHost = "/cis.tc." + OwnerId;
        foreach (string role in new[] { "service", "other", "rogue", "setup" })
        {
            principals.Add(role, new("cis_" + role + "_" + OwnerId[..8],
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=')));
        }
    }

    internal string OwnerId
    {
        get;
    }
    internal string Namespace
    {
        get;
    }
    internal string VirtualHost
    {
        get;
    }
    internal string RootPem { get; private set; } = "";
    internal RabbitMqContainer Container => container ?? throw new InvalidOperationException("The owned broker has not been built.");
    internal string? ContainerId
    {
        get; private set;
    }
    internal string? VolumeName
    {
        get; private set;
    }
    internal string ServerVersion { get; private set; } = "";
    internal bool Dedicated => dedicatedSlot is not null;

    internal static Task<OwnedRabbitService> CreateSharedAsync() => CreateAsync(false, CancellationToken.None);

    internal static Task<OwnedRabbitService> CreateDedicatedAsync(CancellationToken cancellationToken = default)
        => CreateAsync(true, cancellationToken);

    private static async Task<OwnedRabbitService> CreateAsync(bool dedicated, CancellationToken cancellationToken)
    {
        IntegrationServices.EnsureProfile();
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(IntegrationServices.StartupTimeout);
        IAsyncDisposable? slot = dedicated ? await IntegrationServices.AcquireDedicatedSlotAsync(startup.Token) : null;
        OwnedRabbitService? owner = null;
        try
        {
            owner = new OwnedRabbitService(slot);
            await owner.StartAsync(startup.Token);
            return owner;
        }
        catch (Exception original)
        {
            if (owner is null)
            {
                if (slot is not null)
                {
                    await slot.DisposeAsync();
                }
                throw;
            }
            owner.CaptureIdentities();
            await owner.CaptureDiagnosticsAsync();
            try
            {
                await owner.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("RabbitMQ initialization failed and owned cleanup also failed.", original, cleanup);
            }
            throw;
        }
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        TlsFixtureFiles.Write(files.Path);
        RootPem = await File.ReadAllTextAsync(System.IO.Path.Combine(files.Path, "root.pem"), cancellationToken);
        byte[] certificate = await File.ReadAllBytesAsync(System.IO.Path.Combine(files.Path, "server.pem"), cancellationToken);
        byte[] key = await File.ReadAllBytesAsync(System.IO.Path.Combine(files.Path, "server.key"), cancellationToken);
        byte[] definitions = Definitions();
        volume = new VolumeBuilder().WithLabel(IntegrationServices.RunLabel, IntegrationServices.RunId)
            .WithLabel(IntegrationServices.OwnerLabel, OwnerId).WithReuse(false).Build();
        await volume.CreateAsync(cancellationToken);
        CaptureIdentities();
        RabbitFixturePrincipal setup = principals["setup"];
        const string bootstrap = "chown rabbitmq:rabbitmq /etc/rabbitmq/cis-owned/server.key /etc/rabbitmq/cis-owned/definitions.json && exec /usr/local/bin/docker-entrypoint.sh rabbitmq-server";
        container = new RabbitMqBuilder(IntegrationServices.RabbitMqImage)
            .WithUsername(setup.User).WithPassword(setup.Password)
            .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "+S 2:2 +A 2 +SDcpu 1 +SDio 1")
            .WithEnvironment("RABBITMQ_CTL_ERL_ARGS", "+S 1:1 +A 1 +SDcpu 1 +SDio 1")
            .WithLabel(IntegrationServices.RunLabel, IntegrationServices.RunId)
            .WithLabel(IntegrationServices.OwnerLabel, OwnerId)
            .WithReuse(false).WithCleanUp(true)
            .WithVolumeMount(volume, "/var/lib/rabbitmq")
            .WithExposedPort(5671)
            .WithEntrypoint("/bin/sh").WithCommand("-ec", bootstrap)
            .WithResourceMapping(Encoding.UTF8.GetBytes(Configuration), FilePath.Of("/etc/rabbitmq/rabbitmq.conf"))
            .WithResourceMapping(Encoding.UTF8.GetBytes(RootPem), FilePath.Of("/etc/rabbitmq/cis-owned/root.pem"))
            .WithResourceMapping(certificate, FilePath.Of("/etc/rabbitmq/cis-owned/server.pem"))
            .WithResourceMapping(key, FilePath.Of("/etc/rabbitmq/cis-owned/server.key"), fileMode: UnixFileModes.UserRead | UnixFileModes.UserWrite)
            .WithResourceMapping(definitions, FilePath.Of("/etc/rabbitmq/cis-owned/definitions.json"), fileMode: UnixFileModes.UserRead | UnixFileModes.UserWrite)
            .WithCreateParameterModifier(parameters =>
            {
                // Move the module's one random binding to TLS. Removing it and adding a second builder
                // binding would leave the SDK's port-mapping readiness count inconsistent with Docker.
                parameters.User = "0";
                if (parameters.HostConfig?.PortBindings is not { } bindings
                    || !bindings.Remove("5672/tcp", out var tlsBindings))
                {
                    throw new InvalidOperationException("The RabbitMQ module must supply its one random protocol binding.");
                }
                bindings.Add("5671/tcp", tlsBindings);
                parameters.ExposedPorts?.Remove("5672/tcp");
                IntegrationServices.ConfigureLocalContainer(parameters, 256);
            })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete")
                .UntilInternalTcpPortIsAvailable(5671))
            .WithLogger(NullLogger.Instance).Build();
        // Keep the module's application-start log wait; cancellation bounds it before authenticated readiness.
        await container.StartAsync(cancellationToken);
        CaptureIdentities();
        RefreshEndpoint();
        await ValidateReadyAsync(cancellationToken);
        var limit = await container.ExecAsync(["rabbitmqctl", "set_vhost_limits", "-p", VirtualHost,
            "{\"max-queues\":128,\"max-connections\":32}"], cancellationToken);
        if (limit.ExitCode != 0)
        {
            throw new InvalidOperationException("The newly owned broker did not accept its bounded vhost limits.");
        }
        Console.WriteLine($"RABBITMQ_OWNED image={IntegrationServices.RabbitMqImage} version={ServerVersion} owner={OwnerId} container={ContainerId} tls=true");
    }

    internal RabbitFixtureInformation CaptureInformation() => new(host, port, "localhost", VirtualHost, Namespace,
        Container.Id, OwnerId, principals);

    internal IDisposable AcquireClientLease()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            activeClients++;
            return new ClientLease(this);
        }
    }

    internal async Task RestartApplicationAsync(CancellationToken cancellationToken = default)
    {
        if (!Dedicated)
        {
            throw new InvalidOperationException("Broker interruption requires a dedicated owned service.");
        }
        int originalPort = port;
        string originalContainer = Container.Id;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(IntegrationServices.StartupTimeout);
        // This restarts RabbitMQ itself, leaving the Erlang VM and Docker network alive.
        // A full container restart can reassign a random host port and is tested with fresh clients.
        var stopped = await Container.ExecAsync(["rabbitmqctl", "stop_app"], deadline.Token);
        if (stopped.ExitCode != 0)
        {
            throw new InvalidOperationException("The owned RabbitMQ application did not stop.");
        }
        var started = await Container.ExecAsync(["rabbitmqctl", "start_app"], deadline.Token);
        if (started.ExitCode != 0)
        {
            throw new InvalidOperationException("The owned RabbitMQ application did not restart.");
        }
        await Container.StartAsync(deadline.Token);
        RefreshEndpoint();
        Console.WriteLine($"RABBITMQ_APPLICATION_RESTART sameContainer={Container.Id == originalContainer} originalPort={originalPort} currentPort={port}");
        await ValidateReadyAsync(deadline.Token);
        if (Container.Id != originalContainer || port != originalPort)
        {
            throw new InvalidOperationException("The RabbitMQ application restart changed the owned endpoint; live-client recovery cannot be claimed.");
        }
    }

    internal async Task RestartContainerForFreshClientsAsync(CancellationToken cancellationToken = default)
    {
        if (!Dedicated)
        {
            throw new InvalidOperationException("Container restart requires a dedicated owned service.");
        }
        string originalContainer = Container.Id;
        string? originalVolume = VolumeName;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(IntegrationServices.StartupTimeout);
        await Container.StopAsync(deadline.Token);
        await Container.StartAsync(deadline.Token);
        RefreshEndpoint();
        await ValidateReadyAsync(deadline.Token);
        if (Container.Id != originalContainer || VolumeName != originalVolume)
        {
            throw new InvalidOperationException("The container restart must preserve its owned container and data volume.");
        }
        Console.WriteLine("RABBITMQ_CONTAINER_RESTART sameContainer=true sameVolume=true freshClients=true");
    }

    internal async Task StartForFreshClientAsync(CancellationToken cancellationToken = default)
    {
        if (!Dedicated)
        {
            throw new InvalidOperationException("The initial-connection probe requires its dedicated owned broker.");
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(IntegrationServices.StartupTimeout);
        await Container.StartAsync(deadline.Token);
        RefreshEndpoint();
        await ValidateReadyAsync(deadline.Token);
    }

    internal async Task ValidateReadyAsync(CancellationToken cancellationToken)
    {
        await using IConnection connection = await CreateConnectionFactory(principals["setup"]).CreateConnectionAsync(cancellationToken);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        if (!connection.IsOpen || !channel.IsOpen)
        {
            throw new InvalidOperationException("The owned TLS broker has not completed authenticated AMQP readiness.");
        }
        object? version = null;
        connection.ServerProperties?.TryGetValue("version", out version);
        ServerVersion = version switch
        {
            byte[] encoded => Encoding.UTF8.GetString(encoded),
            string text => text,
            _ => ""
        };
        if (ServerVersion != "4.4.1")
        {
            throw new InvalidOperationException("The owned broker does not report the selected RabbitMQ 4.4.1 version.");
        }
    }

    internal ConnectionFactory CreateConnectionFactory(RabbitFixturePrincipal principal) => new()
    {
        HostName = host,
        Port = port,
        VirtualHost = VirtualHost,
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
            ServerName = "localhost",
            CertificateValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != 0)
                {
                    return false;
                }
                using X509Certificate2 root = X509Certificate2.CreateFromPem(RootPem);
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

    private void RefreshEndpoint()
    {
        host = Container.Hostname;
        IntegrationServices.RequireLoopback(host);
        port = Container.GetMappedPublicPort(5671);
    }

    private byte[] Definitions()
    {
        string permission = "^" + Regex.Escape(Namespace) + "\\..*$";
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            users = principals.Values.Select(principal => new
            {
                name = principal.User,
                password_hash = CapturePasswordHash(principal.Password),
                hashing_algorithm = "rabbit_password_hashing_sha256",
                tags = Array.Empty<string>()
            }).ToArray(),
            vhosts = new[] { new { name = VirtualHost } },
            permissions = principals.Select(item => new
            {
                user = item.Value.User,
                vhost = VirtualHost,
                configure = item.Key == "rogue" ? "^$" : permission,
                write = permission,
                read = item.Key == "rogue" ? "^$" : permission
            }).ToArray()
        });
    }

    private string CapturePasswordHash(string password)
    {
        string encoded = PasswordHash(password);
        passwordHashes.Add(encoded);
        return encoded;
    }

    private static string PasswordHash(string password)
    {
        // RabbitMQ's documented definitions format: 32-bit salt, SHA256(salt || UTF8 password), then salt || digest.
        byte[] salt = RandomNumberGenerator.GetBytes(4);
        byte[] clear = Encoding.UTF8.GetBytes(password);
        byte[] salted = new byte[salt.Length + clear.Length];
        salt.CopyTo(salted, 0);
        clear.CopyTo(salted, salt.Length);
        byte[] digest = SHA256.HashData(salted);
        byte[] encoded = new byte[salt.Length + digest.Length];
        salt.CopyTo(encoded, 0);
        digest.CopyTo(encoded, salt.Length);
        CryptographicOperations.ZeroMemory(clear);
        CryptographicOperations.ZeroMemory(salted);
        return Convert.ToBase64String(encoded);
    }

    private async Task CaptureDiagnosticsAsync()
    {
        if (container is null || SafeContainerId(container) is null)
        {
            return;
        }
        try
        {
            using var deadline = new CancellationTokenSource(IntegrationServices.CleanupTimeout);
            var (stdout, stderr) = await container.GetLogsAsync(ct: deadline.Token);
            string text = stdout + "\n" + stderr;
            foreach (RabbitFixturePrincipal principal in principals.Values)
            {
                text = text.Replace(principal.Password, "[redacted]", StringComparison.Ordinal);
            }
            foreach (string hash in passwordHashes)
            {
                text = text.Replace(hash, "[redacted]", StringComparison.Ordinal);
            }
            text = string.Join('\n', text.Split('\n').Where(line => !line.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("password_hash", StringComparison.OrdinalIgnoreCase)));
            Console.Error.WriteLine("Owned RabbitMQ startup diagnostics: " + text[..Math.Min(text.Length, 16_384)]);
        }
        catch
        {
            // The original initialization failure remains authoritative; unreachable diagnostics cannot replace it.
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (activeClients != 0)
            {
                throw new InvalidOperationException("Owned RabbitMQ clients have not drained; retain the service and private inputs for their owner.");
            }
            closing = true;
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var errors = new List<Exception>();
        CaptureIdentities();
        string? id = ContainerId;
        string? volumeName = VolumeName;
        if (container is not null)
        {
            try
            {
                if (ContainerId is not null && container.State is TestcontainersStates.Running or TestcontainersStates.Paused)
                {
                    using var deadline = new CancellationTokenSource(IntegrationServices.CleanupTimeout);
                    await container.StopAsync(deadline.Token);
                }
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            try
            {
                await container.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        if (volume is not null)
        {
            try
            {
                using var deadline = new CancellationTokenSource(IntegrationServices.CleanupTimeout);
                await volume.DeleteAsync(deadline.Token);
                await volume.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        if (errors.Count == 0)
        {
            await IntegrationServices.VerifyAbsentAsync(id, volumeName, OwnerId);
            files.Dispose();
            if (dedicatedSlot is not null)
            {
                await dedicatedSlot.DisposeAsync();
            }
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("Owned RabbitMQ cleanup failed; retain owner " + OwnerId + " and its Resource Reaper fallback.", errors);
        }
    }

    private void CaptureIdentities()
    {
        if (ContainerId is null && container is not null)
        {
            ContainerId = SafeContainerId(container);
        }
        if (VolumeName is null && volume is not null)
        {
            try
            {
                VolumeName = volume.Name;
            }
            catch (InvalidOperationException)
            {
                // The exact owner-label postflight also detects a cancelled create whose response was lost.
            }
        }
    }

    private static string? SafeContainerId(RabbitMqContainer selected)
    {
        try
        {
            return selected.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed class ClientLease(OwnedRabbitService owner) : IDisposable
    {
        private OwnedRabbitService? current = owner;

        public void Dispose()
        {
            OwnedRabbitService? selected = Interlocked.Exchange(ref current, null);
            if (selected is not null)
            {
                lock (selected.gate)
                {
                    selected.activeClients--;
                }
            }
        }
    }

    private const string Configuration = """
        listeners.tcp = none
        listeners.ssl.default = 5671
        ssl_options.cacertfile = /etc/rabbitmq/cis-owned/root.pem
        ssl_options.certfile = /etc/rabbitmq/cis-owned/server.pem
        ssl_options.keyfile = /etc/rabbitmq/cis-owned/server.key
        ssl_options.verify = verify_none
        ssl_options.fail_if_no_peer_cert = false
        ssl_options.versions.1 = tlsv1.3
        ssl_options.versions.2 = tlsv1.2
        definitions.import_backend = local_filesystem
        definitions.local.path = /etc/rabbitmq/cis-owned/definitions.json
        vm_memory_high_watermark.absolute = 256MiB
        disk_free_limit.absolute = 64MiB
        max_message_size = 1064960
        raft.wal_max_size_bytes = 16777216
        quorum_queue.wal_max_size_bytes = 16777216
        quorum_queue.segment_max_size_bytes = 16777216
        """;
}

internal sealed class RabbitFixtureInformation(string host, int port, string serverName, string virtualHost,
    string space, string containerId, string owner, IReadOnlyDictionary<string, RabbitFixturePrincipal> principals)
{
    internal string Host { get; } = host;
    internal int Port { get; } = port;
    internal string ServerName { get; } = serverName;
    internal string VirtualHost { get; } = virtualHost;
    internal string Namespace { get; } = space;
    internal string ContainerId { get; } = containerId;
    internal string Owner { get; } = owner;
    internal IReadOnlyDictionary<string, RabbitFixturePrincipal> Principals { get; } = principals;
}

internal sealed class RabbitFixturePrincipal(string user, string password)
{
    internal string User { get; } = user;
    internal string Password { get; } = password;
}
