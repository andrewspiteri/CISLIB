using CIS.Audit.Hosting;
using CIS.Healthcheck;
using CIS.Coordination.Hosting;
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
        services.TryAddSingleton(root => new MessagingResourceBudget(root.GetRequiredService<MessagingSnapshot>().Settings,
            root.GetRequiredService<MessagingSnapshot>().Requests.Count != 0));
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
        if (snapshot.Requests.Count != 0)
        {
            services.TryAddSingleton(root => new MessageRequestCoordinator(root.GetRequiredService<MessagingSnapshot>(),
                root.GetRequiredService<IMessagingStore>(), root.GetRequiredService<ILocalTransactionFactory>(),
                root.GetRequiredService<IMessageTransport>(), root.GetRequiredService<MessageOutbox>(),
                root.GetRequiredService<MessagingResourceBudget>(), root.GetRequiredService<LocalApplicationIdentity>(),
                root.GetService<TimeProvider>(), root.GetRequiredService<MessagingInstrumentation>(), root.GetRequiredService<MessageOutboxPublisher>().Wake));
        }
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
        services.TryAddSingleton(root =>
        {
            var runtime = new MessagingRuntime(root.GetRequiredService<MessagingSnapshot>(),
            root.GetRequiredService<MessagingComposition>(), root.GetRequiredService<IMessagingStore>(),
            root.GetRequiredService<ILocalTransactionFactory>(), root.GetRequiredService<IMessageTransport>(),
            root.GetRequiredService<MessagingResourceBudget>(), root.GetRequiredService<MessageOutbox>(),
            root.GetRequiredService<MessageOutboxPublisher>(), root.GetRequiredService<MessageDispatcher>(),
            root.GetRequiredService<MessagingInstrumentation>(), root.GetRequiredService<LocalApplicationIdentity>(),
                root.GetService<IHealthMonitorFactory>(), root.GetService<ILogger<MessagingRuntime>>(), root.GetService<TimeProvider>());
            if (root.GetService<MessageRequestCoordinator>() is { } coordinator)
            {
                root.GetRequiredService<MessageOutbox>().BindRequests(coordinator);
                root.GetRequiredService<MessageDispatcher>().BindReplies(coordinator);
                runtime.BindRequests(coordinator);
            }
            return runtime;
        });
        services.TryAddSingleton(new MessagingHostingOrder(services));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartable, MessagingStartable>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MessagingHostedService>());
        return services;
    }
}

internal sealed class MessagingStartable(MessagingRuntime runtime, IServiceProvider? root = null) : IStartable
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        root?.GetService<AuditRuntime>()?.PreparePublishing();

        await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class MessagingHostedService(MessagingRuntime runtime, MessagingHostingOrder order,
    IHostApplicationLifetime lifetime, IOptions<HostOptions> options, IServiceProvider root) : IHostedLifecycleService, IAsyncDisposable
{
    private readonly CoordinationHostLifetime? coordination = root.GetService<CoordinationHostLifetime>();
    private readonly AuditRuntime? audit = root.GetService<AuditRuntime>();
    private CancellationTokenRegistration stopping;
    private CancellationTokenRegistration startupStopping;
    private CancellationTokenRegistration started;
    private CancellationTokenRegistration dependencyStopping;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ServicesStartConcurrently || options.Value.ServicesStopConcurrently || !order.IsValid())
        {
            throw new InvalidOperationException("Messaging requires sequential host lifecycle after ordinary dependencies and before the cooperating CIS bridges; its IStartable must follow business startup participants.");
        }
        stopping = lifetime.ApplicationStopping.UnsafeRegister(static owner => ((MessagingHostedService)owner!).BeginStopping(), this);
        startupStopping = cancellationToken.UnsafeRegister(static owner => ((MessagingHostedService)owner!).BeginStopping(), this);
        if (audit is not null || coordination is not null)
        {
            dependencyStopping = (audit?.DependencyStopping ?? coordination!.DependencyStopping).UnsafeRegister(static owner => ((MessagingHostedService)owner!).BeginStopping(), this);
        }
        started = lifetime.ApplicationStarted.UnsafeRegister(static owner => ((MessagingHostedService)owner!).ReleaseStartup(), this);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => order.HasStartup ? Task.CompletedTask : StartMessagingAsync(cancellationToken);
    private async Task StartMessagingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        audit?.PreparePublishing();

        await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
    }
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => StopCoreAsync();
    public Task StopAsync(CancellationToken cancellationToken) => StopCoreAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ReleaseStartup() => startupStopping.Dispose();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            stopping.Dispose();
            startupStopping.Dispose();
            started.Dispose();
            dependencyStopping.Dispose();
        }
    }

    private void BeginStopping()
    {
        if (audit is not null && !audit.IsDrained)
        {
            audit.BeginStopping();
            return;
        }

        if (coordination is not null && !coordination.IsDrained)
        {
            coordination.BeginStopping();
            return;
        }
        runtime.BeginStopping();
    }

    private async Task StopCoreAsync()
    {
        Exception? failure = await AuditRuntime.CaptureStopAsync(audit, coordination).ConfigureAwait(false);
        await Task.WhenAll(runtime.StopAsync(), failure is null ? Task.CompletedTask : Task.FromException(failure)).ConfigureAwait(false);
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
        bool messagingLast = participants.Length > 0 && participants[^1].ImplementationType == typeof(MessagingStartable);
        bool coordinationLast = participants.Length > 1 && participants[^1].ImplementationType == typeof(CoordinationStartable)
            && participants[^2].ImplementationType == typeof(MessagingStartable);
        bool auditLast = participants.Length > 1 && participants[^1].ImplementationType == typeof(AuditStartable)
            && participants[^2].ImplementationType == typeof(MessagingStartable);
        bool auditBeforeCoordination = participants.Length > 2 && participants[^1].ImplementationType == typeof(CoordinationStartable)
            && participants[^2].ImplementationType == typeof(AuditStartable)
            && participants[^3].ImplementationType == typeof(MessagingStartable);
        if (!messagingLast && !coordinationLast && !auditLast && !auditBeforeCoordination)
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
                && type != typeof(HeartbeatHostedService) && type != typeof(StartupHostedService)
                && type != typeof(CoordinationHostedService) && type != typeof(AuditHostedService))
            {
                return false;
            }
        }
        return true;
    }

    private ServiceDescriptor[] Hosted() => services.Where(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService)).ToArray();
}
