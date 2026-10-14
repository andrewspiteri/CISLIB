using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.Tests.DiscoveryFixture;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderFoundationTests
{
    public static IEnumerable<object[]> Modes => ProviderFoundationFixture.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ProviderUsesActualCoreCaptureAndOwnership(string mode)
    { ProviderFoundationExpectations.Validate(mode, await ProviderFoundationFixture.ExecuteAsync(mode)); }

    [TestMethod]
    public async Task RequiredArgumentsAndDisposedCreationAreConcrete()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new CoreLoggerProvider(null!));
        var runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None }, null, null);
        try
        {
            using var provider = new CoreLoggerProvider(runtime);
            Assert.ThrowsExactly<ArgumentNullException>(() => provider.CreateLogger(null!));
            foreach (string bad in new[] { "", " ", "bad\0name", "\ud800", new string('x', 256) }) { Assert.ThrowsExactly<ArgumentException>(() => provider.CreateLogger(bad)); }
            ILogger logger = provider.CreateLogger("Valid");
            Assert.ThrowsExactly<ArgumentNullException>(() => logger.Log<string>(LogLevel.None, default, "state", null, null!));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => logger.IsEnabled((LogLevel)99));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => logger.Log((LogLevel)99, default, "state", null, (s, _) => s));
            Assert.ThrowsExactly<ArgumentException>(() => logger.BeginScope("unmapped foundation scope"));
            provider.Dispose(); Assert.ThrowsExactly<ObjectDisposedException>(() => provider.CreateLogger("Later"));
            Assert.IsFalse(runtime.GetDiagnostics().AdmissionClosed);
        }
        finally { await runtime.DisposeAsync(); }
    }
}
