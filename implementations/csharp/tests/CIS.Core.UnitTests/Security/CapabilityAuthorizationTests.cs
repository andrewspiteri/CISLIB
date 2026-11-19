using CIS.CommonTesting;
using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Security;

[TestClass]
public sealed class CapabilityAuthorizationTests
{
    [TestMethod]
    [DataRow("rules.read", "target-a", "Development", true)]
    [DataRow("rules.publish", "target-b", "Production", true)]
    [DataRow("rules.publish", "target-a", "Development", false)]
    [DataRow("rules.read", "target-b", "Production", false)]
    [DataRow("rules.read", "target-a", "development", false)]
    [DataRow("not.registered", "target-a", "Development", false)]
    public async Task ACompleteGrantMatchesEveryDimensionWithoutCrossProducts(string capability,
        string application, string environment, bool expected)
    {
        SecurityOptions options = SecurityTestSupport.Options();
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.publish", "target-b", "Production", subject: SecurityTestSupport.Subject));
        var audit = new SecurityTestSupport.RecordingAudit();
        await using ServiceProvider root = SecurityTestSupport.Services(options, audit).BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());

        AuthorizationDecision result = await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync(capability, new CapabilityTarget(application, environment));

        Assert.AreEqual(expected, result.IsAllowed);
        Assert.HasCount(1, audit.Decisions);
        Assert.AreEqual(expected, audit.Decisions[0].Decision.IsAllowed);
        Assert.AreEqual(new CapabilityTarget(application, environment), audit.Decisions[0].Target);
    }

    [TestMethod]
    [DataRow(CapabilityResourceSelection.TargetOnly, null, null, false)]
    [DataRow(CapabilityResourceSelection.ExactResource, "country", "MT", true)]
    [DataRow(CapabilityResourceSelection.ExactResource, "country", "JP", false)]
    [DataRow(CapabilityResourceSelection.AllOfType, "country", null, true)]
    [DataRow(CapabilityResourceSelection.AllOfType, "other", null, false)]
    public async Task ResourceSelectionsKeepTheAuthoritativeTarget(CapabilityResourceSelection selection,
        string? resourceType, string? resourceIdentifier, bool expected)
    {
        SecurityOptions options = SecurityTestSupport.Options();
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development",
            subject: SecurityTestSupport.Subject, resourceSelection: selection,
            resourceType: resourceType, resourceIdentifier: resourceIdentifier));
        await using ServiceProvider root = SecurityTestSupport.Services(options).BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());

        AuthorizationDecision decision = await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("rules.read", new CapabilityTarget("target-a", "Development", "country", "MT"));

        Assert.AreEqual(expected, decision.IsAllowed);
    }

    [TestMethod]
    public async Task MultipleCapabilitiesAreOneCompositeDecisionAndNeverAPartiallyAllowedSet()
    {
        SecurityOptions options = SecurityTestSupport.Options();
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.publish", "target-b", "Production", subject: SecurityTestSupport.Subject));
        var audit = new SecurityTestSupport.RecordingAudit();
        await using ServiceProvider root = SecurityTestSupport.Services(options, audit).BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());

        AuthorizationDecision result = await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync(["rules.read", "rules.publish"], SecurityTestSupport.Target);

        Assert.IsFalse(result.IsAllowed);
        Assert.AreEqual("security.capability_denied", result.Code);
        Assert.HasCount(1, audit.Decisions);
        Assert.HasCount(2, audit.Decisions[0].Capabilities);
    }

    [TestMethod]
    public async Task AdditionalNativeRequirementsCanDenyAfterTheCompleteGrantPasses()
    {
        SecurityOptions options = SecurityTestSupport.Options(additionalPolicy: "native-extra");
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        var audit = new SecurityTestSupport.RecordingAudit();
        ServiceCollection services = SecurityTestSupport.Services(options, audit);
        services.AddAuthorization(native => native.AddPolicy("native-extra", policy => policy.AddRequirements(new RejectRequirement())));
        services.AddSingleton<IAuthorizationHandler>(new RejectHandler());
        await using ServiceProvider root = services.BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());

        AuthorizationDecision result = await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("rules.read", SecurityTestSupport.Target);

        Assert.IsFalse(result.IsAllowed);
        Assert.HasCount(1, audit.Decisions);
        Assert.IsFalse(audit.Decisions[0].Decision.IsAllowed);
    }

    [TestMethod]
    public async Task AnEarlierNativeSuperuserHandlerCannotSubstituteForACompleteCapabilityGrant()
    {
        SecurityOptions options = SecurityTestSupport.Options();
        var earlier = new SucceedPendingHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuthorizationHandler>(earlier);
        services.AddCisSecurity(options);
        services.AddCisSecurityProjection(SecurityTestSupport.Profile,
            _ => new SecurityTestSupport.FixtureProjector(null, null));
        await using ServiceProvider root = services.BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());

        AuthorizationDecision result = await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("rules.read", SecurityTestSupport.Target);

        Assert.IsTrue(earlier.SatisfiedCapabilityRequirement,
            "The earlier native handler must actually satisfy the CIS marker before its own handler runs.");
        Assert.IsFalse(result.IsAllowed);
        Assert.AreEqual(SecurityFailureKind.CapabilityDenied, result.Failure!.Kind);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EntraRoleGrantsRequireTheActualValidatedRole(bool hasRole)
    {
        SecurityOptions options = SecurityTestSupport.Options(SecurityProfileKind.EntraWorkloadIdentity, mappedRoles: ["reader"]);
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", mappedRole: "reader"));
        await using ServiceProvider root = SecurityTestSupport.Services(options,
            actualRoles: hasRole ? ["reader"] : []).BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());

        AuthorizationDecision result = await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("rules.read", SecurityTestSupport.Target);

        Assert.AreEqual(hasRole, result.IsAllowed,
            "This is common validated-role mapping evidence, not a JWT/federation validation claim.");
    }

    [TestMethod]
    public async Task KnownLeafExpiryIsCheckedForEveryOperationButEntraNativeSkewIsNotOverridden()
    {
        foreach (SecurityProfileKind kind in new[] { SecurityProfileKind.ClientCertificate, SecurityProfileKind.EntraWorkloadIdentity })
        {
            SecurityOptions options = SecurityTestSupport.Options(kind);
            options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development",
                subject: SecurityTestSupport.Subject,
                requiredProviderRole: kind == SecurityProfileKind.EntraWorkloadIdentity ? "reader" : null));
            await using ServiceProvider root = SecurityTestSupport.Services(options, actualRoles: ["reader"],
                expiry: SecurityTestSupport.Now.AddSeconds(5)).BuildServiceProvider();
            await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
                SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());
            ICapabilityAuthorizer authorizer = scope.Services.GetRequiredService<ICapabilityAuthorizer>();
            Assert.IsTrue((await authorizer.AuthorizeAsync("rules.read", SecurityTestSupport.Target)).IsAllowed);

            ((ManualTimeProvider)root.GetRequiredService<TimeProvider>()).Advance(TimeSpan.FromSeconds(6));
            AuthorizationDecision later = await authorizer.AuthorizeAsync("rules.read", SecurityTestSupport.Target);

            Assert.AreEqual(kind == SecurityProfileKind.EntraWorkloadIdentity, later.IsAllowed,
                "The native JWT handler owns its receiving tolerance; this common bridge does not invent another grace period.");
            if (kind == SecurityProfileKind.ClientCertificate)
            {
                Assert.AreEqual(SecurityFailureKind.CredentialExpired, later.Failure!.Kind);
            }
        }
    }

    [TestMethod]
    public async Task CancellationWaitsForActualNativeHandlerSettlement()
    {
        SecurityOptions options = SecurityTestSupport.Options(additionalPolicy: "held-native");
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        var held = new HeldHandler();
        ServiceCollection services = SecurityTestSupport.Services(options);
        services.AddAuthorization(native => native.AddPolicy("held-native", policy => policy.AddRequirements(new HeldRequirement())));
        services.AddSingleton<IAuthorizationHandler>(held);
        await using ServiceProvider root = services.BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());
        using var cancellation = new CancellationTokenSource();
        Task<AuthorizationDecision> work = scope.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync(["rules.read"], SecurityTestSupport.Target, cancellation.Token).AsTask();
        try
        {
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.IsFalse(work.IsCompleted, "Cancellation cannot certify settlement of the native evaluator.");
        }
        finally
        {
            held.Release.TrySetResult();
        }
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await work);
        Assert.IsTrue(held.Settled);
    }

    [TestMethod]
    public async Task AuditHookFaultIsObservableAndDoesNotBecomeAnOrdinaryDenialOrSuccessfulReceipt()
    {
        SecurityOptions options = SecurityTestSupport.Options();
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        var original = new InvalidOperationException("audit-owned-fault");
        var audit = new SecurityTestSupport.RecordingAudit { Failure = original };
        await using ServiceProvider root = SecurityTestSupport.Services(options, audit).BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());

        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await scope.Services.GetRequiredService<ICapabilityAuthorizer>().AuthorizeAsync("rules.read", SecurityTestSupport.Target));

        Assert.AreSame(original, observed);
        Assert.IsEmpty(audit.Decisions);
    }

    private sealed class RejectRequirement : IAuthorizationRequirement
    {
    }
    private sealed class RejectHandler : AuthorizationHandler<RejectRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RejectRequirement requirement)
        {
            context.Fail();
            return Task.CompletedTask;
        }
    }

    private sealed class SucceedPendingHandler : IAuthorizationHandler
    {
        internal bool SatisfiedCapabilityRequirement
        {
            get; private set;
        }

        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            foreach (IAuthorizationRequirement requirement in context.PendingRequirements.ToArray())
            {
                if (requirement is CapabilityRequirement)
                {
                    SatisfiedCapabilityRequirement = true;
                }
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class HeldRequirement : IAuthorizationRequirement
    {
    }
    private sealed class HeldHandler : AuthorizationHandler<HeldRequirement>
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Settled
        {
            get; private set;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, HeldRequirement requirement)
        {
            Entered.TrySetResult();
            await Release.Task;
            Settled = true;
            context.Succeed(requirement);
        }
    }
}
