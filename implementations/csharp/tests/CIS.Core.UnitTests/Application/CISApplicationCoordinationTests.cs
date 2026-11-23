using CIS.Coordination;
using CIS.Coordination.Hosting;
using CIS.Hosting;
using CIS.Jobs;
using CIS.Jobs.Hosting;
using CIS.Tests.UnitTests.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Application;

[TestClass]
public sealed class CISApplicationCoordinationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisposalBeforeBuildDrainsAlreadyAdmittedBorrowedJobsBeforeDependencyCutoff(bool web)
    {
        await using var fixture = new JobRuntimeTestSupport();
        fixture.Control.HoldCleanup = true;
        fixture.Control.Callback = async (_, _) =>
        {
            await fixture.Control.WorkRelease.Task;
            return new(ContinuousIterationOutcome.Idle);
        };
        fixture.EnqueueGrant();
        await fixture.Runner.StartAsync("wallet-service", "wallet.compaction", "partition");
        await fixture.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        CoordinationHostLifetime? lifetime = null;
        int dependencyCutoffs = 0;

        void Configure(CISApplicationBuilder builder)
        {
            builder.Services.AddSingleton<IJobCatalog>(fixture.Catalog);
            builder.Services.AddSingleton(fixture.Runtime);
            builder.Services.AddCisJobs(fixture.Configuration(0));
            lifetime = ((CoordinationComposition)builder.Services.Single(item =>
                item.ServiceType == typeof(CoordinationComposition)).ImplementationInstance!).Lifetime;
        }

        CISApplication application = web ? CISApplication.CreateWeb([], Configure, _ => Assert.Fail("No Build was requested."))
            : new CISApplication(Configure);
        Assert.IsNotNull(lifetime);
        using CancellationTokenRegistration cutoff = lifetime.DependencyStopping.Register(() => Interlocked.Increment(ref dependencyCutoffs));
        Task? disposal = null;
        try
        {
            disposal = application.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted);
            Assert.AreEqual(0, dependencyCutoffs);
            Assert.AreEqual(1, fixture.Runtime.CaptureStatus().ActiveContinuousInvocations);
            fixture.Control.WorkRelease.TrySetResult();
            await fixture.Control.ScopeCleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(disposal.IsCompleted, "Returning from the callback does not settle its owned scope.");
            Assert.AreEqual(0, dependencyCutoffs);
            fixture.Control.ScopeCleanupRelease.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, dependencyCutoffs);
            Assert.AreEqual(0, fixture.Runtime.CaptureStatus().DutyReservations);
            Assert.AreEqual(fixture.Control.CreatedScopes, fixture.Control.DisposedScopes);
        }
        finally
        {
            fixture.Control.WorkRelease.TrySetResult();
            fixture.Control.ScopeCleanupRelease.TrySetResult();
            await (disposal ?? application.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
