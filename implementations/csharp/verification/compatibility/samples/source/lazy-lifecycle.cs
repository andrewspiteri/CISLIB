// Compile-only consumer shape; never execute against the reference-only specification.
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using CIS;
using CIS.Logging;

internal static class LazyLifecycle
{
    internal static async Task Use(CancellationToken token)
    {
        await using CoreLogRuntime runtime = CoreLogRuntime.Create();
        CoreLogger logger = runtime.CreateLogger(component: "Orders");
        using IDisposable scope = runtime.BeginScope(context: new OperationContextData { OperationId = "order-1" });
        _ = logger.WriteLazy(level: CoreLogLevel.Information, factory: () => new LogEventData { EventName = "created", Message = "Created" });
        LogFlushResult flush = await runtime.FlushAsync(timeout: TimeSpan.FromSeconds(1), cancellationToken: token);
        LogStopResult stop = await runtime.StopAsync(cancellationToken: token);
        CoreLogDiagnostics diagnostics = runtime.GetDiagnostics();
        _ = (flush.LossObserved, stop.Succeeded, diagnostics.TruncatedEventCount);
    }
}
