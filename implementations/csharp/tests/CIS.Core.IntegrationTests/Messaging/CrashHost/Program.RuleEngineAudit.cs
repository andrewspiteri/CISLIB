using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Messaging.PostgreSql;
using CIS.RuleEngine;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CIS.Tests.MessagingCrashFixture;

internal static partial class Program
{
    static partial void SelectRuleEngineAuditCommand(string[] args, ref Task? command)
    {
        if (args is ["rule-engine-audit-start", string descriptor])
        {
            command = RunRuleEngineAuditStartAsync(Absolute(descriptor));
        }
    }

    private static async Task RunRuleEngineAuditStartAsync(string descriptorPath)
    {
        string directory = Path.GetDirectoryName(descriptorPath)!;
        RuleEngineAuditCrashInput input = JsonSerializer.Deserialize<RuleEngineAuditCrashInput>(await ReadBoundedAsync(descriptorPath), Json)
            ?? throw new InvalidDataException("The owned Rule Engine audit descriptor is empty.");
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        if (input.ContractVersion != RuleEngineAuditEvents.Version || input.ExecutionId == Guid.Empty
            || !OwnUuid(input.AuthorityId) || !OwnUuid(input.ApplicationId) || !OwnUuid(input.RegistrationId) || !OwnUuid(input.Version)
            || string.IsNullOrWhiteSpace(input.RulesetId) || input.RulesetId.Length > 255
            || string.IsNullOrWhiteSpace(input.SchemaRevision) || input.SchemaRevision.Length > 255
            || input.ProducerApplication != identity.Application || input.ProducerEnvironment != identity.Environment
            || input.StartedAtUtc.Offset != TimeSpan.Zero || input.StartedAtUtc <= DateTimeOffset.UnixEpoch)
        {
            throw new InvalidDataException("Use the exact bounded Rule Engine execution, target and native producer.");
        }
        Schema(input.MessagingSchema);
        string postgresPath = Path.Combine(directory, "postgres.txt");
        var postgres = new NpgsqlConnectionStringBuilder(Encoding.UTF8.GetString(await ReadBoundedAsync(postgresPath)).Trim());
        if (postgres.Host is not ("localhost" or "127.0.0.1" or "::1") || !postgres.Pooling || postgres.MaxPoolSize is < 1 or > 6
            || postgres.Enlist || string.IsNullOrWhiteSpace(postgres.Database) || string.IsNullOrWhiteSpace(postgres.Username)
            || postgres.ApplicationName != input.MessagingSchema)
        {
            throw new InvalidDataException("Use only the explicitly owned isolated Rule Engine outbox fixture.");
        }

        const string route = "rule-engine-audit";
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using NpgsqlDataSource source = NpgsqlDataSource.Create(postgres.ConnectionString);
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisAdoNetDatabase("Messaging", _ => source,
            new() { SupportedIsolationLevels = [TransactionIsolation.ReadCommitted] });
        builder.Services.AddSingleton<IMessagingStore>(new PostgreSqlMessagingStore(new() { Schema = input.MessagingSchema }));
        builder.Services.AddSingleton<IMessageTransport>(new UnavailableAuditTransport());
        builder.Services.ConfigureCisMessaging(new() { RequireLiveBrokerForReadiness = false });
        builder.Services.AddCisMessageStore(new("Messaging", "Messaging"));
        builder.Services.ConfigureCisAudit(new(route));
        builder.Services.AddCisMessaging();
        builder.Services.AddCisAudit();

        IHost host = builder.Build();
        await using var ownedHost = (IAsyncDisposable)host;
        await host.StartAsync(lifetime.Token);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        var eventDefinition = new AuditEventDefinition<RuleEngineAuditCrashInput>(RuleEngineAuditEvents.ExecutionStarted,
            RuleEngineAuditEvents.Version, AuditCategory.Business, new RuleEngineAuditCrashDetails(identity));
        CapturedAuditRecord start = scope.ServiceProvider.GetRequiredService<IAuditCapture>().CaptureLocalSystem(eventDefinition,
            input, new(AuditOutcome.Accepted, input.StartedAtUtc, domainEventId: input.ExecutionId.ToString("D") + ":start"));
        AuditSubmissionResult submitted = await scope.ServiceProvider.GetRequiredService<IAuditWriter>().SubmitAsync(start, lifetime.Token);
        if (submitted.Outcome != AuditSubmissionOutcome.OutboxCommitted || submitted.Receipt is not { } receipt)
        {
            throw new InvalidOperationException("The Rule Engine start did not commit to the native outbox: " + submitted.Outcome);
        }
        await MarkerAsync(Path.Combine(directory, "start-committed.json"),
            new RuleEngineAuditCrashMarker(receipt.RecordId, receipt.MessageId, identity.InstanceId));
        await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token);
    }

    private static bool OwnUuid(string value) => Guid.TryParseExact(value, "D", out Guid id) && id != Guid.Empty;

    private sealed class RuleEngineAuditCrashDetails(LocalApplicationIdentity identity) : IAuditDetailsCodec<RuleEngineAuditCrashInput>
    {
        public void Validate(RuleEngineAuditCrashInput input) => ArgumentNullException.ThrowIfNull(input);

        public void Write(Utf8JsonWriter writer, RuleEngineAuditCrashInput input)
        {
            writer.WriteStartObject();
            writer.WriteString("executionId", input.ExecutionId);
            writer.WriteString("authorityId", input.AuthorityId);
            writer.WriteString("applicationId", input.ApplicationId);
            writer.WriteString("application", identity.Application);
            writer.WriteString("environment", identity.Environment);
            writer.WriteString("instanceId", identity.InstanceId);
            writer.WriteString("registrationId", input.RegistrationId);
            writer.WriteString("applicationBuild", EnvironmentOptions.BuildVersion);
            writer.WriteNumber("engineContractVersion", RuleEngineAuditEvents.Version);
            writer.WriteString("rulesetId", input.RulesetId);
            writer.WriteString("schemaRevision", input.SchemaRevision);
            writer.WriteString("version", input.Version);
            writer.WriteString("startedAtUtc", input.StartedAtUtc);
            writer.WritePropertyName("inputs");
            writer.WriteRawValue("""[{"columnId":"country","kind":"string","value":"MLT"}]""");
            writer.WriteEndObject();
        }
    }

    // Fault boundary only. The production Audit writer and PostgreSQL transaction still own submission.
    private sealed class UnavailableAuditTransport : IMessageTransport
    {
        public MessageReplyRoute? ReplyEndpoint => null;
        public bool IsReady => false;
        public Task StartAsync(Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task StopConsumersAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
            => Task.FromResult(new MessagePublicationResult(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient));
    }
}
