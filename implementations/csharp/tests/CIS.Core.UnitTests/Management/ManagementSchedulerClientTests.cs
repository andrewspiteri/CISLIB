using System.Text;
using System.Text.Json;
using CIS.CommonTesting;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.RestClient.Http;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Management.ManagementConfigurationClientTestSupport;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementSchedulerClientTests
{
    private const string TriggerId = "22222222-2222-2222-2222-222222222222";
    private const string TaskId = "33333333-3333-3333-3333-333333333333";
    private const string EventId = "44444444-4444-4444-4444-444444444444";
    private const string RunId = "55555555-5555-5555-5555-555555555555";

    [TestMethod]
    public async Task OriginalCreateUpdateAndLookupUseExactNativeRoutesHeadersAndRetainSeparateCompletionFacts()
    {
        int calls = 0;
        ManagementSchedulerOperationResult original = ResponseValue<ManagementSchedulerOperationResult>("scheduler-original-commit-survives-current-cleanup");
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            int index = calls++;
            Assert.AreEqual("/tenant/control/management/v1/applications/" + AppId + "/scheduler/tasks"
                + (index == 0 ? "" : "/" + TaskId + (index == 2 ? "/operations/original_1" : "")), context.Request.Path.Value);
            Assert.AreEqual(index == 0 ? "POST" : index == 1 ? "PUT" : "GET", context.Request.Method);
            if (index < 2)
            {
                Assert.AreEqual(index == 0 ? "original_1" : "update_1", context.Request.Headers["Idempotency-Key"].Single());
                Assert.AreEqual(index == 0 ? "*" : "", context.Request.Headers.IfNoneMatch.ToString());
                Assert.AreEqual(index == 1 ? "\"task-1\"" : "", context.Request.Headers.IfMatch.ToString());
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body, context.RequestAborted);
                Assert.IsNotNull(ManagementSchedulerWire.DecodeRequest<ManagementSchedulerTaskCommand>(body.ToArray()));
            }
            context.Response.ContentType = "application/json";
            var response = index == 1 ? original with
            {
                Operation = original.Operation with
                {
                    Key = "update_1"
                },
                Result = original.Result! with
                {
                    Kind = "taskUpdated"
                }
            } : original;
            await context.Response.Body.WriteAsync(ManagementSchedulerWire.EncodeResponse(response), context.RequestAborted);
        });
        ManagementSchedulerClientOptions options = OptionsFor();
        RestClientOptions native = NativeFor(options);
        native.AllowedOrigins.Add(new Uri(server.Address).GetLeftPart(UriPartial.Authority));
        native.Handlers.Add(new LocalTransport(server.Address));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementSchedulerClient(rest, options);
        ManagementSchedulerTaskCommand command = RequestValue<ManagementSchedulerTaskCommand>("scheduler-task-command-passive-exact-settings");
        Assert.IsTrue((await client.CreateTaskAsync(AppId, "original_1", command)).Exchange.Success);
        Assert.IsTrue((await client.UpdateTaskAsync(AppId, TaskId, "update_1", "\"task-1\"", command)).Exchange.Success);
        var found = await client.GetTaskOperationAsync(AppId, TaskId, "original_1");
        Assert.IsTrue(found.Exchange.Success, found.Exchange.ErrorMessage);
        Assert.AreEqual("committed", found.Exchange.Value!.OriginalCompletion);
        Assert.IsFalse(found.Exchange.Value.AttemptCompletion!.CleanupSucceeded);
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    [DataRow("key")]
    [DataRow("family")]
    [DataRow("object")]
    [DataRow("application")]
    public async Task ForeignOperationFactsAreRejectedInsideTheNativeOwnerWithoutLosingCallerKey(string mismatch)
    {
        ManagementSchedulerOperationResult original = ResponseValue<ManagementSchedulerOperationResult>("scheduler-original-commit-survives-current-cleanup");
        original = mismatch switch
        {
            "key" => original with { Operation = original.Operation with { Key = "another-key" } },
            "family" => original with { Operation = original.Operation with { Name = ManagementSchedulerOperations.TriggerWrite } },
            "application" => original with { Result = original.Result! with { ApplicationId = EventId } },
            _ => original with { Result = original.Result! with { ObjectId = EventId } }
        };
        ManagementSchedulerClientOptions options = OptionsFor();
        await using var rest = new HttpRestClient(NativeFor(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(ManagementSchedulerWire.EncodeResponse(original)))))));
        var result = await new ManagementSchedulerClient(rest, options).GetTaskOperationAsync(AppId, TaskId, "original_1");
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.AreEqual("original_1", result.OperationKey);
        Assert.IsNull(result.Exchange.Value);
    }

    [TestMethod]
    public async Task UncertainWriteKeepsTheOriginalProblemAndKeyWithoutAutomaticReplay()
    {
        int calls = 0;
        ManagementSchedulerClientOptions options = OptionsFor();
        options.ReadRetryPolicy = RestRetryPolicy.TransientHttp;
        var problem = new ManagementSchedulerProblem("about:blank", "Original outcome unknown", 503,
            "scheduler.operation_unknown", Operation: new(ManagementSchedulerOperations.TaskWrite, "actual-resolved-scope", "original_1"));
        await using var rest = new HttpRestClient(NativeFor(options, new Handler((_, _) =>
        {
            calls++;
            return ValueTask.FromResult<IRestResponse>(new Response(503,
                new MemoryStream(ManagementSchedulerWire.EncodeResponse(problem)), media: "application/problem+json"));
        })));
        var client = new ManagementSchedulerClient(rest, options, [RestRetryPolicy.TransientHttp]);
        var result = await client.CreateTaskAsync(AppId, "original_1",
            RequestValue<ManagementSchedulerTaskCommand>("scheduler-task-command-passive-exact-settings"));
        Assert.AreEqual(1, calls);
        Assert.AreEqual("original_1", result.OperationKey);
        Assert.AreEqual(problem.Code, result.Problem!.Code);
        Assert.IsNull(result.Exchange.Value);
    }

    [TestMethod]
    public async Task EventRunLinksRetainTheSourceOccurrenceEvenForAnotherTargetApplication()
    {
        ManagementSchedulerRun run = ResponseValue<ManagementSchedulerRun>("scheduler-run-attempt-counter-is-lossless");
        run = run with
        {
            ApplicationId = "99999999-9999-9999-9999-999999999999"
        };
        ManagementSchedulerClientOptions options = OptionsFor();
        int calls = 0;
        await using var rest = new HttpRestClient(NativeFor(options, new Handler((request, _) =>
        {
            calls++;
            Assert.IsTrue(new Uri(request.Url).AbsolutePath.EndsWith("/triggers/" + TriggerId + "/events/" + EventId + "/runs", StringComparison.Ordinal));
            var page = new ManagementPage<ManagementSchedulerRun>([run]);
            return ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(ManagementSchedulerWire.EncodeResponse(page))));
        })));
        var client = new ManagementSchedulerClient(rest, options);
        Assert.IsTrue((await client.GetEventRunsAsync(AppId, TriggerId, EventId)).Exchange.Success);
        run = run with
        {
            Trigger = run.Trigger with
            {
                TriggerId = TaskId
            }
        };
        var foreign = await client.GetEventRunsAsync(AppId, TriggerId, EventId);
        Assert.AreEqual(RestFailureKind.InvalidResponse, foreign.Exchange.Context.FailureKind);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    [DataRow("task")]
    [DataRow("job")]
    [DataRow("event")]
    [DataRow("resource")]
    [DataRow("state")]
    public async Task HistoryPagesCannotSubstituteAnotherSelectorWithinTheSameApplication(string selector)
    {
        ManagementSchedulerRun run = ResponseValue<ManagementSchedulerRun>("scheduler-run-attempt-counter-is-lossless");
        ManagementSchedulerHistoryQuery query = selector switch
        {
            "task" => new(TaskId: run.TaskId),
            "job" => new(ExecutionScope: run.Job.ExecutionScope, JobId: run.Job.DefinitionId),
            "event" => new(EventId: run.EventId, TriggerApplicationId: run.Trigger.ApplicationId, TriggerId: run.Trigger.TriggerId),
            "resource" => new(TaskId: run.TaskId, ResourceId: run.ResourceId),
            _ => new(TaskId: run.TaskId, State: run.State)
        };
        ManagementSchedulerClientOptions options = OptionsFor();
        await using var rest = new HttpRestClient(NativeFor(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(200,
                new MemoryStream(ManagementSchedulerWire.EncodeResponse(new ManagementPage<ManagementSchedulerRun>([run]))))))));
        var client = new ManagementSchedulerClient(rest, options);
        Assert.IsTrue((await client.GetRunsAsync(AppId, query)).Exchange.Success);
        run = selector switch
        {
            "task" => run with { TaskId = EventId },
            "job" => run with { Job = new(run.Job.OwnerApplication, run.Job.Kind, "another-scope", run.Job.DefinitionId, run.Job.DefinitionVersion) },
            "event" => run with { Trigger = run.Trigger with { TriggerId = TaskId } },
            "resource" => run with { ResourceId = "another-resource" },
            _ => run with { State = "another-state" }
        };
        var foreign = await client.GetRunsAsync(AppId, query);
        Assert.AreEqual(RestFailureKind.InvalidResponse, foreign.Exchange.Context.FailureKind);
        Assert.IsNull(foreign.Exchange.Value);
    }

    [TestMethod]
    public async Task DeadlineDuringResponseDisposalJoinsActualCleanupAndOmitsEarlierProblem()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var problem = new ManagementSchedulerProblem("about:blank", "Unknown", 503, "scheduler.operation_unknown",
            Operation: new(ManagementSchedulerOperations.TaskWrite, "native-scope", "original_1"));
        var stream = new HeldCleanup(ManagementSchedulerWire.EncodeResponse(problem), cleaning, release);
        ManagementSchedulerClientOptions options = OptionsFor();
        options.Timeout = TimeSpan.FromSeconds(1);
        await using var rest = new HttpRestClient(NativeFor(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(503, stream, media: "application/problem+json")))), clock: clock);
        var client = new ManagementSchedulerClient(rest, options, clock: clock);
        Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> call = client.CreateTaskAsync(AppId, "original_1",
            RequestValue<ManagementSchedulerTaskCommand>("scheduler-task-command-passive-exact-settings"));
        try
        {
            await cleaning.Task.WaitAsync(Deadline);
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.IsFalse(call.IsCompleted, "Deadline cancellation does not replace actual response disposal.");
        }
        finally
        {
            release.TrySetResult();
        }
        var result = await call.WaitAsync(Deadline);
        Assert.AreEqual(RestFailureKind.Timeout, result.Exchange.Context.FailureKind);
        Assert.AreEqual("original_1", result.OperationKey);
        Assert.IsNull(result.Problem);
    }

    [TestMethod]
    public async Task TaskPreviewRequiresBoundWholeDraftWhileCalendarUsesItsOwnExactContext()
    {
        ManagementSchedulerTaskPreviewCommand command = RequestValue<ManagementSchedulerTaskPreviewCommand>("scheduler-preview-side-effect-free-exact-whole-draft");
        var calendar = new ManagementSchedulerTaskPreview(true, [], [], true, [], "unavailable");
        ManagementSchedulerClientOptions options = OptionsFor();
        ManagementSchedulerTaskPreview response = calendar;
        await using var rest = new HttpRestClient(NativeFor(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(ManagementSchedulerWire.EncodeResponse(response)))))));
        var client = new ManagementSchedulerClient(rest, options);
        var invalid = await client.PreviewTaskAsync(AppId, command);
        Assert.AreEqual(RestFailureKind.InvalidResponse, invalid.Exchange.Context.FailureKind);
        ManagementSchedulerCalendar schedule = RequestValue<ManagementSchedulerCalendar>("scheduler-calendar-daily-exact-time-only");
        Assert.IsTrue((await client.PreviewCalendarAsync(AppId, new(1, schedule, Time))).Exchange.Success);
        response = new(true, [], [], true, [], "unavailable", DraftDigest: new string('a', 64),
            TriggerETag: "\"trigger-1\"", TaskETag: "\"task-1\"",
            Preconditions: [command.Definition.JobConfiguration], Definition: command.Definition);
        Assert.IsTrue((await client.PreviewTaskAsync(AppId, command)).Exchange.Success);
        response = response with
        {
            Definition = command.Definition with
            {
                Name = "Another task"
            }
        };
        Assert.AreEqual(RestFailureKind.InvalidResponse, (await client.PreviewTaskAsync(AppId, command)).Exchange.Context.FailureKind);
    }

    [TestMethod]
    public async Task ExactCapabilityKeysTravelAsQueryValuesAndCompanionsRetainExistingConfigurationRoutes()
    {
        ManagementCapabilityDefinition original = ResponseValue<ManagementCapabilityDefinition>("scheduler-capability-logical-job-definition");
        var key = new ManagementCapabilityKey(original.Key.OwnerApplication, original.Key.Kind,
            "customer/work?region=Europe", original.Key.DefinitionId, original.Key.DefinitionVersion);
        var definition = new ManagementCapabilityDefinition(key, original.DisplayName, original.SupportedOperations,
            original.Description, original.CategoryId, original.ConfigurationContract, original.InputContract, original.InputShape,
            original.EventContract, original.EventShape, original.ResultSummaryContract, original.ResultSummaryShape,
            original.Form, original.ExecutionSupport);
        int calls = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            int call = calls++;
            context.Response.ContentType = "application/json";
            byte[] bytes;
            if (call == 0)
            {
                Assert.IsTrue(context.Request.Path.Value!.EndsWith("/capability-definition", StringComparison.Ordinal));
                Assert.AreEqual(key.ExecutionScope, context.Request.Query["executionScope"].Single());
                Assert.AreEqual(key.OwnerApplication, context.Request.Query["ownerApplication"].Single());
                bytes = ManagementSchedulerWire.EncodeResponse(definition);
            }
            else
            {
                Assert.IsTrue(context.Request.Path.Value!.EndsWith("/configuration/customer.settings/" + (call == 1 ? "actions" : "cohorts"), StringComparison.Ordinal));
                Assert.AreEqual("9007199254740993", context.Request.Query["schemaVersion"].Single());
                bytes = call == 1 ? ManagementSchedulerWire.EncodeResponse(new ManagementConfigurationActions(Source,
                    [new("configuration.assignment.replace", true, false, "security.denied")]))
                    : ManagementSchedulerWire.EncodeResponse(new ManagementPage<ManagementConfigurationCohortChoice>([new("canary", "Canary instances")]));
            }
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        });
        ManagementSchedulerClientOptions options = OptionsFor();
        RestClientOptions native = NativeFor(options);
        native.AllowedOrigins.Add(new Uri(server.Address).GetLeftPart(UriPartial.Authority));
        native.Handlers.Add(new LocalTransport(server.Address));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementSchedulerClient(rest, options);
        Assert.IsTrue((await client.GetCapabilityDefinitionAsync(AppId, key)).Exchange.Success);
        var actions = await client.GetBindingActionsAsync(AppId, "customer.settings", "9007199254740993");
        Assert.IsTrue(actions.Exchange.Success);
        Assert.IsFalse(actions.Exchange.Value!.Actions.Single().Allowed);
        Assert.IsTrue((await client.GetCohortsAsync(AppId, "customer.settings", "9007199254740993")).Exchange.Success);
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    public async Task PassiveRegistrationSharesOneNativeOwnerAndAddsNoRuntimeStartup()
    {
        var services = new ServiceCollection();
        ManagementSchedulerClientOptions options = OptionsFor();
        RestClientOptions native = NativeFor(options);
        native.Limits.MaxResponseBytes = 1024;
        services.AddCisRestClient(native);
        int hosted = services.Count(item => item.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
        services.AddCisManagementSchedulerClient(options);
        Assert.AreEqual(hosted, services.Count(item => item.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)));
        Assert.IsFalse(services.Any(item => item.ServiceType == typeof(CIS.Startup.IStartable)));
        await using ServiceProvider root = services.BuildServiceProvider();
        object client = root.GetRequiredService<IManagementSchedulerClient>();
        Assert.AreSame(client, root.GetRequiredService<IManagementSchedulerWorkerClient>());
        Assert.AreSame(client, root.GetRequiredService<IManagementTriggerEventPort>());
        Assert.AreSame(client, root.GetRequiredService<IManagementApplicationCatalogClient>());
        Assert.AreSame(client, root.GetRequiredService<IManagementConfigurationAdministrationClient>());
        Assert.AreEqual(1024L, native.Limits.MaxResponseBytes);
        Assert.AreEqual(4, RestClientSnapshot.Capture(native).Operations.Count);
    }

    [TestMethod]
    public async Task WrongRouteIntentAndMismatchedCommandTargetFailBeforeDispatch()
    {
        ManagementSchedulerClientOptions options = OptionsFor();
        int dispatches = 0;
        await using var rest = new HttpRestClient(NativeFor(options, new Handler((_, _) =>
        {
            dispatches++;
            throw new InvalidOperationException("Unexpected transport.");
        })));
        var client = new ManagementSchedulerClient(rest, options);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.ChangeTaskStateAsync(AppId, TaskId, "original_1", "\"task-1\"", new(1, "cancel", "Cancel")));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.CancelRunAsync(AppId, RunId, "original_1", new(1, "enable", "Enable")));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.UpdateTaskAsync(AppId, RunId, "original_1", "\"task-1\"",
            RequestValue<ManagementSchedulerTaskCommand>("scheduler-task-command-passive-exact-settings")));
        Assert.AreEqual(0, dispatches);
    }

    private static ManagementSchedulerClientOptions OptionsFor() => new("scheduler") { BaseUrl = "https://service.invalid/tenant/control" };
    private static RestClientOptions NativeFor(ManagementSchedulerClientOptions options, IRestExchangeHandler? handler = null)
    {
        var native = new RestClientOptions(options.RestClientName) { LogCalls = false };
        native.ConfigureCisManagementSchedulerRestClient(options);
        if (handler is not null)
        {
            native.Handlers.Add(handler);
        }
        return native;
    }

    private static T RequestValue<T>(string id) where T : class => ManagementSchedulerWire.DecodeRequest<T>(CaseBytes(id))!;
    private static T ResponseValue<T>(string id) where T : class => ManagementSchedulerWire.DecodeResponse<T>(CaseBytes(id))!;

    private static byte[] CaseBytes(string id)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "management", "v1", "scheduler-cases.json");
            if (File.Exists(path))
            {
                using JsonDocument cases = JsonDocument.Parse(File.ReadAllBytes(path));
                return Encoding.UTF8.GetBytes(cases.RootElement.GetProperty("cases").EnumerateArray()
                    .Single(row => row.GetProperty("id").GetString() == id).GetProperty("json").GetString()!);
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Independent Scheduler cases are required.");
    }

    private sealed class LocalTransport(string address) : IRestExchangeHandler
    {
        public ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken cancellationToken)
        {
            Uri source = new(request.Url);
            request.Url = new Uri(new Uri(address), source.PathAndQuery).AbsoluteUri;
            return next(request, cancellationToken);
        }
    }

    private sealed class HeldCleanup(byte[] bytes, TaskCompletionSource entered, TaskCompletionSource release) : MemoryStream(bytes)
    {
        public override async ValueTask DisposeAsync()
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}

