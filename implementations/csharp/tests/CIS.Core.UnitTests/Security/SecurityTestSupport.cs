using System.Security.Claims;
using CIS.CommonTesting;
using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Tests.UnitTests.Security;

internal static class SecurityTestSupport
{
    internal const string Profile = "fixture";
    internal static readonly SecuritySubject Subject = new("issuer-a", "application-a");
    internal static readonly CapabilityTarget Target = new("target-a", "Development");
    internal static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static SecurityOptions Options(SecurityProfileKind kind = SecurityProfileKind.Native,
        string? additionalPolicy = null, string[]? mappedRoles = null)
    {
        var options = new SecurityOptions { PolicyVersion = "fixture-policy" };
        options.Profiles.Add(new(Profile, kind, SecurityProfileRoles.Inbound, authenticationScheme: Profile,
            identityMappings:
            [
                new(Subject, "caller-a", "Development", mappedRoles,
                    clientApplicationId: kind == SecurityProfileKind.EntraWorkloadIdentity ? "client-a" : null)
            ], additionalAuthorizationPolicy: additionalPolicy));
        foreach (string name in new[] { "rules.read", "rules.publish", "rules.unknown" })
        {
            options.Capabilities.Add(name);
        }
        return options;
    }

    internal static ServiceCollection Services(SecurityOptions options, RecordingAudit? audit = null,
        string[]? actualRoles = null, DateTimeOffset? expiry = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));
        if (audit is not null)
        {
            services.AddSingleton<ISecurityAuditSink>(audit);
        }
        services.AddCisSecurity(options);
        services.AddCisSecurityProjection(Profile, _ => new FixtureProjector(actualRoles, expiry));
        return services;
    }

    internal static AuthenticateResult NativeAuthentication(SecuritySubject? subject = null, bool human = false)
    {
        subject ??= Subject;
        var identity = new ClaimsIdentity([
            new Claim("iss", subject.Issuer),
            new Claim("sub", subject.Identifier),
            new Claim("actor", human ? "user" : "application")
        ], Profile);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Profile));
    }

    internal sealed class FixtureProjector(string[]? roles, DateTimeOffset? expiry) : ISecurityIdentityProjector
    {
        public ValueTask<SecurityProjectionResult> ProjectAsync(HttpContext context, AuthenticateResult authentication,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimsPrincipal principal = authentication.Principal!;
            var subject = new SecuritySubject(principal.FindFirst("iss")!.Value, principal.FindFirst("sub")!.Value);
            var evidence = new SecurityAuthenticationEvidence(Now, expiry);
            return ValueTask.FromResult(SecurityProjectionResult.Authenticated(new(subject,
                principal.FindFirst("actor")?.Value == "user" ? SecurityActorKind.User : SecurityActorKind.Application,
                evidence, "native-fixture", roles,
                clientApplicationId: context.RequestServices.GetRequiredService<SecurityPolicySnapshot>()
                    .GetProfile(Profile).Kind == SecurityProfileKind.EntraWorkloadIdentity ? "client-a" : null,
                instance: new CallerInstanceEvidence(context.Request.Headers["X-Reported-Instance"].FirstOrDefault()))));
        }
    }

    internal sealed class RecordingAudit : ISecurityAuditSink
    {
        internal List<SecurityAuthorizationAudit> Decisions { get; } = [];
        internal Exception? Failure
        {
            get; set;
        }

        public ValueTask AuthorizationCompletedAsync(SecurityAuthorizationAudit audit, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return ValueTask.FromException(Failure);
            }
            Decisions.Add(audit);
            return ValueTask.CompletedTask;
        }

        public ValueTask OperationCompletedAsync(SecurityOperationAudit audit, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
