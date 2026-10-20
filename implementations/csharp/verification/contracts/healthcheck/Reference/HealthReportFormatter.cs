// HC00 nonshipping facade. Never execute this type as runtime evidence.
namespace CIS.Healthcheck;

/// <summary>Compile-only shape of the bounded transport formatter entry points.</summary>
public static class HealthReportFormatter
{
    public static HealthReportBuffer FormatSnapshot(
        HealthSnapshot snapshot,
        HealthReportView view,
        HealthcheckOptions options) => throw ReferenceOnly.Unavailable();

    public static HealthReportBuffer FormatPipeResponse(
        HealthSnapshot snapshot,
        HealthReportView view,
        HealthcheckOptions options) => throw ReferenceOnly.Unavailable();
}
