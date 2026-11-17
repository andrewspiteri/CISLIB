using System.Net;
using System.Text.Encodings.Web;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

// Ordinary real-host fixture for the product branch; it does not replace the exporter or native recorder.
internal sealed class HttpTestHost : IAsyncDisposable
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private HttpTestHost(WebApplication app, string pathBase) { App = app; PathBase = pathBase; }
    internal WebApplication App { get; }
    internal string PathBase { get; }
    internal HttpClient Client { get; private set; } = null!;
    internal IMetrics Metrics => App.Services.GetRequiredService<IMetrics>();
    internal MetricsCollectionContext Context => App.Services.GetRequiredService<MetricsCollectionContext>();
    internal HealthcheckService Health => App.Services.GetRequiredService<HealthcheckService>();
    internal List<Action> Release { get; } = [];

    internal static HttpTestHost Create(Action<MetricsOptions>? configure = null, string pathBase = "",
        string errorPath = "/error", Action<WebApplication>? beforeMetrics = null, bool businessGate = false)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { EnvironmentName = "MetricsHttpTest", ApplicationName = typeof(HttpTestHost).Assembly.FullName });
        builder.Host.UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_ => new HealthcheckService(new HealthcheckOptions()));
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoIdentity>("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddCisMetrics(options =>
        {
            options.CollectCoreModules = false;
            options.ScrapeCacheDuration = TimeSpan.Zero;
            configure?.Invoke(options);
        });
        WebApplication app = builder.Build();
        if (pathBase.Length != 0) { app.UsePathBase(pathBase); }
        app.UseExceptionHandler(new ExceptionHandlerOptions
        { ExceptionHandlingPath = errorPath, AllowStatusCode404Response = true });
        app.UseRouting();
        beforeMetrics?.Invoke(app);
        app.UseCisMetrics();
        app.UseCisMetrics(); // Repeated installation must not consume two permits or install a second branch.
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseCisHealthcheck(app.Services.GetRequiredService<HealthcheckService>(), new HealthcheckOptions());
        if (businessGate)
        {
            app.Use(async (HttpContext context, RequestDelegate next) =>
            {
                if (!app.Services.GetRequiredService<HealthcheckService>().CaptureSnapshot().Ready)
                { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                await next(context).ConfigureAwait(false);
            });
        }
        app.MapGet("/error", () => Results.StatusCode(StatusCodes.Status500InternalServerError));
        return new(app, pathBase);
    }

    internal async Task StartAsync()
    {
        await App.StartAsync().WaitAsync(Deadline);
        Client = new() { BaseAddress = new Uri(App.Urls.Single()), Timeout = Deadline };
        if (App.Services.GetRequiredService<MetricsSettings>().Enabled) { Assert.IsTrue(Context.IsActive); }
    }

    internal async Task<string> ScrapeAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync(PathBase + "/_metrics").WaitAsync(Deadline);
        string text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        CollectionText.AssertIdentity(text);
        return text;
    }

    internal static string[] DurationCounts(string text) => CollectionText.Samples(text)
        .Where(line => line.StartsWith("http_server_request_duration_seconds_count{", StringComparison.Ordinal)).ToArray();
    internal static double Count(string text, string route, int status) => DurationCounts(text)
        .Where(line => line.Contains($"http_route=\"{route}\"", StringComparison.Ordinal)
            && line.Contains($"http_response_status_code=\"{status}\"", StringComparison.Ordinal))
        .Sum(CollectionText.Value);

    internal static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        while (!condition()) { await Task.Delay(10, deadline.Token); }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Action release in Release) { release(); }
        try
        {
            await App.StopAsync(CancellationToken.None).WaitAsync(Deadline);
            // A failed wait must not authorize disposal while an actual callback still owns application state.
            await App.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
        finally
        {
            Client?.Dispose();
        }
    }

    private sealed class NoIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
