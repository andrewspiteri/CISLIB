using System.Globalization;
using System.Text;

namespace CIS.Workflows;

/// <summary>Collects an ordered definition without constructing steps or acquiring a scope.</summary>
/// <typeparam name="TContext">The shared application-owned context type.</typeparam>
public sealed class PipelineBuilder<TContext> where TContext : class
{
    private readonly PipelineEnvironment environment;
    private readonly string name;
    private readonly TContext context;
    private readonly PipelineOptions options;
    private readonly List<PipelineRegistration> registrations = [];

    internal PipelineBuilder(PipelineEnvironment environment, string name, TContext context, PipelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(context);
        PipelineContractRules.ValidateName(name, nameof(name));
        this.environment = environment;
        this.name = name;
        this.context = context;
        this.options = options ?? new PipelineOptions();
    }

    /// <summary>Appends a distinct fresh registration; an explicit alias also supplies its bounded metric name.</summary>
    public PipelineBuilder<TContext> Add<TStep>(string? alias = null) where TStep : class, IPipelineStep<TContext>
    {
        Type type = typeof(TStep);
        if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
        {
            throw new ArgumentException("A pipeline registration requires a closed concrete step type.", nameof(TStep));
        }

        string displayName = alias ?? type.FullName ?? type.Name;
        PipelineContractRules.ValidateName(displayName, nameof(alias));
        registrations.Add(new(registrations.Count, displayName, alias ?? "step", type));
        return this;
    }

    /// <summary>Snapshots this definition and options; later builder changes do not affect the returned instance.</summary>
    public IPipeline<TContext> Build()
    {
        if (registrations.Count == 0)
        {
            throw new InvalidOperationException("A pipeline must contain at least one step.");
        }

        PipelineSettings settings = PipelineSettings.Capture(options);
        IReadOnlyList<PipelineRegistration> snapshot = Array.AsReadOnly(registrations.ToArray());
        environment.ValidateDefinition(name, snapshot);
        return new Pipeline<TContext>(environment, name, context, settings, snapshot);
    }
}

internal sealed record PipelineSettings(TimeSpan ErrorHandlingTimeout, bool LogCalls)
{
    internal static PipelineSettings Capture(PipelineOptions options)
    {
        TimeSpan timeout = options.ErrorHandlingTimeout;
        if (timeout != Timeout.InfiniteTimeSpan
            && (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMilliseconds(uint.MaxValue - 1)))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The cleanup budget must be positive within the native timer range, or infinite.");
        }

        return new(timeout, options.LogCalls);
    }
}

internal static class PipelineContractRules
{
    private static readonly UTF8Encoding strictUtf8 = new(false, true);

    internal static void ValidateName(string name, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameterName);
        if (strictUtf8.GetByteCount(name) > 256 || name.Any(character =>
            char.GetUnicodeCategory(character) is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
        {
            throw new ArgumentException("A pipeline name or alias must be printable and at most 256 UTF-8 bytes.", parameterName);
        }
    }
}
