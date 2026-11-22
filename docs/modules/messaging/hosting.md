# Messaging hosting and actual drain

Register explicit routes, subscriptions, the store mapping, the application-owned
database source and the optional transport. Then call `services.AddCisMessaging()`.
It captures the completed immutable definition graph, registers the outbox/client
and scoped `IMessageAdministration`, and adds one cooperating host bridge. It
performs no database, broker, topology, handler or worker I/O during registration.
Changes to definitions after this point require a new complete composition.
Selected request definitions also compose the typed coordinator and reserve its
reply lane within the shared work/byte totals; see [typed requests](requests.md).

The selected inbound Security profile also requires its named authentication
scheme to be registered. For a broker-only profile whose scheme is `cis-rabbitmq`,
the application can register a handler that supplies no HTTP identity:

```csharp
services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, BrokerOnlyScheme>("cis-rabbitmq", _ => { });

sealed class BrokerOnlyScheme(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());
}
```

This uses `Microsoft.AspNetCore.Authentication`, `Microsoft.Extensions.Options`,
`Microsoft.Extensions.Logging` and `System.Text.Encodings.Web`. Register the same
scheme name selected by the native Security profile before startup. HTTP requests
do not authenticate as broker callers through this handler. The RabbitMQ
adapter's private projector authenticates only its current native delivery
tickets, using the validated broker user and the captured subject mapping.

In `CISApplication`, call `builder.UseMessaging()` in the ordinary setup callback
after supplying the module definitions and native provider registrations. The
facade installs the same hosting bridge after its preparation and arranges the
existing cooperating host services. It creates no additional service provider.
`UseMessaging(options)` also captures the existing `MessagingOptions`; neither
overload selects a database provider or broker credentials.

Standalone Generic Host composition requires sequential hosted start and stop.
Register ordinary dependency hosted services and business `IStartable` instances
first, then Messaging, followed by the existing Workflow/REST/Heartbeat/Startup
bridges. When `AddCisStartup()` is selected, Messaging is the final enrolled
`IStartable`, after application initialization. Without that bridge the Messaging
hosted service starts the same module owner directly. Incorrect bridge or startup
order fails before module I/O.

Startup awaits these steps in order:

1. Validate the complete immutable graph and exact registration ownership.
2. Perform one bounded store observation through a short original ReadCommitted
   owner and await its actual rollback/disposal. PostgreSQL checks the installed
   schema version; startup never installs or repairs a schema.
3. Await native transport startup, including TLS/trust, configured topology
   compatibility and confirmed consumer registration.
4. Start one existing bounded outbox pump/poll loop, with its coalesced commit wake
   hint, and publish readiness.

The runtime is independent of CISApplication, Heartbeat and collector SDKs. Its
readiness condition always requires current store availability. With subscriptions
or typed requests, `RequireLiveBrokerForReadiness` defaults to `true` and also
requires current transport availability. Explicitly selecting `false` permits
that module readiness condition to remain clear during a later broker outage;
it does not claim that remote work can complete during the outage. A staging-only
deployment can remain ready during a later broker outage for either option value
while its store remains available. Initial startup still requires the explicitly
selected transport to establish its configured resources. Store failures,
unexpected background faults and shutdown still make Messaging unready for either
option value. Native automatic recovery remains the sole reconnect/topology owner.
There is no second consumer registration loop.

Each pump refreshes a bounded store observation outside scrape callbacks. A store
failure invalidates the retained observation and readiness. Normal polling emits
no success log. Repeated failures share an eight-message lifetime diagnostic
budget and a monotonic 30-second spacing; diagnostics contain a fixed safe code.
An unexpected background fault remains visible and is preserved at actual stop.

Shutdown first closes caller and dispatcher admission, cancels local request
waits and admitted dispatch cooperatively, and stops native consumer admission.
It joins actual request/ticket cleanup, dispatch,
transaction, disposition and independent scope settlement. Only then does it stop
the publisher loop and join native publication, broker cleanup and shared resource
leases. A canceled caller's stop wait is not evidence that this work settled.
Incomplete source work remains unacknowledged or is explicitly requeued; shutdown
never invents a successful processing or remote rollback result.

The facade and existing Startup/Heartbeat/REST pre-stop paths await this barrier
before their actual Metrics, Heartbeat or dependency teardown. Host-owned data
sources remain available through admitted work. Scoped administration holds the
same shared work/byte leases through authorization, transaction completion and
audit; final shared-budget disposal joins those accepted calls too. Applications
retain ownership of borrowed data sources and explicitly supplied instances.

New administrative calls obey the same immediate module cutoff. Accepted calls
still finish their owned settlement. A settled Messaging stop fault cannot skip
the remaining Startup/Heartbeat/REST or Metrics drains. Concurrent disposal joins
one actual monitor/cancellation-resource cleanup barrier and preserves both stop
and cleanup faults.

Maintenance and administrative redrive remain explicit bounded APIs. Registration
does not select a maintenance schedule or delete accepted work to release storage.
See [PostgreSQL](postgresql.md), [RabbitMQ](rabbitmq.md),
[publication ownership](publication.md) and [observations](observations.md).

The hosting and exporter test source is an implementation deliverable. Actual
platform, provider, package and completion facts are recorded separately in
[CIS-0019 verification](../../changes/CIS-0019/verification.md).
