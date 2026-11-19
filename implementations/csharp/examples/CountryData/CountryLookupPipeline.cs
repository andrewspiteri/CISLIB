using CIS.Workflows;
using CIS.Workflows.Hosting;

namespace CIS.Examples.CountryData;

public static class CountryLookupPipeline
{
    public static IPipeline<CountryLookupContext> Build(
        IPipelineFactory pipelines,
        IServiceProvider scopedServices,
        string identifier)
    {
        var context = new CountryLookupContext(identifier);

        return pipelines.CreateBuilderInScope("CountryLookup", context, scopedServices)
            .Add<NormalizeCountryIdentifier>("normalize")
            .Add<FindCachedCountry>("find")
            .Add<PrepareCountryResponse>("prepare_response")
            .Build();
    }
}

public sealed class CountryLookupContext(string identifier)
{
    public string Identifier { get; } = identifier;
    public string? Key { get; internal set; }
    public Country? Country { get; internal set; }
    public IResult? Response { get; internal set; }
}

public sealed class NormalizeCountryIdentifier : IPipelineStep<CountryLookupContext>
{
    public Task<PipelineStepResult> Execute(CountryLookupContext context, CancellationToken cancellationToken)
    {
        context.Key = context.Identifier.Trim();

        return Task.FromResult(context.Key.Length == 0
            ? PipelineStepResult.Failed("country.invalid_identifier", "A country identifier is required.")
            : PipelineStepResult.Succeeded());
    }

    public Task Error(CountryLookupContext context, PipelineFailure failure, CancellationToken cancellationToken)
    {
        context.Key = null;
        return Task.CompletedTask;
    }
}

public sealed class FindCachedCountry(CountryCatalog catalog) : IPipelineStep<CountryLookupContext>
{
    public Task<PipelineStepResult> Execute(CountryLookupContext context, CancellationToken cancellationToken)
    {
        if (!catalog.TryGet(context.Key!, out Country? country))
        {
            return Task.FromResult(PipelineStepResult.Failed("country.not_found", "The country was not found."));
        }

        context.Country = country;
        return Task.FromResult(PipelineStepResult.Succeeded());
    }

    public Task Error(CountryLookupContext context, PipelineFailure failure, CancellationToken cancellationToken)
    {
        context.Country = null;
        return Task.CompletedTask;
    }
}

public sealed class PrepareCountryResponse : IPipelineStep<CountryLookupContext>
{
    public Task<PipelineStepResult> Execute(CountryLookupContext context, CancellationToken cancellationToken)
    {
        context.Response = Results.Ok(context.Country!);
        return Task.FromResult(PipelineStepResult.Succeeded());
    }

    public Task Error(CountryLookupContext context, PipelineFailure failure, CancellationToken cancellationToken)
    {
        context.Response = null;
        return Task.CompletedTask;
    }
}
