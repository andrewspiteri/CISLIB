using System.Text.Json.Nodes;
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
public sealed class ManagementConfigurationClientTests
{
    [TestMethod]
    public async Task AllNineteenTypedMethodsUseFrozenRoutesOriginalKeysAndOneObservationAliasThroughActualNativeContent()
    {
        string[] responses = ["definitionpage", "validationresult", "preview", "source", "operationresult", "publication", "operationresult",
            "release", "assignmentmap", "operationresult", "snapshot", "receipt", "adoptionpage", "catalog", "operationresult",
            "operationresult", "operationresult", "operationresult", "operationresult"];
        var seen = new List<(string Method, string Path)>();
        int commands = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            int index = seen.Count;
            seen.Add((context.Request.Method, context.Request.Path.Value!));
            if (context.Request.Method is "POST" or "PUT")
            {
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body, context.RequestAborted);
                Assert.IsGreaterThan(0L, body.Length, "The actual native encoder must write the declared command.");
                commands++;
                if (index is 4 or 6 or 9 or 14)
                {
                    Assert.AreEqual("original-key", context.Request.Headers["Idempotency-Key"].Single());
                    Assert.AreEqual("\"precondition\"", context.Request.Headers.IfMatch.Single());
                }
            }
            if (index is 6 or 8 or 9 or 10 or 17 or 18)
            {
                Assert.AreEqual("1", context.Request.Query["schemaVersion"].Single());
                Assert.HasCount(1, context.Request.Query["schemaVersion"]);
            }
            context.Response.ContentType = "application/json";
            string sequence = Command<ManagementConfigurationStatusReport>("configuration-statusreport-valid").Sequence;
            byte[] bytes = responses[index] == "receipt"
                ? ManagementWire.EncodeResponse(new ManagementReportReceipt(Registration, "configuration-status", sequence, sequence, "accepted", Time))
                : responses[index] == "operationresult" ? ManagementConfigurationWire.EncodeResponse(OperationFor(index))
                : Bytes(Case("configuration-" + responses[index] + "-valid"));
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        });
        ManagementConfigurationClientOptions options = Options();
        RestClientOptions native = Native(options);
        native.AllowedOrigins.Add(new Uri(server.Address).GetLeftPart(UriPartial.Authority));
        native.Handlers.Add(new LocalTransport(server.Address));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementConfigurationClient(rest, options);
        string key = "original-key";
        string etag = "\"precondition\"";
        ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationDefinition>> definitions = await client.GetDefinitionsAsync();
        Assert.IsTrue(definitions.Exchange.Success, definitions.Exchange.ErrorCode + ": " + definitions.Exchange.ErrorMessage);
        Assert.IsTrue((await client.ValidateAsync(Command<ManagementConfigurationValidationCommand>("configuration-validationcommand-valid"))).Exchange.Success);
        Assert.IsTrue((await client.PreviewAsync(Command<ManagementConfigurationPreviewCommand>("configuration-previewcommand-valid"))).Exchange.Success);
        Assert.IsTrue((await client.GetSourceAsync(Source)).Exchange.Success);
        Assert.IsTrue((await client.PublishSourceAsync(Source, key, etag, Command<ManagementConfigurationPublicationCommand>("configuration-publicationcommand-valid"))).Exchange.Success);
        Assert.IsTrue((await client.GetPublicationAsync(Publication)).Exchange.Success);
        Assert.IsTrue((await client.CreateReleaseAsync(AppId, "sample.settings", "1", key, etag, Command<ManagementConfigurationReleaseCommand>("configuration-releasecommand-valid"))).Exchange.Success);
        Assert.IsTrue((await client.GetReleaseAsync(Release)).Exchange.Success);
        Assert.IsTrue((await client.GetAssignmentsAsync(AppId, "sample.settings", "1")).Exchange.Success);
        Assert.IsTrue((await client.ReplaceAssignmentsAsync(AppId, "sample.settings", "1", key, etag, Command<ManagementConfigurationAssignmentCommand>("configuration-assignmentcommand-valid"))).Exchange.Success);
        Assert.IsTrue((await client.GetRegistrationConfigurationAsync(Target())).Exchange.Success);
        Assert.IsTrue((await client.ReportStatusAsync(Registration, Command<ManagementConfigurationStatusReport>("configuration-statusreport-valid"))).Exchange.Success);
        Assert.IsTrue((await client.GetStatusAsync(AppId)).Exchange.Success);
        Assert.IsTrue((await client.GetCatalogAsync()).Exchange.Success);
        Assert.IsTrue((await client.ApplyCatalogAsync(key, etag, Command<ManagementConfigurationCatalogCommand>("configuration-catalogcommand-valid"))).Exchange.Success);
        Assert.AreEqual(key, (await client.GetCatalogOperationAsync(key)).OperationKey);
        Assert.AreEqual(key, (await client.GetPublicationOperationAsync(Source, key)).OperationKey);
        Assert.AreEqual(key, (await client.GetReleaseOperationAsync(AppId, "sample.settings", "1", key)).OperationKey);
        Assert.AreEqual(key, (await client.GetAssignmentOperationAsync(AppId, "sample.settings", "1", key)).OperationKey);
        Assert.HasCount(19, seen.Distinct().ToArray());
        Assert.AreEqual(7, commands);
        Assert.IsTrue(seen.All(call => call.Path.StartsWith("/tenant/control/management/v1/", StringComparison.Ordinal)));
        Assert.AreEqual("/tenant/control/management/v1/applications/" + AppId + "/configuration/sample.settings/release-operations/original-key", seen[17].Path);
        CollectionAssert.AreEquivalent(new[] { "request", "configuration" }, RestClientSnapshot.Capture(native).Operations.ToArray());
    }

    [TestMethod]
    [DataRow("configuration-payload-digest-mismatch", true)]
    [DataRow("configuration-duplicate-decoded-payload-name", true)]
    [DataRow("configuration-payload-bom-refused", true)]
    public async Task ActualDecodeRetainsOnlyTargetValidatedDesiredFactsForIdentifiedContentFailure(string id, bool identified)
    {
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(Bytes(Case(id))))))));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot> result = await client.GetRegistrationConfigurationAsync(Target());
        Assert.IsFalse(result.Exchange.Success);
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.AreEqual(200, result.Exchange.Context.HttpStatusCode);
        Assert.AreEqual(identified, result.ContentRejection is not null);
        Assert.AreEqual(Release, result.ContentRejection!.Desired.ReleaseId);
        Assert.IsNull(result.Exchange.Context.ErrorBody);
        Assert.IsNull(result.Problem);
    }

    [TestMethod]
    public async Task ConcurrentTargetsShareOneStatelessCodecAndForeignOrMalformedHeadersRemainUnidentified()
    {
        ManagementConfigurationClientOptions options = Options();
        JsonObject foreign = JsonNode.Parse(Case("configuration-snapshot-valid"))!.AsObject();
        foreign["desired"]!["registrationId"] = Source;
        int count = 0;
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(Bytes(Interlocked.Increment(ref count) == 1
                ? foreign.ToJsonString() : "{broken")))))));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot>[] results = await Task.WhenAll(
            client.GetRegistrationConfigurationAsync(Target()), client.GetRegistrationConfigurationAsync(Target() with
            {
                RegistrationId = Source
            }));
        Assert.IsTrue(results.All(result => !result.Exchange.Success && result.ContentRejection is null));
        Assert.IsTrue(results.All(result => result.Exchange.Context.FailureKind == RestFailureKind.InvalidResponse));
    }

    [TestMethod]
    public async Task ExactConditionalReadKeepsNative304FailureAndNoValueWhilePreservingTheRetainedObject()
    {
        ManagementConfigurationSnapshot retained = Value<ManagementConfigurationSnapshot>("configuration-snapshot-valid");
        int calls = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context =>
        {
            calls++;
            Assert.AreEqual(retained.ETag, context.Request.Headers.IfNoneMatch.Single());
            context.Response.StatusCode = 304;
            context.Response.Headers.ETag = retained.ETag;
            return Task.CompletedTask;
        });
        ManagementConfigurationClientOptions options = Options();
        RestClientOptions native = Native(options);
        native.AllowedOrigins.Add(new Uri(server.Address).GetLeftPart(UriPartial.Authority));
        native.Handlers.Add(new LocalTransport(server.Address));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot> result = await client.GetRegistrationConfigurationAsync(Target(), retained);
        Assert.IsFalse(result.Exchange.Success);
        Assert.IsFalse(result.Exchange.Context.HasValue);
        Assert.IsNull(result.Exchange.Value);
        Assert.AreEqual(304, result.Exchange.Context.HttpStatusCode, result.Exchange.ErrorCode + ": " + result.Exchange.ErrorMessage);
        Assert.AreEqual(RestFailureKind.Http, result.Exchange.Context.FailureKind);
        Assert.AreEqual("http.304", result.Exchange.ErrorCode);
        Assert.AreEqual(1, calls);
        Assert.IsNotNull(retained.CommonSource.PayloadJson);
    }

    [TestMethod]
    [DataRow("foreign-etag")]
    [DataRow("no-etag")]
    [DataRow("nonempty")]
    [DataRow("unsolicited")]
    [DataRow("unobserved-body")]
    public async Task InvalidConditional304IsAnOwnedInvalidResponse(string scenario)
    {
        ManagementConfigurationSnapshot retained = Value<ManagementConfigurationSnapshot>("configuration-snapshot-valid");
        ManagementConfigurationClientOptions options = Options();
        RestClientOptions native = Native(options, new Handler((_, _) => ValueTask.FromResult<IRestResponse>(new Response(304,
            new MemoryStream(scenario == "nonempty" ? Bytes("body") : []),
            scenario == "no-etag" ? null : scenario == "foreign-etag" ? "\"foreign\"" : retained.ETag))));
        if (scenario == "unobserved-body")
        {
            native.Limits.MaxErrorBodyBytes = 0;
        }
        await using var rest = new HttpRestClient(native);
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot> result = await client.GetRegistrationConfigurationAsync(Target(),
            scenario == "unsolicited" ? null : retained);
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.IsNull(result.ContentRejection);
        Assert.IsNull(result.Exchange.Value);
    }

    [TestMethod]
    public void ConfigurationProfilePreservesTighterLimitsRejectsZeroBodyObservationAndFitsTheExisting32PairBudget()
    {
        var services = new ServiceCollection();
        foreach (string alias in new[] { "management-a", "management-b" })
        {
            var metadata = new ManagementClientOptions(alias) { BaseUrl = "https://service.invalid/" };
            services.AddCisRestClient(new RestClientOptions(alias).ConfigureCisManagementRestClient(metadata));
            services.AddCisManagementClient(metadata);
        }
        foreach (string alias in new[] { "configuration-a", "configuration-b" })
        {
            ManagementConfigurationClientOptions options = Options(alias);
            RestClientOptions native = new(alias)
            {
                Limits = new()
                {
                    MaxResponseBytes = 1024,
                    MaxErrorBodyBytes = 17,
                    MaxErrorMessageScalars = 20
                }
            };
            native.ConfigureCisManagementConfigurationRestClient(options);
            Assert.AreEqual(1024L, native.Limits.MaxResponseBytes);
            Assert.AreEqual(17, native.Limits.MaxErrorBodyBytes);
            Assert.AreEqual(20, native.Limits.MaxErrorMessageScalars);
            services.AddCisRestClient(native);
            services.AddCisManagementConfigurationClient(options);
        }
        RestClientComposition composition = (RestClientComposition)services.Single(item => item.ServiceType == typeof(RestClientComposition)).ImplementationInstance!;
        Assert.HasCount(32, composition.Operations);
        Assert.ThrowsExactly<ArgumentException>(() => new RestClientOptions("zero") { Limits = new() { MaxErrorBodyBytes = 0 } }
            .ConfigureCisManagementConfigurationRestClient(Options("zero")));
        Assert.AreEqual(64 * 1024, new ManagementWireLimits().MaximumRegularRequestBytes);
    }

    [TestMethod]
    public async Task AWriteUnknownKeepsTheOriginalKeyAndProblemWithoutRetryDespiteAnExplicitReadPolicy()
    {
        int requests = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            requests++;
            Assert.AreEqual("original-key", context.Request.Headers["Idempotency-Key"].Single());
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/problem+json";
            var problem = new ManagementConfigurationProblem("about:blank", "Original completion is unknown", 503,
                "management.configuration.operation_unknown", Operation: new("management.configuration.source.publish", "original-source-scope", "original-key"));
            await context.Response.Body.WriteAsync(ManagementConfigurationWire.EncodeResponse(problem), context.RequestAborted);
        });
        ManagementConfigurationClientOptions options = Options();
        options.ReadRetryPolicy = RestRetryPolicy.TransientHttp;
        RestClientOptions native = Native(options);
        native.AllowedOrigins.Add(new Uri(server.Address).GetLeftPart(UriPartial.Authority));
        native.Handlers.Add(new LocalTransport(server.Address));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementConfigurationClient(rest, options, [RestRetryPolicy.TransientHttp]);
        ManagementConfigurationCallResult<ManagementConfigurationOperationResult> result = await client.PublishSourceAsync(Source,
            "original-key", "\"source-1\"", Command<ManagementConfigurationPublicationCommand>("configuration-publicationcommand-valid"));
        Assert.AreEqual(1, requests, result.Exchange.ErrorCode + ": " + result.Exchange.ErrorMessage);
        Assert.AreEqual("original-key", result.OperationKey);
        Assert.AreEqual("management.configuration.operation_unknown", result.Problem!.Code);
        Assert.AreEqual(RestFailureKind.Http, result.Exchange.Context.FailureKind);
        Assert.IsNull(result.Exchange.Value);
    }

    [TestMethod]
    public async Task TimeoutDuringActualCleanupCannotExposeAnEarlierCapturedContentRejection()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new HeldCleanup(Bytes(Case("configuration-payload-digest-mismatch")), cleaning, release);
        ManagementConfigurationClientOptions options = Options();
        options.Timeout = TimeSpan.FromSeconds(1);
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(new Response(200, stream)))), clock: clock);
        var client = new ManagementConfigurationClient(rest, options, clock: clock);
        Task<ManagementConfigurationCallResult<ManagementConfigurationSnapshot>> call = client.GetRegistrationConfigurationAsync(Target());
        try
        {
            await cleaning.Task.WaitAsync(Deadline);
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.IsFalse(call.IsCompleted, "Deadline cancellation cannot replace actual response cleanup.");
        }
        finally { release.TrySetResult(); }
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot> result = await call.WaitAsync(Deadline);
        Assert.AreEqual(RestFailureKind.Timeout, result.Exchange.Context.FailureKind);
        Assert.IsNull(result.ContentRejection);
        Assert.IsNull(result.Problem);
    }

    [TestMethod]
    public async Task PassiveTypedRegistrationReusesTheExistingKeyedOwnerAndFirstDefaultWithoutAddingStartup()
    {
        var services = new ServiceCollection();
        ManagementConfigurationClientOptions first = Options("first");
        services.AddCisRestClient(Native(first));
        int hosted = services.Count(item => item.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
        services.AddCisManagementConfigurationClient(first);
        ManagementConfigurationClientOptions second = Options("second");
        services.AddCisRestClient(Native(second));
        services.AddCisManagementConfigurationClient(second);
        Assert.AreEqual(hosted, services.Count(item => item.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)));
        Assert.IsFalse(services.Any(item => item.ServiceType == typeof(CIS.Startup.IStartable)));
        await using ServiceProvider root = services.BuildServiceProvider();
        IManagementConfigurationClient firstKey = root.GetRequiredKeyedService<IManagementConfigurationClient>("first");
        Assert.AreSame(firstKey, root.GetRequiredService<IManagementConfigurationClient>());
        Assert.AreNotSame(firstKey, root.GetRequiredKeyedService<IManagementConfigurationClient>("second"));
    }

    [TestMethod]
    [DataRow("key")]
    [DataRow("family")]
    [DataRow("source")]
    [DataRow("problem-key")]
    [DataRow("problem-source")]
    public async Task ForeignOperationFactsAreRejectedInsideTheOwnerWhileTheOriginalCallerKeyRemains(string mismatch)
    {
        ManagementConfigurationOperationResult operation = OperationFor(4);
        operation = mismatch switch
        {
            "key" or "problem-key" => operation with { Operation = operation.Operation with { Key = "foreign-key" } },
            "family" => operation with { Operation = operation.Operation with { Name = "management.configuration.release.create" } },
            _ => operation with { Result = operation.Result! with { SourceId = Publication } }
        };
        bool problem = mismatch.StartsWith("problem-", StringComparison.Ordinal);
        byte[] bytes = problem ? ManagementConfigurationWire.EncodeResponse(new ManagementConfigurationProblem("about:blank", "Refused", 409,
            "configuration.refused", Operation: operation.Operation, Result: operation)) : ManagementConfigurationWire.EncodeResponse(operation);
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) => ValueTask.FromResult<IRestResponse>(
            new Response(problem ? 409 : 200, new MemoryStream(bytes), media: problem ? "application/problem+json" : "application/json")))));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationOperationResult> result = await client.PublishSourceAsync(Source, "original-key",
            "\"source-1\"", Command<ManagementConfigurationPublicationCommand>("configuration-publicationcommand-valid"));
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.AreEqual("original-key", result.OperationKey);
        Assert.IsNull(result.Exchange.Value);
        Assert.IsNull(result.Problem);
    }

    [TestMethod]
    [DataRow("application")]
    [DataRow("configuration")]
    [DataRow("schema")]
    public async Task KnownReleaseDecisionsMustEchoTheRequestedApplicationDefinitionAndSchema(string mismatch)
    {
        ManagementConfigurationOperationResult operation = OperationFor(6);
        operation = operation with
        {
            Result = mismatch switch
            {
                "application" => operation.Result! with { ApplicationId = Publication },
                "configuration" => operation.Result! with { ConfigurationId = "other.settings" },
                _ => operation.Result! with { SchemaVersion = "2" }
            }
        };
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) => ValueTask.FromResult<IRestResponse>(
            new Response(200, new MemoryStream(ManagementConfigurationWire.EncodeResponse(operation)))))));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationOperationResult> result = await client.CreateReleaseAsync(AppId,
            "sample.settings", "1", "original-key", "\"map-1\"", Command<ManagementConfigurationReleaseCommand>("configuration-releasecommand-valid"));
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.IsNull(result.Exchange.Value);
    }

    [TestMethod]
    public async Task FutureDecisionKindRemainsExplicitlyUnknownUnderTheExpectedOriginalOperation()
    {
        ManagementConfigurationOperationResult operation = OperationFor(4);
        operation = operation with
        {
            Result = operation.Result! with
            {
                Kind = "future-publication-kind"
            }
        };
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) => ValueTask.FromResult<IRestResponse>(
            new Response(200, new MemoryStream(ManagementConfigurationWire.EncodeResponse(operation)))))));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationOperationResult> result = await client.PublishSourceAsync(Source, "original-key",
            "\"source-1\"", Command<ManagementConfigurationPublicationCommand>("configuration-publicationcommand-valid"));
        Assert.IsTrue(result.Exchange.Success);
        Assert.AreEqual("future-publication-kind", result.Exchange.Value!.Result!.Kind);
        Assert.IsFalse(result.Exchange.Value.KnownBusinessDecision.IsKnown);
    }

    [TestMethod]
    [DataRow("registration")]
    [DataRow("stream")]
    [DataRow("submitted")]
    [DataRow("accepted")]
    public async Task StatusReceiptMustEchoItsRegistrationStreamAndExactSubmittedAndAcceptedSequence(string mismatch)
    {
        ManagementConfigurationStatusReport report = Command<ManagementConfigurationStatusReport>("configuration-statusreport-valid");
        var receipt = new ManagementReportReceipt(mismatch == "registration" ? Source : Registration,
            mismatch == "stream" ? "inventory" : "configuration-status", mismatch == "submitted" ? "2" : report.Sequence,
            mismatch == "accepted" ? "2" : report.Sequence, "accepted", Time);
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) => ValueTask.FromResult<IRestResponse>(
            new Response(200, new MemoryStream(ManagementWire.EncodeResponse(receipt)))))));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementReportReceipt> result = await client.ReportStatusAsync(Registration, report);
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.IsNull(result.Exchange.Value);
    }

    [TestMethod]
    [DataRow("text/plain")]
    [DataRow(null)]
    public async Task WrongOrMissingSuccessMediaTypeIsRefusedBeforeAnyDesiredIdentityIsRetained(string? media)
    {
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) => ValueTask.FromResult<IRestResponse>(
            new Response(200, new MemoryStream(Bytes(Case("configuration-payload-digest-mismatch"))), media: media)))));
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot> result = await client.GetRegistrationConfigurationAsync(Target());
        Assert.AreEqual(RestFailureKind.InvalidResponse, result.Exchange.Context.FailureKind);
        Assert.IsNull(result.ContentRejection);
    }

    [TestMethod]
    public async Task RuntimeSnapshotDoesNotInheritAnAdministrativeReadRetryPolicy()
    {
        int requests = 0;
        ManagementConfigurationClientOptions options = Options();
        options.ReadRetryPolicy = RestRetryPolicy.TransientHttp;
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) =>
        {
            requests++;
            return ValueTask.FromResult<IRestResponse>(new Response(503, new MemoryStream()));
        })));
        var client = new ManagementConfigurationClient(rest, options, [RestRetryPolicy.TransientHttp]);
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot> result = await client.GetRegistrationConfigurationAsync(Target());
        Assert.AreEqual(1, requests);
        Assert.AreEqual(503, result.Exchange.Context.HttpStatusCode);
        Assert.AreEqual(RestFailureKind.Http, result.Exchange.Context.FailureKind,
            "The standalone native owner has no named retry executor; the runtime call must explicitly select None.");
    }

    [TestMethod]
    public async Task TighterNativeResponseCeilingIsInheritedAndRequiredSchemaCannotBeOmitted()
    {
        ManagementConfigurationClientOptions options = Options();
        RestClientOptions native = new(options.RestClientName)
        {
            Limits = new()
            {
                MaxResponseBytes = 64
            },
            LogCalls = false
        };
        native.ConfigureCisManagementConfigurationRestClient(options);
        native.Handlers.Add(new Handler((_, _) => ValueTask.FromResult<IRestResponse>(new Response(200,
            new MemoryStream(Bytes(Case("configuration-snapshot-valid")))))));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementConfigurationClient(rest, options);
        ManagementConfigurationCallResult<ManagementConfigurationSnapshot> result = await client.GetRegistrationConfigurationAsync(Target());
        Assert.AreEqual(RestFailureKind.LimitExceeded, result.Exchange.Context.FailureKind);
        Assert.IsNull(result.ContentRejection);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.GetAssignmentsAsync(AppId, "sample.settings", null!));
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("publication")]
    [DataRow("release")]
    [DataRow("assignments")]
    [DataRow("preview")]
    [DataRow("status")]
    public async Task ScopedReadValuesMustEchoTheirActualRequestedUriOrPreviewBinding(string kind)
    {
        string response = kind switch
        {
            "assignments" => "assignmentmap",
            "status" => "adoptionpage",
            _ => kind
        };
        ManagementConfigurationClientOptions options = Options();
        await using var rest = new HttpRestClient(Native(options, new Handler((_, _) => ValueTask.FromResult<IRestResponse>(
            new Response(200, new MemoryStream(Bytes(Case("configuration-" + response + "-valid"))))))));
        var client = new ManagementConfigurationClient(rest, options);
        RestResultContext context = kind switch
        {
            "source" => (await client.GetSourceAsync(Publication)).Exchange.Context,
            "publication" => (await client.GetPublicationAsync(Source)).Exchange.Context,
            "release" => (await client.GetReleaseAsync(Source)).Exchange.Context,
            "assignments" => (await client.GetAssignmentsAsync(Publication, "sample.settings", "1")).Exchange.Context,
            "preview" => (await client.PreviewAsync(Command<ManagementConfigurationPreviewCommand>("configuration-previewcommand-valid")
                with
            {
                ApplicationId = Publication
            })).Exchange.Context,
            _ => (await client.GetStatusAsync(Publication)).Exchange.Context
        };
        Assert.AreEqual(RestFailureKind.InvalidResponse, context.FailureKind);
        Assert.IsFalse(context.HasValue);
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

    private static ManagementConfigurationOperationResult OperationFor(int call)
    {
        string family;
        ManagementConfigurationDecision decision;
        if (call is 4 or 16)
        {
            family = "management.configuration.source.publish";
            decision = new(1, "sourcePublished", "applied", "configuration.source.applied", Time,
                SourceId: Source, PublicationId: Publication, Sha256: new string('a', 64), ETag: "\"source-2\"");
        }
        else if (call is 6 or 17)
        {
            family = "management.configuration.release.create";
            decision = new(1, "releaseCreated", "applied", "configuration.release.applied", Time,
                ApplicationId: AppId, ConfigurationId: "sample.settings", SchemaVersion: "1", ReleaseId: Release, ETag: "\"release-1\"");
        }
        else if (call is 9 or 18)
        {
            family = "management.configuration.assignment.replace";
            ManagementConfigurationAssignmentMap map = Value<ManagementConfigurationAssignmentMap>("configuration-assignmentmap-valid");
            decision = new(1, "assignmentsReplaced", "applied", "configuration.assignment.applied", Time,
                ApplicationId: AppId, ConfigurationId: "sample.settings", SchemaVersion: "1", Generation: map.Generation,
                ETag: map.ETag, Intent: "promote", Assignment: map);
        }
        else
        {
            family = "management.configuration.catalog.apply";
            decision = new(1, "catalogApplied", "applied", "configuration.catalog.applied", Time,
                CatalogId: Source, ManifestDigest: Command<ManagementConfigurationCatalogCommand>("configuration-catalogcommand-valid").ManifestDigest,
                ETag: "\"catalog-2\"");
        }
        return new(new(family, "actual-actor-resource-scope", "original-key"), "replayed", decision, "committed");
    }

    private sealed class HeldCleanup(byte[] bytes, TaskCompletionSource cleaning, TaskCompletionSource release) : MemoryStream(bytes)
    {
        public override async ValueTask DisposeAsync()
        {
            cleaning.TrySetResult();
            await release.Task;
            await base.DisposeAsync();
        }
    }
}
