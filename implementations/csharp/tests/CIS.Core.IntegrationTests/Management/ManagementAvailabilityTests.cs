using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using CIS.CommonTesting;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementAvailabilityTests
{
    [TestMethod]
    public async Task IsolatedReplicaFailsClosedWhileHealthyReplicaRetainsAuthorityAndReconcilesManagedAdmissionAfterReplicaLoss()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 2, start: false);
        string role = "rm07_availability_" + Guid.NewGuid().ToString("N");
        string schemas = string.Join(",", new[] { fixture.Options.Persistence.Schema, fixture.Options.OperationsSchema,
            fixture.Options.MessagingSchema }.Select(value => "\"" + value + "\""));
        await Sql(fixture, $"""
            BEGIN;
            CREATE ROLE "{role}" NOLOGIN NOINHERIT;
            GRANT USAGE ON SCHEMA {schemas} TO "{role}";
            GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA {schemas} TO "{role}";
            GRANT USAGE,SELECT,UPDATE ON ALL SEQUENCES IN SCHEMA {schemas} TO "{role}";
            COMMIT;
            """);
        try
        {
            // Only this newly owned role is affected. Both real product roots still use
            // the same durable primary, schema, namespace, authority and cursor key.
            var connection = new NpgsqlConnectionStringBuilder(fixture.Options.ConnectionString)
            {
                Options = "-c role=" + role
            };
            byte[] cursorKey = fixture.Options.CopyCursorSigningKey();
            ManagementServiceOptions isolated;
            try
            {
                isolated = new(connection.ConnectionString, cursorKey, fixture.Options.Persistence, fixture.Options.BasePath,
                    fixture.Options.MaximumConcurrentRequests, fixture.Options.OperationsSchema, fixture.Options.MessagingSchema,
                    fixture.Options.Delivery, fixture.Options.Backend);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(cursorKey);
            }
            await fixture.StartReplicaAsync(serviceOptions: isolated);
            await fixture.StartReplicaAsync();

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
            builder.Logging.ClearProviders();
            ManagementClientOptions first = fixture.RegisterBoundClient(builder.Services, "workload-a", "availability-a");
            ManagementClientOptions second = fixture.RegisterBoundClient(builder.Services, "workload-a", "availability-b", replica: 1);
            IHost clients = builder.Build();
            try
            {
                await clients.StartAsync();
                IManagementClient a = clients.Services.GetRequiredKeyedService<IManagementClient>(first.RestClientName);
                IManagementClient b = clients.Services.GetRequiredKeyedService<IManagementClient>(second.RestClientName);
                HttpClient operatorA = fixture.Client("operator-a");
                HttpClient operatorB = fixture.Client("operator-a", replica: 1);
                string applicationPath = "applications/" + fixture.ApplicationA;
                ManagementApplication before = await Read<ManagementApplication>(operatorA, applicationPath);
                const string key = "replica-availability-original";
                string resultPath = applicationPath + "/metadata-operations/" + key;
                using HttpResponseMessage applied = await Replace(operatorA, applicationPath, key,
                    before.MetadataETag, new(1, "Retained across replica loss", "Exact local value"));
                Assert.AreEqual(HttpStatusCode.OK, applied.StatusCode);
                ManagementMetadataResult original = await ManagementServiceTests.Decode<ManagementMetadataResult>(applied);
                Assert.AreEqual("committed", original.OriginalCompletion);
                ManagementControlConfirmation firstControl = Value(await a.GetControlAsync(fixture.RegistrationA, Guid.NewGuid().ToString("D")));
                ManagementControlConfirmation secondControl = Value(await b.GetControlAsync(fixture.RegistrationA, Guid.NewGuid().ToString("D")));
                Assert.AreEqual(firstControl.AuthorityId, secondControl.AuthorityId);
                Assert.AreEqual(firstControl.ControlRevision, secondControl.ControlRevision);

                // CommonTesting controls only local scheduling/content. The forwarder
                // sends every actual typed call through the real TLS/JWT-bound SDKs.
                var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
                await using var heartbeat = new ManualHeartbeat();
                var observations = new ScriptedManagementObservationSource();
                observations.EnqueueValue(new("ready", "healthy"));
                var inventory = new ScriptedManagementInventorySource();
                inventory.EnqueueValue(new([], [], []));
                var forwarding = new ReplicaClient(a);
                var reconciler = new HeldReconciler();
                object acceptedLocalState = reconciler.AcceptedLocalState;
                var admission = new ManagedWorkAdmission(clock);
                await using var runtime = new ManagementRegistration(admission, forwarding, heartbeat, observations, inventory,
                    new(first.RestClientName)
                    {
                        ReportingInterval = TimeSpan.FromDays(1),
                        InitialReconnectDelay = TimeSpan.FromMilliseconds(10),
                        MaximumReconnectDelay = TimeSpan.FromMilliseconds(10),
                        ProcessStartedAtUtc = clock.GetUtcNow(),
                        ApplicationVersion = "native-availability"
                    }, [reconciler], clock);
                Assert.IsFalse(admission.TryAdmit());
                await runtime.StartAsync();
                Assert.IsTrue(admission.TryAdmit());
                ManagedWorkAdmissionSnapshot accepted = admission.CaptureSnapshot();
                Assert.AreEqual(firstControl.AuthorityId, accepted.AuthorityId);

                await Sql(fixture, $"REVOKE SELECT ON TABLE {Table(fixture, "management_authority")} FROM \"{role}\"");
                clock.Advance(TimeSpan.FromSeconds(6));
                ManualHeartbeatResult failed = await heartbeat.TriggerAsync(runtime);
                Assert.AreEqual(ManualHeartbeatOutcome.Failed, failed.Outcome);
                Assert.AreEqual(503, forwarding.LastControlStatus);
                Assert.IsFalse(admission.TryAdmit());
                Assert.AreEqual(ManagedWorkAdmissionState.ContinuingOnly, admission.CaptureSnapshot().State);
                Assert.AreSame(acceptedLocalState, reconciler.AcceptedLocalState);
                Assert.IsTrue(runtime.CaptureStatus().IsRegistered, "A backend outage does not discard the accepted registration.");

                ManagementCallResult<ManagementRegistrationReceipt> refusedRegistration = await a.RegisterAsync(Guid.NewGuid().ToString("D"),
                    ManagementHostFixture.Request("isolated-bootstrap"));
                Assert.AreEqual(503, refusedRegistration.Exchange.Context.HttpStatusCode);
                using HttpResponseMessage refusedMutation = await Replace(operatorA, applicationPath,
                    "isolated-new-effect", original.Result!.MetadataETag, new(1, "Must not apply"));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refusedMutation.StatusCode);
                await AssertOperational(fixture, 0, ready: false);
                ManagementControlConfirmation healthy = Value(await b.GetControlAsync(fixture.RegistrationA, Guid.NewGuid().ToString("D")));
                Assert.AreEqual(firstControl.AuthorityId, healthy.AuthorityId);
                Assert.AreEqual(firstControl.ControlRevision, healthy.ControlRevision);
                ManagementRegistrationReceipt healthyBootstrap = Value(await b.RegisterAsync(Guid.NewGuid().ToString("D"),
                    ManagementHostFixture.Request("healthy-replica-bootstrap")));
                Assert.AreEqual(fixture.ApplicationA, healthyBootstrap.ApplicationId);
                await AssertOperational(fixture, 1, ready: true);
                AssertOriginal(original, await Read<ManagementMetadataResult>(operatorB, resultPath));
                using HttpResponseMessage replayResponse = await Replace(operatorB, applicationPath, key,
                    before.MetadataETag, new(1, "Retained across replica loss", "Exact local value"));
                Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
                ManagementMetadataResult replay = await ManagementServiceTests.Decode<ManagementMetadataResult>(replayResponse);
                Assert.AreEqual("replayed", replay.ResultSource);
                Assert.AreEqual(original.Operation, replay.Operation);
                Assert.AreEqual(original.Result, replay.Result);
                Assert.AreEqual("committed", replay.OriginalCompletion);

                // Stop just A's actual listener/owners. The fixture retains both roots
                // for its final disposal; B's origin and data source stay unchanged.
                string healthyOrigin = fixture.Origins[1];
                await fixture.StopReplicaAsync(0);
                forwarding.Current = b;
                reconciler.Hold = true;
                clock.Advance(TimeSpan.FromSeconds(1));
                Task<ManualHeartbeatResult> recovery = heartbeat.TriggerAsync(runtime);
                ManualHeartbeatResult? recovered = null;
                try
                {
                    await reconciler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.AreEqual(ManagedWorkAdmissionState.Reconciling, admission.CaptureSnapshot().State);
                    Assert.IsFalse(admission.TryAdmit(), "A healthy replica alone cannot bypass the owning module's reconciliation.");
                    Assert.IsFalse(recovery.IsCompleted);
                    Assert.AreSame(acceptedLocalState, reconciler.AcceptedLocalState);
                    Assert.AreEqual(firstControl.AuthorityId, forwarding.LastConfirmation!.AuthorityId);
                    Assert.AreEqual(firstControl.ControlRevision, forwarding.LastConfirmation.ControlRevision);
                }
                finally
                {
                    reconciler.Release.TrySetResult();
                    recovered = await recovery;
                }
                Assert.IsNotNull(recovered);
                Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, recovered.Outcome);
                Assert.IsTrue(admission.TryAdmit());
                Assert.AreEqual(ManagedWorkAdmissionState.Ready, admission.CaptureSnapshot().State);
                Assert.AreEqual(accepted.AuthorityId, admission.CaptureSnapshot().AuthorityId);
                Assert.AreEqual(accepted.ControlRevision, admission.CaptureSnapshot().ControlRevision);
                Assert.AreEqual(healthyOrigin, fixture.Origins[1]);
                AssertOriginal(original, await Read<ManagementMetadataResult>(operatorB, resultPath));
                ManagementApplication retained = await Read<ManagementApplication>(operatorB, applicationPath);
                Assert.AreEqual(original.Result.MetadataRevision, retained.MetadataRevision);
                Assert.AreEqual(original.Result.Metadata, retained.Metadata);
                await using var count = fixture.Source.CreateCommand($"SELECT count(*) FROM \"{fixture.Options.OperationsSchema}\".operation_journal");
                Assert.AreEqual(1L, (long)(await count.ExecuteScalarAsync())!, "Neither isolation nor replica loss may execute a replacement command.");
                await using var audit = fixture.Source.CreateCommand($"SELECT count(*) FROM \"{fixture.Options.MessagingSchema}\".outbox");
                Assert.AreEqual(1L, (long)(await audit.ExecuteScalarAsync())!, "Replay and recovery retain the one original outgoing Audit intent.");
            }
            finally
            {
                try
                {
                    await clients.StopAsync();
                }
                finally
                {
                    await ((IAsyncDisposable)clients).DisposeAsync();
                }
            }
        }
        finally
        {
            await fixture.StopReplicasAsync();
            await Sql(fixture, $"DROP OWNED BY \"{role}\"; DROP ROLE \"{role}\"");
        }
    }

    private static T Value<T>(ManagementCallResult<T> result) where T : class
    {
        Assert.IsTrue(result.Exchange.Success, result.Problem?.Code);
        Assert.IsNotNull(result.Exchange.Value);
        return result.Exchange.Value;
    }

    private static void AssertOriginal(ManagementMetadataResult original, ManagementMetadataResult found)
    {
        Assert.AreEqual("found", found.ResultSource);
        Assert.AreEqual("committed", found.OriginalCompletion);
        Assert.AreEqual(original.Operation, found.Operation);
        Assert.AreEqual(original.Result, found.Result);
    }

    private static async Task<T> Read<T>(HttpClient client, string path) where T : class
    {
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await ManagementServiceTests.Decode<T>(response);
    }

    private static async Task<HttpResponseMessage> Replace(HttpClient client, string applicationPath, string key,
        string etag, ManagementMetadataCommand command)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, applicationPath + "/metadata");
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = new ByteArrayContent(ManagementWire.EncodeRequest(command));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.SendAsync(request);
    }

    private static async Task AssertOperational(ManagementHostFixture fixture, int replica, bool ready)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        string origin = "http://127.0.0.1:" + fixture.OperationalPorts[replica].ToString(CultureInfo.InvariantCulture);
        using HttpResponseMessage health = await client.GetAsync(origin + "/_health");
        using HttpResponseMessage readiness = await client.GetAsync(origin + "/_health/ready");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode, "Backend isolation does not make process liveness fail.");
        Assert.AreEqual(ready ? HttpStatusCode.OK : HttpStatusCode.InternalServerError, readiness.StatusCode);
    }

    private static string Table(ManagementHostFixture fixture, string table) => $"\"{fixture.Options.Persistence.Schema}\".\"{table}\"";

    private static async Task Sql(ManagementHostFixture fixture, string text)
    {
        await using var command = fixture.Source.CreateCommand(text);
        await command.ExecuteNonQueryAsync();
    }

    // This explicit public module hook controls local reconciliation only. It
    // supplies neither a remote confirmation nor a native identity/provider.
    private sealed class HeldReconciler : IManagedStateReconciler
    {
        internal object AcceptedLocalState { get; } = new();
        internal bool Hold
        {
            get; set;
        }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsReady => true;

        public async Task<bool> ReconcileAsync(ManagementControlConfirmation confirmation, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Hold)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return true;
        }
    }

    // One selected actual client, one retained control observation, no automatic
    // failover, fabricated result, credential projection or extra retry owner.
    private sealed class ReplicaClient(IManagementClient initial) : IManagementClient
    {
        internal IManagementClient Current { get; set; } = initial;
        internal int? LastControlStatus
        {
            get; private set;
        }
        internal ManagementControlConfirmation? LastConfirmation
        {
            get; private set;
        }

        public async Task<ManagementCallResult<ManagementControlConfirmation>> GetControlAsync(string id, string attempt, CancellationToken token = default)
        {
            ManagementCallResult<ManagementControlConfirmation> result = await Current.GetControlAsync(id, attempt, token);
            LastControlStatus = result.Exchange.Context.HttpStatusCode;
            LastConfirmation = result.Exchange.Value;
            return result;
        }

        public Task<ManagementCallResult<ManagementRegistrationReceipt>> RegisterAsync(string id, ManagementRegistrationRequest request, ManagementCallOptions? options = null, CancellationToken token = default) => Current.RegisterAsync(id, request, options, token);
        public Task<ManagementCallResult<ManagementReportReceipt>> ReportObservationAsync(string id, ManagementObservation value, ManagementCallOptions? options = null, CancellationToken token = default) => Current.ReportObservationAsync(id, value, options, token);
        public Task<ManagementCallResult<ManagementReportReceipt>> ReportInventoryAsync(string id, ManagementInventorySnapshot value, ManagementCallOptions? options = null, CancellationToken token = default) => Current.ReportInventoryAsync(id, value, options, token);
        public Task<ManagementCallResult<ManagementCloseReceipt>> CloseRegistrationAsync(string id, ManagementCallOptions? options = null, CancellationToken token = default) => Current.CloseRegistrationAsync(id, options, token);
        public Task<ManagementCallResult<ManagementServiceInfo>> GetServiceInfoAsync(ManagementCallOptions? options = null, CancellationToken token = default) => Current.GetServiceInfoAsync(options, token);
        public Task<ManagementCallResult<ManagementPage<ManagementApplication>>> ListApplicationsAsync(ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken token = default) => Current.ListApplicationsAsync(page, options, token);
        public Task<ManagementCallResult<ManagementApplication>> GetApplicationAsync(string id, ManagementCallOptions? options = null, CancellationToken token = default) => Current.GetApplicationAsync(id, options, token);
        public Task<ManagementCallResult<ManagementPage<ManagementInstance>>> ListInstancesAsync(string id, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken token = default) => Current.ListInstancesAsync(id, page, options, token);
        public Task<ManagementCallResult<ManagementPage<ManagementInventoryEntry>>> ListInventoryAsync(string id, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken token = default) => Current.ListInventoryAsync(id, page, options, token);
        public Task<ManagementCallResult<ManagementCapabilities>> GetCapabilitiesAsync(string id, ManagementCallOptions? options = null, CancellationToken token = default) => Current.GetCapabilitiesAsync(id, options, token);
        public Task<ManagementCallResult<ManagementMetadataResult>> ReplaceMetadataAsync(string id, string key, string etag, ManagementMetadataCommand command, ManagementCallOptions? options = null, CancellationToken token = default) => Current.ReplaceMetadataAsync(id, key, etag, command, options, token);
        public Task<ManagementCallResult<ManagementMetadataResult>> GetMetadataOperationAsync(string id, string key, ManagementCallOptions? options = null, CancellationToken token = default) => Current.GetMetadataOperationAsync(id, key, options, token);
    }
}
