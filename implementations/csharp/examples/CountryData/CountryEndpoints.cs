using CIS.Workflows;
using CIS.Workflows.Hosting;

namespace CIS.Examples.CountryData;

public static class CountryEndpoints
{
    public static async Task<IResult> Get(string identifier, IStateMachineFactory machines, HttpContext httpContext)
    {
        var context = new CountryLookupFlowContext(identifier);
        IStateMachine<CountryLookupState, CountryLookupTrigger> machine = CountryLookupStateMachine.Build(machines, context);
        StateTransitionResult<CountryLookupState, CountryLookupTrigger> result = await machine.TriggerInScope(
            CountryLookupTrigger.Resolve, httpContext.RequestServices, httpContext.RequestAborted);

        if (result.Success)
        {
            result = await machine.Trigger(CountryLookupTrigger.Complete, httpContext.RequestAborted);
            if (result.Success)
            {
                return context.Response!;
            }
        }

        if (result.Outcome == StateTransitionOutcome.Cancelled)
        {
            throw new OperationCanceledException(httpContext.RequestAborted);
        }

        if (result.Error?.Kind == StateTransitionFailureKind.ReportedFailure
            && result.ErrorCode == "country.invalid_identifier")
        {
            return Results.BadRequest(new
            {
                code = "country.invalid_identifier"
            });
        }

        if (result.Error?.Kind == StateTransitionFailureKind.ReportedFailure
            && result.ErrorCode == "country.not_found")
        {
            return Results.NotFound(new
            {
                code = "country.not_found"
            });
        }

        throw new InvalidOperationException("The country lookup could not be prepared.", result.Error?.Exception);
    }
}
