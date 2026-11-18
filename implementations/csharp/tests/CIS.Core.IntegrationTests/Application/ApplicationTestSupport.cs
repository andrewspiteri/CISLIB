using System.Net;
using System.Reflection;
using System.Text.Json;
using CIS.Configuration;
using CIS.Hosting;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.RestClient;
using CIS.Startup;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Application;

internal static class ApplicationTestSupport
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    internal static string HostName => typeof(ApplicationTestSupport).Assembly.GetName().Name!;
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static TypeManagerOptions Inventory(TemporaryDirectory directory)
    {
        // The facade performs the sole real preparation. Do not prepare a second
        // manager, filter its catalog or substitute a test ITypeManager.
        Assembly assembly = typeof(ApplicationTestSupport).Assembly;
        File.Copy(assembly.Location, Path.Combine(directory.Path, "Host.dll"));
        string manifest = Path.Combine(directory.Path, "host.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = assembly.FullName,
            assemblies = new[] { new { assemblyName = assembly.FullName, relativePath = "Host.dll" } }
        }));
        return new(assembly)
        { AssemblyManifestPath = manifest, PrecomputedContracts = [typeof(IConfigurationObject)] };
    }

    internal static void Configure(CISApplicationBuilder builder, TypeManagerOptions options)
    {
        builder.TypeManager = options;
        builder.EnableOptionalIntegrations = false;
        builder.LoggingEnabled = false;
        builder.Host.Logging.ClearProviders();
        if (builder.WebHost is { } web) { web.UseKestrel().UseUrls("http://127.0.0.1:0"); }
    }

    internal static HttpClient Client(CISApplication app)
    {
        string address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new() { BaseAddress = new Uri(address), Timeout = Deadline };
    }

    internal static async Task StatusAsync(HttpClient client, string path, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.AreEqual(expected, response.StatusCode, path);
    }

    internal static async Task<Exception> FailureAsync(Task task)
    {
        try { await task.WaitAsync(Deadline); }
        catch (Exception error) when (task.IsCompleted) { return error; }
        throw new AssertFailedException("The actual owned operation unexpectedly succeeded.");
    }

    internal static async Task SettleAsync(Task? task)
    {
        if (task is null) { return; }
        try { await task.WaitAsync(Deadline); }
        catch (Exception) when (task.IsCompleted) { /* The individual test asserts its retained outcome. */ }
    }

    internal sealed class Participant(Func<CancellationToken, Task> start) : IStartable
    {
        internal int Calls { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken)
        { Calls++; return start(cancellationToken); }
    }

    internal sealed class Dependency : IHostedLifecycleService, IAsyncDisposable
    {
        private int disposed;
        internal TaskCompletionSource Stopping { get; } = Signal();
        internal int Disposals => Volatile.Read(ref disposed);
        internal void Use() => Assert.AreEqual(0, Disposals, "Actual cleanup still owns this dependency.");
        public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken cancellationToken)
        { Stopping.TrySetResult(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            // Microsoft DI may dispose same-reference aliases; destructive work is idempotent.
            Interlocked.CompareExchange(ref disposed, 1, 0);
            return ValueTask.CompletedTask;
        }
    }

    internal static void RegisterDependency(IServiceCollection services, Dependency dependency)
    {
        services.AddSingleton(_ => dependency);
        services.AddSingleton<IHostedService>(root => root.GetRequiredService<Dependency>());
    }

    internal sealed class HeldResponseCleanup(Dependency dependency) : IRestExchangeHandler
    {
        private int disposals;
        internal TaskCompletionSource Entered { get; } = Signal();
        internal TaskCompletionSource Release { get; } = Signal();
        internal TaskCompletionSource CancellationObserved { get; } = Signal();
        internal int Disposals => Volatile.Read(ref disposals);
        public async ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken token)
        {
            CancellationTokenRegistration registration = token.UnsafeRegister(
                _ => CancellationObserved.TrySetResult(), null);
            try { return new HeldResponse(await next(request, token), this, registration); }
            catch { registration.Dispose(); throw; }
        }

        private void UseDependency() => dependency.Use();

        private sealed class HeldResponse(IRestResponse inner, HeldResponseCleanup owner,
            CancellationTokenRegistration registration) : IRestResponse
        {
            public int HttpStatusCode => inner.HttpStatusCode;
            public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers => inner.Headers;
            public Stream Body => inner.Body;
            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.disposals);
                owner.Entered.TrySetResult();
                try { await owner.Release.Task; owner.UseDependency(); }
                finally { registration.Dispose(); }
                // The actual exchange pipeline separately owns its next response.
            }
        }
    }
    internal static CoreLogRuntime LoggingRuntime(ILogWriter writer)
        => QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None,
            QueueCapacity = 4, ShutdownTimeoutMilliseconds = 50
        }, writers: [new LogWriterRegistration("application-held", writer)]);

    internal sealed class HeldLogWriter(Dependency dependency) : ILogWriter
    {
        private int disposed, usable;
        internal TaskCompletionSource Entered { get; } = Signal();
        internal TaskCompletionSource Release { get; } = Signal();
        internal bool UsedDependency => Volatile.Read(ref usable) != 0;
        internal int Disposals => Volatile.Read(ref disposed);
        public async ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task; // Deliberately owns real work after bounded stop cancellation.
            dependency.Use();
            Assert.AreEqual(0, Disposals, "DI must not dispose an active borrowed writer.");
            Volatile.Write(ref usable, 1);
        }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        {
            Interlocked.CompareExchange(ref disposed, 1, 0);
            return ValueTask.CompletedTask;
        }
    }

}
