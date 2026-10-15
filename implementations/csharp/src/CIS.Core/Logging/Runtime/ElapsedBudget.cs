namespace CIS.Logging.Runtime;
// Compare timestamp units directly; rounding elapsed time loses sub-millisecond budgets.
internal readonly record struct ElapsedBudget(long Started, TimeSpan Timeout, long Frequency)
{
    internal bool Contains(long timestamp) => (Int128)Math.Max(0, unchecked(timestamp - Started)) * TimeSpan.TicksPerSecond < (Int128)Timeout.Ticks * Frequency;
}
