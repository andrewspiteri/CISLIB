# Optional Scheduler and application catalog clients

The optional public ports describe Scheduler intent, original results, application
capabilities and operator action eligibility. Constructing or registering a client
starts no scheduler, timer, job worker, registration or provider. Native server
authorization and the existing Jobs ownership still govern every actual action.
Application capabilities are separate from the existing HTTP API discovery protocol.

In C#, prepare a dedicated native profile before its normal REST/security
registration, then register the optional ports:

```csharp
var scheduler = new ManagementSchedulerClientOptions("management-scheduler")
{
    BaseUrl = "https://management.example.local/"
};
var rest = new RestClientOptions(scheduler.RestClientName) { LogCalls = false };
rest.ConfigureCisManagementSchedulerRestClient(scheduler);
services.AddCisRestClient(rest); // Or the application's selected security-bound registration.
services.AddCisManagementSchedulerClient(scheduler);
```

The first default aliases and every keyed optional port borrow the same selected
native owner. No second provider or HttpClient is created. Three finite observation
aliases cover Scheduler, catalog and Configuration administration, alongside the
native request fallback. Existing tighter body limits, authentication, native
transport, trace and logging choices remain in force.

TypeScript exports ManagementSchedulerClient, ManagementApplicationCatalogClient
and ManagementConfigurationAdministrationClient from the portable package. Their
structural ports can be substituted independently. Credentials are supplied per
call through the existing client-core owner. Browser imports acquire no Node,
React, Next or database dependency.

Both implementations retain the entry-through-cleanup deadline. Writes, event
submission, attempt admission and reporting have no automatic transport replay.
An explicitly selected conservative policy can retry reads only. A timeout does
not establish rollback. Keep the original operation key and recover under current
authorization through the exact object's operation lookup. A recovered original
commit remains distinct from the current read's transaction and cleanup facts.

Create uses a caller-retained object UUID, Idempotency-Key and If-None-Match: *.
Replacement/state changes use a strong If-Match; cancellation retains its own
original run operation. Fixed families are management.scheduler.trigger.write,
management.scheduler.task.write and management.scheduler.run.write. The server
must fingerprint intent before effects; reusing a key for another intent conflicts.

History links retain the source application, trigger and event identities,
including cross-application task fanout. Integers that require lossless Int64
semantics are decimal strings. Business Messaging operation keys keep their
existing grammar and are never narrowed to administrative URL keys. Captured
input/settings access and visible history metadata are independent permissions.
Unknown response values remain inspectable, never implicit permission or success.

The fixed Scheduler codec admits complete bounded JSON, exact declared DTOs and
closed commands. Task preview requires the complete echoed draft and source
preconditions; the separate calendar route uses calendar-only preview facts. The
shared literal conformance cases exercise both languages. These client/codec
checks establish transport behavior, not PostgreSQL scheduling or actual callback
execution; those are proved by the backend and managed-worker implementation tasks.

