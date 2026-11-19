using CIS.Workflows;

namespace CIS.Examples.CountryData;

public enum CountryLookupState
{
    Pending,
    Resolved,
    Completed
}

public enum CountryLookupTrigger
{
    Resolve,
    Complete
}

public static class CountryLookupStateMachine
{
    public static IStateMachine<CountryLookupState, CountryLookupTrigger> Build(
        IStateMachineFactory machines, CountryLookupFlowContext context)
    {
        var builder = machines.CreateBuilder<CountryLookupState, CountryLookupTrigger, CountryLookupFlowContext>(
            "CountryLookup", context);
        builder.State(CountryLookupState.Pending)
            .On<ResolveCountryLookup>(CountryLookupTrigger.Resolve, CountryLookupState.Resolved, "resolve");
        builder.State(CountryLookupState.Resolved)
            .On(CountryLookupTrigger.Complete, CountryLookupState.Completed);

        return builder.Build(CountryLookupState.Pending);
    }
}

public sealed class CountryLookupFlowContext(string identifier)
{
    public string Identifier { get; } = identifier;
    public IResult? Response
    {
        get; internal set;
    }
    public CountryLookupReport? LastLookup
    {
        get; internal set;
    }
}

// One detached child summary, without its context, provider or activated steps.
public sealed record CountryLookupReport(
    PipelineStatus Status,
    PipelineFailure? Error,
    IReadOnlyList<PipelineStepReport> Steps,
    IReadOnlyList<PipelineFailure> ErrorHandlingFailures,
    IReadOnlyList<PipelineFailure> LifecycleFailures,
    TimeSpan? Duration);

public sealed class ResolveCountryLookup(IPipelineFactory pipelines, IServiceProvider scopedServices)
    : IStateTransitionHandler<CountryLookupState, CountryLookupTrigger, CountryLookupFlowContext>
{
    public async Task<TransitionActionResult> Execute(CountryLookupFlowContext context,
        TransitionInfo<CountryLookupState, CountryLookupTrigger> transition, CancellationToken cancellationToken)
    {
        context.Response = null;
        IPipeline<CountryLookupContext> child = CountryLookupPipeline.Build(pipelines, scopedServices, context.Identifier);
        try
        {
            await child.Execute(cancellationToken);
        }
        finally
        {
            context.LastLookup = new(child.Status, child.Error, child.Steps,
                child.ErrorHandlingFailures, child.LifecycleFailures, child.Duration);
        }

        if (!child.Success)
        {
            return TransitionActionResult.Failed(child.Error!.Code, child.Error.Message);
        }

        context.Response = child.Context.Response;
        return TransitionActionResult.Succeeded();
    }

    public Task Error(CountryLookupFlowContext context,
        TransitionInfo<CountryLookupState, CountryLookupTrigger> transition, StateTransitionFailure failure,
        CancellationToken cancellationToken)
    {
        // The child already owns its unwind. This lookup has no external writes;
        // a later outer cancellation only needs to discard this response draft.
        context.Response = null;
        return Task.CompletedTask;
    }
}
