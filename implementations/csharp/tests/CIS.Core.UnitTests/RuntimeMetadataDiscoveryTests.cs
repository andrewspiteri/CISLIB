using System.Globalization;
using System.Runtime.InteropServices;
using CIS.Configuration;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class RuntimeMetadataDiscoveryTests
{
    [TestMethod]
    [DataRow("windows", "windows")]
    [DataRow("linux", "linux")]
    [DataRow("macos", "macos")]
    [DataRow("freebsd", "unknown")]
    [DataRow(null, "unknown")]
    public void FamilyRecognitionIsIndependentOfTheExecutionHost(string? supplied, string expected)
    {
        OSPlatform? platform = supplied switch
        {
            "windows" => OSPlatform.Windows,
            "linux" => OSPlatform.Linux,
            "macos" => OSPlatform.OSX,
            null => null,
            _ => OSPlatform.Create(supplied)
        };
        var source = new RuntimeMetadataStub { Platform = platform };

        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover(source);

        Assert.AreEqual(expected, metadata.OperatingSystem);
        Assert.AreEqual("Windows Fixture", metadata.OperatingSystemDescription);
    }

    [TestMethod]
    [DataRow(Architecture.X86, "x86")]
    [DataRow(Architecture.X64, "x64")]
    [DataRow(Architecture.Arm, "arm")]
    [DataRow(Architecture.Arm64, "arm64")]
    [DataRow(Architecture.Wasm, "wasm")]
    [DataRow(Architecture.S390x, "s390x")]
    [DataRow(Architecture.LoongArch64, "loongarch64")]
    [DataRow(Architecture.Armv6, "armv6")]
    [DataRow(Architecture.Ppc64le, "ppc64le")]
    [DataRow(Architecture.RiscV64, "riscv64")]
    [DataRow((Architecture)(-1), null)]
    [DataRow((Architecture)int.MaxValue, null)]
    [DataRow(null, null)]
    public void ArchitecturesUseDefinedLowercaseNamesOrRemainAbsent(Architecture? supplied, string? expected)
    {
        var source = new RuntimeMetadataStub { OSArchitecture = supplied, ProcessArchitecture = supplied };

        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover(source);

        Assert.AreEqual(expected, metadata.OSArchitecture);
        Assert.AreEqual(expected, metadata.ProcessArchitecture);
    }

    [TestMethod]
    public void ProcessArchitectureIsNotReplacedWithOSArchitecture()
    {
        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover(new RuntimeMetadataStub());

        Assert.AreEqual("x64", metadata.OSArchitecture);
        Assert.AreEqual("x86", metadata.ProcessArchitecture);
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow(0, null)]
    [DataRow(-1, null)]
    [DataRow(int.MinValue, null)]
    [DataRow(1, 1)]
    [DataRow(123, 123)]
    [DataRow(int.MaxValue, int.MaxValue)]
    public void ProcessIdsMustBePositiveOrRemainAbsent(int? supplied, int? expected)
    {
        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover(new RuntimeMetadataStub { ProcessId = supplied });

        Assert.AreEqual(expected, metadata.ProcessId);
    }

    [TestMethod]
    [DataRow("2026-10-10T09:00:00+02:00", "2026-10-10T07:00:00Z")]
    [DataRow("2026-10-10T09:00:00-07:00", "2026-10-10T16:00:00Z")]
    [DataRow("2026-10-10T09:00:00+14:00", "2026-10-09T19:00:00Z")]
    [DataRow("2026-10-10T09:00:00Z", "2026-10-10T09:00:00Z")]
    public void ProcessStartUsesTheDiscoveredInstantInUTC(string supplied, string expected)
    {
        var source = new RuntimeMetadataStub { ProcessStartedAt = DateTimeOffset.Parse(supplied, CultureInfo.InvariantCulture) };

        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover(source);

        Assert.AreEqual(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), metadata.ProcessStartedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, metadata.ProcessStartedAtUtc!.Value.Offset);
    }

    [TestMethod]
    public void UnavailableFactsStayAbsentWithoutAClockOrOtherSubstitution()
    {
        var source = new RuntimeMetadataStub
        {
            ProcessStartedAt = null,
            HostName = null,
            Platform = null,
            OSDescription = null,
            OSArchitecture = null,
            ProcessArchitecture = null,
            RuntimeDescription = null,
            ProcessId = null
        };

        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover(source);

        Assert.IsNull(metadata.ProcessStartedAtUtc);
        Assert.IsNull(metadata.HostName);
        Assert.AreEqual("unknown", metadata.OperatingSystem);
        Assert.IsNull(metadata.OperatingSystemDescription);
        Assert.IsNull(metadata.OSArchitecture);
        Assert.IsNull(metadata.ProcessArchitecture);
        Assert.IsNull(metadata.RuntimeDescription);
        Assert.IsNull(metadata.ProcessId);
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow("", null)]
    [DataRow(" \t\r\n", null)]
    [DataRow("\u2003", null)]
    [DataRow(" MixCase ", "MixCase")]
    [DataRow(" OS Description (Build.42) ", "OS Description (Build.42)")]
    public void OptionalTextIsAbsentWhenBlankAndPreservesTrimmedCase(string? supplied, string? expected)
    {
        var source = new RuntimeMetadataStub
        {
            HostName = supplied,
            OSDescription = supplied,
            RuntimeDescription = supplied
        };

        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover(source);

        Assert.AreEqual(expected, metadata.HostName);
        Assert.AreEqual(expected, metadata.OperatingSystemDescription);
        Assert.AreEqual(expected, metadata.RuntimeDescription);
    }

    [TestMethod]
    public void CapturedFactsDoNotRetainTheMutableSource()
    {
        var source = new RuntimeMetadataStub();
        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover(source);
        source.ProcessStartedAt = null;
        source.HostName = "Changed";
        source.Platform = OSPlatform.Linux;
        source.OSDescription = "Changed";
        source.OSArchitecture = Architecture.Arm64;
        source.ProcessArchitecture = Architecture.Arm64;
        source.RuntimeDescription = "Changed";
        source.ProcessId = 42;

        Assert.AreEqual(new DateTimeOffset(2026, 10, 10, 7, 0, 0, TimeSpan.Zero), metadata.ProcessStartedAtUtc);
        Assert.AreEqual("ApplicationVisible.Host", metadata.HostName);
        Assert.AreEqual("windows", metadata.OperatingSystem);
        Assert.AreEqual("Windows Fixture", metadata.OperatingSystemDescription);
        Assert.AreEqual("x64", metadata.OSArchitecture);
        Assert.AreEqual("x86", metadata.ProcessArchitecture);
        Assert.AreEqual(".NET Fixture", metadata.RuntimeDescription);
        Assert.AreEqual(123, metadata.ProcessId);
        Assert.AreEqual(8, source.Reads.Count);
        Assert.AreEqual(8, source.Reads.Distinct().Count());
    }
}
