using CIS.Coordination.Hosting;
using CIS.Heartbeat.Hosting;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.RestClient.Hosting;
using CIS.Security;
using CIS.Startup;
using CIS.Startup.Hosting;
using CIS.Workflows.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CIS.Audit.Hosting;

/// <summary>Passive Audit definition/capture and explicit integration with the existing Messaging host.</summary>
public static class AuditHostingExtensions
{
    /// <summary>Registers one codec/route and trusted capture without constructing a store, transport or worker.</summary>
    /// <remarks>This definition does not register a successful production writer. Select delivery separately.</remarks>
    public static IServiceCollection ConfigureCisAudit(this IServiceCollection services, AuditOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ServiceDescriptor[] previous = services.Where(item => item.ServiceType == typeof(AuditHostingRegistration)).ToArray();
        if (previous.Length != 0)
        {
            if (previous.Length != 1 || previous[0].IsKeyedService
                || previous[0].ImplementationInstance is not AuditHostingRegistration registration
                || registration.Options.RouteAlias != options.RouteAlias
                || registration.Options.Limits.MaximumPayloadBytes != options.Limits.MaximumPayloadBytes
                || registration.Options.Limits.MaximumJsonDepth != options.Limits.MaximumJsonDepth)
            {
                throw new InvalidOperationException("Configure one immutable Audit route and payload profile.");
            }
            return services;
        }
        if (services.Any(item => item.ServiceType == typeof(MessagingComposition)))
        {
            throw new InvalidOperationException("Register the Audit definition before selecting Messaging hosting.");
        }

        var selected = new AuditHostingRegistration(options);
        services.AddCisMessageRoute(new MessageRouteDefinition<AuditRecord>(options.RouteAlias, MessageKind.Event,
            new AuditRecordCodec(options.Limits)));
        services.AddSingleton(selected);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton(_ => LocalApplicationIdentity.Capture());
        services.TryAddSingleton(root => new AuditInstrumentation(selected, root.GetService<TimeProvider>(),
            root.GetService<ILoggerFactory>()));
        services.TryAddSingleton(root => new MessagingResourceBudget(root.GetRequiredService<MessagingSnapshot>().Settings,
            root.GetRequiredService<MessagingSnapshot>().Requests.Count != 0));
        services.TryAddSingleton(root =>
        {
            var engine = new AuditCaptureEngine(root.GetRequiredService<LocalApplicationIdentity>(), options.Limits,
                root.GetRequiredService<TimeProvider>(), root.GetRequiredService<MessagingResourceBudget>(),
                root.GetRequiredService<AuditInstrumentation>());
            selected.Bind(engine);
            return engine;
        });
        services.TryAddScoped<IAuditCapture>(scope => new AuditCapture(scope.GetRequiredService<AuditCaptureEngine>(),
            scope.GetService<ICallerContextAccessor>()));
        return services;
    }

    /// <summary>Selects actual writer/lifetime integration after the complete existing Messaging registration.</summary>
    public static IServiceCollection AddCisAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AuditHostingRegistration registration = Definition(services);
        MessagingSnapshot snapshot = MessageDefinitionServiceCollectionExtensions.InspectRegistration(services);
        snapshot.Validate();
        if (!services.Any(item => !item.IsKeyedService && item.ServiceType == typeof(MessagingComposition)))
        {
            throw new InvalidOperationException("Select the matching existing Messaging host before Audit delivery.");
        }
        MessageRouteDefinition route = snapshot.Routes.Single(item => item.Alias == registration.Options.RouteAlias);
        if (route is not MessageRouteDefinition<AuditRecord> selected || selected.Codec is not AuditRecordCodec
            || selected.Kind != MessageKind.Event
            || registration.Options.Limits.MaximumPayloadBytes > snapshot.Settings.Limits.MaximumPayloadBytes
            || registration.Options.Limits.MaximumJsonDepth > snapshot.Settings.Limits.MaximumJsonDepth)
        {
            throw new InvalidOperationException("The configured Audit route and limits must fit the actual Messaging runtime.");
        }
        if (services.Any(item => item.ServiceType == typeof(AuditRuntime)))
        {
            return services;
        }
        if (services.Any(item => !item.IsKeyedService && item.ServiceType == typeof(IAuditWriter)))
        {
            throw new InvalidOperationException("Select the native Audit writer once; explicit testing writers need only capture definitions.");
        }

