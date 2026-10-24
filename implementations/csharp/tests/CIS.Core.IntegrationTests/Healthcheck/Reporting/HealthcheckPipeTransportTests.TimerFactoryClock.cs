namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private sealed class FailingTimerClock : TimeProvider
    {
        private int attempts;
        internal int Attempts => Volatile.Read(ref attempts);
        internal TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref attempts);
            Attempted.TrySetResult();
            throw new InvalidOperationException("actual-test-clock-timer-failure");
        }
    }
}
