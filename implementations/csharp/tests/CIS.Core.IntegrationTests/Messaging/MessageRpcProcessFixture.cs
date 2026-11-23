using System.Text.Json;
using CIS.Messaging;
using CIS.Operations.PostgreSql;
using CIS.Coordination.PostgreSql;
using CIS.TestSupport;
using CIS.Tests.MessagingCrashFixture;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Messaging;

// One bounded existing-fixture composition shared by B01-B06 and the package-only public entry consumer.
internal sealed class MessageRpcProcessFixture : IAsyncDisposable
{
    private readonly TemporaryDirectory directory = new();
    private readonly List<RpcChild> children = [];
    private PostgresMessageFixture? server;
    private PostgresMessageFixture? caller;
    private RabbitMqMessageFixture? broker;
    private bool operationSchemaOwned;
    private bool coordinationSchemaOwned;
    private bool preserve;
    internal PostgresMessageFixture Server => server!;
    internal PostgresMessageFixture Caller => caller!;
    internal RabbitMqMessageFixture Broker => broker!;
    internal string OperationSchema => Server.Schema + "_op";
    internal string CoordinationSchema => Server.Schema + "_coord";
    internal Guid CoordinationAuthority { get; } = Guid.NewGuid();
    internal string Path => directory.Path;
    internal static string Binary => System.IO.Path.Combine(AppContext.BaseDirectory, "messaging-crash-host", "CIS.Messaging.RpcCrashFixture.dll");

    internal static async Task<MessageRpcProcessFixture> CreateAsync(bool minimum = false, bool coordination = false)
    {
        var fixture = new MessageRpcProcessFixture();
        try
        {
            fixture.server = await PostgresMessageFixture.CreateAsync();
            fixture.caller = await PostgresMessageFixture.CreateAsync();
            fixture.operationSchemaOwned = true;
            await fixture.Server.SqlAsync(PostgreSqlOperationSchema.GetInstallationSql(fixture.OperationSchema));
            await fixture.Server.SqlAsync($"CREATE TABLE {fixture.Server.Table("rpc_effect_counter")} (total integer NOT NULL); INSERT INTO {fixture.Server.Table("rpc_effect_counter")} VALUES (0)");
            if (coordination)
            {
                fixture.coordinationSchemaOwned = true;
                await fixture.Server.SqlAsync(PostgreSqlCoordinationSchema.GetInstallationSql(fixture.CoordinationAuthority, fixture.CoordinationSchema));
            }
            fixture.broker = await RabbitMqMessageFixture.CreateAsync(withRequests: true, transformSnapshot: _ =>
                coordination ? CrashContracts.CoordinationSnapshot(CrashContracts.Options(minimum), CIS.Security.LocalApplicationIdentity.Capture(), true)
                    : CrashContracts.Snapshot(CrashContracts.Options(minimum), CIS.Security.LocalApplicationIdentity.Capture(), true));
            return fixture;
        }
        catch (Exception original)
        {
            try
            {
                await fixture.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("RPC fixture initialization and cleanup both failed.", original, cleanup);
            }
            throw;
        }
    }

    internal CrashDescriptor Descriptor(string mode, string name, bool minimum = false, int input = 7, int timeout = 30, bool deny = false)
        => new()
        {
            Mode = mode,
            Namespace = Broker.Options.Namespace,
            MessagingSchema = mode.StartsWith("caller", StringComparison.Ordinal) ? Caller.Schema : Server.Schema,
            OperationSchema = OperationSchema,
            CounterSchema = Server.Schema,
            CoordinationSchema = mode.StartsWith("coord-", StringComparison.Ordinal) ? CoordinationSchema : "",
            CoordinationAuthority = mode.StartsWith("coord-", StringComparison.Ordinal) ? CoordinationAuthority : Guid.Empty,
            Input = input,
            OperationKey = "rpc-key",
            TimeoutSeconds = timeout,
            MarkerPath = System.IO.Path.Combine(Path, name + ".marker.json"),
            ResultPath = System.IO.Path.Combine(Path, name + ".result.json"),
            CancelPath = System.IO.Path.Combine(Path, name + ".cancel"),
            MinimumBudget = minimum,
            Principal = mode.StartsWith("caller", StringComparison.Ordinal) ? "other" : "service",
            DenyExecute = deny
        };

    private IReadOnlyDictionary<string, string?> Environment(string postgres, string rabbit) => new Dictionary<string, string?>
    {
        ["CORE_SERVICE_NAME"] = Broker.Local.Application,
        ["CORE_ENVIRONMENT"] = Broker.Local.Environment,
        ["CIS_RM04_POSTGRES_CONNECTION_FILE"] = postgres,
        ["CIS_RM04_RABBITMQ_CONNECTION_FILE"] = rabbit
    };

    internal async Task<string> WriteDescriptorAsync(CrashDescriptor descriptor)
    {
        string path = System.IO.Path.Combine(Path, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(descriptor));
        return path;
    }

