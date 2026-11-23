using System.Globalization;
using System.Text;
using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Jobs;
using CIS.Management;
using CIS.Messaging;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Tests.UnitTests.Jobs.Scheduled;

internal sealed class ScheduledDispatcherFixture : IAsyncDisposable
{
    internal const string Scope = "scheduled-workload";
    internal const string Job = "customer.dormancy";
    internal static readonly MessageContract Contract = new("dormancy-input", 1);
    private readonly List<Action> unblock = [];
    private readonly ServiceProvider services;
    private readonly JobRuntimeLimits limits;
    private int scopeNumber;
    private int generation;
    internal readonly ManualTimeProvider Clock = new(DateTimeOffset.UnixEpoch);
    internal readonly LocalApplicationIdentity Application = LocalApplicationIdentity.Capture();
    internal readonly Source AdmissionSource = new();
    internal readonly List<ManualCoordinationLease> Leases = [];
    internal readonly List<JobExecutionContext> Contexts = [];
    internal readonly List<int> DisposedScopes = [];
    internal readonly TaskCompletionSource AcquisitionStarted = Signal();
    internal readonly TaskCompletionSource CallbackStarted = Signal();
    internal readonly TaskCompletionSource WorkCleanupStarted = Signal();
    internal readonly JobExecutionSettings Settings;
    internal readonly JobRuntime Runtime;
    internal readonly ScheduledJobDispatcher Dispatcher;
    internal int Acquisitions;
    internal int Calls;
    internal Func<JobExecutionContext, int, CancellationToken, Task<ScheduledJobResult>>? Handler;
    internal Func<CoordinationRequest, CancellationToken, Task<ManagedCoordinationResult>>? Acquire;
    internal Func<int, ValueTask>? Cleanup;
    internal Func<int, bool>? Authorized;
    internal Func<int, CancellationToken, ValueTask>? Opening;
    internal CancellationToken CallbackToken;

    internal ScheduledDispatcherFixture(int maximumInvocations = 2, int maximumPending = 4,
        int? categoryMaximum = null, bool enabled = true, JobExecutionSettings? settings = null,
        IJobRunSource? source = null, IJobScheduleEvaluator? evaluator = null, IManagedWorkAdmission? managedAdmission = null)
    {
        Settings = settings ?? new(JobExecutionMode.Scheduled,
            schedule: JobScheduleDescriptor.Daily(new(2, 0), "Europe/Malta", JobMissedRunPolicy.RunLatestOnce));
        var definition = new JobDefinition(Scope, Job, "Customer dormancy", "lifecycle",
            [JobExecutionMode.Scheduled], ["jobs.execute"]);
        limits = new JobRuntimeLimits(maximumInvocations: maximumInvocations, maximumPendingDispatches: maximumPending);
        var catalog = new JobCatalog([new(Scope, "lifecycle", "Lifecycle")],
            [JobHandlerBinding.Scheduled<int, ScheduledHandler>(definition, new Codec())], limits);
        var collection = new ServiceCollection();
        collection.AddScoped(_ => new ScopedState());
        collection.AddScoped<ICapabilityAuthorizer>(provider => new Authorizer(this, provider.GetRequiredService<ScopedState>()));
        collection.AddSingleton<ICoordinationTargetResolver>(new Resolver());
        collection.AddSingleton<ICoordinationService>(new Coordination(this));
        collection.AddScoped(_ => new ScheduledHandler(this));
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Runtime = new(catalog, new ScopeFactory(this), Configuration(1, enabled, categoryMaximum: categoryMaximum),
            source ?? AdmissionSource, evaluator, Clock, managedAdmission);
        Dispatcher = new(Runtime);
    }

    internal JobConfigurationSnapshot Configuration(long revision, bool enabled = true,
        JobExecutionSettings? execution = null, int? categoryMaximum = null, TimeSpan? categoryTimeout = null)
        => new(revision, enabled, [new(Scope, "lifecycle", maximumInvocations: categoryMaximum, defaultTimeout: categoryTimeout)],
            [new(Scope, Job, execution ?? Settings, enabled: true)], limits);

