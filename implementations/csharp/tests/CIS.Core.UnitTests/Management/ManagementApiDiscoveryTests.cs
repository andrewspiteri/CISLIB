using System.Text;
using CIS.Management;
using CIS.Management.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementApiDiscoveryTests
{
    private const string ApplicationId = "6d2222a6-2ffd-4010-bf15-bd22a8b70f4b";

    [TestMethod]
    public void LegacyConstructorsAndDeconstructionRemainAvailableWithAdditiveVersionAdvertisement()
    {
        var original = new ManagementServiceInfo("1", [1], []);
        (string version, IReadOnlyList<int> majors, IReadOnlyList<ManagementFeature> features) = original;
        Assert.AreEqual("1", version);
        Assert.AreEqual(1, majors.Single());
        Assert.HasCount(0, features);
        Assert.IsNull(original.ApiDiscoveryVersion);
        var advertised = original with
        {
            ApiDiscoveryVersion = 1
        };
        Assert.AreEqual(1, ManagementWire.DecodeResponse<ManagementServiceInfo>(ManagementWire.EncodeResponse(advertised))!.ApiDiscoveryVersion);

        var capabilities = new ManagementCapabilities(ApplicationId, [], []);
        (string id, IReadOnlyList<ManagementFeature> apis, IReadOnlyList<ManagementOperationHint> hints) = capabilities;
        Assert.AreEqual(ApplicationId, id);
        Assert.HasCount(0, apis);
        Assert.HasCount(0, hints);
        Assert.IsNull(capabilities.ApiDiscoveryVersion);
        Assert.IsNotNull(ManagementWire.DecodeResponse<ManagementServiceInfo>(Encoding.UTF8.GetBytes(
            """{"serviceVersion":"1","protocolMajors":[1],"features":[],"apiDiscoveryVersion":2,"future":true}""")));
    }

    [TestMethod]
    public void CompletePagesValidateTheProfileUniquenessBoundsAndDetachNestedCapabilities()
    {
        var required = new List<string> { "management.applications.read" };
        var rows = new List<ManagementFeature> { new("readSomething", 1, "GET", "/something", required) };
        byte[] bytes = ManagementWire.EncodeResponse(new ManagementServiceFeaturePage("complete-v1", rows, "opaque-cursor"));
        ManagementServiceFeaturePage page = ManagementWire.DecodeResponse<ManagementServiceFeaturePage>(bytes)!;
        required.Clear();
        rows.Clear();
        Assert.AreEqual("management.applications.read", page.Items.Single().RequiredCapabilities.Single());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)page.Items[0].RequiredCapabilities).Clear());

        ManagementFeature[] maximum = Enumerable.Range(0, 64).Select(index => new ManagementFeature("operation" + index, 1, "GET", "/something", [])).ToArray();
        Assert.IsNotNull(ManagementWire.DecodeResponse<ManagementServiceFeaturePage>(ManagementWire.EncodeResponse(new ManagementServiceFeaturePage("complete-v1", maximum))));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(new ManagementServiceFeaturePage("complete-v1", [.. maximum, maximum[0]])));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(new ManagementServiceFeaturePage("complete-v1", [maximum[0], maximum[0]])));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(new ManagementServiceFeaturePage("legacy-v1", [])));
        Assert.ThrowsExactly<ArgumentException>(() => ManagementWire.EncodeResponse(new ManagementServiceInfo("1", [1], [.. maximum, new("another", 1, "GET", "/another", [])])));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("1.5")]
    [DataRow("2147483648")]
    public void AnAdvertisedVersionMustBeAnActualPositiveInt32(string version)
    {
        Assert.IsNull(ManagementWire.DecodeResponse<ManagementServiceInfo>(Encoding.UTF8.GetBytes(
            "{\"serviceVersion\":\"1\",\"protocolMajors\":[1],\"features\":[],\"apiDiscoveryVersion\":" + version + "}")));
        Assert.IsNull(ManagementWire.DecodeResponse<ManagementCapabilities>(Encoding.UTF8.GetBytes(
            "{\"applicationId\":\"" + ApplicationId + "\",\"implementedApis\":[],\"callerHints\":[],\"apiDiscoveryVersion\":" + version + "}")));
    }
}
