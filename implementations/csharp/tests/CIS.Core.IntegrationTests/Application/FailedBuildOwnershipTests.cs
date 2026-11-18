using CIS.Hosting;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Application;

[TestClass]
public sealed class FailedBuildOwnershipTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedHostResolutionOwnsTheActualProviderAndJoinsAsyncCleanupWithoutDisposingBorrowedInstances(bool web)
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var owned = new AsyncDependency();
        var borrowed = new BorrowedDependency();
        var original = new InvalidOperationException("The actual host lifetime factory failed.");
        int creations = 0;
        int maps = 0;

        void Configure(CISApplicationBuilder builder)
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton(_ =>
            {
                Interlocked.Increment(ref creations);
                return owned;
            });
            builder.Services.AddSingleton(borrowed);
            builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime>(root =>
            {
                Assert.AreSame(owned, root.GetRequiredService<AsyncDependency>());
                Assert.AreSame(borrowed, root.GetRequiredService<BorrowedDependency>());
                throw original;
            }));
        }

        CISApplication app = web
            ? CISApplication.CreateWeb([], Configure, _ =>
            {
                maps++;
            })
            : new CISApplication(Configure);
        Task startup = app.StartAsync();
        Task? disposal = null;
        try
        {
            Exception failure = await ApplicationTestSupport.FailureAsync(startup);
            Assert.AreSame(original, failure);
            Assert.IsTrue(startup.IsFaulted);
            Assert.AreSame(original, app.Failure);
            Assert.AreEqual(1, creations, "The dependency belongs to the one real provider used for host resolution.");
            Assert.AreEqual(0, maps, "Web mapping cannot run before its host Build returns.");
            Assert.Throws<InvalidOperationException>(() => _ = app.Services);

            disposal = app.DisposeAsync().AsTask();
            await owned.Entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreSame(disposal, app.DisposeAsync().AsTask());
            Assert.IsFalse(disposal.IsCompleted, "A failed Build still owns actual asynchronous provider cleanup.");
            Assert.AreEqual(1, owned.Calls);
            Assert.AreEqual(0, owned.Completed);
            Assert.AreEqual(0, borrowed.Disposals);

            owned.Release.TrySetResult();
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(1, owned.Completed);
            Assert.AreEqual(1, owned.Calls);
            Assert.AreEqual(0, borrowed.Disposals, "Instance descriptors retain caller disposal ownership.");
            Assert.AreSame(startup, app.StartAsync());
            Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(app.StartAsync()));
            Assert.AreSame(original, app.Failure);
        }
        finally
        {
            owned.Release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(startup);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedBuildCleanupFaultDoesNotReplaceTheOriginalStartupFailure(bool web)
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        var cleanupFailure = new IOException("The owned dependency cleanup failed.");
        var owned = new AsyncDependency(cleanupFailure);
        owned.Release.TrySetResult();
        var borrowed = new BorrowedDependency();
        var original = new InvalidOperationException("The actual host lifetime factory failed.");

        void Configure(CISApplicationBuilder builder)
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton(_ => owned);
            builder.Services.AddSingleton(borrowed);
            builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime>(root =>
            {
                _ = root.GetRequiredService<AsyncDependency>();
                _ = root.GetRequiredService<BorrowedDependency>();
                throw original;
            }));
        }

        CISApplication app = web
            ? CISApplication.CreateWeb([], Configure, _ =>
            {
            })
            : new CISApplication(Configure);
        Task startup = app.StartAsync();
        Task? disposal = null;
        try
        {
            Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(startup));
            disposal = app.DisposeAsync().AsTask();
            Exception cleanup = await ApplicationTestSupport.FailureAsync(disposal);
            Assert.AreSame(cleanupFailure, cleanup);
            Assert.IsTrue(disposal.IsFaulted);
            Assert.AreEqual(1, owned.Calls);
            Assert.AreEqual(1, owned.Completed);
            Assert.AreEqual(0, borrowed.Disposals);
            Assert.AreSame(original, app.Failure);
            Assert.AreSame(startup, app.StartAsync());
            Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(startup));
            Assert.AreSame(disposal, app.DisposeAsync().AsTask());
        }
        finally
        {
            owned.Release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(startup);
            await ApplicationTestSupport.SettleAsync(disposal ?? app.DisposeAsync().AsTask());
        }
    }

    private sealed class AsyncDependency(Exception? failure = null) : IAsyncDisposable
    {
        private int calls;
        private int completed;

        internal TaskCompletionSource Entered { get; } = ApplicationTestSupport.Signal();
        internal TaskCompletionSource Release { get; } = ApplicationTestSupport.Signal();
        internal int Calls => Volatile.Read(ref calls);
        internal int Completed => Volatile.Read(ref completed);

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref calls);
            Entered.TrySetResult();
            await Release.Task;
            Interlocked.Increment(ref completed);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    private sealed class BorrowedDependency : IAsyncDisposable
    {
        private int disposals;

        internal int Disposals => Volatile.Read(ref disposals);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposals);
            return ValueTask.CompletedTask;
        }
    }
}
