using System.Collections.ObjectModel;

namespace CIS.Workflows;

/// <summary>Collects explicit mappings without constructing application handlers.</summary>
public sealed class StateMachineBuilder<TState, TTrigger, TContext>
    where TState : struct, Enum where TTrigger : struct, Enum where TContext : class
{
    private readonly StateMachineEnvironment environment;
    private readonly string name;
    private readonly TContext context;
    private readonly StateMachineOptions options;
    private readonly List<StateMachineMapping<TState, TTrigger>> mappings = [];

    internal StateMachineBuilder(StateMachineEnvironment environment, string name, TContext context, StateMachineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(context);
        PipelineContractRules.ValidateName(name, nameof(name));
        this.environment = environment;
        this.name = name;
        this.context = context;
        this.options = options ?? new StateMachineOptions();
    }

    /// <summary>Configures mappings from an explicitly declared source state.</summary>
    public StateMachineStateBuilder<TState, TTrigger, TContext> State(TState state)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentException("A configured source state must be declared.", nameof(state));
        }
        return new(this, state);
    }

    internal void Add(TState source, TTrigger trigger, TState target, StateMachineHandlerRegistration? handler)
    {
        mappings.Add(new(source, trigger, target, handler));
    }

    /// <summary>Validates and freezes this definition; terminal-only and self-transition definitions are supported.</summary>
    public IStateMachine<TState, TTrigger> Build(TState initialState)
    {
        StateMachineEnum<TState> states = StateMachineEnum<TState>.Create(triggerLabels: false);
        StateMachineEnum<TTrigger> triggers = StateMachineEnum<TTrigger>.Create(triggerLabels: true);
        if (!states.Labels.ContainsKey(initialState))
        {
            throw new ArgumentException("The initial state must be declared.", nameof(initialState));
        }
        var frozen = new Dictionary<(TState, TTrigger), StateMachineMapping<TState, TTrigger>>();
        foreach (StateMachineMapping<TState, TTrigger> mapping in mappings)
        {
            if (!states.Labels.ContainsKey(mapping.Source) || !states.Labels.ContainsKey(mapping.Target)
                || !triggers.Labels.ContainsKey(mapping.Trigger))
            {
                throw new ArgumentException("Every configured source, target and trigger must be declared.");
            }
            if (!frozen.TryAdd((mapping.Source, mapping.Trigger), mapping))
            {
                throw new ArgumentException("A source/trigger pair may be configured only once.");
            }
        }
        StateMachineSettings settings = StateMachineSettings.Capture(options);
        var permitted = new Dictionary<TState, IReadOnlyCollection<TTrigger>>();
        foreach (TState state in states.Labels.Keys)
        {
            permitted.Add(state, Array.AsReadOnly(mappings.Where(mapping => EqualityComparer<TState>.Default.Equals(mapping.Source, state))
                .Select(mapping => mapping.Trigger).ToArray()));
        }
        var metadata = new StateMachineDefinitionMetadata(name, Array.AsReadOnly(states.Labels.Values.ToArray()),
            Array.AsReadOnly(triggers.Labels.Values.Append("invalid").ToArray()),
            Array.AsReadOnly(mappings.Where(mapping => mapping.Handler is not null).Select(mapping => mapping.Handler!).ToArray()));
        environment.ValidateDefinition(metadata);
        var definition = new StateMachineDefinition<TState, TTrigger>(metadata,
            new ReadOnlyDictionary<(TState, TTrigger), StateMachineMapping<TState, TTrigger>>(frozen),
            new ReadOnlyDictionary<TState, IReadOnlyCollection<TTrigger>>(permitted), states.Labels, triggers.Labels);
        return new StateMachine<TState, TTrigger, TContext>(environment, definition, context, settings, initialState);
    }
}

