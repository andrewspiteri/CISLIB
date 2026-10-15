#nullable enable
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;

internal static class ProviderConsumer
{
    public static void Use(CoreLogRuntime runtime, IExternalScopeProvider scopeProvider)
    {
        using var provider = new CoreLoggerProvider(runtime: runtime);
        ILogger category = provider.CreateLogger(categoryName: "Orders");
        provider.SetScopeProvider(scopeProvider: scopeProvider);
        ILoggerProvider abstraction = provider;
        ISupportExternalScope scopes = provider;
        scopes.SetScopeProvider(scopeProvider: scopeProvider);
        provider.Dispose();
        // Shape only: later runtime tests must prove disposal leaves this runtime available.
        CoreLogDiagnostics diagnostic = runtime.GetDiagnostics();
    }
}
