using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
public sealed class MessagingOptionsTests
{
    [TestMethod]
    public void DefaultsCaptureFiniteEntryWaitWorkAndByteBoundsWithoutStartingAnything()
    {
        MessagingSettings settings = new MessagingOptions().Capture();
        Assert.AreEqual(262_144, settings.Limits.MaximumPayloadBytes);
        Assert.AreEqual(32, settings.PublisherBatchSize);
        Assert.AreEqual(4, settings.PublisherConcurrency);
        Assert.AreEqual(32, settings.MaximumConcurrentDeliveries);
        Assert.AreEqual(33_554_432L, settings.MaximumEncodedBufferBytes);
        Assert.AreEqual(256, settings.MaximumOutstandingRequests);
        Assert.AreEqual(TimeSpan.FromSeconds(30), settings.DefaultRequestTimeout);
        Assert.AreEqual(TimeSpan.FromMinutes(5), settings.MaximumRequestTimeout);
        Assert.AreEqual(TimeSpan.FromMinutes(10), settings.ReplyEndpointLifetime);
        Assert.IsFalse(settings.LogCalls);
        Assert.IsNull(settings.Retention.InboxProtectionRetention);
        Assert.IsNull(settings.Retention.PublishedPayloadRetention);
    }

    [TestMethod]
    public void CoupledResourceLimitsRejectUnsafeLeasesDeadlineEndpointsAndCopyCapacity()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessagingOptions { MaximumPayloadBytes = 1_048_577 }.Capture());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessagingLimits(1, 33));
        Assert.ThrowsExactly<ArgumentException>(() => new MessagingOptions { ReservationDuration = TimeSpan.FromSeconds(10) }.Capture());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessagingOptions { DefaultRequestTimeout = TimeSpan.FromMinutes(6) }.Capture());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessagingOptions { MaximumRequestTimeout = TimeSpan.FromMinutes(6) }.Capture());
        Assert.ThrowsExactly<ArgumentException>(() => new MessagingOptions { ReplyEndpointLifetime = TimeSpan.FromMinutes(5) }.Capture());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessagingOptions { PublisherBatchSize = 33 }.Capture());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessagingOptions { MaximumEncodedBufferBytes = 4L * (262_144 + 16_384) - 1 }.Capture());
        Assert.AreEqual(4L * (262_144 + 16_384),
            new MessagingOptions { MaximumEncodedBufferBytes = 4L * (262_144 + 16_384) }.Capture().MaximumEncodedBufferBytes);
    }

    [TestMethod]
    public void FiniteProtectionMustCoverExplicitDeliveryAndAdministrativeReplayHorizons()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRetentionPolicy(inboxProtectionRetention: TimeSpan.FromDays(7)));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRetentionPolicy(inboxProtectionRetention: TimeSpan.FromDays(1),
            supportedRedriveHorizon: TimeSpan.FromDays(2), maximumDeliveryAge: TimeSpan.FromHours(1)));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRetentionPolicy(terminalRetention: TimeSpan.FromHours(1),
            supportedRedriveHorizon: TimeSpan.FromDays(2)));
        var policy = new MessageRetentionPolicy(TimeSpan.FromHours(1), TimeSpan.FromDays(7), TimeSpan.FromDays(4),
            TimeSpan.FromDays(3), TimeSpan.FromDays(7));
        Assert.AreEqual(TimeSpan.FromDays(7), policy.InboxProtectionRetention);
        Assert.AreEqual(TimeSpan.FromHours(1), policy.PublishedPayloadRetention);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessageRetentionPolicy(TimeSpan.Zero));
    }
}
