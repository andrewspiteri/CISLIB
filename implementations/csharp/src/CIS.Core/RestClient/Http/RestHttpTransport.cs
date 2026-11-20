using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Security;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using CIS.Security;

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

/// <summary>A stable native transport owning bounded pools and their actual response leases.</summary>
/// <remarks>Stop borrowing clients and credential-source work before disposal. Disposal closes new
/// exchanges/adoption and joins existing response and construction owners without canceling borrowers.</remarks>
public sealed class RestHttpTransport : IAsyncDisposable
{
    private static readonly HttpRequestOptionsKey<Dispatch> DispatchKey = new("CIS.RestClient.PrimaryDispatch");
    private readonly object gate = new();
    private readonly IMeterFactory? meterFactory;
    private readonly RestHttpTransportOptions options;
    private readonly TimeProvider clock;
    private readonly bool allowsInitialServerTrust;
    private RestHttpServerTrust? serverTrust;
    private bool serverTrustInitialized;
    private readonly List<Generation> generations = [];
    // A cleanup fault closes admission; only the already occupied generation slots can add another fault.
    private readonly List<Exception> cleanupFailures = new(2);
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Generation? current;
    private bool constructing;
    private bool closing;
    private Task? disposal;

    private RestHttpTransport(IMeterFactory? factory, RestHttpTransportOptions supplied,
        RestSecurityBinding? securityBinding, TimeProvider clock, RestHttpServerTrust? serverTrust,
        bool deferServerTrust = false)
    {
        if (supplied.PooledConnectionLifetime < TimeSpan.Zero && supplied.PooledConnectionLifetime != Timeout.InfiniteTimeSpan
            || supplied.MaxConnectionsPerServer <= 0 || supplied.MaxResponseHeadersLength <= 0
            || (supplied.AutomaticDecompression & ~(DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli)) != 0
            || securityBinding is not null && supplied.PooledConnectionLifetime == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException("Use supported native limits and a finite security-bound connection lifetime.", nameof(supplied));
        }
        meterFactory = factory;
        SecurityBinding = securityBinding;
        this.clock = clock;
        this.serverTrust = serverTrust;
        allowsInitialServerTrust = deferServerTrust;
        serverTrustInitialized = !deferServerTrust;
        options = new()
        {
            PooledConnectionLifetime = supplied.PooledConnectionLifetime,
            MaxConnectionsPerServer = supplied.MaxConnectionsPerServer,
            MaxResponseHeadersLength = supplied.MaxResponseHeadersLength,
            AutomaticDecompression = supplied.AutomaticDecompression
        };
        if (securityBinding?.CredentialKind != RestSecurityCredentialKind.ClientCertificate)
        {
            current = CreateGeneration(null);
            generations.Add(current);
        }
    }

    /// <summary>Creates the existing credential-free native profile without network I/O.</summary>
    public static RestHttpTransport Create(IMeterFactory? meterFactory = null, RestHttpTransportOptions? options = null)
        => new(meterFactory, options ?? new(), null, TimeProvider.System, null);

    /// <summary>Captures an immutable security/destination policy and separate native server trust.</summary>
    /// <remarks>A certificate binding initially has no usable credential or handler. Its source adopts
    /// validated material through TryAdoptCertificateAsync. Creation performs no credential or network I/O.</remarks>
    public static RestHttpTransport CreateSecurityBound(RestSecurityBinding binding, IMeterFactory? meterFactory = null,
        RestHttpTransportOptions? options = null, TimeProvider? clock = null, RestHttpServerTrust? serverTrust = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new(meterFactory, options ?? new(), binding, clock ?? TimeProvider.System, serverTrust);
    }

