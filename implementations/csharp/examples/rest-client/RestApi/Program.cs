using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.RestClient.Resilience;
using CIS.TypeManagement;

namespace CIS.RestClient.Examples.RestApi;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        bool selfTest = args.Contains("--self-test", StringComparer.Ordinal);
        using var budget = new CancellationTokenSource(selfTest ? TimeSpan.FromSeconds(30) : Timeout.InfiniteTimeSpan);
        CancellationToken token = budget.Token;
        WebApplication? origin = null, app = null;
        SelfTestReport? report = null;
        try
        {
            if (selfTest) { origin = await SelfTestOrigin.StartAsync(token); }
            string baseUrl = origin?.Urls.Single() ?? Environment.GetEnvironmentVariable("REST_BASE_URL")
                ?? throw new InvalidOperationException("Set REST_BASE_URL to the explicit order backend URL.");
            string accessToken = selfTest ? "example-token" : Environment.GetEnvironmentVariable("REST_ACCESS_TOKEN") ?? "example-token";
            ITypeManager prepared = await TypeManager.CreateAsync(new TypeManagerOptions(typeof(Program).Assembly), token);
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args.Where(value => value != "--self-test").ToArray());
            builder.Host.UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; });
            if (selfTest) { builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); }
            builder.Services.AddSingleton(prepared);
            builder.Services.AddCisRestClientResilience(RestRetryPolicy.TransientHttp);
            var rest = new RestClientOptions("orders")
            {
                BaseUrl = baseUrl.TrimEnd('/') + "/", ResponseMapper = new OrderResponseMapper(),
                Authenticate = async (request, cancellation) =>
                {
                    await Task.Yield(); cancellation.ThrowIfCancellationRequested();
                    request.Headers.Set("Authorization", "Bearer " + accessToken);
                }
            };
            foreach (string operation in new[] { "submit", "retry", "business", "empty", "cancel" }) { rest.Operations.Add(operation); }
            // Default None POST; GET opts into the registered policy explicitly.
            builder.Services.AddCisRestClient(rest);
            builder.Services.AddSingleton<OrderGateway>();
            builder.Services.AddCisMetrics(options => { if (selfTest) { options.ScrapeCacheDuration = TimeSpan.Zero; } });
            app = builder.Build(); app.UseRouting(); app.UseCisMetrics();
            app.MapPost("/orders", (OrderCommand command, OrderGateway gateway, CancellationToken cancellation) => gateway.SubmitAsync(command, cancellation));
            app.MapGet("/orders/{id}", (string id, OrderGateway gateway, CancellationToken cancellation) => gateway.ReadAsync(id, cancellation));
            if (selfTest) { app.MapPost("/exercise", (OrderGateway gateway, CancellationToken cancellation) => gateway.ExerciseAsync(cancellation)); }
            await app.StartAsync(token);
            if (selfTest) { report = await ObserveAsync(app, token); }
            else { await app.WaitForShutdownAsync(); }
        }
        finally
        {
            try { await StopAsync(app); }
            finally { await StopAsync(origin); }
        }
        if (report is not null)
        { Console.WriteLine(JsonSerializer.Serialize(report with { Stopped = true }, new JsonSerializerOptions(JsonSerializerDefaults.Web))); }
        return 0;
    }

    private static async Task<SelfTestReport> ObserveAsync(WebApplication app, CancellationToken token)
    {
        using var client = new HttpClient { BaseAddress = new(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(20) };
        using HttpResponseMessage response = await client.PostAsync("/exercise", null, token); response.EnsureSuccessStatusCode();
        ExerciseReport exercise = await response.Content.ReadFromJsonAsync<ExerciseReport>(token) ?? throw new InvalidOperationException("Missing exercise report.");
        using var scrape = new HttpRequestMessage(HttpMethod.Get, "/_metrics"); scrape.Headers.Accept.ParseAdd("text/plain; version=0.0.4");
        using HttpResponseMessage metrics = await client.SendAsync(scrape, token); metrics.EnsureSuccessStatusCode();
        string text = await metrics.Content.ReadAsStringAsync(token);
        double logical = Sum(text, "cis_restclient_operation_duration_seconds_count"), active = Sum(text, "cis_restclient_active_operations"), native = Sum(text, "http_client_request_duration_seconds_count");
        bool identity = Samples(text).Any() && Samples(text).All(line => line.Contains("application=\"" + Escape(EnvironmentOptions.ApplicationName) + "\"", StringComparison.Ordinal)
            && line.Contains("environment=\"" + Escape(EnvironmentOptions.EnvironmentName) + "\"", StringComparison.Ordinal));
        if (exercise != new ExerciseReport(true, 3, "orders.declined", false, true) || logical != 5 || active != 0 || native < 6 || !identity)
        { throw new InvalidOperationException("The real example behavior or exported observations did not match the contract."); }
        return new("RestClient.RestApi", exercise.PostSuccess, exercise.RetryAttempts, exercise.BusinessCode,
            exercise.EmptyHasValue, exercise.CancellationPreserved, logical, active, native, identity,
            new(RuntimeInformation.FrameworkDescription, typeof(IRestClient).Assembly.Location, typeof(MetricsOptions).Assembly.Location,
                typeof(RestClientResilienceServiceCollectionExtensions).Assembly.Location,
                AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Polly.Core").Location,
                typeof(Meter).Assembly.Location, typeof(Meter).Assembly.GetName().Version!.ToString()), false);
    }

    private static IEnumerable<string> Samples(string text) => text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(line => !line.StartsWith('#'));
    private static double Sum(string text, string name) => Samples(text).Where(line => line.StartsWith(name + "{", StringComparison.Ordinal))
        .Sum(line => double.Parse(line[(line.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture));
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static async Task StopAsync(WebApplication? app)
    { if (app is not null) { try { await app.StopAsync(CancellationToken.None); } finally { await app.DisposeAsync(); } } }
    private sealed record RuntimeReport(string Framework, string CorePath, string MetricsPath, string ResiliencePath, string PollyPath, string DiagnosticSourcePath, string DiagnosticSourceVersion);
    private sealed record SelfTestReport(string Example, bool PostSuccess, int RetryAttempts, string? BusinessCode, bool EmptyHasValue,
        bool CancellationPreserved, double LogicalCompletions, double Active, double NativeCount, bool AllSamplesHaveIdentity, RuntimeReport Runtime, bool Stopped);
}
