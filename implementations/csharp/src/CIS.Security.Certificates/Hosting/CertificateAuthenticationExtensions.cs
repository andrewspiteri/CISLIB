using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CIS.Security.Hosting;
using CIS.Startup;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CIS.Security.Certificates.Hosting;

/// <summary>Explicit native certificate scheme registration over the one existing CIS security policy/provider.</summary>
public static class CertificateAuthenticationExtensions
{
    /// <summary>Registers a receiver without outgoing credential requirements or registration-time I/O.</summary>
    public static IServiceCollection AddCisCertificateAuthentication(this IServiceCollection services,
        string profile, string authenticationScheme, CertificateInboundOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return services.AddCisCertificateAuthentication(profile, authenticationScheme, _ => options);
    }

    /// <summary>Defers immutable receiver construction until the effective common policy has been prepared.</summary>
    /// <remarks>A configuration reader factory must construct the immutable trust/options inside its reader callback.</remarks>
    public static IServiceCollection AddCisCertificateAuthentication(this IServiceCollection services,
        string profile, string authenticationScheme, Func<IServiceProvider, CertificateInboundOptions> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        _ = new SecurityProfile(profile, SecurityProfileKind.ClientCertificate, SecurityProfileRoles.Inbound, authenticationScheme);
        services.AddKeyedSingleton<CertificateInboundRuntime>(profile,
            (provider, _) => new(provider, services, profile, authenticationScheme, factory));
        services.AddSingleton(new CertificateReceivingRegistration(profile));
        new AuthenticationBuilder(services).AddCertificate(authenticationScheme, _ => { });
        services.AddSingleton<IConfigureOptions<CertificateAuthenticationOptions>>(provider =>
            new CertificateNativeOptions(authenticationScheme, provider.GetRequiredKeyedService<CertificateInboundRuntime>(profile)));
        services.AddSingleton<IPostConfigureOptions<CertificateAuthenticationOptions>>(provider =>
            new CertificateNativeValidation(authenticationScheme, provider.GetRequiredKeyedService<CertificateInboundRuntime>(profile)));
        services.AddSingleton<IValidateOptions<CertificateAuthenticationOptions>>(provider =>
            new CertificateFinalNativeValidation(authenticationScheme, provider.GetRequiredKeyedService<CertificateInboundRuntime>(profile)));
        services.AddCisSecurityProjection(profile, provider => new CertificateIdentityProjector(
            provider.GetRequiredKeyedService<CertificateInboundRuntime>(profile)));
        services.AddSingleton<IStartable>(provider => new CertificateInboundStartable(
            provider.GetRequiredKeyedService<CertificateInboundRuntime>(profile)));
        return services;
    }
}

internal sealed class CertificateNativeOptions(string scheme, CertificateInboundRuntime runtime)
    : IConfigureNamedOptions<CertificateAuthenticationOptions>
{
    public void Configure(CertificateAuthenticationOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, CertificateAuthenticationOptions options)
    {
        if (string.Equals(name, scheme, StringComparison.Ordinal))
        {
            runtime.Configure(options);
        }
    }
}

internal sealed class CertificateNativeValidation(string scheme, CertificateInboundRuntime runtime)
    : IPostConfigureOptions<CertificateAuthenticationOptions>
{
    public void PostConfigure(string? name, CertificateAuthenticationOptions options)
    {
        if (string.Equals(name, scheme, StringComparison.Ordinal))
        {
            runtime.ValidateNativeOptions(options);
        }
    }
}

internal sealed class CertificateFinalNativeValidation(string scheme, CertificateInboundRuntime runtime)
    : IValidateOptions<CertificateAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, CertificateAuthenticationOptions options)
    {
        if (!string.Equals(name, scheme, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Skip;
        }
        try
        {
            runtime.ValidateNativeOptions(options);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException error)
        {
            return ValidateOptionsResult.Fail(error.Message);
        }
    }
}

internal sealed class CertificateInboundStartable(CertificateInboundRuntime runtime) : IStartable
{
    public Task StartAsync(CancellationToken cancellationToken) => runtime.StartAsync(cancellationToken);
}

