using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Hosting;

/// <summary>A narrow registration hook for supported optional CIS packages already in the prepared type catalog.</summary>
/// <remarks>Implementations require a public parameterless constructor. Construction and registration perform no I/O,
/// service resolution, startup or provider creation. Each distinct type is invoked once in ordinal type order.</remarks>
public interface ICISApplicationIntegration
{
    /// <summary>Adds the package's existing services before the one final Build.</summary>
    void ConfigureServices(IServiceCollection services);
    /// <summary>Attaches its existing operational web integration before health and the business gate.</summary>
    void ConfigureWeb(WebApplication application);
}
