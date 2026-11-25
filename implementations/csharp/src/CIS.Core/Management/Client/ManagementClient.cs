using System.Globalization;
using System.Net.Http.Headers;
using CIS.Management.Wire;
using CIS.RestClient;

namespace CIS.Management;

/// <summary>A typed client borrowing the selected REST/authentication owner; it starts no registration or background work.</summary>
public sealed class ManagementClient : IManagementClient
{
    private readonly IRestClient rest;
    private readonly ManagementClientSnapshot settings;
    private readonly RestRetryPolicy[] registeredPolicies;
    private readonly TimeProvider clock;

    /// <summary>Captures detached options and the actual registered policy instances without constructing another provider or transport.</summary>
    public ManagementClient(IRestClient rest, ManagementClientOptions options,
        IEnumerable<RestRetryPolicy>? registeredReadPolicies = null, TimeProvider? clock = null)
        : this(rest, ManagementClientSnapshot.Capture(options), registeredReadPolicies, clock)
    {
    }

    internal ManagementClient(IRestClient rest, ManagementClientSnapshot settings,
        IEnumerable<RestRetryPolicy>? registeredReadPolicies, TimeProvider? clock)
    {
        ArgumentNullException.ThrowIfNull(rest);
        this.rest = rest;
        this.settings = settings;
        this.clock = clock ?? TimeProvider.System;
        registeredPolicies = (registeredReadPolicies ?? []).Take(33).ToArray();
        if (registeredPolicies.Length > 32 || registeredPolicies.Any(policy => policy is null)
            || registeredPolicies.Select(policy => policy.Name).Distinct(StringComparer.Ordinal).Count() != registeredPolicies.Length)
        {
            throw new ArgumentException("Supply the actual finite native retry registrations.", nameof(registeredReadPolicies));
        }
        SelectReadPolicy(settings.ReadRetryPolicy);
    }

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementServiceInfo>> GetServiceInfoAsync(ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementServiceInfo>("GET", "service-info", "service-info", null, null, null, null, null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementRegistrationReceipt>> RegisterAsync(string registrationId, ManagementRegistrationRequest request,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementRegistrationReceipt>("PUT", "registration", "registrations", registrationId, null,
            request, null, null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementControlConfirmation>> GetControlAsync(string registrationId, string attemptId,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementControlConfirmation>("GET", "control", "registrations", registrationId, "control",
            null, null, null, null, cancellationToken, attemptId);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementReportReceipt>> ReportObservationAsync(string registrationId, ManagementObservation observation,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementReportReceipt>("PUT", "observation", "registrations", registrationId, "observation",
            observation, null, null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementReportReceipt>> ReportInventoryAsync(string registrationId, ManagementInventorySnapshot inventory,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementReportReceipt>("PUT", "inventory-report", "registrations", registrationId, "inventory",
            inventory, null, null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementCloseReceipt>> CloseRegistrationAsync(string registrationId, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementCloseReceipt>("DELETE", "registration-close", "registrations", registrationId, null,
            null, null, null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementApplication>>> ListApplicationsAsync(ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementApplication>>("GET", "applications", "applications", null, null,
            null, page ?? new(), null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementApplication>>> QueryApplicationsAsync(ManagementApplicationQuery query,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementApplication>>("GET", "applications", "applications", null, null,
            null, page ?? new(), null, options, cancellationToken, query: query ?? throw new ArgumentNullException(nameof(query)));

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementEnvironment>>> ListEnvironmentsAsync(ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementEnvironment>>("GET", "applications", "environments", null, null,
            null, page ?? new(), null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementApplication>> GetApplicationAsync(string applicationId, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementApplication>("GET", "application", "applications", applicationId, null,
            null, null, null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementInstance>>> ListInstancesAsync(string applicationId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementInstance>>("GET", "instances", "applications", applicationId, "instances",
            null, page ?? new(), null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementInventoryEntry>>> ListInventoryAsync(string applicationId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementInventoryEntry>>("GET", "inventory", "applications", applicationId, "inventory",
            null, page ?? new(), null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementCapabilities>> GetCapabilitiesAsync(string applicationId, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementCapabilities>("GET", "capabilities", "applications", applicationId, "capabilities",
            null, null, null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementMetadataResult>> ReplaceMetadataAsync(string applicationId, string operationKey,
        string ifMatch, ManagementMetadataCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementMetadataResult>("PUT", "metadata-replace", "applications", applicationId, "metadata",
            command, null, new(operationKey, ifMatch), options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementMetadataResult>> GetMetadataOperationAsync(string applicationId, string operationKey,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementMetadataResult>("GET", "metadata-operation", "applications", applicationId, "metadata-operations",
            null, null, new(operationKey, null), options, cancellationToken);

    private async Task<ManagementCallResult<T>> CallAsync<T>(string method, string operation, string collection,
        string? id, string? suffix, object? body, ManagementPageRequest? page, CommandHeaders? command,
        ManagementCallOptions? options, CancellationToken cancellationToken, string? controlAttemptId = null,
        ManagementApplicationQuery? query = null) where T : class
    {
        long entered = clock.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        bool requiresId = operation is not ("service-info" or "applications");
        if (requiresId && id is null || id is not null && !ManagementWire.IsUuid(id))
        {
            throw new ArgumentException("Supply a canonical application or registration UUID.", nameof(id));
        }
        if (operation == "control" && !ManagementWire.IsUuid(controlAttemptId))
        {
            throw new ArgumentException("Supply one canonical per-attempt control UUID.", nameof(controlAttemptId));
        }
        if (page is not null && !ManagementWire.IsPageRequest(page))
        {
            throw new ArgumentException("Supply a bounded page size and opaque cursor.", nameof(page));
        }
        if (query is not null && (query.Query is not null && !ManagementWire.IsScalarText(query.Query, 128)
            || query.Environment is not null && !ManagementWire.IsScalarText(query.Environment, 255)))
        {
            throw new ArgumentException("Supply bounded literal application and exact environment filters.", nameof(query));
        }
        if (command is not null && (!ManagementWire.IsOperationKey(command.Key)
            || method == "PUT" && !ManagementWire.IsStrongETag(command.IfMatch)))
        {
            throw new ArgumentException("Supply the original safe operation key and one strong metadata ETag.", nameof(command));
        }
        if (method == "PUT" && body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }
        TimeSpan budget = options?.Timeout ?? settings.Timeout;
        RestContractRules.Timeout(budget, nameof(options));
        string path = "management/v1/" + collection;
        if (id is not null)
        {
            path += "/" + Uri.EscapeDataString(id);
        }
        if (suffix is not null)
        {
            path += "/" + suffix;
        }
        if (suffix == "metadata-operations")
        {
            path += "/" + Uri.EscapeDataString(command!.Key);
        }
        var mapper = new ProblemMapper(settings.WireLimits, operation == "control" ? id : null, controlAttemptId);
        var request = new RestRequestOptions
        {
            OperationName = operation,
            ResponseFormat = RestResponseFormat.Json,
            RequireValue = true,
            DisableConfigureRequest = true,
            RetryPolicy = method == "GET" && operation != "control"
                ? SelectReadPolicy(options?.ReadRetryPolicy ?? settings.ReadRetryPolicy) : RestRetrySelection.None,
            ResponseMapper = mapper,
            Limits = new RestLimits
            {
                MaxResponseBytes = typeof(T) == typeof(ManagementPage<ManagementInventoryEntry>)
                    ? settings.WireLimits.MaximumInventoryResponseBytes : settings.WireLimits.MaximumRegularResponseBytes,
                MaxErrorBodyBytes = ManagementWire.MaximumErrorBodyBytes,
                MaxErrorMessageScalars = 1024
            },
            Body = body switch
            {
                ManagementRegistrationRequest registration => RestBody.Json(registration),
                ManagementObservation observation => RestBody.Json(observation),
                ManagementInventorySnapshot inventory => RestBody.Json(inventory),
                ManagementMetadataCommand metadata => RestBody.Json(metadata),
                null => null,
                _ => throw new ArgumentException("Select one typed management body.", nameof(body))
            }
        };
        request.Headers.Set("Accept", "application/json, application/problem+json");
        if (query?.Query is not null)
        {
            request.Query.Set("query", query.Query);
        }
        if (query?.Environment is not null)
        {
            request.Query.Set("environment", query.Environment);
        }
        if (controlAttemptId is not null)
        {
            request.Query.Set("attemptId", controlAttemptId);
            request.Headers.Set("Cache-Control", "no-store");
        }
        if (page is not null)
        {
            request.Query.Set("pageSize", page.PageSize.ToString(CultureInfo.InvariantCulture));
            if (page.Cursor is not null)
            {
                request.Query.Set("cursor", page.Cursor);
            }
        }
        if (command?.IfMatch is not null)
        {
            request.Headers.Set("Idempotency-Key", command.Key);
            request.Headers.Set("If-Match", command.IfMatch);
        }
        TimeSpan remaining = budget - clock.GetElapsedTime(entered);
        cancellationToken.ThrowIfCancellationRequested();
        if (remaining <= TimeSpan.Zero)
        {
            var timeout = RestResult<T>.Failed("client.timeout", "The management entry budget elapsed before dispatch.",
                new RestResultContext(failureKind: RestFailureKind.Timeout, clientName: settings.RestClientName, operationName: operation));
            return new(timeout, operationKey: command?.Key);
        }
        request.Timeout = remaining;
        RestResult<T> exchange = await rest.Send<T>(method, new Uri(settings.BaseUri, path).AbsoluteUri, request, cancellationToken).ConfigureAwait(false);
        ManagementProblem? problem = !exchange.Success && !exchange.Context.ErrorBodyTruncated
            && exchange.Context.HttpStatusCode == mapper.Problem?.Status
            && ReferenceEquals(exchange.Context.ErrorBody, mapper.Body) ? mapper.Problem : null;
        return new(exchange, problem, command?.Key);
    }

    private RestRetrySelection SelectReadPolicy(RestRetryPolicy? policy)
    {
        ManagementRetryRules.Validate(policy);
        if (policy is null)
        {
            return RestRetrySelection.None;
        }
        if (!registeredPolicies.Any(registered => ReferenceEquals(registered, policy)))
        {
            throw new InvalidOperationException("Select an actually registered native policy instance.");
        }
        return RestRetrySelection.Named(policy.Name);
    }

    private sealed record CommandHeaders(string Key, string? IfMatch);

    private sealed class ProblemMapper(ManagementWireLimits limits, string? registrationId, string? attemptId) : IRestResponseMapper
    {
        internal ManagementProblem? Problem
        {
            get; private set;
        }
        internal RestErrorBody? Body
        {
            get; private set;
        }

        public ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken cancellationToken)
        {
            Problem = null;
            Body = null;
            cancellationToken.ThrowIfCancellationRequested();
            if (response.HttpStatusCode is >= 200 and <= 299 && registrationId is not null
                && response.Decoded.Value is ManagementControlConfirmation control
                && (control.RegistrationId != registrationId || control.AttemptId != attemptId))
            {
                return ValueTask.FromResult(RestResponseDecision.RejectBusiness(
                    "The control confirmation does not echo this registration and attempt.",
                    "management.control.invalid_confirmation"));
            }
            RestErrorBody? body = response.ErrorBody;
            if (response.HttpStatusCode is >= 200 and <= 299 || body is null || body.Bytes.Count > ManagementWire.MaximumErrorBodyBytes
                || !MediaTypeHeaderValue.TryParse(body.MediaType, out MediaTypeHeaderValue? media)
                || !string.Equals(media.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }
            ManagementProblem? problem = ManagementWire.DecodeResponse<ManagementProblem>(body.Bytes.ToArray(), limits);
            cancellationToken.ThrowIfCancellationRequested();
            if (problem?.Status != response.HttpStatusCode)
            {
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }
            Problem = problem;
            Body = body;
            // A full-cap prefix may have a retained probe beyond it. The final native
            // truncation fact decides whether this candidate is exposed at all.
            return ValueTask.FromResult(body.Bytes.Count == ManagementWire.MaximumErrorBodyBytes
                ? RestResponseDecision.Continue : RestResponseDecision.RemoteError(problem.Code, problem.Detail ?? problem.Title));
        }
    }
}
