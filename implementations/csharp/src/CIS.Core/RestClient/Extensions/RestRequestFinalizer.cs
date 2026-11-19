namespace CIS.RestClient;

internal static class RestRequestFinalizer
{
    private static readonly string[] IntrinsicCredentials = ["Authorization", "Proxy-Authorization", "Cookie"];
    internal static RestRequestContext Create(RestClientSnapshot client, RestCallSnapshot call, string method, string url, int iteration)
    {
        var headers = new RestHeaders();
        foreach (var pair in call.Headers)
        {
            headers.Set(pair.Key, pair.Value.ToArray());
        }
        headers.MarkBaseline();
        var query = new RestQuery();
        foreach (RestQueryChange change in call.Query)
        {
            if (change.Kind == RestQueryChangeKind.Set)
            {
                query.Set(change.Name, change.Value);
            }
            else if (change.Kind == RestQueryChangeKind.Append)
            {
                query.Append(change.Name, change.Value);
            }
            else
            {
                query.Remove(change.Name);
            }
        }
        return new(method, RestUri.Resolve(client.BaseUrl, url).AbsoluteUri,
            new(client.ClientName, call.OperationName, iteration), headers, query, call.Body,
            client.BaseUrl is not null && (url.Length == 0 || url.StartsWith('#')));
    }