    /// <summary>Creates a certificate owner whose initial immutable server trust will be captured by sequential Startup.</summary>
    /// <remarks>No handler, key or material factory can be created before InitializeServerTrustOnce.
    /// Ordinary CreateSecurityBound retains its immediate native-system-trust default.</remarks>
    public static RestHttpTransport CreateCertificateBoundUninitializedTrust(RestSecurityBinding binding,
        IMeterFactory? meterFactory = null, RestHttpTransportOptions? options = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.CredentialKind != RestSecurityCredentialKind.ClientCertificate)
        {
            throw new ArgumentException("Deferred initial trust belongs to a certificate-bound transport.", nameof(binding));
        }
        return new(meterFactory, options ?? new(), binding, clock ?? TimeProvider.System, null, deferServerTrust: true);
    }

    /// <summary>Captures initial server trust exactly once before any material acquisition; explicit null selects native system trust.</summary>
    /// <remarks>This is startup preparation, never a live trust setter. Replacement trust requires a replacement transport/instance.</remarks>
    public void InitializeServerTrustOnce(RestHttpServerTrust? trust)
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (!allowsInitialServerTrust || serverTrustInitialized || constructing || generations.Count != 0)
            {
                throw new InvalidOperationException("Initialize deferred server trust once, before any credential generation.");
            }
            serverTrust = trust;
            serverTrustInitialized = true;
        }
    }

    /// <summary>Gets the exact immutable owner/policy, or null for an ordinary unbound transport.</summary>
    public RestSecurityBinding? SecurityBinding
    {
        get;
    }
    /// <summary>Gets actual active leaf expiry without source I/O; null means no active leaf.</summary>
    public DateTimeOffset? CertificateExpiresAtUtc
    {
        get
        {
            lock (gate)
            {
                return current?.Material?.ExpiresAtUtc;
            }
        }
    }
    /// <summary>Gets current and draining generations, excluding the one reserved construction slot.</summary>
    public int LiveGenerationCount
    {
        get
        {
            lock (gate)
            {
                return generations.Count;
            }
        }
    }

    /// <summary>Gets terminal admission closure without waiting for retained native response owners.</summary>
    /// <remarks>Closure is permanent. Credential sources may observe this owned fact for availability;
    /// a retained leaf expiry does not establish that a closed transport can accept another exchange.</remarks>
    public bool IsAdmissionClosed
    {
        get
        {
            lock (gate)
            {
                return closing;
            }
        }
    }

    /// <summary>Throws the original retained cleanup fault, or disposal, when admission has closed.</summary>
    /// <remarks>This performs no I/O and does not wait for active TLS/response owners to drain.</remarks>
    public void ThrowIfAdmissionClosed()
    {
        lock (gate)
        {
            ThrowIfClosed();
        }
    }

    internal void ValidateForHost(IMeterFactory? expectedFactory)
    {
        if (expectedFactory is not null && !ReferenceEquals(expectedFactory, meterFactory))
        {
            throw new InvalidOperationException("The transport must use this root's exact meter factory before first use.");
        }
        lock (gate)
        {
            ThrowIfClosed();
        }
    }

    internal void ValidateBinding(RestSecurityBinding? binding, IReadOnlyList<string> origins)
    {
        if (!ReferenceEquals(SecurityBinding, binding) || binding is not null && !binding.HasExactOrigins(origins))
        {
            throw new InvalidOperationException("Borrow the exact security binding owner and destination policy of this transport.");
        }
    }

    /// <summary>Reserves capacity before acquiring a new leaf/key, then atomically adopts its native pool.</summary>
    /// <remarks>At most one current and one draining generation exist. Deferred never invokes the factory.
    /// The source owns rechecks and must clean material acquired before throwing. A returned material becomes
    /// transport-owned, including cancellation/failure before publication. Trust/grants never change here.</remarks>
    public async ValueTask<RestHttpCertificateAdoption> TryAdoptCertificateAsync(
        Func<CancellationToken, ValueTask<RestHttpCertificateMaterial>> materialFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(materialFactory);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ThrowIfClosed();
            if (SecurityBinding?.CredentialKind != RestSecurityCredentialKind.ClientCertificate)
            {
                throw new InvalidOperationException("Only a certificate-bound transport can adopt client material.");
            }
            if (!serverTrustInitialized)
            {
                throw CredentialFailure(SecurityFailureKind.CredentialUnavailable);
            }
            if (constructing || generations.Count >= 2)
            {
                return RestHttpCertificateAdoption.Deferred;
            }
            constructing = true;
        }

        RestHttpCertificateMaterial? material = null;
        Generation? candidate = null;
        Generation? retired = null;
        Exception? failure = null;
        Exception? materialCleanupFailure = null;
        try
        {
            material = await materialFactory(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The certificate factory returned null.");
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = clock.GetUtcNow();
            if (material.IsDisposed || now < material.NotBeforeUtc || now >= material.ExpiresAtUtc)
            {
                throw new ArgumentException("Adopt only owned material within its actual validity interval.", nameof(materialFactory));
            }
            candidate = CreateGeneration(material);
            material = null;
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                ThrowIfClosed();
                retired = current;
                if (retired is not null)
                {
                    retired.Retired = true;
                }
                current = candidate;
                generations.Add(candidate);
                candidate = null;
            }
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            if (candidate is not null)
            {
                try
                {
                    candidate.DisposeNative();
                }
                catch (Exception cleanup)
                {
                    failure = Combine(failure, cleanup);
                    materialCleanupFailure = cleanup;
                }
            }
            if (material is not null)
            {
                try
                {
                    material.Dispose();
                }
                catch (Exception cleanup)
                {
                    failure = Combine(failure, cleanup);
                    materialCleanupFailure = cleanup;
                }
            }
            Generation[] close;
            lock (gate)
            {
                if (materialCleanupFailure is not null)
                {
                    cleanupFailures.Add(materialCleanupFailure);
                    closing = true;
                    foreach (Generation generation in generations)
                    {
                        generation.Retired = true;
                    }
                }
                close = closing ? generations.ToArray() : [];
                constructing = false;
                SignalDrained();
            }
            foreach (Generation generation in close)
            {
                _ = StartRetiredDisposal(generation);
            }
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        if (retired is not null)
        {
            Task? cleanup = StartRetiredDisposal(retired);
            if (cleanup is not null)
            {
                await cleanup.ConfigureAwait(false);
            }
            retired = null;
        }
        ThrowIfAdmissionClosed();
        return RestHttpCertificateAdoption.Adopted;
    }

    internal async ValueTask<IRestResponse> SendAsync(HttpRequestMessage request, Action dispatch, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dispatch);
        token.ThrowIfCancellationRequested();
        ValidateRequest(request);
        DateTimeOffset now = clock.GetUtcNow();
        Generation selected;
        lock (gate)
        {
            ThrowIfClosed();
            selected = current ?? throw CredentialFailure(SecurityFailureKind.CredentialUnavailable);
            CheckCredential(selected, now);
            selected.Holds++;
        }

        HttpResponseMessage? response = null;
        var boundary = new Dispatch(() =>
        {
            ValidateRequest(request);
            CheckCredential(selected, clock.GetUtcNow());
            dispatch();
        });
        try
        {
            request.Options.Set(DispatchKey, boundary);
            try
            {
                response = await selected.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                RestEncodingFaultException? encoding = FindEncodingFault(error);
                if (encoding is not null)
                {
                    ExceptionDispatchInfo.Capture(encoding).Throw();
                }
                if (error is HttpRequestException nativeError && boundary.Entered)
                {
                    throw new RestTransportException(nativeError, Classify(nativeError.HttpRequestError));
                }
                throw;
            }
            Stream body;
            try
            {
                body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            }
            catch (IOException error) { throw new RestResponseReadException(error, (int)response.StatusCode); }
            var owned = new Response(response, body, () => ReleaseAsync(selected));
            response = null;
            return owned;
        }
        catch (Exception original)
        {
            Exception? cleanupFailure = null;
            try
            {
                response?.Dispose();
            }
            catch (Exception cleanup) { cleanupFailure = cleanup; }
            try
            {
                await ReleaseAsync(selected).ConfigureAwait(false);
            }
            catch (Exception cleanup) { cleanupFailure = Combine(cleanupFailure, cleanup); }
            if (cleanupFailure is not null)
            {
                throw new AggregateException("HTTP exchange and owned cleanup both failed.", original, cleanupFailure);
            }
            throw;
        }
    }

    private void ValidateRequest(HttpRequestMessage request)
    {
        if (SecurityBinding is { } binding && (request.RequestUri is null || !binding.Allows(request.RequestUri)
            || string.Equals(request.Method.Method, "TRACE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Method.Method, "CONNECT", StringComparison.OrdinalIgnoreCase)))
        {
            throw new RestSecurityPolicyException();
        }
    }

    private static void CheckCredential(Generation generation, DateTimeOffset now)
    {
        if (generation.Material is not { } credential)
        {
            return;
        }
        if (credential.IsDisposed)
        {
            throw new InvalidOperationException("Transferred certificate material was disposed before its transport generation settled.");
        }
        if (now >= credential.ExpiresAtUtc)
        {
            throw CredentialFailure(SecurityFailureKind.CredentialExpired);
        }
        if (now < credential.NotBeforeUtc)
        {
            throw CredentialFailure(SecurityFailureKind.CredentialInvalid);
        }
    }

    private Generation CreateGeneration(RestHttpCertificateMaterial? material)
    {
        var roots = new List<X509Certificate2>();
        SocketsHttpHandler? sockets = null;
        try
        {
            sockets = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                Credentials = null,
                PreAuthenticate = false,
                UseProxy = false,
                DefaultProxyCredentials = null,
                ActivityHeadersPropagator = null,
                ConnectTimeout = Timeout.InfiniteTimeSpan,
                MeterFactory = meterFactory,
                PooledConnectionLifetime = options.PooledConnectionLifetime,
                MaxConnectionsPerServer = options.MaxConnectionsPerServer,
                MaxResponseHeadersLength = options.MaxResponseHeadersLength,
                AutomaticDecompression = options.AutomaticDecompression
            };
            if (serverTrust is not null)
            {
                sockets.SslOptions.CertificateChainPolicy = serverTrust.CreatePolicy(roots);
            }
            if (material is not null)
            {
                if (OperatingSystem.IsWindows())
                {
                    // A supplied chain context may install intermediates in Windows certificate stores.
                    // Let native TLS select only this owned leaf; deployment owns intermediate provisioning.
                    sockets.SslOptions.ClientCertificates = new X509CertificateCollection { material.Certificate };
                }
                else
                {
                    sockets.SslOptions.ClientCertificateContext = SslStreamCertificateContext.Create(
                        material.Certificate, material.IntermediateCertificates, offline: true);
                }
            }
            var client = new HttpClient(new PrimaryHandler(sockets), disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            return new(client, material, roots.ToArray());
        }
        catch (Exception original)
        {
            Exception? failure = null;
            try
            {
                sockets?.Dispose();
            }
            catch (Exception cleanup) { failure = cleanup; }
            foreach (X509Certificate2 root in roots)
            {
                try
                {
                    root.Dispose();
                }
                catch (Exception cleanup) { failure = Combine(failure, cleanup); }
            }
            if (failure is not null)
            {
                throw new AggregateException("Native generation construction and cleanup failed.", original, failure);
            }
            throw;
        }
    }

    private ValueTask ReleaseAsync(Generation generation)
    {
        bool retired;
        lock (gate)
        {
            generation.Holds--;
            retired = generation.Retired;
        }
        if (!retired)
        {
            return ValueTask.CompletedTask;
        }
        Task? cleanup = StartRetiredDisposal(generation);
        return cleanup is null ? ValueTask.CompletedTask : new(cleanup);
    }

    private Task? StartRetiredDisposal(Generation generation)
    {
        bool start = false;
        lock (gate)
        {
            if (!generation.Retired || generation.Holds != 0)
            {
                return null;
            }
            if (!generation.DisposalStarted)
            {
                generation.DisposalStarted = true;
                start = true;
            }
        }
        if (start)
        {
            DisposeGeneration(generation);
        }
        return generation.Completion.Task;
    }

    private void DisposeGeneration(Generation generation)
    {
        Exception? failure = null;
        try
        {
            generation.DisposeNative();
        }
        catch (Exception error) { failure = error; }
        Generation[] close;
        lock (gate)
        {
            generations.Remove(generation);
            if (ReferenceEquals(current, generation))
            {
                current = null;
            }
            if (failure is not null)
            {
                cleanupFailures.Add(failure);
                closing = true;
                foreach (Generation remaining in generations)
                {
                    remaining.Retired = true;
                }
            }
            close = closing ? generations.ToArray() : [];
            if (failure is null)
            {
                generation.Completion.TrySetResult();
            }
            else
            {
                generation.Completion.TrySetException(failure);
                _ = generation.Completion.Task.Exception;
            }
            SignalDrained();
        }
        foreach (Generation remaining in close)
        {
            _ = StartRetiredDisposal(remaining);
        }
    }

    /// <summary>Closes new exchanges/adoption and joins actual construction/responses before disposing owned pools/keys.</summary>
    public ValueTask DisposeAsync()
    {
        Generation[] close;
        TaskCompletionSource? completion = null;
        Task shared;
        lock (gate)
        {
            closing = true;
            foreach (Generation generation in generations)
            {
                generation.Retired = true;
            }
            close = generations.ToArray();
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }
            shared = disposal;
            SignalDrained();
        }
        foreach (Generation generation in close)
        {
            _ = StartRetiredDisposal(generation);
        }
        if (completion is not null)
        {
            _ = CompleteDisposalAsync(completion);
        }
        return new(shared);
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        await drained.Task.ConfigureAwait(false);
        Exception[] failures;
        lock (gate)
        {
            failures = cleanupFailures.ToArray();
        }
        if (failures.Length == 0)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(failures.Length == 1 ? failures[0]
                : new AggregateException("Native generation cleanup failed.", failures));
        }
    }

    private void SignalDrained()
    {
        if (closing && !constructing && generations.Count == 0)
        {
            drained.TrySetResult();
        }
    }

    private void ThrowIfClosed()
    {
        if (cleanupFailures.Count != 0)
        {
            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
        }
        ObjectDisposedException.ThrowIf(closing, this);
    }

    private static RestCredentialException CredentialFailure(SecurityFailureKind kind) => new(new(kind,
        kind == SecurityFailureKind.CredentialExpired ? "The bound client certificate has expired."
            : kind == SecurityFailureKind.CredentialInvalid ? "The bound client certificate is not usable."
            : "No usable bound client certificate is available."));

    private static Exception Combine(Exception? original, Exception cleanup) => original is null ? cleanup
        : new AggregateException("Owned operations and cleanup both failed.", original, cleanup);

    private static RestEncodingFaultException? FindEncodingFault(Exception error)
    {
        for (Exception? currentError = error; currentError is not null;
            currentError = currentError is HttpRequestException or IOException ? currentError.InnerException : null)
        {
            if (currentError is RestEncodingFaultException encoding)
            {
                return encoding;
            }
        }
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

    private sealed class Generation(HttpClient client, RestHttpCertificateMaterial? material, X509Certificate2[] roots)
    {
        internal HttpClient Client { get; } = client;
        internal RestHttpCertificateMaterial? Material { get; } = material;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Holds;
        internal bool Retired;
        internal bool DisposalStarted;

        internal void DisposeNative()
        {
            Exception? failure = null;
            try
            {
                Client.Dispose();
            }
            catch (Exception error) { failure = error; }
            try
            {
                Material?.Dispose();
            }
            catch (Exception error) { failure = Combine(failure, error); }
            foreach (X509Certificate2 root in roots)
            {
                try
                {
                    root.Dispose();
                }
                catch (Exception error) { failure = Combine(failure, error); }
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }

    private sealed class Dispatch(Action callback)
    {
        private Action? callback = callback;
        internal bool Entered;

        internal void Enter()
        {
            Action invoke = Interlocked.Exchange(ref callback, null)
                ?? throw new InvalidOperationException("A primary boundary is entered once per exchange.");
            invoke();
            Entered = true;
        }
    }

    private sealed class PrimaryHandler(HttpMessageHandler primary) : DelegatingHandler(primary)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!request.Options.TryGetValue(DispatchKey, out Dispatch? boundary))
            {
                throw new InvalidOperationException("An owned primary dispatch boundary is required.");
            }
            boundary.Enter();
            return base.SendAsync(request, token);
        }
    }

    private sealed class Response : IRestResponse
    {
        private readonly object gate = new();
        private readonly HttpResponseMessage native;
        private Func<ValueTask>? release;
        private Task? disposal;

        internal Response(HttpResponseMessage native, Stream body, Func<ValueTask> release)
        {
            this.native = native;
            this.release = release;
            Body = body;
            HttpStatusCode = (int)native.StatusCode;
            var copied = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in native.Headers.Concat(native.Content.Headers))
            {
                string[] values = pair.Value.ToArray();
                copied[pair.Key] = Array.AsReadOnly(copied.TryGetValue(pair.Key, out var previous) ? previous.Concat(values).ToArray() : values);
            }
            Headers = new ReadOnlyDictionary<string, IReadOnlyList<string>>(copied);
        }

        public int HttpStatusCode
        {
            get;
        }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers
        {
            get;
        }
        public Stream Body
        {
            get;
        }

        public ValueTask DisposeAsync()
        {
            TaskCompletionSource? completion = null;
            Task shared;
            lock (gate)
            {
                if (disposal is null)
                {
                    completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    disposal = completion.Task;
                }
                shared = disposal;
            }
            if (completion is not null)
            {
                _ = DisposeCoreAsync(completion);
            }
            return new(shared);
        }

        private async Task DisposeCoreAsync(TaskCompletionSource completion)
        {
            Exception? failure = null;
            try
            {
                native.Dispose();
            }
            catch (Exception error) { failure = error; }
            Func<ValueTask> ownedRelease = Interlocked.Exchange(ref release, null)!;
            try
            {
                await ownedRelease().ConfigureAwait(false);
            }
            catch (Exception error) { failure = Combine(failure, error); }
            if (failure is null)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(failure);
            }
        }
    }
}
