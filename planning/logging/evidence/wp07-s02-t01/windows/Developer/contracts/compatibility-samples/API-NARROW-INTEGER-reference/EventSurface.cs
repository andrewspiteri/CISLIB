// WP02-S02 reference-only event input/value specification. Never include in CIS.Core.
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CIS.Logging;

public enum LogValueKind : int
{
    Null = 0,
    Boolean = 1,
    String = 2,
    Number = 3,
    Integer = 4,
    IntegerString = 5,
    DecimalString = 6,
    Uuid = 7,
    Timestamp = 8,
    Array = 9,
    Map = 10,
}

public sealed class LogValue
{
    private LogValue() { }
    public extern LogValueKind Kind { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    public extern bool IsTruncated { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue Null();
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue Boolean(bool value);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue String(string value);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue Number(double value);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue Integer(int value);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue IntegerString(string value);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue DecimalString(string value);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue Uuid(Guid value);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue Timestamp(DateTimeOffset value);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue Array(IEnumerable<LogValue> items);
    [MethodImpl(MethodImplOptions.InternalCall)] public static extern LogValue Map(IReadOnlyDictionary<string, LogValue> properties);
}

public sealed class LogEventData
{
    [MethodImpl(MethodImplOptions.InternalCall)] public extern LogEventData();
    public extern string EventName { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
    public extern string Message { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
    public extern string? MessageTemplate { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
    public extern string? OperationId { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
    public extern IReadOnlyDictionary<string, LogValue>? Attributes { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
    public extern Exception? Exception { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
}

public sealed class OperationContextData
{
    [MethodImpl(MethodImplOptions.InternalCall)] public extern OperationContextData();
    public extern string? OperationId { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
    public extern IReadOnlyDictionary<string, LogValue>? Attributes { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
}
