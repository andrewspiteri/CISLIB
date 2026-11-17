using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

// Actual Kestrel + upstream exporter only. E separately owns the product's reserved-route/HTTP policy.
internal sealed class CollectionTestHost : IAsyncDisposable
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private CollectionTestHost(WebApplication app) { App = app; }
    internal WebApplication App { get; }
    internal HttpClient Client { get; private set; } = null!;
    internal IMetrics Metrics => App.Services.GetRequiredService<IMetrics>();
    internal MetricsCollectionContext Context => App.Services.GetRequiredService<MetricsCollectionContext>();
    internal List<Action> Release { get; } = [];

    internal static CollectionTestHost Create(Action<MetricsOptions>? configure = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { EnvironmentName = "CollectionTest", ApplicationName = typeof(CollectionTestHost).Assembly.FullName });
        builder.Host.UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options =>
        {
            options.CollectCoreModules = false;
            options.ScrapeCacheDuration = TimeSpan.Zero;
            configure?.Invoke(options);
        });
        WebApplication app = builder.Build();
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            // Keep this private collection proof independent of E's business-request/probe policy.
            if (context.Request.Path == "/_metrics" && context.Features.Get<IHttpMetricsTagsFeature>() is { } feature)
            { feature.MetricsDisabled = true; }
            await next(context).ConfigureAwait(false);
        });
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
        app.MapGet("/work/{id}", (HttpContext context) =>
        {
            if (context.Features.Get<IHttpMetricsTagsFeature>() is { } feature)
            {
                feature.Tags.Add(new("application", "native-override"));
                feature.Tags.Add(new("environment", "native-override"));
                feature.Tags.Add(new("untrusted", "discard-this"));
            }
            return Results.Ok();
        });
        return new(app);
    }

    internal async Task StartAsync()
    {
        await App.StartAsync().WaitAsync(Deadline).ConfigureAwait(false);
        Client = new HttpClient { BaseAddress = new Uri(App.Urls.Single()), Timeout = Deadline };
        Assert.IsTrue(Context.IsActive);
    }

    internal async Task<string> ScrapeAsync(string accept = "text/plain; version=0.0.4")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_metrics");
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));
        using HttpResponseMessage response = await Client.SendAsync(request).WaitAsync(Deadline).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.IsTrue(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        MediaTypeWithQualityHeaderValue requested = MediaTypeWithQualityHeaderValue.Parse(accept);
        Assert.AreEqual(requested.MediaType, response.Content.Headers.ContentType?.MediaType, "The actual negotiated format must be exercised.");
        foreach (NameValueHeaderValue parameter in requested.Parameters)
        {
            // The fixed strategy has already escaped names; the exporter truthfully reports that effective scheme.
            string? expected = parameter.Name == "escaping" ? "underscores" : parameter.Value;
            Assert.AreEqual(expected, response.Content.Headers.ContentType?.Parameters
                .SingleOrDefault(item => item.Name == parameter.Name)?.Value, $"Negotiated {parameter.Name}");
        }
        return text;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Action release in Release) { release(); }
        try { await App.StopAsync(CancellationToken.None).WaitAsync(Deadline).ConfigureAwait(false); }
        finally
        {
            try { await App.DisposeAsync().ConfigureAwait(false); }
            finally { Client?.Dispose(); }
        }
    }
}

internal static class CollectionText
{
    private static readonly Regex Labels = new("""(?<key>[A-Za-z_][A-Za-z0-9_]*)=["](?<value>(?:\\.|[^"\\])*)["]""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));

    internal static string[] Samples(string text) => text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(line => !line.StartsWith('#')).ToArray();

    internal static void AssertIdentity(string text)
    {
        string[] samples = Samples(text);
        Assert.IsGreaterThan(0, samples.Length, text);
        foreach (string sample in samples)
        {
            Match[] labels = Labels.Matches(sample).Cast<Match>().ToArray();
            Match[] application = labels.Where(match => match.Groups["key"].Value == "application").ToArray();
            Match[] environment = labels.Where(match => match.Groups["key"].Value == "environment").ToArray();
            Assert.HasCount(1, application, sample);
            Assert.HasCount(1, environment, sample);
            Assert.AreEqual(Escape(EnvironmentOptions.ApplicationName), application[0].Groups["value"].Value, sample);
            Assert.AreEqual(Escape(EnvironmentOptions.EnvironmentName), environment[0].Groups["value"].Value, sample);
            Assert.IsFalse(sample.Contains("native-override", StringComparison.Ordinal), sample);
            Assert.IsFalse(sample.Contains("discard-this", StringComparison.Ordinal), sample);
        }
    }

    internal static double Value(string sample)
        => double.Parse(sample[(sample.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture);
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
