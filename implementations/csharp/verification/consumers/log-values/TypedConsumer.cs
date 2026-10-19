using System;
using System.Collections.Generic;
using CIS.Logging;

internal static class Program
{
    // Compiles the new call shapes against the actual Core DLL. Native runtime cases qualify execution.
    private static LogWriteResult Interpolation(CoreLogger logger)
    {
        int id = 42;
        var values = new Dictionary<string, LogValue> { ["id"] = LogValue.Integer(id) };
        _ = logger.Trace($"Order {id}");
        _ = logger.Debug($"Order {id}", values);
        _ = logger.Warn("order.retry", $"Order {id:D4}", values);
        _ = logger.Error($"Order {id}");
        _ = logger.Critical($"Order {id}");
        _ = logger.Write(CoreLogLevel.Warning, $"Order {id}", context: new LogEventData { EventName = "order.retry" });
        return logger.Info($"Order {id} accepted", values);
    }

    private static int Main()
    {
        LogValue[] values = [LogValue.Null(), LogValue.Boolean(true), LogValue.String("text"),
            LogValue.Number(9007199254740992.0), LogValue.Integer(9007199254740991),
            LogValue.IntegerString("9223372036854775808"), LogValue.DecimalString("12.3400"),
            LogValue.Uuid(Guid.Empty), LogValue.Timestamp(DateTimeOffset.MinValue),
            LogValue.Array([]), LogValue.Map(new Dictionary<string, LogValue>())];
        for (int index = 0; index < values.Length; index++)
        {
            if ((int)values[index].Kind != index || values[index].IsTruncated)
            {
                return 1;
            }
        }
        try
        {
            LogValue.Integer(9007199254740992);
            return 2;
        }
        catch (ArgumentOutOfRangeException error)
        {
            if (error.ParamName != "value" || error.ActualValue is not null)
            {
                return 3;
            }
        }
        Console.WriteLine("Typed log-value consumer passed.");
        return 0;
    }
}
