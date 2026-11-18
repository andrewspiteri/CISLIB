namespace CIS.RestClient;

/// <summary>A single owned exchange response usable only within the awaited handler/decoder lifetime.</summary>
/// <remarks>This extension lease is not a streaming result returned by IRestClient. The operation disposes it.</remarks>
public interface IRestResponse : IAsyncDisposable
{
    /// <summary>Gets actual HTTP status.</summary>
    int HttpStatusCode { get; }
    /// <summary>Gets response/content header values.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
    /// <summary>Gets the single owned body stream; handlers must not consume it behind the decoder.</summary>
    Stream Body { get; }
}

/// <summary>Invokes the next exchange at most once; retry has a separate owner.</summary>
public delegate ValueTask<IRestResponse> RestExchange(RestRequestContext request, CancellationToken cancellationToken);

/// <summary>A reusable concurrency-safe exchange handler; per-call state belongs on its context.</summary>
public interface IRestExchangeHandler
{
    /// <summary>Wraps one exchange or returns an owned short-circuit response without primary dispatch.</summary>
    ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken cancellationToken);
}

/// <summary>A replaceable content codec; no HTTP message or serializer SDK appears in the contract.</summary>
public interface IRestContentCodec
{
    /// <summary>States whether this codec supports the explicit result mode/type.</summary>
    bool CanDecode(Type resultType, RestResponseFormat format);
    /// <summary>Encodes into the owned destination using the operation token.</summary>
    ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken cancellationToken);
    /// <summary>Decodes from the one bounded response reader; zero bytes mean absence.</summary>
    ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken cancellationToken);
}

/// <summary>Classifies an integration's decoded envelope or bounded error prefix without another body read.</summary>
public interface IRestResponseMapper
{
    /// <summary>Returns one explicit decision; unexpected thrown exceptions remain faults.</summary>
    ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken cancellationToken);
}
