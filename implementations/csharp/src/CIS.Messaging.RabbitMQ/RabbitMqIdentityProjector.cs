using System.Runtime.CompilerServices;
using System.Security.Claims;
using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace CIS.Messaging.RabbitMQ;

internal sealed class RabbitMqEvidenceRegistry
{
    private readonly ConditionalWeakTable<AuthenticationTicket, IssuedEvidence> tickets = new();
    private readonly RabbitMqSettings settings;
    private readonly SecurityProfile profile;
    private readonly TimeProvider clock;

    internal RabbitMqEvidenceRegistry(RabbitMqSettings settings, SecurityPolicySnapshot policy, TimeProvider clock)
    {
        this.settings = settings;
        this.clock = clock;
        profile = policy.GetProfile(settings.SecurityProfile);
        if (profile.Kind != SecurityProfileKind.Native || (profile.Roles & SecurityProfileRoles.Inbound) == 0)
        {
            throw new ArgumentException("The broker selects an explicit Native/Inbound security profile.");
        }
    }

    internal IssuedEvidence? Issue(string? validatedUserId)
    {
        if (string.IsNullOrWhiteSpace(validatedUserId) || validatedUserId.Length > 255)
        {
            return null;
        }
        var subject = new SecuritySubject(settings.SubjectIssuer, validatedUserId);
        SecurityIdentityMapping? mapping = profile.IdentityMappings.SingleOrDefault(item => item.Subject == subject
            && item.ActorKind == SecurityActorKind.Application);
        if (mapping?.Application is null || mapping.Environment is null || mapping.ClientApplicationId is not null)
        {
            return null;
        }
        var identity = new ClaimsIdentity("rabbitmq-user-id");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), profile.AuthenticationScheme!);
        var provenance = new MessageProvenance(settings.BrokerAlias, settings.VirtualHost, subject, validatedUserId,
            mapping.Application, mapping.Environment);
        var issued = new IssuedEvidence(this, ticket, new(settings.SecurityProfile, AuthenticateResult.Success(ticket), provenance),
            new(subject, SecurityActorKind.Application, new(clock.GetUtcNow()), "rabbitmq-user-id"));
        tickets.Add(ticket, issued);
        return issued;
    }

    internal SecurityProjectionResult Project(AuthenticateResult result)
    {
        if (result.Ticket is not { } ticket || !tickets.TryGetValue(ticket, out IssuedEvidence? issued)
            || !issued.IsActive || !ReferenceEquals(ticket.Principal, result.Principal)
            || ticket.AuthenticationScheme != profile.AuthenticationScheme)
        {
            return SecurityProjectionResult.Rejected(new(SecurityFailureKind.ValidationUnavailable,
                "No current trusted broker delivery evidence exists."));
        }
        return SecurityProjectionResult.Authenticated(issued.Identity);
    }

    internal sealed class IssuedEvidence : IDisposable
    {
        private readonly RabbitMqEvidenceRegistry owner;
        private readonly AuthenticationTicket ticket;
        private int active = 1;

        internal IssuedEvidence(RabbitMqEvidenceRegistry owner, AuthenticationTicket ticket, MessageNativeEvidence evidence,
            AuthenticatedSecurityIdentity identity)
        {
            this.owner = owner;
            this.ticket = ticket;
            Evidence = evidence;
            Identity = identity;
        }

        internal MessageNativeEvidence Evidence { get; }
        internal AuthenticatedSecurityIdentity Identity { get; }
        internal bool IsActive => Volatile.Read(ref active) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref active, 0) != 0)
            {
                owner.tickets.Remove(ticket);
            }
        }
    }
}

internal sealed class RabbitMqIdentityProjector(RabbitMqEvidenceRegistry registry) : ISecurityIdentityProjector
{
    public ValueTask<SecurityProjectionResult> ProjectAsync(HttpContext context, AuthenticateResult authentication,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(registry.Project(authentication));
    }
}
