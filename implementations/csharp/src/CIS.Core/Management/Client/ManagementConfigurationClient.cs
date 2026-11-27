using System.Globalization;
using System.Net.Http.Headers;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;

namespace CIS.Management;

/// <summary>Typed configuration calls over one borrowed native REST/security owner, without background work.</summary>
public sealed partial class ManagementConfigurationClient : IManagementConfigurationClient, IManagementConfigurationEditorClient
{
    internal const string OperationAlias = "configuration";
    private readonly IRestClient rest;
    private readonly ManagementConfigurationClientSnapshot settings;
    private readonly RestRetryPolicy[] registeredPolicies;
    private readonly TimeProvider clock;

    /// <summary>Captures finite options and actual native read policies without resolving or starting another owner.</summary>
    public ManagementConfigurationClient(IRestClient rest, ManagementConfigurationClientOptions options,
        IEnumerable<RestRetryPolicy>? registeredReadPolicies = null, TimeProvider? clock = null)
        : this(rest, ManagementConfigurationClientSnapshot.Capture(options), registeredReadPolicies, clock)
    {
    }

    internal ManagementConfigurationClient(IRestClient rest, ManagementConfigurationClientSnapshot settings,
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
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationDefinition>>> GetDefinitionsAsync(
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementConfigurationDefinition>>("GET", "configuration/definitions", page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationDefinition>>> GetApplicationDefinitionsAsync(
        string applicationId, ManagementPageRequest? page = null, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementConfigurationDefinition>>("GET", "applications/" + Uuid(applicationId) + "/configuration-definitions",
            page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationValidationResult>> ValidateAsync(ManagementConfigurationValidationCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationValidationResult>("POST", "configuration/validate", body: command, options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationPreview>> PreviewAsync(ManagementConfigurationPreviewCommand command,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationPreview>("POST", "configuration/preview", body: command, page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationSource>> GetSourceAsync(string sourceId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationSource>("GET", "configuration/sources/" + Uuid(sourceId), page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> PublishSourceAsync(string sourceId, string operationKey,
        string ifMatch, ManagementConfigurationPublicationCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("POST", "configuration/sources/" + Uuid(sourceId) + "/publications",
            body: command, headers: new(operationKey, ifMatch), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationPublication>> GetPublicationAsync(string publicationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationPublication>("GET", "configuration/publications/" + Uuid(publicationId), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> CreateReleaseAsync(string applicationId, string configurationId,
        string schemaVersion, string operationKey, string ifMatch, ManagementConfigurationReleaseCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("POST", Binding(applicationId, configurationId) + "/releases", schemaVersion,
            command, headers: new(operationKey, ifMatch), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationRelease>> GetReleaseAsync(string releaseId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationRelease>("GET", "configuration/releases/" + Uuid(releaseId), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationAssignmentMap>> GetAssignmentsAsync(string applicationId,
        string configurationId, string schemaVersion, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationAssignmentMap>("GET", Binding(applicationId, configurationId) + "/assignments", schemaVersion, options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> ReplaceAssignmentsAsync(string applicationId,
        string configurationId, string schemaVersion, string operationKey, string ifMatch, ManagementConfigurationAssignmentCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("PUT", Binding(applicationId, configurationId) + "/assignments", schemaVersion,
            command, headers: new(operationKey, ifMatch), options: options, token: cancellationToken);

    /// <inheritdoc />
    public async Task<ManagementConfigurationCallResult<ManagementConfigurationSnapshot>> GetRegistrationConfigurationAsync(
        ManagementConfigurationSnapshotTarget expectedTarget, ManagementConfigurationSnapshot? retainedSnapshot = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        long entered = clock.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTarget(expectedTarget);
        var mapper = new ConfigurationMapper(settings.WireLimits, expectedTarget, retainedSnapshot);
        RestResult<ManagementConfigurationSnapshotBody> exchange = await SendAsync<ManagementConfigurationSnapshotBody>(entered,
            "GET", "registrations/" + Uuid(expectedTarget.RegistrationId) + "/configuration/" + Identifier(expectedTarget.ConfigurationId),
            expectedTarget.SchemaVersion, null, null, null, options, mapper, mapper.PrepareConditionalAsync, cancellationToken).ConfigureAwait(false);
        RestResult<ManagementConfigurationSnapshot> projected = exchange.Success
            ? RestResult<ManagementConfigurationSnapshot>.FromValue(mapper.Snapshot
                ?? throw new InvalidOperationException("The selected native response mapper did not validate its snapshot."), exchange.Context)
            : RestResult<ManagementConfigurationSnapshot>.Failed(exchange.ErrorCode!, exchange.ErrorMessage!, exchange.Context);
        ManagementConfigurationContentRejection? rejection = exchange.Context.FailureKind == RestFailureKind.InvalidResponse
            && exchange.Context.HttpStatusCode is >= 200 and <= 299 && mapper.ContentRejected
            && mapper.Read?.Desired is { } desired
                ? new(desired, mapper.Read.FailureCode ?? "configuration.content.invalid") : null;
        return new(projected, FinalProblem(exchange, mapper), contentRejection: rejection);
    }

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementReportReceipt>> ReportStatusAsync(string registrationId,
        ManagementConfigurationStatusReport report, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementReportReceipt>("PUT", "registrations/" + Uuid(registrationId) + "/configuration-status", body: report, options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationAdoptionEntry>>> GetStatusAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementConfigurationAdoptionEntry>>("GET", "applications/" + Uuid(applicationId) + "/configuration-status",
            page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationCatalog>> GetCatalogAsync(ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationCatalog>("GET", "configuration/catalog", options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> ApplyCatalogAsync(string operationKey, string ifMatch,
        ManagementConfigurationCatalogCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("POST", "configuration/catalog/apply", body: command,
            headers: new(operationKey, ifMatch), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetCatalogOperationAsync(string operationKey,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("GET", "configuration/catalog/operations/" + Identifier(operationKey),
            headers: new(operationKey, null, false), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetPublicationOperationAsync(string sourceId,
        string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("GET", "configuration/sources/" + Uuid(sourceId) + "/publication-operations/" + Identifier(operationKey),
            headers: new(operationKey, null, false), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetReleaseOperationAsync(string applicationId,
        string configurationId, string schemaVersion, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("GET", Binding(applicationId, configurationId) + "/release-operations/" + Identifier(operationKey),
            schemaVersion, headers: new(operationKey, null, false), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetAssignmentOperationAsync(string applicationId,
        string configurationId, string schemaVersion, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("GET", Binding(applicationId, configurationId) + "/assignment-operations/" + Identifier(operationKey),
            schemaVersion, headers: new(operationKey, null, false), options: options, token: cancellationToken);

    private async Task<ManagementConfigurationCallResult<T>> CallAsync<T>(string method, string path, string? schema = null,
        object? body = null, ManagementPageRequest? page = null, CommandHeaders? headers = null, ManagementCallOptions? options = null,
        CancellationToken token = default) where T : class
    {
        long entered = clock.GetTimestamp();
        var mapper = new ConfigurationMapper(settings.WireLimits, responseBinding: new(path, schema, body, headers, maximumPageSize: page?.PageSize));
        RestResult<T> exchange = await SendAsync<T>(entered, method, path, schema, body, page, headers, options, mapper, null, token).ConfigureAwait(false);
        return new(exchange, FinalProblem(exchange, mapper), headers?.Key);
    }

    private async Task<RestResult<T>> SendAsync<T>(long entered, string method, string path, string? schema, object? body,
        ManagementPageRequest? page, CommandHeaders? headers, ManagementCallOptions? options, ConfigurationMapper mapper,
        Func<RestRequestContext, CancellationToken, Task>? configure, CancellationToken token, string? editorSource = null)
    {
        token.ThrowIfCancellationRequested();
        bool schemaRequired = path.StartsWith("applications/", StringComparison.Ordinal) && path.Contains("/configuration/", StringComparison.Ordinal)
            || path.StartsWith("registrations/", StringComparison.Ordinal) && path.Contains("/configuration/", StringComparison.Ordinal);
        if ((schemaRequired || schema is not null) && !ManagementWire.IsOrderedInteger(schema))
        {
            throw new ArgumentException("Supply canonical positive Int64 schema text.", nameof(schema));
        }
        if (page is not null && !ManagementWire.IsPageRequest(page))
        {
            throw new ArgumentException("Supply a bounded page/cursor.", nameof(page));
        }
        if (headers is not null && (!ManagementWire.IsOperationKey(headers.Key)
            || headers.RequiresPrecondition && !ManagementWire.IsStrongETag(headers.IfMatch)))
        {
            throw new ArgumentException("Supply the original safe key and required strong precondition.", nameof(headers));
        }
        if ((method is "POST" or "PUT") && body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }
        TimeSpan budget = options?.Timeout ?? settings.Timeout;
        RestContractRules.Timeout(budget, nameof(options));
        var request = new RestRequestOptions
        {
            OperationName = OperationAlias,
            ResponseFormat = RestResponseFormat.Json,
            RequireValue = true,
            ConfigureRequest = configure,
            DisableConfigureRequest = configure is null,
            ResponseMapper = mapper,
            RetryPolicy = method == "GET" && !mapper.IsSnapshot
                ? SelectReadPolicy(options?.ReadRetryPolicy ?? settings.ReadRetryPolicy) : RestRetrySelection.None,
            Body = Body(body)
        };
        request.Headers.Set("Accept", "application/json, application/problem+json");
        request.Headers.Set("Cache-Control", "no-store");
        if (schema is not null)
        {
            request.Query.Set("schemaVersion", schema);
        }
        if (editorSource is not null)
        {
            request.Query.Set("source", editorSource);
        }
        if (page is not null)
        {
            request.Query.Set("pageSize", page.PageSize.ToString(CultureInfo.InvariantCulture));
            if (page.Cursor is not null)
            {
                request.Query.Set("cursor", page.Cursor);
            }
        }
        if (headers?.RequiresPrecondition == true)
        {
            request.Headers.Set("Idempotency-Key", headers.Key);
            request.Headers.Set("If-Match", headers.IfMatch!);
        }
        TimeSpan remaining = budget - clock.GetElapsedTime(entered);
        token.ThrowIfCancellationRequested();
        if (remaining <= TimeSpan.Zero)
        {
            return RestResult<T>.Failed("client.timeout", "The configuration entry budget elapsed before dispatch.",
                new(failureKind: RestFailureKind.Timeout, clientName: settings.RestClientName, operationName: OperationAlias));
        }
        request.Timeout = remaining;
        return await rest.Send<T>(method, new Uri(settings.BaseUri, "management/v1/" + path).AbsoluteUri, request, token).ConfigureAwait(false);
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
            throw new InvalidOperationException("Select an actually registered native retry policy instance.");
        }
        return RestRetrySelection.Named(policy.Name);
    }

    private static RestBody? Body(object? value) => value switch
    {
        ManagementConfigurationCreationCommand command => RestBody.Json(command),
        ManagementConfigurationCandidatePreviewCommand command => RestBody.Json(command),
        ManagementConfigurationCatalogCommand command => RestBody.Json(command),
        ManagementConfigurationPublicationCommand command => RestBody.Json(command),
        ManagementConfigurationReleaseCommand command => RestBody.Json(command),
        ManagementConfigurationAssignmentCommand command => RestBody.Json(command),
        ManagementConfigurationValidationCommand command => RestBody.Json(command),
        ManagementConfigurationPreviewCommand command => RestBody.Json(command),
        ManagementConfigurationStatusReport report => RestBody.Json(report),
        null => null,
        _ => throw new ArgumentException("Select one declared configuration command.", nameof(value))
    };

    private static string Uuid(string value)
        => ManagementWire.IsUuid(value) ? value : throw new ArgumentException("Supply a canonical nonempty UUID.", nameof(value));

    private static string Identifier(string value)
        => ManagementWire.IsOperationKey(value) ? Uri.EscapeDataString(value) : throw new ArgumentException("Supply one exact portable identifier/key.", nameof(value));

    private static string Binding(string application, string configuration)
        => "applications/" + Uuid(application) + "/configuration/" + Identifier(configuration);

    private static void ValidateTarget(ManagementConfigurationSnapshotTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!ManagementWire.IsUuid(target.AuthorityId) || !ManagementWire.IsUuid(target.ApplicationId) || !ManagementWire.IsUuid(target.RegistrationId)
            || !ManagementWire.IsScalarText(target.Environment, 255) || !ManagementWire.IsScalarText(target.HostApplicationName, 255)
            || target.HostApplicationName == "CommonConfiguration" || !ManagementWire.IsOperationKey(target.ConfigurationId)
            || !ManagementWire.IsOrderedInteger(target.SchemaVersion) || target.CohortId is not null && !ManagementWire.IsOperationKey(target.CohortId))
        {
            throw new ArgumentException("Supply the independently established exact target and enrollment.", nameof(target));
        }
    }

    private static ManagementConfigurationProblem? FinalProblem<T>(RestResult<T> exchange, ConfigurationMapper mapper)
        => !exchange.Success && exchange.Context.FailureKind == RestFailureKind.Http && !exchange.Context.ErrorBodyTruncated
            && exchange.Context.HttpStatusCode == mapper.Problem?.Status && ReferenceEquals(exchange.Context.ErrorBody, mapper.ProblemBody)
                ? mapper.Problem : null;

    private sealed record CommandHeaders(string Key, string? IfMatch, bool RequiresPrecondition = true);

    private sealed class ExpectedResponse(string path, string? schema, object? body, CommandHeaders? headers, string? editorSource = null, int? maximumPageSize = null)
    {
        private readonly string[] segments = path.Split('/');

        internal void Check(object? value)
        {
            switch (value)
            {
                case ManagementConfigurationCreationContext creation:
                    Require(creation.ApplicationId == segments[1]);
                    break;
                case ManagementPage<ManagementConfigurationSummary> summaries:
                    Require(summaries.Items.All(item => item.ApplicationId == segments[1])
                        && summaries.Items.Count <= (maximumPageSize ?? 100));
                    break;
                case ManagementConfigurationCandidatePreview candidate when body is ManagementConfigurationCandidatePreviewCommand candidateCommand:
                    Require(candidate.ApplicationId == candidateCommand.ApplicationId && candidate.ConfigurationId == candidateCommand.ConfigurationId
                        && candidate.SchemaVersion == candidateCommand.SchemaVersion && candidate.SelectedSource == candidateCommand.SelectedSource
                        && candidate.SourceId == candidateCommand.SourceId && candidate.SourceETag == candidateCommand.SourceETag
                        && candidate.AssignmentETag == candidateCommand.AssignmentETag && candidate.CommonSource == candidateCommand.CommonSource
                        && candidate.ApplicationSource == candidateCommand.ApplicationSource
                        && candidate.DraftSha256 == ManagementConfigurationWire.ComputePayloadSha256(candidateCommand.PayloadJson));
                    break;
                case ManagementConfigurationEditorContext editor:
                    CheckApplication(editor.ApplicationId, editor.ConfigurationId, editor.SchemaVersion);
                    Require(editor.SelectedSource == editorSource);
                    break;
                case ManagementConfigurationOperationResult operation:
                    CheckOperation(operation.Operation);
                    CheckDecision(operation.Result);
                    break;
                case ManagementConfigurationSource source:
                    Require(segments.Length >= 3 && source.SourceId == segments[2]);
                    Require(source.Publications.Items.All(item => item.SourceId == source.SourceId));
                    break;
                case ManagementConfigurationPublication publication:
                    Require(segments.Length >= 3 && publication.PublicationId == segments[2]);
                    break;
                case ManagementConfigurationRelease release:
                    Require(segments.Length >= 3 && release.ReleaseId == segments[2]);
                    break;
                case ManagementConfigurationAssignmentMap assignments:
                    CheckApplication(assignments.ApplicationId, assignments.ConfigurationId, assignments.SchemaVersion);
                    break;
                case ManagementConfigurationPreview preview when body is ManagementConfigurationPreviewCommand command:
                    Require(preview.ApplicationId == command.ApplicationId && preview.ConfigurationId == command.ConfigurationId
                        && preview.SchemaVersion == command.SchemaVersion);
                    break;
                case ManagementPage<ManagementConfigurationRelease> releases:
                    foreach (ManagementConfigurationRelease item in releases.Items)
                    {
                        CheckApplication(item.ApplicationId, item.ConfigurationId, item.SchemaVersion);
                    }
                    break;
                case ManagementPage<ManagementConfigurationAdoptionEntry> page:
                    Require(page.Items.All(item => item.ApplicationId == segments[1]));
                    if (segments.Length >= 5 && segments[2] == "configuration")
                    {
                        foreach (ManagementConfigurationAdoptionEntry item in page.Items)
                        {
                            CheckApplication(item.ApplicationId, item.ConfigurationId, item.SchemaVersion);
                        }
                    }
                    break;
                case ManagementReportReceipt receipt when body is ManagementConfigurationStatusReport report:
                    Require(receipt.RegistrationId == segments[1] && receipt.Stream == "configuration-status"
                        && receipt.SubmittedSequence == report.Sequence && receipt.AcceptedSequence == report.Sequence);
                    break;
            }
        }

        internal void CheckProblem(ManagementConfigurationProblem problem)
        {
            if (problem.Operation is { } operation)
            {
                CheckOperation(operation);
            }
            if (problem.Result is { } result)
            {
                CheckOperation(result.Operation);
                CheckDecision(result.Result);
            }
        }

        private (string Name, string Kind) Family()
            => segments[0] == "applications" && segments.Length >= 3 && segments[2] is "configuration-creations" or "configuration-creation-operations"
                ? ("management.configuration.create", "configurationCreated") : segments[0] == "applications"
                ? segments[4] is "releases" or "release-operations"
                    ? ("management.configuration.release.create", "releaseCreated")
                    : ("management.configuration.assignment.replace", "assignmentsReplaced")
                : segments.Length > 1 && segments[1] == "catalog"
                    ? ("management.configuration.catalog.apply", "catalogApplied")
                    : ("management.configuration.source.publish", "sourcePublished");

        private void CheckOperation(ManagementOperationReference operation)
        {
            if (headers is null)
            {
                return;
            }
            Require(operation.Key == headers.Key && operation.Name == Family().Name);
            if (Family().Kind == "configurationCreated")
            {
                string prefix = "management.configuration.v1/management.configuration.create/" + segments[1] + "/management.actor.v1.";
                Require(operation.Scope.StartsWith(prefix, StringComparison.Ordinal) && operation.Scope.Length == prefix.Length + 64
                    && operation.Scope[prefix.Length..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'));
            }
        }

        private void CheckDecision(ManagementConfigurationDecision? decision)
        {
            if (headers is null || decision is null || !decision.KnownKind.IsKnown)
            {
                return;
            }
            // Future kinds remain explicitly unknown, never coerced to a known
            // successful family merely because the HTTP exchange succeeded.
            Require(decision.Kind == Family().Kind);
            if (Family().Kind == "sourcePublished")
            {
                Require(decision.SourceId == segments[2]);
            }
            if (Family().Kind == "configurationCreated")
            {
                Require(decision.ApplicationId == segments[1] && decision.SchemaVersion == "1");
                if (body is ManagementConfigurationCreationCommand creation)
                {
                    Require(decision.ConfigurationId == creation.ConfigurationId);
                }
            }
            else if (segments[0] == "applications")
            {
                CheckApplication(decision.ApplicationId, decision.ConfigurationId, decision.SchemaVersion);
            }
            if (body is ManagementConfigurationCatalogCommand catalog)
            {
                Require(decision.ManifestDigest == catalog.ManifestDigest);
            }
        }

        private void CheckApplication(string? application, string? configuration, string? version)
            => Require(application == segments[1] && configuration == Uri.UnescapeDataString(segments[3]) && version == schema);

        private static void Require(bool valid)
        {
            if (!valid)
            {
                throw new RestDecodingException();
            }
        }
    }

    private sealed class ConfigurationMapper(ManagementConfigurationWireLimits limits,
        ManagementConfigurationSnapshotTarget? expected = null, ManagementConfigurationSnapshot? retained = null,
        ExpectedResponse? responseBinding = null) : IRestResponseMapper
    {
        private bool retainedValidated;
        internal bool IsSnapshot => expected is not null;
        internal ManagementConfigurationSnapshotRead? Read
        {
            get; private set;
        }
        internal ManagementConfigurationSnapshot? Snapshot => Read?.Snapshot;
        internal bool ContentRejected
        {
            get; private set;
        }
        internal ManagementConfigurationProblem? Problem
        {
            get; private set;
        }
        internal RestErrorBody? ProblemBody
        {
            get; private set;
        }

        internal Task PrepareConditionalAsync(RestRequestContext request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (retained is not null)
            {
                ValidateRetained(token);
                request.Headers.Set("If-None-Match", retained.ETag);
            }
            return Task.CompletedTask;
        }

        public ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Read = null;
            ContentRejected = false;
            Problem = null;
            ProblemBody = null;
            if (response.HttpStatusCode is >= 200 and <= 299
                && (!response.Headers.TryGetValue("Content-Type", out IReadOnlyList<string>? contentTypes) || contentTypes.Count != 1
                    || !MediaTypeHeaderValue.TryParse(contentTypes[0], out MediaTypeHeaderValue? contentType)
                    || !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)))
            {
                // Refuse an undeclared representation before identifying desired
                // content, not after exposing a decoded snapshot or operation.
                throw new RestDecodingException();
            }
            if (expected is not null)
            {
                // A permitted short-circuit exchange still passes through this
                // owned mapper, even when no primary preparation was needed.
                ValidateRetained(token);
                if (response.HttpStatusCode == 304)
                {
                    if (retained is null || response.ErrorBody is not { Bytes.Count: 0 }
                        || !response.Headers.TryGetValue("ETag", out IReadOnlyList<string>? etags)
                        || etags.Count != 1 || etags[0] != retained.ETag)
                    {
                        throw new RestDecodingException();
                    }
                    return ValueTask.FromResult(RestResponseDecision.Continue);
                }
                if (response.HttpStatusCode is >= 200 and <= 299)
                {
                    if (response.Decoded.Value is not ManagementConfigurationSnapshotBody body)
                    {
                        throw new RestDecodingException();
                    }
                    Read = ManagementConfigurationWire.DecodeSnapshot(body.Bytes, expected, limits);
                    token.ThrowIfCancellationRequested();
                    if (Read.Snapshot is null)
                    {
                        ContentRejected = Read.Desired is not null;
                        throw new RestDecodingException();
                    }
                    return ValueTask.FromResult(RestResponseDecision.Continue);
                }
            }
            RestErrorBody? error = response.ErrorBody;
            if (response.HttpStatusCode is >= 200 and <= 299)
            {
                responseBinding?.Check(response.Decoded.Value);
            }
            if (response.HttpStatusCode is >= 200 and <= 299 || error is null
                || error.Bytes.Count > ManagementWire.MaximumErrorBodyBytes
                || !MediaTypeHeaderValue.TryParse(error.MediaType, out MediaTypeHeaderValue? media)
                || !string.Equals(media.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }
            ManagementConfigurationProblem? problem = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationProblem>(error.Bytes.ToArray(), limits);
            token.ThrowIfCancellationRequested();
            if (problem?.Status != response.HttpStatusCode)
            {
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }
            responseBinding?.CheckProblem(problem);
            Problem = problem;
            ProblemBody = error;
            return ValueTask.FromResult(error.Bytes.Count == ManagementWire.MaximumErrorBodyBytes
                ? RestResponseDecision.Continue : RestResponseDecision.RemoteError(problem.Code, problem.Detail ?? problem.Title));
        }

        private void ValidateRetained(CancellationToken token)
        {
            if (retained is null || retainedValidated)
            {
                return;
            }
            ManagementConfigurationSnapshotRead checkedRetained = ManagementConfigurationWire.DecodeSnapshot(
                ManagementConfigurationWire.EncodeResponse(retained, limits), expected!, limits);
            token.ThrowIfCancellationRequested();
            if (checkedRetained.Snapshot is null)
            {
                throw new ArgumentException("A conditional read requires an exact complete validated retained target/session snapshot.", nameof(retained));
            }
            retainedValidated = true;
        }
    }
}
