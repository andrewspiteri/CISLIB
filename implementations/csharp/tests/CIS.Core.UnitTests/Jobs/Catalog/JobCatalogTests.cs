using CIS.Coordination;
using CIS.Jobs;
using CIS.Jobs.Registration;
using CIS.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs.Catalog;

[TestClass]
public sealed class JobCatalogTests
{
    [TestMethod]
    public void PortableDefinitionOwnsItsModesCapabilitiesAndOrdinalIdentity()
    {
        JobExecutionMode[] modes = [JobExecutionMode.Continuous];
        string[] capabilities = ["jobs.execute"];
        var definition = new JobDefinition("wallet-service", "wallet.compaction", "Compaction", "maintenance", modes, capabilities);
        modes[0] = JobExecutionMode.Scheduled;
        capabilities[0] = "other.execute";

        Assert.AreEqual(JobExecutionMode.Continuous, definition.SupportedModes[0]);
        Assert.AreEqual("jobs.execute", definition.ExecuteCapabilities[0]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<JobExecutionMode>)definition.SupportedModes).Clear());
        Assert.ThrowsExactly<ArgumentException>(() => Definition(new string('é', 65)));
        Assert.ThrowsExactly<ArgumentException>(() => Definition("bad\u0001job"));
        Assert.ThrowsExactly<ArgumentException>(() => Definition("bad\ud800job"));
        Assert.AreEqual(new string('é', 64), Definition(new string('é', 64)).JobId);
    }

    [TestMethod]
    public void PassiveRegistrationAndCatalogInspectionNeverConstructHandlersOrResolveDependencies()
    {
        var services = new ServiceCollection();
        services.AddCisJobCategory(new("wallet-service", "maintenance", "Maintenance"));
        services.AddCisContinuousJob<ConstructorMustNotRun>(Definition());
        services.AddCisJobCatalog();
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        JobCatalog catalog = provider.GetRequiredService<JobCatalog>();

        Assert.AreSame(catalog, provider.GetRequiredService<IJobCatalog>());
        Assert.AreEqual(1, catalog.Count);
        Assert.AreEqual("wallet.compaction", catalog.Inspect()[0].JobId);
        Assert.AreEqual(typeof(ConstructorMustNotRun), catalog.Find("wallet-service", "wallet.compaction")!.HandlerType);
        Assert.IsNull(catalog.Find("wallet-service", "Wallet.Compaction"));
        Assert.IsNull(catalog.Find("customer-service", "wallet.compaction"));
        catalog.ValidateActivation(new(0));
    }

    [TestMethod]
    public void OneExactBindingCannotBeMergedOrAdvertiseAnUnimplementedMode()
    {
        var services = new ServiceCollection();
        services.AddCisContinuousJob<WalletJob>(Definition());
        Assert.ThrowsExactly<ArgumentException>(() => services.AddCisScheduledJob<int, CustomerJob>(
            Definition(mode: JobExecutionMode.Scheduled), new Codec()));

        var both = new JobDefinition("wallet-service", "wallet.compaction", "Compaction", "maintenance",
            [JobExecutionMode.Continuous, JobExecutionMode.Scheduled], ["jobs.execute"]);
        Assert.ThrowsExactly<ArgumentException>(() => JobHandlerBinding.Continuous<WalletJob>(both));
        Assert.ThrowsExactly<ArgumentException>(() => JobHandlerBinding.Scheduled<int, CustomerJob>(both, new Codec()));
        JobHandlerBinding binding = JobHandlerBinding.Combined<int, CombinedJob>(both, new Codec());
        Assert.AreEqual(typeof(CombinedJob), binding.HandlerType);
        Assert.AreEqual(new MessageContract("job-input", 1), binding.InputContract);
    }

    [TestMethod]
    public void CategoriesStayWithinScopeAndNeverSelectAHandlerOrChangeTheOwnershipKey()
    {
        var first = Definition();
        var renamed = new JobDefinition("wallet-service", "wallet.compaction", "Renamed", "new-category",
            [JobExecutionMode.Continuous], ["jobs.execute"]);
        var catalog = new JobCatalog(
            [new("wallet-service", "maintenance", "Wallet"), new("customer-service", "maintenance", "Customer")],
            [JobHandlerBinding.Continuous<WalletJob>(first),
                JobHandlerBinding.Scheduled<int, CustomerJob>(new("customer-service", "customer.dormancy", "Dormancy", "maintenance",
                    [JobExecutionMode.Scheduled], ["jobs.execute"]), new Codec())]);

        Assert.AreEqual(typeof(WalletJob), catalog.Find("wallet-service", "wallet.compaction")!.HandlerType);
        Assert.AreEqual(typeof(CustomerJob), catalog.Find("customer-service", "customer.dormancy")!.HandlerType);
        Assert.IsNull(catalog.Find("customer-service", "wallet.compaction"));
        Assert.AreEqual(new CoordinationKey("test", first.ExecutionScope, first.JobId),
            new CoordinationKey("test", renamed.ExecutionScope, renamed.JobId));
        Assert.ThrowsExactly<ArgumentException>(() => new JobCatalog(
            [new("customer-service", "maintenance", "Customer")], [JobHandlerBinding.Continuous<WalletJob>(first)]));
    }

    [TestMethod]
    public void CatalogBoundsAndPagesRejectOverflowWithoutExpandingTheirState()
    {
        JobHandlerBinding[] bindings = Enumerable.Range(0, 200)
            .Select(index => JobHandlerBinding.Continuous<WalletJob>(Definition("job" + index))).ToArray();
        var catalog = new JobCatalog([new("wallet-service", "maintenance", "Maintenance")], bindings,
            new JobRuntimeLimits(maximumDefinitions: 200, maximumSnapshotPageSize: 20));
        Assert.AreEqual(200, catalog.Count);
        Assert.AreEqual(20, catalog.Inspect().Count);
        Assert.AreEqual(0, catalog.Inspect(200).Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => catalog.Inspect(pageSize: 21));
        Assert.ThrowsExactly<ArgumentException>(() => new JobCatalog(
            [new("wallet-service", "maintenance", "Maintenance")], bindings, new JobRuntimeLimits(maximumDefinitions: 199)));
        Assert.ThrowsExactly<ArgumentException>(() => new JobCatalog(
            [new("wallet-service", "maintenance", "One"), new("wallet-service", "maintenance", "Duplicate")], []));
    }

    [TestMethod]
    public void MissingHandlersAndOrdinaryConstructorDependenciesFailBeforeActivation()
    {
        var services = new ServiceCollection();
        services.AddCisJobCategory(new("wallet-service", "maintenance", "Maintenance"));
        services.AddCisContinuousJob<WalletJob>(Definition());
        services.RemoveAll<WalletJob>();
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisJobCatalog());

        var missingDependency = new ServiceCollection();
        missingDependency.AddCisJobCategory(new("wallet-service", "maintenance", "Maintenance"));
        missingDependency.AddCisContinuousJob<MissingDependencyJob>(Definition());
        missingDependency.AddCisJobCatalog();
        Assert.ThrowsExactly<AggregateException>(() => missingDependency.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        }));
    }

    [TestMethod]
    public void OpaqueMetadataAndChangesAfterFreezeAreRejectedWithoutInvokingFactories()
    {
        var opaque = new ServiceCollection();
        opaque.AddSingleton<JobCategoryDefinition>(_ => throw new AssertFailedException("Metadata factories must not execute."));
        Assert.ThrowsExactly<InvalidOperationException>(() => opaque.AddCisJobCategory(new("wallet-service", "maintenance", "Maintenance")));

        var frozen = new ServiceCollection();
        frozen.AddCisJobCategory(new("wallet-service", "maintenance", "Maintenance"));
        frozen.AddCisContinuousJob<WalletJob>(Definition());
        frozen.AddCisJobCatalog();
        Assert.ThrowsExactly<InvalidOperationException>(() => frozen.AddCisContinuousJob<WalletJob>(Definition("another")));
        Assert.ThrowsExactly<InvalidOperationException>(() => frozen.AddCisJobCatalog());
    }

    [TestMethod]
    public void ConfigurationCopiesCompleteExactTargetsAndRetainsCategoryDisable()
    {
        var catalog = new JobCatalog([new("wallet-service", "maintenance", "Maintenance")],
            [JobHandlerBinding.Continuous<WalletJob>(Definition())]);
        JobSettings[] jobs = [new("wallet-service", "wallet.compaction", new(JobExecutionMode.Continuous), enabled: true)];
        JobCategorySettings[] categories = [new("wallet-service", "maintenance", enabled: false, maximumInvocations: 1)];
        var snapshot = new JobConfigurationSnapshot(1, enabled: true, categories, jobs);
        jobs[0] = new("wallet-service", "other", new(JobExecutionMode.Continuous));
        categories[0] = new("wallet-service", "maintenance", enabled: true);

        catalog.ValidateActivation(snapshot);
        Assert.IsFalse(snapshot.Categories[("wallet-service", "maintenance")].Enabled);
        Assert.IsTrue(snapshot.Jobs[("wallet-service", "wallet.compaction")].Enabled);
        Assert.ThrowsExactly<ArgumentException>(() => catalog.ValidateConfiguration(new(2, jobs:
            [new("wallet-service", "wallet.compaction", new(JobExecutionMode.Scheduled))])));
        Assert.ThrowsExactly<ArgumentException>(() => catalog.ValidateConfiguration(new(2, jobs:
            [new("customer-service", "wallet.compaction", new(JobExecutionMode.Continuous))])));
        Assert.ThrowsExactly<ArgumentException>(() => catalog.ValidateConfiguration(new(2, jobs:
            [new("wallet-service", "wallet.compaction", new(JobExecutionMode.Continuous), categoryId: "missing")])));
    }

    private static JobDefinition Definition(string id = "wallet.compaction", JobExecutionMode mode = JobExecutionMode.Continuous)
        => new("wallet-service", id, "Compaction", "maintenance", [mode], ["jobs.execute"]);

    private sealed class ConstructorMustNotRun : IContinuousJobHandler
    {
        public ConstructorMustNotRun() => throw new AssertFailedException("Passive catalog operations cannot construct handlers.");
        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Passive catalog operations cannot execute handlers.");
    }

    private sealed class WalletJob : IContinuousJobHandler
    {
        public WalletJob()
        {
        }
        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new ContinuousIterationResult(ContinuousIterationOutcome.Idle));
    }

    private sealed class CustomerJob : IScheduledJobHandler<int>
    {
        public CustomerJob()
        {
        }
        public Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, int input, CancellationToken cancellationToken = default)
            => Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Completed));
    }

    private sealed class CombinedJob : IContinuousJobHandler, IScheduledJobHandler<int>
    {
        public CombinedJob()
        {
        }
        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new ContinuousIterationResult(ContinuousIterationOutcome.Idle));
        public Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, int input, CancellationToken cancellationToken = default)
            => Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Completed));
    }

    private sealed class MissingDependencyJob : IContinuousJobHandler
    {
        private readonly IDisposable missing;

        public MissingDependencyJob(IDisposable missing)
        {
            this.missing = missing;
        }

        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
        {
            missing.Dispose();
            return Task.FromResult(new ContinuousIterationResult(ContinuousIterationOutcome.Idle));
        }
    }

    private sealed class Codec : IMessageCodec<int>
    {
        public MessageContract Contract => new("job-input", 1);
        public byte[] Encode(int value) => throw new AssertFailedException("Registration cannot encode.");
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
            => throw new AssertFailedException("Registration cannot decode.");
    }
}
