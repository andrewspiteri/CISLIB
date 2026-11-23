using System.Diagnostics.Metrics;
using CIS.Coordination;
using CIS.Coordination.Hosting;
using CIS.Jobs;
using CIS.Jobs.Hosting;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class CoordinationDefinitionTests
{
    [TestMethod]
    public async Task SevenDefinitionsHaveFixedSchemasAndNoIdentityOrPerResourceDimension()
    {
        using var meter = new Meter("CoordinationDefinitionTests");
        await using var metrics = new MetricsRuntime(meter);
        await using var coordination = new CoordinationRuntime(new NeverProvider(), new());
        await using var jobs = new JobRuntime(new JobCatalog([], []), new NeverScope());
        await using var owner = new CoordinationInstrumentation(new CoordinationComposition(), coordination);
        await using var jobOwner = new JobInstrumentation(new JobsComposition(), jobs);
        owner.AttachMetrics(metrics);
        jobOwner.AttachMetrics(metrics);
        Assert.AreEqual(7, metrics.Definitions.Count);
        Assert.AreEqual(21, metrics.ReservedExportedNames.Count);
        foreach (MetricRegistration registration in metrics.Definitions)
        {
            Assert.IsTrue(registration.Definition.Name.StartsWith("cis.coordination.", StringComparison.Ordinal)
                || registration.Definition.Name.StartsWith("cis.jobs.", StringComparison.Ordinal));
            Assert.IsTrue(registration.Definition.AllowedDimensionKeys.All(key => key is "operation" or "outcome" or "mode" or "reason"));
            Assert.AreEqual(registration.Kind == MetricKind.Timer ? MetricUnit.Seconds : MetricUnit.Count, registration.Definition.Unit);
        }

        Assert.AreEqual("cis_coordination_operation_duration_seconds", metrics.Definitions.Single(item => item.Definition.Name == "cis.coordination.operation.duration").ExportedName);
        Assert.AreEqual("cis_jobs_invocation_duration_seconds", metrics.Definitions.Single(item => item.Definition.Name == "cis.jobs.invocation.duration").ExportedName);
    }

    private sealed class NeverScope : IJobExecutionScopeFactory
    {
        public ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Definition attachment must not create execution scopes.");
    }

    private sealed class NeverProvider : ICoordinationProvider
    {
        public Guid AuthorityId { get; } = Guid.NewGuid();
        public Task ValidateAsync(CancellationToken cancellationToken = default) => throw new AssertFailedException("Definition attachment must be passive.");
        public Task<CoordinationAcquireResult> AcquireAsync(CoordinationAcquireRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CoordinationMutationResult> RenewAsync(CoordinationGrant grant, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CoordinationMutationResult> ReleaseAsync(CoordinationGrant grant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CoordinationObservation> ResolveAsync(CoordinationAcquisition acquisition, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
