using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportBudgetedFrameTests
{
    private const int MaximumBytes = 4096;

    [TestMethod]
    public async Task CompletedFormattingBeforeTheDeadlineWritesTheCurrentFrame()
    {
        var clock = new DelayedTimerClock();
        using var budget = new HealthPipeBudget(clock, 50, CancellationToken.None);
        HealthReportBuffer response = FormatCurrentResponse();
        clock.Timestamp = 49;
        using var stream = new MemoryStream();

        await budget.WriteFrameAsync(stream, response.Utf8Bytes, MaximumBytes);

        Assert.IsFalse(budget.Token.IsCancellationRequested);
        Assert.AreEqual((long)response.Utf8Bytes.Length + 4, stream.Length);
        stream.Position = 0;
        byte[] payload = await HealthReportProtocol.ReadFrameAsync(stream, MaximumBytes, CancellationToken.None);
        CollectionAssert.AreEqual(response.Utf8Bytes.ToArray(), payload);
        HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(payload, HealthReportView.Health, MaximumBytes);
        Assert.IsFalse(parsed.IsError);
        Assert.IsTrue(parsed.SelectedHealthy);
    }

    [TestMethod]
    [DataRow(50)]
    [DataRow(51)]
    public async Task CompletedFormattingCannotCommitAnExpiredResponse(int elapsedMilliseconds)
    {
        var clock = new DelayedTimerClock();
        using var budget = new HealthPipeBudget(clock, 50, CancellationToken.None);
        HealthReportBuffer response = FormatCurrentResponse();
        // Formatting has genuinely completed. Advancing elapsed time leaves the
        // timer callback delayed, so only the commit boundary can reject it.
        clock.Timestamp = elapsedMilliseconds;
        Assert.IsFalse(budget.Token.IsCancellationRequested);
        using var stream = new MemoryStream();

        OperationCanceledException failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await budget.WriteFrameAsync(stream, response.Utf8Bytes, MaximumBytes));

        Assert.AreEqual(budget.Token, failure.CancellationToken);
        Assert.IsFalse(budget.Token.IsCancellationRequested);
        Assert.AreEqual(0L, stream.Length, "An expired response must not commit even its framing prefix.");
    }

    private static HealthReportBuffer FormatCurrentResponse()
    {
        HealthReportBuffer response = HealthReportFormatter.FormatPipeResponse(
            HealthReportFormatterTests.PendingSnapshot(), HealthReportView.Health, new HealthcheckOptions { MaxReportBytes = MaximumBytes });
        Assert.IsFalse(response.IsError);
        return response;
    }

    // Elapsed monotonic time can cross the deadline while timer dispatch is delayed.
    private sealed class DelayedTimerClock : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new DelayedTimer();

        private sealed class DelayedTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
