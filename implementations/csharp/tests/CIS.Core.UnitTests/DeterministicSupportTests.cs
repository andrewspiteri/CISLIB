using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class DeterministicSupportTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestMethod]
    public void ClockSeparatesWallClockCorrectionsFromMonotonicTime()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        long start = clock.GetTimestamp();
        clock.Advance(TimeSpan.FromSeconds(3));
        clock.SetUtcNow(clock.GetUtcNow() - TimeSpan.FromHours(1));

        Assert.AreEqual(TimeSpan.FromSeconds(3), clock.GetElapsedTime(start));
        Assert.AreEqual(DateTimeOffset.Parse("2025-12-31T23:00:03Z", System.Globalization.CultureInfo.InvariantCulture), clock.GetUtcNow());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromTicks(-1)));
    }

    [TestMethod]
    public async Task ControlledClockCompletesDelayOnlyAtItsDueTime()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        Task delay = Task.Delay(TimeSpan.FromSeconds(2), clock);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsFalse(delay.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        await delay.WaitAsync(Deadline);
    }

    [TestMethod]
    public void TimerChangeAndDisposalControlPeriodicCallbacks()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var observed = new List<long>();
        using ITimer timer = clock.CreateTimer(_ => observed.Add(clock.GetTimestamp()), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(3));
        CollectionAssert.AreEqual(new[] { 10_000_000L, 20_000_000L, 30_000_000L }, observed);
        Assert.IsTrue(timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.AreEqual(3, observed.Count);
        timer.Dispose();
        Assert.IsFalse(timer.Change(TimeSpan.Zero, TimeSpan.Zero));
    }

    [TestMethod]
    public void ExplicitMetadataAndIdsAreStableAndExhaustionIsVisible()
    {
        var metadata = new TestMetadata("fixture-app", "1.2.3+fixture", "fixture-env", "fixture-host", "fixture-runtime");
        Guid first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var values = new List<Guid> { first, second };
        var inputs = new ExplicitTestInputs(metadata, values);
        values.Clear();

        Assert.AreEqual(metadata, inputs.Metadata);
        Assert.AreEqual(first, inputs.NextInstanceId());
        Assert.AreEqual(second, inputs.NextInstanceId());
        Assert.ThrowsExactly<InvalidOperationException>(() => inputs.NextInstanceId());
    }

    [TestMethod]
    public async Task BarrierRequiresEveryParticipantAndRejectsExtraArrivals()
    {
        var barrier = new AsyncBarrier(2);
        Task first = barrier.SignalAndWaitAsync(Deadline);
        Assert.IsFalse(first.IsCompleted);
        Task second = barrier.SignalAndWaitAsync(Deadline);
        await Task.WhenAll(first, second).WaitAsync(Deadline);
        Assert.ThrowsExactly<InvalidOperationException>(() => barrier.SignalAndWaitAsync(Deadline));
    }

    [TestMethod]
    public async Task WriterSignalsEntryAndPublishesOnlyAfterRelease()
    {
        using var writer = new ControlledTextWriter(Deadline);
        Task write = writer.WriteAsync("captured record");
        await writer.Entered.WaitAsync(Deadline);
        Assert.IsFalse(write.IsCompleted);
        Assert.AreEqual(0, writer.Writes.Count);
        writer.Release();
        await write.WaitAsync(Deadline);
        CollectionAssert.AreEqual(new[] { "captured record" }, writer.Writes.ToArray());
    }

    [TestMethod]
    public async Task WriterFailureIsObservedWithoutPublishingTheRecord()
    {
        using var writer = new ControlledTextWriter(Deadline);
        Task write = writer.WriteAsync("failed record");
        await writer.Entered.WaitAsync(Deadline);
        writer.Fail(new IOException("controlled failure"));
        await Assert.ThrowsExactlyAsync<IOException>(() => write.WaitAsync(Deadline));
        Assert.AreEqual(0, writer.Writes.Count);
    }

    [TestMethod]
    public async Task ChildEnvironmentIsIsolatedFromTheParent()
    {
        const string name = "CIS_TEST_CHILD_ENVIRONMENT";
        string? parentValue = Environment.GetEnvironmentVariable(name);
        using var directory = new TemporaryDirectory();
        ProcessResult result = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-Command", "[Console]::Out.Write($env:CIS_TEST_CHILD_ENVIRONMENT)"], directory.Path, Deadline, new Dictionary<string, string?> { [name] = "child-only" });

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("child-only", result.StandardOutput);
        Assert.AreEqual(parentValue, Environment.GetEnvironmentVariable(name));
    }

    [TestMethod]
    public async Task HungOwnedChildIsStoppedAtTheFiniteDeadline()
    {
        using var directory = new TemporaryDirectory();
        await Assert.ThrowsExactlyAsync<ProcessTimeoutException>(() => ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-Command", "[System.Threading.ManualResetEvent]::new($false).WaitOne()"], directory.Path, TimeSpan.FromMilliseconds(500)));
    }
}
