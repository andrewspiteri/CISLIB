using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Examples.CountryData.Coordination;
using CIS.Jobs;
using CIS.Messaging;
using CIS.Operations;
using CIS.Security;
using CIS.Transactions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Examples.CountryData.UnitTests.Coordination;

[TestClass]
public sealed class MaintenanceRecipeExamples
{
    [TestMethod]
    public async Task PassiveCatalogKeepsTheTwoResponsibilitiesDistinctAndTheDefaultApplicationDisabled()
    {
        var services = new ServiceCollection();
        services.AddCountryMaintenanceJobs();
        await using ServiceProvider root = services.BuildServiceProvider();
        IJobCatalog catalog = root.GetRequiredService<IJobCatalog>();
        Assert.AreEqual(2, catalog.Count);
        Assert.AreEqual(typeof(WalletCompactionJob), catalog.Find("wallet-service", "wallet.compaction")!.HandlerType);
        Assert.AreEqual(typeof(CustomerDormancyJob), catalog.Find("customer-service", "customer.dormancy")!.HandlerType);
        Assert.IsNull(catalog.Find("customer-service", "wallet.compaction"));
        JobConfigurationSnapshot configuration = CountryMaintenanceJobs.Settings();
        Assert.IsFalse(configuration.Enabled);
        Assert.IsTrue(configuration.Jobs.Values.All(job => !job.Enabled));
        JobExecutionSettings scheduled = configuration.Jobs[(CountryMaintenanceJobs.CustomerScope, CountryMaintenanceJobs.CustomerJob)].Execution;
        Assert.AreEqual(JobScheduledActivation.ExternalOccurrences, scheduled.ScheduledActivation);
        Assert.AreEqual(new TimeOnly(2, 0), scheduled.Schedule!.LocalTime!.Value);
        Assert.AreEqual("Europe/Malta", scheduled.Schedule.TimeZoneId);
        Assert.AreEqual(JobMissedRunPolicy.Skip, scheduled.Schedule.MissedRunPolicy);
    }

    [TestMethod]
    [DataRow(false, ContinuousIterationOutcome.Idle)]
    [DataRow(true, ContinuousIterationOutcome.MoreWork)]
    public async Task WalletFiniteBatchUsesTheGuardedOriginalOwnerAndMapsItsScriptedCommit(bool more, ContinuousIterationOutcome expected)
    {
        var example = new MaintenanceExampleSupport();
        example.Transactions.Enqueue();
        example.Work.MoreWork = more;
        await using ManualCoordinationLease lease = MaintenanceExampleSupport.Lease(CountryMaintenanceJobs.WalletDefinition);
        var job = new WalletCompactionJob(example.Transactions, example.Guard, example.Work);

        ContinuousIterationResult result = await job.ExecuteAsync(MaintenanceExampleSupport.WalletContext(lease));

        Assert.AreEqual(expected, result.Outcome);
        Assert.AreEqual(TransactionOutcome.Committed, result.Completion!.Outcome);
        ScriptedLocalTransaction owner = example.Transactions.CreatedTransactions.Single();
        Assert.AreSame(owner.Context, example.Guard.Context);
        Assert.AreSame(owner.Context, example.Work.Context);
        Assert.AreSame(lease.Grant, example.Guard.Grant);
        CollectionAssert.AreEqual(new[] { "guard", "wallet" }, example.Steps);
        Assert.AreEqual(1, example.Work.WalletCalls);
        Assert.AreEqual(0, example.Work.CustomerCalls);
        Assert.IsTrue(owner.CommitRequested);
        Assert.IsTrue(owner.DisposeRequested);
    }

