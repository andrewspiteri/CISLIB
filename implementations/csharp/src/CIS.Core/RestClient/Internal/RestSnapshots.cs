namespace CIS.RestClient;

internal sealed record RestLimitsSnapshot(long MaxResponseBytes, int MaxErrorBodyBytes, int MaxErrorMessageScalars)
{
    internal static RestLimitsSnapshot Capture(RestLimits limits, RestLimitsSnapshot? maximum = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var copied = new RestLimitsSnapshot(limits.MaxResponseBytes, limits.MaxErrorBodyBytes, limits.MaxErrorMessageScalars);
        if (copied.MaxResponseBytes <= 0 || copied.MaxErrorBodyBytes < 0 || copied.MaxErrorMessageScalars <= 0
            || maximum is not null && (copied.MaxResponseBytes > maximum.MaxResponseBytes
                || copied.MaxErrorBodyBytes > maximum.MaxErrorBodyBytes || copied.MaxErrorMessageScalars > maximum.MaxErrorMessageScalars))
        {
            throw new ArgumentException("Use valid processing limits no larger than the registered client policy.", nameof(limits));
        }
        return copied;
    }
}

// Immutable registration snapshot. It deliberately borrows configured extension
// instances; they are concurrency-safe and never disposed by a call.
internal sealed class RestClientSnapshot
{
    private static readonly string[] FramingHeaders = ["Host", "Content-Length", "Content-Type", "Transfer-Encoding", "Connection", "Upgrade", "Trailer", "TE"];
    private RestClientSnapshot(RestClientOptions options, bool deferredAuthentication)
    {
        if (!RestContractRules.Alias(options.ClientName))
        {
            throw new ArgumentException("Use a bounded client alias.");
        }
        RestContractRules.Timeout(options.Timeout, nameof(options.Timeout));
        if (!Enum.IsDefined(options.ResponseFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(options.ResponseFormat));
        }
        ClientName = options.ClientName;
        BaseUrl = options.BaseUrl;
        if (BaseUrl is not null && (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0))
        {
            throw new ArgumentException("Use an absolute HTTP(S) base without embedded credentials.", nameof(options.BaseUrl));
        }
        Timeout = options.Timeout;
        Limits = RestLimitsSnapshot.Capture(options.Limits);
        RetryPolicy = ValidateSelection(options.RetryPolicy);
        ResponseFormat = options.ResponseFormat;
        RequireValue = options.RequireValue;
        ForwardTraceId = options.ForwardTraceId;
        LogCalls = options.LogCalls;
        ConfigureRequest = options.ConfigureRequest;
        Authenticate = options.Authenticate;
        SecurityBinding = options.SecurityBinding;
        Codec = options.Codec;
        ResponseMapper = options.ResponseMapper;
        var headers = new RestHeaders();
        options.Headers.ApplyTo(headers, default);
        Headers = headers.Snapshot();
        ValidateHeaders(Headers);
        string[] operations = Copy(options.Operations, 31, "operations");
        if (operations.Any(name => !RestContractRules.Alias(name)) || operations.Contains("request", StringComparer.Ordinal)
            || operations.Distinct(StringComparer.Ordinal).Count() != operations.Length)
        {
            throw new ArgumentException("Use distinct bounded operation aliases; request is automatically included.");
        }
        Operations = Array.AsReadOnly(new[] { "request" }.Concat(operations).ToArray());
        string[] origins = Copy(options.AllowedOrigins, 32, "allowedOrigins");
        ExplicitOrigins = origins.Length != 0;
        if (origins.Length == 0 && BaseUrl is not null)
        {
            origins = [new Uri(BaseUrl).GetLeftPart(UriPartial.Authority)];
        }
        var normalized = new List<string>();
        foreach (string origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? originUri) || originUri.Scheme is not ("http" or "https")
                || originUri.UserInfo.Length != 0 || originUri.AbsolutePath != "/" || originUri.Query.Length != 0 || originUri.Fragment.Length != 0)
            {
                throw new ArgumentException("Configure origins without credentials, path, query or fragment.");
            }
            string value = originUri.GetLeftPart(UriPartial.Authority);
            if (normalized.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Allowed origins must be distinct.");
            }
            normalized.Add(value);
        }
        if (Authenticate is not null && normalized.Count == 0)
        {
            throw new ArgumentException("Inherited authentication without a base needs explicit allowed origins.");
        }
        AllowedOrigins = Array.AsReadOnly(normalized.ToArray());
        if (SecurityBinding is not null)
        {
            ApplySecurityBinding(SecurityBinding, deferredAuthentication);
        }
        CredentialHeaders = Array.AsReadOnly(Copy(options.CredentialHeaders, 32, "credentialHeaders"));
        CredentialQueryParameters = Array.AsReadOnly(Copy(options.CredentialQueryParameters, 32, "credentialQueryParameters"));
        if (CredentialHeaders.Any(name => !RestContractRules.Method(name))
            || CredentialHeaders.Any(name => FramingHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
            || CredentialHeaders.Contains("Idempotency-Key", StringComparer.OrdinalIgnoreCase)
            || CredentialHeaders.Distinct(StringComparer.OrdinalIgnoreCase).Count() != CredentialHeaders.Count
            || CredentialQueryParameters.Any(string.IsNullOrEmpty)
            || CredentialQueryParameters.Distinct(StringComparer.Ordinal).Count() != CredentialQueryParameters.Count)
        {
            throw new ArgumentException("Use distinct valid credential field names.");
        }
        if (AllowedOrigins.Count == 0 && Headers.Any(pair => pair.Value.Count != 0
            && (new[] { "Authorization", "Proxy-Authorization", "Cookie" }.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
                || CredentialHeaders.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))))
        {
            throw new ArgumentException("Inherited credential headers without a base need explicit allowed origins.");
        }
        IRestExchangeHandler[] handlers = Copy(options.Handlers, 32, "handlers");
        if (handlers.Any(handler => handler is null))
        {
            throw new ArgumentException("Handlers cannot be null.");
        }
        Handlers = Array.AsReadOnly(handlers);
    }
    internal static RestClientSnapshot Capture(RestClientOptions options, bool deferredAuthentication = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(options, deferredAuthentication);
    }
    internal RestClientSnapshot WithSecurityBinding(RestSecurityBinding binding,
        Func<RestAuthenticationContext, CancellationToken, Task>? authenticate)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (SecurityBinding is not null && !ReferenceEquals(SecurityBinding, binding))
        {
            throw new InvalidOperationException("The deferred binding cannot replace an already selected security owner.");
        }
        var copied = (RestClientSnapshot)MemberwiseClone();
        copied.Authenticate = authenticate ?? Authenticate;
        copied.ApplySecurityBinding(binding, deferredAuthentication: false);
        return copied;
    }

    private void ApplySecurityBinding(RestSecurityBinding binding, bool deferredAuthentication)
    {
        if (ExplicitOrigins && !binding.HasExactOrigins(AllowedOrigins)
            || BaseUrl is not null && !binding.Allows(new Uri(BaseUrl)))
        {
            throw new ArgumentException("The REST client must retain its security binding's exact HTTPS destination policy.");
        }
        if (!deferredAuthentication && binding.CredentialKind == RestSecurityCredentialKind.RequestAuthentication
            && Authenticate is null)
        {
            throw new ArgumentException("Request-authenticated security bindings require their selected final callback.");
        }
        SecurityBinding = binding;
        AllowedOrigins = binding.AllowedOrigins;
    }

    private bool ExplicitOrigins
    {
        get;
    }
    internal RestSecurityBinding? SecurityBinding
    {
        get; private set;
    }
    internal string ClientName
    {
        get;
    }
    internal string? BaseUrl
    {
        get;
    }
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> Headers
    {
        get;
    }
    internal TimeSpan Timeout
    {
        get;
    }
    internal RestLimitsSnapshot Limits
    {
        get;
    }
    internal RestRetrySelection RetryPolicy
    {
        get;
    }
    internal RestResponseFormat ResponseFormat
    {
        get;
    }
    internal bool RequireValue
    {
        get;
    }
    internal bool LogCalls
    {
        get;
    }
    internal bool? ForwardTraceId
    {
        get;
    }
    internal Func<RestRequestContext, CancellationToken, Task>? ConfigureRequest
    {
        get;
    }
    internal Func<RestAuthenticationContext, CancellationToken, Task>? Authenticate
    {
        get; private set;
    }
    internal IRestContentCodec? Codec
    {
        get;
    }
    internal IRestResponseMapper? ResponseMapper
    {
        get;
    }
    internal IReadOnlyList<string> Operations
    {
        get;
    }
    internal IReadOnlyList<string> AllowedOrigins
    {
        get; private set;
    }
    internal IReadOnlyList<string> CredentialHeaders
    {
        get;
    }
    internal IReadOnlyList<string> CredentialQueryParameters
    {
        get;
    }
    internal IReadOnlyList<IRestExchangeHandler> Handlers
    {
        get;
    }
    internal string NormalizeOperation(string? name) => name is not null && Operations.Contains(name, StringComparer.Ordinal) ? name : "request";
    internal static RestRetrySelection ValidateSelection(RestRetrySelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.PolicyName is not null && !RestContractRules.Alias(selection.PolicyName))
        {
            throw new ArgumentException("Use a registered bounded policy alias.");
        }
        return selection;
    }
    internal static void ValidateHeaders(IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
    {
        foreach (var header in headers)
        {
            if (!RestContractRules.Method(header.Key) || header.Value.Any(value => value is null || value.Contains('\r') || value.Contains('\n')))
            {
                throw new ArgumentException("Use valid header tokens and values without line breaks.");
            }
        }
    }
    private static T[] Copy<T>(IList<T> values, int maximum, string name)
    {
        int count = values.Count;
        if (count > maximum)
        {
            throw new ArgumentException("The configured collection exceeds its finite limit.", name);
        }
        var copied = new T[count];
        for (int index = 0; index < count; index++)
        {
            copied[index] = values[index];
        }
        return copied;
    }
}

