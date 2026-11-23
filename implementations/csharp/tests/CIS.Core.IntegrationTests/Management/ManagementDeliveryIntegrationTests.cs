using System.Globalization;
using System.Net;
using CIS.Heartbeat;
using CIS.Hosting;
using CIS.Jobs;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.RestClient;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementDeliveryIntegrationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    [TestMethod]
    public async Task ActualCisApplicationRegistersThroughNativeCredentialsReportsWithRealHeartbeatAndDrainsOnStop()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        const string clientName = "managed-facade";
        var definition = new JobDefinition("managed-jobs", "passive-inventory", "Passive inventory", "maintenance",
            [JobExecutionMode.Continuous], ["jobs.execute"]);
        var catalog = new JobCatalog([new("managed-jobs", "maintenance", "Maintenance")],
            [JobHandlerBinding.Continuous<InventoryHandlerMustNotRun>(definition)]);
        await using var application = new CISApplication(builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            fixture.RegisterBoundClient(builder.Services, "workload-a", clientName);
            builder.Services.AddSingleton<IJobCatalog>(catalog);
            builder.Services.AddCisManagementJobsInventory();
            builder.Services.AddCisManagementRegistration(new(clientName)
            {
                RequireInitialRegistration = true,
                InitialRegistrationTimeout = TimeSpan.FromSeconds(10),
                ReportingInterval = TimeSpan.FromMilliseconds(30),
                InitialReconnectDelay = TimeSpan.FromMilliseconds(10),
                MaximumReconnectDelay = TimeSpan.FromMilliseconds(50),
                ReportedInstanceId = "actual-facade-client",
                ProcessStartedAtUtc = DateTimeOffset.UtcNow,
                ApplicationVersion = "native-facade-proof"
            });
        });
        ManagementRegistration? reporter = null;
        try
        {
            await application.StartAsync().WaitAsync(Deadline);
            reporter = application.Services.GetRequiredService<ManagementRegistration>();
            ManagementRegistrationStatus initial = reporter.CaptureStatus();
            Assert.IsTrue(initial.IsRegistered);
            Assert.IsTrue(initial.IsConnected, initial.LastErrorCode);
            Assert.IsTrue(initial.InventoryValid);
            Assert.AreEqual(fixture.ApplicationA, initial.ApplicationId);
            Assert.IsInstanceOfType<HeartbeatRuntime>(application.Services.GetRequiredService<IHeartbeatOwnerFactory>(),
                "The actual facade must run its ordinary Heartbeat owner, not a scripted callback source.");
            IManagementClient client = application.Services.GetRequiredKeyedService<IManagementClient>(clientName);
            using var deadline = new CancellationTokenSource(Deadline);
            ManagementInstance? observed = null;
            while (observed?.Observation is null || long.Parse(observed.Observation.Sequence, CultureInfo.InvariantCulture) < 2)
            {
                ManagementCallResult<ManagementPage<ManagementInstance>> instances = await client.ListInstancesAsync(fixture.ApplicationA,
                    cancellationToken: deadline.Token);
                Assert.IsTrue(instances.Exchange.Success, instances.Problem?.Code);
                observed = instances.Exchange.Value!.Items.SingleOrDefault(instance => instance.RegistrationId == initial.RegistrationId);
                if (observed?.Observation is null || long.Parse(observed.Observation.Sequence, CultureInfo.InvariantCulture) < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10), deadline.Token);
                }
            }
            Assert.AreEqual("actual-facade-client", observed.ReportedInstanceId);
            Assert.AreEqual("native-facade-proof", observed.ApplicationVersion);
            Assert.AreEqual("active", observed.Presence);
            Assert.AreEqual("1", observed.InventorySequence);
            ManagementCallResult<ManagementPage<ManagementInventoryEntry>> inventories = await client.ListInventoryAsync(fixture.ApplicationA,
                cancellationToken: deadline.Token);
            Assert.IsTrue(inventories.Exchange.Success, inventories.Problem?.Code);
            ManagementInventoryEntry published = inventories.Exchange.Value!.Items.Single(entry => entry.RegistrationId == initial.RegistrationId);
            ManagementJobInventory job = published.Inventory.Jobs.Single();
            Assert.AreEqual(definition.ExecutionScope, job.ExecutionScope);
            Assert.AreEqual(definition.JobId, job.JobId);
            Assert.AreEqual(definition.CategoryId, job.CategoryId);
            Assert.IsNull(job.EffectiveConfiguration, "A passive catalog must not invent runtime configuration.");
            Assert.IsNull(job.Observations, "Inventory alone must not activate a handler or manufacture run state.");

            await application.StopAsync().WaitAsync(Deadline);
            Assert.IsTrue(reporter.CaptureStatus().IsStopping);
            Assert.IsFalse(reporter.CaptureStatus().IsConnected);
            await reporter.StopAsync().WaitAsync(Deadline);
            await application.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
        finally
        {
            await application.StopAsync().WaitAsync(Deadline);
            if (reporter is not null)
            {
                await reporter.StopAsync().WaitAsync(Deadline);
            }
        }
    }

    [TestMethod]
    public async Task ActualLostHttpBodyRetainsSdkOriginalKeyAndHealthyReplicaRecoversOneCommittedEffectAndAudit()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(start: false);
        const string originalKey = "actual-http-body-lost";
        var loss = new LoseOneMetadataBody(fixture.Options.BasePath + "/management/v1/applications/" + fixture.ApplicationA + "/metadata", originalKey);
        await fixture.StartReplicaAsync(configure: builder => builder.Services.AddSingleton<IStartupFilter>(loss));
        await fixture.StartReplicaAsync();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        fixture.RegisterBoundClient(builder.Services, "operator-a", "lost-body-client", replica: 0);
        fixture.RegisterBoundClient(builder.Services, "operator-a", "recovery-client", replica: 1);
        IHost host = builder.Build();
        try
        {
            await host.StartAsync().WaitAsync(Deadline);
            IManagementClient first = host.Services.GetRequiredKeyedService<IManagementClient>("lost-body-client");
            IManagementClient healthy = host.Services.GetRequiredKeyedService<IManagementClient>("recovery-client");
            ManagementCallResult<ManagementApplication> read = await first.GetApplicationAsync(fixture.ApplicationA);
            Assert.IsTrue(read.Exchange.Success, read.Problem?.Code);
            ManagementApplication original = read.Exchange.Value!;
            var command = new ManagementMetadataCommand(1, "Committed before HTTP body loss", "One original operation");

            ManagementCallResult<ManagementMetadataResult> lost = await first.ReplaceMetadataAsync(fixture.ApplicationA,
                originalKey, original.MetadataETag, command).WaitAsync(Deadline);

            Assert.IsTrue(loss.DidAbort, "The real service response must reach the one selected post-commit body write.");
            Assert.IsTrue(loss.StartedHeaders, "Native response headers were flushed before the connection was aborted.");
            Assert.AreEqual((int)HttpStatusCode.OK, loss.StatusAtAbort);
            Assert.AreEqual(1, loss.Aborts);
            Assert.IsFalse(lost.Exchange.Success, "A lost response body cannot become an applied SDK result.");
            Assert.AreEqual(RestFailureKind.Transport, lost.Exchange.Context.FailureKind);
            Assert.IsNull(lost.Exchange.Value);
            Assert.AreEqual(originalKey, lost.OperationKey);
            Assert.IsNull(lost.Problem, "No complete server Problem body was received or synthesized.");
            Assert.AreEqual(1L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
            Assert.AreEqual(1L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));

            ManagementCallResult<ManagementMetadataResult> found = await healthy.GetMetadataOperationAsync(fixture.ApplicationA, originalKey);
            Assert.IsTrue(found.Exchange.Success, found.Problem?.Code);
            Assert.AreEqual("found", found.Exchange.Value!.ResultSource);
            Assert.AreEqual("committed", found.Exchange.Value.OriginalCompletion);
            Assert.AreEqual(originalKey, found.Exchange.Value.Operation.Key);
            Assert.AreEqual("2", found.Exchange.Value.Result!.MetadataRevision);
            Assert.AreEqual(command.DisplayName, found.Exchange.Value.Result.Metadata.DisplayName);
            ManagementCallResult<ManagementMetadataResult> replay = await healthy.ReplaceMetadataAsync(fixture.ApplicationA,
                originalKey, original.MetadataETag, command);
            Assert.IsTrue(replay.Exchange.Success, replay.Problem?.Code);
            Assert.AreEqual("replayed", replay.Exchange.Value!.ResultSource);
            Assert.AreEqual("committed", replay.Exchange.Value.OriginalCompletion);
            Assert.AreEqual(found.Exchange.Value.Result, replay.Exchange.Value.Result);
            Assert.AreEqual(found.Exchange.Value.Operation, replay.Exchange.Value.Operation);
            ManagementCallResult<ManagementApplication> current = await healthy.GetApplicationAsync(fixture.ApplicationA);
            Assert.IsTrue(current.Exchange.Success, current.Problem?.Code);
            Assert.AreEqual("2", current.Exchange.Value!.MetadataRevision);
            Assert.AreEqual(1L, await Count(fixture, fixture.Options.OperationsSchema, "operation_journal"));
            Assert.AreEqual(1L, await Count(fixture, fixture.Options.MessagingSchema, "outbox"));
            Assert.AreEqual(1, loss.Aborts, "Lookup and explicit replay must not create another transport-loss attempt.");
        }
        finally
        {
            try
            {
                await host.StopAsync().WaitAsync(Deadline);
            }
            finally
            {
                await ((IAsyncDisposable)host).DisposeAsync().AsTask().WaitAsync(Deadline);
            }
        }
    }

    private static async Task<long> Count(ManagementHostFixture fixture, string schema, string table)
    {
        await using var command = fixture.Source.CreateCommand($"SELECT count(*) FROM \"{schema}\".\"{table}\"");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class InventoryHandlerMustNotRun : IContinuousJobHandler
    {
        public InventoryHandlerMustNotRun() => throw new AssertFailedException("Passive inventory cannot resolve its job handler.");
        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Registration must not execute a job.");
    }

    // A narrowly owned transport fault in the existing real product fixture. The normal native
    // authentication, authorization, handler and transaction run before result-body execution.
    private sealed class LoseOneMetadataBody(string path, string key) : IStartupFilter
    {
        private int selected;
        private int aborted;
        internal bool DidAbort => Volatile.Read(ref aborted) != 0;
        internal int Aborts => Volatile.Read(ref aborted);
        internal bool StartedHeaders
        {
            get; private set;
        }
        internal int StatusAtAbort
        {
            get; private set;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
        {
            application.Use(async (context, continuation) =>
            {
                if (HttpMethods.IsPut(context.Request.Method) && context.Request.Path == path
                    && context.Request.Headers["Idempotency-Key"].Count == 1
                    && context.Request.Headers["Idempotency-Key"][0] == key
                    && Interlocked.CompareExchange(ref selected, 1, 0) == 0)
                {
                    Stream original = context.Response.Body;
                    context.Response.Body = new LostBodyStream(original, context, this);
                    try
                    {
                        await continuation(context);
                    }
                    finally
                    {
                        context.Response.Body = original;
                    }
                }
                else
                {
                    await continuation(context);
                }
            });
            next(application);
        };

        private sealed class LostBodyStream(Stream inner, HttpContext context, LoseOneMetadataBody owner) : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException(); set => throw new NotSupportedException();
            }
            public override void Flush() => inner.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (context.Response.StatusCode != StatusCodes.Status200OK)
                {
                    await inner.WriteAsync(buffer, cancellationToken);
                    return;
                }
                // EncodedResult writes only after the original metadata coordinator/owner returned.
                // Flush real native headers, then terminate the real socket before any body bytes.
                await context.Response.StartAsync(cancellationToken);
                owner.StartedHeaders = context.Response.HasStarted;
                owner.StatusAtAbort = context.Response.StatusCode;
                Interlocked.Increment(ref owner.aborted);
                context.Abort();
                throw new IOException("The owned fixture aborted one committed metadata response body.");
            }
        }
    }
}
