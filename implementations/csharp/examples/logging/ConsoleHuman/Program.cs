using System.Globalization;
using System.Text.Json;
using CIS;
using CIS.Configuration;
using CIS.Logging;

CoreLogRuntime? runtime = null;
bool succeeded = true;
try
{
    // First access applies the existing defaults and environment overrides once.
    // The application supplies its kind; logging options, including 64 KiB, stay caller-configured.
    EnvironmentOptions.EnvironmentKind = EnvironmentKind.Console;
    LogStartResult start = CoreLogRuntime.TryCreate(out runtime);
    if (!start.Succeeded || runtime is null)
    {
        Console.Error.WriteLine($"STARTUP_FAILED: {start.ErrorCode}. Check CORE_LOG_* settings and access to the configured output.");
        return 1;
    }
    Console.WriteLine($"LOGGING max_entry_bytes={EnvironmentOptions.Logging.MaxEntryBytes} console_format={EnvironmentOptions.Logging.ConsoleFormat} file_enabled={EnvironmentOptions.Logging.FileEnabled}");

    CoreLogger logger = runtime.CreateLogger("Orders"); // The logger borrows this application's runtime.
    LogWriteResult[] writes = await QuoteOrderAsync(runtime, logger);
    foreach (LogWriteResult write in writes)
    {
        // Admission to the incoming queue is not processing, delivery or durability.
        Console.WriteLine($"ADMISSION outcome={write.Outcome} incoming_sequence={write.IncomingSequence} truncated={write.IsTruncated}");
        succeeded &= (write.Outcome is LogWriteOutcome.Accepted or LogWriteOutcome.Filtered) && !write.IsTruncated;
    }
    LogFlushResult flush = await runtime.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
    Console.WriteLine($"FLUSH completion={flush.Completion} succeeded={flush.Succeeded} loss_observed={flush.LossObserved} error={flush.ErrorCode ?? "none"}");
    succeeded &= flush.Succeeded && !flush.LossObserved;
}
catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
{
    succeeded = false;
    Console.Error.WriteLine($"{(runtime is null ? "STARTUP_FAILED" : "APPLICATION_FAILED")}: {error.GetType().Name}.");
    // Static environment initialization can fail before TryCreate. These existing
    // configuration diagnostics contain recognized keys and safe guidance, not raw values.
    Exception cause = error is TypeInitializationException { InnerException: { } inner } ? inner : error;
    if (cause is CoreConfigurationException configuration)
    {
        foreach (ConfigurationIssue issue in configuration.Issues)
        { Console.Error.WriteLine($"{issue.Key}: {issue.Explanation} ({issue.Code})"); }
    }
}
finally
{
    if (runtime is not null)
    {
        try
        {
            LogStopResult stop = await runtime.StopAsync(CancellationToken.None);
            Console.WriteLine($"STOP completion={stop.Completion} succeeded={stop.Succeeded} loss_observed={stop.LossObserved} error={stop.ErrorCode ?? "none"}");
            succeeded &= stop.Succeeded && !stop.LossObserved;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            succeeded = false;
            Console.Error.WriteLine($"SHUTDOWN_FAILED: stop failed ({error.GetType().Name}).");
        }
        try
        {
            await runtime.DisposeAsync(); // Joins the same bounded stop; it is not actual-completion proof.
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            succeeded = false;
            Console.Error.WriteLine($"SHUTDOWN_FAILED: bounded disposal failed ({error.GetType().Name}).");
        }
        try
        {
            await runtime.Completion; // No timeout: keep runtime-owned resources alive until cleanup really settles.
            Console.WriteLine("RUNTIME_COMPLETED");
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            succeeded = false;
            Console.Error.WriteLine($"SHUTDOWN_FAILED: runtime cleanup failed ({error.GetType().Name}).");
        }
    }
}
return succeeded ? 0 : 1;

static async Task<LogWriteResult[]> QuoteOrderAsync(CoreLogRuntime runtime, CoreLogger logger)
{
    // This method creates and disposes its scope in its own logical async flow.
    using var scope = runtime.BeginScope(new OperationContextData
    {
        OperationId = "quote-order-1042",
        Attributes = new Dictionary<string, LogValue>
        {
            ["request_id"] = LogValue.String("request-1042"),
            ["channel"] = LogValue.String("console")
        }
    });
    LogWriteResult started = logger.Info(new LogEventData
    {
        EventName = "order.started", Message = "Reading order order-1042 for a quote.",
        Attributes = new Dictionary<string, LogValue> { ["order_id"] = LogValue.String("order-1042") }
    });
    using var input = new MemoryStream("""
        {"OrderId":"order-1042","Items":[{"Sku":"tea","Quantity":2,"UnitPrice":4.50},{"Sku":"coffee","Quantity":1,"UnitPrice":7.25}]}
        """u8.ToArray());
    Order order = await JsonSerializer.DeserializeAsync<Order>(input)
        ?? throw new InvalidDataException("The sample order is missing.");
    int units = order.Items.Sum(item => item.Quantity);
    string total = order.Items.Sum(item => item.Quantity * item.UnitPrice).ToString("0.00", CultureInfo.InvariantCulture);
    LogWriteResult completed = logger.Info(new LogEventData
    {
        EventName = "order.completed", Message = $"Quoted order {order.OrderId}: {units} units total {total}.",
        Attributes = new Dictionary<string, LogValue>
        {
            ["order_id"] = LogValue.String(order.OrderId), ["item_count"] = LogValue.Integer(order.Items.Length),
            ["unit_count"] = LogValue.Integer(units), ["total"] = LogValue.DecimalString(total)
        }
    });
    Console.WriteLine($"ORDER order_id={order.OrderId} item_count={order.Items.Length} unit_count={units} total={total}");
    return [started, completed];
}

internal sealed record Order(string OrderId, OrderItem[] Items);
internal sealed record OrderItem(string Sku, int Quantity, decimal UnitPrice);
