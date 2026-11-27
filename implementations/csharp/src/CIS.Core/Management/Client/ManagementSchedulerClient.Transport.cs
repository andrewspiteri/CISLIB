using System.Globalization;
using System.Net.Http.Headers;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;

namespace CIS.Management;

public sealed partial class ManagementSchedulerClient
{
    private async Task<ManagementSchedulerCallResult<T>> CallAsync<T>(string method, string path, object? body = null,
        ManagementPageRequest? page = null, IReadOnlyDictionary<string, string>? query = null, Headers? headers = null,
        Action<object?>? check = null, ManagementCallOptions? options = null, CancellationToken token = default)
    {
        var mapper = new SchedulerMapper(settings.WireLimits, check, headers);
        RestResult<T> exchange = await SendAsync<T>(method, path, body, page, query, headers, mapper, "scheduler", options, token).ConfigureAwait(false);
        ManagementSchedulerProblem? problem = FinalProblem(exchange, mapper) ? mapper.SchedulerProblem : null;
        return new(exchange, problem, headers?.Key);
    }

    private Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> OperationAsync(string method, string path,
        string application, string objectId, string key, string family, object? body = null, string? ifMatch = null,
        bool create = false, ManagementCallOptions? options = null, CancellationToken token = default)
    {
        if (method != "GET" && !create && family != ManagementSchedulerOperations.RunWrite)
        {
            RequireArgument(ManagementWire.IsStrongETag(ifMatch), nameof(ifMatch));
        }
        string[]? kinds = method == "GET" ? null : family switch
        {
            ManagementSchedulerOperations.TriggerWrite => [create ? "triggerCreated" : method == "PUT" ? "triggerUpdated" : "triggerStateChanged"],
            ManagementSchedulerOperations.TaskWrite when body is ManagementSchedulerManualRunCommand => ["manualRunAdmitted"],
            ManagementSchedulerOperations.TaskWrite when body is ManagementSchedulerStateCommand { Intent: "activate" } => ["taskActivated", "taskStateChanged"],
            ManagementSchedulerOperations.TaskWrite => [create ? "taskCreated" : method == "PUT" ? "taskUpdated" : "taskStateChanged"],
            _ => ["cancellationRequested"]
        };
        var headers = new Headers(key, family, application, Uuid(objectId), ifMatch, create, kinds);
        return CallAsync<ManagementSchedulerOperationResult>(method, path, body, headers: headers,
            check: value => CheckOperation((ManagementSchedulerOperationResult)value!, headers), options: options, token: token);
    }

    private Task<ManagementSchedulerCallResult<ManagementSchedulerEventReceipt>> EventReceiptAsync(string method, string application,
        string trigger, string eventId, ManagementSchedulerEventCommand? body, ManagementCallOptions? options, CancellationToken token)
        => CallAsync<ManagementSchedulerEventReceipt>(method, method == "POST" ? Trigger(application, trigger) + "/events"
                : Event(application, trigger, eventId) + "/receipt", body,
            check: value => Require(value is ManagementSchedulerEventReceipt receipt && receipt.ApplicationId == application
                && receipt.TriggerId == trigger && receipt.EventId == eventId), options: options, token: token);

