using System;
using CIS.Logging;

internal static class CallerConversion
{
    // Conversion belongs to the caller, before the typed factory is invoked.
    internal static LogValue Convert(DateTime value) => LogValue.Timestamp(value);
}
