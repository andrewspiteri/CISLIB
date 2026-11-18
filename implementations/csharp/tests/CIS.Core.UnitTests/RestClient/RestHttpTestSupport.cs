using System.Text;
using CIS.RestClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.UnitTests.RestClient;

internal static class RestHttpTestSupport
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}

internal sealed class RestHttpObservation : IRestOperationObservation
{
    private long uploaded, downloaded;
    internal long Uploaded => Interlocked.Read(ref uploaded);
    internal long Downloaded => Interlocked.Read(ref downloaded);
    public void UploadCompleted(long bytes) => Interlocked.Add(ref uploaded, bytes);
    public void DownloadCompleted(long bytes) => Interlocked.Add(ref downloaded, bytes);
    public void Complete(RestOperationOutcome outcome, TimeSpan settledElapsed) { }
}

internal sealed class RestHttpTestResponse(int status, Stream body, string? contentType = null) : IRestResponse
{
    private int disposed;
    public int HttpStatusCode => status;
    public Stream Body => body;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; } = contentType is null
        ? new Dictionary<string, IReadOnlyList<string>>() : new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = new[] { contentType } };
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref disposed, 1) == 0) { await body.DisposeAsync(); } }
}

// Actual Kestrel, using the repository's ordinary WebApplication pattern; this
// is a bounded local transport fixture, not an alternate client or host runner.
internal sealed class RestHttpLocalServer : IAsyncDisposable
{
    private RestHttpLocalServer(WebApplication app) { App = app; }
    internal WebApplication App { get; }
    internal string Address => App.Urls.Single();
    internal static async Task<RestHttpLocalServer> StartAsync(RequestDelegate handler)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build(); app.Run(handler);
        try { await app.StartAsync().WaitAsync(RestHttpTestSupport.Deadline); return new(app); }
        catch { await app.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try { await App.StopAsync(CancellationToken.None).WaitAsync(RestHttpTestSupport.Deadline); }
        finally { await App.DisposeAsync().AsTask().WaitAsync(RestHttpTestSupport.Deadline); }
    }
}
