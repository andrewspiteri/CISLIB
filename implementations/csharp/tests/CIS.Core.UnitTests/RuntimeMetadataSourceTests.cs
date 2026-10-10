using System.Diagnostics;
using System.Runtime.InteropServices;
using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class RuntimeMetadataSourceTests
{
    [TestMethod]
    public void NativeAdapterMatchesCurrentProcessAndRuntimeFacts()
    {
        using Process process = Process.GetCurrentProcess();

        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover();

        Assert.AreEqual(new DateTimeOffset(process.StartTime.ToUniversalTime()), metadata.ProcessStartedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, metadata.ProcessStartedAtUtc!.Value.Offset);
        Assert.AreEqual(System.Environment.MachineName, metadata.HostName);
        Assert.AreEqual(RuntimeInformation.OSDescription.Trim(), metadata.OperatingSystemDescription);
        Assert.AreEqual(RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), metadata.OSArchitecture);
        Assert.AreEqual(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), metadata.ProcessArchitecture);
        Assert.AreEqual(RuntimeInformation.FrameworkDescription.Trim(), metadata.RuntimeDescription);
        Assert.AreEqual(System.Environment.ProcessId, metadata.ProcessId);
        string family = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown";
        Assert.AreEqual(family, metadata.OperatingSystem);
    }
}
