using CIS.Hosting;
using CIS.RestClient;
using CIS.RestClient.Resilience;
using CIS.Startup;
using CIS.Workflows.Hosting;

namespace CIS.Examples.CountryData;

/// <summary>The same maintained tutorial composition, also available to ordinary application-level tests.</summary>
public static class CountryApplication
{
    public static CISApplication CreateService(string[] args, Action<CISApplicationBuilder>? configure = null)
        => CISApplication.CreateWeb(args, builder =>
        {
            configure?.Invoke(builder);
            CountrySecurityComposition.Configure(builder);
            var countries = new RestClientOptions("countries")
            {
                LogCalls = false,
                ResponseMapper = new CountryProviderMapper(),
                RetryPolicy = RestRetrySelection.Named(RestRetryPolicy.TransientHttp.Name)
            };
            countries.Operations.Add("refresh");
            builder.RestClients.Add(countries);
            builder.Services.AddCisRestClientResilience(RestRetryPolicy.TransientHttp);
            builder.Services.AddCisWorkflows();
            builder.Services.AddCisStateMachines();
            builder.Services.AddSingleton<CountryCatalog>();
            builder.Services.AddSingleton<IStartable>(provider => provider.GetRequiredService<CountryCatalog>());
        }, CountrySecurityComposition.Map);
}