    internal static async ValueTask<RestDeclaredFailure?> PrepareAsync(RestRequestContext request,
        RestClientSnapshot client, RestCallSnapshot call, Func<CancellationToken, ValueTask> prepareContent,
        Func<int, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> signingBytes, CancellationToken cancellationToken,
        Action? prepareNativeRepresentation = null, bool captureUnexpectedCancellation = false, CancellationToken knownCallerToken = default,
        string? capturedTraceParent = null)
    {
        try
        {
            if (call.ConfigureRequest is not null)
            {
                await call.ConfigureRequest(request, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Failure is not null)
            {
                return request.Failure;
            }
            RestDeclaredFailure? failure = Validate(request, client, call, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }
            RestBody? preparedBody = request.Body;
            await prepareContent(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Failure is not null)
            {
                return request.Failure;
            }
            if (!ReferenceEquals(preparedBody, request.Body))
            {
                return Preparation("Content acquisition cannot replace its request body descriptor.");
            }
            // Application content factories may retain the request. Recheck all
            // actual fields after acquisition and before granting credential writes.
            failure = Validate(request, client, call, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }
            RestTraceForwarding.Apply(request, call.ForwardTraceId, capturedTraceParent);
            prepareNativeRepresentation?.Invoke();
            request.FinalizeOrdinary(CredentialHeaders(client), client.CredentialQueryParameters);
            if (!string.Equals(request.Method, "TRACE", StringComparison.OrdinalIgnoreCase) && call.Authenticate is not null)
            {
                var authentication = new RestAuthenticationContext(request, signingBytes,
                    () => RestUri.Resolve(null, request.Url, request.Query.CaptureChanges(default)).AbsoluteUri);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo? fault = null;
                try
                {
                    await call.Authenticate(authentication, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    fault = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(RestCallbackFaultException.Capture(error, cancellationToken, captureUnexpectedCancellation, knownCallerToken));
                }
                try
                {
                    await authentication.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    if (fault is null || IsCancellation(fault.SourceException, cancellationToken) && !IsCancellation(error, cancellationToken))
                    {
                        fault = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
                    }
                }
                fault?.Throw();
                cancellationToken.ThrowIfCancellationRequested();
                if (authentication.Failure is not null)
                {
                    return authentication.Failure;
                }
            }
            RestClientSnapshot.ValidateHeaders(request.Headers.Snapshot());
            request.Seal(RestUri.Resolve(null, request.Url, request.Query.CaptureChanges(cancellationToken)).AbsoluteUri);
            request.Query.ClearApplied();
            return null;
        }
        finally { request.Seal(); }
    }

    private static RestDeclaredFailure? Validate(RestRequestContext request, RestClientSnapshot client, RestCallSnapshot call, CancellationToken token)
    {
        if (!RestContractRules.Method(request.Method))
        {
            return Preparation("Use a valid native method token.");
        }
        if (client.SecurityBinding is not null && (!string.Equals(request.Method, request.OriginalMethod, StringComparison.Ordinal)
            || string.Equals(request.Method, "TRACE", StringComparison.OrdinalIgnoreCase)))
        {
            return Preparation("A security-bound call cannot replace its method or use TRACE.");
        }
        if (string.Equals(request.Method, "CONNECT", StringComparison.OrdinalIgnoreCase))
        {
            return Unsupported("The native profile does not support CONNECT tunneling.");
        }
        Uri destination;
        bool trace = string.Equals(request.Method, "TRACE", StringComparison.OrdinalIgnoreCase);
        try
        {
            IReadOnlyList<RestQueryChange> changes = request.Query.CaptureChanges(token);
            if (trace && request.InheritedBaseQuery && !request.UrlChanged)
            {
                RestQueryChange[] removals = client.CredentialQueryParameters.Select(name => new RestQueryChange(RestQueryChangeKind.Remove, name, null)).ToArray();
                destination = RestUri.Resolve(client.BaseUrl, request.Url, removals);
                destination = RestUri.Resolve(null, destination.AbsoluteUri, changes);
            }
            else
            {
                destination = RestUri.Resolve(client.BaseUrl, request.Url, changes);
            }
            RestClientSnapshot.ValidateHeaders(request.Headers.Snapshot());
        }
        catch (ArgumentException error) { return Preparation(error.Message); }
        request.Url = destination.AbsoluteUri;
        request.Query.ClearApplied();
        if (!RestUri.AllowedOrigin(client, destination) || call.Authenticate is not null && client.AllowedOrigins.Count == 0
            || client.SecurityBinding is { } binding && !binding.Allows(destination))
        {
            return Preparation("The finalized destination is outside the configured credential origins.");
        }
        var headers = request.Headers.Snapshot();
        if (headers.ContainsKey("Upgrade") || headers.TryGetValue("Connection", out var connection)
            && connection.SelectMany(value => value.Split(',')).Any(value => string.Equals(value.Trim(), "upgrade", StringComparison.OrdinalIgnoreCase)))
        {
            return Unsupported("Protocol upgrades require an unsupported channel capability.");
        }
        if (headers.TryGetValue("Host", out var hosts) && (hosts.Count != 1 || !HostMatches(destination, hosts[0])))
        {
            return Preparation("Host must retain the finalized destination origin.");
        }
        if (trace)
        {
            if (request.Body is not null || call.AuthenticationExplicit || RestUri.ContainsQueryField(destination, client.CredentialQueryParameters))
            {
                return Preparation("TRACE cannot contain a body or explicit credentials.");
            }
            foreach (string name in CredentialHeaders(client))
            {
                if (headers.TryGetValue(name, out var values) && values.Count != 0
                    && (call.ExplicitHeaderNames.Contains(name, StringComparer.OrdinalIgnoreCase) || request.Headers.ChangedSinceBaseline(name)))
                {
                    return Preparation("TRACE cannot contain explicit or customized credential fields.");
                }
                request.Headers.Remove(name);
            }
        }
        return null;
    }
    private static string[] CredentialHeaders(RestClientSnapshot client)
        => IntrinsicCredentials.Concat(client.CredentialHeaders).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static bool IsCancellation(Exception error, CancellationToken token)
        => error is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested;
    private static RestDeclaredFailure Preparation(string explanation) => new(RestFailureKind.Preparation, "client.preparation_failed", explanation);
    private static RestDeclaredFailure Unsupported(string explanation) => new(RestFailureKind.Preparation, "client.unsupported_operation", explanation);
    private static bool HostMatches(Uri destination, string host)
        => Uri.TryCreate(destination.Scheme + "://" + host, UriKind.Absolute, out Uri? supplied)
            && supplied.UserInfo.Length == 0 && supplied.AbsolutePath == "/" && supplied.Query.Length == 0 && supplied.Fragment.Length == 0
            && string.Equals(destination.GetLeftPart(UriPartial.Authority), supplied.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}
