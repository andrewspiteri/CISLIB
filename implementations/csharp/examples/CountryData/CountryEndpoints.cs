using CIS.Workflows;

namespace CIS.Examples.CountryData;

public static class CountryEndpoints
{
    public static async Task<IResult> Get(string identifier, IPipelineFactory pipelines, HttpContext httpContext)
    {
        IPipeline<CountryLookupContext> pipeline = CountryLookupPipeline.Build(
            pipelines, httpContext.RequestServices, identifier);
        await pipeline.Execute(httpContext.RequestAborted);

        if (pipeline.Success)
        {
            return pipeline.Context.Response!;
        }

        if (pipeline.Error?.Code == "country.invalid_identifier")
        {
            return Results.BadRequest(new
            {
                code = "country.invalid_identifier"
            });
        }

        return Results.NotFound(new
        {
            code = "country.not_found"
        });
    }
}
