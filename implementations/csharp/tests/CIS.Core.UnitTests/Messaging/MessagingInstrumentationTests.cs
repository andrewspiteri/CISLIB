using System.Diagnostics.Metrics;
using CIS.Messaging;
using CIS.Metrics;
using CIS.Security;
using CIS.Tests.UnitTests.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
public sealed class MessagingInstrumentationTests
{
    [TestMethod]
    [DataRow("publish", "confirmed")]
    [DataRow("publish", "unknown")]
    [DataRow("publish", "cancelled")]
    [DataRow("consume", "retry")]
    [DataRow("consume", "duplicate")]
    [DataRow("consume", "failed")]
    [DataRow("rpc", "submission_unknown")]
    [DataRow("rpc", "invalid_response")]
    public async Task OneOwnedObservationBalancesActiveAndKeepsItsFirstActualOutcome(string kind, string outcome)
    {
        using var meter = new Meter("Messaging.Unit");
        await using var runtime = new MetricsRuntime(meter);
        using var listener = new MetricsTestListener(meter);
        var clock = new MetricsTestClock();
        var owner = new MessagingInstrumentation(Snapshot(), clock);
        owner.AttachMetrics(runtime);
        using MessageObservation operation = Capture(owner, kind);
        clock.AdvanceMilliseconds(1250);
        operation.Complete(outcome);
        operation.Complete("failed");
        operation.Dispose();
        string instrument = kind == "publish" ? "publish" : kind == "consume" ? "delivery" : "rpc";
        MetricsPoint point = listener.Points("cis.messaging." + instrument + ".duration").Single();
        Assert.AreEqual(1.25d, (double)point.Value);
        Assert.IsTrue(point.Tags.Any(tag => tag.Key == "outcome" && Equals(tag.Value, outcome)));
        MetricsPoint[] active = listener.Points("cis.messaging.active");
        CollectionAssert.AreEqual(new long[] { 1, -1 }, active.Select(item => (long)item.Value).ToArray());
        Assert.IsTrue(active.All(item => item.Tags.Single().Key == "kind" && Equals(item.Tags.Single().Value, kind)));
    }

    [TestMethod]
    public async Task LateAttachmentDoesNotReplayAnAlreadyAdmittedCallAndReservedReplyAliasAddsNoStream()
    {
        using var meter = new Meter("Messaging.Late");
        await using var runtime = new MetricsRuntime(meter);
        using var listener = new MetricsTestListener(meter);
        var owner = new MessagingInstrumentation(Snapshot());
        using MessageObservation before = owner.Delivery("A");
        owner.AttachMetrics(runtime);
        before.Complete("handled");
        using (MessageObservation reply = owner.Delivery(MessagingSystemSubscriptions.Replies))
        {
            reply.Complete("handled");
        }
        using (MessageObservation unknown = owner.Publish("unregistered"))
        {
            unknown.Complete("confirmed");
        }
        Assert.HasCount(0, listener.Points("cis.messaging.delivery.duration"));
        Assert.HasCount(0, listener.Points("cis.messaging.publish.duration"));
        Assert.HasCount(0, listener.Points("cis.messaging.active"));
    }

    [TestMethod]
    public async Task BacklogOmitsUnknownIncompleteAndStaleCountsWithoutScrapeTimeWork()
    {
        using var meter = new Meter("Messaging.Backlog");
        await using var runtime = new MetricsRuntime(meter);
        using var listener = new MetricsTestListener(meter);
        var clock = new MetricsTestClock();
        var owner = new MessagingInstrumentation(Snapshot(), clock);
        owner.AttachMetrics(runtime);
        listener.Collect();
        Assert.HasCount(0, listener.Points("cis.messaging.outbox.pending"));
        owner.Observe(new(DateTimeOffset.UnixEpoch, null, 2, false));
        listener.Collect();
        Assert.HasCount(0, listener.Points("cis.messaging.outbox.pending"));
        Assert.AreEqual(2d, (double)listener.Points("cis.messaging.outbox.oldest.age").Single().Value);
        owner.Observe(new(DateTimeOffset.UnixEpoch, 0, 0, true));
        listener.Collect();
        Assert.AreEqual(0d, (double)listener.Points("cis.messaging.outbox.pending").Single().Value);
        clock.AdvanceMilliseconds(6000);
        listener.Collect();
        Assert.HasCount(1, listener.Points("cis.messaging.outbox.pending"));
        owner.Observe(null);
        listener.Collect();
        Assert.HasCount(1, listener.Points("cis.messaging.outbox.pending"));
    }

    [TestMethod]
    public async Task DispositionsUseOnlyRegisteredLogicalSubscriptionsAndFixedCommittedCategories()
    {
        using var meter = new Meter("Messaging.Dispositions");
        await using var runtime = new MetricsRuntime(meter);
        using var listener = new MetricsTestListener(meter);
        var owner = new MessagingInstrumentation(Snapshot());
        owner.AttachMetrics(runtime);
        owner.Disposition("A", "retry");
        owner.Disposition("A", "terminal");
        owner.Disposition("A", "duplicate");
        owner.Disposition("A", "private-exception-text");
        owner.Disposition("unregistered", "retry");
        Assert.HasCount(3, listener.Points("cis.messaging.dispositions"));
        Assert.IsTrue(listener.Points("cis.messaging.dispositions").All(item => (long)item.Value == 1
            && item.Tags.All(tag => tag.Key is "subscription" or "disposition")));
    }

    [TestMethod]
    public async Task OneInstrumentationOwnerCannotBeAttachedToAnotherHost()
    {
        using var firstMeter = new Meter("Messaging.First");
        using var secondMeter = new Meter("Messaging.Second");
        await using var first = new MetricsRuntime(firstMeter);
        await using var second = new MetricsRuntime(secondMeter);
        var owner = new MessagingInstrumentation(Snapshot());
        owner.AttachMetrics(first);
        owner.AttachMetrics(first);
        Assert.ThrowsExactly<InvalidOperationException>(() => owner.AttachMetrics(second));
    }

    internal static MessagingSnapshot Snapshot()
    {
        var codec = new PublicationNumberCodec();
        return new(new MessagingOptions(),
            [new MessageRouteDefinition<int>("events", MessageKind.Event, codec), new MessageRouteDefinition<int>("commands", MessageKind.Command, codec)],
            [new("Messaging", "Ledger")],
            [new MessageSubscriptionDefinition<int>("A", "Ledger", "events", codec, _ => new DispatchHandler((_, _, _) => Task.CompletedTask),
                (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget("server", "test")), ["test.handle"])],
            [new MessageRequestDefinition<int, int>("Query", "commands", codec, codec, MessageRequestMode.Query, "server", "test")]);
    }

    private static MessageObservation Capture(MessagingInstrumentation owner, string kind) => kind switch
    {
        "publish" => owner.Publish("events"),
        "consume" => owner.Delivery("A"),
        _ => owner.Rpc("Query")
    };
}
