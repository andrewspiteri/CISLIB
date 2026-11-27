using System.Diagnostics.CodeAnalysis;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CIS.Management.Service.Security;

/// <summary>The fixed application-local mixed provider; it does not alter the workload Entra adapter.</summary>
internal static class ManagementEntraSecurity
{
    internal static void Configure(IServiceCollection services, ManagementSecurityOptions options)
    {
        if (options.EntraMetadataManager is null)
        {
            services.AddKeyedSingleton<ManagementEntraMetadata>(options.Profile, (provider, _) =>
                new(provider.GetRequiredService<IHostApplicationLifetime>()));
        }

        services.AddKeyedSingleton<ManagementEntraProvider>(options.Profile, (provider, _) =>
        {
            IConfigurationManager<OpenIdConnectConfiguration> metadata = options.EntraMetadataManager
                ?? new ConfigurationManager<OpenIdConnectConfiguration>(ManagementEntraIdentity.Issuer(options)
                    + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(),
                    provider.GetRequiredKeyedService<ManagementEntraMetadata>(options.Profile));
            return new(options, metadata, provider.GetService<TimeProvider>() ?? TimeProvider.System);
        });

        services.AddAuthentication().AddJwtBearer(options.AuthenticationScheme);
        services.AddOptions<JwtBearerOptions>(options.AuthenticationScheme)
            .Configure<IServiceProvider>((native, provider) => ManagementEntraJwt.Apply(native,
                provider.GetRequiredKeyedService<ManagementEntraProvider>(options.Profile)))
            .PostConfigure<IServiceProvider>((native, provider) => ManagementEntraJwt.Apply(native,
                provider.GetRequiredKeyedService<ManagementEntraProvider>(options.Profile)));
        services.AddSingleton<IValidateOptions<JwtBearerOptions>>(provider => new ManagementEntraJwtValidator(
            provider.GetRequiredKeyedService<ManagementEntraProvider>(options.Profile)));
        services.AddCisSecurityProjection(options.Profile, provider =>
            provider.GetRequiredKeyedService<ManagementEntraProvider>(options.Profile));
    }
}

internal sealed class ManagementEntraProvider : JwtBearerEvents, ISecurityIdentityProjector
{
    private const string EvidenceKey = "CIS.Management.Entra.NativeValidation";
    private readonly object validationOwner = new();
    private readonly TimeProvider clock;

    internal ManagementEntraProvider(ManagementSecurityOptions options,
        IConfigurationManager<OpenIdConnectConfiguration> metadata, TimeProvider clock)
    {
        Options = options;
        Metadata = metadata;
        this.clock = clock;
    }

    internal ManagementSecurityOptions Options
    {
        get;
    }

    internal IConfigurationManager<OpenIdConnectConfiguration> Metadata
    {
        get;
    }

    public override Task MessageReceived(MessageReceivedContext context)
    {
        return Task.CompletedTask;
    }

    public override Task AuthenticationFailed(AuthenticationFailedContext context)
    {
        return Task.CompletedTask;
    }

    public override Task Challenge(JwtBearerChallengeContext context)
    {
        return Task.CompletedTask;
    }

    public override Task Forbidden(ForbiddenContext context)
    {
        return Task.CompletedTask;
    }

    public override Task TokenValidated(TokenValidatedContext context)
    {
        if (context.Principal is null || !ManagementEntraIdentity.TryRead(context.Principal, Options, out ManagementEntraIdentity? identity))
        {
            context.Fail("The token does not satisfy the selected Management workload/operator profile.");
        }
        else
        {
            var expiry = new DateTimeOffset(DateTime.SpecifyKind(context.SecurityToken.ValidTo, DateTimeKind.Utc));
            context.Properties.ExpiresUtc = expiry;
            context.Properties.Parameters[EvidenceKey] = new ValidatedEvidence(validationOwner, context.Principal, identity!, expiry);
        }

        return Task.CompletedTask;
    }

    public ValueTask<SecurityProjectionResult> ProjectAsync(HttpContext context, AuthenticateResult authentication,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!authentication.Succeeded || authentication.Principal is null
            || authentication.Ticket?.AuthenticationScheme != Options.AuthenticationScheme
            || !TryGetEvidence(authentication, out ValidatedEvidence? evidence))
        {
            return ValueTask.FromResult(Rejected());
        }

        DateTimeOffset now = clock.GetUtcNow();
        if (evidence.Expiry < now - Options.EntraClockSkew)
        {
            return ValueTask.FromResult(SecurityProjectionResult.Rejected(new(SecurityFailureKind.CredentialExpired,
                "The native Management authentication evidence is outside its lifetime tolerance.")));
        }

        string? reported = null;
        if (context.Request.Headers.TryGetValue(EntraSecurityDefaults.ReportedInstanceHeader, out var values))
        {
            if (values.Count != 1)
            {
                return ValueTask.FromResult(Rejected());
            }

            reported = values[0];
        }

        ManagementEntraIdentity identity = evidence.Identity;
        try
        {
            var projected = new AuthenticatedSecurityIdentity(identity.Subject, identity.ActorKind,
                new(now, evidence.Expiry), identity.ActorKind == SecurityActorKind.User ? "entra-v2-delegated" : "entra-v2-workload",
                identity.Roles, identity.ClientId, new CallerInstanceEvidence(reported),
                humanSubject: identity.ActorKind == SecurityActorKind.User ? identity.Subject : null);
            return ValueTask.FromResult(SecurityProjectionResult.Authenticated(projected));
        }
        catch (ArgumentException)
        {
            return ValueTask.FromResult(Rejected());
        }
    }

    private bool TryGetEvidence(AuthenticateResult authentication, [NotNullWhen(true)] out ValidatedEvidence? evidence)
    {
        evidence = null;
        if (authentication.Properties?.Parameters.TryGetValue(EvidenceKey, out object? value) != true
            || value is not ValidatedEvidence proof || !ReferenceEquals(proof.Owner, validationOwner)
            || !ReferenceEquals(proof.Principal, authentication.Principal))
        {
            return false;
        }

        evidence = proof;
        return true;
    }

    private static SecurityProjectionResult Rejected()
    {
        return SecurityProjectionResult.Rejected(new(SecurityFailureKind.CredentialInvalid,
            "The credential does not satisfy the selected Management identity profile."));
    }

    private sealed record ValidatedEvidence(object Owner, System.Security.Claims.ClaimsPrincipal Principal,
        ManagementEntraIdentity Identity, DateTimeOffset Expiry);
}
