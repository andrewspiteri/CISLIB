namespace CIS.RestClient;

internal sealed record RestMappedResponse<T>(RestDecodedValue<T> Decoded, RestFailureKind FailureKind,
    string? ErrorCode = null, string? Explanation = null, string? RemoteErrorCode = null);

internal static class RestResponseMapping
{
    internal static async ValueTask<RestMappedResponse<T>> MapAsync<T>(IRestResponse response, RestDecodedValue<T> decoded,
        RestErrorBody? errorBody, IRestResponseMapper? mapper, CancellationToken token)
    {
        bool successfulHttp = response.HttpStatusCode is >= 200 and <= 299;
        RestResponseDecision decision = mapper is null ? RestResponseDecision.Continue
            : await mapper.MapAsync(new RestResponseContext<T>(response.HttpStatusCode, response.Headers,
                successfulHttp ? decoded : RestDecodedValue<T>.Absent, errorBody), token).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(decision);
        if (!successfulHttp)
        { return new(RestDecodedValue<T>.Absent, RestFailureKind.Http, "http." + response.HttpStatusCode,
            decision.Explanation ?? "The remote service returned HTTP " + response.HttpStatusCode + ".", decision.RemoteErrorCode); }
        if (decision.IsBusinessRejection)
        { return new(RestDecodedValue<T>.Absent, RestFailureKind.Business, decision.ErrorCode, decision.Explanation, decision.RemoteErrorCode); }
        return new(decoded, RestFailureKind.None);
    }
}
