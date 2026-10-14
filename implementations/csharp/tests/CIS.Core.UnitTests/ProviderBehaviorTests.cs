using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.Tests.DiscoveryFixture;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderBehaviorTests
{
    public static IEnumerable<object[]> Modes => ProviderBehaviorFixture.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ProviderInterestAndRejectionFollowCore(string mode)
    { ProviderBehaviorExpectations.Validate(mode, await ProviderBehaviorFixture.ExecuteAsync(mode)); }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ArgumentGuardsPrecedeDisposedAndClosedNoOps(bool disposed)
    {
        var runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None }, null, null);
        using var provider = new CoreLoggerProvider(runtime); ILogger logger = provider.CreateLogger("Guards");
        try
        {
            if (disposed) { provider.Dispose(); } else { await runtime.StopAsync(CancellationToken.None); }
            Assert.ThrowsExactly<ArgumentNullException>(() => logger.Log<string>((LogLevel)99, default, "state", null, null!));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => logger.Log((LogLevel)99, default, "state", null, (s, _) => s));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => logger.IsEnabled((LogLevel)99));
            Assert.AreEqual(0L, runtime.GetDiagnostics().InvalidEventCount);
        }
        finally { await runtime.DisposeAsync(); }
    }
}

