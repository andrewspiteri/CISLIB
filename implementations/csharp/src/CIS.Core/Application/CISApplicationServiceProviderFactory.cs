using Microsoft.Extensions.DependencyInjection;

namespace CIS.Hosting;

// Observe the actual native creation boundary. The returned provider is never
// wrapped or replaced, and capture only records its reference for failed Build.
internal sealed class CISApplicationServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
{
    private readonly DefaultServiceProviderFactory factory;
    private readonly Action<IServiceProvider> capture;

    internal CISApplicationServiceProviderFactory(ServiceProviderOptions options, Action<IServiceProvider> capture)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(capture);
        factory = new(options);
        this.capture = capture;
    }

    public IServiceCollection CreateBuilder(IServiceCollection services) => factory.CreateBuilder(services);

    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        IServiceProvider provider = factory.CreateServiceProvider(containerBuilder);
        capture(provider);
        return provider;
    }
}
