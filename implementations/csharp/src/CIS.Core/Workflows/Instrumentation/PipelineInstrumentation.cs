using System.Globalization;
using System.Text;
using CIS.Metrics;
using CIS.Text;
using Microsoft.Extensions.Logging;

namespace CIS.Workflows;

// Bounded definition metadata and one optional host binding, never execution history.
internal sealed class PipelineInstrumentation : IMetricsSource
{
    internal const int MaximumPipelineNames = 16;
    internal const int MaximumStepPairs = 64;
    private readonly object gate = new();
    private readonly HashSet<string> names = new(StringComparer.Ordinal);
    private readonly HashSet<(string Pipeline, string Step)> pairs = [];
    private readonly MetricsAttachment attachment = new();
    private readonly ILogger? logger;
    private PipelineMetrics? metrics;

    public PipelineInstrumentation(ILoggerFactory? loggerFactory = null)
    {
        try
        {
            logger = loggerFactory?.CreateLogger("CIS.Workflows.Pipeline");
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
    }

    internal void ValidateDefinition(string pipelineName, IReadOnlyList<PipelineRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ValidateName(pipelineName);
        if (registrations.Count == 0)
        {
            throw new ArgumentException("A pipeline requires at least one registered step.", nameof(registrations));
        }

        var candidate = new HashSet<(string Pipeline, string Step)>();
        foreach (PipelineRegistration registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ValidateName(registration.MetricAlias);
            candidate.Add((pipelineName, registration.MetricAlias));
            if (candidate.Count > MaximumStepPairs)
            {
                throw new ArgumentException("The pipeline definition exceeds the finite metric-step alias profile.", nameof(registrations));
            }
        }

        lock (gate)
        {
            int additionalName = names.Contains(pipelineName) ? 0 : 1;
            int additionalPairs = candidate.Count(pair => !pairs.Contains(pair));
            if (names.Count + additionalName > MaximumPipelineNames || pairs.Count + additionalPairs > MaximumStepPairs)
            {
                throw new InvalidOperationException("The workflow factory's finite pipeline-name or metric-step alias capacity is exhausted.");
            }

            // All validation and capacity checks complete before either retained set changes.
            names.Add(pipelineName);
            pairs.UnionWith(candidate);
        }
    }

    internal IPipelineObservation Begin(PipelineRunInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new PipelineObservation(info, logger, Volatile.Read(ref metrics));
    }

    public void AttachMetrics(MetricsRuntime runtime)
    {
        if (attachment.Attach(runtime))
        {
            Volatile.Write(ref metrics, new PipelineMetrics(runtime));
        }
    }

    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !UnicodeScalarText.IsValid(value, 256, allowControls: false)
            || Encoding.UTF8.GetByteCount(value) > 256 || HasNonprintingScalar(value))
        {
            throw new ArgumentException("Use nonempty printable pipeline names and metric-step aliases of at most 256 UTF-8 bytes.", nameof(value));
        }
    }

    private static bool HasNonprintingScalar(string value)
    {
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                return true;
            }
        }

        return false;
    }
}
