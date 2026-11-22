using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Volumes;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CIS.Tests.IntegrationTests.Infrastructure.Containers;

// One service owns PostgreSQL and its storage. Each behavior fixture still owns its schema and client lifetime.
internal sealed class OwnedPostgresService : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IVolume volume;
    private readonly IAsyncDisposable? dedicatedSlot;
    private readonly string password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private Task<OwnedPostgresService>? startup;
    private Task? disposal;
    private int clients;
    private bool ready;
    private bool closing;

    private OwnedPostgresService(IAsyncDisposable? dedicatedSlot, bool failStartup = false)
    {
        IntegrationServices.EnsureProfile();
        this.dedicatedSlot = dedicatedSlot;
        OwnerId = Guid.NewGuid().ToString("N");
        volume = new VolumeBuilder()
            .WithLabel(IntegrationServices.RunLabel, IntegrationServices.RunId)
            .WithLabel(IntegrationServices.OwnerLabel, OwnerId)
            .WithReuse(false)
            .Build();
        string[] command = ["-c", "fsync=on", "-c", "full_page_writes=on", "-c", "synchronous_commit=on", "-c", "max_connections=16"];
        if (failStartup)
        {
            command = [.. command, "-c", "cislib_invalid_startup_setting=on"];
        }
        Container = new PostgreSqlBuilder(IntegrationServices.PostgreSqlImage)
            .WithDatabase("cislib_tests")
            .WithUsername("cislib_tests")
            .WithPassword(password)
            .WithCommand(new OverwriteEnumerable<string>(command))
            .WithVolumeMount(volume, "/var/lib/postgresql")
            .WithLabel(IntegrationServices.RunLabel, IntegrationServices.RunId)
            .WithLabel(IntegrationServices.OwnerLabel, OwnerId)
            .WithReuse(false)
            .WithLogger(NullLogger.Instance)
            .WithCreateParameterModifier(parameters => IntegrationServices.ConfigureLocalContainer(parameters, 128))
            .Build();
        IntegrationServices.RequireLoopback(Container.Hostname);
    }

    internal PostgreSqlContainer Container
    {
        get;
    }
    internal string OwnerId
    {
        get;
    }
    internal string? ContainerId
    {
        get; private set;
    }
    internal string? VolumeName
    {
        get; private set;
    }
    internal string ServerVersion { get; private set; } = string.Empty;

    internal static Task<OwnedPostgresService> CreateSharedAsync() => new OwnedPostgresService(null).StartAsync();

    internal static async Task<OwnedPostgresService> CreateDedicatedAsync()
    {
        OwnedPostgresService owner = await CreateDedicatedOwnerAsync(failStartup: false);
        return await owner.StartAsync();
    }

    // Only the named failed-start lifecycle test obtains an unstarted, deliberately invalid service.
    internal static Task<OwnedPostgresService> CreateFailedStartupProbeAsync() => CreateDedicatedOwnerAsync(failStartup: true);

    private static async Task<OwnedPostgresService> CreateDedicatedOwnerAsync(bool failStartup)
    {
        using var cancellation = new CancellationTokenSource(IntegrationServices.StartupTimeout);
        IAsyncDisposable slot = await IntegrationServices.AcquireDedicatedSlotAsync(cancellation.Token);
        try
        {
            return new(slot, failStartup);
        }
        catch
        {
            await slot.DisposeAsync();
            throw;
        }
    }

    internal Task<OwnedPostgresService> StartAsync()
    {
        lock (gate)
        {
            return startup ??= StartCoreAsync();
        }
    }

    private async Task<OwnedPostgresService> StartCoreAsync()
    {
        using var cancellation = new CancellationTokenSource(IntegrationServices.StartupTimeout);
        try
        {
            await Container.StartAsync(cancellation.Token);
            CaptureIdentities();
            await ValidateReadyAsync(cancellation.Token);
            Console.WriteLine($"POSTGRES_OWNED image={IntegrationServices.PostgreSqlImage} version={ServerVersion} owner={OwnerId} container={ContainerId} durability=on/on/on");
            return this;
        }
        catch (Exception original)
        {
            CaptureIdentities();
            await CaptureFailureDiagnosticsAsync();
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Owned PostgreSQL startup and cleanup both failed.", original, cleanup);
            }
            throw;
        }
    }

    internal string CreateConnectionString(int maximumPoolSize)
    {
        if (maximumPoolSize is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPoolSize));
        }
        IntegrationServices.RequireLoopback(Container.Hostname);
        var options = new NpgsqlConnectionStringBuilder(Container.GetConnectionString())
        {
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = maximumPoolSize,
            Timeout = 15,
            CommandTimeout = 30,
            CancellationTimeout = 2_000,
            Enlist = false,
            NoResetOnClose = false,
            SslMode = SslMode.Disable,
            ApplicationName = "cis_testcontainer_" + OwnerId
        };
        options["GSS Encryption Mode"] = "Disable";
        return options.ConnectionString;
    }

    internal async Task ValidateReadyAsync(CancellationToken cancellationToken = default)
    {
        var options = new NpgsqlConnectionStringBuilder(CreateConnectionString(1)) { Pooling = false };
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT current_setting('server_version_num')::integer, current_setting('server_version'),
                   current_setting('fsync'), current_setting('full_page_writes'), current_setting('synchronous_commit')
            """, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetInt32(0) is < 180_000 or >= 190_000
            || reader.GetString(2) != "on" || reader.GetString(3) != "on" || reader.GetString(4) != "on")
        {
            throw new InvalidOperationException("The owned PostgreSQL 18 service must authenticate with fsync, full_page_writes and synchronous_commit enabled.");
        }
        ServerVersion = reader.GetString(1);
        lock (gate)
        {
            ready = true;
        }
    }

    internal IDisposable AcquireClientLease()
    {
        lock (gate)
        {
            if (!ready || closing)
            {
                throw new InvalidOperationException("The owned PostgreSQL service is not ready for another client.");
            }
            clients = checked(clients + 1);
            return new ClientLease(this);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null)
            {
                return new(disposal);
            }
            if (clients != 0)
            {
                throw new InvalidOperationException("PostgreSQL clients or preserved child ownership have not drained; the owned container is retained.");
            }
            closing = true;
            return new(disposal = DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        CaptureIdentities();
        var errors = new List<Exception>();
        try
        {
            using var cancellation = new CancellationTokenSource(IntegrationServices.CleanupTimeout);
            if (ContainerId is not null && Container.State is TestcontainersStates.Running or TestcontainersStates.Paused)
            {
                await Container.StopAsync(cancellation.Token);
            }
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            // The SDK exposes no Dispose cancellation overload. Keep actual deletion owned and joined.
            await Container.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            using var cancellation = new CancellationTokenSource(IntegrationServices.CleanupTimeout);
            await volume.DeleteAsync(cancellation.Token);
            await volume.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        if (errors.Count == 0)
        {
            await IntegrationServices.VerifyAbsentAsync(ContainerId, VolumeName, OwnerId);
            if (dedicatedSlot is not null)
            {
                await dedicatedSlot.DisposeAsync();
            }
        }
        else
        {
            throw new AggregateException("Owned PostgreSQL cleanup failed; retained identities remain available for restoration.", errors);
        }
    }

    private void CaptureIdentities()
    {
        if (ContainerId is null)
        {
            try
            {
                ContainerId = Container.Id;
            }
            catch (InvalidOperationException)
            {
                // Creation may have failed before the SDK obtained an identity.
            }
        }
        if (VolumeName is null)
        {
            try
            {
                VolumeName = volume.Name;
            }
            catch (InvalidOperationException)
            {
                // No volume identity is claimed before it actually exists.
            }
        }
    }

    private async Task CaptureFailureDiagnosticsAsync()
    {
        if (ContainerId is null)
        {
            return;
        }
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            (string stdout, string stderr) = await Container.GetLogsAsync(ct: cancellation.Token);
            string redacted = (stdout + "\n" + stderr).Replace(password, "[redacted]", StringComparison.Ordinal);
            Console.Error.WriteLine("POSTGRES_STARTUP_DIAGNOSTIC " + redacted[^Math.Min(8_000, redacted.Length)..]);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("POSTGRES_STARTUP_DIAGNOSTIC unavailable: " + error.GetType().Name);
        }
    }

    private sealed class ClientLease(OwnedPostgresService owner) : IDisposable
    {
        private OwnedPostgresService? owner = owner;

        public void Dispose()
        {
            OwnedPostgresService? current = Interlocked.Exchange(ref owner, null);
            if (current is null)
            {
                return;
            }
            lock (current.gate)
            {
                current.clients--;
            }
        }
    }
}
