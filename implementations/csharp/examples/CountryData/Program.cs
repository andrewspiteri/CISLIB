using CIS.Hosting;
using CIS.RestClient;
using CIS.RestClient.Resilience;
using CIS.Startup;

namespace CIS.Examples.CountryData;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        await using CISApplication app = CISApplication.CreateWeb(args, builder =>
        {
            var countries = new RestClientOptions("countries")
            {
                LogCalls = false,
                ResponseMapper = new CountryProviderMapper(),
                RetryPolicy = RestRetrySelection.Named(RestRetryPolicy.TransientHttp.Name)
            };
            countries.Operations.Add("refresh");
            builder.RestClients.Add(countries);
            builder.Services.AddCisRestClientResilience(RestRetryPolicy.TransientHttp);
            builder.Services.AddSingleton<CountryCatalog>();
            builder.Services.AddSingleton<IStartable>(provider => provider.GetRequiredService<CountryCatalog>());
        }, web => web.MapGet("/country/{identifier}", CountryEndpoints.Get));

        return await app.RunAsync();
    }
}
