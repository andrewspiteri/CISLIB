using System.Reflection;
using System.Text.Json;
using CIS.Configuration;
using CIS.Examples.CountryData;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Hosting;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient;
using CIS.RestClient.Resilience;
using CIS.Startup;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Metrics;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.CountryData;

internal sealed class CountryDataTestHost : IAsyncDisposable
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private readonly TemporaryDirectory directory = new();
    internal ManualTimeProvider Clock { get; }
    internal HeartbeatRuntime Heartbeat { get; }
    internal CISApplication App { get; }
    internal List<Action> Release { get; } = [];
    internal CountryCatalog Catalog => App.Services.GetRequiredService<CountryCatalog>();
    internal HealthcheckService Health => App.Services.GetRequiredService<HealthcheckService>();

    internal CountryDataTestHost(string source, TimeSpan? interval = null, bool metrics = true,
        Action<RestClientOptions>? configureClient = null, TimeSpan? timeout = null)
    {
        TimeSpan delay = interval ?? TimeSpan.FromHours(1);
        Clock = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        Heartbeat = new(new HeartbeatSettings(2), Clock, new HeartbeatThreadPoolExecutor());
        Assembly host = typeof(CountryCatalog).Assembly;
        Assembly[] selected = [host, typeof(TypeManager).Assembly, typeof(MetricsApplicationIntegration).Assembly,
            typeof(RestClientResilienceServiceCollectionExtensions).Assembly];
        foreach (Assembly assembly in selected)
        { File.Copy(assembly.Location, Path.Combine(directory.Path, Path.GetFileName(assembly.Location))); }
        string manifest = Path.Combine(directory.Path, "country.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, hostAssembly = host.FullName,
            assemblies = selected.Select(assembly => new
            { assemblyName = assembly.FullName, relativePath = Path.GetFileName(assembly.Location) })
        }));
        App = CISApplication.CreateWeb([], builder =>
        {
            builder.TypeManager = new TypeManagerOptions(host) { AssemblyManifestPath = manifest };
            builder.LoggingEnabled = false;
            builder.WebHost!.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<TimeProvider>(Clock);
            builder.Services.Replace(ServiceDescriptor.Singleton(Heartbeat));
            builder.Configuration = new ConfigurationOptions(host.GetName().Name!)
            {
                IsHealthCritical = true,
                Factories = new Dictionary<Type, Func<IConfigurationObject>>
                {
                    [typeof(CountrySettings)] = () => new CountrySettings
                    { SourceUrl = source, RefreshInterval = delay, RequestTimeout = timeout ?? TimeSpan.FromSeconds(30), RetryPolicyName = null }
                }
            };
            var client = new RestClientOptions("countries")
            { ResponseMapper = new CountryProviderMapper(), RetryPolicy = RestRetrySelection.Named(RestRetryPolicy.TransientHttp.Name) };
            client.Operations.Add("refresh");
            configureClient?.Invoke(client);
            builder.RestClients.Add(client);
            builder.Services.AddCisRestClientResilience(RestRetryPolicy.TransientHttp);
            builder.ConfigureCisMetrics(options => { options.Enabled = metrics; options.ScrapeCacheDuration = TimeSpan.Zero; });
            builder.Services.AddSingleton<CountryCatalog>();
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<CountryCatalog>());
        }, web => web.MapGet("/country/{identifier}",
            (string identifier, CountryCatalog catalog) => CountryEndpoints.Get(identifier, catalog)));
    }

    internal HttpClient Client()
        => new() { BaseAddress = new Uri(App.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()), Timeout = Deadline };

    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static async Task ObserveAsync(Func<bool> condition, string explanation)
        => Assert.IsTrue(await Task.Run(() => SpinWait.SpinUntil(condition, Deadline)).WaitAsync(Deadline), explanation);

    internal HealthMonitorSnapshot Availability()
        => Health.CaptureSnapshot().Monitors.Single(monitor => monitor.Name == "Usable country data");

    internal async Task SettledAsync(long executionId)
    {
        await ObserveAsync(() => Availability().Outcomes?.Execution?.ExecutionId == executionId &&
            Availability().Outcomes?.Execution?.Phase == HealthExecutionPhase.Settled,
            "The actual callback and final health handoff must settle. Heartbeat resets its interval origin before reporting Settled.");
        Assert.IsNull(Availability().RegistrationDiagnostic);
    }

    internal static async Task<string> ScrapeAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_metrics");
        request.Headers.Accept.ParseAdd("text/plain; version=0.0.4");
        using HttpResponseMessage response = await client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, text);
        CollectionText.AssertIdentity(text);
        return text;
    }

    internal static double Gauge(string text, string name)
        => CollectionText.Value(CollectionText.Samples(text).Single(sample => sample.StartsWith(name + "{", StringComparison.Ordinal)));

    public async ValueTask DisposeAsync()
    {
        foreach (Action release in Release) { release(); }
        // A failed/expired barrier must not dispose dependencies while actual work remains.
        await App.StopAsync().WaitAsync(Deadline);
        await App.DisposeAsync().AsTask().WaitAsync(Deadline);
        await Heartbeat.DisposeAsync().AsTask().WaitAsync(Deadline);
        directory.Dispose();
    }
}

internal sealed class CountryResponseCleanup : IRestExchangeHandler
{
    private int calls;
    internal TaskCompletionSource Entered { get; } = CountryDataTestHost.Signal();
    internal TaskCompletionSource BodyReadPending { get; } = CountryDataTestHost.Signal();
    internal TaskCompletionSource Release { get; } = CountryDataTestHost.Signal();
    public async ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken token)
    {
        IRestResponse response = await next(request, token);
        return Interlocked.Increment(ref calls) == 2 ? new HeldResponse(response, this) : response;
    }
    private sealed class HeldResponse(IRestResponse inner, CountryResponseCleanup owner) : IRestResponse
    {
        public int HttpStatusCode => inner.HttpStatusCode;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers => inner.Headers;
        private readonly Stream body = new PendingReadStream(inner.Body, owner.BodyReadPending);
        public Stream Body => body;
        public async ValueTask DisposeAsync()
        {
            owner.Entered.TrySetResult();
            await owner.Release.Task;
            // The exchange pipeline separately owns its actual next response.
        }
    }

    // Borrow the actual response body. Observe only a genuinely incomplete native
    // read; the pipeline retains disposal ownership of the original response.
    private sealed class PendingReadStream(Stream inner, TaskCompletionSource pending) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ValueTask<int> read = inner.ReadAsync(buffer, cancellationToken);
            if (!read.IsCompleted) { pending.TrySetResult(); }
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}

internal sealed class CountrySecondCallFault(Exception original) : IRestExchangeHandler
{
    private int calls;
    public async ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken token)
    {
        IRestResponse response = await next(request, token);
        if (Interlocked.Increment(ref calls) == 2) { throw original; }
        return response;
    }
}
