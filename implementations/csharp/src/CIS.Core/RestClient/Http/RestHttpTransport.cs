using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.Net;

namespace CIS.RestClient.Http;

/// <summary>Native pooling controls captured once by a known REST transport.</summary>
public sealed class RestHttpTransportOptions
{
    /// <summary>Gets or sets pooled connection rotation; default two minutes.</summary>
    public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(2);
    /// <summary>Gets or sets native per-server connection capacity.</summary>
    public int MaxConnectionsPerServer { get; set; } = int.MaxValue;
    /// <summary>Gets or sets native response header limit in KiB; default 64.</summary>
    public int MaxResponseHeadersLength { get; set; } = 64;
    /// <summary>Gets or sets native decompression; consumed-byte limits apply afterward.</summary>
    public DecompressionMethods AutomaticDecompression { get; set; } = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli;
}

/// <summary>One known long-lived native pool. Logical owners can explicitly borrow it.</summary>
/// <remarks>Stop borrowing clients before disposal. This resource closes new sends and waits for
/// existing response leases; it never cancels another borrower's requests. No raw opaque HttpClient is imported.</remarks>
public sealed class RestHttpTransport : IAsyncDisposable
{
    private static readonly HttpRequestOptionsKey<Dispatch> DispatchKey = new("CIS.RestClient.PrimaryDispatch");
    private readonly object gate = new();
    private readonly HttpClient client;
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IMeterFactory? meterFactory;
    private int holds;
    private bool closing;
    private Task? disposal;

    private RestHttpTransport(IMeterFactory? factory, RestHttpTransportOptions options)
    {
        if (options.PooledConnectionLifetime < TimeSpan.Zero && options.PooledConnectionLifetime != Timeout.InfiniteTimeSpan
            || options.MaxConnectionsPerServer <= 0 || options.MaxResponseHeadersLength <= 0
            || (options.AutomaticDecompression & ~(DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli)) != 0)
        { throw new ArgumentException("Use a supported finite native transport profile.", nameof(options)); }
        meterFactory = factory;
        var sockets = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, Credentials = null, PreAuthenticate = false,
            UseProxy = false, DefaultProxyCredentials = null,
            ActivityHeadersPropagator = null,
            ConnectTimeout = Timeout.InfiniteTimeSpan, MeterFactory = factory,
            PooledConnectionLifetime = options.PooledConnectionLifetime,
            MaxConnectionsPerServer = options.MaxConnectionsPerServer,
            MaxResponseHeadersLength = options.MaxResponseHeadersLength,
            AutomaticDecompression = options.AutomaticDecompression
        };
        client = new HttpClient(new PrimaryHandler(sockets), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }
    /// <summary>Creates a known native profile without network I/O. A host supplies its exact existing meter factory.</summary>
    public static RestHttpTransport Create(IMeterFactory? meterFactory = null, RestHttpTransportOptions? options = null)
        => new(meterFactory, options ?? new());
    internal void ValidateForHost(IMeterFactory? expectedFactory)
    {
        if (expectedFactory is not null && !ReferenceEquals(expectedFactory, meterFactory))
        { throw new InvalidOperationException("The transport must use this root's exact meter factory before first use."); }
        lock (gate) { ObjectDisposedException.ThrowIf(closing, this); }
    }

