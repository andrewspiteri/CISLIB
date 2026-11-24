using CIS.Configuration;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.RestClient.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Hosting;

/// <summary>Selects a bounded native pre-provider Management and central Configuration bootstrap.</summary>
/// <remarks>Credential preparation is trusted application composition. It must join its own failed acquisition before
/// returning; on success the facade immediately retains the returned exact native owner. No temporary provider is permitted.</remarks>
public sealed class CISApplicationCentralConfigurationOptions
{
    /// <summary>Captures explicit endpoint profiles, registration facts and the actual independent native credential preparation.</summary>
    public CISApplicationCentralConfigurationOptions(ManagementClientOptions management, ManagementConfigurationClientOptions configuration,
        ManagementRegistrationOptions registration, Func<CancellationToken, Task<CISApplicationCentralConfigurationPreparation>> prepareCredentialsAsync)
    {
        ArgumentNullException.ThrowIfNull(management);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(prepareCredentialsAsync);
        Management = management;
        Configuration = configuration;
        Registration = registration;
        PrepareCredentialsAsync = prepareCredentialsAsync;
    }

    /// <summary>Gets the independent ordinary Management endpoint/profile.</summary>
    public ManagementClientOptions Management
    {
        get;
    }
    /// <summary>Gets the separate configuration endpoint/profile.</summary>
    public ManagementConfigurationClientOptions Configuration
    {
        get;
    }
    /// <summary>Gets the reported registration facts and existing runtime timing selection.</summary>
    public ManagementRegistrationOptions Registration
    {
        get;
    }
    /// <summary>Gets the trusted factory for one actual prepared native credential owner.</summary>
    public Func<CancellationToken, Task<CISApplicationCentralConfigurationPreparation>> PrepareCredentialsAsync
    {
        get;
    }
    /// <summary>Gets the complete preparation budget, initially 120 seconds and at most 300 seconds.</summary>
    public TimeSpan Timeout
    {
        get; init;
    } = TimeSpan.FromSeconds(120);
    /// <summary>Gets the independent expected environment; it must equal the application's central identity.</summary>
    public string Environment
    {
        get; init;
    } = EnvironmentOptions.EnvironmentName;
    /// <summary>Gets the independent exact cohort expectation. Null expects no cohort; it never selects one on the server.</summary>
    public string? ExpectedCohortId
    {
        get; init;
    }
    /// <summary>Gets the monotonic preparation and admission clock, also selected in final DI.</summary>
    public TimeProvider TimeProvider
    {
        get; init;
    } = global::System.TimeProvider.System;
}

/// <summary>The finite handoff of one prepared native credential and its two bootstrap REST profiles.</summary>
/// <remarks>Callbacks refer to the captured actual native preparation. ConfigureFinal registers ordinary security/clients
/// with the final provider's meter factory; it must not build a provider or register bootstrap transports as hosted clients.
/// Retirement joins credentials after the facade has disposed both borrowing REST owners. The facade disposes Owner after the real final provider.</remarks>
public sealed class CISApplicationCentralConfigurationPreparation
{
    /// <summary>Captures the exact native resource and trusted passive final-registration/activation/retirement callbacks.</summary>
    public CISApplicationCentralConfigurationPreparation(IAsyncDisposable owner, RestClientOptions managementRest,
        RestClientOptions configurationRest, Action<IServiceCollection> configureFinal,
        Func<IServiceProvider, CancellationToken, Task> activateFinalAsync, Func<Task> retireCredentialsAsync,
        RestHttpTransport? borrowedManagementTransport = null, RestHttpTransport? borrowedConfigurationTransport = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(managementRest);
        ArgumentNullException.ThrowIfNull(configurationRest);
        ArgumentNullException.ThrowIfNull(configureFinal);
        ArgumentNullException.ThrowIfNull(activateFinalAsync);
        ArgumentNullException.ThrowIfNull(retireCredentialsAsync);
        Owner = owner;
        ManagementRest = managementRest;
        ConfigurationRest = configurationRest;
        ConfigureFinal = configureFinal;
        ActivateFinalAsync = activateFinalAsync;
        RetireCredentialsAsync = retireCredentialsAsync;
        BorrowedManagementTransport = borrowedManagementTransport;
        BorrowedConfigurationTransport = borrowedConfigurationTransport;
    }

