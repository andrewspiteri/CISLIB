using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Management.Wire;
using CIS.Messaging;
using CIS.Operations;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.RuleEngine;

namespace CIS.Management;

/// <summary>Typed rule version operations over one caller-selected native REST owner.</summary>
public sealed partial class ManagementRuleEngineClient : IManagementRuleEngineClient, IManagementRuleEngineDeploymentClient
{
    private const string OperationAlias = "rule-engine";
    private readonly IRestClient rest;
    private readonly ManagementClientSnapshot settings;
    private readonly RestRetryPolicy[] readPolicies;
    private readonly TimeProvider clock;

    /// <summary>Captures immutable options and the owner’s actual optional read-policy registrations.</summary>
    public ManagementRuleEngineClient(IRestClient rest, ManagementClientOptions options,
        IEnumerable<RestRetryPolicy>? registeredReadPolicies = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(rest);
        this.rest = rest;
        settings = ManagementClientSnapshot.Capture(options);
        this.clock = clock ?? TimeProvider.System;
        readPolicies = (registeredReadPolicies ?? []).Take(33).ToArray();
        if (readPolicies.Length > 32 || readPolicies.Any(policy => policy is null)
            || readPolicies.Select(policy => policy.Name).Distinct(StringComparer.Ordinal).Count() != readPolicies.Length)
        {
            throw new ArgumentException("Supply the actual finite native retry registrations.", nameof(registeredReadPolicies));
        }
        SelectReadPolicy(settings.ReadRetryPolicy);
    }

