using System.Text;
using System.Text.Json;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Tests.MessagingCrashFixture;

internal sealed record RuleEngineAuditCrashInput(int ContractVersion, string MessagingSchema, Guid ExecutionId,
    string AuthorityId, string ApplicationId, string RegistrationId, string RulesetId, string SchemaRevision, string Version,
    string ProducerApplication, string ProducerEnvironment, DateTimeOffset StartedAtUtc);

internal sealed record RuleEngineAuditCrashMarker(Guid RecordId, Guid MessageId, string InstanceId);

internal sealed record RuleEngineCountryDataInput(int ContractVersion, string ManagementBaseUrl, string MessagingSchema,
    string ExpectedVersion, bool RequestExactVersion, string ResidenceCountry, bool Pensioner, string CountrySourceUrl,
    bool HoldAfterCommittedStart = false, string? ExpectedReleaseId = null, string? ExpectedCohortId = null);

internal sealed record RuleEngineCountryDataBroker(string Host, int Port, string ServerName, string VirtualHost,
    string Namespace, string SubjectIssuer, string User, string Password);

internal sealed record RuleEngineCountryDataStarted(Guid ExecutionId, string Version);

internal sealed record RuleEngineCountryDataResult(int ProcessId, string InstanceId, string RegistrationId, Guid ExecutionId,
    string RulesetId, string? Version, string Status, RuleEngineCountryDataMatch[] Matches, RuleEngineCountryDataTrace[] Trace, string? ErrorCode);

internal sealed record RuleEngineCountryDataMatch(string RuleId, int Ordinal, string Weight, string? Metadata);

internal sealed record RuleEngineCountryDataTrace(int RowIndex, string State, string Outcome);

// Public-only source shared by the one B05 process boundary and its package consumer.
public sealed class CrashDescriptor
{
    public int Version { get; set; } = 1;
    public string Mode { get; set; } = "roundtrip";
    public string Namespace { get; set; } = "";
    public string MessagingSchema { get; set; } = "";
    public string OperationSchema { get; set; } = "";
    public string CounterSchema { get; set; } = "";
    public string CoordinationSchema { get; set; } = "";
    public Guid CoordinationAuthority
    {
        get; set;
    }
    public string MarkerPath { get; set; } = "";
    public string ResultPath { get; set; } = "";
    public string CancelPath { get; set; } = "";
    public int Input { get; set; } = 7;
    public string OperationKey { get; set; } = "rpc-key";
    public int TimeoutSeconds { get; set; } = 30;
    public bool MinimumBudget
    {
        get; set;
    }
    public string Principal { get; set; } = "service";
    public bool DenyExecute
    {
        get; set;
    }
    public bool ObserveBeforeCommit
    {
        get; set;
    }
}

public sealed class CrashReport
{
    public int Version { get; set; } = 1;
    public string Application { get; set; } = "";
    public string Environment { get; set; } = "";
    public string Submission { get; set; } = "";
    public string Completion { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string Business { get; set; } = "";
    public string? Value
    {
        get; set;
    }
    public string? ReplayedValue
    {
        get; set;
    }
    public string? QueryValue
    {
        get; set;
    }
    public Guid MessageId
    {
        get; set;
    }
    public Guid AttemptId
    {
        get; set;
    }
    public Guid SecondMessageId
    {
        get; set;
    }
    public Guid SecondAttemptId
    {
        get; set;
    }
    public Guid EndpointId
    {
        get; set;
    }
    public long EffectCount
    {
        get; set;
    }
    public long JournalCount
    {
        get; set;
    }
    public string CoreHash { get; set; } = "";
    public string PreCommitExchange { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string CoordinationOutcome { get; set; } = "";
    public string ObservedOwnerInstanceId { get; set; } = "";
    public string Generation { get; set; } = "";
    public Guid OutgoingRecordId
    {
        get; set;
    }
    public string OutgoingDigest { get; set; } = "";
}

// Controlled application-policy substitution for SQL/process tests. It authenticates no workload or broker.
public sealed class CoordinationConsumerAuthorization(LocalApplicationIdentity local) : ICapabilityAuthorizer
{
    public int Checks
    {
        get; private set;
    }

    public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Checks++;
        bool allowed = target.Application == local.Application && target.Environment == local.Environment
            && target.ResourceType == "ledger" && target.ResourceIdentifier == "account"
            && capabilities.Count > 0 && capabilities.All(capability => capability is "dispatch.execute" or "dispatch.lookup");
        return ValueTask.FromResult(allowed ? AuthorizationDecision.Allow
            : AuthorizationDecision.Deny(new(SecurityFailureKind.CapabilityDenied, "The controlled consumer policy refused the target.")));
    }
}

public static class CrashContracts
{
    public static MessagingOptions Options(bool minimum = false) => new()
    {
        MaximumPayloadBytes = 1024,
        MaximumConcurrentDeliveries = 2,
        MaximumOutstandingRequests = 4,
        PublisherConcurrency = 1,
        MaximumEncodedBufferBytes = minimum ? 12 * new MessagingLimits(1024).MaximumEnvelopeBytes : 1_000_000,
        DefaultRequestTimeout = TimeSpan.FromSeconds(30),
        PublicationTimeout = TimeSpan.FromSeconds(3),
        ReservationDuration = TimeSpan.FromSeconds(5),
        IdlePollInterval = TimeSpan.FromMilliseconds(50)
    };