    internal IAsyncDisposable Owner
    {
        get;
    }
    internal RestClientOptions ManagementRest
    {
        get;
    }
    internal RestClientOptions ConfigurationRest
    {
        get;
    }
    internal Action<IServiceCollection> ConfigureFinal
    {
        get;
    }
    internal Func<IServiceProvider, CancellationToken, Task> ActivateFinalAsync
    {
        get;
    }
    internal Func<Task> RetireCredentialsAsync
    {
        get;
    }
    internal RestHttpTransport? BorrowedManagementTransport
    {
        get;
    }
    internal RestHttpTransport? BorrowedConfigurationTransport
    {
        get;
    }
}

internal sealed class CISApplicationCentralConfiguration
{
    private readonly object gate = new();
    private readonly CISApplicationCentralConfigurationOptions options;
    private readonly ManagementClientSnapshot management;
    private readonly ManagementConfigurationClientSnapshot configuration;
    private readonly ManagementRegistrationOptions registration;
    private readonly ManagedWorkAdmission admission;
    private CISApplicationCentralConfigurationPreparation? preparation;
    private HttpRestClient? managementRest;
    private HttpRestClient? configurationRest;
    private ManagementClient? bootstrapManagement;
    private ManagementConfigurationStore? store;
    private ManagementBootstrapSession? session;
    private ManagementConfigurationReconciler? reconciler;
    private RestSecurityBinding? securityBinding;
    private Task? retirement;
    private IReadOnlyList<ConfigurationEnrollment> enrollments = [];
    private TaskCompletionSource preparationSettled = Settled();
    private bool closing;

    internal CISApplicationCentralConfiguration(CISApplicationCentralConfigurationOptions selected)
    {
        options = selected;
        management = ManagementClientSnapshot.Capture(selected.Management);
        configuration = ManagementConfigurationClientSnapshot.Capture(selected.Configuration);
        registration = selected.Registration.Copy();
        if (management.RestClientName == configuration.RestClientName || management.RestClientName != registration.ClientName
            || management.BaseUri != configuration.BaseUri || management.ReadRetryPolicy is not null || configuration.ReadRetryPolicy is not null
            || selected.Timeout <= TimeSpan.Zero || selected.Timeout > TimeSpan.FromSeconds(300)
            || selected.Environment != EnvironmentOptions.EnvironmentName || selected.TimeProvider is null
            || selected.ExpectedCohortId is not null && !ManagementWire.IsOperationKey(selected.ExpectedCohortId))
        {
            throw new ArgumentException("Select distinct matching authenticated aliases, no bootstrap retry, the central environment and a bounded preparation budget.");
        }
        admission = new(selected.TimeProvider);
    }

    internal TimeSpan Timeout => options.Timeout;
    internal TimeProvider Clock => options.TimeProvider;

