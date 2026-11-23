using System.Reflection;
using CIS.Configuration;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.RestClient;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CIS.Hosting;

/// <summary>Collects application registrations and existing CIS module options before preparation.</summary>
/// <remarks>Complete setup in the application callback. This object never builds a provider or starts services.</remarks>
public sealed class CISApplicationBuilder
{
    internal bool AuditEnabled
    {
        get; set;
    }

    internal bool MessagingEnabled
    {
        get; set;
    }
    internal CISApplicationBuilder(IHostApplicationBuilder host, IWebHostBuilder? webHost)
    {
        Host = host;
        WebHost = webHost;
        TypeManager = new TypeManagerOptions(Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("Select a managed entry assembly for the application."));
    }

    /// <summary>Gets the actual native builder for configuration, logging, environment and container setup.</summary>
    public IHostApplicationBuilder Host
    {
        get;
    }
    /// <summary>Gets the ordinary registration collection used by the one final provider.</summary>
    public IServiceCollection Services => Host.Services;
    /// <summary>Gets native web-host setup in web mode, otherwise null.</summary>
    public IWebHostBuilder? WebHost
    {
        get;
    }
    /// <summary>Gets or sets preparation options; its host assembly also determines Configuration scope.</summary>
    public TypeManagerOptions TypeManager
    {
        get; set;
    }
    /// <summary>Gets or sets an explicit bootstrap store/profile; null selects initialized defaults without external updates.</summary>
    public ConfigurationOptions? Configuration
    {
        get; set;
    }
    /// <summary>Gets or sets existing Healthcheck options, with the module's environment precedence.</summary>
    public HealthcheckOptions Healthcheck { get; set; } = new();
    /// <summary>Gets or sets the native predicate for detailed health reports; omitted access is denied.</summary>
    public Func<HttpContext, ValueTask<bool>>? AuthorizeHealthDetails
    {
        get; set;
    }
    /// <summary>Gets or sets Heartbeat setup; null retains EnvironmentOptions.Heartbeat.</summary>
    public HeartbeatOptions? Heartbeat
    {
        get; set;
    }
    /// <summary>Gets or sets whether automatic CIS logging is installed. Explicit provider replacements remain ordinary DI.</summary>
    public bool LoggingEnabled { get; set; } = true;
    /// <summary>Gets or sets whether the standard health service and reserved reports are installed.</summary>
    public bool HealthcheckEnabled { get; set; } = true;
    /// <summary>Gets or sets whether the standard Heartbeat runtime and bridge are installed.</summary>
    public bool HeartbeatEnabled { get; set; } = true;
    /// <summary>Gets or sets automatic discovery of supported installed CIS integration registrars.</summary>
    public bool EnableOptionalIntegrations { get; set; } = true;
    /// <summary>Gets explicit finite named REST profiles. No profiles means no REST owner; no retries are implied.</summary>
    public IList<RestClientOptions> RestClients { get; } = new List<RestClientOptions>();
}
