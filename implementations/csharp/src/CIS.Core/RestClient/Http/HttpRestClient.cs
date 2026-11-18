namespace CIS.RestClient.Http;

/// <summary>A typed REST client sharing one long-lived transport and an awaited operation owner.</summary>
public sealed class HttpRestClient : IRestClient, IAsyncDisposable
{
    private readonly RestClientRuntime runtime;
    private readonly string name;
    private readonly bool ownsRuntime;

    /// <summary>Creates a standalone client. An explicitly supplied transport remains borrowed.</summary>
    /// <remarks>Standalone clients support RetryPolicy.None. Register the optional retry adapter in
    /// an existing host to use named policies. Always await disposal before disposing borrowed resources.</remarks>
    public HttpRestClient(RestClientOptions options, RestHttpTransport? borrowed = null, TimeProvider? clock = null)
    {
        RestClientSnapshot snapshot = RestClientSnapshot.Capture(options);
        if (snapshot.RetryPolicy.PolicyName is not null)
        { throw new InvalidOperationException("A named retry policy requires the optional host retry registration."); }
        var composition = new RestClientComposition(new object(), snapshot.Operations
            .Select(operation => new RestOperationIdentity(snapshot.ClientName, operation)).ToArray());
        RestHttpTransport transport = borrowed ?? RestHttpTransport.Create();
        try
        {
            runtime = new RestClientRuntime(composition, [new(snapshot, transport, borrowed is null)], clock ?? TimeProvider.System);
        }
        catch
        {
            if (borrowed is null) { transport.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            throw;
        }
        name = snapshot.ClientName; ownsRuntime = true;
    }

    internal HttpRestClient(RestClientRuntime runtime, string name)
    { this.runtime = runtime; this.name = name; }

    /// <inheritdoc/>
    public Task<RestResult<T>> Get<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("GET", url, options, cancellationToken);
    /// <inheritdoc/>
    public Task<RestResult<T>> Post<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("POST", url, options, cancellationToken);
    /// <inheritdoc/>
    public Task<RestResult<T>> Put<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("PUT", url, options, cancellationToken);
    /// <inheritdoc/>
    public Task<RestResult<T>> Patch<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("PATCH", url, options, cancellationToken);
    /// <inheritdoc/>
    public Task<RestResult<T>> Delete<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("DELETE", url, options, cancellationToken);
    /// <inheritdoc/>
    public Task<RestResult<T>> Options<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("OPTIONS", url, options, cancellationToken);
    /// <inheritdoc/>
    public Task<RestResult<T>> Trace<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("TRACE", url, options, cancellationToken);
    /// <inheritdoc/>
    public Task<RestResult<RestNoValue>> Head(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<RestNoValue>("HEAD", url, options, cancellationToken);
    /// <inheritdoc/>
    public Task<RestResult<T>> Send<T>(string method, string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => runtime.ExecuteAsync<T>(name, method, url, options, cancellationToken);

    /// <summary>Closes admission, cancels and joins actual operations, then disposes only owned resources.</summary>
    /// <remarks>A client resolved from the host borrows its runtime; the host owns its lifecycle.</remarks>
    public ValueTask DisposeAsync() => ownsRuntime ? runtime.DisposeAsync() : ValueTask.CompletedTask;
}
