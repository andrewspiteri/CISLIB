using System.Diagnostics.Metrics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CIS.Heartbeat;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security.Certificates.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.Certificate;

namespace CIS.Security.Certificates;

internal sealed class CertificatePemRuntime : IAsyncDisposable, IHeartbeatSubscriber
{
    private readonly object gate = new();
    private readonly IServiceProvider services;
    private readonly SecurityProfile profile;
    private readonly CertificatePemSecurityOptions options;
    private readonly CertificateProfileAvailability availability;
    private readonly TimeProvider clock;
    private readonly ISecurityProfileStatusReporter reporter;
    private readonly ILogger<CertificatePemRuntime>? logger;
    private readonly Func<IServiceProvider, RestHttpNativeKeyScope>? borrowedKeys;
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenRegistration stopRegistration;
    private readonly TaskCompletionSource cutoff = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly RestHttpTransport? transport;
    private CertificateTrustPolicy? trust;
    private CertificateInboundOptions? receiving;
    private RestHttpNativeKeyScope? keys;
    private IHeartbeatOwner? heartbeat;
    private Task? heartbeatDrain;
    private Task? startup;
    private Task? checking;
    private Task? disposal;
    private Exception? cutoffFailure;
    private string? lastFingerprint;
    private SecurityCertificateReloadOutcome? lastOutcome;
    private SecurityFailure? lastCandidateFailure;
    private DateTimeOffset? retryAt;
    private DateTimeOffset? activeExpiry;
    private long lastDiagnostic;
    private int diagnosticCount;
    private bool forceCheck;
    private bool ready;
    private bool closed;

    internal CertificatePemRuntime(IServiceProvider services, string alias, string? scheme,
        Func<IServiceProvider, CertificatePemSecurityOptions> factory,
        Func<IServiceProvider, RestHttpNativeKeyScope>? borrowedKeys)
    {
        this.services = services;
        SecurityPolicySnapshot policy = services.GetRequiredService<SecurityPolicySnapshot>();
        profile = policy.GetProfile(alias);
        options = factory(services) ?? throw new InvalidOperationException("A PEM setup factory must return immutable options.");
        if (profile.Kind is not (SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment)
            || ((profile.Roles & SecurityProfileRoles.Inbound) != 0) != (scheme is not null)
            || scheme is not null && scheme != profile.AuthenticationScheme
            || (profile.Kind == SecurityProfileKind.LocalDevelopment) != (options.Development is not null))
        {
            throw new ArgumentException("PEM roles, native scheme, profile kind and development gate must agree.");
        }
        this.borrowedKeys = borrowedKeys;
        clock = services.GetRequiredService<TimeProvider>();
        reporter = services.GetRequiredService<ISecurityProfileStatusReporter>();
        availability = services.GetRequiredKeyedService<CertificateProfileAvailability>(alias);
        logger = services.GetService<ILogger<CertificatePemRuntime>>();
        if ((profile.Roles & SecurityProfileRoles.Outbound) != 0)
        {
            if (options.Credential is null)
            {
                throw new ArgumentException("An enabled outgoing certificate profile requires its paired PEM references.");
            }
            Binding = new(policy, alias, RestSecurityCredentialKind.ClientCertificate);
            transport = RestHttpTransport.CreateCertificateBoundUninitializedTrust(Binding,
                services.GetRequiredService<IMeterFactory>(), clock: clock);
        }
        stopRegistration = services.GetService<IHostApplicationLifetime>()?.ApplicationStopping.Register(BeginStopping) ?? default;
    }

    internal string Profile => profile.Alias;
    internal RestSecurityBinding? Binding
    {
        get;
    }
    internal RestHttpTransport Transport => transport ?? throw new InvalidOperationException("An incoming-only profile has no outgoing transport.");
    internal bool IsTrustPrepared => Volatile.Read(ref receiving) is not null;
    internal CertificateInboundOptions Receiving => Volatile.Read(ref receiving)
        ?? throw new InvalidOperationException("Initial PEM trust must be prepared by sequential Startup.");

    internal bool ValidateTls(X509Certificate2 certificate)
    {
        CertificateTrustPolicy? selected = Volatile.Read(ref trust);
        return selected is not null && !Volatile.Read(ref closed)
            && CertificateTrustValidator.Validate(certificate, selected, clock.GetUtcNow()).Succeeded;
    }

