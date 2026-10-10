using System.Globalization;
using CIS.Configuration;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class InitializationIdentityFactoryTests
{
    private static readonly Guid FirstId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid SecondId = Guid.Parse("00000000-0000-4000-8000-000000000002");

    [TestMethod]
    [DataRow("2026-10-10T09:05:00+02:00", "2026-10-10T07:05:00Z")]
    [DataRow("2026-10-10T09:05:00-07:00", "2026-10-10T16:05:00Z")]
    [DataRow("2026-10-10T09:05:00+14:00", "2026-10-09T19:05:00Z")]
    [DataRow("2026-10-10T09:05:00Z", "2026-10-10T09:05:00Z")]
    public void InitializationUsesTheSuppliedClockInstantInUTC(string supplied, string expected)
    {
        var clock = new InitializationTimeProvider(DateTimeOffset.Parse(supplied, CultureInfo.InvariantCulture));
        int generated = 0;
        var factory = new InitializationIdentityFactory(clock, () =>
        {
            generated++;
            return FirstId;
        });

        InitializationIdentity identity = factory.Create();

        Assert.AreEqual(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), identity.InitializedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, identity.InitializedAtUtc.Offset);
        Assert.AreEqual("00000000-0000-4000-8000-000000000001", identity.InstanceId);
        Assert.AreEqual(1, clock.Reads);
        Assert.AreEqual(1, generated);
    }

    [TestMethod]
    [DataRow("ABCDEF12-ABCD-4ABC-8ABC-ABCDEF123456", "abcdef12-abcd-4abc-8abc-abcdef123456")]
    [DataRow("00000000-0000-4000-8000-000000000001", "00000000-0000-4000-8000-000000000001")]
    public void InstanceIdentifiersUseCanonicalUUIDStrings(string supplied, string expected)
    {
        var clock = new InitializationTimeProvider(DateTimeOffset.UnixEpoch);
        var factory = new InitializationIdentityFactory(clock, () => Guid.Parse(supplied));

        InitializationIdentity identity = factory.Create();

        Assert.AreEqual(expected, identity.InstanceId);
    }

    [TestMethod]
    public void EachGenerationCapturesANewIdAndTimeAndExistingValuesStayStable()
    {
        var clock = new InitializationTimeProvider(DateTimeOffset.UnixEpoch);
        var ids = new Queue<Guid>([FirstId, SecondId]);
        var factory = new InitializationIdentityFactory(clock, ids.Dequeue);
        InitializationIdentity first = factory.Create();
        clock.UtcNow = DateTimeOffset.UnixEpoch.AddMinutes(5);
        InitializationIdentity second = factory.Create();
        clock.UtcNow = DateTimeOffset.UnixEpoch.AddYears(1);

        Assert.AreEqual(FirstId.ToString("D"), first.InstanceId);
        Assert.AreEqual(DateTimeOffset.UnixEpoch, first.InitializedAtUtc);
        Assert.AreEqual(SecondId.ToString("D"), second.InstanceId);
        Assert.AreEqual(DateTimeOffset.UnixEpoch.AddMinutes(5), second.InitializedAtUtc);
        Assert.AreEqual(2, clock.Reads);
        Assert.AreEqual(0, ids.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProcessStartAndInitializationTimeRemainIndependent(bool missingProcessStart)
    {
        var source = new RuntimeMetadataStub();
        if (missingProcessStart)
        {
            source.ProcessStartedAt = null;
        }

        RuntimeMetadata process = RuntimeMetadataDiscovery.Discover(source);
        var instant = new DateTimeOffset(2026, 10, 10, 7, 5, 0, TimeSpan.Zero);
        InitializationIdentity initialization = new InitializationIdentityFactory(new InitializationTimeProvider(instant), () => FirstId).Create();

        Assert.AreEqual(instant, initialization.InitializedAtUtc);
        if (missingProcessStart)
        {
            Assert.IsNull(process.ProcessStartedAtUtc);
        }
        else
        {
            Assert.AreEqual(new DateTimeOffset(2026, 10, 10, 7, 0, 0, TimeSpan.Zero), process.ProcessStartedAtUtc);
            Assert.AreNotEqual(initialization.InitializedAtUtc, process.ProcessStartedAtUtc);
        }
    }

    [TestMethod]
    public void ExhaustedIdSourcesDoNotFallBackToRandomGeneration()
    {
        var ids = new Queue<Guid>([FirstId]);
        var factory = new InitializationIdentityFactory(new InitializationTimeProvider(DateTimeOffset.UnixEpoch), ids.Dequeue);
        Assert.AreEqual(FirstId.ToString("D"), factory.Create().InstanceId);

        Assert.ThrowsExactly<InvalidOperationException>(() => factory.Create());
    }

    [TestMethod]
    public void EmptyGeneratedIdsAreRejectedBeforeReturningAnIdentity()
    {
        var factory = new InitializationIdentityFactory(new InitializationTimeProvider(DateTimeOffset.UnixEpoch), () => Guid.Empty);

        Assert.ThrowsExactly<InvalidOperationException>(() => factory.Create());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClockFailuresPropagateWithoutGeneratingAnotherId(bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new IOException();
        var clock = new InitializationTimeProvider(DateTimeOffset.UnixEpoch) { Failure = failure };
        int generated = 0;
        var factory = new InitializationIdentityFactory(clock, () =>
        {
            generated++;
            return FirstId;
        });

        Exception actual = Assert.Throws<Exception>(() => factory.Create());

        Assert.AreSame(failure, actual);
        Assert.AreEqual(1, generated);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IdSourceFailuresPropagateWithoutReadingTheClock(bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new IOException();
        var clock = new InitializationTimeProvider(DateTimeOffset.UnixEpoch);
        var factory = new InitializationIdentityFactory(clock, () => throw failure);

        Exception actual = Assert.Throws<Exception>(() => factory.Create());

        Assert.AreSame(failure, actual);
        Assert.AreEqual(0, clock.Reads);
    }

    [TestMethod]
    public void InitializationTimeIsCapturedAfterTheIdentityIsReady()
    {
        var clock = new InitializationTimeProvider(DateTimeOffset.UnixEpoch);
        var factory = new InitializationIdentityFactory(clock, () =>
        {
            clock.UtcNow = DateTimeOffset.UnixEpoch.AddMinutes(5);
            return FirstId;
        });

        InitializationIdentity identity = factory.Create();

        Assert.AreEqual(DateTimeOffset.UnixEpoch.AddMinutes(5), identity.InitializedAtUtc);
        Assert.AreEqual(1, clock.Reads);
    }

    [TestMethod]
    public void DefaultsUseNewUUIDsAndUTCInitializationTime()
    {
        var factory = new InitializationIdentityFactory();

        InitializationIdentity first = factory.Create();
        InitializationIdentity second = factory.Create();

        Assert.IsTrue(Guid.TryParseExact(first.InstanceId, "D", out Guid id));
        Assert.AreNotEqual(Guid.Empty, id);
        Assert.AreNotEqual(first.InstanceId, second.InstanceId);
        Assert.AreEqual(TimeSpan.Zero, first.InitializedAtUtc.Offset);
        Assert.AreEqual(TimeSpan.Zero, second.InitializedAtUtc.Offset);
    }
}
