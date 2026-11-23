using CIS.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Jobs;

/// <summary>The C# binding for one exact portable job definition, independent of discovery and category labels.</summary>
public sealed class JobHandlerBinding
{
    private readonly Func<JobExecutionContext, CancellationToken, Task<ContinuousIterationResult>>? continuous;
    private readonly Func<JobExecutionContext, CancellationToken, Task<ScheduledJobResult>>? scheduled;

    private JobHandlerBinding(JobDefinition definition, Type handlerType,
        Func<JobExecutionContext, CancellationToken, Task<ContinuousIterationResult>>? continuous,
        Func<JobExecutionContext, CancellationToken, Task<ScheduledJobResult>>? scheduled, MessageContract? inputContract)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (handlerType.IsAbstract || handlerType.ContainsGenericParameters)
        {
            throw new ArgumentException("Bind a concrete closed handler class.", nameof(handlerType));
        }

        if (definition.SupportedModes.Contains(JobExecutionMode.Continuous) != (continuous is not null)
            || definition.SupportedModes.Contains(JobExecutionMode.Scheduled) != (scheduled is not null))
        {
            throw new ArgumentException("Every advertised mode requires an invokable binding; use an explicit combined binding for both modes.", nameof(definition));
        }

        Definition = definition;
        HandlerType = handlerType;
        InputContract = inputContract;
        this.continuous = continuous;
        this.scheduled = scheduled;
    }

    /// <summary>Creates metadata for one finite-iteration handler, without constructing it.</summary>
    public static JobHandlerBinding Continuous<THandler>(JobDefinition definition)
        where THandler : class, IContinuousJobHandler
        => new(definition, typeof(THandler), (context, token) =>
            context.Services.GetRequiredService<THandler>().ExecuteAsync(context, token), null, null);

    /// <summary>Captures the existing typed payload codec and exact scheduled handler without resolving either from DI.</summary>
    public static JobHandlerBinding Scheduled<T, THandler>(JobDefinition definition, IMessageCodec<T> codec)
        where THandler : class, IScheduledJobHandler<T>
    {
        ArgumentNullException.ThrowIfNull(codec);
        MessageContract contract = codec.Contract ?? throw new ArgumentException("The codec requires a portable contract.", nameof(codec));
        return new(definition, typeof(THandler), null,
            (context, token) => DecodeAndInvokeAsync<T, THandler>(context, codec, contract, token), contract);
    }

    /// <summary>Binds both implemented ports explicitly; duplicate registrations are never silently merged.</summary>
    public static JobHandlerBinding Combined<T, THandler>(JobDefinition definition, IMessageCodec<T> codec)
        where THandler : class, IContinuousJobHandler, IScheduledJobHandler<T>
    {
        ArgumentNullException.ThrowIfNull(codec);
        MessageContract contract = codec.Contract ?? throw new ArgumentException("The codec requires a portable contract.", nameof(codec));
        return new(definition, typeof(THandler), (context, token) =>
            context.Services.GetRequiredService<THandler>().ExecuteAsync(context, token),
            (context, token) => DecodeAndInvokeAsync<T, THandler>(context, codec, contract, token), contract);
    }

    /// <summary>Gets the exact immutable scope/job definition.</summary>
    public JobDefinition Definition
    {
        get;
    }
    /// <summary>Gets the explicit CLR handler type, kept outside the portable definition.</summary>
    public Type HandlerType
    {
        get;
    }
    /// <summary>Gets captured scheduled DTO contract metadata, or null for a continuous-only job.</summary>
    public MessageContract? InputContract
    {
        get;
    }

    internal Task<ContinuousIterationResult> InvokeContinuousAsync(JobExecutionContext context, CancellationToken token)
    {
        ValidateContext(context, JobExecutionMode.Continuous);
        return continuous is null ? throw new InvalidOperationException("This definition does not support continuous execution.") : continuous(context, token);
    }

    internal Task<ScheduledJobResult> InvokeScheduledAsync(JobExecutionContext context, CancellationToken token)
    {
        ValidateContext(context, JobExecutionMode.Scheduled);
        return scheduled is null ? throw new InvalidOperationException("This definition does not support scheduled execution.") : scheduled(context, token);
    }

    private void ValidateContext(JobExecutionContext context, JobExecutionMode mode)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(context.Definition, Definition) || context.Settings.Mode != mode)
        {
            throw new ArgumentException("Invoke only the exact binding for this registered definition and mode.", nameof(context));
        }
    }

    private static Task<ScheduledJobResult> DecodeAndInvokeAsync<T, THandler>(JobExecutionContext context,
        IMessageCodec<T> codec, MessageContract contract, CancellationToken token)
        where THandler : class, IScheduledJobHandler<T>
    {
        AdmittedJobRun run = context.AdmittedRun ?? throw new ArgumentException("Scheduled execution requires its admitted occurrence.", nameof(context));
        token.ThrowIfCancellationRequested();
        if (run.Input.Envelope.Contract != contract || !codec.TryDecode(contract, run.Input.Envelope.PayloadMemory, out T? input) || input is null)
        {
            return Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Unsupported, "jobs.payload_unsupported"));
        }

        token.ThrowIfCancellationRequested();
        return context.Services.GetRequiredService<THandler>().ExecuteAsync(context, input, token);
    }
}
