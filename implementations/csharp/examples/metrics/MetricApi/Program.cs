using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using CIS.Metrics.OpenTelemetry;
using CIS.Startup;
using CIS.Startup.Hosting;
using CIS.TypeManagement;

namespace CIS.Metrics.Examples.MetricApi;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        bool selfTest = args.Contains("--self-test", StringComparer.Ordinal);
        string[] hostArgs = args.Where(argument => argument != "--self-test").ToArray();
        // This actual discovery completes once, before the one application root.
        ITypeManager prepared = await TypeManager.CreateAsync(new TypeManagerOptions(typeof(Program).Assembly)).ConfigureAwait(false);
        HealthcheckOptions healthOptions = HealthcheckOptions.Resolve();
        await using var health = new HealthcheckService(healthOptions);
        var hold = new BootstrapGate(selfTest);
        WebApplication? app = null;
        Task? startup = null;
        SelfTestReport? report = null;
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(hostArgs);
            builder.Host.UseDefaultServiceProvider(options =>
            { options.ValidateScopes = true; options.ValidateOnBuild = true; });
            if (selfTest)
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Logging.ClearProviders();
            }
            builder.Services.AddSingleton(prepared);
            builder.Services.AddSingleton(health);
            builder.Services.AddSingleton(hold);
            builder.Services.AddSingleton<OrderService>();
            builder.Services.AddSingleton<OrderReader>();
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<OrderService>());
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<OrderReader>());
            builder.Services.AddCisStartup();
            // Both installation orders are supported. The package owns one provider.
            builder.Services.AddCisMetrics(options =>
            { if (selfTest) { options.ScrapeCacheDuration = TimeSpan.Zero; } });
            app = builder.Build();
            app.UseRouting();
            app.UseCisMetrics();
            app.UseCisHealthcheck(health, healthOptions, _ => ValueTask.FromResult(false));
            int businessCalls = 0;
            app.Use(async (context, next) =>
            {
                if (!health.CaptureSnapshot().Ready)
                { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                Interlocked.Increment(ref businessCalls);
                await next(context).ConfigureAwait(false);
            });
            app.MapPost("/orders/{id}", (string id, OrderReader orders) => orders.Submit(id));
            startup = app.StartAsync();
            if (selfTest)
            {
                report = await ObserveAsync(app, startup, hold, prepared, () => Volatile.Read(ref businessCalls)).ConfigureAwait(false);
            }
            else
            {
                await startup.ConfigureAwait(false);
                await app.WaitForShutdownAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // A timeout is not settlement. Release owned work, then join the real
            // startup/host/callback barriers before disposing the one root.
            hold.Release();
            if (app is not null)
            {
                await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
                if (startup is not null)
                {
                    try { await startup.ConfigureAwait(false); }
                    catch (Exception) when (startup.IsFaulted || startup.IsCanceled) { }
                }
                await app.DisposeAsync().ConfigureAwait(false);
            }
            await health.DisposeAsync().ConfigureAwait(false);
        }
        if (report is not null)
        { Console.WriteLine(JsonSerializer.Serialize(report with { Stopped = true }, new JsonSerializerOptions(JsonSerializerDefaults.Web))); }
        return 0;
    }

    private static async Task<SelfTestReport> ObserveAsync(WebApplication app, Task startup, BootstrapGate hold,
        ITypeManager prepared, Func<int> businessCalls)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken token = budget.Token;
        await Task.WhenAny(hold.Entered.Task, startup).WaitAsync(token).ConfigureAwait(false);
        if (!hold.Entered.Task.IsCompleted) { await startup.ConfigureAwait(false); }
        Require(hold.Entered.Task.IsCompleted && !startup.IsCompleted, "The actual required initializer must be held.");
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(5) };
        (int metricsStatus, string initial) = await MetricsAsync(client, token).ConfigureAwait(false);
        var before = new BeforeObservation(metricsStatus, await StatusAsync(client, "/_health/ready", token).ConfigureAwait(false),
            await SubmitStatusAsync(client, token).ConfigureAwait(false), businessCalls(), !startup.IsCompleted);
        Require(before == new BeforeObservation(200, 500, 503, 0, true), "Metrics and health must be reachable before business readiness.");
        Require(HasSample(initial, "cis_typemanager_assemblies") && HasSample(initial, "cis_startup_completed"),
            "Already-owned bootstrap and current startup facts must be observable without replay.");
        Require(IdentityMatches(initial), "Every initial sample must retain the resolved identity.");

        hold.Release();
        await startup.WaitAsync(token).ConfigureAwait(false);
        int ready = await StatusAsync(client, "/_health/ready", token).ConfigureAwait(false);
        int business = await SubmitStatusAsync(client, token).ConfigureAwait(false);
        (int afterMetrics, string text) = await MetricsAsync(client, token).ConfigureAwait(false);
        var after = new AfterObservation(afterMetrics, ready, business, businessCalls(),
            app.Services.GetRequiredService<OrderReader>().Started,
            ReferenceEquals(prepared, app.Services.GetRequiredService<ITypeManager>())
                && ReferenceEquals(prepared, app.Services.GetRequiredService<OrderService>().Prepared));
        Require(after == new AfterObservation(200, 200, 200, 1, true, true), "Required startup must precede the first real business operation.");
        string[] names = ["app_orders_completed_total", "app_orders_duration_seconds_bucket", "app_orders_duration_seconds_sum",
            "app_orders_duration_seconds_count", "http_server_request_duration_seconds_count", "cis_startup_operation_duration_seconds_count"];
        Require(names.All(name => HasSample(text, name)), "The actual exporter must emit custom, native and Startup families.");
        double counter = SampleValue(text, "app_orders_completed_total");
        double durations = SampleValue(text, "app_orders_duration_seconds_count");
        Require(counter == 1 && durations == 1 && IdentityMatches(text), "One business action must retain its own count, duration and identity.");
        return new("Metrics.MetricApi", before, after,
            new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName, names, counter, durations, true),
            new(RuntimeInformation.FrameworkDescription, typeof(EnvironmentOptions).Assembly.Location,
                typeof(MetricsOptions).Assembly.Location, typeof(Meter).Assembly.Location,
                typeof(Meter).Assembly.GetName().Version!.ToString()), false);
    }

    private static async Task<(int Status, string Text)> MetricsAsync(HttpClient client, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_metrics");
        request.Headers.Accept.ParseAdd("text/plain; version=0.0.4");
        using HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
    }
    private static async Task<int> StatusAsync(HttpClient client, string path, CancellationToken token)
    { using HttpResponseMessage response = await client.GetAsync(path, token).ConfigureAwait(false); return (int)response.StatusCode; }
    private static async Task<int> SubmitStatusAsync(HttpClient client, CancellationToken token)
    { using HttpResponseMessage response = await client.PostAsync("/orders/example-42", null, token).ConfigureAwait(false); return (int)response.StatusCode; }
    private static IEnumerable<string> Samples(string text) => text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(line => !line.StartsWith('#'));
    private static bool HasSample(string text, string name) => Samples(text).Any(line => line.StartsWith(name + "{", StringComparison.Ordinal));
    private static double SampleValue(string text, string name) => Samples(text).Where(line => line.StartsWith(name + "{", StringComparison.Ordinal))
        .Sum(line => double.Parse(line[(line.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture));
    private static bool IdentityMatches(string text)
    {
        string application = "application=\"" + Escape(EnvironmentOptions.ApplicationName) + "\"";
        string environment = "environment=\"" + Escape(EnvironmentOptions.EnvironmentName) + "\"";
        string[] samples = Samples(text).ToArray();
        return samples.Length != 0 && samples.All(line => line.Contains(application, StringComparison.Ordinal)
            && line.Contains(environment, StringComparison.Ordinal));
    }
    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static void Require(bool condition, string explanation)
    { if (!condition) { throw new InvalidOperationException(explanation); } }

    private sealed record BeforeObservation(int MetricsStatus, int ReadinessStatus, int BusinessStatus, int BusinessCalls, bool StartupPending);
    private sealed record AfterObservation(int MetricsStatus, int ReadinessStatus, int BusinessStatus, int BusinessCalls, bool ReaderStarted, bool SameTypeManager);
    private sealed record MetricsObservation(string Application, string Environment, string[] SampleNames, double Completed, double DurationCount, bool AllSamplesHaveIdentity);
    private sealed record RuntimeObservation(string Framework, string CorePath, string MetricsPath, string DiagnosticSourcePath, string DiagnosticSourceVersion);
    private sealed record SelfTestReport(string Example, BeforeObservation Before, AfterObservation After, MetricsObservation Metrics, RuntimeObservation Runtime, bool Stopped);
}
