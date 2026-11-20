using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.RestClient.Http;
using CIS.Security.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Security.Entra;

/// <summary>Binds a selected workload profile to the existing REST owner and final authentication callback.</summary>
public static class EntraRestClientExtensions
{
    /// <summary>Registers one named REST client using the actual root policy's immutable profile/origin binding.</summary>
    /// <remarks>Register the common policy and AddCisEntraSecurity for the same alias. Runtime activation creates no credential and performs no network calls; sequential Startup activates the selected SDK owner.</remarks>
    public static IServiceCollection AddCisEntraRestClient(this IServiceCollection services,
        RestClientOptions options, string profile,
        Func<IServiceProvider, RestSecurityBinding, RestHttpTransport>? borrowedTransport = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        string alias = EntraText.Identifier(profile, nameof(profile), 64);
        return services.AddCisSecurityBoundRestClient(options, provider =>
        {
            SecurityPolicySnapshot policy = provider.GetRequiredService<SecurityPolicySnapshot>();
            SecurityProfile selected = policy.GetProfile(alias);
            if (selected.Kind != SecurityProfileKind.EntraWorkloadIdentity)
            {
                throw new InvalidOperationException("An Entra REST client requires the selected Entra workload profile.");
            }
            return new RestSecurityBinding(policy, alias, RestSecurityCredentialKind.RequestAuthentication);
        }, provider =>
        {
            EntraProfileRuntime runtime = provider.GetRequiredKeyedService<EntraProfileRuntime>(alias);
            return runtime.AuthenticateAsync;
        }, borrowedTransport);
    }
}
