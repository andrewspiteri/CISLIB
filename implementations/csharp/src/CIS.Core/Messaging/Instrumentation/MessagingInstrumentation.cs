using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Messaging;

// One exact host binding and reusable handle bundle, never a registry of running calls.
internal sealed class MessagingInstrumentation
{
    private readonly MessagingSnapshot snapshot;
    private readonly TimeProvider clock;
    private readonly MetricsAttachment attachment = new();
    private MessagingMetrics? metrics;
    private BacklogSnapshot? backlog;

    internal MessagingInstrumentation(MessagingSnapshot snapshot, TimeProvider? timeProvider = null,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
    {
        this.snapshot = snapshot;
        clock = timeProvider ?? TimeProvider.System;
    }

    internal object RegistrationIdentity => snapshot.RegistrationIdentity;

    internal void AttachMetrics(MetricsRuntime runtime)
    {
        if (attachment.Attach(runtime))
        {
            Volatile.Write(ref metrics, new MessagingMetrics(runtime, ObservePending, ObserveAge));
        }
    }

    internal MessageObservation Publish(string routeAlias) => Capture("publish", routeAlias,
        snapshot.Routes.Any(item => item.Alias == routeAlias));

    internal MessageObservation Delivery(string subscriptionAlias) => Capture("consume", subscriptionAlias,
        snapshot.Subscriptions.Any(item => item.Alias == subscriptionAlias));

    internal MessageObservation Rpc(string requestAlias) => Capture("rpc", requestAlias,
        snapshot.Requests.Any(item => item.Alias == requestAlias));

    private MessageObservation Capture(string kind, string alias, bool registered)
        => new(registered ? Volatile.Read(ref metrics) : null, clock, kind, alias);

    internal void Disposition(string subscriptionAlias, string disposition)
    {
        if (snapshot.Subscriptions.Any(item => item.Alias == subscriptionAlias)
            && disposition is "retry" or "terminal" or "duplicate")
        {
            Attempt(() => Volatile.Read(ref metrics)?.Disposition(subscriptionAlias, disposition));
        }
    }

    internal void Observe(MessageStoreObservation? observation)
    {
        BacklogSnapshot? captured = null;
        if (observation is not null)
        {
            try
            {
                captured = new(observation, clock.GetTimestamp());
            }
            catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
            {
            }
        }
        Volatile.Write(ref backlog, captured);
    }

    private IReadOnlyList<MetricObservation> ObservePending()
    {
        MessageStoreObservation? current = CurrentBacklog();
        return current is { IsComplete: true, PendingCount: >= 0 } ?
            [new(current.PendingCount.Value, new(("store", snapshot.Settings.StoreAlias)))] : [];
    }

    private IReadOnlyList<MetricObservation> ObserveAge()
    {
        double? age = CurrentBacklog()?.OldestAgeSeconds;
        return age is { } value && double.IsFinite(value) && value >= 0 ?
            [new(value, new(("store", snapshot.Settings.StoreAlias)))] : [];
    }

    private MessageStoreObservation? CurrentBacklog()
    {
        BacklogSnapshot? current = Volatile.Read(ref backlog);
        if (current is null)
        {
            return null;
        }
        double seconds = clock.GetElapsedTime(current.Timestamp, clock.GetTimestamp()).TotalSeconds;
        double freshness = Math.Max(5, snapshot.Settings.IdlePollInterval.TotalSeconds * 3);
        return seconds >= 0 && seconds <= freshness ? current.Value : null;
    }

    internal static void Attempt(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
            // Optional observation never replaces business, broker or cleanup evidence.
        }
    }

    private sealed record BacklogSnapshot(MessageStoreObservation Value, long Timestamp);
}

internal sealed class MessagingMetrics
{
    private readonly ITimer publish;
    private readonly ITimer delivery;
    private readonly ITimer rpc;
    private readonly IUpDownCounter active;
    private readonly ICounter dispositions;

    internal MessagingMetrics(IMetrics metrics, Func<IReadOnlyList<MetricObservation>> pending,
        Func<IReadOnlyList<MetricObservation>> age)
    {
        publish = metrics.CreateTimer(new("cis.messaging.publish.duration", MetricUnit.Seconds,
            "One admitted native publication through its actual settlement.", ["route", "outcome"]));
        delivery = metrics.CreateTimer(new("cis.messaging.delivery.duration", MetricUnit.Seconds,
            "One admitted consumer attempt through transaction, disposition and native settlement.", ["subscription", "outcome"]));
        rpc = metrics.CreateTimer(new("cis.messaging.rpc.duration", MetricUnit.Seconds,
            "One admitted typed request through its final local settlement.", ["request", "outcome"]));
        active = metrics.CreateUpDownCounter(new("cis.messaging.active", description: "Admitted messaging work not yet settled.",
            allowedDimensionKeys: ["kind"]));
        dispositions = metrics.CreateCounter(new("cis.messaging.dispositions", description: "Known committed delivery responsibility dispositions.",
            allowedDimensionKeys: ["subscription", "disposition"]));
        metrics.RegisterObservableGauge(new("cis.messaging.outbox.pending", description: "Last fresh complete bounded outbox responsibility count.",
            allowedDimensionKeys: ["store"]), pending);
        metrics.RegisterObservableGauge(new("cis.messaging.outbox.oldest.age", MetricUnit.Seconds,
            "Last fresh bounded oldest eligible responsibility age.", ["store"]), age);
    }

    internal void Active(string kind, long amount) => active.Add(amount, new(("kind", kind)));
    internal void Disposition(string alias, string outcome) => dispositions.Add(1, new(("subscription", alias), ("disposition", outcome)));

    internal void Record(string kind, string alias, string outcome, double seconds)
    {
        bool known = kind switch
        {
            "publish" => outcome is "confirmed" or "rejected" or "unknown" or "cancelled" or "failed",
            "consume" => outcome is "handled" or "duplicate" or "retry" or "terminal" or "unknown" or "cancelled" or "failed",
            "rpc" => outcome is "response_received" or "timed_out" or "cancelled" or "not_submitted" or "submission_unknown"
                or "submission_not_confirmed" or "unavailable" or "overloaded" or "invalid_response" or "failed",
            _ => false
        };
        if (!known)
        {
            return;
        }
        ITimer timer = kind == "publish" ? publish : kind == "consume" ? delivery : rpc;
        string key = kind == "publish" ? "route" : kind == "consume" ? "subscription" : "request";
        timer.Record(seconds, new((key, alias), ("outcome", outcome)));
    }
}

internal sealed class MessageObservation : IDisposable
{
    private readonly MessagingMetrics? metrics;
    private readonly TimeProvider clock;
    private readonly string kind;
    private readonly string alias;
    private readonly long? started;
    private int completed;

    internal MessageObservation(MessagingMetrics? metrics, TimeProvider clock, string kind, string alias)
    {
        this.metrics = metrics;
        this.clock = clock;
        this.kind = kind;
        this.alias = alias;
        try
        {
            started = clock.GetTimestamp();
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
        MessagingInstrumentation.Attempt(() => metrics?.Active(kind, 1));
    }

    internal void Complete(string outcome)
    {
        if (Interlocked.Exchange(ref completed, 1) != 0)
        {
            return;
        }
        try
        {
            if (started is { } beginning)
            {
                MessagingInstrumentation.Attempt(() => metrics?.Record(kind, alias, outcome,
                    clock.GetElapsedTime(beginning, clock.GetTimestamp()).TotalSeconds));
            }
        }
        finally
        {
            MessagingInstrumentation.Attempt(() => metrics?.Active(kind, -1));
        }
    }

    public void Dispose() => Complete("failed");
}
