using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.RestClient.Resilience;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

internal static class RuntimeTestSupport
{
    internal static WebApplication Root(RestClientOptions options, TimeProvider clock, params RestRetryPolicy[] policies)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.Host.UseDefaultServiceProvider(settings => { settings.ValidateScopes = true; settings.ValidateOnBuild = true; });
        builder.Logging.ClearProviders();
        IServiceCollection services = builder.Services;
        services.AddSingleton(clock);
        services.AddCisRestClientResilience(policies);
        services.AddCisRestClient(options);
        return builder.Build();
    }

    internal static async Task SettleAsync(Task? task, Exception? expected = null)
    {
        if (task is null) { return; }
        try { await task.WaitAsync(RestHttpTestSupport.Deadline); }
        catch (OperationCanceledException) when (task.IsCanceled) { }
        catch (Exception error) when (ReferenceEquals(error, expected)) { }
    }

    internal static async Task<ResilienceClock.Wait> RetryWaitAsync(ResilienceClock clock, TimeSpan delay)
    {
        for (int index = 0; index < 8; index++)
        {
            ResilienceClock.Wait wait = await clock.NextAsync();
            if (wait.Delay == delay) { return wait; }
        }
        throw new AssertFailedException("The actual selected retry wait was not observed among the bounded operation timers.");
    }
}

internal sealed class RuntimeHeldInput : MemoryStream
{
    private readonly object gate = new();
    private Task? disposal;
    internal RuntimeHeldInput() : base([1, 2, 3]) { }
    internal TaskCompletionSource Cleaning { get; } = RestHttpTestSupport.Signal();
    internal TaskCompletionSource Release { get; } = RestHttpTestSupport.Signal();
    internal int Disposals { get; private set; }
    public override ValueTask DisposeAsync()
    {
        lock (gate) { return new(disposal ??= DisposeCoreAsync()); }
    }
    private async Task DisposeCoreAsync()
    {
        Disposals++; Cleaning.TrySetResult(); await Release.Task;
        await base.DisposeAsync();
    }
}
