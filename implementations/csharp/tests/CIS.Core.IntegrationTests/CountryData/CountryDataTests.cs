using System.Net;
using System.Net.Http.Json;
using CIS.Examples.CountryData;
using CIS.Healthcheck;
using CIS.Tests.IntegrationTests.Metrics;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.CountryData;

[TestClass]
[DoNotParallelize]
public sealed class CountryDataTests
{
    private const string Malta = "{\"error\":false,\"msg\":\"ok\",\"data\":[{\"name\":\"Malta\",\"Iso2\":\"mt\",\"Iso3\":\"mlt\"}]}";
    private const string Expanded = "{\"error\":false,\"data\":[{\"name\":\"Malta\",\"Iso2\":\"MT\",\"Iso3\":\"MLT\"},{\"name\":\"Trinidad and Tobago\",\"Iso2\":\"TT\",\"Iso3\":\"TTO\"}]}";

    [TestMethod]
    public void SnapshotCopiesCanonicalRecordsAndAliasesWithoutMutatingProviderData()
    {
        var row = new ProviderCountry { Name = "Malta", Iso2 = "mt", Iso3 = "mlt" };
        ProviderCountry?[] rows = [row];
        var published = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        Assert.IsTrue(CountrySnapshot.TryCreate(rows, published, out CountrySnapshot snapshot));
        rows[0] = new ProviderCountry { Name = "Other", Iso2 = "ZZ", Iso3 = "ZZZ" };
        Assert.AreEqual(1, snapshot.Count);
        Assert.AreEqual(3, snapshot.Index.Count);
        Assert.AreEqual(published, snapshot.PublishedAt);
        Assert.AreSame(snapshot.Index["Malta"], snapshot.Index["MT"]);
        Assert.AreSame(snapshot.Index["MT"], snapshot.Index["mlt"]);
        Assert.AreEqual(new Country("Malta", "MT", "MLT"), snapshot.Index["malta"]);
        Assert.AreEqual("mt", row.Iso2);
        var defaults = new CountrySettings();
        Assert.AreEqual(TimeSpan.FromHours(1), defaults.RefreshInterval);
        Assert.AreEqual(TimeSpan.FromSeconds(30), defaults.RequestTimeout);
        Assert.AreEqual("transient-http", defaults.RetryPolicyName);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("empty")]
    [DataRow("null-row")]
    [DataRow("blank-name")]
    [DataRow("bad-iso2")]
    [DataRow("bad-iso3")]
    [DataRow("duplicate")]
    [DataRow("alias-conflict")]
    public void InvalidCandidateNeverPublishesAPartialSnapshot(string scenario)
    {
        var valid = new ProviderCountry { Name = "Malta", Iso2 = "MT", Iso3 = "MLT" };
        ProviderCountry?[]? rows = scenario switch
        {
            "null" => null, "empty" => [], "null-row" => [valid, null],
            "blank-name" => [valid, new() { Name = " ", Iso2 = "TT", Iso3 = "TTO" }],
            "bad-iso2" => [valid, new() { Name = "Other", Iso2 = "M1", Iso3 = "OTH" }],
            "bad-iso3" => [valid, new() { Name = "Other", Iso2 = "OT", Iso3 = "OT" }],
            "duplicate" => [valid, valid],
            _ => [valid, new() { Name = "mLt", Iso2 = "OT", Iso3 = "OTH" }]
        };
        Assert.IsFalse(CountrySnapshot.TryCreate(rows, DateTimeOffset.UtcNow, out CountrySnapshot rejected));
        Assert.AreEqual(0, rejected.Count);
        Assert.AreEqual(0, rejected.Index.Count);
        Assert.IsNull(rejected.PublishedAt);
    }

