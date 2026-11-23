using System.Globalization;
using System.Text;
using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Jobs;
using CIS.Management;
using CIS.Management.Wire;
using CIS.Messaging;
using CIS.Security;
using CIS.Tests.UnitTests.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class JobsManagementInventorySourceTests
{
    private const string Timestamp = "2026-11-23T12:34:56.1234567Z";

    [TestMethod]
    public void HundredsOfJobsReadEveryCatalogPageAndRetainExactScopedCategoryTuplesWithoutResolvingHandlers()
    {
        var bindings = new List<JobHandlerBinding>();
        foreach (string scope in new[] { "scope-a", "scope-b" })
        {
            for (int index = 0; index < 250; index++)
            {
                string id = "job-" + index.ToString("D3", CultureInfo.InvariantCulture);
                bindings.Add(JobHandlerBinding.Continuous<HandlerMustNotBeConstructed>(Definition(scope, id, "shared")));
            }
        }
        var catalog = new JobCatalog([new("scope-a", "shared", "A"), new("scope-b", "shared", "B")], bindings,
            new(maximumDefinitions: 500, maximumSnapshotPageSize: 17));
        var source = new JobsManagementInventorySource(catalog);

        ManagementInventoryContent content = source.CaptureInventory();

        Assert.HasCount(500, content.Jobs);
        Assert.HasCount(2, content.Categories);
        CollectionAssert.AreEqual(new[] { ("scope-a", "shared"), ("scope-b", "shared") },
            content.Categories.Select(category => (category.ExecutionScope, category.CategoryId)).ToArray());
        CollectionAssert.AreEqual(catalog.Bindings.Keys.OrderBy(key => key.Scope, StringComparer.Ordinal)
            .ThenBy(key => key.Id, StringComparer.Ordinal).ToArray(),
            content.Jobs.Select(job => (job.ExecutionScope, job.JobId)).ToArray());
        Assert.AreEqual("job-249", content.Jobs[^1].JobId);
        Assert.AreEqual("scope-b", content.Jobs[^1].ExecutionScope);
        Assert.IsTrue(content.Jobs.All(job => job.CategoryId == "shared" && job.EffectiveConfiguration is null && job.Observations is null));
        ManagementInventorySnapshot decoded = Roundtrip(content);
        Assert.HasCount(500, decoded.Jobs);
        Assert.HasCount(2, decoded.Categories);
        Assert.AreEqual("jobs", decoded.Modules.Single().Kind);
    }

    [TestMethod]
    public void ExactNativeUtf8BoundsAndBothSupportedModesSurviveWireProjectionWithoutScalarReinterpretation()
    {
        string scope = new('é', 128);
        string id = new('é', 64);
        string display = new('é', 128);
        string description = new('é', 1024);
        string resourceDescription = new('é', 512);
        var definition = new JobDefinition(scope, id, display, id,
            [JobExecutionMode.Continuous, JobExecutionMode.Scheduled], ["jobs.execute"], description, resourceDescription);
        var catalog = new JobCatalog([new(scope, id, display, description)],
            [JobHandlerBinding.Combined<int, HandlerMustNotBeConstructed>(definition, new CodecMustNotRun())]);

        ManagementJobInventory projected = Roundtrip(new JobsManagementInventorySource(catalog).CaptureInventory()).Jobs.Single();

        Assert.AreEqual(scope, projected.ExecutionScope);
        Assert.AreEqual(id, projected.JobId);
        Assert.AreEqual(id, projected.CategoryId);
        Assert.AreEqual(display, projected.DisplayName);
        Assert.AreEqual(description, projected.Description);
        Assert.AreEqual(resourceDescription, projected.ResourceScopeDescription);
        Assert.AreEqual(256, Encoding.UTF8.GetByteCount(projected.ExecutionScope));
        Assert.AreEqual(128, Encoding.UTF8.GetByteCount(projected.JobId));
        Assert.AreEqual(2048, Encoding.UTF8.GetByteCount(projected.Description!));
        CollectionAssert.AreEqual(new[] { "continuous", "scheduled" }, projected.SupportedModes.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => Definition(scope, id + "é", id));
    }

    [TestMethod]
    [DataRow("jobs")]
    [DataRow("categories")]
    [DataRow("modules")]
    public void WholeCatalogCountLimitRefusesInsteadOfReturningAnAdvertisedPartialInventory(string boundary)
    {
        JobCatalog catalog = Catalog();
        var limits = new ManagementWireLimits(maximumJobs: boundary == "jobs" ? 1 : 2,
            maximumCategories: boundary == "categories" ? 1 : 2, maximumModules: boundary == "modules" ? 1 : 2);
        var source = new JobsManagementInventorySource(catalog, limits: limits,
            modules: [new("jobs", "1"), new("another", "1")]);

        ManagementInventoryRejectedException refusal = Assert.ThrowsExactly<ManagementInventoryRejectedException>(() => source.CaptureInventory());

        Assert.AreEqual("management.inventory.local_limit", refusal.Code);
        Assert.AreEqual(2, catalog.Count);
        Assert.HasCount(2, catalog.Categories);
    }

    [TestMethod]
    public void ReportingBoundaryRefusesOversizedCompleteWireWithoutTruncatingTheAvailableCatalog()
    {
        var limits = new ManagementWireLimits(maximumInventoryRequestBytes: 512);
        ManagementInventoryContent content = new JobsManagementInventorySource(Catalog(), limits: limits).CaptureInventory();
        ManagementInventorySnapshot snapshot = Snapshot(content);
        byte[] full = ManagementWire.EncodeRequest(snapshot);
        Assert.IsGreaterThan(512, full.Length);

        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeRequest(snapshot, limits));
        Assert.IsNull(ManagementWire.DecodeRequest<ManagementInventorySnapshot>(full, out ManagementWireFailure failure, limits));

        Assert.AreEqual(ManagementWireFailure.TooLarge, failure);
        Assert.HasCount(2, content.Jobs);
        Assert.HasCount(2, content.Categories);
        Assert.HasCount(2, Roundtrip(content).Jobs);
    }

    [TestMethod]
    public async Task ExistingRuntimePreservesRevisionZeroAndEffectiveCategoryEnablementWithoutActivatingHandlers()
    {
        await using var fixture = new JobRuntimeTestSupport();
        var source = new JobsManagementInventorySource(fixture.Catalog, fixture.Runtime);
        ManagementJobInventory initial = source.CaptureInventory().Jobs.Single();
        Assert.AreEqual("0", initial.EffectiveConfiguration!.Revision);
        Assert.IsTrue(initial.EffectiveConfiguration.Enabled);
        Assert.AreEqual("maintenance", initial.CategoryId);
        Assert.IsNull(initial.Observations);
        const long revision = 9007199254740993;
        Assert.IsTrue(fixture.Runtime.TryApplyConfiguration(fixture.Configuration(revision,
            categories: [new("wallet-service", "other", enabled: false)], settings: id =>
                new("wallet-service", id, new(JobExecutionMode.Continuous), enabled: true, categoryId: "other"))));

        ManagementInventoryContent updated = source.CaptureInventory();
        ManagementJobInventory job = Roundtrip(updated).Jobs.Single();

        Assert.AreEqual("9007199254740993", job.EffectiveConfiguration!.Revision);
        Assert.IsFalse(job.EffectiveConfiguration.Enabled);
        Assert.AreEqual("other", job.CategoryId);
        Assert.IsTrue(updated.Categories.Any(category => category.ExecutionScope == job.ExecutionScope && category.CategoryId == job.CategoryId));
        Assert.AreEqual("maintenance", fixture.Catalog.Inspect().Single().CategoryId);
        Assert.AreEqual(0, fixture.Control.CreatedScopes);
        Assert.IsTrue(fixture.Control.Invocations.IsEmpty);
    }

    [TestMethod]
    public async Task ActualResourceObservationPagesRetainAvailableOwnerGenerationAndOmitUnprovidedSchedulerFacts()
    {
        await using var fixture = new JobRuntimeTestSupport(limits: new(maximumSnapshotPageSize: 1));
        await fixture.Runtime.StartAsync();
        await using JobDutyReservation first = fixture.Runtime.TryReserveContinuous(
            new("wallet-service", "wallet.compaction", "partition-a", JobExecutionMode.Continuous)).Duty!;
        await using JobDutyReservation second = fixture.Runtime.TryReserveContinuous(
            new("wallet-service", "wallet.compaction", "partition-b", JobExecutionMode.Continuous)).Duty!;
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        DateTimeOffset expiry = fixture.Clock.GetUtcNow().AddMinutes(1);
        var acquisition = new CoordinationAcquisition(new(local.Environment, "wallet-service", "wallet.compaction", "partition-a"),
            local.Application, local.InstanceId, Guid.NewGuid());
        await using var lease = new ManualCoordinationLease(new(Guid.NewGuid(), acquisition, 9007199254740993, expiry));
        Assert.IsTrue(fixture.Runtime.AttachLease(first, lease));
        var source = new JobsManagementInventorySource(fixture.Catalog, fixture.Runtime);

        IReadOnlyList<ManagementJobObservation> observations = Roundtrip(source.CaptureInventory()).Jobs.Single().Observations!;

        Assert.HasCount(2, observations);
        CollectionAssert.AreEqual(new[] { "partition-a", "partition-b" }, observations.Select(observation => observation.ResourceId).ToArray());
        ManagementJobObservation owned = observations[0];
        Assert.AreEqual("continuous", owned.Mode);
        Assert.AreEqual("9007199254740993", owned.ObservedOwner!.Generation);
        Assert.AreEqual(local.Application, owned.ObservedOwner.OwnerApplication);
        Assert.AreEqual(local.InstanceId, owned.ObservedOwner.InstanceId);
        Assert.AreEqual(expiry.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture), owned.ObservedOwner.ExpiresAtUtc);
        Assert.AreEqual(fixture.Clock.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture), owned.ObservedOwner.ObservedAtUtc);
        Assert.IsNull(observations[1].ObservedOwner);
        Assert.IsTrue(observations.All(observation => observation.LastOccurrenceUtc is null && observation.NextOccurrenceUtc is null));
        var bounded = new JobsManagementInventorySource(fixture.Catalog, fixture.Runtime,
            new(maximumJobObservations: 1));
        Assert.AreEqual("management.inventory.local_limit",
            Assert.ThrowsExactly<ManagementInventoryRejectedException>(() => bounded.CaptureInventory()).Code);
        Assert.AreEqual(0, fixture.Control.CreatedScopes);
        Assert.IsTrue(fixture.Control.Invocations.IsEmpty);
    }

    [TestMethod]
    public void ConstructorCapturesModuleSelectionAndPreCancellationPreservesTheOriginalToken()
    {
        ManagementModuleInventory[] modules = [new("selected", "original")];
        var source = new JobsManagementInventorySource(Catalog(), modules: modules);
        modules[0] = new("replaced", "later");
        Assert.AreEqual(new ManagementModuleInventory("selected", "original"), source.CaptureInventory().Modules.Single());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException error = Assert.ThrowsExactly<OperationCanceledException>(() => source.CaptureInventory(cancellation.Token));

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.HasCount(2, source.CaptureInventory().Jobs);
    }

    private static JobCatalog Catalog() => new([new("scope", "first", "First"), new("scope", "second", "Second")],
        [JobHandlerBinding.Continuous<HandlerMustNotBeConstructed>(Definition("scope", "one", "first")),
            JobHandlerBinding.Continuous<HandlerMustNotBeConstructed>(Definition("scope", "two", "second"))]);

    private static JobDefinition Definition(string scope, string id, string category)
        => new(scope, id, id, category, [JobExecutionMode.Continuous], ["jobs.execute"]);

    private static ManagementInventorySnapshot Snapshot(ManagementInventoryContent content)
        => new("1", Timestamp, content.Modules, content.Categories, content.Jobs);

    private static ManagementInventorySnapshot Roundtrip(ManagementInventoryContent content)
        => ManagementWire.DecodeRequest<ManagementInventorySnapshot>(ManagementWire.EncodeRequest(Snapshot(content)))
            ?? throw new AssertFailedException("The complete native Jobs projection must conform to the portable request contract.");

    private sealed class HandlerMustNotBeConstructed : IContinuousJobHandler, IScheduledJobHandler<int>
    {
        public HandlerMustNotBeConstructed() => throw new AssertFailedException("Inventory must not resolve or construct job handlers.");

        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Inventory must not execute a job.");

        public Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, int input, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Inventory must not execute a scheduled job.");
    }

    private sealed class CodecMustNotRun : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new("inventory-passive", 1);
        public byte[] Encode(int value) => throw new AssertFailedException("Inventory must not encode job input.");
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
            => throw new AssertFailedException("Inventory must not decode job input.");
    }
}