    internal async ValueTask<IRestResponse> SendAsync(HttpRequestMessage request, Action dispatch, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(dispatch); token.ThrowIfCancellationRequested();
        lock (gate) { ObjectDisposedException.ThrowIf(closing, this); holds++; }
        HttpResponseMessage? response = null;
        var boundary = new Dispatch(dispatch);
        try
        {
            request.Options.Set(DispatchKey, boundary);
            try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); }
            catch (Exception error)
            {
                RestEncodingFaultException? encoding = FindEncodingFault(error);
                if (encoding is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(encoding).Throw(); }
                if (error is HttpRequestException nativeError && boundary.Entered)
                { throw new RestTransportException(nativeError, Classify(nativeError.HttpRequestError)); }
                throw;
            }
            Stream body;
            try { body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false); }
            catch (IOException error) { throw new RestResponseReadException(error, (int)response.StatusCode); }
            var owned = new Response(response, body, Release);
            response = null;
            return owned;
        }
        catch (Exception original)
        {
            try { response?.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("HTTP exchange and response cleanup both failed.", original, cleanup); }
            finally { Release(); }
            throw;
        }
    }
    private static RestEncodingFaultException? FindEncodingFault(Exception error)
    {
        // Follow only known native copy/send wrappers, never arbitrary extension
        // exception graphs. The marker remains a fault until the public owner.
        for (Exception? current = error; current is not null; current = current is HttpRequestException or IOException ? current.InnerException : null)
        { if (current is RestEncodingFaultException encoding) { return encoding; } }
        return null;
    }
    private static RestTransportFailureKind Classify(HttpRequestError error) => error switch
    {
        HttpRequestError.NameResolutionError => RestTransportFailureKind.NameResolution,
        HttpRequestError.ConnectionError => RestTransportFailureKind.Connection,
        HttpRequestError.SecureConnectionError => RestTransportFailureKind.SecureConnection,
        HttpRequestError.HttpProtocolError => RestTransportFailureKind.Protocol,
        HttpRequestError.ResponseEnded => RestTransportFailureKind.ResponseEnded,
        _ => RestTransportFailureKind.Other
    };
    private void Release()
    { lock (gate) { holds--; if (closing && holds == 0) { drained.TrySetResult(); } } }
    /// <summary>Closes new sends and joins all borrowed response leases before disposing the owned native pool once.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null; Task shared;
        lock (gate)
        {
            closing = true;
            if (holds == 0) { drained.TrySetResult(); }
            if (disposal is null) { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); disposal = completion.Task; }
            shared = disposal;
        }
        if (completion is not null) { _ = DisposeCoreAsync(completion); }
        return new(shared);
    }
    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try { await drained.Task.ConfigureAwait(false); client.Dispose(); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }
    private sealed class Dispatch(Action callback)
    { internal Action Callback { get; } = callback; internal bool Entered; }
    private sealed class PrimaryHandler(HttpMessageHandler primary) : DelegatingHandler(primary)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!request.Options.TryGetValue(DispatchKey, out Dispatch? boundary)) { throw new InvalidOperationException("An owned primary dispatch boundary is required."); }
            boundary.Callback(); boundary.Entered = true;
            return base.SendAsync(request, token);
        }
    }
    private sealed class Response : IRestResponse
    {
        private readonly object gate = new();
        private readonly HttpResponseMessage native;
        private readonly Action release;
        private Task? disposal;
        internal Response(HttpResponseMessage native, Stream body, Action release)
        {
            this.native = native; this.release = release; Body = body; HttpStatusCode = (int)native.StatusCode;
            var copied = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in native.Headers.Concat(native.Content.Headers))
            {
                string[] values = pair.Value.ToArray();
                copied[pair.Key] = Array.AsReadOnly(copied.TryGetValue(pair.Key, out var previous) ? previous.Concat(values).ToArray() : values);
            }
            Headers = new ReadOnlyDictionary<string, IReadOnlyList<string>>(copied);
        }
        public int HttpStatusCode { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
        public Stream Body { get; }
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource? completion = null; Task shared;
            lock (gate)
            {
                if (disposal is null) { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); disposal = completion.Task; }
                shared = disposal;
            }
            if (completion is not null)
            {
                Exception? failure = null;
                try { native.Dispose(); }
                catch (Exception error) { failure = error; }
                finally { release(); }
                if (failure is null) { completion.TrySetResult(); } else { completion.TrySetException(failure); }
            }
            return new(shared);
        }
    }
}