// Captured under the already-running operation deadline; mutable request inputs
// are not retained except the explicitly borrowed body/extension instances.
internal sealed class RestCallSnapshot
{
    private RestCallSnapshot(RestClientSnapshot client, RestRequestOptions? input, CancellationToken token, TimeSpan? capturedTimeout)
    {
        token.ThrowIfCancellationRequested();
        if (client.SecurityBinding is not null && (input?.DisableAuthentication == true || input?.Authenticate is not null))
        {
            throw new ArgumentException("A security-bound REST call cannot suppress or replace its selected authentication.", nameof(input));
        }
        if (input is not null && (input.DisableAuthentication && input.Authenticate is not null
            || input.DisableConfigureRequest && input.ConfigureRequest is not null))
        {
            throw new ArgumentException("A replacement callback and its disable flag are mutually exclusive.");
        }
        Timeout = capturedTimeout ?? input?.Timeout ?? client.Timeout;
        RestContractRules.Timeout(Timeout, nameof(RestRequestOptions.Timeout));
        Limits = input?.Limits is { } limits ? RestLimitsSnapshot.Capture(limits, client.Limits) : client.Limits;
        RetryPolicy = RestClientSnapshot.ValidateSelection(input?.RetryPolicy ?? client.RetryPolicy);
        ResponseFormat = input?.ResponseFormat ?? client.ResponseFormat;
        ReplaySafety = input?.ReplaySafety ?? RestReplaySafety.Default;
        if (!Enum.IsDefined(ResponseFormat) || !Enum.IsDefined(ReplaySafety))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }
        RequireValue = input?.RequireValue ?? client.RequireValue;
        ForwardTraceId = input?.ForwardTraceId ?? client.ForwardTraceId;
        OperationName = client.NormalizeOperation(input?.OperationName);
        Body = input?.Body;
        ConfigureRequest = input?.DisableConfigureRequest == true ? null : input?.ConfigureRequest ?? client.ConfigureRequest;
        Authenticate = input?.DisableAuthentication == true ? null : input?.Authenticate ?? client.Authenticate;
        AuthenticationExplicit = input?.Authenticate is not null;
        ExplicitHeaderNames = Array.AsReadOnly(input?.Headers.Snapshot().Keys.ToArray() ?? []);
        ResponseMapper = input?.ResponseMapper ?? client.ResponseMapper;
        var headers = new RestHeaders();
        foreach (var pair in client.Headers)
        {
            token.ThrowIfCancellationRequested();
            headers.Set(pair.Key, pair.Value.ToArray());
        }
        input?.Headers.ApplyTo(headers, token);
        Headers = headers.Snapshot();
        RestClientSnapshot.ValidateHeaders(Headers);
        Query = input?.Query.CaptureChanges(token) ?? [];
        token.ThrowIfCancellationRequested();
    }
    internal static RestCallSnapshot Capture(RestClientSnapshot client, RestRequestOptions? input, CancellationToken token = default,
        TimeSpan? capturedTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new(client, input, token, capturedTimeout);
    }
    internal TimeSpan Timeout
    {
        get;
    }
    internal RestLimitsSnapshot Limits
    {
        get;
    }
    internal RestRetrySelection RetryPolicy
    {
        get;
    }
    internal RestResponseFormat ResponseFormat
    {
        get;
    }
    internal RestReplaySafety ReplaySafety
    {
        get;
    }
    internal bool RequireValue
    {
        get;
    }
    internal bool? ForwardTraceId
    {
        get;
    }
    internal string OperationName
    {
        get;
    }
    internal RestBody? Body
    {
        get;
    }
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> Headers
    {
        get;
    }
    internal IReadOnlyList<RestQueryChange> Query
    {
        get;
    }
    internal Func<RestRequestContext, CancellationToken, Task>? ConfigureRequest
    {
        get;
    }
    internal Func<RestAuthenticationContext, CancellationToken, Task>? Authenticate
    {
        get;
    }
    internal bool AuthenticationExplicit
    {
        get;
    }
    internal IReadOnlyList<string> ExplicitHeaderNames
    {
        get;
    }
    internal IRestResponseMapper? ResponseMapper
    {
        get;
    }
}