    internal AdmittedJobRun Run(string resource = "partition", string occurrence = "occurrence-1",
        JobExecutionSettings? settings = null, string source = "controlled-source", string? job = null,
        MessageContract? contract = null, MessageOperationReference? operation = null, long revision = 7)
    {
        operation ??= new("Dormancy", "original-business-scope", "original-key-" + occurrence);
        EncodedMessage input = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Command, contract ?? Contract,
            "claimed-producer", "claimed-environment", Clock.GetUtcNow(), "7"u8.ToArray(), operation: operation));
        return new(Scope, job ?? Job, resource, occurrence, operation, revision, settings ?? Settings, input, source,
            "admission-" + occurrence, Clock.GetUtcNow());
    }

    internal ManualCoordinationLease NewLease(CoordinationRequest request)
    {
        var lease = new ManualCoordinationLease(new(Guid.Parse("f8b7c45d-f7c0-4013-9270-0e91fc6de79a"),
            new(new(Application.Environment, request.ExecutionScope, request.DutyId, request.ResourceId), Application.Application,
                Application.InstanceId, Guid.NewGuid()), Interlocked.Increment(ref generation), Clock.GetUtcNow().AddSeconds(30)));
        lock (Leases)
        {
            Leases.Add(lease);
        }

        return lease;
    }

    internal TaskCompletionSource<T> Hold<T>(T fallback)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        unblock.Add(() => completion.TrySetResult(fallback));
        return completion;
    }

    internal TaskCompletionSource Hold()
    {
        TaskCompletionSource completion = Signal();
        unblock.Add(() => completion.TrySetResult());
        return completion;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Action release in unblock)
        {
            release();
        }

        await Runtime.DisposeAsync();
        await services.DisposeAsync();
    }

    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed class Source : IJobRunSource
    {
        internal int Calls;
        internal Func<AdmittedJobRun, CancellationToken, ValueTask<JobAdmissionValidation>>? Validate;
        internal readonly TaskCompletionSource Started = Signal();
        public string SourceId => "controlled-source";
        public JobRunSourceCapabilities Capabilities => JobRunSourceCapabilities.ExternallyAdmittedOccurrences;
        public ValueTask<JobAdmissionValidation> ValidateAsync(AdmittedJobRun run, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            Started.TrySetResult();
            return Validate?.Invoke(run, cancellationToken) ?? ValueTask.FromResult(new JobAdmissionValidation(true));
        }
    }

    private sealed class ScopeFactory(ScheduledDispatcherFixture owner) : IJobExecutionScopeFactory
    {
        public async ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default)
        {
            int number = Interlocked.Increment(ref owner.scopeNumber);
            if (owner.Opening is { } opening)
            {
                await opening(number, cancellationToken);
            }

            AsyncServiceScope scope = owner.services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ScopedState>().Number = number;
            return new OwnedScope(owner, scope, number);
        }
    }

    private sealed class OwnedScope(ScheduledDispatcherFixture owner, AsyncServiceScope scope, int number) : IJobExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public async ValueTask DisposeAsync()
        {
            if (number % 2 == 0)
            {
                owner.WorkCleanupStarted.TrySetResult();
            }

            await scope.DisposeAsync();
            lock (owner.DisposedScopes)
            {
                owner.DisposedScopes.Add(number);
            }

            if (owner.Cleanup is { } cleanup)
            {
                await cleanup(number);
            }
        }
    }

    private sealed class ScopedState
    {
        internal int Number;
    }

    private sealed class Authorizer(ScheduledDispatcherFixture owner, ScopedState state) : ICapabilityAuthorizer
    {
        public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(owner.Authorized?.Invoke(state.Number) == false
                ? AuthorizationDecision.Deny(new(SecurityFailureKind.CapabilityDenied, "Test scoped denial.")) : AuthorizationDecision.Allow);
        }
    }

    private sealed class Resolver : ICoordinationTargetResolver
    {
        public ValueTask<CoordinationTarget?> ResolveAsync(CoordinationRequest request, LocalApplicationIdentity application,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<CoordinationTarget?>(CoordinationTarget.ForDuty(new(application.Environment,
                request.ExecutionScope, request.DutyId, request.ResourceId), "authorized-shared-target", ["coordination.execute"]));
    }

    private sealed class Coordination(ScheduledDispatcherFixture owner) : ICoordinationService
    {
        public Task<ManagedCoordinationResult> AcquireAsync(CoordinationRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref owner.Acquisitions);
            owner.AcquisitionStarted.TrySetResult();
            return owner.Acquire?.Invoke(request, cancellationToken)
                ?? Task.FromResult(new ManagedCoordinationResult(CoordinationAcquireOutcome.Acquired, owner.NewLease(request)));
        }
    }

    private sealed class ScheduledHandler(ScheduledDispatcherFixture owner) : IScheduledJobHandler<int>
    {
        public Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, int input, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref owner.Calls);
            lock (owner.Contexts)
            {
                owner.Contexts.Add(context);
            }

            owner.CallbackToken = cancellationToken;
            owner.CallbackStarted.TrySetResult();
            return owner.Handler?.Invoke(context, input, cancellationToken)
                ?? Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Completed));
        }
    }

    private sealed class Codec : IMessageCodec<int>
    {
        public MessageContract Contract => ScheduledDispatcherFixture.Contract;
        public byte[] Encode(int value) => Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture));
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
            => int.TryParse(Encoding.UTF8.GetString(json.Span), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
