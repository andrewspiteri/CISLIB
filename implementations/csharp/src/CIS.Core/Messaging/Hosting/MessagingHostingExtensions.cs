using CIS.Healthcheck;
using CIS.Heartbeat.Hosting;
using CIS.RestClient.Hosting;
using CIS.Security;
using CIS.Startup;
using CIS.Startup.Hosting;
using CIS.Transactions;
using CIS.Workflows.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CIS.Messaging.Hosting;

/// <summary>Connects the complete passive Messaging definition graph to the existing host and provider.</summary>
public static class MessagingHostingExtensions
{
    /// <summary>Registers module services without database, broker or worker I/O.</summary>
    /// <remarks>Register definitions and the selected store/transport first, then this bridge after ordinary
    /// business hosted services and startup participants, before Workflow/REST/Heartbeat/Startup bridges.
    /// Disable concurrent host start/stop. CISApplication arranges those cooperating bridges itself.
    /// Required store/schema validation and native topology/consumer startup remain sequential.</remarks>
    public static IServiceCollection AddCisMessaging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        MessagingSnapshot snapshot = MessageDefinitionServiceCollectionExtensions.InspectRegistration(services);
        snapshot.Validate();
        ServiceDescriptor? previous = services.SingleOrDefault(item => item.ServiceType == typeof(MessagingComposition));
        if (previous is not null)
        {
            if (previous.ImplementationInstance is not MessagingComposition composition
                || !ReferenceEquals(composition.RegistrationIdentity, snapshot.RegistrationIdentity))
            {
                throw new InvalidOperationException("Messaging hosting retains one exact completed registration snapshot.");
            }
            return services;
        }
        services.AddSingleton(new MessagingComposition(snapshot));
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton(_ => LocalApplicationIdentity.Capture());
        services.TryAddSingleton(root => new MessagingResourceBudget(root.GetRequiredService<MessagingSnapshot>().Settings));
        services.TryAddSingleton(root => new MessagingInstrumentation(root.GetRequiredService<MessagingSnapshot>(),
            root.GetService<TimeProvider>(), root.GetService<ILoggerFactory>()));
        services.TryAddSingleton(root => new MessageOutbox(root.GetRequiredService<MessagingSnapshot>(),
            root.GetRequiredService<IMessagingStore>(), root.GetRequiredService<MessagingResourceBudget>(),
            root.GetRequiredService<LocalApplicationIdentity>(), root.GetService<TimeProvider>()));
        services.TryAddSingleton(root => new MessageOutboxPublisher(root.GetRequiredService<MessagingSnapshot>(),
            root.GetRequiredService<IMessagingStore>(), root.GetRequiredService<ILocalTransactionFactory>(),
            root.GetRequiredService<IMessageTransport>(), root.GetRequiredService<MessagingResourceBudget>(),
            root.GetRequiredService<LocalApplicationIdentity>(), root.GetService<TimeProvider>(), root.GetRequiredService<MessagingInstrumentation>()));
        services.TryAddSingleton(root => new MessageDeliveryDispositionCoordinator(root.GetRequiredService<MessagingSnapshot>(),
            root.GetRequiredService<IMessagingStore>(), root.GetRequiredService<ILocalTransactionFactory>(),
            root.GetRequiredService<LocalApplicationIdentity>(), root.GetRequiredService<MessageOutboxPublisher>().Wake));
        services.TryAddSingleton(root => new MessageDispatcher(root, root.GetRequiredService<MessagingSnapshot>(),
            root.GetRequiredService<IMessagingStore>(), root.GetRequiredService<ILocalTransactionFactory>(),
            root.GetRequiredService<MessagingResourceBudget>(), root.GetRequiredService<LocalApplicationIdentity>(),
            root.GetRequiredService<MessageDeliveryDispositionCoordinator>(), root.GetService<TimeProvider>(),
            root.GetRequiredService<MessagingInstrumentation>()));
        services.TryAddSingleton<IMessageOutbox>(root => root.GetRequiredService<MessageOutbox>());
        services.TryAddSingleton<IMessagingClient>(root => new MessageSubmissionClient(root.GetRequiredService<MessageOutbox>(),
            root.GetRequiredService<ILocalTransactionFactory>(), root.GetRequiredService<MessageOutboxPublisher>().Wake));
        services.TryAddScoped<IMessageAdministration>(root =>
        {
            MessagingRuntime runtime = root.GetRequiredService<MessagingRuntime>();
            return new MessageAdministration(root.GetRequiredService<MessagingSnapshot>(),
                root.GetRequiredService<IMessagingStore>(), root.GetRequiredService<ILocalTransactionFactory>(),
                root.GetRequiredService<LocalApplicationIdentity>(), root.GetRequiredService<ICapabilityAuthorizer>(),
                root.GetRequiredService<ICallerContextAccessor>(), root.GetRequiredService<ISecurityAuditSink>(),
                root.GetRequiredService<MessagingResourceBudget>(), root.GetRequiredService<MessageOutboxPublisher>().Wake,
                accepting: () => runtime.IsAccepting);
        });
        services.TryAddSingleton(root => new MessagingRuntime(root.GetRequiredService<MessagingSnapshot>(),
            root.GetRequiredService<MessagingComposition>(), root.GetRequiredService<IMessagingStore>(),
            root.GetRequiredService<ILocalTransactionFactory>(), root.GetRequiredService<IMessageTransport>(),
            root.GetRequiredService<MessagingResourceBudget>(), root.GetRequiredService<MessageOutbox>(),
            root.GetRequiredService<MessageOutboxPublisher>(), root.GetRequiredService<MessageDispatcher>(),
            root.GetRequiredService<MessagingInstrumentation>(), root.GetRequiredService<LocalApplicationIdentity>(),
            root.GetService<IHealthMonitorFactory>(), root.GetService<ILogger<MessagingRuntime>>(), root.GetService<TimeProvider>()));
        services.TryAddSingleton(new MessagingHostingOrder(services));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartable, MessagingStartable>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MessagingHostedService>());
        return services;
    }
}

