using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class IncomingStartupTests
{
    [TestMethod]
    public void InvalidStartupArgumentsReturnSafeResultsWithoutRetainingExceptions()
    {
        LogStartResult writers = CoreLogRuntime.TryCreate((IEnumerable<LogWriterRegistration>)null!, out CoreLogRuntime? failedWriter);
        LogStartResult processing = CoreLogRuntime.TryCreate((LogProcessingOptions)null!, out CoreLogRuntime? failedProcessing);
        Assert.IsFalse(writers.Succeeded); Assert.IsNull(failedWriter); Assert.IsNull(failedProcessing);
        Assert.AreEqual("InvalidStartupInput", writers.ErrorCode);
        Assert.AreEqual("InvalidStartupInput", processing.ErrorCode);
        LoggingOptions settings = EnvironmentOptions.Logging;
        int original = settings.IncomingScanPercentage;
        try
        {
            settings.IncomingScanPercentage = 0;
            LogStartResult configuration = CoreLogRuntime.TryCreate(out CoreLogRuntime? failedConfiguration);
            Assert.IsFalse(configuration.Succeeded); Assert.IsNull(failedConfiguration);
            Assert.AreEqual("StartupConfigurationInvalid", configuration.ErrorCode);
        }
        finally { settings.IncomingScanPercentage = original; }
    }

    [TestMethod]
    public async Task StartupResultUsesActualConfiguredCompositionAndCanStopSafely()
    {
        LogStartResult result = CoreLogRuntime.TryCreate(out CoreLogRuntime? created);
        Assert.IsTrue(result.Succeeded, result.ErrorCode); Assert.IsNotNull(created); Assert.IsNull(result.ErrorCode);
        await using var runtime = created;
        LogStopResult stopped = await runtime.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(LogCompletion.Completed, stopped.Completion); Assert.IsTrue(stopped.Succeeded);
        Assert.AreEqual(0L, stopped.Incoming!.Admitted);
    }
}
