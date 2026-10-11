// WP02-S03 compile-only provider ownership/API contract; approved Microsoft abstractions only.
#nullable enable
using System.Runtime.CompilerServices;
using CIS.Logging;
using Microsoft.Extensions.Logging;

namespace CIS.Logging.MicrosoftLogger;

public sealed class CoreLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern CoreLoggerProvider(CoreLogRuntime runtime);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern ILogger CreateLogger(string categoryName);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern void SetScopeProvider(IExternalScopeProvider scopeProvider);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public extern void Dispose();
}
