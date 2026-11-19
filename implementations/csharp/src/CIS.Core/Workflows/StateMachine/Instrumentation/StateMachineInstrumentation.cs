using CIS.Metrics;
using Microsoft.Extensions.Logging;

namespace CIS.Workflows;

// Only finite label metadata is retained here, never enum types, contexts or machine instances.
internal sealed class StateMachineInstrumentation : IMetricsSource
{
    internal const int MaximumMachineNames = 16;
    internal const int MaximumSourceTriggerTuples = 512;
    internal const int MaximumHandlerPairs = 64;
    private readonly object gate = new();
    private readonly HashSet<string> names = new(StringComparer.Ordinal);
    private readonly HashSet<(string Machine, string Source, string Trigger)> tuples = [];
    private readonly HashSet<(string Machine, string Handler)> handlers = [];
    private readonly MetricsAttachment attachment = new();
    private readonly ILogger? logger;
    private StateMachineMetrics? metrics;

    public StateMachineInstrumentation(ILoggerFactory? loggerFactory = null)
    {
        try
        {
            logger = loggerFactory?.CreateLogger("CIS.Workflows.StateMachine");
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
    }

    internal void ValidateDefinition(StateMachineDefinitionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        PipelineContractRules.ValidateName(metadata.Name, nameof(metadata));
        if (metadata.States.Count is < 1 or > MaximumSourceTriggerTuples
            || metadata.Triggers.Count is < 1 or > MaximumSourceTriggerTuples
            || !metadata.Triggers.Contains("invalid", StringComparer.Ordinal))
        {
            throw new ArgumentException("State-machine metadata requires a finite declared source/trigger domain and its invalid category.", nameof(metadata));
        }

        var candidate = new HashSet<(string Machine, string Source, string Trigger)>();
        foreach (string source in metadata.States)
        {
            PipelineContractRules.ValidateName(source, nameof(metadata));
            foreach (string trigger in metadata.Triggers)
            {
                PipelineContractRules.ValidateName(trigger, nameof(metadata));
                candidate.Add((metadata.Name, source, trigger));
                if (candidate.Count > MaximumSourceTriggerTuples)
                {
                    throw new ArgumentException("The declared state/trigger domain exceeds the finite state-machine metric profile.", nameof(metadata));
                }
            }
        }

        var candidateHandlers = new HashSet<(string Machine, string Handler)>();
        foreach (StateMachineHandlerRegistration handler in metadata.Handlers)
        {
            PipelineContractRules.ValidateName(handler.MetricAlias, nameof(metadata));
            candidateHandlers.Add((metadata.Name, handler.MetricAlias));
            if (candidateHandlers.Count > MaximumHandlerPairs)
            {
                throw new ArgumentException("The definition exceeds the finite handler-alias profile.", nameof(metadata));
            }
        }

        lock (gate)
        {
            int additionalName = names.Contains(metadata.Name) ? 0 : 1;
            int additionalTuples = candidate.Count(tuple => !tuples.Contains(tuple));
            int additionalHandlers = candidateHandlers.Count(pair => !handlers.Contains(pair));
            if (names.Count + additionalName > MaximumMachineNames
                || tuples.Count + additionalTuples > MaximumSourceTriggerTuples
                || handlers.Count + additionalHandlers > MaximumHandlerPairs)
            {
                throw new InvalidOperationException("The workflow owner's finite state-machine metadata capacity is exhausted.");
            }

            names.Add(metadata.Name);
            tuples.UnionWith(candidate);
            handlers.UnionWith(candidateHandlers);
        }
    }

    internal IStateMachineObservation Begin(StateMachineCallInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new StateMachineObservation(info, logger, Volatile.Read(ref metrics));
    }

    public void AttachMetrics(MetricsRuntime runtime)
    {
        if (attachment.Attach(runtime))
        {
            Volatile.Write(ref metrics, new StateMachineMetrics(runtime));
        }
    }
}
