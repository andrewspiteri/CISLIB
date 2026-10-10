// Compile-only consumer shape; never execute against the reference-only specification.
#nullable enable
using CIS.Logging;

internal static class NumericValues
{
    internal static LogValue[] Create() =>
    [
        LogValue.Integer(value: (byte)1),
        LogValue.Integer(value: (short)2),
        LogValue.Integer(value: 3),
        LogValue.Integer(value: 9007199254740991L),
        LogValue.Number(value: 1),
        LogValue.Number(value: 1f),
        LogValue.Number(value: 1d),
        LogValue.IntegerString(value: "9223372036854775807"),
        LogValue.DecimalString(value: "3.00")
    ];
}
