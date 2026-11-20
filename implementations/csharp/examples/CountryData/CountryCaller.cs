using CIS.Hosting;
using CIS.RestClient;

namespace CIS.Examples.CountryData;

public sealed record CountryCallerResult(bool Success, int? HttpStatusCode, string? ErrorCode,
    Country? Country = null, CountrySelectionSnapshot? Selection = null);

/// <summary>Runs one explicitly selected operation through the existing real REST adapter.</summary>
public static class CountryCaller
{
    public static async Task<CountryCallerResult> ExecuteAsync(IRestClient client, string operation = "read",
        string identifier = "MT", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (operation is not ("read" or "select" or "inspect") || string.IsNullOrWhiteSpace(identifier) || identifier.Length > 255)
        {
            throw new ArgumentException("Choose --operation=read|select|inspect and a bounded nonempty --country identifier.");
        }
        string country = Uri.EscapeDataString(identifier);
        if (operation == "read")
        {
            RestResult<Country> result = await client.Get<Country>("/country/" + country,
                new()
                {
                    OperationName = "read"
                }, cancellationToken);
            bool success = result.Success && result.Value is not null;
            return new(success, result.Context.HttpStatusCode, result.ErrorCode ?? (success ? null : "country.missing_response"),
                Country: success ? result.Value : null);
        }
        RestResult<CountrySelectionSnapshot> selection = operation == "select"
            ? await client.Put<CountrySelectionSnapshot>("/selection/" + country, new()
            {
                OperationName = "select"
            }, cancellationToken)
            : await client.Get<CountrySelectionSnapshot>("/selection", new()
            {
                OperationName = "inspect"
            }, cancellationToken);
        bool selected = selection.Success && selection.Value is not null;
        return new(selected, selection.Context.HttpStatusCode,
            selection.ErrorCode ?? (selected ? null : "country.missing_response"), Selection: selected ? selection.Value : null);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        string operation = "read";
        string country = "MT";
        await using var application = new CISApplication(args, builder =>
        {
            CountrySecurityComposition.Configure(builder, caller: true);
            operation = builder.Host.Configuration["operation"] ?? "read";
            country = builder.Host.Configuration["country"] ?? "MT";
        });
        await application.StartAsync();
        CountryCallerResult result = await ExecuteAsync(application.Services.GetRequiredKeyedService<IRestClient>(
            CountrySecurityComposition.CallerClient), operation, country);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        return result.Success ? 0 : 1;
    }
}