    internal Task StartAsync(CancellationToken token)
    {
        TaskCompletionSource? completion = null;
        Task work;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (startup is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                startup = completion.Task;
            }
            work = startup;
        }
        if (completion is not null)
        {
            _ = StartCoreAsync(completion, token);
        }
        return work;
    }

    private async Task StartCoreAsync(TaskCompletionSource completion, CancellationToken caller)
    {
        Exception? failure = null;
        CancellationToken? canceled = null;
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
            CancellationToken token = operation.Token;
            token.ThrowIfCancellationRequested();
            options.Development?.Validate(services.GetService<IHostEnvironment>());
            if (options.Development is { } development && (profile.Roles & SecurityProfileRoles.Outbound) != 0
                && (options.ServerTrustBundleFile is null || profile.AllowedOrigins.Any(origin => !development.PermitsOrigin(new Uri(origin)))))
            {
                throw new ArgumentException("Development outgoing clients require isolated server trust and loopback or explicitly allowed private IP origins.");
            }
            CertificateTrustPolicy selected = await LoadTrustAsync(token).ConfigureAwait(false);
            selected.ValidateAuthorities(clock.GetUtcNow());
            token.ThrowIfCancellationRequested();
            Volatile.Write(ref trust, selected);
            if ((profile.Roles & SecurityProfileRoles.Inbound) != 0)
            {
                Volatile.Write(ref receiving, new(selected, options.Development));
                services.GetRequiredService<IOptionsMonitorCache<CertificateAuthenticationOptions>>().TryRemove(profile.AuthenticationScheme!);
            }
            availability.Initialize();
            if (transport is not null)
            {
                availability.SetOutboundTrust(selected.AuthorityExpiresAtUtc);
                RestHttpServerTrust? serverTrust = null;
                if (options.ServerTrustBundleFile is { } path)
                {
                    X509Certificate2Collection roots = await CertificatePemFiles.ReadCertificatesAsync(path, token).ConfigureAwait(false);
                    try
                    {
                        var validation = new CertificateTrustPolicy("native-server-trust", roots.Cast<X509Certificate2>().ToArray(),
                            roots.Cast<X509Certificate2>().ToArray(), revocation: CertificateRevocationPolicy.ShortLived);
                        validation.ValidateAuthorities(clock.GetUtcNow());
                        serverTrust = new(roots.Cast<X509Certificate2>().ToArray(), options.ServerRevocationMode);
                        availability.SetOutboundTrust(validation.AuthorityExpiresAtUtc);
                    }
                    finally
                    {
                        foreach (X509Certificate2 root in roots)
                        {
                            root.Dispose();
                        }
                    }
                }
                transport.InitializeServerTrustOnce(serverTrust);
                keys = borrowedKeys is null ? new(2) : borrowedKeys(services)
                    ?? throw new InvalidOperationException("The borrowed native key scope factory returned null.");
                heartbeat = services.GetRequiredService<IHeartbeatOwnerFactory>().CreateOwner();
                lock (gate)
                {
                    token.ThrowIfCancellationRequested();
                    ready = true;
                }
                await CheckAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                heartbeat.Service.Register(this, new()
                {
                    Name = "Certificate source: " + profile.Alias,
                    Interval = options.PollInterval,
                    InitialDelay = options.PollInterval
                });
            }
        }
        catch (OperationCanceledException error) when (caller.IsCancellationRequested || stopping.IsCancellationRequested)
        {
            canceled = error.CancellationToken;
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            Complete(completion, failure, canceled);
        }
    }

    private async Task<CertificateTrustPolicy> LoadTrustAsync(CancellationToken token)
    {
        var loaded = new List<X509Certificate2>();
        try
        {
            X509Certificate2[] roots = await Read(options.ClientTrust.RootBundleFile).ConfigureAwait(false);
            X509Certificate2[] issuers = await Read(options.ClientTrust.IssuingAuthorityBundleFile).ConfigureAwait(false);
            X509Certificate2[] intermediates = options.ClientTrust.IntermediateBundleFile is { } path
                ? await Read(path).ConfigureAwait(false) : [];
            return new(options.ClientTrust.TrustDomain, roots, issuers, intermediates,
                options.ClientTrust.Revocation, options.ClientTrust.MaximumShortLivedLifetime);
        }
        finally
        {
            foreach (X509Certificate2 certificate in loaded)
            {
                certificate.Dispose();
            }
        }

        async Task<X509Certificate2[]> Read(string path)
        {
            X509Certificate2Collection value = await CertificatePemFiles.ReadCertificatesAsync(path, token).ConfigureAwait(false);
            loaded.AddRange(value.Cast<X509Certificate2>());
            return value.Cast<X509Certificate2>().ToArray();
        }
    }

    internal ValueTask RequestRecheckAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            // A signal coalesces into the next existing Heartbeat check. It does not mint/replay or spawn a worker.
            forceCheck = true;
        }
        return ValueTask.CompletedTask;
    }

    private Task CheckAsync(CancellationToken token)
    {
        TaskCompletionSource? completion = null;
        Task work;
        lock (gate)
        {
            if (closed || !ready)
            {
                return Task.CompletedTask;
            }
            if (checking is null || checking.IsCompletedSuccessfully || checking.IsCanceled)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                checking = completion.Task;
            }
            work = checking;
        }
        if (completion is not null)
        {
            _ = CheckCoreAsync(completion, token);
        }
        return work;
    }

    private async Task CheckCoreAsync(TaskCompletionSource completion, CancellationToken caller)
    {
        Exception? failure = null;
        CancellationToken? canceled = null;
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
            CancellationToken token = operation.Token;
            PemCandidate candidate;
            try
            {
                candidate = await CertificatePemFiles.ReadPairAsync(options.Credential!, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                token.ThrowIfCancellationRequested();
                Record("source-unavailable", SecurityCertificateReloadOutcome.Rejected,
                    new(error is InvalidDataException ? SecurityFailureKind.CredentialInvalid : SecurityFailureKind.CredentialUnavailable,
                        "The paired PEM source is unavailable or outside its fixed bounds."));
                return;
            }
            using (candidate)
            {
                bool forced;
                lock (gate)
                {
                    forced = forceCheck;
                    forceCheck = false;
                }
                if (!forced && candidate.Fingerprint == lastFingerprint
                    && (lastOutcome == SecurityCertificateReloadOutcome.Adopted
                        || lastOutcome == SecurityCertificateReloadOutcome.Rejected && (retryAt is null || retryAt > clock.GetUtcNow())))
                {
                    availability.SetOutbound(activeExpiry, lastCandidateFailure);
                    return;
                }
                DateTimeOffset? expiry = null;
                try
                {
                    RestHttpCertificateAdoption adopted = await transport!.TryAdoptCertificateAsync(token =>
                    {
                        RestHttpCertificateMaterial material = CreateMaterial(candidate, token, out DateTimeOffset actualExpiry);
                        expiry = actualExpiry;
                        return ValueTask.FromResult(material);
                    }, token).ConfigureAwait(false);
                    if (adopted == RestHttpCertificateAdoption.Adopted)
                    {
                        activeExpiry = expiry;
                        retryAt = null;
                        Record(candidate.Fingerprint, SecurityCertificateReloadOutcome.Adopted, null);
                    }
                    else
                    {
                        Record(candidate.Fingerprint, SecurityCertificateReloadOutcome.Deferred, null);
                    }
                }
                catch (PemCredentialRejection error)
                {
                    retryAt = error.RetryAt;
                    Record(candidate.Fingerprint, SecurityCertificateReloadOutcome.Rejected, error.Failure);
                }
            }
        }
        catch (OperationCanceledException error) when (caller.IsCancellationRequested || stopping.IsCancellationRequested)
        {
            canceled = error.CancellationToken;
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            // Linked cancellation and every private candidate buffer have settled before publication.
            Complete(completion, failure, canceled);
        }
    }

    private static void Complete(TaskCompletionSource completion, Exception? failure, CancellationToken? canceled)
    {
        if (failure is not null)
        {
            completion.TrySetException(failure);
        }
        else if (canceled is { } token)
        {
            completion.TrySetCanceled(token);
        }
        else
        {
            completion.TrySetResult();
        }
    }

    private RestHttpCertificateMaterial CreateMaterial(PemCandidate candidate, CancellationToken token, out DateTimeOffset expiry)
    {
        token.ThrowIfCancellationRequested();
        var publicChain = new X509Certificate2Collection();
        X509Certificate2? leaf = null;
        char[] chainCharacters = [];
        char[] keyCharacters = [];
        try
        {
            try
            {
                chainCharacters = CertificatePemFiles.Characters(candidate.Chain);
                keyCharacters = CertificatePemFiles.Characters(candidate.Key);
                CertificatePemFiles.CheckBlocks(chainCharacters, privateKey: false);
                CertificatePemFiles.CheckBlocks(keyCharacters, privateKey: true);
                publicChain.ImportFromPem(chainCharacters);
                leaf = X509Certificate2.CreateFromPem(chainCharacters, keyCharacters);
                if (leaf.NotBefore.ToUniversalTime() > clock.GetUtcNow().UtcDateTime)
                {
                    throw new PemCredentialRejection(new(SecurityFailureKind.CredentialInvalid,
                        "The candidate certificate is not valid yet."), leaf.NotBefore.ToUniversalTime());
                }
                CertificateValidationResult validation = CertificateTrustValidator.Validate(leaf, trust!, clock.GetUtcNow());
                token.ThrowIfCancellationRequested();
                if (!validation.Succeeded || validation.Subject != Binding!.CredentialIdentity)
                {
                    throw new PemCredentialRejection(validation.Failure ?? new(SecurityFailureKind.CredentialInvalid,
                        "The certificate candidate does not match the fixed local credential mapping."),
                        validation.Failure?.Kind == SecurityFailureKind.ValidationUnavailable ? clock.GetUtcNow().Add(options.PollInterval) : null);
                }
                expiry = validation.ExpiresAtUtc!.Value;
            }
            catch (Exception error) when (error is CryptographicException or ArgumentException or InvalidDataException)
            {
                throw new PemCredentialRejection(new(SecurityFailureKind.CredentialInvalid,
                    "The bounded PEM candidate has invalid certificate/key/chain data."));
            }
            token.ThrowIfCancellationRequested();
            var native = new RestHttpNativeCertificate(leaf);
            leaf = null;
            try
            {
                native.PrepareForTls(keys!);
                token.ThrowIfCancellationRequested();
                if (expiry <= clock.GetUtcNow())
                {
                    throw new PemCredentialRejection(new(SecurityFailureKind.CredentialExpired,
                        "The candidate expired before native generation publication."));
                }
                return RestHttpCertificateMaterial.FromNativeCertificate(native,
                    publicChain.Cast<X509Certificate2>().Skip(1).ToArray());
            }
            catch (Exception original)
            {
                try
                {
                    native.Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(original, cleanup);
                }
                throw;
            }
        }
        finally
        {
            leaf?.Dispose();
            foreach (X509Certificate2 certificate in publicChain)
            {
                certificate.Dispose();
            }
            Array.Clear(chainCharacters);
            Array.Clear(keyCharacters);
        }
    }

    private void Record(string fingerprint, SecurityCertificateReloadOutcome outcome, SecurityFailure? failure)
    {
        bool changed = fingerprint != lastFingerprint || outcome != lastOutcome;
        lastFingerprint = fingerprint;
        lastOutcome = outcome;
        lastCandidateFailure = failure;
        availability.SetOutbound(activeExpiry, failure);
        if (!changed)
        {
            return;
        }
        reporter.RecordCertificateReload(profile.Alias, outcome);
        long now = clock.GetTimestamp();
        if (diagnosticCount >= 64 || diagnosticCount >= 4 && clock.GetElapsedTime(lastDiagnostic, now) < TimeSpan.FromMinutes(1))
        {
            return;
        }
        diagnosticCount++;
        lastDiagnostic = now;
        try
        {
            logger?.Log(outcome == SecurityCertificateReloadOutcome.Rejected ? LogLevel.Warning : LogLevel.Debug,
                "Certificate source {Profile} changed to {Outcome}; {Code}", profile.Alias, outcome, failure?.Code);
        }
        catch (Exception)
        {
            // Logging never changes credential adoption or authorization.
        }
    }

    public async Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
    {
        await CheckAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task OnErrorAsync(Exception exception) => Task.CompletedTask;

    private void BeginStopping()
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }
            closed = true;
        }
        try
        {
            stopping.Cancel();
        }
        catch (Exception error)
        {
            cutoffFailure = error;
        }
        try
        {
            heartbeatDrain = heartbeat?.CloseAsync();
        }
        catch (Exception error)
        {
            cutoffFailure = cutoffFailure is null ? error : new AggregateException(cutoffFailure, error);
        }
        finally
        {
            cutoff.TrySetResult();
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task work;
        lock (gate)
        {
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }
            work = disposal;
        }
        if (completion is not null)
        {
            _ = DisposeCoreAsync(completion);
        }
        return new(work);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        BeginStopping();
        await cutoff.Task.ConfigureAwait(false);
        var errors = new List<Exception>();
        await Capture(async () =>
        {
            if (startup is not null)
            {
                try
                {
                    await startup.ConfigureAwait(false);
                }
                catch (Exception) { /* The startup owner retains its original outcome. */ }
            }
            if (checking is not null)
            {
                try
                {
                    await checking.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            }
        }).ConfigureAwait(false);
        await Capture(async () =>
        {
            if (heartbeatDrain is not null)
            {
                await heartbeatDrain.ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        await Capture(async () =>
        {
            if (heartbeat is not null)
            {
                await heartbeat.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        await Capture(async () =>
        {
            if (transport is not null)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        await Capture(() =>
        {
            if (borrowedKeys is null)
            {
                keys?.Dispose();
            }
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await Capture(() =>
        {
            stopRegistration.Dispose();
            stopping.Dispose();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        if (cutoffFailure is not null)
        {
            errors.Add(cutoffFailure);
        }
        if (errors.Count == 0)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(errors.Count == 1 ? errors[0] : new AggregateException(errors));
        }

        async Task Capture(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception error) { errors.Add(error); }
        }
    }
}

internal sealed class PemCredentialRejection(SecurityFailure failure, DateTimeOffset? retryAt = null) : Exception(failure.Message)
{
    internal SecurityFailure Failure { get; } = failure;
    internal DateTimeOffset? RetryAt { get; } = retryAt;
}
