using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Examples.CountryData.Coordination;
using CIS.Jobs;
using CIS.Messaging;
using CIS.Operations;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Examples.CountryData.UnitTests.Coordination;

internal sealed class MaintenanceExampleSupport
{
    internal readonly ScriptedLocalTransactionFactory Transactions = new();
    internal readonly ScriptedDurableOperationService Operations = new();
    internal readonly List<string> Steps = [];
    internal GuardRecorder Guard
    {
        get;
    }
    internal WorkRecorder Work
    {
        get;
    }

    internal MaintenanceExampleSupport()
    {
        Guard = new(Steps);
        Work = new(Steps);
    }

    internal static ManualCoordinationLease Lease(JobDefinition definition, long generation = 1, Guid? authorityId = null)
    {
        LocalApplicationIdentity application = LocalApplicationIdentity.Capture();
        var acquisition = new CoordinationAcquisition(new(application.Environment, definition.ExecutionScope, definition.JobId, "partition-1"),
            application.Application, application.InstanceId, Guid.NewGuid());
        return new(new(authorityId ?? Guid.NewGuid(), acquisition, generation, DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    internal static JobExecutionContext WalletContext(ManualCoordinationLease lease)
        => new(CountryMaintenanceJobs.WalletDefinition, "partition-1", lease, EmptyServices.Instance,
            CountryMaintenanceJobs.Settings().Jobs[(CountryMaintenanceJobs.WalletScope, CountryMaintenanceJobs.WalletJob)].Execution);

    internal static JobExecutionContext CustomerContext(ManualCoordinationLease lease, MessageOperationReference? original = null)
    {
        AdmittedJobRun run = CustomerRun(original);
        return new(CountryMaintenanceJobs.CustomerDefinition, run.ResourceId, lease, EmptyServices.Instance, run.Settings, run);
    }

    internal static AdmittedJobRun CustomerRun(MessageOperationReference? original = null)
    {
        original ??= new(CountryMaintenanceJobs.CustomerJob, "original-customer-operation-scope", "original-occurrence-key");
        var codec = new CustomerDormancyCodec();
        var input = new CustomerDormancyInput("partition-1", new(2026, 1, 1));
        EncodedMessage frame = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Command, codec.Contract,
            "scheduler-fixture", "Development", DateTimeOffset.UtcNow, codec.Encode(input), operation: original));
        JobExecutionSettings settings = CountryMaintenanceJobs.Settings().Jobs[(CountryMaintenanceJobs.CustomerScope,
            CountryMaintenanceJobs.CustomerJob)].Execution;
        return new(CountryMaintenanceJobs.CustomerScope, CountryMaintenanceJobs.CustomerJob, input.PartitionId, "occurrence-1",
            original, 7, settings, frame, "unit-source", "unit-admission");
    }

    internal sealed class GuardRecorder(List<string> steps) : ICoordinationGuard
    {
        internal CoordinationRejectionReason Refusal;
        internal ILocalTransactionContext? Context;
        internal CoordinationGrant? Grant;
        internal int Calls;

        public Task<CoordinationGuardResult> ValidateAsync(ILocalTransactionContext context, CoordinationGrant grant,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            steps.Add("guard");
            Calls++;
            Context = context;
            Grant = grant;
            if (Refusal != CoordinationRejectionReason.None)
            {
                context.MarkRollbackOnly("unit.guard_refused");
            }

            return Task.FromResult(new CoordinationGuardResult(Refusal));
        }
    }

    internal sealed class WorkRecorder(List<string> steps) : IWalletCompactionWork, ICustomerDormancyWork
    {
        internal bool MoreWork;
        internal int WalletCalls;
        internal int CustomerCalls;
        internal ILocalTransactionContext? Context;
        internal MessageOperationReference? Original;

        public Task<bool> CompactNextBatchAsync(ILocalTransactionContext context, string partitionId, CancellationToken cancellationToken = default)
        {
            Assert.AreEqual("partition-1", partitionId);
            steps.Add("wallet");
            WalletCalls++;
            Context = context;
            return Task.FromResult(MoreWork);
        }

        public Task<CustomerDormancyResult> ApplyAsync(ILocalTransactionContext context, CustomerDormancyInput input,
            MessageOperationReference originalOperation, CancellationToken cancellationToken = default)
        {
            Assert.AreEqual("partition-1", input.PartitionId);
            steps.Add("customer");
            CustomerCalls++;
            Context = context;
            Original = originalOperation;
            return Task.FromResult(new CustomerDormancyResult(3));
        }
    }

    // Only this application's callback interval is exercised. This is no journal, authorization or SQL substitute.
    internal sealed class InvokingOperation(List<string> steps) : IDurableOperationService
    {
        internal ILocalTransactionContext? Context;
        internal int Calls;

        public async Task<OperationExecution<TResult>> ExecuteAsync<TRequest, TResult>(ILocalTransactionContext context,
            OperationRequest<TRequest> request,
            Func<ILocalTransactionContext, TRequest, CancellationToken, Task<OperationDecision<TResult>>> handler,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Context = context;
            steps.Add("operation-handler");
            OperationDecision<TResult> decision = await handler(context, request.Input, cancellationToken);
            return new(OperationExecutionDisposition.Prepared, decision);
        }

        public Task<OperationLookup<TResult>> GetResultAsync<TResult>(OperationLookupRequest request,
            CancellationToken cancellationToken = default)
            => throw new AssertFailedException("This logical handler stub performs no durable lookup.");
    }

    internal sealed class ScopeFactory(ServiceProvider root) : IJobExecutionScopeFactory
    {
        public ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IJobExecutionScope>(new Scope(root.CreateAsyncScope()));

        private sealed class Scope(AsyncServiceScope owned) : IJobExecutionScope
        {
            public IServiceProvider Services => owned.ServiceProvider;
            public ValueTask DisposeAsync() => owned.DisposeAsync();
        }
    }

    // Explicit test substitutions. The production recipe uses SecurityJobScopeFactory and real native evidence.
    internal sealed class Authorizer : ICapabilityAuthorizer
    {
        public Authorizer()
        {
        }

        public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(AuthorizationDecision.Allow);
    }

    internal sealed class Resolver : ICoordinationTargetResolver
    {
        public Resolver()
        {
        }

        public ValueTask<CoordinationTarget?> ResolveAsync(CoordinationRequest request, LocalApplicationIdentity application,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<CoordinationTarget?>(CoordinationTarget.ForDuty(
                new(application.Environment, request.ExecutionScope, request.DutyId, request.ResourceId), request.ExecutionScope, ["coordination.acquire"]));
    }

    private sealed class EmptyServices : IServiceProvider
    {
        internal static EmptyServices Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }
}
