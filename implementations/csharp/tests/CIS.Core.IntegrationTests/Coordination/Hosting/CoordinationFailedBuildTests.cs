using CIS.Coordination;
using CIS.Coordination.Hosting;
using CIS.Hosting;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Coordination.Hosting;

[TestClass]
public sealed class CoordinationFailedBuildTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedNativeBuildJoinsAnAlreadyRunningAuthorityCallBeforeDisposingItsActualProviderDependencies(bool web)
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        var physical = new HeldProvider();
        await using var runtime = new CoordinationRuntime(physical, new());
        await runtime.StartAsync();
        Task<ManagedCoordinationResult> acquisition = runtime.AcquireAuthorizedAsync(new(
            new("Development", "build-test", "cleanup", "partition"), "build-test", "instance-a", Guid.NewGuid()));
        await physical.Entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
        var dependency = new Dependency();
        var original = new InvalidOperationException("The actual native lifetime construction failed.");

        void Configure(CISApplicationBuilder builder)
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton(runtime);
            builder.Services.AddScoped<ICoordinationService>(_ => throw new AssertFailedException("A failed Build cannot acquire business scopes."));
            builder.Services.AddCisCoordination();
            builder.Services.AddSingleton(_ => dependency);
            builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime>(root =>
            {
                Assert.AreSame(dependency, root.GetRequiredService<Dependency>());
                throw original;
            }));
        }

        CISApplication application = web ? CISApplication.CreateWeb([], Configure, _ => Assert.Fail("Build failed."))
            : new CISApplication(Configure);
        Task? disposal = null;
        try
        {
            Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(application.StartAsync()));
            disposal = application.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            physical.Returned.TrySetResult(new(CoordinationAcquireOutcome.Unavailable));
            await acquisition.WaitAsync(ApplicationTestSupport.Deadline);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(1, dependency.Disposals);
            Assert.AreSame(original, application.Failure);
        }
        finally
        {
            physical.Returned.TrySetResult(new(CoordinationAcquireOutcome.Unavailable));
            await ApplicationTestSupport.SettleAsync(acquisition);
            await (disposal ?? application.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    private sealed class Dependency : IAsyncDisposable
    {
        internal int Disposals;
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposals);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HeldProvider : ICoordinationProvider
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<CoordinationAcquireResult> Returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid AuthorityId { get; } = Guid.NewGuid();
        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CoordinationAcquireResult> AcquireAsync(CoordinationAcquireRequest request, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return Returned.Task;
        }
        public Task<CoordinationMutationResult> RenewAsync(CoordinationGrant grant, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Unavailable));
        public Task<CoordinationMutationResult> ReleaseAsync(CoordinationGrant grant, CancellationToken cancellationToken = default)
            => Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Unavailable));
        public Task<CoordinationObservation> ResolveAsync(CoordinationAcquisition acquisition, CancellationToken cancellationToken = default)
            => Task.FromResult(new CoordinationObservation(CoordinationObservationOutcome.NotObserved));
    }
}