        services.AddSingleton(root =>
        {
            var writer = new AuditWriter(registration.Options, root.GetRequiredService<LocalApplicationIdentity>(),
                root.GetRequiredService<IMessageOutbox>(), root.GetRequiredService<IMessagingClient>());
            registration.Bind(writer);
            return writer;
        });
        services.AddSingleton<IAuditWriter>(root => root.GetRequiredService<AuditWriter>());
        services.AddSingleton(root =>
        {
            var runtime = new AuditRuntime(root.GetRequiredService<AuditCaptureEngine>(), root.GetRequiredService<AuditWriter>(),
                root.GetRequiredService<MessagingRuntime>(), root.GetService<CoordinationHostLifetime>());
            registration.Bind(runtime);
            return runtime;
        });
        services.AddSingleton(new AuditHostingOrder(services));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartable, AuditStartable>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AuditHostedService>());
        return services;
    }

    private static AuditHostingRegistration Definition(IServiceCollection services)
    {
        ServiceDescriptor[] definitions = services.Where(item => item.ServiceType == typeof(AuditHostingRegistration)).ToArray();
        return definitions.Length == 1 && !definitions[0].IsKeyedService
            && definitions[0].ImplementationInstance is AuditHostingRegistration definition
            ? definition : throw new InvalidOperationException("Configure one passive Audit definition before selecting delivery.");
    }
}

internal sealed class AuditStartable(AuditRuntime runtime) : IStartable
{
    public Task StartAsync(CancellationToken cancellationToken) => runtime.StartAsync(cancellationToken);
}

internal sealed class AuditHostedService(AuditRuntime runtime, AuditHostingOrder order,
    IHostApplicationLifetime lifetime, IOptions<HostOptions> options) : IHostedLifecycleService, IAsyncDisposable
{
    private CancellationTokenRegistration stopping;
    private CancellationTokenRegistration startupStopping;
    private CancellationTokenRegistration started;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ServicesStartConcurrently || options.Value.ServicesStopConcurrently || !order.IsValid())
        {
            throw new InvalidOperationException("Audit requires sequential host lifecycle after Messaging and before its cooperating bridges.");
        }
        stopping = lifetime.ApplicationStopping.UnsafeRegister(static value => ((AuditRuntime)value!).BeginStopping(), runtime);
        startupStopping = cancellationToken.UnsafeRegister(static value => ((AuditRuntime)value!).BeginStopping(), runtime);
        started = lifetime.ApplicationStarted.UnsafeRegister(static value => ((AuditHostedService)value!).ReleaseStartup(), this);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => order.HasStartup ? Task.CompletedTask : runtime.StartAsync(cancellationToken);
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => runtime.StopAsync();
    public Task StopAsync(CancellationToken cancellationToken) => runtime.StopAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ReleaseStartup() => startupStopping.Dispose();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await runtime.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            stopping.Dispose();
            startupStopping.Dispose();
            started.Dispose();
        }
    }
}

internal sealed class AuditHostingOrder(IServiceCollection services)
{
    private static readonly Type? WebHost = typeof(GenericHostWebHostBuilderExtensions).Assembly
        .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService");

    internal bool HasStartup => Hosted().Any(item => item.ImplementationType == typeof(StartupHostedService));

    internal bool IsValid()
    {
        ServiceDescriptor[] hosted = Hosted();
        int audit = Array.FindIndex(hosted, item => item.ImplementationType == typeof(AuditHostedService));
        int messaging = Array.FindIndex(hosted, item => item.ImplementationType == typeof(MessagingHostedService));
        if (audit < 0 || messaging < 0 || audit <= messaging
            || hosted.Count(item => item.ImplementationType == typeof(AuditHostedService)) != 1)
        {
            return false;
        }
        Type?[] participants = services.Where(item => !item.IsKeyedService && item.ServiceType == typeof(IStartable))
            .Select(item => item.ImplementationType).ToArray();
        int position = Array.IndexOf(participants, typeof(AuditStartable));
        if (position < 1 || participants[position - 1] != typeof(MessagingStartable)
            || position != participants.Length - 1
                && !(position == participants.Length - 2 && participants[^1] == typeof(CoordinationStartable)))
        {
            return false;
        }
        for (int index = audit + 1; index < hosted.Length; index++)
        {
            Type? type = hosted[index].ImplementationType;
            if (type == WebHost && WebHost is not null && index == hosted.Length - 1)
            {
                continue;
            }
            if (type != typeof(PipelineHostedService) && type != typeof(RestHostedService)
                && type != typeof(CoordinationHostedService) && type != typeof(HeartbeatHostedService)
                && type != typeof(StartupHostedService))
            {
                return false;
            }
        }
        return true;
    }

    private ServiceDescriptor[] Hosted() => services.Where(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService)).ToArray();
}
