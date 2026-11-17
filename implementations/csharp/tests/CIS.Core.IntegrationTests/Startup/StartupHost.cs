using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using CIS.Startup;
using CIS.Startup.Hosting;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Healthcheck.Reporting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Startup;

// An actual WebApplication and its explicit resource owners; no alternate container or server.
internal sealed class StartupHost : IAsyncDisposable
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    internal static readonly HeartbeatRegistrationOptions Schedule = new() { Interval = TimeSpan.FromDays(1) };
    internal WebApplication App { get; }
    internal HealthcheckService Health { get; }
    internal HeartbeatRuntime Runtime { get; }
    internal HeartbeatOwner Owner { get; }
    internal List<Action> Release { get; } = [];
    private readonly HealthcheckOptions options;
    private HealthPipeServer? pipe;
    private HttpClient? client;
    private Task? start, stop;
    private int businessCalls;
    internal int BusinessCalls => Volatile.Read(ref businessCalls);

    internal StartupHost(Action<IServiceCollection>? configure = null, Action<IServiceCollection>? afterStartup = null,
        Action<WebApplication>? configureApp = null)
    {
        options = new() { PipeEnabled = true, PipeName = TransportTestSupport.NewPipeName() };
        Health = new(options);
        Runtime = new(new HeartbeatOptions { MaxConcurrent = 1 });
        Owner = Runtime.CreateOwner();
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { Args = [], EnvironmentName = Environments.Production });
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(provider =>
        { provider.ValidateScopes = true; provider.ValidateOnBuild = true; });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(Health);
        builder.Services.AddSingleton(Runtime);
        configure?.Invoke(builder.Services);
        builder.Services.AddCisHeartbeat();
        builder.Services.AddCisStartup();
        afterStartup?.Invoke(builder.Services);
        App = builder.Build();
        configureApp?.Invoke(App);
        App.UseCisHealthcheck(Health, options);
        App.Use(async (context, next) =>
        {
            if (!Health.CaptureSnapshot().Ready)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            await next(context);
        });
        App.Run(async context =>
        {
            Interlocked.Increment(ref businessCalls);
            await context.Response.WriteAsync("business");
        });
    }

    internal Task StartAsync(CancellationToken cancellation = default) => start = App.StartAsync(cancellation);
    internal Task StopAsync(CancellationToken cancellation = default) => stop = App.StopAsync(cancellation);

    internal async Task AssertStatusAsync(string path, HttpStatusCode expected)
    {
        string address = App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        client ??= new HttpClient { BaseAddress = new Uri(address), Timeout = Deadline };
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.AreEqual(expected, response.StatusCode, path);
    }

    internal async Task StartPipeAsync()
    {
        pipe = new(Health, options);
        await TransportTestSupport.StartServerAsync(pipe, options.PipeName!);
    }

    internal async Task AssertProbeAsync(int expectedExit, bool ready, bool startupCompleted)
    {
        string probe = Path.Combine(AppContext.BaseDirectory, "healthcheck-probe", "CisHealthProbe.dll");
        Assert.IsTrue(File.Exists(probe), "The existing real probe must be built and copied by the integration project.");
        ProcessResult result = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", probe,
                "--pipe", options.PipeName!, "--view", "readiness", "--timeout-ms", "1000"],
            AppContext.BaseDirectory, Deadline, outputEncoding: new UTF8Encoding(false, true));
        Assert.IsTrue(result.OutputDrained);
        Assert.IsFalse(result.CaptureTruncated);
        Assert.AreEqual(expectedExit, result.ExitCode, result.StandardError + result.StandardOutput);
        Assert.AreEqual("", result.StandardError);
        JsonObject snapshot = JsonNode.Parse(result.StandardOutput)!.AsObject();
        Assert.AreEqual(ready, (bool?)snapshot["ready"]);
        Assert.AreEqual(startupCompleted, (bool?)snapshot["application"]!["startupCompleted"]);
    }

    public async ValueTask DisposeAsync()
    {
        // Close admission before releasing any held user phase. A timeout is not permission to dispose the root.
        Task runtimeDrain = Runtime.StopAsync();
        Task hostDrain = stop ?? App.StopAsync(CancellationToken.None);
        foreach (Action release in Release) { release(); }
        if (start is not null)
        {
            try { await start.WaitAsync(Deadline); }
            catch (Exception) when (start.IsCompleted) { /* Expected startup outcomes are asserted by each test. */ }
        }
        await runtimeDrain.WaitAsync(Deadline);
        try { await hostDrain.WaitAsync(Deadline); }
        catch (OperationCanceledException) when (hostDrain.IsCanceled) { }
        await Owner.DisposeAsync().AsTask().WaitAsync(Deadline);
        if (pipe is not null) { await new TransportTestSupport.PipeServerCleanup(pipe).DisposeAsync(); }
        client?.Dispose();
        await App.DisposeAsync().AsTask().WaitAsync(Deadline);
        await Health.DisposeAsync().AsTask().WaitAsync(Deadline);
        await Runtime.DisposeAsync().AsTask().WaitAsync(Deadline);
    }

    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class StartupParticipant(Func<CancellationToken, Task>? action = null) : IStartable
{
    private int calls;
    internal int Calls => Volatile.Read(ref calls);
    public Task StartAsync(CancellationToken cancellationToken)
    { Interlocked.Increment(ref calls); return action?.Invoke(cancellationToken) ?? Task.CompletedTask; }
}

internal sealed class StartupSubscriber(Func<CancellationToken, Task<bool>>? callback = null,
    Func<Exception, Task>? handler = null) : IHeartbeatSubscriber
{
    internal TaskCompletionSource Entered { get; } = StartupHost.Signal();
    private int calls, errors;
    internal int Calls => Volatile.Read(ref calls);
    internal int Errors => Volatile.Read(ref errors);
    public Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
    { Interlocked.Increment(ref calls); Entered.TrySetResult(); return callback?.Invoke(cancellationToken) ?? Task.FromResult(true); }
    public Task OnErrorAsync(Exception exception)
    { Interlocked.Increment(ref errors); return handler?.Invoke(exception) ?? Task.CompletedTask; }
}

internal sealed class StartupDependency : IHostedLifecycleService, IAsyncDisposable
{
    internal TaskCompletionSource Stopping { get; } = StartupHost.Signal();
    private int disposals;
    internal int Disposals => Volatile.Read(ref disposals);
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) { Stopping.TrySetResult(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() { Interlocked.Increment(ref disposals); return ValueTask.CompletedTask; }
}
