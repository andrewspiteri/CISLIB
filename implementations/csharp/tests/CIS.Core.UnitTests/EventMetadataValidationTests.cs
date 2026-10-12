using CIS.Configuration;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class EventMetadataValidationTests
{
    [TestMethod]
    [DataRow("OperatingSystem", null)]
    [DataRow("OperatingSystem", "")]
    [DataRow("OperatingSystem", "invalid-os")]
    [DataRow("OSArchitecture", "x-64")]
    [DataRow("ProcessArchitecture", "arm 64")]
    [DataRow("HostName", "high-surrogate")]
    [DataRow("OperatingSystemDescription", "high-surrogate")]
    [DataRow("RuntimeDescription", "high-surrogate")]
    [DataRow("OSArchitecture", "high-surrogate")]
    [DataRow("ProcessArchitecture", "high-surrogate")]
    public void LoggingRejectsInvalidSuppliedRuntimeFacts(string field, string? value)
    {
        EnvironmentStoreTestData.AssignValidFacts();
        if (value == "high-surrogate") { value = "text" + (char)0xd800; }
        typeof(FixtureEnvironmentValues).GetProperty(field)!.SetValue(EnvironmentStoreTestData.Values, value);
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentStoreTestData.Values.CaptureMetadata());
        Assert.AreEqual("InvalidIdentity", error.Issues.Single().Code);
        Assert.AreEqual("EnvironmentOptions." + field, error.Issues.Single().Key);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void ProcessIdentifiersMustBePositive(int value)
    {
        EnvironmentStoreTestData.AssignValidFacts();
        EnvironmentStoreTestData.Values.ProcessId = value;
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentStoreTestData.Values.CaptureMetadata());
        Assert.AreEqual("OutOfRange", error.Issues.Single().Code);
        Assert.AreEqual("EnvironmentOptions.ProcessId", error.Issues.Single().Key);
    }

    [TestMethod]
    public void LoggingNormalizesOwnedFactsWithoutChangingTheStore()
    {
        EnvironmentStoreTestData.AssignValidFacts();
        EnvironmentStoreTestData.Values.OperatingSystem = " Windows ";
        EnvironmentStoreTestData.Values.OSArchitecture = " X64 ";
        EnvironmentStoreTestData.Values.ProcessArchitecture = " ARM64 ";
        EnvironmentStoreTestData.Values.HostName = " Host.Mixed ";
        EnvironmentStoreTestData.Values.RuntimeDescription = " ";
        EnvironmentStoreTestData.Values.OperatingSystemDescription = "\t";
        EnvironmentStoreTestData.Values.InstanceId = "AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE";
        var supplied = new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.FromHours(2));
        EnvironmentStoreTestData.Values.InitializedAtUtc = supplied;
        EnvironmentStoreTestData.Values.ProcessStartedAtUtc = supplied.AddMinutes(-1);
        EventMetadata metadata = EnvironmentStoreTestData.Values.CaptureMetadata();
        Assert.AreEqual("windows", metadata.OperatingSystem);
        Assert.AreEqual("x64", metadata.OSArchitecture);
        Assert.AreEqual("arm64", metadata.ProcessArchitecture);
        Assert.AreEqual("Host.Mixed", metadata.HostName);
        Assert.IsNull(metadata.RuntimeDescription);
        Assert.IsNull(metadata.OperatingSystemDescription);
        Assert.AreEqual("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", metadata.InstanceId);
        Assert.AreEqual(TimeSpan.Zero, metadata.InitializedAtUtc.Offset);
        Assert.AreEqual(TimeSpan.Zero, metadata.ProcessStartedAtUtc!.Value.Offset);
        Assert.AreEqual(" Windows ", EnvironmentStoreTestData.Values.OperatingSystem);
        Assert.AreEqual(supplied, EnvironmentStoreTestData.Values.InitializedAtUtc);
        Assert.AreEqual("AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE", EnvironmentStoreTestData.Values.InstanceId);
    }

    [TestMethod]
    public void MissingStartupTimeIsRejectedAtLoggingConsumption()
    {
        EnvironmentStoreTestData.AssignValidFacts();
        EnvironmentStoreTestData.Values.InitializedAtUtc = default;
        Assert.AreEqual(default, EnvironmentStoreTestData.Values.InitializedAtUtc);
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentStoreTestData.Values.CaptureMetadata());
        Assert.AreEqual("EnvironmentOptions.InitializedAtUtc", error.Issues.Single().Key);
        Assert.AreEqual("InvalidIdentity", error.Issues.Single().Code);
    }

    [TestMethod]
    public void CallerFixturePreparationDoesNotRetainInvalidFactsFromEarlierCases()
    {
        EnvironmentStoreTestData.AssignValidFacts();
        EnvironmentStoreTestData.Values.OperatingSystem = "invalid-os";
        EnvironmentStoreTestData.Values.HostName = "bad" + (char)0xd800;
        EnvironmentStoreTestData.Values.ProcessId = -1;
        var input = ConfigurationFixtureAdapter.Case(ConfigurationFixtureAdapter.ReadDocument(ConfigurationFixtureAdapter.FindFixture()), "CFG-RESOLVE-DEFAULTS")["inputs"]!.AsObject();
        (PreparedConfiguration? prepared, CoreConfigurationException? error) = ConfigurationValidationAdapter.Execute(input, Path.GetTempPath);
        Assert.IsNull(error);
        Assert.IsNotNull(prepared);
        Assert.AreEqual("unknown", EnvironmentStoreTestData.Values.OperatingSystem);
        Assert.IsNull(EnvironmentStoreTestData.Values.HostName);
        Assert.IsNull(EnvironmentStoreTestData.Values.ProcessId);
    }

    [TestMethod]
    public void UndefinedGlobalLevelIsReportedOnceWhenThresholdsInherit()
    {
        var logging = new LoggingOptions { Level = (CoreLogLevel)77 };
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => logging.Prepare(Path.GetTempPath()));
        Assert.AreEqual("LoggingOptions.Level", error.Issues.Single().Key);
    }
}