    [TestMethod]
    [DataRow(TransactionOutcome.Unknown, ContinuousIterationOutcome.Indeterminate)]
    [DataRow(TransactionOutcome.NotCommitted, ContinuousIterationOutcome.Failed)]
    public async Task WalletCompletionNeverConvertsAnUnknownOrRefusedCommitIntoMoreWork(TransactionOutcome outcome,
        ContinuousIterationOutcome expected)
    {
        var example = new MaintenanceExampleSupport();
        var completion = new TransactionCompletion(outcome, TransactionEndReason.CommitRequested,
            outcome == TransactionOutcome.Unknown, rollbackAttempted: false, rollbackConfirmed: false);
        example.Transactions.Enqueue(completion);
        example.Work.MoreWork = true;
        await using ManualCoordinationLease lease = MaintenanceExampleSupport.Lease(CountryMaintenanceJobs.WalletDefinition);
        var job = new WalletCompactionJob(example.Transactions, example.Guard, example.Work);

        ContinuousIterationResult result = await job.ExecuteAsync(MaintenanceExampleSupport.WalletContext(lease));

        Assert.AreEqual(expected, result.Outcome);
        Assert.AreSame(completion, result.Completion);
        Assert.AreEqual(1, example.Work.WalletCalls);
        Assert.AreEqual(1, example.Transactions.CreatedTransactions.Count);
    }

