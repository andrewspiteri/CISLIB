using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CIS.Security.Entra;

internal static class EntraJwtConfiguration
{
    internal static void Apply(JwtBearerOptions native, EntraProfileRuntime runtime)
    {
        EntraOptionsSnapshot options = runtime.Options;
        if ((options.Profile.Roles & SecurityProfileRoles.Inbound) == 0)
        {
            throw new InvalidOperationException("Only an explicitly enabled receiving role configures JWT authentication.");
        }
        native.Authority = options.Issuer;
        native.MetadataAddress = options.Issuer + "/.well-known/openid-configuration";
        native.Audience = options.Audience;
        native.RequireHttpsMetadata = true;
        native.MapInboundClaims = false;
        native.SaveToken = false;
        native.IncludeErrorDetails = false;
        native.RefreshOnIssuerKeyNotFound = true;
        native.UseSecurityTokenValidators = false;
        native.ForwardDefault = null;
        native.ForwardAuthenticate = null;
        native.ForwardChallenge = null;
        native.ForwardForbid = null;
        native.ForwardDefaultSelector = null;
        native.ConfigurationManager = options.MetadataManager ?? runtime.GetMetadataManager();
        native.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            RequireAudience = true,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = options.ClockSkew,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            AuthenticationType = options.Profile.AuthenticationScheme,
            NameClaimType = "oid",
            RoleClaimType = "roles",
            IncludeTokenOnFailedValidation = false,
            LogValidationExceptions = false
        };
        native.TokenHandlers.Clear();
        native.TokenHandlers.Add(new JsonWebTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 128 * 1024 });
        native.Events = runtime.GetJwtEvents();
    }
}

// A sealed native event owner cannot be replaced through mutable delegate properties.
// Evidence lives on the actual server-side ticket, not in a new principal/cache service.
internal sealed class EntraJwtEvents(EntraProfileRuntime runtime) : JwtBearerEvents
{
    private const string EvidenceKey = "CIS.Security.Entra.NativeValidation";

    public override Task MessageReceived(MessageReceivedContext context) => Task.CompletedTask;
    public override Task AuthenticationFailed(AuthenticationFailedContext context) => Task.CompletedTask;
    public override Task Challenge(JwtBearerChallengeContext context) => Task.CompletedTask;
    public override Task Forbidden(ForbiddenContext context) => Task.CompletedTask;

    public override Task TokenValidated(TokenValidatedContext context)
    {
        if (context.Principal is null || !EntraClaims.TryRead(context.Principal, runtime.Options, out EntraClaims? claims))
        {
            context.Fail("The token does not satisfy the selected app-only workload profile.");
        }
        else
        {
            var expiry = new DateTimeOffset(DateTime.SpecifyKind(context.SecurityToken.ValidTo, DateTimeKind.Utc));
            context.Properties.ExpiresUtc = expiry;
            context.Properties.Parameters[EvidenceKey] = new EntraValidatedEvidence(runtime.ValidationOwner, context.Principal, claims, expiry);
        }
        return Task.CompletedTask;
    }

    internal static bool TryGetEvidence(AuthenticateResult authentication, EntraProfileRuntime expected,
        [NotNullWhen(true)] out EntraValidatedEvidence? evidence)
    {
        evidence = null;
        if (authentication.Properties?.Parameters.TryGetValue(EvidenceKey, out object? value) != true
            || value is not EntraValidatedEvidence proof || !ReferenceEquals(proof.Owner, expected.ValidationOwner)
            || !ReferenceEquals(proof.Principal, authentication.Principal))
        {
            return false;
        }
        evidence = proof;
        return true;
    }
}

internal sealed record EntraValidatedEvidence(object Owner, ClaimsPrincipal Principal, EntraClaims Claims, DateTimeOffset Expiry);

// IValidateOptions runs after every native post-configuration delegate, including later host registrations.
internal sealed class EntraJwtOptionsValidator(string scheme, EntraProfileRuntime runtime) : IValidateOptions<JwtBearerOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtBearerOptions value)
    {
        if (!string.Equals(name, scheme, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Skip;
        }
        EntraOptionsSnapshot expected = runtime.Options;
        if (value.TokenValidationParameters is null)
        {
            return ValidateOptionsResult.Fail("The selected Entra scheme requires its fixed native validation parameters.");
        }
        TokenValidationParameters parameters = value.TokenValidationParameters;
        bool valid = value.Authority == expected.Issuer && value.MetadataAddress == expected.Issuer + "/.well-known/openid-configuration"
            && value.Audience == expected.Audience && value.RequireHttpsMetadata && !value.MapInboundClaims
            && !value.SaveToken && !value.IncludeErrorDetails && !value.UseSecurityTokenValidators
            && value.EventsType is null && ReferenceEquals(value.Events, runtime.GetJwtEvents())
            && ReferenceEquals(value.ConfigurationManager, expected.MetadataManager ?? runtime.GetMetadataManager())
            && value.ForwardDefault is null && value.ForwardAuthenticate is null && value.ForwardChallenge is null
            && value.ForwardForbid is null && value.ForwardDefaultSelector is null
            && value.TokenHandlers.Count == 1 && value.TokenHandlers[0].GetType() == typeof(JsonWebTokenHandler)
            && value.TokenHandlers[0] is JsonWebTokenHandler { MapInboundClaims: false, MaximumTokenSizeInBytes: 131072 }
            && parameters.GetType() == typeof(TokenValidationParameters)
            && parameters.ValidateIssuer && parameters.ValidIssuer == expected.Issuer && parameters.ValidIssuers is null
            && parameters.ValidateAudience && parameters.RequireAudience && !parameters.IgnoreTrailingSlashWhenValidatingAudience
            && parameters.ValidAudience == expected.Audience && parameters.ValidAudiences is null
            && parameters.ValidateIssuerSigningKey && parameters.RequireSignedTokens && parameters.ValidateLifetime
            && parameters.RequireExpirationTime && parameters.ClockSkew == expected.ClockSkew
            && parameters.AuthenticationType == scheme && parameters.NameClaimType == "oid" && parameters.RoleClaimType == "roles"
            && parameters.IssuerSigningKey is null && parameters.IssuerSigningKeys is null && parameters.CryptoProviderFactory is null
            && parameters.ConfigurationManager is null
            && parameters.ValidAlgorithms is not null && parameters.ValidAlgorithms.Take(2).SequenceEqual([SecurityAlgorithms.RsaSha256], StringComparer.Ordinal)
            && !parameters.IncludeTokenOnFailedValidation && !parameters.LogValidationExceptions;
        if (valid)
        {
            // The supported native surface has several resolver/validator/reader hooks.
            // None is part of this scheme's contract; a future delegate must not silently bypass its native checks.
            valid = typeof(TokenValidationParameters).GetProperties().Where(property => typeof(Delegate).IsAssignableFrom(property.PropertyType))
                .All(property => property.GetValue(parameters) is null);
        }
        return valid ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("The selected Entra scheme's fixed native validation, event or forwarding ownership was replaced.");
    }
}
