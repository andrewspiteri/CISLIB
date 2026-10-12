// Compile-only consumer shape; never execute against the reference-only specification.
#nullable enable
using CIS;
using CIS.Logging;

internal static class NullCalls
{
    internal static void Bind(CoreLogger logger)
    {
        _ = logger.Write(level: CoreLogLevel.None, data: null!);
        _ = logger.WriteLazy(level: CoreLogLevel.None, factory: null!);
        _ = LogValue.String(value: null!);
    }
}
