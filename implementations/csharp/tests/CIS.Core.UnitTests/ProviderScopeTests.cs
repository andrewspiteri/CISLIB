using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.Tests.DiscoveryFixture;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderScopeTests
{
    public static IEnumerable<object[]> Modes => ProviderScopeFixture.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ProviderScopesUseBoundedCoreCopies(string mode)
    { ProviderScopeExpectations.Validate(mode, await ProviderScopeFixture.ExecuteAsync(mode)); }

    [TestMethod]
    public async Task ScopeGuardsRemainConcreteAndRegistrationDoesNotTakeOwnership()
    {
        var runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None });
        using var provider = new CoreLoggerProvider(runtime); ILogger logger = provider.CreateLogger("Guards"); var host = new LoggerExternalScopeProvider();
        try
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => provider.SetScopeProvider(null!)); provider.SetScopeProvider(host);
            IDisposable outer = logger.BeginScope(new OperationContextData())!, inner = logger.BeginScope(new OperationContextData())!;
            Assert.ThrowsExactly<InvalidOperationException>(outer.Dispose); inner.Dispose(); outer.Dispose(); outer.Dispose();
            await runtime.StopAsync(CancellationToken.None); Assert.ThrowsExactly<InvalidOperationException>(() => logger.BeginScope(new Dictionary<string, object?> { ["bad"] = new object() }));
            provider.Dispose(); Assert.ThrowsExactly<ArgumentNullException>(() => provider.SetScopeProvider(null!)); Assert.ThrowsExactly<ObjectDisposedException>(() => provider.SetScopeProvider(host));
            Assert.IsNull(typeof(CoreLoggerProvider).GetField("externalScopes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(provider));
        }
        finally { await runtime.DisposeAsync(); }
    }
}
