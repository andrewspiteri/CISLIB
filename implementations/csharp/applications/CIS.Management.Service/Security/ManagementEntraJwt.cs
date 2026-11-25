using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CIS.Management.Service.Security;

internal static class ManagementEntraJwt
{
    internal static void Apply(JwtBearerOptions native, ManagementEntraProvider provider)
    {
        ManagementSecurityOptions options = provider.Options;
        string issuer = ManagementEntraIdentity.Issuer(options);
        native.Authority = issuer;
        native.MetadataAddress = issuer + "/.well-known/openid-configuration";
        native.Audience = options.EntraAudience;
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
        native.ForwardSignIn = null;
        native.ForwardSignOut = null;
        native.ForwardDefaultSelector = null;
        native.ConfigurationManager = provider.Metadata;
        native.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            RequireAudience = true,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidAudience = options.EntraAudience,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = options.EntraClockSkew,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            AuthenticationType = options.AuthenticationScheme,
            NameClaimType = "oid",
            RoleClaimType = "roles",
            IncludeTokenOnFailedValidation = false,
            LogValidationExceptions = false
        };
        native.TokenHandlers.Clear();
        native.TokenHandlers.Add(new JsonWebTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 128 * 1024 });
        native.EventsType = null;
        native.Events = provider;
    }
}

internal sealed class ManagementEntraJwtValidator(ManagementEntraProvider provider) : IValidateOptions<JwtBearerOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtBearerOptions value)
    {
        ManagementSecurityOptions options = provider.Options;
        if (name != options.AuthenticationScheme)
        {
            return ValidateOptionsResult.Skip;
        }

        TokenValidationParameters? parameters = value.TokenValidationParameters;
        string issuer = ManagementEntraIdentity.Issuer(options);
        bool valid = parameters is not null && parameters.GetType() == typeof(TokenValidationParameters)
            && value.Authority == issuer && value.MetadataAddress == issuer + "/.well-known/openid-configuration"
            && value.Audience == options.EntraAudience && value.RequireHttpsMetadata && !value.MapInboundClaims
            && !value.SaveToken && !value.IncludeErrorDetails && !value.UseSecurityTokenValidators
            && value.RefreshOnIssuerKeyNotFound && value.EventsType is null && ReferenceEquals(value.Events, provider)
            && ReferenceEquals(value.ConfigurationManager, provider.Metadata)
            && value.ForwardDefault is null && value.ForwardAuthenticate is null && value.ForwardChallenge is null
            && value.ForwardForbid is null && value.ForwardSignIn is null && value.ForwardSignOut is null && value.ForwardDefaultSelector is null
            && value.TokenHandlers.Count == 1 && value.TokenHandlers[0].GetType() == typeof(JsonWebTokenHandler)
            && value.TokenHandlers[0] is JsonWebTokenHandler { MapInboundClaims: false, MaximumTokenSizeInBytes: 131072 }
            && parameters.ValidateIssuer && parameters.ValidIssuer == issuer && parameters.ValidIssuers is null
            && parameters.ValidateAudience && parameters.RequireAudience && !parameters.IgnoreTrailingSlashWhenValidatingAudience
            && parameters.ValidAudience == options.EntraAudience && parameters.ValidAudiences is null
            && parameters.ValidateIssuerSigningKey && parameters.RequireSignedTokens && parameters.ValidateLifetime
            && parameters.RequireExpirationTime && parameters.ClockSkew == options.EntraClockSkew
            && parameters.AuthenticationType == options.AuthenticationScheme && parameters.NameClaimType == "oid" && parameters.RoleClaimType == "roles"
            && parameters.IssuerSigningKey is null && parameters.IssuerSigningKeys is null && parameters.CryptoProviderFactory is null
            && parameters.ConfigurationManager is null && !parameters.IncludeTokenOnFailedValidation && !parameters.LogValidationExceptions
            && parameters.ValidAlgorithms is not null && parameters.ValidAlgorithms.Take(2).SequenceEqual([SecurityAlgorithms.RsaSha256], StringComparer.Ordinal);
        if (valid)
        {
            valid = typeof(TokenValidationParameters).GetProperties().Where(property => typeof(Delegate).IsAssignableFrom(property.PropertyType))
                .All(property => property.GetValue(parameters) is null);
        }

        return valid ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Preserve the selected Management provider's native validation, events, keys and forwarding ownership.");
    }
}
