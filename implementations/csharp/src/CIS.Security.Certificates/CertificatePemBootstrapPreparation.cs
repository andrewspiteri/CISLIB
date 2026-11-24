using CIS.RestClient;
using CIS.RestClient.Http;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Security.Certificates;

/// <summary>Owns explicit authenticated PEM preparation before the application's single provider is built.</summary>
/// <remarks>No provider, timer or certificate store is created. Dispose standalone borrowing clients, then
/// retire bootstrap transport before activating this preparation in the final native runtime.</remarks>
public sealed class CertificatePemBootstrapPreparation : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly string profile;
    private readonly RestHttpTransport transport;
    private readonly RestHttpNativeKeyScope keys;
    private readonly bool ownsKeys;
    private CertificateTrustPolicy? clientTrust;
    private RestHttpServerTrust? serverTrust;
    private DateTimeOffset? serverTrustExpiry;
    private object? runtimeOwner;
    private object? providerOwner;
    private Task? retirement;
    private Task? disposal;
    private bool transferred;
    private bool closing;

    private CertificatePemBootstrapPreparation(SecurityPolicySnapshot policy, string profile,
        CertificatePemSecurityOptions options, RestHttpNativeKeyScope? borrowedKeys, TimeProvider clock)
    {
        Policy = policy;
        this.profile = profile;
        Options = options;
        TimeProvider = clock;
        Binding = new(policy, profile, RestSecurityCredentialKind.ClientCertificate);
        keys = borrowedKeys ?? new RestHttpNativeKeyScope(2);
        ownsKeys = borrowedKeys is null;
        transport = RestHttpTransport.CreateCertificateBoundUninitializedTrust(Binding, clock: clock);
    }

    /// <summary>Gets the exact independent immutable policy to register in the final application.</summary>
    public SecurityPolicySnapshot Policy
    {
        get;
    }
    /// <summary>Gets the selected profile alias.</summary>
    public string Profile => profile;
    /// <summary>Gets the exact binding reused by the final native transport.</summary>
    public RestSecurityBinding Binding
    {
        get;
    }
    /// <summary>Gets the captured immutable source/trust references; changing trust requires replacement.</summary>
    public CertificatePemSecurityOptions Options
    {
        get;
    }
    /// <summary>Gets the preparation clock, also required for the adopting native runtime.</summary>
    public TimeProvider TimeProvider
    {
        get;
    }

    /// <summary>Gets the unmetered bootstrap-only transport for explicitly owned standalone REST clients.</summary>
    /// <remarks>Never register this transport as a hosted borrowed transport. The final runtime creates its own
    /// native transport with the exact final provider's IMeterFactory and renews into that actual transport.</remarks>
    public RestHttpTransport Transport
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(closing || retirement is not null, this);
                return transport;
            }
        }
    }

    internal CertificateTrustPolicy ClientTrust => clientTrust
        ?? throw new InvalidOperationException("Bootstrap trust has not been prepared.");
    internal RestHttpServerTrust? ServerTrust => serverTrust;
    internal DateTimeOffset? ServerTrustExpiry => serverTrustExpiry;
    internal bool OwnsKeys => ownsKeys;

    /// <summary>Loads and validates fixed trust and an initial real credential without constructing a service provider.</summary>
    /// <remarks>Only newly owned native key backing is used. A supplied scope remains caller-owned. Expected
    /// credential failures fail this preparation; a new process cannot bootstrap using unavailable credentials.</remarks>
    public static async Task<CertificatePemBootstrapPreparation> PrepareAsync(SecurityPolicySnapshot policy,
        string profile, CertificatePemSecurityOptions options, IHostEnvironment? hostEnvironment = null,
        RestHttpNativeKeyScope? borrowedNativeKeys = null, TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        SecurityProfile selected = policy.GetProfile(profile);
        if ((selected.Roles & SecurityProfileRoles.Outbound) == 0
            || selected.Kind is not (SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment)
            || options.Credential is null
            || (selected.Kind == SecurityProfileKind.LocalDevelopment) != (options.Development is not null))
        {
            throw new ArgumentException("Select a complete enabled outgoing PEM profile with independent credential inputs.");
        }
        options.Development?.Validate(hostEnvironment);
        if (options.Development is { } development && (options.ServerTrustBundleFile is null
            || selected.AllowedOrigins.Any(origin => !development.PermitsOrigin(new Uri(origin)))))
        {
            throw new ArgumentException("Development bootstrap requires isolated server trust and permitted native origins.");
        }
        var prepared = new CertificatePemBootstrapPreparation(policy, profile, options, borrowedNativeKeys,
            clock ?? global::System.TimeProvider.System);
        try
        {
            prepared.clientTrust = await CertificatePemRuntime.LoadTrustAsync(options, cancellationToken).ConfigureAwait(false);
            prepared.clientTrust.ValidateAuthorities(prepared.TimeProvider.GetUtcNow());
            (prepared.serverTrust, prepared.serverTrustExpiry) = await CertificatePemRuntime.LoadServerTrustAsync(
                options, prepared.TimeProvider, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            prepared.transport.InitializeServerTrustOnce(prepared.serverTrust);
            using PemCandidate candidate = await CertificatePemFiles.ReadPairAsync(options.Credential, cancellationToken).ConfigureAwait(false);
            RestHttpCertificateAdoption adoption = await prepared.transport.TryAdoptCertificateAsync(token =>
                ValueTask.FromResult(CertificatePemRuntime.CreateMaterial(candidate, token, prepared.clientTrust,
                    prepared.Binding, prepared.keys, prepared.TimeProvider, options.PollInterval, out _)),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (adoption != RestHttpCertificateAdoption.Adopted)
            {
                throw new InvalidOperationException("The bootstrap certificate could not be admitted within its native ownership bound.");
            }
            return prepared;
        }
        catch (Exception original)
        {
            try
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Certificate bootstrap and its owned cleanup both failed.", original, cleanup);
            }
            if (original is PemCredentialRejection or IOException or UnauthorizedAccessException
                or InvalidDataException or CryptographicException)
            {
                throw new AuthenticationException("The prepared PEM credential or fixed trust is unavailable.");
            }
            throw;
        }
    }

    /// <summary>Closes bootstrap dispatch and joins the actual native transport/body owners before final adoption.</summary>
    /// <remarks>First close and dispose borrowing standalone clients. This wait has no canceled-wait shortcut.</remarks>
    public Task RetireBootstrapAsync()
    {
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (retirement is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                retirement = completion.Task;
            }
            operation = retirement;
        }
        if (completion is not null)
        {
            _ = RetireCoreAsync(completion);
        }
        return operation;
    }

    private async Task RetireCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    internal void ClaimProvider(object owner, SecurityPolicySnapshot policy)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (!ReferenceEquals(Policy, policy) || providerOwner is not null && !ReferenceEquals(providerOwner, owner))
            {
                throw new InvalidOperationException("One final provider may own a prepared certificate profile using its exact policy.");
            }
            providerOwner = owner;
        }
    }

    internal void ClaimRuntime(object owner, SecurityPolicySnapshot policy, string alias, CertificatePemSecurityOptions options)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (!ReferenceEquals(Policy, policy) || profile != alias || !ReferenceEquals(Options, options)
                || providerOwner is null || runtimeOwner is not null)
            {
                throw new InvalidOperationException("Adopt the exact prepared policy/options into one final native runtime.");
            }
            runtimeOwner = owner;
        }
    }

    internal RestHttpNativeKeyScope TransferKeys(object owner)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (!ReferenceEquals(runtimeOwner, owner) || transferred || retirement?.IsCompletedSuccessfully != true)
            {
                throw new InvalidOperationException("Only the claimed native runtime may acquire the settled preparation's key scope once.");
            }
            transferred = true;
            return keys;
        }
    }

    /// <summary>Activates only this preparation's claimed final keyed runtime after bootstrap drain.</summary>
    /// <remarks>No service-list discovery is performed. Ordinary later Startup joins the same native activation.</remarks>
    public Task ActivateFinalAsync(IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();
        RequireBootstrapRetired();
        if (!ReferenceEquals(provider.GetRequiredKeyedService<CertificatePemBootstrapPreparation>(profile), this)
            || !ReferenceEquals(provider.GetRequiredService<SecurityPolicySnapshot>(), Policy)
            || !ReferenceEquals(provider.GetRequiredService<TimeProvider>(), TimeProvider))
        {
            throw new InvalidOperationException("Activate the exact prepared PEM profile, immutable policy and clock in its one final provider.");
        }
        return provider.GetRequiredKeyedService<CertificatePemRuntime>(profile).StartAsync(cancellationToken);
    }

    internal void RequireBootstrapRetired()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (retirement?.IsCompletedSuccessfully != true)
            {
                throw new InvalidOperationException("Join bootstrap retirement before activating the final certificate profile.");
            }
        }
    }

    /// <summary>Joins bootstrap cleanup and disposes only the still-owned scope; final-runtime key ownership is never duplicated.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (disposal is null)
            {
                closing = true;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }
            operation = disposal;
        }
        if (completion is not null)
        {
            _ = DisposeCoreAsync(completion);
        }
        return new(operation);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
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
        bool release;
        lock (gate)
        {
            release = ownsKeys && !transferred;
        }
        if (release)
        {
            try
            {
                keys.Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        if (errors.Count == 0)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(errors.Count == 1 ? errors[0] : new AggregateException(errors));
        }
    }
}