    /// <summary>Adds the rule wire codec and operation alias to an existing REST registration before it is bound.</summary>
    public static void ConfigureNativeRestClient(RestClientOptions native)
    {
        ArgumentNullException.ThrowIfNull(native);
        if (native.Codec is not RuleContentCodec)
        {
            native.Codec = new RuleContentCodec(native.Codec ?? RestDefaultContentCodec.Instance);
        }
        if (!native.Operations.Contains(OperationAlias))
        {
            native.Operations.Add(OperationAlias);
        }
    }

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementPage<ManagementRuleSetSummary>>> ListRulesetsAsync(
        string applicationId, ManagementPageRequest? page = null, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementRuleSetSummary>>(new(applicationId), "", page: page ?? new(),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleSchema>> GetSchemaAsync(string applicationId, string rulesetId,
        string schemaRevision, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementRuleSchema>(new(applicationId, rulesetId, schemaRevision), "/schema",
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementPage<ManagementRuleVersionSummary>>> ListVersionsAsync(
        string applicationId, string rulesetId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementRuleVersionSummary>>(new(applicationId, rulesetId), "/versions",
            page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleVersion>> GetVersionAsync(string applicationId, string rulesetId,
        string version, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementRuleVersion>(new(applicationId, rulesetId, Version: version), "/version",
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleVersionSaveResult>> SaveVersionAsync(string applicationId,
        string rulesetId, string operationKey, string ifMatch, ManagementSaveRuleVersionCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CallAsync<ManagementRuleVersionSaveResult>(
            new(applicationId, rulesetId, OperationKey: operationKey, OperationName: ManagementRuleEngineWire.SaveOperationName),
            "/versions", write: new(RestBody.Json(new SaveBody(rulesetId, command)), ifMatch, SchemaRevision: command.SchemaRevision),
            options: options, token: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleVersionSaveResult>> GetSaveOperationAsync(string applicationId,
        string rulesetId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementRuleVersionSaveResult>(
            new(applicationId, rulesetId, OperationKey: operationKey, OperationName: ManagementRuleEngineWire.SaveOperationName),
            "/version-operations", options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleDeployment>> GetDeploymentAsync(string applicationId,
        string rulesetId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementRuleDeployment>(new(applicationId, rulesetId), "/deployment",
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleDeploymentOperationResult>> RequestDeploymentAsync(
        string applicationId, string rulesetId, string operationKey, string ifMatch, ManagementRequestRuleDeploymentCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CallAsync<ManagementRuleDeploymentOperationResult>(
            new(applicationId, rulesetId, OperationKey: operationKey, OperationName: ManagementRuleEngineWire.DeploymentOperationName),
            "/deployments", write: new(RestBody.Json(command), ifMatch, Deployment: command),
            options: options, token: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagementRuleEngineCallResult<ManagementRuleDeploymentOperationResult>> GetDeploymentOperationAsync(
        string applicationId, string rulesetId, string operationKey, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ManagementRuleDeploymentOperationResult>(
            new(applicationId, rulesetId, OperationKey: operationKey, OperationName: ManagementRuleEngineWire.DeploymentOperationName),
            "/deployment-operations", options: options, token: cancellationToken);

    private async Task<ManagementRuleEngineCallResult<T>> CallAsync<T>(CallScope scope, string suffix,
        ManagementPageRequest? page = null, Mutation? write = null, ManagementCallOptions? options = null,
        CancellationToken token = default) where T : class
    {
        long entered = clock.GetTimestamp();
        token.ThrowIfCancellationRequested();
        bool lookup = scope.OperationName is not null && write is null;
        if (!ManagementWire.IsUuid(scope.ApplicationId)
            || suffix.Length != 0 && !ManagementRuleEngineWire.IsRuleId(scope.RulesetId)
            || suffix == "/schema" && !ManagementRuleEngineWire.IsRuleId(scope.SchemaRevision)
            || suffix == "/version" && !ManagementRuleEngineWire.IsRuleId(scope.Version)
            || scope.ExecutionId is not null && !ManagementWire.IsUuid(scope.ExecutionId))
        {
            throw new ArgumentException("Supply the canonical application ID and exact bounded rule identifiers.", nameof(scope));
        }
        if (page is not null && !ManagementWire.IsPageRequest(page))
        {
            throw new ArgumentException("Supply a bounded page and opaque cursor.", nameof(page));
        }
        if (scope.OperationName is not null && !ManagementWire.IsOperationKey(scope.OperationKey))
        {
            throw new ArgumentException("Supply the original bounded operation key.", nameof(scope));
        }
        if (write is not null && write.CounterReset is null && !ManagementWire.IsStrongETag(write.IfMatch))
        {
            throw new ArgumentException("Supply one strong precondition ETag.", nameof(write));
        }

        TimeSpan budget = options?.Timeout ?? settings.Timeout;
        RestContractRules.Timeout(budget, nameof(options));
        var mapper = new RuleMapper(scope, page?.PageSize, write);
        var request = new RestRequestOptions
        {
            OperationName = OperationAlias,
            ResponseFormat = RestResponseFormat.Json,
            RequireValue = true,
            DisableConfigureRequest = true,
            ResponseMapper = mapper,
            RetryPolicy = write is not null ? RestRetrySelection.None : SelectReadPolicy(options?.ReadRetryPolicy ?? settings.ReadRetryPolicy),
            Body = write?.Body
        };
        request.Headers.Set("Accept", "application/json, application/problem+json");
        request.Headers.Set("Cache-Control", "no-store");
        if (write is not null)
        {
            request.Headers.Set("Idempotency-Key", scope.OperationKey!);
            if (write.IfMatch is { } tag)
            {
                request.Headers.Set("If-Match", tag);
            }
        }
        AddQuery(request.Query, "rulesetId", scope.RulesetId);
        AddQuery(request.Query, "schemaRevision", scope.SchemaRevision);
        AddQuery(request.Query, "version", scope.Version);
        if (scope.ExecutionQuery is { } executionQuery)
        {
            AddExecutionQuery(request.Query, executionQuery);
        }
        if (page is not null)
        {
            request.Query.Set("pageSize", page.PageSize.ToString(CultureInfo.InvariantCulture));
            AddQuery(request.Query, "cursor", page.Cursor);
        }
        string path = "management/v1/applications/" + scope.ApplicationId + "/rule-engine" + (suffix.Length == 0 ? "/rulesets" : suffix);
        if (lookup)
        {
            path += "/" + scope.OperationKey + scope.LookupSuffix;
        }
        if (scope.ExecutionId is not null)
        {
            path += "/" + scope.ExecutionId;
        }

        TimeSpan remaining = budget - clock.GetElapsedTime(entered);
        token.ThrowIfCancellationRequested();
        if (remaining <= TimeSpan.Zero)
        {
            return new(RestResult<T>.Failed("client.timeout", "The rule-engine entry budget elapsed before dispatch.",
                new(failureKind: RestFailureKind.Timeout, clientName: settings.RestClientName, operationName: OperationAlias)),
                operationKey: scope.OperationKey);
        }
        request.Timeout = remaining;
        RestResult<T> exchange = await rest.Send<T>(write is not null ? "POST" : "GET",
            new Uri(settings.BaseUri, path).AbsoluteUri, request, token).ConfigureAwait(false);
        ManagementRuleEngineProblem? problem = !exchange.Success && !exchange.Context.ErrorBodyTruncated
            && exchange.Context.HttpStatusCode == mapper.Problem?.Status
            && ReferenceEquals(exchange.Context.ErrorBody, mapper.ProblemBody) ? mapper.Problem : null;
        return new(exchange, problem, scope.OperationKey);
    }

    private static void AddQuery(RestQuery query, string name, string? value)
    {
        if (value is not null)
        {
            query.Set(name, value);
        }
    }

    private RestRetrySelection SelectReadPolicy(RestRetryPolicy? policy)
    {
        ManagementRetryRules.Validate(policy);
        if (policy is null)
        {
            return RestRetrySelection.None;
        }
        if (!readPolicies.Any(registered => ReferenceEquals(registered, policy)))
        {
            throw new InvalidOperationException("Select an actually registered native retry policy instance.");
        }
        return RestRetrySelection.Named(policy.Name);
    }

    private sealed record CallScope(string ApplicationId, string? RulesetId = null, string? SchemaRevision = null,
        string? Version = null, string? OperationKey = null, string? OperationName = null,
        ManagementRuleExecutionQuery? ExecutionQuery = null, string? ExecutionId = null, string? LookupSuffix = null);

    private sealed record Mutation(RestBody Body, string? IfMatch, string? SchemaRevision = null,
        ManagementRequestRuleDeploymentCommand? Deployment = null, ManagementRuleCounterResetCommand? CounterReset = null);

    private sealed record SaveBody(string RulesetId, ManagementSaveRuleVersionCommand Command);

    private sealed class RuleContentCodec(IRestContentCodec fallback) : IRestContentCodec
    {
        public bool CanDecode(Type resultType, RestResponseFormat format)
            => ManagementRuleEngineWire.IsResponseType(resultType)
                ? format == RestResponseFormat.Json : fallback.CanDecode(resultType, format);

        public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            byte[]? bytes = body.Kind == RestBodyKind.Json ? body.Value switch
            {
                SaveBody save => ManagementRuleEngineWire.EncodeRequest(save.Command, save.RulesetId),
                ManagementRequestRuleDeploymentCommand deployment => ManagementRuleEngineWire.EncodeRequest(deployment),
                ManagementRuleCounterResetCommand reset => ManagementRuleEngineWire.EncodeRequest(reset),
                _ => null
            } : null;
            if (bytes is null)
            {
                await fallback.EncodeAsync(body, destination, token).ConfigureAwait(false);
                return;
            }
            token.ThrowIfCancellationRequested();
            await destination.WriteAsync(bytes, token).ConfigureAwait(false);
        }

        public async ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token)
        {
            if (!ManagementRuleEngineWire.IsResponseType(typeof(T)))
            {
                return await fallback.DecodeAsync<T>(source, format, token).ConfigureAwait(false);
            }
            using var bytes = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                if (count > ManagementRuleEngineWire.MaximumBodyBytes - bytes.Length)
                {
                    throw new RestResponseLimitException();
                }
                bytes.Write(buffer, 0, count);
            }
            token.ThrowIfCancellationRequested();
            object? value = ManagementRuleEngineWire.DecodeResponse(typeof(T), bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length)));
            token.ThrowIfCancellationRequested();
            return value is T selected ? RestDecodedValue<T>.Present(selected) : throw new RestDecodingException();
        }
    }

    private sealed class RuleMapper(CallScope expected, int? maximumPageSize, Mutation? write) : IRestResponseMapper
    {
        internal ManagementRuleEngineProblem? Problem
        {
            get; private set;
        }
        internal RestErrorBody? ProblemBody
        {
            get; private set;
        }

        public ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Problem = null;
            ProblemBody = null;
            if (response.HttpStatusCode is >= 200 and <= 299)
            {
                if (!response.Headers.TryGetValue("Content-Type", out IReadOnlyList<string>? contentTypes) || contentTypes.Count != 1
                    || !HasMediaType(contentTypes[0], "application/json") || !Matches(response.Decoded.Value, response.Headers))
                {
                    throw new RestDecodingException();
                }
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }

            RestErrorBody? body = response.ErrorBody;
            if (body is null || body.Bytes.Count > ManagementWire.MaximumErrorBodyBytes
                || !HasMediaType(body.MediaType, "application/problem+json"))
            {
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }
            ManagementRuleEngineProblem? problem = ManagementRuleEngineWire.DecodeResponse<ManagementRuleEngineProblem>(body.Bytes.ToArray());
            token.ThrowIfCancellationRequested();
            if (problem?.Status != response.HttpStatusCode
                || problem.Operation is { } operation && !MatchesOperation(operation)
                || problem.Result is { } result && !MatchesResult(result)
                || problem.DeploymentResult is { } deployment && !MatchesDeploymentResult(deployment)
                || problem.CounterResetResult is { } reset && !MatchesCounterResetResult(reset))
            {
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }
            Problem = problem;
            ProblemBody = body;
            return ValueTask.FromResult(body.Bytes.Count == ManagementWire.MaximumErrorBodyBytes
                ? RestResponseDecision.Continue : RestResponseDecision.RemoteError(problem.Code, problem.Detail ?? problem.Title));
        }

        private bool Matches(object? response, IReadOnlyDictionary<string, IReadOnlyList<string>> headers) => response switch
        {
            ManagementPage<ManagementRuleSetSummary> page => page.Items.Count <= maximumPageSize
                && page.Items.All(item => MatchesScope(item.ApplicationId, item.RulesetId)),
            ManagementPage<ManagementRuleVersionSummary> page => page.Items.Count <= maximumPageSize
                && page.Items.All(item => MatchesScope(item.ApplicationId, item.RulesetId)),
            ManagementRuleSchema schema => MatchesScope(schema.ApplicationId, schema.RulesetId)
                && schema.SchemaRevision == expected.SchemaRevision,
            ManagementRuleVersion version => MatchesScope(version.ApplicationId, version.RulesetId)
                && version.Version == expected.Version,
            ManagementRuleVersionSaveResult result => MatchesResult(result)
                && MatchesSettled(result.ResultSource, result.OriginalCompletion, result.Result?.EditETag, headers),
            ManagementRuleDeployment deployment => MatchesScope(deployment.ApplicationId, deployment.RulesetId)
                && MatchesETag(deployment.AssignmentETag, headers),
            ManagementRuleDeploymentOperationResult result => MatchesDeploymentResult(result)
                && MatchesSettled(result.ResultSource, result.OriginalCompletion, result.Result?.AssignmentETag, headers),
            ManagementPage<ManagementRuleExecutionSummary> executions => executions.Items.Count <= maximumPageSize
                && executions.Items.All(item => MatchesScope(item.ApplicationId, item.RulesetId)
                    && ManagementRuleEngineWire.MatchesExecutionQuery(item, expected.ExecutionQuery)),
            ManagementRuleExecutionDetail detail => detail.Execution.ExecutionId == expected.ExecutionId
                && MatchesScope(detail.Execution.ApplicationId, detail.Execution.RulesetId),
            ManagementRuleCounterPage counters => counters.Items.Count <= maximumPageSize
                && MatchesScope(counters.ApplicationId, counters.RulesetId),
            ManagementRuleCounterResetOperationResult reset => MatchesCounterResetResult(reset)
                && reset.OriginalCompletion == "committed" && (write is null
                    ? reset.ResultSource == "found"
                    : reset.Result is not null && (reset.ResultSource is "prepared" or "replayed")),
            ManagementRuleCounterResetOutcomesPage outcomes => outcomes.Items.Count <= maximumPageSize
                && MatchesScope(outcomes.ApplicationId, outcomes.RulesetId) && MatchesOperation(outcomes.Operation),
            _ => false
        };

        private bool MatchesOperation(ManagementOperationReference operation)
            => expected.OperationKey is not null && operation.Key == expected.OperationKey
                && operation.Name == expected.OperationName;

        private bool MatchesResult(ManagementRuleVersionSaveResult result)
            => MatchesOperation(result.Operation) && MatchesScope(result.ApplicationId, result.RulesetId)
                && (write?.SchemaRevision is null || result.Result is null || result.Result.SchemaRevision == write.SchemaRevision);

        private bool MatchesDeploymentResult(ManagementRuleDeploymentOperationResult result)
            => MatchesOperation(result.Operation) && MatchesScope(result.ApplicationId, result.RulesetId)
                && (write?.Deployment is not { } command || result.Result is null
                    || result.Result.Intent == command.Intent && result.Result.Desired.Version == command.Version);

        private bool MatchesCounterResetResult(ManagementRuleCounterResetOperationResult result)
            => MatchesOperation(result.Operation) && MatchesScope(result.ApplicationId, result.RulesetId)
                && (write?.CounterReset is not { } requested || result.Result is null
                    || ManagementRuleEngineWire.MatchesCounterResetDecision(result.Result, requested));

        private bool MatchesSettled(string source, string originalCompletion, string? tag,
            IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
            => originalCompletion == "committed" && (write is null
                ? source == "found"
                : (source is "prepared" or "replayed") && tag is not null && MatchesETag(tag, headers));

        private static bool MatchesETag(string tag, IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
            => !headers.TryGetValue("ETag", out IReadOnlyList<string>? tags) || tags.Count == 1 && tags[0] == tag;

        private bool MatchesScope(string applicationId, string rulesetId)
            => applicationId == expected.ApplicationId && (expected.RulesetId is null || rulesetId == expected.RulesetId);

        private static bool HasMediaType(string? value, string expected)
            => MediaTypeHeaderValue.TryParse(value, out MediaTypeHeaderValue? media)
                && string.Equals(media.MediaType, expected, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The closed rule-version save request and bounded Management v1 responses.</summary>
public static partial class ManagementRuleEngineWire
{
    /// <summary>The registered original save operation identity.</summary>
    public const string SaveOperationName = "management.rule-engine.version.save";

    /// <summary>The registered original deployment request operation identity.</summary>
    public const string DeploymentOperationName = "management.rule-engine.deployment.request";

    /// <summary>The encoded outer request and successful response bound.</summary>
    public const int MaximumBodyBytes = 1024 * 1024;

    internal static readonly UTF8Encoding Utf8 = new(false, true);

    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectRequiredConstructorParameters = true,
        MaxDepth = RuleBounds.MaximumJsonDepth
    };

    private static readonly JsonSerializerOptions RequestJson = new(ResponseJson)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Tests an exact nonempty Rule Engine ID of at most 128 UTF-8 bytes without control scalars.</summary>
    public static bool IsRuleId(string? value) => RuleBounds.IsId(value);

    /// <summary>Validates and encodes a complete bounded save request with exact raw definition text.</summary>
    public static byte[] EncodeRequest(ManagementSaveRuleVersionCommand command) => EncodeRequest(command, null);

    internal static byte[] EncodeRequest(ManagementSaveRuleVersionCommand command, string? expectedRulesetId)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command, expectedRulesetId);
        return Encode(command, RequestJson, MaximumBodyBytes);
    }

    /// <summary>Reads a strict save request, returning null for invalid data or a mismatched expected ruleset.</summary>
    public static ManagementSaveRuleVersionCommand? DecodeRequest(ReadOnlyMemory<byte> bytes, string? expectedRulesetId = null)
    {
        try
        {
            var command = (ManagementSaveRuleVersionCommand)Read(bytes, typeof(ManagementSaveRuleVersionCommand),
                RequestJson, MaximumBodyBytes);
            ValidateCommand(command, expectedRulesetId);
            return command;
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Validates and encodes a closed deploy or rollback command.</summary>
    public static byte[] EncodeRequest(ManagementRequestRuleDeploymentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateDeploymentCommand(command);
        return Encode(command, RequestJson, MaximumBodyBytes);
    }

    /// <summary>Reads a closed deploy or rollback command, returning null for invalid data.</summary>
    public static ManagementRequestRuleDeploymentCommand? DecodeDeploymentRequest(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            var command = (ManagementRequestRuleDeploymentCommand)Read(bytes, typeof(ManagementRequestRuleDeploymentCommand),
                RequestJson, MaximumBodyBytes);
            ValidateDeploymentCommand(command);
            return command;
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Validates and encodes one declared Management rule response.</summary>
    public static byte[] EncodeResponse<T>(T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        Require(IsWireResponseType(typeof(T)), "Select a declared rule-engine response.");
        ValidateResponse(value);
        return Encode(value, ResponseJson, ResponseLimit(typeof(T)));
    }

    /// <summary>Decodes a declared response, preserving opaque JSON and genuine operation facts.</summary>
    public static T? DecodeResponse<T>(ReadOnlyMemory<byte> bytes) where T : class => DecodeResponse(typeof(T), bytes) as T;

    internal static object? DecodeResponse(Type type, ReadOnlyMemory<byte> bytes)
    {
        Require(IsWireResponseType(type), "Select a declared rule-engine response.");
        try
        {
            object value = Read(bytes, type, ResponseJson, ResponseLimit(type));
            ValidateResponse(value);
            return value;
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            return null;
        }
    }

    internal static bool IsResponseType(Type type) => type == typeof(ManagementPage<ManagementRuleSetSummary>)
        || type == typeof(ManagementRuleSchema) || type == typeof(ManagementPage<ManagementRuleVersionSummary>)
        || type == typeof(ManagementRuleVersion) || type == typeof(ManagementRuleVersionSaveResult)
        || type == typeof(ManagementRuleWorkloadVersion) || type == typeof(ManagementRuleDeployment)
        || type == typeof(ManagementRuleDeploymentOperationResult) || type == typeof(ManagementPage<ManagementRuleDeploymentInstance>)
        || type == typeof(ManagementPage<ManagementRuleExecutionSummary>) || type == typeof(ManagementRuleExecutionDetail)
        || type == typeof(ManagementRuleCounterPage) || type == typeof(ManagementRuleCounterResetOperationResult)
        || type == typeof(ManagementRuleCounterResetOutcomesPage);

    private static bool IsWireResponseType(Type type) => IsResponseType(type) || type == typeof(ManagementRuleEngineProblem);

    private static int ResponseLimit(Type type) => type == typeof(ManagementRuleEngineProblem)
        ? ManagementWire.MaximumErrorBodyBytes : MaximumBodyBytes;

    internal static RuleDefinition Definition(string raw, string? rulesetId, string schemaRevision, string? version = null)
    {
        RuleDefinition definition = RuleDefinitionCodec.Decode(RawBytes(raw));
        Require((rulesetId is null || definition.RulesetId == rulesetId) && definition.SchemaRevision == schemaRevision
            && (version is null || definition.Version == version), "The definition does not echo the expected rule identity.");
        return definition;
    }

    internal static RuleDefinition WorkloadDefinition(string raw, string rulesetId, string version)
    {
        Require(IsRuleId(rulesetId) && ManagementWire.IsUuid(version), "Supply the exact retained workload ruleset and version.");
        RuleDefinition definition = RuleDefinitionCodec.Decode(RawBytes(raw));
        Require(definition.RulesetId == rulesetId && definition.Version == version,
            "The retained definition does not echo the expected ruleset and version.");
        return definition;
    }

    private static byte[] RawBytes(string raw)
    {
        Require(raw is { Length: > 0 and <= RuleBounds.MaximumDefinitionBytes }
            && Utf8.GetByteCount(raw) <= RuleBounds.MaximumDefinitionBytes, "Supply bounded raw rule JSON.");
        return Utf8.GetBytes(raw);
    }

    private static void ValidateCommand(ManagementSaveRuleVersionCommand command, string? expectedRulesetId)
    {
        Require(IsRuleId(command.SchemaRevision)
            && (command.BaseVersion is null || IsRuleId(command.BaseVersion))
            && ManagementWire.IsUtf8Text(command.Reason, 1024), "The save command has an invalid schema, reason or base version.");
        _ = Definition(command.DefinitionJson, expectedRulesetId, command.SchemaRevision);
    }

    private static void ValidateDeploymentCommand(ManagementRequestRuleDeploymentCommand command)
        => Require(ManagementWire.IsUuid(command.Version) && (command.Intent is "deploy" or "rollback")
            && ManagementWire.IsUtf8Text(command.Reason, 1024), "Supply one exact saved version, deployment intent and bounded reason.");

    private static void ValidateResponse(object value)
    {
        switch (value)
        {
            case ManagementPage<ManagementRuleSetSummary> rulesets:
                CheckPage(rulesets, item => item.RulesetId, item => item.Environment, item =>
                {
                    CheckScope(item.ApplicationId, item.Environment, item.RulesetId);
                    Require(IsRuleId(item.SchemaRevision) && ManagementWire.IsStrongETag(item.EditETag)
                        && ManagementWire.IsScalarText(item.DisplayName, 512), "Invalid ruleset summary.");
                });
                break;
            case ManagementRuleSchema schema:
                CheckScope(schema.ApplicationId, schema.Environment, schema.RulesetId);
                Require(IsRuleId(schema.SchemaRevision), "Invalid schema revision.");
                Require(Definition(schema.SchemaJson, schema.RulesetId, schema.SchemaRevision).Rows.Count == 0,
                    "A trusted rule schema must contain no saved rows.");
                break;
            case ManagementPage<ManagementRuleVersionSummary> versions:
                CheckPage(versions, item => item.Version, item => item.Environment, item => CheckVersion(item.ApplicationId,
                    item.Environment, item.RulesetId, item.Version, item.SchemaRevision, item.SavedAtUtc, item.VersionNote));
                break;
            case ManagementRuleVersion version:
                CheckVersion(version.ApplicationId, version.Environment, version.RulesetId, version.Version,
                    version.SchemaRevision, version.SavedAtUtc, version.VersionNote);
                _ = version.DecodeDefinition();
                break;
            case ManagementRuleWorkloadVersion workload:
                ManagementConfigurationClient.ValidateTarget(workload.Target);
                _ = workload.DecodeDefinition();
                break;
            case ManagementRuleVersionSaveResult result:
                CheckResult(result);
                break;
            case ManagementRuleDeployment deployment:
                CheckScope(deployment.ApplicationId, deployment.Environment, deployment.RulesetId);
                Require(deployment.RequestedAtUtc is null || deployment.Desired is not null
                    && ManagementWire.IsTimestamp(deployment.RequestedAtUtc), "Invalid current deployment request time.");
                Require(ManagementWire.IsStrongETag(deployment.AssignmentETag), "Invalid assignment-map ETag.");
                if (deployment.Desired is { } desired)
                {
                    CheckDeploymentSelection(desired);
                }
                break;
            case ManagementRuleDeploymentOperationResult deployment:
                CheckDeploymentResult(deployment);
                break;
            case ManagementPage<ManagementRuleDeploymentInstance> instances:
                CheckDeploymentInstances(instances);
                break;
            case ManagementPage<ManagementRuleExecutionSummary> executions:
                CheckPage(executions, item => item.ExecutionId, item => item.Environment, CheckExecutionSummary);
                break;
            case ManagementRuleExecutionDetail detail:
                CheckExecutionDetail(detail);
                break;
            case ManagementRuleCounterPage counters:
                CheckCounterPage(counters);
                break;
            case ManagementRuleCounterResetOperationResult reset:
                CheckCounterResetResult(reset);
                break;
            case ManagementRuleCounterResetOutcomesPage outcomes:
                CheckCounterResetOutcomes(outcomes);
                break;
            case ManagementRuleEngineProblem problem:
                CheckProblem(problem);
                break;
            default:
                throw new ArgumentException("Select a declared rule-engine response.", nameof(value));
        }
    }

    private static void CheckPage<T>(ManagementPage<T> page, Func<T, string> identity, Func<T, string> environment,
        Action<T> validate) where T : class
    {
        Require(page.Items is not null && ManagementWire.IsPageRequest(new(page.Items.Count == 0 ? 1 : page.Items.Count, page.NextCursor)),
            "The rule-engine page or cursor exceeds its bounds.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? expectedEnvironment = null;
        foreach (T item in page.Items!)
        {
            Require(item is not null, "The rule-engine page contains a null item.");
            validate(item!);
            expectedEnvironment ??= environment(item!);
            Require(seen.Add(identity(item!)) && expectedEnvironment == environment(item!), "The page contains a duplicate or mixed environment.");
        }
    }

    private static void CheckScope(string applicationId, string environment, string rulesetId) =>
        Require(ManagementWire.IsUuid(applicationId) && ManagementWire.IsScalarText(environment, 255)
            && IsRuleId(rulesetId), "Invalid resolved application, environment or ruleset.");

    private static void CheckDeploymentInstances(ManagementPage<ManagementRuleDeploymentInstance> page)
    {
        Require(page.Items is not null && ManagementWire.IsPageRequest(new(Math.Max(1, page.Items.Count), page.NextCursor)),
            "The Rule Engine deployment observation page or cursor exceeds its bounds.");
        var rows = new List<ManagementConfigurationAdoptionEntry>(page.Items!.Count);
        (string ApplicationId, string Environment, string RulesetId, string ConfigurationId, string SchemaVersion)? scope = null;
        foreach (ManagementRuleDeploymentInstance? item in page.Items)
        {
            if (item?.Configuration is not { } observation)
            {
                throw new ArgumentException("The deployment observation must contain its original Configuration state.");
            }
            var target = (observation.ApplicationId, observation.Environment, item.RulesetId, observation.ConfigurationId, observation.SchemaVersion);
            scope ??= target;
            Require(target == scope.GetValueOrDefault() && IsRuleId(item.RulesetId)
                && OptionalUuid(item.RequestedVersion) && OptionalUuid(item.ReportedDesiredVersion)
                && OptionalUuid(item.AppliedVersion) && OptionalUuid(item.LastSuccessfulVersion)
                && (item.RequestedVersion is null || observation.Desired is not null)
                && (item.ReportedDesiredVersion is null || observation.Reported?.Desired is not null)
                && (item.AppliedVersion is null || observation.Reported?.Applied is not null)
                && (item.LastSuccessfulVersion is null || observation.Reported?.LastSuccessful is not null),
                "Invalid or mixed Rule Engine deployment observation.");
            rows.Add(observation);
        }
        _ = ManagementConfigurationWire.EncodeResponse(new ManagementPage<ManagementConfigurationAdoptionEntry>(rows, page.NextCursor));
    }

    private static bool OptionalUuid(string? value) => value is null || ManagementWire.IsUuid(value);

    private static void CheckVersion(string applicationId, string environment, string rulesetId, string version,
        string schemaRevision, string savedAtUtc, string? versionNote = null)
    {
        CheckScope(applicationId, environment, rulesetId);
        Require(IsRuleId(version) && IsRuleId(schemaRevision) && ManagementWire.IsTimestamp(savedAtUtc),
            "Invalid saved version identity or timestamp.");
        Require(versionNote is null || ManagementWire.IsUtf8Text(versionNote, 1024), "Invalid saved version note.");
    }

    private static void CheckOperation(ManagementOperationReference operation, string? name = null)
    {
        Require(operation is not null && (operation.Name is SaveOperationName or DeploymentOperationName or CounterResetOperationName)
            && (name is null || operation.Name == name)
            && ManagementWire.IsUtf8Text(operation.Scope, 256) && ManagementWire.IsOperationKey(operation.Key),
            "Invalid resolved Rule Engine operation.");
    }

    private static void CheckResult(ManagementRuleVersionSaveResult result)
    {
        CheckScope(result.ApplicationId, result.Environment, result.RulesetId);
        CheckOperation(result.Operation, SaveOperationName);
        CheckOperationFacts(result.ResultSource, result.OriginalCompletion, result.Result is not null,
            result.Rejection, result.AttemptCompletion);
        if (result.Result is { } decision)
        {
            CheckVersion(result.ApplicationId, result.Environment, result.RulesetId, decision.Version,
                decision.SchemaRevision, decision.SavedAtUtc);
            Require(ManagementWire.IsStrongETag(decision.EditETag), "Invalid resulting edit ETag.");
        }
    }

    private static void CheckDeploymentResult(ManagementRuleDeploymentOperationResult result)
    {
        CheckScope(result.ApplicationId, result.Environment, result.RulesetId);
        CheckOperation(result.Operation, DeploymentOperationName);
        CheckOperationFacts(result.ResultSource, result.OriginalCompletion, result.Result is not null,
            result.Rejection, result.AttemptCompletion);
        if (result.Result is { } decision)
        {
            Require((decision.Intent is "deploy" or "rollback") && ManagementWire.IsTimestamp(decision.RequestedAtUtc)
                && ManagementWire.IsStrongETag(decision.AssignmentETag), "Invalid original deployment decision.");
            CheckDeploymentSelection(decision.Desired);
        }
    }

    private static void CheckDeploymentSelection(ManagementRuleDeploymentSelection? desired)
        => Require(desired is not null && ManagementWire.IsUuid(desired.Version) && IsRuleId(desired.SchemaRevision)
            && ManagementWire.IsUuid(desired.ReleaseId) && ManagementWire.IsOrderedInteger(desired.Generation),
            "Invalid desired saved version, release or generation.");

    private static void CheckOperationFacts(string resultSource, string originalCompletion, bool hasDecision,
        ManagementOperationRejection? rejection, ManagementTransactionFacts? attempt)
    {
        bool hasEstablishedResult = hasDecision || rejection is not null;
        bool hasKnownSource = resultSource is "prepared" or "replayed" or "found";
        Require(ManagementWire.IsScalarText(resultSource, 64) && ManagementWire.IsScalarText(originalCompletion, 64)
            && (!hasDecision || rejection is null) && (!hasKnownSource || hasEstablishedResult)
            && (originalCompletion != "committed" || hasEstablishedResult)
            && (originalCompletion is not ("unknown" or "notCommitted") || !hasEstablishedResult),
            "The operation facts contradict their original completion or result source.");
        if (rejection is not null)
        {
            _ = OperationText.Code(rejection.Code, nameof(rejection.Code));
            _ = OperationText.Utf8(rejection.Message, 1024, nameof(rejection.Message), allowControls: true);
        }
        if (attempt is not null)
        {
            Require(ManagementWire.IsScalarText(attempt.Outcome, 64)
                && (attempt.CleanupCodes is null || attempt.CleanupCodes.Count <= 16
                    && attempt.CleanupCodes.All(code => IsCode(code))), "Invalid observed transaction completion.");
        }
    }

    private static void CheckProblem(ManagementRuleEngineProblem problem)
    {
        Require(ManagementWire.IsScalarText(problem.Type, 2048) && Uri.TryCreate(problem.Type, UriKind.Absolute, out _)
            && ManagementWire.IsScalarText(problem.Title, 256) && problem.Status is >= 400 and <= 599
            && IsCode(problem.Code)
            && (problem.Detail is null || ManagementWire.IsScalarText(problem.Detail, 1024, allowEmpty: true))
            && (problem.CorrelationId is null || ManagementWire.IsScalarText(problem.CorrelationId, 128)),
            "Invalid rule-engine Problem Details.");
        Require((problem.Result is null ? 0 : 1) + (problem.DeploymentResult is null ? 0 : 1) + (problem.CounterResetResult is null ? 0 : 1) <= 1,
            "The Rule Engine Problem Details contains more than one original operation result.");
        if (problem.Operation is { } operation)
        {
            CheckOperation(operation);
        }
        if (problem.Result is { } result)
        {
            CheckResult(result);
            Require(problem.Operation == result.Operation,
                "The rule-engine Problem Details and save result disagree on operation identity.");
        }
        if (problem.DeploymentResult is { } deployment)
        {
            CheckDeploymentResult(deployment);
            Require(problem.Operation == deployment.Operation,
                "The rule-engine Problem Details and deployment result disagree on operation identity.");
        }
        if (problem.CounterResetResult is { } reset)
        {
            CheckCounterResetResult(reset);
            Require(problem.Operation == reset.Operation,
                "The rule-engine Problem Details and counter reset result disagree on operation identity.");
        }
    }

    private static bool IsCode(string? code) => code is { Length: > 0 and <= 128 }
        && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static object Read(ReadOnlyMemory<byte> bytes, Type type, JsonSerializerOptions options, int maximum)
    {
        Require(bytes.Length <= maximum && MessageWire.ValidateJson(bytes.Span, RuleBounds.MaximumJsonDepth),
            "Supply finite strict JSON with unique field names.");
        return JsonSerializer.Deserialize(bytes.Span, type, options)
            ?? throw new ArgumentException("The rule wire body must be an object.");
    }

    private static byte[] Encode<T>(T value, JsonSerializerOptions options, int maximum)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        Require(bytes.Length <= maximum, "The encoded rule wire body exceeds its bound.");
        return bytes;
    }

    private static void Require(bool valid, string explanation)
    {
        if (!valid)
        {
            throw new ArgumentException(explanation);
        }
    }
}
