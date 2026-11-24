using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Management.Wire;
using CIS.RestClient;

namespace CIS.Tests.UnitTests.Management;

internal static class ManagementConfigurationClientTestSupport
{
    internal const string AppId = "11111111-1111-1111-1111-111111111111";
    internal const string Registration = "22222222-2222-2222-2222-222222222222";
    internal const string Source = "44444444-4444-4444-4444-444444444444";
    internal const string Publication = "66666666-6666-6666-6666-666666666666";
    internal const string Release = "77777777-7777-7777-7777-777777777777";
    internal const string Authority = "88888888-8888-8888-8888-888888888888";
    internal const string Time = "2026-11-24T00:12:34.1234567Z";
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    internal static ManagementConfigurationSnapshotTarget Target()
        => new(Authority, AppId, "Development", Registration, "sample.settings", "1", "ExampleHost", "canary");

    internal static ManagementConfigurationClientOptions Options(string alias = "configuration")
        => new(alias)
        {
            BaseUrl = "https://service.invalid/tenant/control"
        };

    internal static RestClientOptions Native(ManagementConfigurationClientOptions options, IRestExchangeHandler? handler = null)
    {
        var native = new RestClientOptions(options.RestClientName) { LogCalls = false };
        native.ConfigureCisManagementConfigurationRestClient(options);
        if (handler is not null)
        {
            native.Handlers.Add(handler);
        }
        return native;
    }

    internal static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    internal static T Value<T>(string id) where T : class
        => ManagementConfigurationWire.DecodeResponse<T>(Bytes(Case(id)))
            ?? throw new InvalidOperationException("A declared valid conformance response is required.");

    internal static T Command<T>(string id) where T : class
        => ManagementConfigurationWire.DecodeRequest<T>(Bytes(Case(id)))
            ?? throw new InvalidOperationException("A declared valid conformance command is required.");

    internal static string Case(string id)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "management", "v1", "configuration-cases.json");
            if (File.Exists(path))
            {
                using JsonDocument cases = JsonDocument.Parse(File.ReadAllBytes(path));
                return cases.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id)
                    .GetProperty("json").GetString()!;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Independent configuration cases are required.");
    }

    internal sealed class Handler(Func<RestRequestContext, CancellationToken, ValueTask<IRestResponse>> handle) : IRestExchangeHandler
    {
        public ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken cancellationToken)
            => handle(request, cancellationToken);
    }

    internal sealed class Response(int status, Stream body, string? etag = null, string? media = "application/json") : IRestResponse
    {
        public int HttpStatusCode => status;
        public Stream Body => body;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; } = HeadersFor(etag, media);
        public ValueTask DisposeAsync() => body.DisposeAsync();

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> HeadersFor(string? etag, string? media)
        {
            var headers = new Dictionary<string, IReadOnlyList<string>>();
            if (media is not null)
            {
                headers.Add("Content-Type", [media]);
            }
            if (etag is not null)
            {
                headers.Add("ETag", [etag]);
            }
            return headers;
        }
    }
}