    public static MessagingSnapshot Snapshot(MessagingOptions options, LocalApplicationIdentity local, bool consume,
        Func<MessageHandlerContext, int, CancellationToken, Task<OperationDecision<string>>>? command = null)
    {
        var input = new NumberCodec();
        var output = new TextCodec();
        var submit = new MessageRequestDefinition<int, string>("SubmitRequest", "commands", input, output,
            MessageRequestMode.DurableCommand, local.Application, local.Environment, "Submit");
        var query = new MessageRequestDefinition<int, string>("Query", "queries", input, output,
            MessageRequestMode.Query, local.Application, local.Environment);
        MessageSubscriptionDefinition[] subscriptions = consume
            ? [new MessageRequestSubscriptionDefinition<int, string>("A", "Messaging", submit,
                    _ => new DelegateHandler(command ?? ((_, value, _) => Task.FromResult(OperationDecision<string>.Succeed("setup:" + value)))),
                    (_, value, _, _) => ValueTask.FromResult(new CapabilityTarget(local.Application, local.Environment, "ledger",
                        value == 999 ? "forbidden-account" : "account")), ["fixture.handle"]),
                new MessageRequestSubscriptionDefinition<int, string>("Q", "Messaging", query,
                    _ => new DelegateHandler((_, value, _) => Task.FromResult(value < 0
                        ? OperationDecision<string>.Reject("fixture.rejected", "Declined by the fixture.")
                        : OperationDecision<string>.Succeed("query:" + value))),
                    (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(local.Application, local.Environment)), ["fixture.handle"])]
            : [];
        return new(options, [new MessageRouteDefinition<int>("commands", MessageKind.Command, input),
            new MessageRouteDefinition<int>("queries", MessageKind.Command, input)], [new("Messaging", "Messaging")], subscriptions, [submit, query]);
    }

    public static void Register(IServiceCollection services, MessagingSnapshot snapshot)
    {
        foreach (MessageRouteDefinition route in snapshot.Routes)
        {
            services.AddCisMessageRoute(route);
        }
        foreach (MessageStoreDefinition store in snapshot.Stores)
        {
            services.AddCisMessageStore(store);
        }
        foreach (MessageRequestDefinition request in snapshot.Requests)
        {
            services.AddCisMessageRequest(request);
        }
        foreach (MessageSubscriptionDefinition subscription in snapshot.Subscriptions)
        {
            services.AddCisMessageSubscription(subscription);
        }
    }

    public static OperationDefinition<int, string> Operation() => new("Submit", "Messaging", new("crash.input", 1),
        new NumberNormalizer(), new ResultCodec(), _ => new ScopeResolver(),
        new()
        {
            ExecuteCapabilities = ["dispatch.execute"],
            LookupCapabilities = ["dispatch.lookup"]
        });

    // One extra scenario in the existing public process fixture; no calendar or workload host is embedded here.
    public static MessagingSnapshot CoordinationSnapshot(MessagingOptions options, LocalApplicationIdentity local, bool consume)
    {
        var codec = new NumberCodec();
        MessageSubscriptionDefinition[] subscriptions = consume
            ? [new MessageSubscriptionDefinition<int>("Coordination", "Messaging", "coordination", codec,
                _ => new CoordinationCaptureHandler(),
                (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(local.Application, local.Environment)), ["fixture.handle"])]
            : [];
        return new(options, [new MessageRouteDefinition<int>("coordination", MessageKind.Event, codec)],
            [new("Messaging", "Messaging")], subscriptions);
    }

    private sealed class CoordinationCaptureHandler : IMessageHandler<int>
    {
        public Task HandleAsync(MessageHandlerContext context, int value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NumberCodec : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new("fixture.number", 1);
        public byte[] Encode(int value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            value = JsonSerializer.Deserialize<int>(json.Span);
            return contract == Contract;
        }
    }
    private sealed class TextCodec : IMessageCodec<string>
    {
        public MessageContract Contract { get; } = new("fixture.response", 1);
        public byte[] Encode(string value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out string? value)
        {
            value = JsonSerializer.Deserialize<string>(json.Span);
            return contract == Contract;
        }
    }
    private sealed class DelegateHandler(Func<MessageHandlerContext, int, CancellationToken, Task<OperationDecision<string>>> action) : IRequestHandler<int, string>
    {
        public Task<OperationDecision<string>> HandleAsync(MessageHandlerContext context, int value, CancellationToken cancellationToken = default)
            => action(context, value, cancellationToken);
    }
    private sealed class NumberNormalizer : IOperationRequestNormalizer<int>
    {
        public NormalizedOperationInput<int> Normalize(int input, OperationScope scope)
            => new(input, [new(new("crash.input", 1), OperationCanonicalValue.Integer(input))]);
    }
    private sealed class ResultCodec : IOperationResultCodec<string>
    {
        public OperationContract CurrentContract { get; } = new("crash.result", 1);
        public IReadOnlyList<OperationContract> ReadableContracts { get; } = [new("crash.result", 1)];
        public byte[] Encode(string? value) => Encoding.UTF8.GetBytes(value!);
        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out string? decoded)
        {
            decoded = Encoding.UTF8.GetString(value.Span);
            return contract == CurrentContract;
        }
    }
    private sealed class ScopeResolver : IOperationScopeResolver
    {
        public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(requestedScope == "forbidden" ? new OperationScope("forbidden-scope", "ledger", "forbidden-account")
                : new OperationScope("trusted-account", "ledger", "account"));
    }
}
