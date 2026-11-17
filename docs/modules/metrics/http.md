---
title: Metrics endpoint and native HTTP policy
type: document
status: draft
cis:
  stable_id: cislib.document.modules.metrics.http
---

# Metrics endpoint and native HTTP policy

The optional CIS.Metrics.OpenTelemetry package installs its HTTP branch only
when the application calls UseCisMetrics. AddCisMetrics registers the owned
provider and captures finite options; it does not expose an endpoint by itself.
The application owns management access and path-base configuration.

~~~csharp
using CIS.Healthcheck.AspNetCore;
using CIS.Metrics.OpenTelemetry;

builder.Services.AddCisMetrics();
// Other ordinary application registrations.
var app = builder.Build();
app.UsePathBase("/service"); // When the deployment actually uses this base.
app.UseRouting();
// Application/deployment management access policy goes before the branch.
app.UseCisMetrics();
app.UseAuthentication();
app.UseAuthorization();
app.UseCisHealthcheck(health, healthOptions);
// Business readiness and ordinary application middleware follow.
~~~

The branch serves exactly GET /_metrics relative to PathBase, with no /metrics
alias. Install it after routing and before authentication/authorization or
readiness middleware that would otherwise gate management traffic. Protect it
with the deployment's management network or an explicit earlier access policy.
This placement lets readiness remain closed while a scrape succeeds; it does
not grant public access or make a scrape certify application readiness.

Repeated installation on the same application is idempotent. A declared business
route for the canonical path fails during real pipeline construction before
the listener starts. Endpoint declarations and route diagnostics form a fixed
startup snapshot: adding dynamic endpoint producers afterward is unsupported.
Use ordinary fixed ASP.NET route templates, without raw path diagnostics or
custom replacement HTTP metrics features/collections.

## Bounded standard exporter branch

The branch uses the existing upstream exporter for format negotiation,
serialization, resource labels, collection coalescing, caching and the
wait/write deadline. CIS adds only admission and native request policy.

| Boundary | Default behavior |
| --- | --- |
| Method | GET; other methods return 405 with Allow: GET. |
| Concurrent scrape handlers | Two standard ConcurrencyLimiter permits, no waiting queue; capacity returns 429. |
| Stopping | New admission returns 503 after collection cutoff. |
| Cache | 300 ms upstream cache; options may explicitly select zero or another supported finite interval. |
| Wait/write budget | Five seconds upstream; expiration does not interrupt synchronous owner code. |
| Encoded response | Four MiB upstream cap; the pinned exporter requires a configured minimum of 85,000 bytes. |

Permits are released after the actual HTTP branch unwinds, including errors,
timeout and request cancellation. An expired HTTP wait can release its permit
while the observable callback still runs. Host shutdown closes admission and
awaits actual callback execution plus bounded observation-list materialization.
Keep owners alive until that drain completes; see [hosting](hosting.md).

The response cap bounds complete encoded output, not retained managed memory.
The separate incremental retained-memory target requires measurement. SDK
overflow, cache and scrape failures neither alter health/readiness nor reset
cumulative instruments.

## Native request observations

Incoming duration and active requests use the host's exact native
Microsoft.AspNetCore.Hosting meter. There is no second CIS request timer or
counter. The duration view retains only native method, declared route diagnostics
and final status. Active requests retain native method and scheme and remain
host-wide, including scrapes and health requests.

Completed durations exclude the exact canonical metrics path and every
StartsWithSegments /_health request, case-insensitively. A similarly named
business path such as /_healthcare remains included. These exclusions use one
first-pass management decision per real request. Exception-handler reexecution
from a business route into a health path therefore stays a business observation.
The framework produces one final duration. After the downstream handler finishes,
CIS restores the public IExceptionHandlerFeature.Endpoint as the current endpoint
for native completion. The native recorder then reads its original declared
route, including its own empty-root-to-slash behavior. An unknown original
endpoint is restored as null, so no route label is emitted. No duplicate selected
route tag or raw path is added. Outer completion middleware also observes the
restored original endpoint; the completed handler's response and final status
are preserved.

Use bounded declared route diagnostics of at most 256 UTF-8 bytes, with valid
Unicode and no controls. Validation occurs once while the application pipeline
is built. Request IDs, query strings, raw paths and unmatched paths never become
route labels. Unknown methods retain the framework's normalization; final
statuses include authorization failures and handled error responses.

The pinned framework appends custom HTTP metrics tags alongside native fields.
Duplicates in the selected keys can cause SDK point rejection. CIS removes
custom duplicates of the four selected native keys from the original
framework-owned List before native request completion: http.route,
http.request.method, http.response.status_code and url.scheme. The framework's
route/method/status/scheme remain authoritative. Unrelated custom tags and
application exceptions are preserved. Ordinary optional-feature cleanup failures
are isolated with the runtime's bounded diagnostic policy.

Canceled requests keep the framework's actual final status and completion
semantics; a recorded duration does not prove a response reached the client.
A handled exception need not expose error.type, and this duration view does not
retain it. Use final status for HTTP error analysis.

With CollectIncomingHttp=false, no native streams, duration exclusions, native
tag cleanup or route-diagnostic length validation are installed. The reserved
exporter branch, method/admission limits and canonical-route ownership remain.
Disabled Metrics installs neither that branch nor a provider.

See the [collection policy](collection.md), [hosting lifetime](hosting.md) and
[change verification](../../changes/CIS-0009/verification.md). The recorded
65-case Windows host checkpoint includes real Kestrel branch/native HTTP cases;
source/package consumption and the single footprint have separate observations.
Read the record for post-correction currentness and remaining compatibility,
mutation, coverage and platform scope. These local passes do not establish
release qualification.