    [TestMethod]
    public async Task RefusedWalletGuardRollsBackBeforeAnyProtectedBatch()
    {
        var example = new MaintenanceExampleSupport();
        example.Transactions.Enqueue();
        example.Guard.Refusal = CoordinationRejectionReason.Superseded;
        await using ManualCoordinationLease lease = MaintenanceExampleSupport.Lease(CountryMaintenanceJobs.WalletDefinition);
        var job = new WalletCompactionJob(example.Transactions, example.Guard, example.Work);

        ContinuousIterationResult result = await job.ExecuteAsync(MaintenanceExampleSupport.WalletContext(lease));

        Assert.AreEqual(ContinuousIterationOutcome.Failed, result.Outcome);
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Completion!.Outcome);
        Assert.AreEqual(0, example.Work.WalletCalls);
        Assert.IsTrue(example.Transactions.CreatedTransactions.Single().RollbackRequested);
        Assert.IsFalse(example.Transactions.CreatedTransactions.Single().CommitRequested);
    }

    [TestMethod]
    public async Task ActualApplicationHandlerIntervalGuardsTheOriginalContextBeforeInjectedCustomerWork()
    {
        var example = new MaintenanceExampleSupport();
        example.Transactions.Enqueue();
        var invoking = new MaintenanceExampleSupport.InvokingOperation(example.Steps);
        await using ManualCoordinationLease lease = MaintenanceExampleSupport.Lease(CountryMaintenanceJobs.CustomerDefinition);
        JobExecutionContext context = MaintenanceExampleSupport.CustomerContext(lease);
        var job = new CustomerDormancyJob(example.Transactions, invoking, example.Guard, example.Work);

        ScheduledJobResult result = await job.ExecuteAsync(context, new("partition-1", new(2026, 1, 1)));

        Assert.AreEqual(ScheduledJobOutcome.Completed, result.Outcome);
        Assert.AreEqual(TransactionOutcome.Committed, result.Completion!.Outcome);
        Assert.AreSame(example.Transactions.CreatedTransactions.Single().Context, invoking.Context);
        Assert.AreSame(invoking.Context, example.Guard.Context);
        Assert.AreSame(invoking.Context, example.Work.Context);
        Assert.AreSame(context.AdmittedRun!.Operation, example.Work.Original);
        CollectionAssert.AreEqual(new[] { "operation-handler", "guard", "customer" }, example.Steps);
        Assert.AreEqual(0, example.Work.WalletCalls);
        Assert.AreEqual(1, example.Work.CustomerCalls);
    }

    [TestMethod]
    public async Task CustomerGuardRejectionCannotCommitOrReachProtectedWork()
    {
        var example = new MaintenanceExampleSupport();
        example.Transactions.Enqueue();
        example.Guard.Refusal = CoordinationRejectionReason.Superseded;
        var invoking = new MaintenanceExampleSupport.InvokingOperation(example.Steps);
        await using ManualCoordinationLease lease = MaintenanceExampleSupport.Lease(CountryMaintenanceJobs.CustomerDefinition);
        var job = new CustomerDormancyJob(example.Transactions, invoking, example.Guard, example.Work);

        ScheduledJobResult result = await job.ExecuteAsync(MaintenanceExampleSupport.CustomerContext(lease), new("partition-1", new(2026, 1, 1)));

        Assert.AreEqual(ScheduledJobOutcome.OwnershipLost, result.Outcome);
        Assert.AreEqual(TransactionOutcome.NotCommitted, result.Completion!.Outcome);
        Assert.AreEqual(0, example.Work.CustomerCalls);
        Assert.IsTrue(example.Transactions.CreatedTransactions.Single().RollbackRequested);
        Assert.IsFalse(example.Transactions.CreatedTransactions.Single().CommitRequested);
    }

    [TestMethod]
    public async Task ScriptedUnknownThenAnotherGenerationAndFreshLookupKeepTheOriginalIdentityWithoutInventingEffects()
    {
        var example = new MaintenanceExampleSupport();
        var unknown = new TransactionCompletion(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested, true, false, false);
        example.Transactions.Enqueue(unknown);
        example.Transactions.Enqueue();
        OperationDecision<CustomerDormancyResult> decision = OperationDecision<CustomerDormancyResult>.Succeed(new(3));
        example.Operations.EnqueueExecution<CustomerDormancyInput, CustomerDormancyResult>(new(OperationExecutionDisposition.Prepared, decision));
        example.Operations.EnqueueExecution<CustomerDormancyInput, CustomerDormancyResult>(new(OperationExecutionDisposition.Replayed, decision));
        var readCompletion = new TransactionCompletion(TransactionOutcome.NotCommitted, TransactionEndReason.RollbackRequested, false, true, true);
        example.Operations.EnqueueLookup(new OperationLookup<CustomerDormancyResult>(OperationLookupDisposition.Found, decision, readCompletion));
        await using ManualCoordinationLease former = MaintenanceExampleSupport.Lease(CountryMaintenanceJobs.CustomerDefinition);
        await using ManualCoordinationLease successor = MaintenanceExampleSupport.Lease(CountryMaintenanceJobs.CustomerDefinition,
            generation: 2, authorityId: former.Grant.AuthorityId);
        var original = new MessageOperationReference(CountryMaintenanceJobs.CustomerJob, "original-operation-scope", "one-stable-key");
        var job = new CustomerDormancyJob(example.Transactions, example.Operations, example.Guard, example.Work);

        ScheduledJobResult first = await job.ExecuteAsync(MaintenanceExampleSupport.CustomerContext(former, original), new("partition-1", new(2026, 1, 1)));
        Assert.AreEqual(ScheduledJobOutcome.Indeterminate, first.Outcome);
        Assert.AreSame(unknown, first.Completion);
        await former.LoseAsync(CoordinationLossReason.Superseded);
        ScheduledJobResult replay = await job.ExecuteAsync(MaintenanceExampleSupport.CustomerContext(successor, original), new("partition-1", new(2026, 1, 1)));
        OperationLookup<CustomerDormancyResult> lookup = await job.RecoverAsync(original);

        Assert.AreEqual(ScheduledJobOutcome.PreviouslyCompleted, replay.Outcome);
        Assert.IsNull(replay.Completion, "The new read owner's rollback is not the original business completion.");
        Assert.AreEqual(OperationLookupDisposition.Found, lookup.Disposition);
        Assert.AreSame(readCompletion, lookup.ReadScopeCompletion);
        Assert.AreSame(decision, lookup.Decision);
        Assert.AreEqual(3, example.Operations.Calls.Count);
        Assert.IsTrue(example.Operations.Calls.All(call => call.Key == original.Key && call.RequestedScope == original.RequestedScope));
        Assert.AreSame(example.Transactions.CreatedTransactions[0].Context, example.Operations.Calls[0].Context);
        Assert.AreSame(example.Transactions.CreatedTransactions[1].Context, example.Operations.Calls[1].Context);
        Assert.IsTrue(example.Transactions.CreatedTransactions[1].RollbackRequested);
        Assert.IsFalse(example.Transactions.CreatedTransactions[1].CommitRequested);
        Assert.AreEqual(0, example.Guard.Calls, "The public scripted operation service does not invoke handlers or guards.");
        Assert.AreEqual(0, example.Work.CustomerCalls);
    }

    [TestMethod]
    public async Task ControlledExternalDispatchResolvesTheCustomerBindingAndLeavesWalletWorkUntouched()
    {
        var example = new MaintenanceExampleSupport();
        example.Transactions.Enqueue();
        var invoking = new MaintenanceExampleSupport.InvokingOperation(example.Steps);
        var coordination = new ScriptedCoordinationService();
        await using ManualCoordinationLease lease = MaintenanceExampleSupport.Lease(CountryMaintenanceJobs.CustomerDefinition);
        coordination.Enqueue(new(CoordinationAcquireOutcome.Acquired, lease));
        var source = new ScriptedJobRunSource("unit-source");
        AdmittedJobRun run = MaintenanceExampleSupport.CustomerRun();
        source.EnqueueValidation(run, new(true));
        var services = new ServiceCollection();
        services.AddCountryMaintenanceJobs();
        services.AddSingleton<ILocalTransactionFactory>(example.Transactions);
        services.AddSingleton<IDurableOperationService>(invoking);
        services.AddSingleton<ICoordinationGuard>(example.Guard);
        services.AddSingleton<IWalletCompactionWork>(example.Work);
        services.AddSingleton<ICustomerDormancyWork>(example.Work);
        services.AddSingleton<ICoordinationService>(coordination);
        services.AddScoped<ICapabilityAuthorizer, MaintenanceExampleSupport.Authorizer>();
        services.AddScoped<ICoordinationTargetResolver, MaintenanceExampleSupport.Resolver>();
        await using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var runtime = new JobRuntime(root.GetRequiredService<IJobCatalog>(), new MaintenanceExampleSupport.ScopeFactory(root),
            CountryMaintenanceJobs.Settings(1, enabled: true, walletEnabled: true, customerEnabled: true), source);
        await runtime.StartAsync();

        ScheduledJobResult result = await new ScheduledJobDispatcher(runtime).DispatchAsync(run);

        Assert.AreEqual(ScheduledJobOutcome.Completed, result.Outcome);
        Assert.AreEqual(0, example.Work.WalletCalls);
        Assert.AreEqual(1, example.Work.CustomerCalls);
        Assert.AreEqual(1, coordination.Calls.Count);
        Assert.AreEqual(CountryMaintenanceJobs.CustomerScope, coordination.Calls[0].ExecutionScope);
        Assert.AreEqual(CountryMaintenanceJobs.CustomerJob, coordination.Calls[0].DutyId);
        Assert.AreEqual(1, source.Calls.Count);
        Assert.AreEqual(CoordinationLeaseState.Released, lease.State);
    }

    [TestMethod]
    public async Task WrongJobTargetDoesNotOpenATransactionOrBorrowAnotherHandlersPermission()
    {
        var example = new MaintenanceExampleSupport();
        await using ManualCoordinationLease lease = MaintenanceExampleSupport.Lease(CountryMaintenanceJobs.WalletDefinition);
        var job = new CustomerDormancyJob(example.Transactions, example.Operations, example.Guard, example.Work);

        ScheduledJobResult result = await job.ExecuteAsync(MaintenanceExampleSupport.WalletContext(lease), new("partition-1", new(2026, 1, 1)));

        Assert.AreEqual(ScheduledJobOutcome.Unsupported, result.Outcome);
        Assert.AreEqual(0, example.Transactions.CreatedTransactions.Count);
        Assert.AreEqual(0, example.Operations.Calls.Count);
    }

    [TestMethod]
    public async Task TrustedSecurityScopeAdapterRejectsUnknownTargetsAndCancellationBeforeNativeAuthentication()
    {
        int calls = 0;
        await using ServiceProvider root = new ServiceCollection().BuildServiceProvider();
        var factory = new SecurityJobScopeFactory(root, "maintenance-native", (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(AuthenticateResult.NoResult());
        });
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => factory.CreateAsync(new("customer-service", "wallet.compaction", "partition-1",
            JobExecutionMode.Continuous)).AsTask());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => factory.CreateAsync(new("wallet-service", "wallet.compaction", "partition-1",
            JobExecutionMode.Continuous), canceled.Token).AsTask());
        Assert.AreEqual(0, calls);
    }
}
