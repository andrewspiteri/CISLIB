using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Coordination.Hosting;
using CIS.Jobs;
using CIS.Jobs.Hosting;
using CIS.Messaging;
using CIS.Metrics.OpenTelemetry;
using CIS.Security;
using CIS.Tests.IntegrationTests.Metrics;
using CIS.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Coordination.Observability;

[TestClass]
public sealed class CoordinationCollectionTests
{
    [TestMethod]
    public async Task ActualHttpScrapeExportsAllSevenBoundariesAndNeverCompletesAnUnfinishedCallbackOrScope()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var source = new ControlledSource();
        var work = new ControlledWork();
        var provider = new Provider(clock);
        await using var coordination = new CoordinationRuntime(provider, new(), clock);
        var coordinationComposition = new CoordinationComposition();
        var jobsComposition = new JobsComposition();
        await using var coordinationObservations = new CoordinationInstrumentation(coordinationComposition, coordination, clock);
        var settings = new JobExecutionSettings(JobExecutionMode.Scheduled,
            schedule: JobScheduleDescriptor.FixedDelay(TimeSpan.FromMinutes(5), JobMissedRunPolicy.Skip));
        var definition = new JobDefinition("collection-work", "dormancy", "Dormancy", "maintenance",
            [JobExecutionMode.Scheduled], ["jobs.execute"]);
        var catalog = new JobCatalog([new("collection-work", "maintenance", "Maintenance")],
            [JobHandlerBinding.Scheduled<int, Handler>(definition, new Codec())]);
        var scopes = new ScopeFactory();
        await using var jobs = new JobRuntime(catalog, scopes, new(1, true,
            jobs: [new("collection-work", "dormancy", settings, enabled: true)]), source, timeProvider: clock);
        await using var jobObservations = new JobInstrumentation(jobsComposition, jobs, clock);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(CoordinationCollectionTests).Assembly.FullName,
            EnvironmentName = "CoordinationCollection"
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(coordinationComposition);
        builder.Services.AddSingleton(jobsComposition);
        builder.Services.AddSingleton(coordinationObservations);
        builder.Services.AddSingleton(jobObservations);
        builder.Services.AddSingleton<ICoordinationTargetResolver>(new Resolver());
        builder.Services.AddSingleton<ICapabilityAuthorizer>(new Authorizer());
        builder.Services.AddScoped<ICoordinationService>(root => new CoordinationService(coordination,
            root.GetRequiredService<ICoordinationTargetResolver>(), root.GetRequiredService<ICapabilityAuthorizer>(),
            LocalApplicationIdentity.Capture()));
        builder.Services.AddScoped(_ => new Cleanup(work));
        builder.Services.AddScoped(root => new Handler(work, root.GetRequiredService<Cleanup>()));
        builder.Services.AddCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
        await using WebApplication app = builder.Build();
        scopes.Root = app.Services;
        app.UseRouting();
        app.UseCisMetrics();
        Task<ScheduledJobResult>? dispatch = null;
        ICoordinationLease? direct = null;
        await app.StartAsync().WaitAsync(CollectionTestHost.Deadline);
        try
        {
            await coordination.StartAsync();
            await jobs.StartAsync();
            LocalApplicationIdentity application = LocalApplicationIdentity.Capture();
            ManagedCoordinationResult acquired = await coordination.AcquireAuthorizedAsync(new(
                new(application.Environment, "collection-work", "direct", "partition"), application.Application,
                application.InstanceId, Guid.NewGuid()));
            direct = acquired.Lease!;
            var transactions = new ScriptedLocalTransactionFactory();
            transactions.Enqueue();
            await using ILocalTransaction transaction = await transactions.BeginAsync("Ledger");
            var guard = new ObservedCoordinationGuard(new RejectingGuard(), coordinationObservations);
            Assert.IsFalse((await guard.ValidateAsync(transaction.Context, direct.Grant)).Admitted);
            var operation = new MessageOperationReference("Dormancy", "business-scope", "stable-original-operation");
            EncodedMessage input = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Command, new("collection-input", 1),
                "claimed-producer", "claimed-environment", clock.GetUtcNow(), "7"u8.ToArray(), operation: operation));
            var run = new AdmittedJobRun("collection-work", "dormancy", "partition", "stable-occurrence", operation,
                7, settings, input, source.SourceId, "controlled-admission");
            dispatch = new ScheduledJobDispatcher(jobs).DispatchAsync(run);
            await source.Started.Task.WaitAsync(CollectionTestHost.Deadline);
            using var client = new HttpClient { BaseAddress = new(app.Urls.Single()), Timeout = CollectionTestHost.Deadline };
            string pending = await client.GetStringAsync("/_metrics");
            CollectionText.AssertIdentity(pending);
            Assert.AreEqual(1d, Sum(pending, "cis_jobs_pending_dispatches{"));
            Assert.AreEqual(1d, Sum(pending, "cis_coordination_active_leases{"));
            Assert.AreEqual(0d, Sum(pending, "cis_jobs_invocation_duration_seconds_count{"));
            source.Validated.TrySetResult(new(true));
            await work.Started.Task.WaitAsync(CollectionTestHost.Deadline);
            direct.BeginDraining(TimeSpan.FromMilliseconds(500));
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await direct.DisposeAsync();
            string unfinished = await client.GetStringAsync("/_metrics");
            Assert.AreEqual(1d, Sum(unfinished, "cis_jobs_active_invocations{"));
            Assert.AreEqual(0d, Sum(unfinished, "cis_jobs_pending_dispatches{"));
            Assert.AreEqual(0d, Sum(unfinished, "cis_jobs_invocation_duration_seconds_count{"));
            work.Finished.TrySetResult(new(ScheduledJobOutcome.Completed));
            await work.CleanupStarted.Task.WaitAsync(CollectionTestHost.Deadline);
            clock.Advance(TimeSpan.FromSeconds(1));
            string cleaning = await client.GetStringAsync("/_metrics");
            Assert.AreEqual(1d, Sum(cleaning, "cis_jobs_active_invocations{"));
            Assert.AreEqual(0d, Sum(cleaning, "cis_jobs_invocation_duration_seconds_count{"));
            work.Cleaned.TrySetResult();
            Assert.AreEqual(ScheduledJobOutcome.Completed, (await dispatch.WaitAsync(CollectionTestHost.Deadline)).Outcome);
            string text = await client.GetStringAsync("/_metrics");
            CollectionText.AssertIdentity(text);
            Assert.AreEqual(1d, Sum(text, "cis_jobs_invocation_duration_seconds_count{"));
            Assert.AreEqual(1.5d, Sum(text, "cis_jobs_invocation_duration_seconds_sum{"));
            Assert.AreEqual(0d, Sum(text, "cis_jobs_active_invocations{"));
            Assert.AreEqual(0d, Sum(text, "cis_coordination_active_leases{"));
            Assert.AreEqual(1d, Sum(text, "cis_coordination_ownership_losses_total{"));
            Assert.AreEqual(1d, Sum(text, "cis_coordination_guard_rejections_total{"));
            Assert.IsTrue(Sum(text, "cis_coordination_operation_duration_seconds_count{") >= 3);
            Assert.HasCount(15, CollectionText.Samples(text).Where(line => line.StartsWith("cis_jobs_invocation_duration_seconds_bucket{", StringComparison.Ordinal)).ToArray());
            Assert.IsTrue(text.Contains("# TYPE cis_jobs_invocation_duration_seconds histogram", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("job_id=", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("resource=", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("generation=", StringComparison.Ordinal));
        }
        finally
        {
            source.Validated.TrySetResult(new(false));
            work.Finished.TrySetResult(new(ScheduledJobOutcome.Failed));
            work.Cleaned.TrySetResult();
            if (dispatch is not null)
            {
                await dispatch;
            }

            if (direct is not null)
            {
                await direct.DisposeAsync();
            }

            await app.Services.GetRequiredService<MetricsCollectionContext>().StopAsync();
            await jobs.StopAsync();
            await coordination.StopAsync();
            await app.StopAsync(CancellationToken.None);
        }
    }

    private static double Sum(string text, string prefix) => CollectionText.Samples(text)
        .Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).Sum(CollectionText.Value);

    private sealed class ControlledSource : IJobRunSource
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<JobAdmissionValidation> Validated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string SourceId => "controlled-source";
        public JobRunSourceCapabilities Capabilities => JobRunSourceCapabilities.ExternallyAdmittedOccurrences;
        public ValueTask<JobAdmissionValidation> ValidateAsync(AdmittedJobRun run, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return new(Validated.Task);
        }
    }

    private sealed class ControlledWork
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<ScheduledJobResult> Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CleanupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Cleaned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Cleanup(ControlledWork work) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            work.CleanupStarted.TrySetResult();
            await work.Cleaned.Task;
        }
    }

    private sealed class Handler(ControlledWork work, Cleanup cleanup) : IScheduledJobHandler<int>
    {
        public Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, int input, CancellationToken cancellationToken = default)
        {
            GC.KeepAlive(cleanup);
            work.Started.TrySetResult();
            return work.Finished.Task;
        }
    }

    private sealed class Codec : IMessageCodec<int>
    {
        public MessageContract Contract => new("collection-input", 1);
        public byte[] Encode(int value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            value = System.Text.Json.JsonSerializer.Deserialize<int>(json.Span);
            return contract == Contract;
        }
    }

    private sealed class ScopeFactory : IJobExecutionScopeFactory
    {
        internal IServiceProvider Root = null!;
        public ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IJobExecutionScope>(new Scope(Root.CreateAsyncScope()));
    }

    private sealed class Scope(AsyncServiceScope scope) : IJobExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    private sealed class Resolver : ICoordinationTargetResolver
    {
        public ValueTask<CoordinationTarget?> ResolveAsync(CoordinationRequest request, LocalApplicationIdentity application, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<CoordinationTarget?>(CoordinationTarget.ForDuty(new(application.Environment,
                request.ExecutionScope, request.DutyId, request.ResourceId), application.Application, ["jobs.execute"]));
    }

    private sealed class Authorizer : ICapabilityAuthorizer
    {
        public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(AuthorizationDecision.Allow);
    }

    private sealed class RejectingGuard : ICoordinationGuard
    {
        public Task<CoordinationGuardResult> ValidateAsync(ILocalTransactionContext context, CoordinationGrant grant, CancellationToken cancellationToken = default)
            => Task.FromResult(new CoordinationGuardResult(CoordinationRejectionReason.Superseded));
    }

    private sealed class Provider(ManualTimeProvider clock) : ICoordinationProvider
    {
        private long generation;
        public Guid AuthorityId { get; } = Guid.NewGuid();
        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CoordinationAcquireResult> AcquireAsync(CoordinationAcquireRequest request, CancellationToken cancellationToken = default)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            return Task.FromResult(new CoordinationAcquireResult(CoordinationAcquireOutcome.Acquired,
                new(AuthorityId, request.Acquisition, Interlocked.Increment(ref generation), clock.GetUtcNow().AddSeconds(30)), TimeSpan.FromSeconds(30)));
        }
        public Task<CoordinationMutationResult> RenewAsync(CoordinationGrant grant, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded,
                new(AuthorityId, grant.Acquisition, grant.Generation, clock.GetUtcNow().AddSeconds(30)), TimeSpan.FromSeconds(30)));
        public Task<CoordinationMutationResult> ReleaseAsync(CoordinationGrant grant, CancellationToken cancellationToken = default)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            return Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded));
        }
        public Task<CoordinationObservation> ResolveAsync(CoordinationAcquisition acquisition, CancellationToken cancellationToken = default)
            => Task.FromResult(new CoordinationObservation(CoordinationObservationOutcome.NotObserved));
    }
}
