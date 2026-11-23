using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Encodings.Web;
using CIS.Messaging;
using CIS.Coordination;
using CIS.Coordination.PostgreSql;
using CIS.Messaging.Hosting;
using CIS.Messaging.PostgreSql;
using CIS.Messaging.RabbitMQ;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Operations.PostgreSql;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.Startup.Hosting;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;
using Npgsql;

namespace CIS.Tests.MessagingCrashFixture;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                throw new InvalidDataException("Select one owned descriptor.");
            }
            string descriptorPath = Absolute(args[0]);
            CrashDescriptor descriptor = JsonSerializer.Deserialize<CrashDescriptor>(await ReadBoundedAsync(descriptorPath), Json)!;
            if (descriptor is null || descriptor.Version != 1 || descriptor.Mode is not ("roundtrip" or "responder" or "responder-hold" or "caller" or "caller-hold"
                    or "coord-owner" or "coord-contender" or "coord-successor")
                || descriptor.TimeoutSeconds is < 1 or > 60 || descriptor.Principal is not ("service" or "other"))
            {
                throw new InvalidDataException("Invalid bounded process mode.");
            }
            string directory = Path.GetDirectoryName(descriptorPath)!;
            OwnedPath(directory, descriptor.MarkerPath);
            OwnedPath(directory, descriptor.ResultPath);
            if (descriptor.CancelPath.Length != 0)
            {
                OwnedPath(directory, descriptor.CancelPath);
            }
            Schema(descriptor.MessagingSchema);
            Schema(descriptor.OperationSchema);
            Schema(descriptor.CounterSchema);
            string postgresPath = Absolute(Environment.GetEnvironmentVariable("CIS_RM04_POSTGRES_CONNECTION_FILE")!);
            string brokerPath = Absolute(Environment.GetEnvironmentVariable("CIS_RM04_RABBITMQ_CONNECTION_FILE")!);
            OwnedPath(directory, postgresPath);
            OwnedPath(directory, brokerPath);
            var postgres = new NpgsqlConnectionStringBuilder(System.Text.Encoding.UTF8.GetString(await ReadBoundedAsync(postgresPath)).Trim());
            if (postgres.Host is not ("localhost" or "127.0.0.1" or "::1") || !postgres.Pooling || postgres.MaxPoolSize is < 1 or > 6
                || postgres.Enlist || string.IsNullOrWhiteSpace(postgres.Database) || string.IsNullOrWhiteSpace(postgres.Username))
            {
                throw new InvalidDataException("Use only the explicitly owned isolated database fixture.");
            }
            BrokerFile broker = JsonSerializer.Deserialize<BrokerFile>(await ReadBoundedAsync(brokerPath), Json)!;
            if (broker.Host is not ("localhost" or "127.0.0.1" or "::1") || broker.Port is < 1 or > 65535
                || broker.ServerName != "localhost" || !broker.VirtualHost.StartsWith("/cis.tc.", StringComparison.Ordinal)
                || broker.ContainerId.Length != 64 || broker.ContainerId.Any(character => !Uri.IsHexDigit(character))
                || broker.Owner.Length != 32 || broker.Owner.Any(character => !Uri.IsHexDigit(character))
                || broker.Namespace != "cis.tc." + broker.Owner
                || !descriptor.Namespace.StartsWith(broker.Namespace + ".", StringComparison.Ordinal)
                || descriptor.Namespace.Length != broker.Namespace.Length + 9
                || descriptor.Namespace[(broker.Namespace.Length + 1)..].Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("Use only the parent-owned bounded local broker handoff.");
            }
            string certificate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(brokerPath)!, broker.RootCertificateFile));
            OwnedPath(Path.GetDirectoryName(brokerPath)!, certificate);
            Principal selected = broker.Principals[descriptor.Principal];
            if (string.IsNullOrWhiteSpace(selected.User) || string.IsNullOrWhiteSpace(selected.Password)
                || broker.Principals.Count != 2 || broker.Principals[descriptor.Principal == "service" ? "other" : "service"].Password.Length != 0
                || broker.ForeignUser != "cis_setup_" + broker.Owner[..8])
            {
                throw new InvalidDataException("The child descriptor must carry only its selected broker secret.");
            }
            LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
            var rabbit = new RabbitMqOptions
            {
                HostName = broker.Host,
                Port = broker.Port,
                ServerName = broker.ServerName,
                VirtualHost = broker.VirtualHost,
                UserName = selected.User,
                Password = selected.Password,
                Namespace = descriptor.Namespace,
                SubjectIssuer = "rm04-fixture",
                TrustedRootCertificatePem = System.Text.Encoding.UTF8.GetString(await ReadBoundedAsync(certificate)),
                SafeDeadLetteringVerified = true,
                EndpointNamespaces = [new(local.Application, local.Environment, "local")]
            };
            RabbitMqSettings settings = rabbit.Capture();
            var security = Security(local, settings, broker, descriptor.DenyExecute);
            bool consuming = descriptor.Mode is "roundtrip" or "responder" or "responder-hold";
            await using NpgsqlDataSource source = NpgsqlDataSource.Create(postgres.ConnectionString);
            if (descriptor.Mode.StartsWith("coord-", StringComparison.Ordinal))
            {
                await RunCoordinationAsync(descriptor, source, rabbit, local, security);
                return 0;
            }
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddCisAdoNetDatabase("Messaging", _ => source,
                new()
                {
                    SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
                });
            builder.Services.AddSingleton<IMessagingStore>(new PostgreSqlMessagingStore(new()
            {
                Schema = descriptor.MessagingSchema
            }));
            builder.Services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal(new()
            {
                Schema = descriptor.OperationSchema
            }));
            builder.Services.AddCisOperations();
            builder.Services.AddCisOperation(CrashContracts.Operation());
            MessagingOptions options = CrashContracts.Options(descriptor.MinimumBudget);
            MessagingSnapshot snapshot = CrashContracts.Snapshot(options, local, consuming, async (context, value, token) =>
            {
                if (value < 0)
                {
                    return OperationDecision<string>.Reject("fixture.rejected", "Declined by the fixture.");
                }
                await context.Transaction.UseAdoNetAsync(async (connection, transaction, cancellation) =>
                {
                    await using DbCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = $"UPDATE \"{descriptor.CounterSchema}\".rpc_effect_counter SET total=total+1";
                    if (await command.ExecuteNonQueryAsync(cancellation) != 1)
                    {
                        throw new InvalidOperationException("The owned fixture counter is missing.");
                    }
                }, token);
                return OperationDecision<string>.Succeed("original:" + value);
            });
            builder.Services.ConfigureCisMessaging(options);
            CrashContracts.Register(builder.Services, snapshot);
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BrokerOnlyScheme>("cis-rabbitmq", _ => { });
            builder.Services.AddCisSecurity(security);
            builder.Services.AddCisRabbitMqMessaging(rabbit);
            if (descriptor.Mode == "responder-hold")
            {
                builder.Services.Replace(ServiceDescriptor.Singleton<IMessageTransport>(provider =>
                    new HoldReplyTransport(provider.GetRequiredService<RabbitMqTransport>(), descriptor.MarkerPath)));
            }
            builder.Services.AddCisMessaging();
            builder.Services.AddCisStartup();
            IHost host = builder.Build();
            try
            {
                using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                await host.StartAsync(lifetime.Token);
                MessageReplyRoute endpoint = host.Services.GetRequiredService<IMessageTransport>().ReplyEndpoint!;
                await MarkerAsync(descriptor.MarkerPath, new
                {
                    Phase = "ready",
                    EndpointId = endpoint.EndpointId
                });
                if (descriptor.Mode is "responder" or "responder-hold")
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token);
                }
                else if (descriptor.Mode == "caller-hold")
                {
                    await using ILocalTransaction owner = await host.Services.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Messaging");
                    MessageRequestStage<string> stage = await host.Services.GetRequiredService<IMessageOutbox>().StageRequestAsync<int, string>(
                        owner.Context, Request(descriptor));
                    await using MessageRequestTicket<string> ticket = stage.Ticket ?? throw new InvalidOperationException("The request was not staged.");
                    string beforeCommit = "";
                    if (descriptor.ObserveBeforeCommit)
                    {
                        MessageRequestResult<string> unseen = await host.Services.GetRequiredService<IMessagingClient>().WaitForReplyAsync(ticket, lifetime.Token);
                        beforeCommit = unseen.Outcome.ToString();
                        if (unseen.Outcome != MessageExchangeOutcome.SubmissionNotConfirmed)
                        {
                            throw new InvalidOperationException("An independent read observed an active original transaction.");
                        }
                    }
                    TransactionCompletion committed = await owner.CommitAsync(lifetime.Token);
                    if (committed.Outcome != TransactionOutcome.Committed)
                    {
                        throw new InvalidOperationException("The caller request did not commit.");
                    }
                    await MarkerAsync(descriptor.MarkerPath, new
                    {
                        Phase = "caller-committed",
                        EndpointId = endpoint.EndpointId,
                        ticket.Reference.MessageId,
                        ticket.Reference.RequestAttemptId,
                        Digest = Convert.ToHexString(ticket.Reference.Receipt.Digest.CopyBytes()).ToLowerInvariant()
                    });
                    using var cancelWait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    using var stopSignal = new CancellationTokenSource();
                    Task signal = WatchCancellationAsync(descriptor.CancelPath, cancelWait, stopSignal.Token);
                    MessageRequestResult<string> ended;
                    try
                    {
                        ended = await host.Services.GetRequiredService<IMessagingClient>().WaitForReplyAsync(ticket, cancelWait.Token);
                    }
                    finally
                    {
                        await stopSignal.CancelAsync();
                        await signal;
                    }
                    await MarkerAsync(descriptor.ResultPath, new CrashReport
                    {
                        Application = local.Application,
                        Environment = local.Environment,
                        Submission = ended.Submission.Outcome.ToString(),
                        Completion = committed.Outcome.ToString(),
                        Exchange = ended.Outcome.ToString(),
                        Protocol = ended.Response?.Disposition.ToString() ?? "",
                        Business = ended.Response?.Decision?.Decision.ToString() ?? "",
                        Value = ended.Response?.Decision?.Value,
                        PreCommitExchange = beforeCommit,
                        MessageId = ticket.Reference.MessageId,
                        AttemptId = ticket.Reference.RequestAttemptId,
                        EndpointId = endpoint.EndpointId
                    });
                    await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token);
                }
                else
                {
                    IMessagingClient client = host.Services.GetRequiredService<IMessagingClient>();
                    MessageRequestResult<string> query = await client.RequestAsync<int, string>(new("Query", descriptor.Input,
                        timeout: TimeSpan.FromSeconds(descriptor.TimeoutSeconds)), lifetime.Token);
                    MessageRequestResult<string> first = await client.RequestAsync<int, string>(Request(descriptor), lifetime.Token);
                    MessageRequestResult<string> second = await client.RequestAsync<int, string>(Request(descriptor), lifetime.Token);
                    if (first.Outcome != MessageExchangeOutcome.ResponseReceived || second.Outcome != MessageExchangeOutcome.ResponseReceived
                        || query.Outcome != MessageExchangeOutcome.ResponseReceived)
                    {
                        throw new InvalidOperationException("The controlled roundtrip did not receive its responses.");
                    }
                    var report = new CrashReport
                    {
                        Application = local.Application,
                        Environment = local.Environment,
                        Submission = first.Submission.Outcome.ToString(),
                        Completion = first.Submission.Completion?.Outcome.ToString() ?? "",
                        Exchange = first.Outcome.ToString(),
                        Protocol = first.Response!.Disposition.ToString(),
                        Business = first.Response.Decision?.Decision.ToString() ?? "",
                        Value = first.Response.Decision?.Value,
                        ReplayedValue = second.Response!.Decision?.Value,
                        QueryValue = query.Response!.Decision?.Value,
                        MessageId = first.Reference!.MessageId,
                        AttemptId = first.Reference.RequestAttemptId,
                        SecondMessageId = second.Reference!.MessageId,
                        SecondAttemptId = second.Reference.RequestAttemptId,
                        EndpointId = endpoint.EndpointId,
                        EffectCount = await ScalarAsync(source, $"SELECT total::bigint FROM \"{descriptor.CounterSchema}\".rpc_effect_counter"),
                        JournalCount = await ScalarAsync(source, $"SELECT count(*) FROM \"{descriptor.OperationSchema}\".operation_journal"),
                        CoreHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(IMessagingClient).Assembly.Location))).ToLowerInvariant()
                    };
                    await MarkerAsync(descriptor.ResultPath, report);
                }
            }
            finally
            {
                await host.StopAsync();
                if (host is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }
                else
                {
                    host.Dispose();
                }
            }
            return 0;
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine("Messaging RPC fixture failed: " + failure.GetType().Name);
            return 2;
        }
    }

    // Public package/process proof reuses this existing entry. No Docker, schema installation or alternate application mode escapes the fixture.
    private static async Task RunCoordinationAsync(CrashDescriptor descriptor, NpgsqlDataSource source,
        RabbitMqOptions rabbit, LocalApplicationIdentity local, SecurityOptions security)
    {
        Schema(descriptor.CoordinationSchema);
        if (descriptor.CoordinationAuthority == Guid.Empty)
        {
            throw new InvalidDataException("Use the parent-installed coordination authority.");
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisAdoNetDatabase("Messaging", _ => source,
            new()
            {
                SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
            });
        builder.Services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal(new()
        {
            Schema = descriptor.OperationSchema
        }));
        builder.Services.AddSingleton<IMessagingStore>(new PostgreSqlMessagingStore(new()
        {
            Schema = descriptor.MessagingSchema
        }));
        builder.Services.AddCisOperations();
        builder.Services.AddCisOperation(CrashContracts.Operation());
        MessagingOptions options = CrashContracts.Options(descriptor.MinimumBudget);
        builder.Services.ConfigureCisMessaging(options);
        CrashContracts.Register(builder.Services, CrashContracts.CoordinationSnapshot(options, local, consume: false));
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BrokerOnlyScheme>("cis-rabbitmq", _ => { });
        builder.Services.AddCisSecurity(security);
        // Explicit bounded test policy, not fabricated workload authentication. Broker TLS/native evidence stays real.
        builder.Services.Replace(ServiceDescriptor.Scoped<ICapabilityAuthorizer, CoordinationConsumerAuthorization>());
        builder.Services.AddCisRabbitMqMessaging(rabbit);
        builder.Services.AddCisMessaging();
        builder.Services.AddCisStartup();
        IHost host = builder.Build();
        bool started = false;
        try
        {
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            ILocalTransactionFactory transactions = host.Services.GetRequiredService<ILocalTransactionFactory>();
            var authority = new PostgreSqlCoordinationProvider(transactions, "Messaging", new()
            {
                AuthorityId = descriptor.CoordinationAuthority,
                Schema = descriptor.CoordinationSchema
            });
            await authority.ValidateAsync(lifetime.Token);
            var key = new CoordinationKey(local.Environment, "wallet-service", "wallet.compaction", "partition-12");
            CoordinationAcquireResult acquired;
            using (var acquisitionDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                acquisitionDeadline.CancelAfter(TimeSpan.FromSeconds(25));
                do
                {
                    acquired = await authority.AcquireAsync(new(new(key, local.Application, local.InstanceId, Guid.NewGuid()),
                        TimeSpan.FromSeconds(15)), acquisitionDeadline.Token);
                    if (descriptor.Mode != "coord-successor" || acquired.Outcome != CoordinationAcquireOutcome.Busy)
                    {
                        break;
                    }

                    await Task.Delay(250, acquisitionDeadline.Token);
                }
                while (true);
            }

            var report = new CrashReport
            {
                Application = local.Application,
                Environment = local.Environment,
                InstanceId = local.InstanceId,
                CoordinationOutcome = acquired.Outcome.ToString(),
                ObservedOwnerInstanceId = acquired.ObservedOwner?.InstanceId ?? "",
                CoreHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(CoordinationGrant).Assembly.Location)))
            };
            if (descriptor.Mode == "coord-contender")
            {
                if (acquired.Outcome != CoordinationAcquireOutcome.Busy)
                {
                    throw new InvalidOperationException("The live independent owner must remain Busy.");
                }
                await MarkerAsync(descriptor.ResultPath, report);
                return;
            }

            CoordinationGrant grant = acquired.Grant ?? throw new InvalidOperationException("The actual authority did not commit a grant.");
            report.Generation = grant.Generation.ToString(CultureInfo.InvariantCulture);
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            IDurableOperationService operations = scope.ServiceProvider.GetRequiredService<IDurableOperationService>();
            await using (ILocalTransaction owner = await transactions.BeginAsync("Messaging", cancellationToken: lifetime.Token))
            {
                OperationExecution<string> result = await operations.ExecuteAsync<int, string>(owner.Context,
                    new("Submit", "requested-account", descriptor.OperationKey, descriptor.Input), async (context, value, token) =>
                    {
                        if (descriptor.Mode != "coord-owner")
                        {
                            throw new InvalidOperationException("A successor must replay the original result without repeating business work.");
                        }
                        (await authority.ValidateAsync(context, grant, token)).ThrowIfRejected();
                        await context.UseAdoNetAsync(async (connection, transaction, cancellation) =>
                        {
                            await using DbCommand command = connection.CreateCommand();
                            command.Transaction = transaction;
                            command.CommandText = $"UPDATE \"{descriptor.CounterSchema}\".rpc_effect_counter SET total=total+1";
                            if (await command.ExecuteNonQueryAsync(cancellation) != 1)
                            {
                                throw new InvalidOperationException("The owned effect counter is missing.");
                            }
                        }, token);
                        MessageStageResult staged = await host.Services.GetRequiredService<IMessageOutbox>()
                            .PublishAsync(context, "coordination", value, cancellationToken: token);
                        if (staged.Disposition != MessageStageDisposition.Staged)
                        {
                            throw new InvalidOperationException("The guarded outgoing intent was not staged.");
                        }
                        report.MessageId = staged.Receipt!.MessageId;
                        report.OutgoingRecordId = staged.Receipt.RecordId;
                        report.OutgoingDigest = Convert.ToHexStringLower(staged.Receipt.Digest.CopyBytes());
                        return OperationDecision<string>.Succeed("guarded:" + value);
                    }, lifetime.Token);
                OperationExecutionDisposition expected = descriptor.Mode == "coord-owner"
                    ? OperationExecutionDisposition.Prepared : OperationExecutionDisposition.Replayed;
                if (result.Disposition != expected)
                {
                    throw new InvalidOperationException("The original durable operation was not prepared or replayed as required.");
                }
                report.Business = result.Disposition.ToString();
                report.Value = result.Decision!.Value;
                TransactionCompletion completion = await owner.CommitAsync(lifetime.Token);
                if (completion.Outcome != TransactionOutcome.Committed)
                {
                    throw new InvalidOperationException("The guarded original transaction did not commit.");
                }
                report.Completion = completion.Outcome.ToString();
            }

            if (descriptor.Mode == "coord-owner")
            {
                await MarkerAsync(descriptor.MarkerPath, new
                {
                    Phase = "coordination-committed",
                    report.InstanceId,
                    report.Generation,
                    grant.ExpiresAtUtc,
                    report.MessageId,
                    report.OutgoingRecordId,
                    report.OutgoingDigest,
                    report.CoreHash
                });
                // The parent must kill and join this exact child; no cooperative release disguises recovery after failure.
                await Task.Delay(TimeSpan.FromSeconds(20), lifetime.Token);
                throw new InvalidOperationException("The parent did not terminate its bounded committed owner.");
            }

            if ((await authority.ReleaseAsync(grant, lifetime.Token)).Outcome != CoordinationMutationOutcome.Succeeded)
            {
                throw new InvalidOperationException("The successor must release its exact grant before publication starts.");
            }
            OperationLookup<string> retained = await operations.GetResultAsync<string>(new("Submit", "requested-account", descriptor.OperationKey), lifetime.Token);
            if (retained.Disposition != OperationLookupDisposition.Found || retained.Decision!.Value != report.Value)
            {
                throw new InvalidOperationException("Authorized result lookup after ownership ended lost the original result.");
            }

            // Starting the existing publisher only here proves committed intent survives the failed owner and later release.
            await host.StartAsync(lifetime.Token);
            started = true;
            while (await ScalarAsync(source, $"SELECT count(*) FROM \"{descriptor.MessagingSchema}\".outbox WHERE state=2") != 1)
            {
                await Task.Delay(25, lifetime.Token);
            }
            report.EffectCount = await ScalarAsync(source, $"SELECT total::bigint FROM \"{descriptor.CounterSchema}\".rpc_effect_counter");
            report.JournalCount = await ScalarAsync(source, $"SELECT count(*) FROM \"{descriptor.OperationSchema}\".operation_journal");
            await using (NpgsqlConnection connection = await source.OpenConnectionAsync(lifetime.Token))
            {
                await using var command = new NpgsqlCommand($"SELECT record_id,message_id,digest FROM \"{descriptor.MessagingSchema}\".outbox", connection);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(lifetime.Token);
                if (!await reader.ReadAsync(lifetime.Token))
                {
                    throw new InvalidOperationException("The committed outgoing record is missing.");
                }
                report.OutgoingRecordId = reader.GetGuid(0);
                report.MessageId = reader.GetGuid(1);
                report.OutgoingDigest = Convert.ToHexStringLower(reader.GetFieldValue<byte[]>(2));
            }
            await MarkerAsync(descriptor.ResultPath, report);
        }
        finally
        {
            try
            {
                if (started)
                {
                    await host.StopAsync(CancellationToken.None);
                }
            }
            finally
            {
                await ((IAsyncDisposable)host).DisposeAsync();
            }
        }
    }

    private static MessageRequest<int> Request(CrashDescriptor descriptor) => new("SubmitRequest", descriptor.Input,
        new("Submit", "requested-account", descriptor.OperationKey), TimeSpan.FromSeconds(descriptor.TimeoutSeconds));

    private static SecurityOptions Security(LocalApplicationIdentity local, RabbitMqSettings settings, BrokerFile broker, bool denyExecute)
    {
        var options = new SecurityOptions { PolicyVersion = "rpc-process-fixture" };
        options.Profiles.Add(new(settings.SecurityProfile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            authenticationScheme: "cis-rabbitmq", identityMappings: [new(new(settings.SubjectIssuer, broker.Principals["service"].User), local.Application, local.Environment),
                new(new(settings.SubjectIssuer, broker.Principals["other"].User), local.Application, local.Environment),
                new(new(settings.SubjectIssuer, broker.ForeignUser), "rpc-foreign", local.Environment)]));
        foreach (string capability in new[] { "fixture.handle", "dispatch.execute", "dispatch.lookup", "messaging.reply", "messaging.retry" })
        {
            options.Capabilities.Add(capability);
            foreach (string role in new[] { "service", "other" })
            {
                if (denyExecute && capability == "dispatch.execute" && role == "other")
                {
                    continue;
                }
                var subject = new SecuritySubject(settings.SubjectIssuer, broker.Principals[role].User);
                options.Grants.Add(new(settings.SecurityProfile, capability, local.Application, local.Environment, subject: subject));
                options.Grants.Add(new(settings.SecurityProfile, capability, local.Application, local.Environment, subject: subject,
                    resourceSelection: CapabilityResourceSelection.ExactResource, resourceType: "ledger", resourceIdentifier: "account"));
                foreach (string alias in new[] { "Query", "SubmitRequest" })
                {
                    options.Grants.Add(new(settings.SecurityProfile, capability, local.Application, local.Environment, subject: subject,
                        resourceSelection: CapabilityResourceSelection.ExactResource, resourceType: "messaging.reply", resourceIdentifier: alias));
                }
            }
        }
        return options;
    }

    private static async Task<long> ScalarAsync(NpgsqlDataSource source, string sql)
    {
        await using NpgsqlConnection connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path) ? Path.GetFullPath(path)
        : throw new InvalidDataException("An absolute owned fixture path is required.");
    private static void OwnedPath(string directory, string path)
    {
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        if (!Absolute(path).StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fixture output escaped its owned directory.");
        }
    }
    private static void Schema(string value)
    {
        if (!value.StartsWith("cis_rm04_", StringComparison.Ordinal) || value.Length > 63
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidDataException("Use the exact newly owned fixture schema.");
        }
    }
    private static async Task<byte[]> ReadBoundedAsync(string path)
    {
        var file = new FileInfo(path);
        if (file.Length is < 1 or > 65_536)
        {
            throw new InvalidDataException("The private fixture input exceeds its bound.");
        }
        return await File.ReadAllBytesAsync(path);
    }
    private static Task MarkerAsync<T>(string path, T marker) => File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(marker));

    private static async Task WatchCancellationAsync(string path, CancellationTokenSource wait, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested && path.Length != 0)
            {
                if (File.Exists(path))
                {
                    await wait.CancelAsync();
                    return;
                }
                await Task.Delay(25, stop);
            }
        }
        catch (OperationCanceledException error) when (error.CancellationToken == stop && stop.IsCancellationRequested)
        {
        }
    }

    private sealed class HoldReplyTransport(IMessageTransport inner, string marker) : IMessageTransport
    {
        private int held;
        public MessageReplyRoute? ReplyEndpoint => inner.ReplyEndpoint;
        public bool IsReady => inner.IsReady;
        public Task StartAsync(Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken = default) => inner.StartAsync(dispatch, cancellationToken);
        public Task StopConsumersAsync() => inner.StopConsumersAsync();
        public Task StopAsync() => inner.StopAsync();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask; // The provider owns the one real inner transport.
        public async Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
        {
            if (publication.Purpose == MessageRecordPurpose.Reply)
            {
                if (Interlocked.CompareExchange(ref held, 1, 0) == 0)
                {
                    await MarkerAsync(marker, new
                    {
                        Phase = "reply-held",
                        publication.OutboxRecordId,
                        MessageId = publication.Message.Envelope.InReplyToMessageId,
                        publication.Message.Envelope.RequestAttemptId,
                        EndpointId = inner.ReplyEndpoint!.EndpointId,
                        RecipientEndpointId = publication.Destination.ReplyRoute!.EndpointId
                    });
                }
                while (!File.Exists(marker + ".release"))
                {
                    await Task.Delay(25, cancellationToken);
                }
            }
            return await inner.PublishAsync(publication, cancellationToken);
        }
    }

    // Startup validates this selected native scheme. HTTP never mints broker identity.
    private sealed class BrokerOnlyScheme(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
    private sealed class BrokerFile
    {
        public BrokerFile()
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
        public string ContainerId { get; set; } = "";
        public string Owner { get; set; } = "";
        public string ForeignUser { get; set; } = "";
        public Dictionary<string, Principal> Principals { get; set; } = [];
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
