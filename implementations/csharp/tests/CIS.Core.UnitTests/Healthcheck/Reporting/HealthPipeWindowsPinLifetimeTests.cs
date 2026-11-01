using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
[TestCategory("ComponentIntegration")]
[DoNotParallelize]
public sealed class HealthPipeWindowsPinLifetimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task ActualSettledWindowsEndpointsReleaseTheirSecurityDescriptorPins()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows native security-descriptor pin observation is inapplicable on this platform.");
            return;
        }
        await using var service = new HealthcheckService(new(), new HealthApplicationIdentity { Name = "direct pipe pin lifetime", InstanceId = Guid.NewGuid() });
        int warmup = await SettleAsync(service, 8);
        long before = PinsAfterFullCollection();
        int measured = await SettleAsync(service, 32);
        long after = PinsAfterFullCollection();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            observation = "actual Windows endpoint pin lifetime",
            runtime = RuntimeInformation.FrameworkDescription,
            processId = Environment.ProcessId,
            warmupServers = warmup,
            measuredServers = measured,
            pinnedBefore = before,
            pinnedAfter = after
        }));
        Assert.IsTrue(before >= 0 && after >= 0);
        Assert.IsTrue(after <= before, $"Actually started, stopped and disposed Windows endpoints retained pins: before={before}, after={after}.");
    }

    private static async Task<int> SettleAsync(IHealthSnapshotSource source, int count)
    {
        int settled = 0;
        for (int index = 0; index < count; index++)
        {
            var server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = "hc-direct-pin-" + Guid.NewGuid().ToString("N"), PipeMaxClients = 1 });
            var actual = new List<Task>();
            try
            {
                Task starting = server.StartAsync(); actual.Add(starting); await starting.WaitAsync(Deadline);
                Task stopping = server.StopAsync(); actual.Add(stopping); await stopping.WaitAsync(Deadline);
            }
            finally
            {
                // Initiate and join both actual drains even when startup/stop faults.
                try { actual.Add(server.StopAsync()); } catch (Exception error) { actual.Add(Task.FromException(error)); }
                try { actual.Add(server.DisposeAsync().AsTask()); } catch (Exception error) { actual.Add(Task.FromException(error)); }
                await Task.WhenAll(actual).WaitAsync(Deadline);
            }
            settled++;
        }
        return settled;
    }

    private static long PinsAfterFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetGCMemoryInfo().PinnedObjectsCount;
    }
}