internal sealed class MessagingStartable(MessagingRuntime runtime) : IStartable
{
    public Task StartAsync(CancellationToken cancellationToken) => runtime.StartAsync(cancellationToken);
}

internal sealed class MessagingHostedService(MessagingRuntime runtime, MessagingHostingOrder order,
    IHostApplicationLifetime lifetime, IOptions<HostOptions> options) : IHostedLifecycleService, IAsyncDisposable
{
    private CancellationTokenRegistration stopping;
    private CancellationTokenRegistration startupStopping;
    private CancellationTokenRegistration started;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ServicesStartConcurrently || options.Value.ServicesStopConcurrently || !order.IsValid())
        {
            throw new InvalidOperationException("Messaging requires sequential host lifecycle after ordinary dependencies and before the cooperating CIS bridges; its IStartable must follow business startup participants.");
        }
        stopping = lifetime.ApplicationStopping.UnsafeRegister(static owner => ((MessagingRuntime)owner!).BeginStopping(), runtime);
        startupStopping = cancellationToken.UnsafeRegister(static owner => ((MessagingRuntime)owner!).BeginStopping(), runtime);
        started = lifetime.ApplicationStarted.UnsafeRegister(static owner => ((MessagingHostedService)owner!).ReleaseStartup(), this);
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

internal sealed class MessagingHostingOrder(IServiceCollection services)
{
    private static readonly Type? WebHost = typeof(GenericHostWebHostBuilderExtensions).Assembly
        .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService");

    internal bool HasStartup => Hosted().Any(item => item.ImplementationType == typeof(StartupHostedService));

    internal bool IsValid()
    {
        ServiceDescriptor[] hosted = Hosted();
        int position = Array.FindIndex(hosted, item => item.ImplementationType == typeof(MessagingHostedService));
        if (position < 0 || hosted.Count(item => item.ImplementationType == typeof(MessagingHostedService)) != 1)
        {
            return false;
        }
        ServiceDescriptor[] participants = services.Where(item => !item.IsKeyedService && item.ServiceType == typeof(IStartable)).ToArray();
        if (participants.Length == 0 || participants[^1].ImplementationType != typeof(MessagingStartable))
        {
            return false;
        }
        for (int index = position + 1; index < hosted.Length; index++)
        {
            Type? type = hosted[index].ImplementationType;
            if (type == WebHost && WebHost is not null && index == hosted.Length - 1)
            {
                continue;
            }
            if (type != typeof(PipelineHostedService) && type != typeof(RestHostedService)
                && type != typeof(HeartbeatHostedService) && type != typeof(StartupHostedService))
            {
                return false;
            }
        }
        return true;
    }

    private ServiceDescriptor[] Hosted() => services.Where(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService)).ToArray();
}
