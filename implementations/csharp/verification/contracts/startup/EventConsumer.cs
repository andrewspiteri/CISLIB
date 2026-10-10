#nullable enable
using System;
using System.Collections.Generic;
using CIS.Logging;

internal static class EventConsumer
{
    public static LogEventData Specify()
    {
        var properties = new Dictionary<string, LogValue>
        {
            ["null"] = LogValue.Null(),
            ["boolean"] = LogValue.Boolean(value: true),
            ["string"] = LogValue.String(value: "value"),
            ["number"] = LogValue.Number(value: 1.5),
            ["integer"] = LogValue.Integer(value: 9007199254740991),
            ["integer_text"] = LogValue.IntegerString(value: "9223372036854775808"),
            ["decimal_text"] = LogValue.DecimalString(value: "12.3400"),
            ["uuid"] = LogValue.Uuid(value: Guid.Empty),
            ["timestamp"] = LogValue.Timestamp(value: DateTimeOffset.UnixEpoch),
            ["array"] = LogValue.Array(items: new[] { LogValue.Null() }),
            ["map"] = LogValue.Map(properties: new Dictionary<string, LogValue>()),
        };
        LogValueKind kind = properties["map"].Kind;
        bool valueTruncated = properties["map"].IsTruncated;
        var context = new OperationContextData { OperationId = "op-1", Attributes = properties };
        return new LogEventData
        {
            EventName = "order.completed",
            Message = "Order completed",
            MessageTemplate = "Order {OrderId} completed",
            OperationId = context.OperationId,
            Attributes = context.Attributes,
            Exception = new Exception("Failure"),
        };
    }
}