    internal async Task PrepareCredentialsAsync(CancellationToken token)
    {
        lock (gate)
        {
            if (closing)
            {
                throw new OperationCanceledException(token);
            }
            preparationSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            // Native factories own failed acquisition until this return. Retain before any following await or constructor.
            preparation = await options.PrepareCredentialsAsync(token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The native credential preparation returned no owner.");
            token.ThrowIfCancellationRequested();
            CreateStandaloneClients();
        }
        finally
        {
            preparationSettled.TrySetResult();
        }
    }

    private void CreateStandaloneClients()
    {
        RestClientOptions ordinary = preparation!.ManagementRest;
        RestClientOptions central = preparation.ConfigurationRest;
        if (ordinary.ClientName != management.RestClientName || central.ClientName != configuration.RestClientName
            || ordinary.SecurityBinding?.PolicyOwner is null || !ReferenceEquals(ordinary.SecurityBinding, central.SecurityBinding)
            || ordinary.LogCalls || central.LogCalls || ordinary.RetryPolicy.PolicyName is not null || central.RetryPolicy.PolicyName is not null)
        {
            throw new InvalidOperationException("Bootstrap requires the exact same native security binding, dedicated aliases and no implicit calls or retries.");
        }
        securityBinding = ordinary.SecurityBinding;
        ordinary.ConfigureCisManagementRestClient(new(management.RestClientName)
        {
            BaseUrl = management.BaseUri.AbsoluteUri,
            Timeout = management.Timeout,
            WireLimits = management.WireLimits
        });
        central.ConfigureCisManagementConfigurationRestClient(new(configuration.RestClientName)
        {
            BaseUrl = configuration.BaseUri.AbsoluteUri,
            Timeout = configuration.Timeout,
            WireLimits = configuration.WireLimits
        });
        managementRest = new(ordinary, preparation.BorrowedManagementTransport, Clock);
        configurationRest = new(central, preparation.BorrowedConfigurationTransport, Clock);
        bootstrapManagement = new(managementRest, management, [], Clock);
    }

    internal async Task<ConfigurationOptions> PrepareTargetAsync(ConfigurationOptions selected, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("D");
        ManagementRegistrationRequest request = ManagementRegistration.BootstrapRequest(registration);
        admission.BindRegistration(id);
        ManagementCallResult<ManagementRegistrationReceipt> result = await bootstrapManagement!.RegisterAsync(id, request,
            new(registration.InitialRegistrationTimeout), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!result.Exchange.Success || result.Exchange.Value is not { } receipt || receipt.RegistrationId != id
            || !ManagementWire.IsUuid(receipt.ApplicationId) || !ManagementWire.IsTimestamp(receipt.RegisteredAtUtc))
        {
            throw new InvalidOperationException("The required native bootstrap registration did not complete.");
        }
        ManagedControlAttempt attempt = admission.BeginAttempt(id, Guid.NewGuid().ToString("D"));
        ManagementCallResult<ManagementControlConfirmation> control = await bootstrapManagement.GetControlAsync(id, attempt.AttemptId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!control.Exchange.Success || control.Exchange.Value is not { } confirmed || !admission.ValidateConfirmation(attempt, confirmed))
        {
            throw new InvalidOperationException("The required native bootstrap control did not establish its live namespace/session.");
        }
        session = new(admission, id, request, receipt, confirmed.AuthorityId);
        enrollments = Array.AsReadOnly(selected.Enrollments.ToArray());
        store = new(storeClient(), selected.Store);
        return new(selected.HostApplicationName, store)
        {
            Enrollments = enrollments,
            PollingInterval = selected.PollingInterval,
            IsHealthCritical = selected.IsHealthCritical,
            Factories = selected.Factories,
            Validators = selected.Validators,
            SnapshotTarget = new(confirmed.AuthorityId, receipt.ApplicationId, options.Environment, id,
                selected.HostApplicationName, options.ExpectedCohortId)
        };

        IManagementConfigurationClient storeClient() => new ManagementConfigurationClient(configurationRest!, configuration, [], Clock);
    }

    internal void ConfigureFinal(IServiceCollection services, ConfigurationRuntime runtime)
    {
        preparation!.ConfigureFinal(services);
        ValidateFinalProfiles(services);
        if (services.LastOrDefault(item => item.ServiceType == typeof(TimeProvider))?.ImplementationInstance is TimeProvider clock
            && !ReferenceEquals(clock, Clock))
        {
            throw new InvalidOperationException("Use the exact bootstrap clock for the final managed admission owner.");
        }
        if (!services.Any(item => item.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(Clock);
        }
        reconciler = new(runtime, admission, runtime.CaptureCentralReadiness().Target!);
        services.AddSingleton<IManagedStateReconciler>(reconciler);
        services.AddCisManagementRegistration(registration);
        var lifetime = (ManagementRegistrationLifetime)services.Single(item => item.ServiceType == typeof(ManagementRegistrationLifetime)).ImplementationInstance!;
        lifetime.BindBootstrap(session!);
    }

    internal async Task ActivateFinalAsync(IServiceProvider root, ConfigurationRuntime runtime, CancellationToken token)
    {
        if (!ReferenceEquals(root.GetRequiredService<TimeProvider>(), Clock))
        {
            throw new InvalidOperationException("The final native owners must use the exact bootstrap admission clock.");
        }
        await preparation!.ActivateFinalAsync(root, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        foreach (RestClientRegistration selected in root.GetServices<RestClientRegistration>()
            .Where(item => item.Options.ClientName == management.RestClientName || item.Options.ClientName == configuration.RestClientName))
        {
            RestSecurityBinding? binding = selected.SecurityBindingFactory?.Invoke(root) ?? selected.Options.SecurityBinding;
            if (!ReferenceEquals(binding, securityBinding))
            {
                throw new InvalidOperationException("Final native clients must adopt the exact prepared Security policy and binding.");
            }
        }
        store!.AdoptHostedClient(root.GetRequiredKeyedService<IManagementConfigurationClient>(configuration.RestClientName));
        await runtime.ActivateAsync(root.GetService<CIS.Heartbeat.HeartbeatRuntime>(), root.GetService<CIS.Healthcheck.HealthcheckService>(),
            root.GetService<Microsoft.Extensions.Logging.ILogger<ConfigurationRuntime>>(), token).ConfigureAwait(false);
        if (!await runtime.ReconcileCentralAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException("The required initial central Configuration epoch did not complete.");
        }
        token.ThrowIfCancellationRequested();
        ManagementRegistration managed = root.GetRequiredService<ManagementRegistration>();
        managed.BindConfigurationGap(reconciler!.RequireGap);
        managed.AttachConfigurationStatus(root.GetRequiredKeyedService<IManagementConfigurationClient>(configuration.RestClientName), runtime, enrollments);
        await managed.StartAsync(token).ConfigureAwait(false);
    }

    private void ValidateFinalProfiles(IServiceCollection services)
    {
        RestClientRegistration[] registered = services.Where(item => item.ServiceType == typeof(RestClientRegistration))
            .Select(item => item.ImplementationInstance).OfType<RestClientRegistration>().ToArray();
        RestClientRegistration[] ordinary = registered.Where(item => item.Options.ClientName == management.RestClientName).ToArray();
        RestClientRegistration[] central = registered.Where(item => item.Options.ClientName == configuration.RestClientName).ToArray();
        if (ordinary.Length != 1 || central.Length != 1 || ordinary[0].Options.BaseUrl != management.BaseUri.AbsoluteUri
            || central[0].Options.BaseUrl != configuration.BaseUri.AbsoluteUri || ordinary[0].Options.LogCalls || central[0].Options.LogCalls
            || ordinary[0].Options.Codec is not ManagementContentCodec controlCodec || !ReferenceEquals(controlCodec.Limits, management.WireLimits)
            || central[0].Options.Codec is not ManagementConfigurationContentCodec centralCodec || !ReferenceEquals(centralCodec.Limits, configuration.WireLimits)
            || ordinary[0].Options.RetryPolicy.PolicyName is not null || central[0].Options.RetryPolicy.PolicyName is not null)
        {
            throw new InvalidOperationException("Final native profiles must preserve the captured endpoints, codecs, bounds, aliases and no-retry selection.");
        }
    }

    internal Task RetireBootstrapAsync()
    {
        TaskCompletionSource? completion = null;
        Task work;
        Task preparing;
        lock (gate)
        {
            preparing = preparationSettled.Task;
            if (retirement is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                retirement = completion.Task;
            }
            work = retirement;
        }
        if (completion is not null)
        {
            _ = RetireOwnedAsync(preparing, completion);
        }
        return work;
    }

    private async Task RetireOwnedAsync(Task preparing, TaskCompletionSource completion)
    {
        try
        {
            await RetireCoreAsync(preparing).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private async Task RetireCoreAsync(Task preparing)
    {
        await preparing.ConfigureAwait(false);
        var errors = new List<Exception>();
        Task ordinary = managementRest?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        Task central = configurationRest?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        try
        {
            await ordinary.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            await central.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        if (preparation is not null)
        {
            try
            {
                await preparation.RetireCredentialsAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("Native bootstrap owners did not fully retire.", errors);
        }
    }

    internal void BeginStopping()
    {
        lock (gate)
        {
            closing = true;
        }
        admission.BeginStopping();
        _ = RetireBootstrapAsync();
    }

    private static TaskCompletionSource Settled()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult();
        return completed;
    }

    internal async Task DisposeAsync()
    {
        var errors = new List<Exception>();
        try
        {
            await RetireBootstrapAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        if (reconciler is not null)
        {
            try
            {
                await reconciler.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        if (preparation is not null)
        {
            try
            {
                await preparation.Owner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("Central bootstrap cleanup did not fully settle.", errors);
        }
    }
}
