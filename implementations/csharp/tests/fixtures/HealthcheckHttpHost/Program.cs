using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.HealthcheckHttpHost;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        string mode = args.Length == 0 ? "normal" : args[0];
        if (mode == "pipe-pin-release")
        {
            try { await ObserveWindowsPipePinRelease(); }
            catch (Exception error) when (IsAttributedPipeResourceFailure(error))
            {
                Write(new
                {
                    status = "resource-error",
                    diagnostic = "pipe-pin-release",
                    error = error.GetType().Name,
                    pid = Environment.ProcessId,
                    runtime = RuntimeInformation.FrameworkDescription
                });
            }
            return;
        }
        var options = new HealthcheckOptions
        {
            DetailsEnabled = mode != "details-disabled",
            MaxReportBytes = mode == "overflow" ? 1024 : 4194304,
            PipeEnabled = mode is "parity" or "pipe-live",
            PipeName = mode is "parity" or "pipe-live" ? "cis-fixture-" + Guid.NewGuid().ToString("N") : null,
            // Parity checks complete payload equality within the functional test's
            // 15-second limit; its pipe exchange is not a default-deadline test.
            PipeRequestTimeoutMs = mode == "parity" ? 15_000 : 1000,
        };
        await using var service = new HealthcheckService(options, new HealthApplicationIdentity { Name = "transport-fixture", InstanceId = Guid.Parse("02fc362c-a583-48c3-8f17-2e5743312e54") });
        var monitors = new List<IConditionMonitor>();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { Name = "critical", IsCritical = true, InitiallyFlagged = mode == "unhealthy" });
        monitors.Add(monitor);
        if (mode == "overflow")
        {
            for (int i = 0; i < 32; i++) { monitors.Add(service.CreateCondition(new ConditionMonitorOptions { Name = new string('x', 128), IsCritical = false, InitiallyFlagged = false })); }
        }
        if (mode != "not-ready") { service.MarkStartupCompleted(); }
        if (mode == "stopping") { service.BeginStopping(); }
        var source = new CountingSource(service, monitor, mode);
        await using var pipeServer = new HealthPipeServer(source, options);
        await pipeServer.StartAsync();
        int authorizationCalls = 0, businessCalls = 0;
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using WebApplication app = builder.Build();
        app.UseCisHealthcheck(source, options, async context =>
        {
            Interlocked.Increment(ref authorizationCalls);
            string? selected = context.Request.Headers["X-Auth"].FirstOrDefault();
            if (selected == "throw") { throw new InvalidOperationException("secret-authorization-error"); }
            if (selected == "cancel") { throw new OperationCanceledException("secret-cancellation"); }
            if (selected == "block") { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            return selected != "deny";
        });
        app.Run(async context =>
        {
            Interlocked.Increment(ref businessCalls);
            await context.Response.WriteAsync("business:" + context.Request.Path);
        });
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Write(new { status = "ready", address, pipeName = options.PipeName, pid = Environment.ProcessId, runtime = RuntimeInformation.FrameworkDescription });
        while (await Console.In.ReadLineAsync() is { } command)
        {
            if (command == "exit") { break; }
            switch (command)
            {
                case "flag": monitor.Flag(); break;
                case "clear": monitor.Clear(); break;
                case "startup": service.MarkStartupCompleted(); break;
                case "begin-stopping": service.BeginStopping(); break;
                case "state": break;
                default: throw new ArgumentException("Unknown fixture command.");
            }
            Write(new { captures = source.Captures, authorizationCalls = Volatile.Read(ref authorizationCalls), businessCalls = Volatile.Read(ref businessCalls), revision = service.CaptureSnapshot().Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        await app.StopAsync();
        GC.KeepAlive(monitors);
    }

    private static void Write(object value) { Console.WriteLine(JsonSerializer.Serialize(value)); }

    // Only this diagnostic normalizes an expected public resource failure. An
    // unrelated BCL/runtime/observer failure must retain its abnormal termination.
    private static bool IsAttributedPipeResourceFailure(Exception error)
    {
        if (error is not Win32Exception and not ArgumentException and not InvalidOperationException and not IOException) { return false; }
        var method = new StackTrace(error, fNeedFileInfo: false).GetFrame(0)?.GetMethod();
        if (method?.Module != typeof(HealthPipeServer).Module) { return false; }
        Type? declaringType = method.DeclaringType;
        while (declaringType?.DeclaringType is { } outer) { declaringType = outer; }
        return declaringType == typeof(HealthPipeServer)
            || declaringType?.FullName == "CIS.Healthcheck.Pipes.HealthPipeEndpoint";
    }

    // A separate real-process diagnostic keeps the public GC observation free of
    // concurrent tests. The integration test, rather than this fixture, decides
    // whether the observed retained pin count satisfies the resource lifetime.
    private static async Task ObserveWindowsPipePinRelease()
    {
        if (!OperatingSystem.IsWindows())
        {
            Write(new { status = "inapplicable", diagnostic = "pipe-pin-release", platform = "non-Windows" });
            return;
        }
        const int WarmupServers = 8, MeasuredServers = 32;
        await using var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "pipe-pin-release", InstanceId = Guid.NewGuid() });
        await SettlePipeServers(service, WarmupServers);
        long before = ObservePinnedObjectsAfterFullCollection();
        await SettlePipeServers(service, MeasuredServers);
        long after = ObservePinnedObjectsAfterFullCollection();
        Write(new
        {
            status = "observed",
            diagnostic = "pipe-pin-release",
            platform = "Windows",
            pid = Environment.ProcessId,
            runtime = RuntimeInformation.FrameworkDescription,
            warmupServers = WarmupServers,
            measuredServers = MeasuredServers,
            pinnedBefore = before,
            pinnedAfter = after
        });
    }

    private static async Task SettlePipeServers(IHealthSnapshotSource source, int count)
    {
        for (int index = 0; index < count; index++)
        {
            await using var server = new HealthPipeServer(source, new HealthcheckOptions
            {
                PipeEnabled = true,
                PipeName = "cis-pin-release-" + Guid.NewGuid().ToString("N"),
                PipeMaxClients = 1
            });
            await server.StartAsync();
            await server.StopAsync();
        }
    }

    private static long ObservePinnedObjectsAfterFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetGCMemoryInfo().PinnedObjectsCount;
    }

    private sealed class CountingSource(HealthcheckService service, IConditionMonitor monitor, string mode) : IHealthSnapshotSource
    {
        private int captures;
        private readonly HealthSnapshot? frozen = mode == "parity" ? service.CaptureSnapshot() : null;
        internal int Captures => Volatile.Read(ref captures);
        public HealthSnapshot CaptureSnapshot()
        {
            int count = Interlocked.Increment(ref captures);
            if (mode == "source-throw") { throw new InvalidOperationException("secret-snapshot-error"); }
            if (mode == "source-cancel") { throw new OperationCanceledException("secret-source-cancellation"); }
            HealthSnapshot snapshot = frozen ?? service.CaptureSnapshot();
            if (mode == "alternating") { if ((count & 1) == 1) { monitor.Flag(); } else { monitor.Clear(); } }
            return snapshot;
        }
    }
}
