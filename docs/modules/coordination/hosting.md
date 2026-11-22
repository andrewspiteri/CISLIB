# Coordination and jobs in the existing host

Use `CIS.Coordination.Hosting.AddCisCoordination` after the application's selected
provider, original-transaction guard, trusted target resolver and ordinary business
dependencies. It captures `CoordinationOptions` and optional
`CoordinationHostingOptions` without opening a connection, installing a schema or
starting a timer. The application still owns its database source and explicit
authority installation.

```csharp
services.AddSingleton<ICoordinationProvider>(selectedAuthority);
services.AddSingleton<ICoordinationGuard>(selectedGuard);
services.AddScoped<ICoordinationTargetResolver, ApplicationDutyResolver>();
services.AddCisCoordination(coordinationOptions);

// Optional jobs, after the explicit catalog and trusted scope factory:
services.AddCisJobs(initialSettings, new JobHostingOptions
{
    ContinuousTargets =
    [
        new("wallet-service", "wallet.compaction", "partition-12",
            JobExecutionMode.Continuous)
    ]
});
services.AddCisJobConfiguration<JobSettingsRoot>((root, revision) =>
    CaptureWholeValidatedSettings(root, revision));
```

The jobs methods are in `CIS.Jobs.Hosting`. `AddCisJobs` uses the one frozen passive
`JobCatalog`/`IJobCatalog`, an application-supplied `IJobExecutionScopeFactory`, and
optional selected `IJobRunSource`/`IJobScheduleEvaluator`. It selects Coordination
hosting with default options when not already selected. Jobs and the host remain
disabled by default, and no continuous resources are selected automatically.

`AddCisJobConfiguration<T>` connects one retained Configuration root through the
existing coordinated reader. Its capture delegate only copies a complete snapshot
under that root's value gate: no mutation, I/O, awaiting or nested configuration
reads. Initial revision zero cannot overwrite a newer observed event. See
[configuration](configuration.md) for adoption and rejection behavior.

## Sequential startup

Use native DI `ValidateOnBuild` and `ValidateScopes`, and keep hosted start/stop
nonconcurrent. For ordinary Generic Host registration, the cooperating tail is:

```text
ordinary dependencies → Messaging → Workflows → REST → Coordination
                      → Heartbeat → Startup → exact framework web host
```

Only installed modules appear. Keep Heartbeat immediately before Startup. The
single `CoordinationStartable` is the last initializer after Messaging and ordinary
business participants. The CISApplication facade arranges these exact cooperating
descriptors. Generic Host users arrange them explicitly. Without Startup, the
Coordination hosted bridge initializes its own module during native host startup;
ordinary application `IStartable` participants still require the normal Startup
integration.

The actual sequence is authority validation/start, one configuration subscription
connection, jobs activation validation/start, then explicit configured continuous
targets. It does not await a continuous duty's operational lifetime. Missing
required source/evaluator capabilities or authority validation fail activation
clearly. Standby, Idle and Waiting are normal states, and ownership itself is not
a default readiness requirement.

Automatic instrumentation attaches to the exact registered runtime before its
startup work. Health monitors are created during sequential startup, after actual
authority/job activation where applicable. `IsHealthCritical` in the module's
hosting options selects required operational health; a separate critical stopping
condition always makes readiness false immediately when the host begins draining.
Constructors do not create monitors or resolve runtime owners through a lifecycle
service.

## Explicit resources and configuration transitions

`JobHostingOptions.ContinuousTargets` is a copied bounded list of distinct exact
registered Continuous scope/job/resources. Its count must fit the configured
continuous-duty bound. Discovering a type or adding hundreds of catalog entries
does not activate it or retain every possible resource ID.

One bounded supervisor watches accepted configuration/admission notifications. It
latches an activation epoch before invoking Start, including when a handler faults
synchronously. Enablement or mode/binding transitions can request a new activation;
the runtime retains the last transition revision per registered definition when
accepting configuration. A rapid disable/re-enable therefore requests replacement
even if the supervisor observes only the final enabled settings. Unrelated settings
updates do not create a new activation epoch. This storage is bounded by the frozen
catalog, rather than the number of configuration revisions or resource IDs.
The supervisor binds each admission atomically to that captured transition revision;
a stale reconciliation cannot start work from a newer epoch and then replay it.
An admitted activation attempt consumes its epoch even when its initial scope
cleanup fails. Such failure is reported, fails initial startup, and is not retried
on ordinary notifications. After a transition, the old actual key/callback/scope
must drain first. Deferred capacity or an old
still-running lifetime can keep that explicit transition pending. Ordinary
capacity notifications never restart a Faulted, Indeterminate or Lost callback
from the same epoch. Unexpected business outcomes have no automatic replay policy.

An initial enabled target that cannot be authorized/activated fails required
startup. Later explicit activation refusal is retained as a bounded supervisor
failure rather than retried on every capacity notification. Scheduled source and
evaluator objects are fixed for the composition; replacement requires actual drain
and a new composition. Already admitted runs keep their immutable snapshots and
original operation identity.

## Actual drain before dependency cutoff

Raw application stopping first closes new coordination/job admission and flags the
critical stopping condition. Existing job callbacks retain their required REST,
Workflow, Messaging/outbox, Security, Configuration, Heartbeat, health-monitor and
telemetry dependencies during finite graceful authority drain. In particular,
health monitor admission remains open while readiness is already false, so an
admitted final unit can still use its normal health dependency.

The internal known-module barrier joins actual callbacks/scopes, configuration
notifications, provider requests, renewal/terminal notifications and instrumentation
refresh work. Only then does its deferred dependency token run the modules' usual
cutoffs. A separate successful physical barrier precedes deferred callbacks so
credential disposal cannot wait recursively for the cancellation callback currently
being dispatched. Cleanup faults are retained without skipping other module drains.

If drain expires, the managed lease stops renewal and signals cancellation. An
uncooperative callback remains owned and keeps host stop pending until it actually
settles. No timeout certifies graceful completion, releases a successor's grant or
permits disposing dependencies beneath continuing work. Startup caller cancellation
does not become a successfully started duty's lifetime token.

Passive composition metadata captures actual owners before exposure, including
borrowed singleton instances. A runtime factory admitted before stop is joined;
new factory creation after cutoff is refused before constructing an owner. The
facade retains that metadata before Build, so failed Build cleanup does not resolve
more services from a failed provider or miss an already active borrowed runtime.

Always await host/application stop before disposing the root provider. The normal
DI container owns factory-created services; supplied instance descriptors remain
caller-owned. Repeated stop/disposal joins the same actual runtime work. Without
Coordination hosting, existing module cutoff behavior and raw application stopping
tokens remain unchanged. This integration adds no general lifecycle framework or
alternative provider.

Focused unit evidence uses a native Generic Host and real Healthcheck with explicit
CommonTesting leases/scopes to test lifecycle boundaries. Those substitutions prove
no database authorization, renewal or SQL fencing. Real authority/guard and complete
RM-02/03/04 proof retain their separate integration tests and reported results.
