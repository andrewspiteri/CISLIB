using CIS.RestClient.Http;

namespace CIS.RestClient;

internal sealed record RestClientBinding(
    RestClientSnapshot Options,
    RestHttpTransport Transport,
    bool OwnsTransport);
