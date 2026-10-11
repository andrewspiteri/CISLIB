// Compile-only consumer shape; never execute against the reference-only specification.
#nullable enable
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;

internal static class LoggingExtensions
{
    internal static void Use(CoreLogRuntime runtime)
    {
        using var provider = new CoreLoggerProvider(runtime: runtime);
        ILogger logger = provider.CreateLogger(categoryName: "Orders");
        logger.LogInformation(eventId: new EventId(7, "Created"), message: "Order {OrderId}", args: new object?[] { 42 });
        logger.LogError(exception: null, message: "Order {OrderId} failed", args: new object?[] { 42 });
        using var scope = logger.BeginScope(state: "order-42");
    }
}