    [TestMethod]
    public async Task HeldInitialLoadServesManagementAndOpensAliasesOnlyAfterAtomicPublicationWithoutLookupIo()
    {
        int calls = 0;
        var entered = CountryDataTestHost.Signal();
        var release = CountryDataTestHost.Signal();
        await using var origin = await RestHttpLocalServer.StartAsync(async context =>
        {
            Interlocked.Increment(ref calls); entered.TrySetResult();
            await release.Task;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(Expanded, context.RequestAborted);
        });
        await using var host = new CountryDataTestHost(origin.Address);
        host.Release.Add(() => release.TrySetResult());
        Task start = host.App.StartAsync();
        try
        {
            await entered.Task.WaitAsync(CountryDataTestHost.Deadline);
            Assert.IsFalse(start.IsCompleted);
            using HttpClient client = host.Client();
            await Status(client, "/_health/ready", HttpStatusCode.InternalServerError);
            await Status(client, "/country/MT", HttpStatusCode.ServiceUnavailable);
            string before = await CountryDataTestHost.ScrapeAsync(client);
            Assert.AreEqual(0d, CountryDataTestHost.Gauge(before, "app_country_cache_entries"));
            Assert.IsFalse(CollectionText.Samples(before).Any(line => line.StartsWith("app_country_cache_last_success_timestamp", StringComparison.Ordinal)));
            Assert.AreEqual(1, Volatile.Read(ref calls));
            release.TrySetResult();
            await start.WaitAsync(CountryDataTestHost.Deadline);
            foreach (string key in new[] { "MT", "MLT", "Malta", "mt", "mlt", "malta", "%20Malta%20" })
            { Assert.AreEqual(new Country("Malta", "MT", "MLT"), await client.GetFromJsonAsync<Country>("/country/" + key)); }
            Assert.AreEqual(new Country("Trinidad and Tobago", "TT", "TTO"),
                await client.GetFromJsonAsync<Country>("/country/Trinidad%20and%20Tobago"));
            await ErrorCode(client, "/country/%20%20", HttpStatusCode.BadRequest, "country.invalid_identifier");
            await ErrorCode(client, "/country/not-a-country", HttpStatusCode.NotFound, "country.not_found");
            await Status(client, "/_health/ready", HttpStatusCode.OK);
            string after = await CountryDataTestHost.ScrapeAsync(client);
            Assert.AreEqual(2d, CountryDataTestHost.Gauge(after, "app_country_cache_entries"));
            Assert.AreEqual(host.Clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000d,
                CountryDataTestHost.Gauge(after, "app_country_cache_last_success_timestamp_seconds"));
            CollectionAssert.AreEquivalent(new[] { "app_country_cache_entries", "app_country_cache_last_success_timestamp_seconds" },
                CollectionText.Samples(after).Where(line => line.StartsWith("app_country_cache_", StringComparison.Ordinal))
                    .Select(line => line[..line.IndexOf('{')]).ToArray());
            Assert.AreEqual(1, Volatile.Read(ref calls), "Lookups, misses and repeated management reads perform no provider I/O.");
            Assert.AreSame(host.App.Services.GetRequiredService<CountryCatalog>(), host.Catalog);
            host.Clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromTicks(1));
            Assert.AreEqual(1, Volatile.Read(ref calls), "Default first delay must be one hour, without duplicate priming.");
            host.Clock.Advance(TimeSpan.FromTicks(1));
            await host.SettledAsync(1);
            Assert.AreEqual(2, Volatile.Read(ref calls));
            host.Clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromTicks(1));
            Assert.AreEqual(2, Volatile.Read(ref calls), "The subsequent default interval must also be one hour.");
            host.Clock.Advance(TimeSpan.FromTicks(1));
            await host.SettledAsync(2);
            Assert.AreEqual(3, Volatile.Read(ref calls));
        }
        finally { release.TrySetResult(); await ObserveStart(start); }
    }

    [TestMethod]
    [DataRow("business")]
    [DataRow("http")]
    [DataRow("malformed")]
    [DataRow("empty")]
    [DataRow("missing-error")]
    public async Task FailedInitialPublicationNeverSignalsStartupCompletionOrUsableData(string scenario)
    {
        await using var origin = await RestHttpLocalServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = scenario == "http" ? 500 : 200;
            await context.Response.WriteAsync(scenario switch
            {
                "business" => "{\"error\":true,\"msg\":\"declined\",\"data\":[]}",
                "malformed" => "{bad json", "empty" => "{\"error\":false,\"data\":[]}",
                "missing-error" => "{\"data\":[{\"name\":\"Malta\",\"Iso2\":\"MT\",\"Iso3\":\"MLT\"}]}", _ => "{}"
            }, context.RequestAborted);
        });
        await using var host = new CountryDataTestHost(origin.Address);
        Task start = host.App.StartAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => start.WaitAsync(CountryDataTestHost.Deadline));
        Assert.IsTrue(start.IsFaulted);
        Assert.IsFalse(host.Health.CaptureSnapshot().Ready);
        Assert.IsFalse(host.Catalog.TryGet("MT", out _));
        Assert.AreEqual(0L, host.Availability().Outcomes!.Successful);
        Assert.IsNull(host.Availability().Outcomes!.Execution, "Failed initial loading must not run a recurring callback.");
    }

    [TestMethod]
    public async Task ActualHeartbeatDoesNotOverlapAndFailuresRetainBothPublishedDataAndTimestamp()
    {
        int calls = 0;
        var secondEntered = CountryDataTestHost.Signal();
        var releaseSecond = CountryDataTestHost.Signal();
        await using var origin = await RestHttpLocalServer.StartAsync(async context =>
        {
            int call = Interlocked.Increment(ref calls);
            context.Response.ContentType = "application/json";
            if (call == 2) { secondEntered.TrySetResult(); await releaseSecond.Task; }
            string body = call switch
            {
                1 => Malta, 2 => Expanded,
                3 => "{\"error\":true,\"msg\":\"declined\",\"data\":[]}",
                _ => "{\"error\":false,\"data\":[{\"name\":\"Malta\",\"Iso2\":\"MT\",\"Iso3\":\"MLT\"},{\"name\":\"mLt\",\"Iso2\":\"OT\",\"Iso3\":\"OTH\"}]}"
            };
            await context.Response.WriteAsync(body, context.RequestAborted);
        });
        await using var host = new CountryDataTestHost(origin.Address, TimeSpan.FromSeconds(2), timeout: TimeSpan.FromHours(1));
        host.Release.Add(() => releaseSecond.TrySetResult());
        await host.App.StartAsync().WaitAsync(CountryDataTestHost.Deadline);
        using HttpClient client = host.Client();
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        await secondEntered.Task.WaitAsync(CountryDataTestHost.Deadline);
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.AreEqual(2, Volatile.Read(ref calls), "A running refresh must not overlap or accumulate catch-up work.");
        Assert.IsTrue(host.Catalog.TryGet("MT", out Country? first));
        Assert.AreEqual(new Country("Malta", "MT", "MLT"), first);
        releaseSecond.TrySetResult();
        await host.SettledAsync(1);
        string accepted = await CountryDataTestHost.ScrapeAsync(client);
        double publishedAt = CountryDataTestHost.Gauge(accepted, "app_country_cache_last_success_timestamp_seconds");
        Assert.AreEqual(2d, CountryDataTestHost.Gauge(accepted, "app_country_cache_entries"));
        Assert.IsTrue(host.Catalog.TryGet("TTO", out _));
        Assert.AreEqual(2L, host.Availability().Outcomes!.Successful, "Only the initial publication and one actual callback count as success.");
        for (int failure = 0; failure < 2; failure++)
        {
            host.Clock.Advance(TimeSpan.FromSeconds(2));
            await host.SettledAsync(failure + 2);
            string retained = await CountryDataTestHost.ScrapeAsync(client);
            Assert.AreEqual(publishedAt, CountryDataTestHost.Gauge(retained, "app_country_cache_last_success_timestamp_seconds"));
            Assert.AreEqual(2d, CountryDataTestHost.Gauge(retained, "app_country_cache_entries"));
            Assert.IsTrue(host.Catalog.TryGet("TTO", out _));
            Assert.AreEqual(HealthExecutionOutcome.Failed, host.Availability().Outcomes!.LastOutcome);
            Assert.AreEqual(MonitorState.Healthy, host.Availability().State);
            await Status(client, "/_health/ready", HttpStatusCode.OK);
        }
        Assert.AreEqual(4, Volatile.Read(ref calls));
    }

    [TestMethod]
    public async Task StoppingCancelsTheActualHttpBodyAndJoinsHeartbeatAndResponseCleanupBeforeCatalogDisposal()
    {
        int calls = 0;
        var bodyEntered = CountryDataTestHost.Signal();
        var requestCanceled = CountryDataTestHost.Signal();
        var serverRelease = CountryDataTestHost.Signal();
        var cleanup = new CountryResponseCleanup();
        await using var origin = await RestHttpLocalServer.StartAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            if (Interlocked.Increment(ref calls) == 1) { await context.Response.WriteAsync(Malta); return; }
            await context.Response.WriteAsync("{\"error\":false,\"data\":[", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            bodyEntered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { requestCanceled.TrySetResult(); }
            await serverRelease.Task;
        });
        await using var host = new CountryDataTestHost(origin.Address, TimeSpan.FromSeconds(2),
            configureClient: options => options.Handlers.Add(cleanup));
        host.Release.Add(() => cleanup.Release.TrySetResult());
        host.Release.Add(() => serverRelease.TrySetResult());
        Task? stop = null, disposal = null;
        try
        {
            await host.App.StartAsync().WaitAsync(CountryDataTestHost.Deadline);
            host.Clock.Advance(TimeSpan.FromSeconds(2));
            await bodyEntered.Task.WaitAsync(CountryDataTestHost.Deadline);
            await cleanup.BodyReadPending.Task.WaitAsync(CountryDataTestHost.Deadline);
            stop = host.App.StopAsync();
            await cleanup.Entered.Task.WaitAsync(CountryDataTestHost.Deadline);
            disposal = host.Catalog.DisposeAsync().AsTask();
            Assert.AreSame(disposal, host.Catalog.DisposeAsync().AsTask());
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(disposal.IsCompleted);
            Assert.IsFalse(host.Health.CaptureSnapshot().Ready);
            Assert.AreEqual(1L, host.Availability().Outcomes!.Successful);
            cleanup.Release.TrySetResult(); serverRelease.TrySetResult();
            await requestCanceled.Task.WaitAsync(CountryDataTestHost.Deadline);
            await stop.WaitAsync(CountryDataTestHost.Deadline);
            await disposal.WaitAsync(CountryDataTestHost.Deadline);
            Assert.AreEqual(2, Volatile.Read(ref calls));
        }
        finally
        {
            cleanup.Release.TrySetResult(); serverRelease.TrySetResult();
            if (stop is not null) { await stop.WaitAsync(CountryDataTestHost.Deadline); }
            if (disposal is not null) { await disposal.WaitAsync(CountryDataTestHost.Deadline); }
        }
    }

    [TestMethod]
    public async Task ForeignCancellationIsNotSwallowedWhenStoppingArrivesDuringActualCleanup()
    {
        using var foreign = new CancellationTokenSource();
        var original = new OperationCanceledException("foreign callback fault", foreign.Token);
        var cleanup = new CountryResponseCleanup();
        await using var origin = await RestHttpLocalServer.StartAsync(context =>
        { context.Response.ContentType = "application/json"; return context.Response.WriteAsync(Malta); });
        await using var host = new CountryDataTestHost(origin.Address, configureClient: options =>
        { options.Handlers.Add(new CountrySecondCallFault(original)); options.Handlers.Add(cleanup); });
        host.Release.Add(() => cleanup.Release.TrySetResult());
        await host.App.StartAsync().WaitAsync(CountryDataTestHost.Deadline);
        Task<bool> refresh = host.Catalog.OnElapsedAsync(CancellationToken.None);
        try
        {
            await cleanup.Entered.Task.WaitAsync(CountryDataTestHost.Deadline);
            host.App.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
            Assert.IsFalse(refresh.IsCompleted);
            cleanup.Release.TrySetResult();
            OperationCanceledException observed = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                () => refresh.WaitAsync(CountryDataTestHost.Deadline));
            Assert.AreSame(original, observed);
            Assert.IsFalse(refresh.IsCompletedSuccessfully, "The original exception must escape rather than becoming a false callback result.");
        }
        finally { cleanup.Release.TrySetResult(); await ObserveStart(refresh); }
    }

    [TestMethod]
    public async Task DisabledMetricsLeavesRealInitializationAndCountryLookupUnchanged()
    {
        await using var origin = await RestHttpLocalServer.StartAsync(context =>
        { context.Response.ContentType = "application/json"; return context.Response.WriteAsync(Malta); });
        await using var host = new CountryDataTestHost(origin.Address, metrics: false);
        await host.App.StartAsync().WaitAsync(CountryDataTestHost.Deadline);
        using HttpClient client = host.Client();
        Assert.AreEqual(new Country("Malta", "MT", "MLT"), await client.GetFromJsonAsync<Country>("/country/MT"));
        await Status(client, "/_health/ready", HttpStatusCode.OK);
        await Status(client, "/_metrics", HttpStatusCode.NotFound);
    }

    private static async Task Status(HttpClient client, string path, HttpStatusCode status)
    { using HttpResponseMessage response = await client.GetAsync(path); Assert.AreEqual(status, response.StatusCode, path); }
    private static async Task ErrorCode(HttpClient client, string path, HttpStatusCode status, string code)
    {
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.AreEqual(status, response.StatusCode);
        using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, body.RootElement.GetProperty("code").GetString());
    }
    private static async Task ObserveStart(Task task)
    { try { await task.WaitAsync(CountryDataTestHost.Deadline); } catch (Exception) when (task.IsCompleted) { } }
}