    internal async Task<RpcChild> StartAsync(CrashDescriptor descriptor, string? binary = null)
    {
        if (children.Count >= 8)
        {
            throw new InvalidOperationException("This case permits at most eight owned child entries.");
        }
        string connection = descriptor.Mode.StartsWith("caller", StringComparison.Ordinal) ? Caller.ConnectionString : Server.ConnectionString;
        (string postgres, string rabbit) = await OwnedRpcConnectionFiles.WriteAsync(Path, connection, Broker, descriptor.Principal);
        string input = await WriteDescriptorAsync(descriptor);
        var child = new RpcChild(descriptor);
        child.Execution = ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", binary ?? Binary, input],
            Path, TimeSpan.FromSeconds(120), Environment(postgres, rabbit), maximumCapturedBytesPerStream: 65_536, cancellationToken: child.Cancellation.Token);
        children.Add(child);
        return child;
    }

    internal async Task<JsonElement> MarkerAsync(RpcChild child, string phase, bool result = false)
    {
        JsonElement captured = default;
        await RabbitMqMessageFixture.UntilAsync(async () =>
        {
            if (child.Execution.IsCompleted)
            {
                ProcessResult finished = await child.Execution;
                Assert.Fail("The process ended before its expected marker: " + finished.StandardError);
            }
            string path = result ? child.Descriptor.ResultPath : child.Descriptor.MarkerPath;
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
                JsonElement root = document.RootElement;
                if (phase.Length != 0 && (!root.TryGetProperty("Phase", out JsonElement value) || value.GetString() != phase))
                {
                    return false;
                }
                captured = root.Clone();
                if (root.TryGetProperty("EndpointId", out JsonElement endpoint))
                {
                    Broker.TrackReplyEndpoint(endpoint.GetGuid());
                }
                return true;
            }
            catch (JsonException)
            {
                return false; // An owned marker write may still be completing.
            }
        }, TimeSpan.FromSeconds(30));
        return captured;
    }

    internal async Task<CrashReport> ReportAsync(RpcChild child)
    {
        ProcessResult result = await child.Execution;
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.IsTrue(result.OutputDrained);
        var report = JsonSerializer.Deserialize<CrashReport>(await File.ReadAllBytesAsync(child.Descriptor.ResultPath))!;
        if (!child.Descriptor.Mode.StartsWith("coord-", StringComparison.Ordinal))
        {
            Broker.TrackReplyEndpoint(report.EndpointId);
        }
        return report;
    }

    internal async Task<long> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = await Server.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    internal async Task<bool> ReplyPublishedAsync(Guid id)
    {
        await using NpgsqlConnection connection = await Server.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT state=2 FROM {Server.Table("outbox")} WHERE record_id=@id", connection);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync() is true;
    }

    internal async Task<EncodedMessage> ReadRequestAsync(Guid message)
    {
        await using NpgsqlConnection connection = await Caller.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT envelope FROM {Caller.Table("outbox")} WHERE message_id=@message AND purpose=0", connection);
        command.Parameters.AddWithValue("message", message);
        byte[] body = (byte[])(await command.ExecuteScalarAsync())!;
        Assert.IsTrue(MessageWire.TryDecode(body, out EncodedMessage? decoded, out _, CrashContracts.Options().Capture().Limits));
        return decoded!;
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        foreach (RpcChild child in children)
        {
            try
            {
                await child.StopAsync();
            }
            catch (ProcessTimeoutException failure)
            {
                preserve |= !failure.CleanupCompleted || !failure.Result.OutputDrained;
                failures.Add(failure);
            }
            catch (ProcessOutputLimitException failure)
            {
                preserve |= !failure.Result.OutputDrained;
                failures.Add(failure);
            }
            catch (Exception failure)
            {
                preserve = true;
                failures.Add(failure);
            }
        }
        if (!preserve)
        {
            if (broker is not null)
            {
                await CaptureAsync(() => broker.DisposeAsync().AsTask(), failures);
            }
            if (operationSchemaOwned && server is not null)
            {
                await CaptureAsync(() => server.SqlAsync($"DROP SCHEMA IF EXISTS \"{OperationSchema}\" CASCADE"), failures);
            }
            if (coordinationSchemaOwned && server is not null)
            {
                await CaptureAsync(() => server.SqlAsync($"DROP SCHEMA IF EXISTS \"{CoordinationSchema}\" CASCADE"), failures);
            }
            if (caller is not null)
            {
                await CaptureAsync(() => caller.DisposeAsync().AsTask(), failures);
            }
            if (server is not null)
            {
                await CaptureAsync(() => server.DisposeAsync().AsTask(), failures);
            }
            if (failures.Count == 0)
            {
                directory.Dispose();
            }
        }
        if (failures.Count != 0)
        {
            throw new AggregateException("Owned RPC process cleanup failed; retained location: " + Path, failures);
        }
    }

    private static async Task CaptureAsync(Func<Task> cleanup, List<Exception> failures)
    {
        try
        {
            await cleanup();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    internal sealed class RpcChild(CrashDescriptor descriptor)
    {
        internal readonly CancellationTokenSource Cancellation = new();
        internal CrashDescriptor Descriptor { get; } = descriptor;
        internal Task<ProcessResult> Execution = null!;
        internal bool Killed
        {
            get; private set;
        }
        private Task<ProcessResult>? stopping;

        internal async Task<ProcessResult> StopAsync(bool requireCrash = false)
        {
            ProcessResult result = await (stopping ??= StopCoreAsync());
            if (requireCrash)
            {
                Assert.IsTrue(Killed, "The child must still be running when the exact owner requests its kill.");
            }
            return result;
        }

        private async Task<ProcessResult> StopCoreAsync()
        {
            bool previouslyComplete = Execution.IsCompleted;
            await Cancellation.CancelAsync();
            try
            {
                ProcessResult result = await Execution;
                return result;
            }
            catch (ProcessTimeoutException failure) when (failure.CleanupCompleted && failure.Result.OutputDrained)
            {
                Killed = !previouslyComplete || Killed;
                return failure.Result;
            }
            finally
            {
                Cancellation.Dispose();
            }
        }
    }
}
