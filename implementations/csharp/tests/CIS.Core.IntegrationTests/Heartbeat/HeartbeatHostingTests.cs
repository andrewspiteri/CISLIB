using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Heartbeat;

[TestClass]
public sealed class HeartbeatHostingTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static readonly HeartbeatRegistrationOptions Schedule = new() { Interval = TimeSpan.FromDays(1) };

    [TestMethod]
    public async Task RequiredHostedInitializationFinishesBeforeTheFirstCallbackWithoutStartupDeadlock()
    {
        var initializer = new Initializer();
        var callbackRelease = Signal();
        var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = new Subscriber(async _ =>
        {
            observed.TrySetResult(initializer.Ready);
            await callbackRelease.Task.ConfigureAwait(false);
            return true;
        });
        await using HostLifetime lifetime = CreateHost(services => services.AddSingleton<IHostedService>(initializer));
        lifetime.Release.Add(() => initializer.Release.TrySetResult());
        lifetime.Release.Add(() => callbackRelease.TrySetResult());
        HeartbeatOwner owner = lifetime.CreateOwner();
        owner.Service.Register(subscriber, Schedule);

        Task start = lifetime.StartAsync();
        await initializer.Entered.Task.WaitAsync(Deadline);
        Assert.IsFalse(start.IsCompleted, "Hosted initialization is held at a controlled boundary.");
        Assert.AreEqual(0, subscriber.Calls, "Creating the singleton and registering before readiness must remain inactive.");
        initializer.Release.TrySetResult();
        await start.WaitAsync(Deadline);
        Assert.IsTrue(await observed.Task.WaitAsync(Deadline), "The actual first callback must observe completed initialization.");
        callbackRelease.TrySetResult();
        await owner.CloseAsync().WaitAsync(Deadline);
        Assert.AreEqual(1, subscriber.Calls);
    }

    [TestMethod]
    public async Task HeartbeatBarrierPrecedesDependentStoppingThroughCallbackAndAwaitedErrorHandling()
    {
        var dependency = new LifecycleProbe();
        var callbackRelease = Signal();
        var handlerEntered = Signal();
        var handlerRelease = Signal();
        bool callbackHadLiveDependencies = false;
        bool handlerHadLiveDependencies = false;
        var subscriber = new Subscriber(async _ =>
        {
            await callbackRelease.Task.ConfigureAwait(false);
            callbackHadLiveDependencies = !dependency.Stopping.Task.IsCompleted;
            throw new InvalidOperationException("Controlled callback failure.");
        }, async _ =>
        {
            handlerEntered.TrySetResult();
            await handlerRelease.Task.ConfigureAwait(false);
            handlerHadLiveDependencies = !dependency.Stopping.Task.IsCompleted;
        });
        await using HostLifetime lifetime = CreateHost(services => services.AddSingleton<IHostedService>(dependency));
        lifetime.Release.Add(() => callbackRelease.TrySetResult());
        lifetime.Release.Add(() => handlerRelease.TrySetResult());
        HeartbeatOwner owner = lifetime.CreateOwner();
        owner.Service.Register(subscriber, Schedule);
        await lifetime.StartAsync().WaitAsync(Deadline);
        await subscriber.Entered.Task.WaitAsync(Deadline);

        Task stop = lifetime.StopAsync();
        Assert.IsFalse(stop.IsCompleted);
        Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
        callbackRelease.TrySetResult();
        await handlerEntered.Task.WaitAsync(Deadline);
        Assert.IsFalse(stop.IsCompleted, "A callback failure does not complete the barrier while its error handler remains active.");
        Assert.IsFalse(dependency.Stopping.Task.IsCompleted, "The dependency's lifecycle stopping phase must await Heartbeat.");
        handlerRelease.TrySetResult();
        await stop.WaitAsync(Deadline);
        await dependency.Stopping.Task.WaitAsync(Deadline);
        Assert.IsTrue(callbackHadLiveDependencies);
        Assert.IsTrue(handlerHadLiveDependencies);
        Assert.AreEqual(1, subscriber.Errors);
        Assert.AreEqual(1, subscriber.Calls);
        Assert.IsFalse(lifetime.Runtime.InfrastructureFailure.IsCompleted, "The observed subscriber exception is not an infrastructure failure.");
    }

    [TestMethod]
    public async Task ActualScopeDisposalWaitsForItsCallbackAndAsyncDependencyDisposalExactlyOnce()
    {
        var callbackRelease = Signal();
        var dependencyDisposalRelease = Signal();
        var facts = new ScopedFacts(callbackRelease);
        await using HostLifetime lifetime = CreateHost(services =>
        {
            services.AddScoped(_ => new ScopedDependency(dependencyDisposalRelease));
            services.AddScoped(provider => new ScopedSubscriber(provider.GetRequiredService<ScopedDependency>(), facts));
        });
        lifetime.Release.Add(() => callbackRelease.TrySetResult());
        lifetime.Release.Add(() => dependencyDisposalRelease.TrySetResult());
        var scope = new HeartbeatScope(lifetime.Runtime, lifetime.Host.Services.GetRequiredService<IServiceScopeFactory>());
        lifetime.AfterDrain.Add(async () => await scope.DisposeAsync().ConfigureAwait(false));
        ScopedDependency dependency = scope.Services.GetRequiredService<ScopedDependency>();
        ScopedSubscriber subscriber = scope.Services.GetRequiredService<ScopedSubscriber>();
        Assert.AreSame(dependency, subscriber.Dependency, "The registered instance must use its actual scope's dependency.");
        scope.Service.Register(subscriber, Schedule);
        await lifetime.StartAsync().WaitAsync(Deadline);
        await facts.Entered.Task.WaitAsync(Deadline);

        Task disposal = scope.DisposeAsync().AsTask();
        Task joinedDisposal = scope.DisposeAsync().AsTask();
        Assert.IsFalse(disposal.IsCompleted);
        Assert.AreEqual(0, dependency.DisposalCalls);
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Service.Register(new Subscriber(_ => Task.FromResult(true)), Schedule));
        dependency.Use();
        callbackRelease.TrySetResult();
        await dependency.DisposalEntered.Task.WaitAsync(Deadline);
        Assert.IsTrue(facts.UsedBeforeReturning, "The dependency must stay usable until the callback's returned task ends.");
        Assert.IsFalse(disposal.IsCompleted, "Scope disposal also owns the dependency's actual asynchronous disposal.");
        Assert.IsFalse(joinedDisposal.IsCompleted);
        Assert.AreEqual(1, dependency.DisposalCalls);
        dependencyDisposalRelease.TrySetResult();
        await Task.WhenAll(disposal, joinedDisposal).WaitAsync(Deadline);
        Assert.IsTrue(dependency.Disposed);
        Assert.AreEqual(1, dependency.DisposalCalls);
        Assert.AreEqual(0, facts.Errors);
        await scope.DisposeAsync().AsTask().WaitAsync(Deadline);
        Assert.AreEqual(1, dependency.DisposalCalls);
    }

    [TestMethod]
    public async Task ShutdownDuringRequiredInitializationNeverInvokesRegisteredWork()
    {
        var initializer = new Initializer();
        var subscriber = new Subscriber(_ => Task.FromResult(true));
        await using HostLifetime lifetime = CreateHost(services => services.AddSingleton<IHostedService>(initializer));
        lifetime.Release.Add(() => initializer.Release.TrySetResult());
        HeartbeatOwner owner = lifetime.CreateOwner();
        owner.Service.Register(subscriber, Schedule);
        Task start = lifetime.StartAsync();
        await initializer.Entered.Task.WaitAsync(Deadline);

        await lifetime.StopAsync().WaitAsync(Deadline);
        Assert.AreEqual(0, subscriber.Calls);
        initializer.Release.TrySetResult();
        await ObserveCanceledStartAsync(start);
        await lifetime.Runtime.StopAsync().WaitAsync(Deadline);
        Assert.AreEqual(0, subscriber.Calls, "A late ApplicationStarted event must not reopen the stopped runtime.");
        Assert.ThrowsExactly<InvalidOperationException>(() => owner.Service.Register(subscriber, Schedule));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task UnsupportedConcurrentOrUnorderedStoppingFailsStartupWithoutCallbacks(bool concurrentStopping)
    {
        var subscriber = new Subscriber(_ => Task.FromResult(true));
        await using HostLifetime lifetime = CreateHost(
            concurrentStopping ? services => services.Configure<HostOptions>(options => options.ServicesStopConcurrently = true) : null,
            addLaterHostedService: !concurrentStopping);
        HeartbeatOwner owner = lifetime.CreateOwner();
        owner.Service.Register(subscriber, Schedule);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.StartAsync()).WaitAsync(Deadline);
        await lifetime.Runtime.StopAsync().WaitAsync(Deadline);
        Assert.AreEqual(0, subscriber.Calls);
    }

    [TestMethod]
    public async Task AlreadyCanceledHostStopBudgetCannotReleaseDependenciesBeforeTheActualDrain()
    {
        var dependency = new LifecycleProbe();
        var callbackRelease = Signal();
        bool dependencyWasLive = false;
        var subscriber = new Subscriber(async _ =>
        {
            await callbackRelease.Task.ConfigureAwait(false);
            dependencyWasLive = !dependency.Stopping.Task.IsCompleted;
            return true;
        });
        await using HostLifetime lifetime = CreateHost(services => services.AddSingleton<IHostedService>(dependency));
        lifetime.Release.Add(() => callbackRelease.TrySetResult());
        HeartbeatOwner owner = lifetime.CreateOwner();
        owner.Service.Register(subscriber, Schedule);
        await lifetime.StartAsync().WaitAsync(Deadline);
        await subscriber.Entered.Task.WaitAsync(Deadline);
        using var canceledBudget = new CancellationTokenSource();
        canceledBudget.Cancel();

        Task stop = lifetime.StopAsync(canceledBudget.Token);
        Assert.IsFalse(stop.IsCompleted, "The adapter must retain the real barrier despite an already-canceled host budget.");
        Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
        callbackRelease.TrySetResult();
        await ObserveCanceledStopAsync(stop);
        await lifetime.Runtime.StopAsync().WaitAsync(Deadline);
        await dependency.Stopping.Task.WaitAsync(Deadline);
        Assert.IsTrue(dependencyWasLive);
        Assert.AreEqual(1, subscriber.Calls);
    }

    private static HostLifetime CreateHost(Action<IServiceCollection>? configure = null, bool addLaterHostedService = false)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.ClearProviders();
        configure?.Invoke(builder.Services);
        builder.Services.AddCisHeartbeat(new HeartbeatOptions { MaxConcurrent = 1 });
        if (addLaterHostedService) { builder.Services.AddSingleton<IHostedService>(new LifecycleProbe()); }
        IHost host = builder.Build();
        return new HostLifetime(host, host.Services.GetRequiredService<HeartbeatRuntime>());
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task ObserveCanceledStartAsync(Task start)
    {
        try { await start.WaitAsync(Deadline); }
        catch (OperationCanceledException) when (start.IsCanceled) { }
    }

    private static async Task ObserveCanceledStopAsync(Task stop)
    {
        try { await stop.WaitAsync(Deadline); }
        catch (OperationCanceledException) when (stop.IsCanceled) { }
    }

    private sealed class HostLifetime(IHost host, HeartbeatRuntime runtime) : IAsyncDisposable
    {
        internal IHost Host { get; } = host;
        internal HeartbeatRuntime Runtime { get; } = runtime;
        internal List<Action> Release { get; } = [];
        internal List<Func<Task>> AfterDrain { get; } = [];
        private Task? start;
        private Task? stop;

        internal Task StartAsync() => start = Host.StartAsync();
        internal Task StopAsync(CancellationToken cancellationToken = default) => stop = Host.StopAsync(cancellationToken);

        internal HeartbeatOwner CreateOwner()
        {
            HeartbeatOwner owner = Runtime.CreateOwner();
            AfterDrain.Add(async () => await owner.DisposeAsync().ConfigureAwait(false));
            return owner;
        }

        public async ValueTask DisposeAsync()
        {
            // Establish the cutoff before releasing a held initializer or any user phase.
            Task drain = Runtime.StopAsync();
            foreach (Action release in Release) { release(); }
            if (start is not null)
            {
                try { await start.WaitAsync(Deadline); }
                catch (Exception) when (start.IsCompleted) { /* The test body observes expected startup failures. */ }
            }
            await drain.WaitAsync(Deadline);
            try
            {
                foreach (Func<Task> afterDrain in AfterDrain) { await afterDrain().WaitAsync(Deadline); }
                await ObserveCanceledStopAsync(stop ?? Host.StopAsync());
            }
            finally
            {
                // IHost exposes IDisposable; its actual implementation supports asynchronous root disposal.
                if (Host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync().AsTask().WaitAsync(Deadline); }
                else { Host.Dispose(); }
            }
        }
    }

    private sealed class Initializer : IHostedService
    {
        internal readonly TaskCompletionSource Entered = Signal();
        internal readonly TaskCompletionSource Release = Signal();
        private int ready;
        internal bool Ready => Volatile.Read(ref ready) != 0;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            Volatile.Write(ref ready, 1);
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class LifecycleProbe : IHostedLifecycleService
    {
        internal readonly TaskCompletionSource Stopping = Signal();
        public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken cancellationToken) { Stopping.TrySetResult(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Subscriber(Func<CancellationToken, Task<bool>> callback, Func<Exception, Task>? handler = null) : IHeartbeatSubscriber
    {
        internal readonly TaskCompletionSource Entered = Signal();
        private int calls, errors;
        internal int Calls => Volatile.Read(ref calls);
        internal int Errors => Volatile.Read(ref errors);

        public Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Entered.TrySetResult();
            return callback(cancellationToken);
        }
        public Task OnErrorAsync(Exception exception)
        {
            Interlocked.Increment(ref errors);
            return handler?.Invoke(exception) ?? Task.CompletedTask;
        }
    }

    private sealed class ScopedFacts(TaskCompletionSource callbackRelease)
    {
        internal readonly TaskCompletionSource Entered = Signal();
        internal Task CallbackRelease => callbackRelease.Task;
        internal bool UsedBeforeReturning;
        internal int Errors;
    }

    private sealed class ScopedSubscriber(ScopedDependency dependency, ScopedFacts facts) : IHeartbeatSubscriber
    {
        internal ScopedDependency Dependency => dependency;
        public async Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
        {
            dependency.Use();
            facts.Entered.TrySetResult();
            await facts.CallbackRelease.ConfigureAwait(false);
            dependency.Use();
            facts.UsedBeforeReturning = true;
            return true;
        }
        public Task OnErrorAsync(Exception exception) { Interlocked.Increment(ref facts.Errors); return Task.CompletedTask; }
    }

    private sealed class ScopedDependency(TaskCompletionSource disposalRelease) : IAsyncDisposable
    {
        internal readonly TaskCompletionSource DisposalEntered = Signal();
        private int disposalCalls, disposed;
        internal int DisposalCalls => Volatile.Read(ref disposalCalls);
        internal bool Disposed => Volatile.Read(ref disposed) != 0;
        internal void Use() => ObjectDisposedException.ThrowIf(DisposalCalls != 0, this);

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposalCalls);
            DisposalEntered.TrySetResult();
            await disposalRelease.Task.ConfigureAwait(false);
            Volatile.Write(ref disposed, 1);
        }
    }
}