internal sealed class CertificateInboundRuntime(IServiceProvider provider, IServiceCollection registrations,
    string profileAlias, string scheme, Func<IServiceProvider, CertificateInboundOptions> factory) : IDisposable
{
    private readonly object gate = new();
    private CertificateInboundOptions? options;
    private SecurityProfile? profile;
    private X509Certificate2[] roots = [];
    private X509Certificate2[] chainCertificates = [];
    private bool disposed;

    internal CertificateInboundOptions Options
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (options is null)
                {
                    SecurityProfile selected = provider.GetRequiredService<SecurityPolicySnapshot>().GetProfile(profileAlias);
                    CertificateInboundOptions captured = factory(provider)
                        ?? throw new InvalidOperationException("A certificate receiver factory must return immutable options.");
                    if ((selected.Roles & SecurityProfileRoles.Inbound) == 0
                        || selected.Kind is not (SecurityProfileKind.ClientCertificate or SecurityProfileKind.LocalDevelopment)
                        || !string.Equals(selected.AuthenticationScheme, scheme, StringComparison.Ordinal)
                        || (selected.Kind == SecurityProfileKind.LocalDevelopment) != (captured.Development is not null))
                    {
                        throw new InvalidOperationException("Certificate receiver role, scheme, profile kind and development trust must agree.");
                    }
                    if (selected.IdentityMappings.Any(mapping => mapping.Subject.Issuer != captured.Trust.TrustDomain
                        || mapping.ClientApplicationId is not null || !Uri.TryCreate(mapping.Subject.Identifier, UriKind.Absolute, out _)))
                    {
                        throw new InvalidOperationException("Certificate mappings require the configured issuer domain and exact absolute URI identities.");
                    }
                    captured.Development?.Validate(provider.GetService<IHostEnvironment>());
                    if (registrations.Any(descriptor => !descriptor.IsKeyedService
                        && descriptor.ServiceType == typeof(ICertificateValidationCache)))
                    {
                        throw new InvalidOperationException("CIS certificate profiles do not permit a principal/certificate-result cache.");
                    }
                    options = captured;
                    profile = selected;
                }
                return options;
            }
        }
    }

    internal TimeProvider Clock => provider.GetRequiredService<TimeProvider>();

    internal void Configure(CertificateAuthenticationOptions native)
    {
        lock (gate)
        {
            CertificateTrustPolicy trust = Options.Trust;
            if (roots.Length == 0)
            {
                roots = trust.LoadRoots();
                try
                {
                    chainCertificates = trust.LoadChainCertificates();
                }
                catch
                {
                    foreach (X509Certificate2 root in roots)
                    {
                        root.Dispose();
                    }
                    roots = [];
                    throw;
                }
            }
            native.AllowedCertificateTypes = CertificateTypes.Chained;
            native.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
            native.CustomTrustStore = new(roots);
            native.AdditionalChainCertificates = new(chainCertificates);
            native.ValidateCertificateUse = true;
            native.ValidateValidityPeriod = true;
            native.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            // The handler does not expose URL timeouts. Its mandatory event performs the
            // selected native revocation check with bounded retrieval before a ticket exists.
            native.RevocationMode = X509RevocationMode.NoCheck;
            native.Events = new CertificateAuthenticationEvents { OnCertificateValidated = ValidateNativeAsync };
        }
    }

    internal void ValidateNativeOptions(CertificateAuthenticationOptions native)
    {
        if (native.ChainTrustValidationMode != X509ChainTrustMode.CustomRootTrust
            || native.AllowedCertificateTypes != CertificateTypes.Chained || !native.ValidateCertificateUse
            || !native.ValidateValidityPeriod || native.RevocationMode != X509RevocationMode.NoCheck
            || native.EventsType is not null || native.Events?.OnCertificateValidated != ValidateNativeAsync
            || native.ForwardAuthenticate is not null || native.ForwardDefault is not null || native.ForwardDefaultSelector is not null
            || native.ForwardChallenge is not null || native.ForwardForbid is not null
            || native.ForwardSignIn is not null || native.ForwardSignOut is not null)
        {
            throw new InvalidOperationException("Preserve the mandatory strict CIS certificate validation event and native baseline.");
        }
    }

    private Task ValidateNativeAsync(CertificateValidatedContext context)
    {
        CancellationToken token = context.HttpContext.RequestAborted;
        token.ThrowIfCancellationRequested();
        context.HttpContext.Features.Set<ValidatedCertificateEvidence>(null);
        CertificateInboundOptions selected = Options;
        if (selected.Development is { } development && !development.Permits(context.HttpContext))
        {
            context.Fail("The actual listener does not permit the selected development profile.");
            return Task.CompletedTask;
        }
        DateTimeOffset now = Clock.GetUtcNow();
        CertificateValidationResult result = CertificateTrustValidator.Validate(context.ClientCertificate, selected.Trust, now);
        // Native synchronous work is joined before observing cancellation; no timeout task abandons it.
        token.ThrowIfCancellationRequested();
        if (!result.Succeeded)
        {
            context.Fail(result.Failure!.Message);
            return Task.CompletedTask;
        }
        if (!profile!.IdentityMappings.Any(mapping => mapping.Subject == result.Subject))
        {
            context.Fail("The exact certificate URI has no trusted identity mapping in the selected profile.");
            return Task.CompletedTask;
        }
        context.HttpContext.Features.Set(new ValidatedCertificateEvidence(scheme, now, result));
        context.Success();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Options;
        Options.Trust.ValidateAuthorities(Clock.GetUtcNow());
        ValidateNativeOptions(provider.GetRequiredService<IOptionsMonitor<CertificateAuthenticationOptions>>().Get(scheme));
        foreach (CertificateReceivingRegistration registration in provider.GetServices<CertificateReceivingRegistration>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CertificateInboundOptions other = provider.GetRequiredKeyedService<CertificateInboundRuntime>(registration.Profile).Options;
            if ((Options.Development is null) != (other.Development is null) && Options.Trust.SharesRoot(other.Trust))
            {
                throw new InvalidOperationException("Production and development client trust cannot share a root within one host.");
            }
        }
        // Receiving-only availability concerns trust/scheme setup, not an unused outgoing leaf.
        if (profile!.Roles == SecurityProfileRoles.Inbound)
        {
            provider.GetRequiredService<ISecurityProfileStatusReporter>().Publish(
                new(profileAlias, SecurityProfileAvailability.Available));
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            foreach (X509Certificate2 certificate in roots.Concat(chainCertificates))
            {
                certificate.Dispose();
            }
            roots = [];
            chainCertificates = [];
        }
    }
}

