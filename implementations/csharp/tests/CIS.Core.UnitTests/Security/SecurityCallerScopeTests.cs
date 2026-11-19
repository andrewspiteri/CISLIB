using System.Security.Claims;
using System.Text.Encodings.Web;
using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Security;

[TestClass]
public sealed class SecurityCallerScopeTests
{
    [TestMethod]
    public async Task IssuerQualificationAndSerializedCallerDataCannotEstablishAuthority()
    {
        SecurityOptions options = GrantedOptions();
        await using ServiceProvider root = SecurityTestSupport.Services(options).BuildServiceProvider();
        await using SecurityHostContextScope foreign = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication(new("issuer-b", SecurityTestSupport.Subject.Identifier)));

        Assert.IsNull(foreign.Caller);
        Assert.AreEqual(SecurityFailureKind.IdentityUnmapped, foreign.Failure!.Kind);
        Assert.IsFalse((await foreign.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("rules.read", SecurityTestSupport.Target)).IsAllowed);

        await using AsyncServiceScope plain = root.CreateAsyncScope();
        var data = new CallerContext(SecurityTestSupport.Profile, "serialized", SecurityTestSupport.Subject,
            SecurityActorKind.Application, "caller-a", "Development", new(SecurityTestSupport.Now), "fixture-policy");
        Assert.AreEqual(SecurityTestSupport.Subject, data.Subject);
        Assert.IsNull(plain.ServiceProvider.GetRequiredService<ICallerContextAccessor>().Current);
        Assert.IsFalse((await plain.ServiceProvider.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("rules.read", SecurityTestSupport.Target)).IsAllowed,
            "Caller data is not accepted as an authorization credential.");
    }

    [TestMethod]
    public async Task ASeparateBackgroundScopeDoesNotInheritCallerAuthority()
    {
        SecurityOptions options = GrantedOptions();
        await using ServiceProvider root = SecurityTestSupport.Services(options).BuildServiceProvider();
        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());
        Assert.IsNotNull(caller.Caller);

        CallerContext? background = await Task.Run(async () =>
        {
            await using AsyncServiceScope independent = root.CreateAsyncScope();
            return independent.ServiceProvider.GetRequiredService<ICallerContextAccessor>().Current;
        });

        Assert.IsNull(background);
        Assert.AreSame(caller.Caller, caller.Services.GetRequiredService<ICallerContextAccessor>().Current);
    }

    [TestMethod]
    public async Task AValidatedResultFromAnotherNativeSchemeCannotBeAttributedToTheSelectedProfile()
    {
        await using ServiceProvider root = SecurityTestSupport.Services(GrantedOptions()).BuildServiceProvider();
        AuthenticateResult other = AuthenticateResult.Success(new AuthenticationTicket(
            SecurityTestSupport.NativeAuthentication().Principal!, "another-scheme"));

        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, other);

        Assert.IsNull(scope.Caller);
        Assert.AreEqual(SecurityFailureKind.Unauthenticated, scope.Failure!.Kind);
        Assert.IsFalse((await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("rules.read", SecurityTestSupport.Target)).IsAllowed);
    }

    [TestMethod]
    public async Task AnExtraUnauthenticatedIdentityCannotSupplyRolesToAnAdditionalNativePolicy()
    {
        SecurityOptions options = SecurityTestSupport.Options(additionalPolicy: "native-admin");
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        ServiceCollection services = SecurityTestSupport.Services(options);
        services.AddAuthorization(native => native.AddPolicy("native-admin", policy => policy.RequireRole("administrator")));
        await using ServiceProvider root = services.BuildServiceProvider();
        AuthenticateResult original = SecurityTestSupport.NativeAuthentication();
        var extra = new ClaimsIdentity([new Claim(ClaimsIdentity.DefaultRoleClaimType, "administrator")]);
        Assert.IsFalse(extra.IsAuthenticated);
        original.Principal!.AddIdentity(extra);
        Assert.IsTrue(original.Principal.IsInRole("administrator"),
            "The fixture must demonstrate native role lookup seeing an unauthenticated additional identity.");

        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, original);

        Assert.IsNull(scope.Caller);
        Assert.AreEqual(SecurityFailureKind.Unauthenticated, scope.Failure!.Kind);
        Assert.IsFalse((await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("rules.read", SecurityTestSupport.Target)).IsAllowed);
    }

    [TestMethod]
    public async Task ConcurrentScopesAndNestedFailuresRestoreTheirOwnPreviousCaller()
    {
        SecurityOptions options = GrantedOptions();
        await using ServiceProvider root = SecurityTestSupport.Services(options).BuildServiceProvider();
        await using SecurityHostContextScope first = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());
        await using SecurityHostContextScope second = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, AuthenticateResult.NoResult());
        ICallerContextAccessor firstAccessor = first.Services.GetRequiredService<ICallerContextAccessor>();
        ICallerContextAccessor secondAccessor = second.Services.GetRequiredService<ICallerContextAccessor>();
        Assert.IsNotNull(firstAccessor.Current);
        Assert.IsNull(secondAccessor.Current);
        SecurityCallerState firstState = first.Services.GetRequiredService<SecurityCallerState>();
        CallerContext original = firstAccessor.Current;
        var fault = new InvalidOperationException("nested-owner-fault");

        InvalidOperationException observed = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using IDisposable nested = firstState.Establish(new(null, null, new ClaimsPrincipal(), failure: new(
                SecurityFailureKind.Unauthenticated, "No nested caller.")));
            Assert.IsNull(firstAccessor.Current);
            throw fault;
        });

        Assert.AreSame(fault, observed);
        Assert.AreSame(original, firstAccessor.Current);
        await first.DisposeAsync();
        Assert.IsNull(firstAccessor.Current);
        Assert.IsNull(secondAccessor.Current);
    }

    [TestMethod]
    public async Task ProjectionRestoresScopeAfterFailureAndHeadersCannotReplaceItsTrustedIdentity()
    {
        var options = GrantedOptions();
        ServiceCollection services = SecurityTestSupport.Services(options);
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureHandler>(SecurityTestSupport.Profile, _ => { });
        services.AddCisSecurityWeb(web => web.SelectProfile = _ => SecurityTestSupport.Profile);
        await using ServiceProvider root = services.BuildServiceProvider();
        await using AsyncServiceScope requestScope = root.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        context.Request.Headers["X-Test-Subject"] = SecurityTestSupport.Subject.Identifier;
        context.Request.Headers["X-Application"] = "administrator";
        context.Request.Headers["X-Environment"] = "Production";
        context.Request.Headers["X-User"] = "operator";
        context.Request.Headers["X-Reported-Instance"] = "reported-replica";
        var originalPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
        context.User = originalPrincipal;
        ClaimsPrincipal? nativePrincipal = null;
        var fault = new InvalidOperationException("protected-owner-fault");
        var application = new ApplicationBuilder(root);
        application.UseCisSecurityAuthentication();
        application.Run(http =>
        {
            CallerContext current = http.RequestServices.GetRequiredService<ICallerContextAccessor>().Current!;
            Assert.AreEqual("caller-a", current.Application);
            Assert.AreEqual("Development", current.Environment);
            Assert.AreEqual(SecurityActorKind.Application, current.ActorKind);
            Assert.IsNull(current.HumanSubject);
            Assert.AreEqual(InstanceEvidenceKind.Reported, current.Instance.Kind);
            Assert.IsNull(current.Instance.VerifiedIdentifier);
            nativePrincipal = http.User;
            http.User = new ClaimsPrincipal(new ClaimsIdentity());
            throw fault;
        });

        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await application.Build()(context));

        Assert.AreSame(fault, observed);
        Assert.IsNull(requestScope.ServiceProvider.GetRequiredService<ICallerContextAccessor>().Current);
        Assert.IsNotNull(nativePrincipal);
        Assert.AreSame(nativePrincipal, context.User,
            "Projection restores the principal established by the preceding native authentication middleware.");
        Assert.AreNotSame(originalPrincipal, context.User);
    }

    [TestMethod]
    public async Task ARegisteredSingleProfileDoesNotSelectAnUnboundOperationalSurface()
    {
        ServiceCollection services = SecurityTestSupport.Services(GrantedOptions());
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureHandler>(SecurityTestSupport.Profile, _ => { });
        await using ServiceProvider root = services.BuildServiceProvider();
        await using AsyncServiceScope requestScope = root.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        context.Request.Headers["X-Test-Subject"] = SecurityTestSupport.Subject.Identifier;
        var application = new ApplicationBuilder(root);
        application.UseCisSecurityAuthentication();
        bool reached = false;
        application.Run(http =>
        {
            reached = true;
            Assert.IsNull(http.RequestServices.GetRequiredService<ICallerContextAccessor>().Current);
            return Task.CompletedTask;
        });

        await application.Build()(context);

        Assert.IsTrue(reached);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeSchemeOptionsGetTheDefaultClockOrPreserveTheExplicitHostClock(bool supplyClock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        TimeProvider expectedClock = supplyClock
            ? new CIS.CommonTesting.ManualTimeProvider(SecurityTestSupport.Now)
            : TimeProvider.System;
        if (supplyClock)
        {
            services.AddSingleton(expectedClock);
        }
        services.AddCisSecurity(GrantedOptions());
        services.AddCisSecurityProjection(SecurityTestSupport.Profile,
            _ => new SecurityTestSupport.FixtureProjector(null, null));
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureHandler>(SecurityTestSupport.Profile, _ => { });
        using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        Assert.AreSame(expectedClock, root.GetRequiredService<TimeProvider>());
        Assert.AreSame(expectedClock, root.GetRequiredService<IOptionsMonitor<AuthenticationSchemeOptions>>()
            .Get(SecurityTestSupport.Profile).TimeProvider);
    }

    [TestMethod]
    public async Task ConcurrentNativeRequestsKeepTheirOwnMappedOrUnmappedScopeAcrossAwait()
    {
        var options = new SecurityOptions { PolicyVersion = "concurrent-fixture" };
        options.Profiles.Add(new(SecurityTestSupport.Profile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            authenticationScheme: SecurityTestSupport.Profile, identityMappings:
            [
                new(SecurityTestSupport.Subject, "caller-a", "Development"),
                new(new("issuer-a", "application-b"), "caller-b", "Development")
            ]));
        ServiceCollection services = SecurityTestSupport.Services(options);
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureHandler>(SecurityTestSupport.Profile, _ => { });
        services.AddCisSecurityWeb(web => web.SelectProfile = _ => SecurityTestSupport.Profile);
        await using ServiceProvider root = services.BuildServiceProvider();
        await using AsyncServiceScope first = root.CreateAsyncScope();
        await using AsyncServiceScope second = root.CreateAsyncScope();
        await using AsyncServiceScope unmapped = root.CreateAsyncScope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        var application = new ApplicationBuilder(root);
        application.UseCisSecurityAuthentication();
        application.Run(async context =>
        {
            ICallerContextAccessor accessor = context.RequestServices.GetRequiredService<ICallerContextAccessor>();
            CallerContext? before = accessor.Current;
            string subject = context.Request.Headers["X-Test-Subject"].ToString();
            Assert.AreEqual(subject == "unmapped" ? null : subject, before?.Subject.Identifier);
            if (Interlocked.Increment(ref count) == 3)
            {
                entered.TrySetResult();
            }
            await release.Task;
            Assert.AreSame(before, accessor.Current);
        });
        RequestDelegate pipeline = application.Build();
        IServiceProvider[] scopes = [first.ServiceProvider, second.ServiceProvider, unmapped.ServiceProvider];
        string[] subjects = [SecurityTestSupport.Subject.Identifier, "application-b", "unmapped"];
        Task[] work = scopes.Select((scope, index) =>
        {
            var context = new DefaultHttpContext { RequestServices = scope };
            context.Request.Headers["X-Test-Subject"] = subjects[index];
            return pipeline(context);
        }).ToArray();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(work.All(task => !task.IsCompleted));
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(work);
        }
        foreach (IServiceProvider scope in scopes)
        {
            Assert.IsNull(scope.GetRequiredService<ICallerContextAccessor>().Current);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AComposedNativePolicyCannotAddAnotherAuthenticationScheme(bool otherAuthenticates)
    {
        var audit = new SecurityTestSupport.RecordingAudit();
        ServiceCollection services = SecurityTestSupport.Services(GrantedOptions(), audit);
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureHandler>(SecurityTestSupport.Profile, _ => { });
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureHandler>("other", _ => { });
        await using ServiceProvider root = services.BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());
        var context = new DefaultHttpContext { RequestServices = scope.Services };
        context.Request.Headers["X-Test-Subject"] = SecurityTestSupport.Subject.Identifier;
        if (otherAuthenticates)
        {
            context.Request.Headers["X-Other-Authenticates"] = "yes";
        }
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new SecurityCapabilityMetadata(SecurityTestSupport.Profile,
                ["rules.read"], (_, _) => ValueTask.FromResult(SecurityTestSupport.Target))), "fixture"));
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder(SecurityTestSupport.Profile, "other")
            .RequireAuthenticatedUser().AddRequirements(CapabilityRequirement.Instance).Build();
        IPolicyEvaluator native = scope.Services.GetRequiredService<IPolicyEvaluator>();
        AuthenticateResult authentication = await native.AuthenticateAsync(policy, context);
        PolicyAuthorizationResult result = await native.AuthorizeAsync(policy, authentication, context, context);
        if (!otherAuthenticates)
        {
            Assert.IsTrue(result.Succeeded,
                "The strict scheme guard must reject even when native composition would otherwise allow the selected caller.");
        }
        int bodyCalls = 0;

        await scope.Services.GetRequiredService<IAuthorizationMiddlewareResultHandler>().HandleAsync(_ =>
        {
            bodyCalls++;
            return Task.CompletedTask;
        }, context, policy, result);

        Assert.AreEqual(0, bodyCalls);
        Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.HasCount(1, audit.Decisions);
        Assert.IsFalse(audit.Decisions[0].Decision.IsAllowed);
    }

    [TestMethod]
    public async Task ARepeatedSatisfiedNativeRequirementResolvesTheTargetOnce()
    {
        var audit = new SecurityTestSupport.RecordingAudit();
        ServiceCollection services = SecurityTestSupport.Services(GrantedOptions(), audit);
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureHandler>(SecurityTestSupport.Profile, _ => { });
        await using ServiceProvider root = services.BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());
        var context = new DefaultHttpContext { RequestServices = scope.Services };
        context.Request.Headers["X-Test-Subject"] = SecurityTestSupport.Subject.Identifier;
        int resolutions = 0;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new SecurityCapabilityMetadata(SecurityTestSupport.Profile,
                ["rules.read"], (_, _) =>
                {
                    resolutions++;
                    return ValueTask.FromResult(SecurityTestSupport.Target);
                })), "fixture"));
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder(SecurityTestSupport.Profile)
            .RequireAuthenticatedUser().AddRequirements(CapabilityRequirement.Instance, CapabilityRequirement.Instance).Build();
        IPolicyEvaluator native = scope.Services.GetRequiredService<IPolicyEvaluator>();
        AuthenticateResult authentication = await native.AuthenticateAsync(policy, context);

        PolicyAuthorizationResult result = await native.AuthorizeAsync(policy, authentication, context, context);
        int bodyCalls = 0;
        await scope.Services.GetRequiredService<IAuthorizationMiddlewareResultHandler>().HandleAsync(_ =>
        {
            bodyCalls++;
            return Task.CompletedTask;
        }, context, policy, result);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, resolutions);
        Assert.AreEqual(1, bodyCalls);
        Assert.HasCount(1, audit.Decisions);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LateSameSchemePoliciesCannotOmitCoreOrConfiguredNativeChecks(bool retainCoreMarker)
    {
        SecurityOptions options = SecurityTestSupport.Options(additionalPolicy: "profile-extra");
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        var audit = new SecurityTestSupport.RecordingAudit();
        ServiceCollection services = SecurityTestSupport.Services(options, audit);
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureHandler>(SecurityTestSupport.Profile, _ => { });
        services.AddAuthorization(native => native.AddPolicy("profile-extra", policy => policy.RequireRole("required-extra")));
        services.PostConfigure<AuthorizationOptions>(native =>
        {
            var replacement = new AuthorizationPolicyBuilder(SecurityTestSupport.Profile).RequireAuthenticatedUser();
            if (retainCoreMarker)
            {
                replacement.AddRequirements(CapabilityRequirement.Instance);
            }
            native.AddPolicy(SecurityPolicySnapshot.PolicyName(SecurityTestSupport.Profile), replacement.Build());
        });
        await using ServiceProvider root = services.BuildServiceProvider();
        await using SecurityHostContextScope caller = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());

        AuthorizationDecision imperative = await caller.Services.GetRequiredService<ICapabilityAuthorizer>()
            .AuthorizeAsync("rules.read", SecurityTestSupport.Target);

        Assert.IsFalse(imperative.IsAllowed);
        Assert.AreEqual(SecurityFailureKind.ValidationUnavailable, imperative.Failure!.Kind);

        await using AsyncServiceScope request = root.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = request.ServiceProvider };
        context.Request.Headers["X-Test-Subject"] = SecurityTestSupport.Subject.Identifier;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(
            new SecurityCapabilityMetadata(SecurityTestSupport.Profile, ["rules.read"],
                (_, _) => ValueTask.FromResult(SecurityTestSupport.Target)),
            new AuthorizeAttribute { Policy = SecurityPolicySnapshot.PolicyName(SecurityTestSupport.Profile) }), "late-policy"));
        var application = new ApplicationBuilder(root);
        application.UseCisSecurityAuthentication();
        SecurityAuthorizationOperation? earlierOperation = null;
        application.Use(async (http, next) =>
        {
            AuthorizationPolicy preliminary = new AuthorizationPolicyBuilder(SecurityTestSupport.Profile)
                .RequireAuthenticatedUser().AddRequirements(CapabilityRequirement.Instance).Build();
            AuthorizationResult prior = await http.RequestServices.GetRequiredService<IAuthorizationService>()
                .AuthorizeAsync(http.User, http, preliminary);
            Assert.IsTrue(prior.Succeeded);
            earlierOperation = http.Features.Get<SecurityAuthorizationOperation>();
            Assert.IsTrue(earlierOperation?.CompleteGrantAllowed == true);
            await next(http);
        });
        application.UseCisSecurityAuthorization();
        int bodyCalls = 0;
        application.Run(_ =>
        {
            bodyCalls++;
            return Task.CompletedTask;
        });

        await application.Build()(context);

        Assert.AreEqual(0, bodyCalls);
        Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.AreNotSame(earlierOperation, context.Features.Get<SecurityAuthorizationOperation>(),
            "The current native evaluation cannot inherit an earlier grant-check feature.");
        Assert.HasCount(2, audit.Decisions);
        Assert.IsTrue(audit.Decisions.All(decision => !decision.Decision.IsAllowed));
    }

    [TestMethod]
    public async Task CallerAddedNativeRequirementsRemainValidWhenTheyRetainTheProfilePolicy()
    {
        var audit = new SecurityTestSupport.RecordingAudit();
        ServiceCollection services = SecurityTestSupport.Services(GrantedOptions(), audit);
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureHandler>(SecurityTestSupport.Profile, _ => { });
        services.AddAuthorization(native => native.AddPolicy("caller-extra", policy => policy.RequireAssertion(_ => true)));
        await using ServiceProvider root = services.BuildServiceProvider();
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            SecurityTestSupport.Profile, SecurityTestSupport.NativeAuthentication());
        var context = new DefaultHttpContext { RequestServices = scope.Services };
        context.Request.Headers["X-Test-Subject"] = SecurityTestSupport.Subject.Identifier;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(
            new SecurityCapabilityMetadata(SecurityTestSupport.Profile, ["rules.read"],
                (_, _) => ValueTask.FromResult(SecurityTestSupport.Target))), "additional-policy"));
        IAuthorizationPolicyProvider policies = scope.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        AuthorizationPolicy selected = (await policies.GetPolicyAsync(SecurityPolicySnapshot.PolicyName(SecurityTestSupport.Profile)))!;
        AuthorizationPolicy extra = (await policies.GetPolicyAsync("caller-extra"))!;
        AuthorizationPolicy combined = new AuthorizationPolicyBuilder().Combine(selected).Combine(extra).Build();
        IPolicyEvaluator native = scope.Services.GetRequiredService<IPolicyEvaluator>();
        AuthenticateResult authentication = await native.AuthenticateAsync(combined, context);
        PolicyAuthorizationResult result = await native.AuthorizeAsync(combined, authentication, context, context);
        int bodyCalls = 0;

        await scope.Services.GetRequiredService<IAuthorizationMiddlewareResultHandler>().HandleAsync(_ =>
        {
            bodyCalls++;
            return Task.CompletedTask;
        }, context, combined, result);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, bodyCalls);
        Assert.HasCount(1, audit.Decisions);
        Assert.IsTrue(audit.Decisions[0].Decision.IsAllowed);
    }

    [TestMethod]
    public async Task AResultHandlerReplacementAfterRegistrationFailsClosed()
    {
        ServiceCollection services = SecurityTestSupport.Services(GrantedOptions());
        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(new AuthorizationMiddlewareResultHandler());
        await using ServiceProvider root = services.BuildServiceProvider();
        var application = new ApplicationBuilder(root);
        application.UseCisSecurityAuthentication();

        Assert.ThrowsExactly<InvalidOperationException>(() => application.UseCisSecurityAuthorization());
        CIS.Startup.IStartable validator = root.GetServices<CIS.Startup.IStartable>().First();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => validator.StartAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task AnonymousMetadataCannotBypassAnExplicitCapabilityEndpoint()
    {
        await using ServiceProvider root = SecurityTestSupport.Services(GrantedOptions()).BuildServiceProvider();
        await using AsyncServiceScope requestScope = root.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new SecurityCapabilityMetadata(SecurityTestSupport.Profile,
                ["rules.read"], (_, _) => ValueTask.FromResult(SecurityTestSupport.Target)), new AllowAnonymousAttribute()), "invalid"));
        var application = new ApplicationBuilder(root);
        application.UseCisSecurityAuthentication();
        int bodyCalls = 0;
        application.Run(_ =>
        {
            bodyCalls++;
            return Task.CompletedTask;
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => application.Build()(context));

        Assert.AreEqual(0, bodyCalls);
        Assert.IsNull(requestScope.ServiceProvider.GetRequiredService<ICallerContextAccessor>().Current);
    }

    [TestMethod]
    public async Task ThePreservedNativeHandlerRetainsOwnershipButCannotExecuteADeniedBody()
    {
        var native = new DeliberatelyPermissiveNativeHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(native);
        services.AddCisSecurity(GrantedOptions());
        services.AddCisSecurityProjection(SecurityTestSupport.Profile, _ => new SecurityTestSupport.FixtureProjector(null, null));
        ServiceProvider root = services.BuildServiceProvider();
        try
        {
            await using AsyncServiceScope requestScope = root.CreateAsyncScope();
            var context = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(new SecurityCapabilityMetadata(SecurityTestSupport.Profile,
                    ["rules.read"], (_, _) => ValueTask.FromResult(SecurityTestSupport.Target))), "fixture"));
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder(SecurityTestSupport.Profile)
                .RequireAuthenticatedUser().AddRequirements(CapabilityRequirement.Instance).Build();
            int bodyCalls = 0;

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => requestScope.ServiceProvider
                .GetRequiredService<IAuthorizationMiddlewareResultHandler>().HandleAsync(_ =>
                {
                    bodyCalls++;
                    return Task.CompletedTask;
                }, context, policy, PolicyAuthorizationResult.Forbid()));

            Assert.AreEqual(1, native.Calls);
            Assert.AreEqual(0, bodyCalls);
        }
        finally
        {
            await root.DisposeAsync();
        }
        Assert.AreEqual(0, native.Disposals, "An instance descriptor remains borrowed after preservation.");
        native.Dispose();
    }

    [TestMethod]
    public async Task EnumeratingTheObserverDoesNotActivateOrDisposeThePreservedFactoryTwice()
    {
        var ownership = new HandlerOwnership();
        var native = new OwnedNativeHandler(ownership);
        int factoryCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(_ =>
        {
            factoryCalls++;
            return native;
        });
        services.AddCisSecurity(GrantedOptions());
        ServiceProvider root = services.BuildServiceProvider();
        try
        {
            IAuthorizationMiddlewareResultHandler[] all = root.GetServices<IAuthorizationMiddlewareResultHandler>().ToArray();
            Assert.HasCount(1, all);
            var context = new DefaultHttpContext { RequestServices = root };
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

            await all[0].HandleAsync(_ => Task.CompletedTask, context, policy, PolicyAuthorizationResult.Success());

            Assert.AreEqual(1, factoryCalls);
            Assert.AreEqual(1, ownership.Calls);
            Assert.AreEqual(0, ownership.Disposals);
        }
        finally
        {
            await root.DisposeAsync();
        }
        Assert.AreEqual(1, ownership.Disposals,
            "The original factory's returned object remains one DI-owned resource after descriptor movement.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreservedNativeFactoryAndTypeDescriptorsKeepTheirScopedOwnership(bool factory)
    {
        var witness = new HandlerOwnership();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(witness);
        if (factory)
        {
            services.AddScoped<IAuthorizationMiddlewareResultHandler>(provider => new OwnedNativeHandler(provider.GetRequiredService<HandlerOwnership>()));
        }
        else
        {
            services.AddScoped<IAuthorizationMiddlewareResultHandler, OwnedNativeHandler>();
        }
        services.AddCisSecurity(GrantedOptions());
        await using ServiceProvider root = services.BuildServiceProvider();
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        for (int index = 0; index < 2; index++)
        {
            await using (AsyncServiceScope scope = root.CreateAsyncScope())
            {
                var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
                IAuthorizationMiddlewareResultHandler observer = scope.ServiceProvider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
                await observer.HandleAsync(_ => Task.CompletedTask, context, policy, PolicyAuthorizationResult.Success());
                await observer.HandleAsync(_ => Task.CompletedTask, context, policy, PolicyAuthorizationResult.Success());
                Assert.AreEqual(index + 1, witness.Constructions);
                Assert.AreEqual(index, witness.Disposals);
            }
            Assert.AreEqual(index + 1, witness.Disposals);
        }
        Assert.AreEqual(4, witness.Calls);
    }

    private static SecurityOptions GrantedOptions()
    {
        SecurityOptions options = SecurityTestSupport.Options();
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        return options;
    }

    private sealed class FixtureHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? subject = Request.Headers["X-Test-Subject"].FirstOrDefault();
            if (subject is null || Scheme.Name == "other" && !Request.Headers.ContainsKey("X-Other-Authenticates"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var identity = new ClaimsIdentity([new Claim("iss", "issuer-a"), new Claim("sub", subject)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }

    private sealed class DeliberatelyPermissiveNativeHandler : IAuthorizationMiddlewareResultHandler, IDisposable
    {
        internal int Calls
        {
            get; private set;
        }
        internal int Disposals
        {
            get; private set;
        }

        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            Calls++;
            return next(context);
        }

        public void Dispose()
        {
            Disposals++;
        }
    }

    private sealed class HandlerOwnership
    {
        internal int Constructions
        {
            get; set;
        }
        internal int Disposals
        {
            get; set;
        }
        internal int Calls
        {
            get; set;
        }
    }

    private sealed class OwnedNativeHandler : IAuthorizationMiddlewareResultHandler, IDisposable
    {
        private readonly HandlerOwnership witness;

        public OwnedNativeHandler(HandlerOwnership witness)
        {
            this.witness = witness;
            witness.Constructions++;
        }

        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            witness.Calls++;
            return next(context);
        }

        public void Dispose()
        {
            witness.Disposals++;
        }
    }
}