    private async Task<RestResult<T>> SendAsync<T>(string method, string path, object? body, ManagementPageRequest? page,
        IReadOnlyDictionary<string, string>? query, Headers? headers, SchedulerMapper mapper, string alias,
        ManagementCallOptions? options, CancellationToken token)
    {
        long entered = clock.GetTimestamp();
        token.ThrowIfCancellationRequested();
        if (page is not null && !ManagementWire.IsPageRequest(page))
        {
            throw new ArgumentException("Supply a bounded page/cursor.", nameof(page));
        }
        if (headers is not null && (!ManagementWire.IsOperationKey(headers.Key)
            || headers.IfMatch is not null && !ManagementWire.IsStrongETag(headers.IfMatch)))
        {
            throw new ArgumentException("Supply the original safe key and required strong precondition.", nameof(headers));
        }
        if (method is "POST" or "PUT" && body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }
        TimeSpan budget = options?.Timeout ?? settings.Timeout;
        RestContractRules.Timeout(budget, nameof(options));
        var request = new RestRequestOptions
        {
            OperationName = alias,
            ResponseFormat = RestResponseFormat.Json,
            RequireValue = true,
            DisableConfigureRequest = true,
            ResponseMapper = mapper,
            RetryPolicy = method == "GET" ? SelectReadPolicy(options?.ReadRetryPolicy ?? settings.ReadRetryPolicy) : RestRetrySelection.None,
            Body = Body(body)
        };
        request.Headers.Set("Accept", "application/json, application/problem+json");
        request.Headers.Set("Cache-Control", "no-store");
        if (headers is not null && method != "GET")
        {
            request.Headers.Set("Idempotency-Key", headers.Key);
            if (headers.Create)
            {
                request.Headers.Set("If-None-Match", "*");
            }
            else if (headers.IfMatch is not null)
            {
                request.Headers.Set("If-Match", headers.IfMatch);
            }
        }
        if (page is not null)
        {
            request.Query.Set("pageSize", page.PageSize.ToString(CultureInfo.InvariantCulture));
            if (page.Cursor is not null)
            {
                request.Query.Set("cursor", page.Cursor);
            }
        }
        if (query is not null)
        {
            foreach ((string name, string value) in query)
            {
                request.Query.Set(name, value);
            }
        }
        TimeSpan remaining = budget - clock.GetElapsedTime(entered);
        token.ThrowIfCancellationRequested();
        if (remaining <= TimeSpan.Zero)
        {
            return RestResult<T>.Failed("client.timeout", "The Scheduler entry budget elapsed before dispatch.",
                new(failureKind: RestFailureKind.Timeout, clientName: settings.RestClientName, operationName: alias));
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
            throw new InvalidOperationException("Select an actually registered native read policy instance.");
        }
        return RestRetrySelection.Named(policy.Name);
    }

    private static RestBody? Body(object? value) => value switch
    {
        ManagementSchedulerTriggerCommand command => RestBody.Json(command),
        ManagementSchedulerTaskCommand command => RestBody.Json(command),
        ManagementSchedulerStateCommand command => RestBody.Json(command),
        ManagementSchedulerTaskPreviewCommand command => RestBody.Json(command),
        ManagementSchedulerCalendarPreviewCommand command => RestBody.Json(command),
        ManagementSchedulerEventCommand command => RestBody.Json(command),
        ManagementSchedulerManualRunCommand command => RestBody.Json(command),
        ManagementSchedulerAttemptCommand command => RestBody.Json(command),
        ManagementSchedulerAttemptReport report => RestBody.Json(report),
        null => null,
        _ => throw new ArgumentException("Select one declared Scheduler command.", nameof(value))
    };

    private static string App(string application) => "applications/" + Uuid(application) + "/scheduler";
    private static string Trigger(string application, string trigger) => App(application) + "/triggers/" + Uuid(trigger);
    private static string TaskPath(string application, string task) => App(application) + "/tasks/" + Uuid(task);
    private static string Run(string application, string run) => App(application) + "/runs/" + Uuid(run);
    private static string Event(string application, string trigger, string occurrence) => Trigger(application, trigger) + "/events/" + Uuid(occurrence);
    private static string Registration(string registration) => "registrations/" + Uuid(registration) + "/scheduler";
    private static string Uuid(string value) => ManagementWire.IsUuid(value) ? value : throw new ArgumentException("Supply a canonical nonempty UUID.", nameof(value));
    private static string Key(string value) => ManagementWire.IsOperationKey(value) ? value : throw new ArgumentException("Supply an exact safe identifier/key.", nameof(value));

