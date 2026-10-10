// Compile-only consumer shape; never execute against the reference-only specification.
#nullable enable
using System;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;

internal sealed class CustomScopes : IExternalScopeProvider
{
    private readonly LoggerExternalScopeProvider inner = new();

    public void ForEachScope<TState>(Action<object?, TState> callback, TState state) => inner.ForEachScope(callback, state);
    public IDisposable Push(object? state) => inner.Push(state);

    internal static void Use(CoreLogRuntime runtime)
    {
        using var provider = new CoreLoggerProvider(runtime: runtime);
        ISupportExternalScope scopes = provider;
        scopes.SetScopeProvider(scopeProvider: new CustomScopes());
        ILoggerProvider abstraction = provider;
        _ = abstraction.CreateLogger(categoryName: "Orders");
    }
}