/// <summary>Configures explicit triggers from one source state.</summary>
public sealed class StateMachineStateBuilder<TState, TTrigger, TContext>
    where TState : struct, Enum where TTrigger : struct, Enum where TContext : class
{
    private readonly StateMachineBuilder<TState, TTrigger, TContext> builder;
    private readonly TState source;
    internal StateMachineStateBuilder(StateMachineBuilder<TState, TTrigger, TContext> builder, TState source)
    {
        this.builder = builder;
        this.source = source;
    }
    /// <summary>Adds a direct transition without a handler or execution scope.</summary>
    public StateMachineStateBuilder<TState, TTrigger, TContext> On(TTrigger trigger, TState target)
    {
        builder.Add(source, trigger, target, null);
        return this;
    }
    /// <summary>Adds one fresh compatible handler, with an optional configured display/metric alias.</summary>
    public StateMachineStateBuilder<TState, TTrigger, TContext> On<THandler>(TTrigger trigger, TState target, string? alias = null)
        where THandler : class, IStateTransitionHandler<TState, TTrigger, TContext>
    {
        Type type = typeof(THandler);
        if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
        {
            throw new ArgumentException("A transition handler must be a closed concrete compatible type.", nameof(THandler));
        }
        string display = alias ?? type.FullName ?? type.Name;
        PipelineContractRules.ValidateName(display, nameof(alias));
        builder.Add(source, trigger, target, new(display, alias ?? "handler", type));
        return this;
    }
}

internal sealed record StateMachineMapping<TState, TTrigger>(TState Source, TTrigger Trigger, TState Target,
    StateMachineHandlerRegistration? Handler) where TState : struct, Enum where TTrigger : struct, Enum;

internal sealed record StateMachineDefinition<TState, TTrigger>(StateMachineDefinitionMetadata Metadata,
    IReadOnlyDictionary<(TState, TTrigger), StateMachineMapping<TState, TTrigger>> Mappings,
    IReadOnlyDictionary<TState, IReadOnlyCollection<TTrigger>> Permitted,
    IReadOnlyDictionary<TState, string> StateLabels, IReadOnlyDictionary<TTrigger, string> TriggerLabels)
    where TState : struct, Enum where TTrigger : struct, Enum;

internal sealed record StateMachineSettings(TimeSpan ErrorHandlingTimeout, bool LogCalls, bool LogRoutineAsInformation)
{
    internal static StateMachineSettings Capture(StateMachineOptions options)
    {
        TimeSpan timeout = options.ErrorHandlingTimeout;
        if (timeout != Timeout.InfiniteTimeSpan && (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMilliseconds(uint.MaxValue - 1)))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The compensation budget must fit the native timer range, or be infinite.");
        }
        return new(timeout, options.LogCalls, options.LogRoutineAsInformation);
    }
}

internal sealed class StateMachineEnum<TEnum> where TEnum : struct, Enum
{
    internal IReadOnlyDictionary<TEnum, string> Labels
    {
        get;
    }
    private StateMachineEnum(Dictionary<TEnum, string> labels)
    {
        Labels = new ReadOnlyDictionary<TEnum, string>(labels);
    }
    internal static StateMachineEnum<TEnum> Create(bool triggerLabels)
    {
        Type type = typeof(TEnum);
        Type underlying = Enum.GetUnderlyingType(type);
        if (type.IsDefined(typeof(FlagsAttribute), inherit: false)
            || underlying != typeof(byte) && underlying != typeof(sbyte) && underlying != typeof(short)
                && underlying != typeof(ushort) && underlying != typeof(int) && underlying != typeof(uint)
                && underlying != typeof(long) && underlying != typeof(ulong))
        {
            throw new ArgumentException("State and trigger types must be ordinary non-Flags C# enums.");
        }
        var labels = new Dictionary<TEnum, string>();
        foreach (string name in Enum.GetNames<TEnum>())
        {
            PipelineContractRules.ValidateName(name, nameof(TEnum));
            TEnum value = Enum.Parse<TEnum>(name);
            string label = triggerLabels && name == "invalid" ? "enum:invalid" : name;
            if (labels.TryGetValue(value, out string? previous))
            {
                throw new ArgumentException($"Enum aliases '{previous}' and '{name}' share an underlying value.");
            }
            labels.Add(value, label);
        }
        return new(labels);
    }
}
