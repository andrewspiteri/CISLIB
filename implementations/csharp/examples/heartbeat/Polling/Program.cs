using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Healthcheck;
using CIS.Heartbeat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddScoped<PollingDependency>();
builder.Services.AddScoped<Poller>();
builder.Services.AddCisHeartbeat(new HeartbeatOptions { MaxConcurrent = 2 });
IHost host = builder.Build();
HeartbeatRuntime? runtime = null;
HealthcheckService? health = null;
HeartbeatScope? scope = null;
try
{
    runtime = host.Services.GetRequiredService<HeartbeatRuntime>();
    health = new HealthcheckService(new HealthcheckOptions { DetailsEnabled = true });
    scope = new HeartbeatScope(runtime, host.Services.GetRequiredService<IServiceScopeFactory>());
    Poller poller = scope.Services.GetRequiredService<Poller>();
    scope.Service.Register(poller, new HeartbeatRegistrationOptions { Interval = TimeSpan.FromMilliseconds(20), Name = "Polling example" },
        new HeartbeatHealthcheckOptions
        {
            MonitorFactory = health,
            ConsecutiveFailures = new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 2 }
        });

    await host.StartAsync();
    await poller.Completed.WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine($"observed {poller.Calls} calls; exception handler ran {poller.Errors} time(s)");
    Console.WriteLine($"health monitors before stop: {health.CaptureSnapshot().Monitors.Count}");
}
finally
{
    // Once an engine exists, its global barrier must succeed before destructive
    // cleanup begins, including startup/registration failure paths. A failed
    // barrier deliberately leaves its protected dependencies usable.
    if (runtime is not null) { await runtime.StopAsync(); }

    // A later host/scope/monitor disposal error must still allow the remaining
    // cleanup now that actual Heartbeat work is proved settled.
    try { await host.StopAsync(CancellationToken.None); }
    finally
    {
        try
        {
            if (scope is not null) { await scope.DisposeAsync(); }
        }
        finally
        {
            try
            {
                if (health is not null) { await health.DisposeAsync(); }
            }
            finally
            {
                if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync(); }
                else { host.Dispose(); }
            }
        }
    }
}
Console.WriteLine("heartbeat drained; scoped dependency disposed safely");

sealed class PollingDependency : IAsyncDisposable
{
    public bool Disposed { get; private set; }
    public async Task ReadAsync()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        await Task.Yield();
        ObjectDisposedException.ThrowIf(Disposed, this);
    }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

sealed class Poller(PollingDependency dependency) : IHeartbeatSubscriber
{
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completed => completion.Task;
    public int Calls { get; private set; }
    public int Errors { get; private set; }
    public async Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
    {
        int call = ++Calls;
        await dependency.ReadAsync();
        if (call == 1) { Console.WriteLine("negative result (no exception handler)"); return false; }
        if (call == 2) { throw new InvalidOperationException("temporary polling dependency failure"); }
        Console.WriteLine("refresh succeeded");
        completion.TrySetResult();
        return true;
    }
    public async Task OnErrorAsync(Exception exception)
    {
        Errors++;
        await dependency.ReadAsync();
        Console.WriteLine($"awaited recovery: {exception.Message}");
    }
}