internal sealed record CertificateReceivingRegistration(string Profile);

internal sealed record ValidatedCertificateEvidence(string Scheme, DateTimeOffset ValidatedAtUtc, CertificateValidationResult Result);

internal sealed class CertificateIdentityProjector(CertificateInboundRuntime runtime) : ISecurityIdentityProjector
{
    public async ValueTask<SecurityProjectionResult> ProjectAsync(HttpContext context, AuthenticateResult authentication,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CertificateInboundOptions options = runtime.Options;
        if (!context.Request.IsHttps || options.Development is { } development && !development.Permits(context))
        {
            return SecurityProjectionResult.Rejected(new(SecurityFailureKind.AuthenticationRejected,
                "The actual native listener does not permit this certificate profile."));
        }
        X509Certificate2? certificate = await context.Connection.GetClientCertificateAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (certificate is null)
        {
            return SecurityProjectionResult.Rejected(new(SecurityFailureKind.CredentialUnavailable,
                "The native TLS connection has no client certificate."));
        }
        ValidatedCertificateEvidence? evidence = context.Features.Get<ValidatedCertificateEvidence>();
        if (evidence is null || evidence.Scheme != authentication.Ticket?.AuthenticationScheme
            || !string.Equals(evidence.Result.CredentialIdentifier,
                Convert.ToHexStringLower(SHA256.HashData(certificate.RawDataMemory.Span)), StringComparison.Ordinal))
        {
            return SecurityProjectionResult.Rejected(new(SecurityFailureKind.ValidationUnavailable,
                "The native certificate authentication has no current strict validation evidence."));
        }
        CertificateValidationResult validation = evidence.Result;
        cancellationToken.ThrowIfCancellationRequested();
        return validation.Succeeded
            ? SecurityProjectionResult.Authenticated(new(validation.Subject!, SecurityActorKind.Application,
                new(evidence.ValidatedAtUtc, validation.ExpiresAtUtc, validation.CredentialIdentifier), "mutual-tls"))
            : SecurityProjectionResult.Rejected(validation.Failure!);
    }
}