    private static void State(ManagementSchedulerStateCommand command, ManagementSchedulerStateTarget target)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireArgument(ManagementSchedulerWire.IsStateIntent(command.Intent, target), nameof(command));
    }

    private static void CheckOperation(ManagementSchedulerOperationResult value, Headers expected)
    {
        CheckReference(value.Operation, expected);
        if (value.Result is { } result)
        {
            Require(result.ApplicationId == expected.Application && result.ObjectId == expected.Object);
            if (result.KnownKind.IsKnown)
            {
                bool familyMatches = expected.Family switch
                {
                    ManagementSchedulerOperations.TriggerWrite => result.Kind is "triggerCreated" or "triggerUpdated" or "triggerStateChanged",
                    ManagementSchedulerOperations.TaskWrite => result.Kind is "taskCreated" or "taskUpdated" or "taskActivated" or "taskStateChanged" or "manualRunAdmitted",
                    ManagementSchedulerOperations.RunWrite => result.Kind == "cancellationRequested",
                    _ => false
                };
                Require(familyMatches);
                Require(expected.Kinds is null || expected.Kinds.Contains(result.Kind, StringComparer.Ordinal));
            }
        }
    }

    private static void CheckReference(ManagementOperationReference value, Headers expected)
        => Require(value.Key == expected.Key && value.Name == expected.Family);

    private static void CheckPreview(ManagementSchedulerTaskPreview value, ManagementSchedulerTaskPreviewCommand command)
    {
        if (!value.Valid)
        {
            return;
        }
        Require(ManagementSchedulerWire.IsTaskPreview(value, command.TaskId is not null));
        byte[] requested = ManagementSchedulerWire.EncodeRequest(command.Definition);
        byte[] echoed = ManagementSchedulerWire.EncodeRequest(value.Definition!);
        Require(requested.AsSpan().SequenceEqual(echoed));
    }

    private static void CheckAdmission(ManagementSchedulerAttemptAdmission value, string runId, ManagementSchedulerAttemptCommand command)
    {
        if (value.Attempt is { } attempt)
        {
            Require(attempt.RunId == runId && attempt.AttemptId == command.AttemptId
                && attempt.RegistrationId == command.RegistrationId && attempt.Role == command.Role
                && attempt.Build == command.Build && attempt.AcquisitionId == command.AcquisitionId
                && (command.EffectivePolicy is null || attempt.EffectivePolicy == command.EffectivePolicy));
        }
        Require(value.Disposition != "admitted" || value.Attempt is not null);
    }

    private static IReadOnlyDictionary<string, string> History(ManagementSchedulerHistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        // The same closed fixed DTO validator checks canonical dates, byte bounds and paired event scope.
        ManagementSchedulerWire.EncodeRequest(query);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(values, "taskId", query.TaskId);
        Add(values, "executionScope", query.ExecutionScope);
        Add(values, "jobId", query.JobId);
        Add(values, "eventId", query.EventId);
        Add(values, "resourceId", query.ResourceId);
        Add(values, "fromUtc", query.FromUtc);
        Add(values, "untilUtc", query.UntilUtc);
        Add(values, "state", query.State);
        Add(values, "applicationVersion", query.ApplicationVersion);
        Add(values, "buildId", query.BuildId);
        Add(values, "triggerApplicationId", query.TriggerApplicationId);
        Add(values, "triggerId", query.TriggerId);
        if (query.ExecutionContract is { } contract)
        {
            values.Add("executionContractName", contract.Name);
            values.Add("executionContractVersion", contract.Version.ToString(CultureInfo.InvariantCulture));
        }
        return values;
    }

    private static bool MatchesHistory(ManagementSchedulerRun run, ManagementSchedulerHistoryQuery query)
        => (query.TaskId is null || run.TaskId == query.TaskId)
            && (query.ExecutionScope is null || run.Job.ExecutionScope == query.ExecutionScope)
            && (query.JobId is null || run.Job.DefinitionId == query.JobId)
            && (query.EventId is null || run.EventId == query.EventId)
            && (query.TriggerApplicationId is null || run.Trigger.ApplicationId == query.TriggerApplicationId)
            && (query.TriggerId is null || run.Trigger.TriggerId == query.TriggerId)
            && (query.ResourceId is null || run.ResourceId == query.ResourceId)
            && (query.State is null || run.State == query.State);

    private static void Add(Dictionary<string, string> values, string name, string? value)
    {
        if (value is not null)
        {
            values.Add(name, value);
        }
    }

    private static void RequireArgument(bool valid, string parameter)
    {
        if (!valid)
        {
            throw new ArgumentException("The exact request does not match its selected resource or intent.", parameter);
        }
    }

    private static void Require(bool valid)
    {
        if (!valid)
        {
            throw new RestDecodingException();
        }
    }

    private static bool FinalProblem<T>(RestResult<T> exchange, SchedulerMapper mapper)
        => !exchange.Success && exchange.Context.FailureKind == RestFailureKind.Http && !exchange.Context.ErrorBodyTruncated
            && ReferenceEquals(exchange.Context.ErrorBody, mapper.ProblemBody)
            && exchange.Context.HttpStatusCode == mapper.ProblemStatus;

    private sealed record Headers(string Key, string Family, string Application, string Object, string? IfMatch = null,
        bool Create = false, string[]? Kinds = null);

    private enum ProblemKind
    {
        Scheduler,
        Catalog,
        Configuration
    }

    private sealed class SchedulerMapper(ManagementSchedulerWireLimits limits, Action<object?>? check = null,
        Headers? expected = null, ProblemKind kind = ProblemKind.Scheduler) : IRestResponseMapper
    {
        internal ManagementSchedulerProblem? SchedulerProblem
        {
            get; private set;
        }
        internal ManagementProblem? CatalogProblem
        {
            get; private set;
        }
        internal ManagementConfigurationProblem? ConfigurationProblem
        {
            get; private set;
        }
        internal RestErrorBody? ProblemBody
        {
            get; private set;
        }
        internal int? ProblemStatus => SchedulerProblem?.Status ?? CatalogProblem?.Status ?? ConfigurationProblem?.Status;

        public ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken cancellationToken)
        {
            SchedulerProblem = null;
            CatalogProblem = null;
            ConfigurationProblem = null;
            ProblemBody = null;
            cancellationToken.ThrowIfCancellationRequested();
            if (response.HttpStatusCode is >= 200 and <= 299)
            {
                Require(response.Headers.TryGetValue("Content-Type", out IReadOnlyList<string>? mediaValues)
                    && mediaValues.Count == 1 && MediaTypeHeaderValue.TryParse(mediaValues[0], out MediaTypeHeaderValue? media)
                    && string.Equals(media.MediaType, "application/json", StringComparison.OrdinalIgnoreCase));
                Require(response.Decoded.Value is not null);
                check?.Invoke(response.Decoded.Value);
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }
            RestErrorBody? error = response.ErrorBody;
            if (error is null || error.Bytes.Count > ManagementWire.MaximumErrorBodyBytes
                || !MediaTypeHeaderValue.TryParse(error.MediaType, out MediaTypeHeaderValue? errorMedia)
                || !string.Equals(errorMedia.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }
            ReadOnlyMemory<byte> bytes = error.Bytes.ToArray();
            string? code;
            string? message;
            if (kind == ProblemKind.Catalog)
            {
                CatalogProblem = ManagementWire.DecodeResponse<ManagementProblem>(bytes);
                code = CatalogProblem?.Code;
                message = CatalogProblem?.Detail ?? CatalogProblem?.Title;
            }
            else if (kind == ProblemKind.Configuration)
            {
                ConfigurationProblem = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationProblem>(bytes);
                code = ConfigurationProblem?.Code;
                message = ConfigurationProblem?.Detail ?? ConfigurationProblem?.Title;
            }
            else
            {
                SchedulerProblem = ManagementSchedulerWire.DecodeResponse<ManagementSchedulerProblem>(bytes, limits);
                if (expected is not null && SchedulerProblem is { } problem)
                {
                    if (problem.Operation is { } operation)
                    {
                        CheckReference(operation, expected);
                    }
                    if (problem.Result is { } result)
                    {
                        CheckOperation(result, expected);
                    }
                }
                code = SchedulerProblem?.Code;
                message = SchedulerProblem?.Detail ?? SchedulerProblem?.Title;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (ProblemStatus != response.HttpStatusCode)
            {
                SchedulerProblem = null;
                CatalogProblem = null;
                ConfigurationProblem = null;
                return ValueTask.FromResult(RestResponseDecision.Continue);
            }
            ProblemBody = error;
            return ValueTask.FromResult(error.Bytes.Count == ManagementWire.MaximumErrorBodyBytes
                ? RestResponseDecision.Continue : RestResponseDecision.RemoteError(code!, message!));
        }
    }
}

