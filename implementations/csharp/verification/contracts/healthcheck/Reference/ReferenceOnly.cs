// HC00 nonshipping facade. These declarations describe future behavior only.
namespace CIS.Healthcheck;

internal static class ReferenceOnly
{
    internal static NotSupportedException Unavailable() =>
        new("HC00 reference-only contract facade; runtime behavior is not implemented.");
}
