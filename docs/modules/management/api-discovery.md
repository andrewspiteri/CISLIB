# Complete API discovery

The original ServiceInfo `features` and application Capabilities `implementedApis`
and `callerHints` arrays are the closed **legacy-v1** operation profile. Their
64-item bounds and existing constructors remain compatible. Missing an operation
from that projection does not establish that a newer operation is unsupported.
These API descriptions are separate from the application's schedulable capability
catalog, trigger providers and reported participation.

A service advertising `apiDiscoveryVersion: 1` supports two complete-v1 reads:

| Read | Optional client method | Result |
| --- | --- | --- |
| `GET /management/v1/service-features` | `IManagementApiDiscoveryClient.ListServiceFeaturesAsync` | `ManagementServiceFeaturePage` |
| `GET /management/v1/applications/{applicationId}/api-capabilities` | `IManagementApiDiscoveryClient.ListApplicationApiCapabilitiesAsync` | `ManagementApplicationApiCapabilityPage` |

Both default to32 rows and accept at most64. An explicitly supplied ordinary
`ManagementPageRequest()` defaults to100 and is rejected here; omit the page or
choose a size through64. A future positive discovery version is retained as data,
without being interpreted as version-one support. New typed calls request the
fixed version-one route and require an exact `complete-v1` response. They do not
create a second transport or add a hidden service-info round trip.

Pages enumerate the actual initialized installed handlers in operation-ID order.
Unselected Scheduler or Configuration providers do not become supported merely
because their OpenAPI descriptions or portable clients exist. The service's
finite complete descriptor inventory is capped at128 and each page retains the
ordinary response byte limit. Invalid or duplicate descriptors refuse discovery
rather than silently truncating the inventory.

An opaque continuation belongs to its endpoint, current native actor, validated
claims, security profile/policy, exact application, page size and complete
descriptor digest, as well as the existing namespace/authority/recovery epoch.
Changing any binding requires a fresh first page. A client never follows a cursor
as a URL. Application pages must echo the requested canonical application ID.

`allowed` is an advisory observation, not an execution permit. A source, catalog,
binding, task or job may require a more specific resource than the application
page can resolve. Such permission remains unestablished here; the actual route
authorizes its complete resolved resource independently. Historical registration
or a reported label cannot grant permission.

`ManagementClient` and its existing keyed/default registrations expose the
optional discovery port while reusing the same native REST owner, deadlines,
read-retry selection, bounded codec and cleanup. Existing custom
`IManagementClient` implementations need no new members.
`ScriptedManagementClient` supports finite discovery steps for consumer logic;
those scripts prove no native authorization, server inventory or cursor signing.
