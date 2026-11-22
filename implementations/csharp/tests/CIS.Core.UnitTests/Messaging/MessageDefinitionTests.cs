using System.Text.Json;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
public sealed class MessageDefinitionTests
{
    [TestMethod]
    public void PassiveRegistrationCapturesOneSnapshotWithoutResolvingHandlersOrRuntimeStubs()
    {
        int calls = 0;
        var codec = new NumberCodec("country.query");
        var request = new MessageRequestDefinition<int, int>("Lookup", "country", codec, new NumberCodec("country.reply"),
            MessageRequestMode.Query, "server", "test");
        var subscription = new MessageRequestSubscriptionDefinition<int, int>("Country", "Ledger", request,
            _ =>
            {
                calls++;
                throw new InvalidOperationException("Registration must not create a handler.");
            },
            (_, _, _, _) =>
            {
                calls++;
                throw new InvalidOperationException("Registration must not resolve a resource.");
            },
            ["country.read"]);
        var options = new MessagingOptions();
        var services = new ServiceCollection();
        services.AddCisMessageRequest(request);
        services.AddCisMessageSubscription(subscription);
        services.AddCisMessageRoute(new MessageRouteDefinition<int>("country", MessageKind.Command, codec));
        services.AddCisMessageStore(new("Messaging", "Ledger"));
        services.ConfigureCisMessaging(options);
        options.MaximumPayloadBytes = 1;
        using ServiceProvider provider = services.BuildServiceProvider();
        MessagingSnapshot snapshot = provider.GetRequiredService<MessagingSnapshot>();
        snapshot.Validate();
        Assert.AreEqual(262_144, snapshot.Settings.Limits.MaximumPayloadBytes);
        Assert.AreSame(request, snapshot.GetRequest("Lookup"));
        Assert.AreSame(subscription, snapshot.Subscriptions.Single());
        Assert.AreEqual(0, calls);
        Assert.IsNull(provider.GetService<IMessageOutbox>());
        Assert.IsNull(provider.GetService<IMessagingClient>());
        Assert.IsNull(provider.GetService<IMessageTransport>());
    }

    [TestMethod]
    public void RegistriesAreFiniteOrdinalAndRejectDuplicateOrOpaqueOwnership()
    {
        var codec = new NumberCodec("value");
        var routes = Enumerable.Range(0, 16)
            .Select(index => (MessageRouteDefinition)new MessageRouteDefinition<int>("r" + index, MessageKind.Event, codec)).ToArray();
        var snapshot = new MessagingSnapshot(new(), routes);
        Assert.AreEqual(16, snapshot.Routes.Count);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<MessageRouteDefinition>)snapshot.Routes).Clear());
        Assert.ThrowsExactly<ArgumentException>(() => new MessagingSnapshot(new(), routes.Append(new MessageRouteDefinition<int>("r16", MessageKind.Event, codec))));
        Assert.ThrowsExactly<ArgumentException>(() => new MessagingSnapshot(new(), [routes[0], routes[0]]));
        Assert.AreEqual(2, new MessagingSnapshot(new(), [routes[0], new MessageRouteDefinition<int>("R0", MessageKind.Event, codec)]).Routes.Count);
        var services = new ServiceCollection();
        services.AddSingleton(_ => snapshot);
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisMessageStore(new("Messaging", "Ledger")));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRouteDefinition<int>("service", MessageKind.Command, codec, true));
    }

    [TestMethod]
    public void CompleteGraphRejectsMismatchedOriginalDatabaseCodecAndRequestMode()
    {
        var codec = new NumberCodec("value");
        var route = new MessageRouteDefinition<int>("r", MessageKind.Command, codec);
        var wrongDatabase = new MessageSubscriptionDefinition<int>("s", "Other", "r", codec, _ => new IgnoreHandler(),
            (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget("server", "test", "country", "MT")), ["country.read"]);
        var snapshot = new MessagingSnapshot(new(), [route], [new("Messaging", "Ledger")], [wrongDatabase]);
        Assert.ThrowsExactly<InvalidOperationException>(snapshot.Validate);
        var wrongContract = new MessageSubscriptionDefinition<int>("s", "Ledger", "r", new NumberCodec("different"), _ => new IgnoreHandler(),
            (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget("server", "test", "country", "MT")), ["country.read"]);
        Assert.ThrowsExactly<InvalidOperationException>(() => new MessagingSnapshot(new(), [route], [new("Messaging", "Ledger")], [wrongContract]).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRequestDefinition<int, int>("q", "r", codec, codec,
            MessageRequestMode.Query, "server", "test", "Debit"));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRequestDefinition<int, int>("q", "r", codec, codec,
            MessageRequestMode.DurableCommand, "server", "test"));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRequest<int>("q", 1,
            diagnostics: new(operation: new("Debit", "scope", "key"))));
    }

    [TestMethod]
    public void SubscriptionSettingsOwnRetryAndCapabilityListsAndBoundAllScheduledOrdinals()
    {
        TimeSpan[] delays = [TimeSpan.FromSeconds(1)];
        string[] capabilities = ["country.read"];
        var options = new MessageSubscriptionOptions { MaximumDeliveryOrdinals = 2, RetryDelays = delays };
        var subscription = new MessageSubscriptionDefinition<int>("s", "Ledger", "r", new NumberCodec("value"),
            _ => new IgnoreHandler(), (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget("server", "test")), capabilities, options);
        delays[0] = TimeSpan.FromDays(99);
        capabilities[0] = "country.other";
        options.Concurrency = 99;
        Assert.AreEqual(TimeSpan.FromSeconds(1), subscription.Settings.RetryDelays.Single());
        Assert.AreEqual("country.read", subscription.Capabilities.Single());
        Assert.AreEqual(8, subscription.Settings.Concurrency);
        Assert.ThrowsExactly<ArgumentException>(() => Subscription(new() { MaximumDeliveryOrdinals = 1 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Subscription(new() { RetryJitterRatio = double.NaN }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Subscription(new() { Prefetch = 7 }));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageSubscriptionDefinition<int>("s", "Ledger", "r", new NumberCodec("value"),
            _ => new IgnoreHandler(), (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget("server", "test")), []));
    }

    [TestMethod]
    public void CommandsHaveOneLogicalConsumerWhileEventsCanFanOutToIndependentSubscriptions()
    {
        var codec = new NumberCodec("value");
        MessageSubscriptionDefinition<int> First(string alias) => new(alias, "Ledger", "r", codec, _ => new IgnoreHandler(),
            (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget("server", "test")), ["country.read"]);
        MessageSubscriptionDefinition[] subscriptions = [First("first"), First("second")];
        Assert.ThrowsExactly<InvalidOperationException>(() => new MessagingSnapshot(new(),
            [new MessageRouteDefinition<int>("r", MessageKind.Command, codec)], [new("Messaging", "Ledger")], subscriptions).Validate());
        var events = new MessagingSnapshot(new(), [new MessageRouteDefinition<int>("r", MessageKind.Event, codec)],
            [new("Messaging", "Ledger")], subscriptions);
        events.Validate();
        Assert.AreEqual(2, events.Subscriptions.Count);
    }

    private static MessageSubscriptionDefinition<int> Subscription(MessageSubscriptionOptions options) => new("s", "Ledger", "r",
        new NumberCodec("value"), _ => new IgnoreHandler(),
        (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget("server", "test")), ["country.read"], options);

    private sealed class NumberCodec(string name) : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new(name, 1);
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
