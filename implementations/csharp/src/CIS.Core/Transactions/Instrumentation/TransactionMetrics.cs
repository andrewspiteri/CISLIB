using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Transactions;

// One reusable bundle is published atomically; each begin captures it before acquisition.
internal sealed class TransactionMetrics
{
    private readonly ITimer duration;
    private readonly IUpDownCounter active;
    private readonly ICounter failures;

    internal TransactionMetrics(IMetrics metrics)
    {
        duration = metrics.CreateTimer(new("cis.transaction.duration", MetricUnit.Seconds,
            "Published local transaction through actual owned cleanup.", ["database", "outcome"]));
        active = metrics.CreateUpDownCounter(new("cis.transaction.active",
            description: "Published local transactions whose owned cleanup has not settled.", allowedDimensionKeys: ["database"]));
        failures = metrics.CreateCounter(new("cis.transaction.failures",
            description: "Failed native local transaction invocations.", allowedDimensionKeys: ["database", "phase"]));
    }

    internal void Published(string database)
    {
        TransactionObservation.Attempt(() => active.Add(1, new(("database", database))));
    }

    internal void NativeFailure(string database, TransactionNativePhase phase)
    {
        string value = phase switch
        {
            TransactionNativePhase.Begin => "begin",
            TransactionNativePhase.Commit => "commit",
            TransactionNativePhase.Rollback => "rollback",
            TransactionNativePhase.Dispose => "dispose",
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
        TransactionObservation.Attempt(() => failures.Add(1, new(("database", database), ("phase", value))));
    }

    internal void Completed(string database, TransactionCompletion completion)
    {
        if (completion.DurationSeconds is { } seconds)
        {
            string outcome = completion.Outcome switch
            {
                TransactionOutcome.Committed => "committed",
                TransactionOutcome.NotCommitted => "not_committed",
                TransactionOutcome.Unknown => "unknown",
                _ => throw new ArgumentOutOfRangeException(nameof(completion))
            };
            TransactionObservation.Attempt(() => duration.Record(seconds, new(("database", database), ("outcome", outcome))));
        }

        // A failed duration observation cannot suppress this matching decrement attempt.
        TransactionObservation.Attempt(() => active.Add(-1, new(("database", database))));
    }
}
