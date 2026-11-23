using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CIS.Management;
using CIS.Management.Wire;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementServiceTests
{
    [TestMethod]
    public async Task ProductHostRequiresRealSignedMappedCredentialsAndKeepsOperationalListenersSeparate()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        using RSA otherKey = RSA.Create(2048);
        foreach (string token in new[] { fixture.Token("workload-a", key: otherKey),
            fixture.Token("workload-a", issuer: "https://untrusted.invalid"),
            fixture.Token("workload-a", audience: "another-service"),
            fixture.Token("workload-a", expiration: DateTimeOffset.UtcNow.AddMinutes(-1)), fixture.Token("unmapped") })
        {
            using HttpResponseMessage response = await fixture.Client(explicitToken: token).GetAsync("service-info");
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.AreEqual("management.unauthenticated", (await Decode<ManagementProblem>(response)).Code);
        }
        using (HttpResponseMessage anonymous = await fixture.Client(subject: null).GetAsync("service-info"))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
        using (HttpResponseMessage valid = await fixture.Client("operator-a").GetAsync("service-info"))
        {
            Assert.AreEqual(HttpStatusCode.OK, valid.StatusCode);
            ManagementServiceInfo info = await Decode<ManagementServiceInfo>(valid);
            Assert.IsTrue(info.Features.Any(feature => feature.OperationId == "getApplication"));
            Assert.IsFalse(info.Features.Any(feature => feature.OperationId.Contains("configuration", StringComparison.OrdinalIgnoreCase)));
        }
        using HttpClient operational = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        string origin = "http://127.0.0.1:" + fixture.OperationalPorts[0].ToString(CultureInfo.InvariantCulture);
        using (HttpResponseMessage health = await operational.GetAsync(origin + "/_health"))
        {
            Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        }
        using (HttpResponseMessage business = await operational.GetAsync(origin + "/platform/management/v1/service-info"))
        {
            Assert.IsFalse(business.IsSuccessStatusCode);
        }
        using (HttpResponseMessage exposedHealth = await fixture.Client().GetAsync(fixture.Origins[0] + "/_health"))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, exposedHealth.StatusCode);
        }
        using var forged = new HttpRequestMessage(HttpMethod.Get, "service-info");
        forged.Headers.Add("X-CIS-Application", "managed-a");
        forged.Headers.Add("X-CIS-Subject", "workload-a");
        using HttpResponseMessage rejected = await fixture.Client(subject: null).SendAsync(forged);
        Assert.AreEqual(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [TestMethod]
    public async Task ExactResourceGrantsFilterListsAndHideDirectReadsInventoryAndHintsAcrossReplicas()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HttpClient reader = fixture.Client("reader", replica: 1);
        using (HttpResponseMessage response = await reader.GetAsync("applications?pageSize=1"))
        {
            ManagementPage<ManagementApplication> page = await Decode<ManagementPage<ManagementApplication>>(response);
            Assert.HasCount(1, page.Items);
            Assert.AreEqual(fixture.ApplicationA, page.Items[0].ApplicationId);
            Assert.IsNull(page.NextCursor, "Hidden application rows cannot create continuation evidence.");
        }
        foreach (string suffix in new[] { "", "/instances", "/inventory", "/capabilities" })
        {
            using HttpResponseMessage hidden = await reader.GetAsync("applications/" + fixture.ApplicationB + suffix);
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        using (HttpResponseMessage response = await reader.GetAsync("applications/" + fixture.ApplicationA + "/capabilities"))
        {
            ManagementCapabilities capabilities = await Decode<ManagementCapabilities>(response);
            Assert.IsTrue(capabilities.CallerHints.Single(hint => hint.OperationId == "getApplication").Allowed);
            Assert.IsFalse(capabilities.CallerHints.Single(hint => hint.OperationId == "registerInstance").Allowed);
        }
        using HttpResponseMessage wrongEnvironment = await Put(fixture.Client("wrong-env"), "registrations/" + Guid.NewGuid().ToString("D"),
            ManagementHostFixture.Request("wrong-environment"));
        Assert.AreEqual(HttpStatusCode.Forbidden, wrongEnvironment.StatusCode);
    }

    [TestMethod]
    public async Task RegistrationAndIndependentReportReceiptsRemainOriginalAcrossReplicaRetriesAndTerminalClose()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HttpClient first = fixture.Client();
        HttpClient second = fixture.Client(replica: 1);
        string id = Guid.NewGuid().ToString("D");
        ManagementRegistrationRequest request = ManagementHostFixture.Request("reported-body-label");
        using HttpResponseMessage accepted = await Put(first, "registrations/" + id, request);
        using HttpResponseMessage repeated = await Put(second, "registrations/" + id, request);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        Assert.AreEqual(await Decode<ManagementRegistrationReceipt>(accepted), await Decode<ManagementRegistrationReceipt>(repeated));
        using HttpResponseMessage changed = await Put(first, "registrations/" + id, request with
        {
            ReportedInstanceId = "changed"
        });
        Assert.AreEqual(HttpStatusCode.Conflict, changed.StatusCode);
        using HttpResponseMessage foreign = await Put(fixture.Client("workload-b"), "registrations/" + id, request);
        Assert.AreEqual(HttpStatusCode.Conflict, foreign.StatusCode);
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        var observation = new ManagementObservation("9007199254740993", timestamp, "ready", "healthy");
        using HttpResponseMessage report = await Put(first, "registrations/" + id + "/observation", observation);
        using HttpResponseMessage replay = await Put(second, "registrations/" + id + "/observation", observation);
        ManagementReportReceipt original = await Decode<ManagementReportReceipt>(report);
        ManagementReportReceipt duplicate = await Decode<ManagementReportReceipt>(replay);
        Assert.AreEqual(original.ReceivedAtUtc, duplicate.ReceivedAtUtc);
        Assert.AreEqual("replayed", duplicate.Disposition);
        using HttpResponseMessage obsolete = await Put(first, "registrations/" + id + "/observation", observation with
        {
            Sequence = "1"
        });
        Assert.AreEqual(HttpStatusCode.Conflict, obsolete.StatusCode);
        Assert.AreEqual("management.sequence.obsolete", (await Decode<ManagementProblem>(obsolete)).Code);
        using HttpResponseMessage conflict = await Put(second, "registrations/" + id + "/observation", observation with
        {
            Health = "unhealthy"
        });
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        var inventory = new ManagementInventorySnapshot("1", timestamp, [], [], []);
        using HttpResponseMessage uploaded = await Put(second, "registrations/" + id + "/inventory", inventory);
        Assert.AreEqual("1", (await Decode<ManagementReportReceipt>(uploaded)).AcceptedSequence);
        using HttpResponseMessage closed = await first.DeleteAsync("registrations/" + id);
        using HttpResponseMessage closeReplay = await second.DeleteAsync("registrations/" + id);
        Assert.AreEqual(await Decode<ManagementCloseReceipt>(closed), await Decode<ManagementCloseReceipt>(closeReplay));
        using HttpResponseMessage afterClose = await Put(second, "registrations/" + id + "/observation", observation with
        {
            Sequence = "9007199254740994"
        });
        Assert.AreEqual(HttpStatusCode.Conflict, afterClose.StatusCode);
        using HttpResponseMessage instances = await first.GetAsync("applications/" + fixture.ApplicationA + "/instances");
        ManagementInstance instance = (await Decode<ManagementPage<ManagementInstance>>(instances)).Items.Single(value => value.RegistrationId == id);
        Assert.AreEqual("closed", instance.Presence);
        Assert.AreEqual("reported-body-label", instance.ReportedInstanceId);
        Assert.AreEqual("notProvided", instance.InstanceEvidence.Kind, "Body metadata cannot become native verified instance evidence.");
    }

    [TestMethod]
    public async Task OpaqueCursorUsesLastReturnedItemAndCannotMoveBetweenActorsEndpointsOrPageSizes()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        HttpClient client = fixture.Client();
        using HttpResponseMessage registered = await Put(client, "registrations/" + Guid.NewGuid().ToString("D"), ManagementHostFixture.Request("second"));
        Assert.AreEqual(HttpStatusCode.OK, registered.StatusCode);
        string path = "applications/" + fixture.ApplicationA + "/instances?pageSize=1";
        using HttpResponseMessage first = await client.GetAsync(path);
        ManagementPage<ManagementInstance> page = await Decode<ManagementPage<ManagementInstance>>(first);
        Assert.IsNotNull(page.NextCursor);
        string encoded = Uri.EscapeDataString(page.NextCursor);
        using HttpResponseMessage next = await fixture.Client(replica: 1).GetAsync(path + "&cursor=" + encoded);
        ManagementPage<ManagementInstance> continuation = await Decode<ManagementPage<ManagementInstance>>(next);
        Assert.HasCount(1, continuation.Items);
        Assert.AreNotEqual(page.Items[0].RegistrationId, continuation.Items[0].RegistrationId);
        Assert.IsNull(continuation.NextCursor);
        foreach ((HttpClient caller, string url) in new[]
        {
            (fixture.Client("reader"), path + "&cursor=" + encoded),
            (client, path.Replace("pageSize=1", "pageSize=2", StringComparison.Ordinal) + "&cursor=" + encoded),
            (client, path.Replace("/instances", "/inventory", StringComparison.Ordinal) + "&cursor=" + encoded)
        })
        {
            using HttpResponseMessage invalid = await caller.GetAsync(url);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.AreEqual("management.cursor.invalid", (await Decode<ManagementProblem>(invalid)).Code);
        }
    }

    [TestMethod]
    public async Task ClosedRequestShapesVersionsAndActualBodyBytesAreRejectedWithSafeProblems()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        HttpClient client = fixture.Client();
        string route = "registrations/" + Guid.NewGuid().ToString("D");
        string valid = Encoding.UTF8.GetString(ManagementWire.EncodeRequest(ManagementHostFixture.Request("instance")));
        foreach ((string body, int status, string code) in new[]
        {
            (valid[..^1] + ",\"unknown\":true}", 400, "management.invalid_request"),
            (valid.Replace("\"protocolMajor\":1", "\"protocolMajor\":2", StringComparison.Ordinal), 400, "management.unsupported_version"),
            (new string(' ', 65537), 413, "management.request_too_large")
        })
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PutAsync(route, content);
            Assert.AreEqual(status, (int)response.StatusCode);
            Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            ManagementProblem problem = await Decode<ManagementProblem>(response);
            Assert.AreEqual(code, problem.Code);
            Assert.IsNull(problem.Operation);
        }
    }

    internal static async Task<HttpResponseMessage> Put<T>(HttpClient client, string path, T request) where T : class
    {
        using var content = new ByteArrayContent(ManagementWire.EncodeRequest(request));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PutAsync(path, content);
    }

    internal static async Task<T> Decode<T>(HttpResponseMessage response) where T : class
    {
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        T? value = ManagementWire.DecodeResponse<T>(bytes);
        Assert.IsNotNull(value, "Expected a valid management response for status " + (int)response.StatusCode + ": " + Encoding.UTF8.GetString(bytes));
        return value;
    }
}
