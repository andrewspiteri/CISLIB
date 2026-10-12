using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class EnvironmentConstructorTests
{
    [TestMethod]
    [DataRow(" 1.2.3-rc.1+Actual.Build ", "1.2.3-rc.1+Actual.Build")]
    [DataRow(" \t ", "23.0.0.0")]
    [DataRow(null, "23.0.0.0")]
    public void AssemblyInformationPrecedesAssemblyVersionAndIgnoresFileVersion(string? information, string expected)
    {
        Assert.AreEqual(expected, EnvironmentOptions.ReadBuildVersion(ApplicationAssembly(information)));
        Assert.AreNotEqual("88.0.0.0", EnvironmentOptions.ReadBuildVersion(ApplicationAssembly(information)));
    }

    [TestMethod]
    public void MissingEntryAssemblyHasAnHonestUnknownBuildFallback()
    {
        Assert.AreEqual("unknown", EnvironmentOptions.ReadBuildVersion(null));
    }

    [TestMethod]
    public void AutomaticFactsMatchTheCurrentProcessAndProduceOwnedMetadata()
    {
        Assert.AreEqual(System.Environment.ProcessId, EnvironmentOptions.ProcessId);
        Assert.AreEqual(System.Environment.MachineName, EnvironmentOptions.HostName);
        Assert.AreEqual(RuntimeInformation.OSDescription, EnvironmentOptions.OperatingSystemDescription);
        Assert.AreEqual(RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), EnvironmentOptions.OSArchitecture);
        Assert.AreEqual(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), EnvironmentOptions.ProcessArchitecture);
        Assert.AreEqual(RuntimeInformation.FrameworkDescription, EnvironmentOptions.RuntimeDescription);
        string family = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown";
        Assert.AreEqual(family, EnvironmentOptions.OperatingSystem);
        using Process process = Process.GetCurrentProcess();
        Assert.AreEqual(new DateTimeOffset(process.StartTime.ToUniversalTime()), EnvironmentOptions.ProcessStartedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, EnvironmentOptions.ProcessStartedAtUtc!.Value.Offset);
        Assert.IsTrue(EnvironmentOptions.ProcessStartedAtUtc <= EnvironmentOptions.InitializedAtUtc);
        Assert.IsTrue(EnvironmentOptions.InitializedAtUtc <= DateTimeOffset.UtcNow);
        Assert.AreEqual(TimeSpan.Zero, EnvironmentOptions.InitializedAtUtc.Offset);
        Assert.IsTrue(Guid.TryParseExact(EnvironmentOptions.InstanceId, "D", out Guid instance));
        Assert.AreNotEqual(Guid.Empty, instance);
        var metadata = new EventMetadata();
        Assert.AreEqual(process.Id, metadata.ProcessId);
        Assert.AreSame(EnvironmentOptions.ApplicationName, metadata.ApplicationName);
        Assert.AreSame(EnvironmentOptions.BuildVersion, metadata.BuildVersion);
    }

    [TestMethod]
    public void RepeatedAccessRetainsTheSameFactsAndSettingsWithoutRegeneratingIds()
    {
        string instance = EnvironmentOptions.InstanceId;
        DateTimeOffset time = EnvironmentOptions.InitializedAtUtc;
        LoggingOptions logging = EnvironmentOptions.Logging;
        var first = new EventMetadata();
        var second = new EventMetadata();
        Assert.AreEqual(instance, first.InstanceId);
        Assert.AreEqual(instance, second.InstanceId);
        Assert.AreEqual(time, second.InitializedAtUtc);
        Assert.AreSame(logging, EnvironmentOptions.Logging);
        Assert.IsNull(string.IsInterned(instance));
    }

    [TestMethod]
    public void CommonIdentityValuesAreInternedAndPolicyValuesRemainOrdinaryProperties()
    {
        foreach (string identity in new[] { EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName, EnvironmentOptions.BuildVersion, EnvironmentOptions.ServiceVersion })
        {
            Assert.AreSame(identity, string.IsInterned(new string(identity.ToCharArray())));
        }
        foreach (string name in new[] { "EnvironmentKind", "CommitId", "BuildId" })
        {
            Assert.IsTrue(typeof(EnvironmentOptions).GetProperty(name)!.SetMethod!.IsPublic);
        }
        Assert.AreEqual(15, typeof(EnvironmentOptions).GetProperties().Count(property => property.SetMethod is null));
    }

    private static Assembly ApplicationAssembly(string? information)
    {
        AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("DefaultsTest.Application") { Version = new Version(23, 0, 0, 0) }, AssemblyBuilderAccess.RunAndCollect);
        builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyFileVersionAttribute).GetConstructor([typeof(string)])!, ["88.0.0.0"]));
        if (information is not null)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [information]));
        }
        return builder;
    }
}
