namespace CIS.Healthcheck.Internal;

// Inspected as compiled metadata only; this private method is never invoked.
internal static class ForbiddenHttpPolicyFixture
{
    public static bool IsHealthy() => true;
    private static string ReadScheme(object request) => ((Microsoft.AspNetCore.Http.HttpRequest)request).Scheme;
}
