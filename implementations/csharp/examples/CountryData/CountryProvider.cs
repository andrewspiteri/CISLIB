using System.Text.Json.Serialization;
using CIS.RestClient;

namespace CIS.Examples.CountryData;

internal sealed class CountriesEnvelope
{
    public CountriesEnvelope() { }
    [JsonPropertyName("error")] public bool? Error { get; init; }
    [JsonPropertyName("msg")] public string? Message { get; init; }
    [JsonPropertyName("data")] public ProviderCountry?[]? Data { get; init; }
}

internal sealed class ProviderCountry
{
    public ProviderCountry() { }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("Iso2")] public string? Iso2 { get; init; }
    [JsonPropertyName("Iso3")] public string? Iso3 { get; init; }
}

internal sealed class CountryProviderMapper : IRestResponseMapper
{
    public ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestResponseDecision decision = response.Decoded.Value is CountriesEnvelope envelope
            ? envelope.Error switch
            {
                true => RestResponseDecision.RejectBusiness("The country provider rejected the request.", "country.provider_rejected"),
                null => RestResponseDecision.RejectBusiness("The country provider returned an invalid envelope.", "country.invalid_data"),
                _ => RestResponseDecision.Continue
            }
            : RestResponseDecision.Continue;
        return ValueTask.FromResult(decision);
    }
}
