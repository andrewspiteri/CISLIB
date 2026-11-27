using CIS.Management;
using CIS.Management.Hosting;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Management.ManagementConfigurationClientTestSupport;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementDiscoveryClientTests
{
    [TestMethod]
    public async Task ApplicationQueriesAndEnvironmentPagesKeepTypedTransportPrefixAndLiteralValues()
    {
        int count = 0;
        var settings = new ManagementClientOptions("discovery") { BaseUrl = "https://service.invalid/tenant/control" };
        var native = new RestClientOptions(settings.RestClientName) { LogCalls = false };
        native.ConfigureCisManagementRestClient(settings);
        native.Handlers.Add(new Handler((request, _) =>
        {
            count++;
            Assert.AreEqual("applications", request.Call.OperationName, "Application and environment discovery share the existing finite read family.");
            byte[] body;
            if (new Uri(request.Url).AbsolutePath.EndsWith("/applications", StringComparison.Ordinal))
            {
                Assert.AreEqual("https://service.invalid/tenant/control/management/v1/applications", request.Url);
                IReadOnlyList<KeyValuePair<string, string?>> query = request.Query.Snapshot();
                Assert.AreEqual(" Café%_ ", query.Single(pair => pair.Key == "query").Value);
                Assert.AreEqual("Prod+Blue", query.Single(pair => pair.Key == "environment").Value);
                Assert.AreEqual("opaque+/", query.Single(pair => pair.Key == "cursor").Value);
                body = ManagementWire.EncodeResponse(new ManagementPage<ManagementApplication>([]));
            }
            else
            {
                Assert.AreEqual("https://service.invalid/tenant/control/management/v1/environments", request.Url);
                body = ManagementWire.EncodeResponse(new ManagementPage<ManagementEnvironment>([new("Prod+Blue")]));
            }
            return ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(body)));
        }));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementClient(rest, settings);
        Assert.IsTrue((await client.QueryApplicationsAsync(new(" Café%_ ", "Prod+Blue"), new(1, "opaque+/"))).Exchange.Success);
        ManagementCallResult<ManagementPage<ManagementEnvironment>> environments = await client.ListEnvironmentsAsync(new(1));
        Assert.IsTrue(environments.Exchange.Success);
        Assert.AreEqual("Prod+Blue", environments.Exchange.Value!.Items.Single().Environment);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.QueryApplicationsAsync(new(new string('x', 129))));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.QueryApplicationsAsync(new(Environment: "")));
        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public async Task ScopedDefinitionsUseTheExactApplicationRouteAndExistingConfigurationWire()
    {
        ManagementConfigurationClientOptions settings = Options();
        RestClientOptions native = Native(settings, new Handler((request, _) =>
        {
            Assert.AreEqual("https://service.invalid/tenant/control/management/v1/applications/" + AppId + "/configuration-definitions", request.Url);
            Assert.AreEqual("2", request.Query.Snapshot().Single(pair => pair.Key == "pageSize").Value);
            return ValueTask.FromResult<IRestResponse>(new Response(200, new MemoryStream(
                ManagementConfigurationWire.EncodeResponse(new ManagementPage<ManagementConfigurationDefinition>([])))));
        }));
        await using var rest = new HttpRestClient(native);
        var client = new ManagementConfigurationClient(rest, settings);
        Assert.IsTrue((await client.GetApplicationDefinitionsAsync(AppId, new(2))).Exchange.Success);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.GetApplicationDefinitionsAsync("../another"));
    }
}
