using CIS.Metrics;
using CIS.Operations.Hosting;
using Microsoft.Extensions.Logging;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Operations;

// Passive host identity and one atomically published optional bundle, never an attempt registry.
internal sealed class OperationInstrumentation
{
    private readonly MetricsAttachment attachment = new();
    private readonly TimeProvider clock;
    private readonly ILogger? logger;
    private OperationMetrics? metrics;

    public OperationInstrumentation(OperationsComposition? composition = null, TimeProvider? clock = null,
        ILoggerFactory? loggerFactory = null)
    {
        RegistrationIdentity = composition?.RegistrationIdentity;
        this.clock = clock ?? TimeProvider.System;
        try
        {
            logger = loggerFactory?.CreateLogger("CIS.Operations");
        }
        catch (Exception error) when (OperationObservation.IsOrdinary(error))
        {
        }
    }

    internal object? RegistrationIdentity
    {
        get;
    }

    internal OperationObservation Capture(string name, bool lookup, bool logCalls)
        => new(name, lookup, logCalls, clock, logger, Volatile.Read(ref metrics));

    internal void AttachMetrics(MetricsRuntime runtime)
    {
        if (attachment.Attach(runtime))
        {
            Volatile.Write(ref metrics, new OperationMetrics(runtime));
        }
    }
}

internal sealed class OperationMetrics
{
    private readonly ITimer execute;
    private readonly ITimer lookup;

    internal OperationMetrics(IMetrics metrics)
    {
        execute = metrics.CreateTimer(new("cis.operation.execute.duration", MetricUnit.Seconds,
            "One durable attempt through method-owned settlement; Prepared is not committed.", ["operation", "outcome"]));
        lookup = metrics.CreateTimer(new("cis.operation.lookup.duration", MetricUnit.Seconds,
            "One authoritative result lookup through its read-owner cleanup.", ["operation", "outcome"]));
    }

    internal void Record(bool isLookup, string operation, string outcome, double seconds)
        => (isLookup ? lookup : execute).Record(seconds, new(("operation", operation), ("outcome", outcome)));
}

// Only immutable finite metadata, a captured bundle and operation-local timing flags are retained.
internal sealed class OperationObservation
{
    private static readonly EventId Finished = new(16000, "operation.completed");
    private readonly string name;
    private readonly bool lookup;
    private readonly bool logCalls;
    private readonly TimeProvider clock;
    private readonly ILogger? logger;
    private readonly OperationMetrics? metrics;
    private readonly long? started;
    private int completed;

    internal OperationObservation(string name, bool lookup, bool logCalls, TimeProvider clock,
        ILogger? logger, OperationMetrics? metrics)
    {
        this.name = name;
        this.lookup = lookup;
        this.logCalls = logCalls;
        this.clock = clock;
        this.logger = logger;
        this.metrics = metrics;
        try
        {
            started = clock.GetTimestamp();
        }
        catch (Exception error) when (IsOrdinary(error))
        {
        }
    }

    internal void Complete(string outcome)
    {
        if (Interlocked.Exchange(ref completed, 1) != 0)
        {
            return;
        }

        double? seconds = Duration();
        if (seconds is { } elapsed)
        {
            Attempt(() => metrics?.Record(lookup, name, outcome, elapsed));
        }

        bool failure = outcome is "failed" or "rollback_only";
        if (logger is not null && (failure || logCalls))
        {
            LogLevel level = failure ? LogLevel.Warning : LogLevel.Debug;
            Attempt(() =>
            {
                if (logger.IsEnabled(level))
                {
                    logger.Log(level, Finished,
                        "Durable operation {Operation} {Method} completed {Outcome} in {DurationSeconds} seconds.",
                        name, lookup ? "lookup" : "execute", outcome, seconds);
                }
            });
        }
    }

    private double? Duration()
    {
        if (started is not { } origin)
        {
            return null;
        }

        try
        {
            double seconds = clock.GetElapsedTime(origin, clock.GetTimestamp()).TotalSeconds;
            return double.IsFinite(seconds) && seconds >= 0 ? seconds : null;
        }
        catch (Exception error) when (IsOrdinary(error))
        {
            return null;
        }
    }

    internal static void Attempt(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (IsOrdinary(error))
        {
            // Optional diagnostics cannot replace business, authorization, cancellation or cleanup facts.
        }
    }

    internal static bool IsOrdinary(Exception error) => error is not OutOfMemoryException
        and not StackOverflowException and not AccessViolationException;
}
