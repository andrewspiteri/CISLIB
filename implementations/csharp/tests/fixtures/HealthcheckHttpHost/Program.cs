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
        var options = new HealthcheckOptions
        {
            DetailsEnabled = mode != "details-disabled",
            MaxReportBytes = mode == "overflow" ? 1024 : 4194304,
            PipeEnabled = mode is "parity" or "pipe-live",
            PipeName = mode is "parity" or "pipe-live" ? "cis-fixture-" + Guid.NewGuid().ToString("N") : null,
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
