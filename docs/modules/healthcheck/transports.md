# Reporting and deployment

Both transports read one immutable snapshot, use the shared bounded formatter, and expose recorded facts. They do not invoke collectors, retry business work, mutate a monitor, or reclassify failures. A legitimate unhealthy report is distinct from a transport/formatting error.

Detailed monitors can include optional `registrationDiagnostic`, using the existing diagnostic object (`code` up to 64 Unicode scalars, `message` up to 1024, `exceptionType` up to 256 and required Boolean `isTruncated`). It represents a separate registration-level integration observation, not a health failure or an execution error. Compact views omit monitor details. Older v1 payloads without this field remain valid; a present field must be an object and cannot be null. The amendment adds no schema version or required field.

## ASP.NET Core

```csharp
using CIS.Healthcheck.AspNetCore;

// app is the existing application pipeline. Infrastructure required before this
// point can run, but put business-side-effect middleware after this branch.
app.UseCisHealthcheck(health, options, authorizeDetails: async context =>
{
    return await applicationAuthorization.CanReadHealthDetailsAsync(context);
});
// Install the application's readiness gate and business middleware next.
```

The authorization service above is application-owned pseudocode. The [CountryList source](../../../implementations/csharp/examples/healthcheck/CountryList/Program.cs) supplies a compilable predicate that deliberately denies details. Middleware runs in registration order; a terminal branch short-circuits downstream work. [ASP.NET Core middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0) explains the underlying pipeline behavior.

| Route | GET status |
| --- | --- |
| `/_health` | 200 for aggregate Healthy, 500 for Unhealthy. |
| `/_health/ready` | 200 for Ready, 500 for NotReady. |
| `/_health/details` | Enabled and authorized: complete monitor details with aggregate 200/500. Disabled: 404. Denied: 403. |

HEAD makes the same snapshot/status/complete-size decision as GET but sends no body. Enabled known routes reject other methods with 405 and `Allow: GET, HEAD`. Unknown reserved descendants and disabled details return 404 without entering business middleware. Paths are case-insensitive with at most one optional trailing slash. `/_healthcare` is ordinary application traffic. Query parameters cannot select details or change the view.

When `DetailsEnabled = true`, supply `authorizeDetails` explicitly. A restricted listener may deliberately authorize all, but the example does not. An authorization exception becomes a bounded `ReportingUnavailable` 500; request cancellation terminates that request. No error leaks exception text. Proxy/infrastructure middleware before the branch may still run; keep expensive or side-effecting business work after it. Starting the adapter installs middleware only. The host must start listening before awaiting mandatory priming if failure must remain observable.

Normal/error responses use JSON content type with no-store/no-cache headers. Formatting errors return bounded codes `ReportTooLarge`, `SerializationFailed` or `ReportingUnavailable` and a 500 decision, for example:

```json
{"kind":"error","schemaVersion":1,"error":{"code":"ReportTooLarge"}}
```

The formatter never sends partial successful JSON when the complete payload exceeds `MaxReportBytes`. HTTP branch errors use `Forbidden`, `NotFound` or `MethodNotAllowed`, with no body on HEAD. Clients should distinguish the error marker from a valid snapshot whose `healthStatus` is `Unhealthy`.

## Local pipe and matching probe

Keep the Unix endpoint's temporary directory short and writable in both
processes. In the Ubuntu qualification run, a long `TMPDIR` produced a
135-byte socket path and the runtime rejected it against its reported
108-byte limit; a short shared root allowed all 61 baseline cases to pass.
See the [retained diagnosis](../../../planning/healthcheck/evidence/HC12/capture-runtime-followup-2026-10-25/current194-closure.md).

The .NET pipe APIs are cross-platform. On Linux, this transport uses a Unix domain socket; the Windows native ACL code runs only in the Windows branch. Kubernetes can run the matching probe as an exec command inside the application container, or use the HTTP routes above. See [Microsoft's Linux implementation note](https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-use-named-pipes-for-network-interprocess-communication).

```csharp
HealthcheckOptions options = HealthcheckOptions.Resolve(new HealthcheckOptions
{
    PipeEnabled = true,
    PipeName = "country-list-health"
});
await using var server = new HealthPipeServer(health, options);
await server.StartAsync();
// Later, after producer handoff and owner drain:
await server.StopAsync();
```

The service and server should receive the same resolved options. Construction opens no endpoint. Disabled `StartAsync` opens none. Repeated start while running is idempotent; stopping is terminal. Stop closes admission and awaits actual active handlers. Caller cancellation cancels only the wait. Client capacity and one total per-connection timeout bound active work without making snapshot/state commits wait for clients.

Only local clients are supported. On Windows the server creates a protected owner ACL and sets `PIPE_REJECT_REMOTE_CLIENTS`; it does not rely on the BCL `CurrentUserOnly` server constructor to reject remote clients. Executed Windows evidence covers a local exchange and inspection of the owner ACL; remote-client, alternate-user and elevated-identity rejection lanes remain pending. On Unix the BCL `CurrentUserOnly` option checks peer UID, while socket mode follows the process umask; 0600 is not promised. Actual Ubuntu 24.04.4/.NET 10.0.12 packaged processes exercised owner/nobody/owner probes with exits 0/2/0 at modes 0755 and 0777. The denied attempt captured no health, and subsequent owner traffic succeeded. These ordinary-user results do not establish privileged-root denial or arbitrary container/filesystem behavior. See the [qualification receipt](../../../planning/healthcheck/evidence/HC12/qualification-next/receipt.json), [endpoint implementation](../../../implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthPipeEndpoint.cs), [pinned Unix runtime source](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Unix.cs) and [Microsoft's PipeOptions contract](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0). Run the server and exec probe in the intended same identity and endpoint filesystem context; a shared group or socket chmod does not replace that requirement. A process with access to the local endpoint can request details whenever `DetailsEnabled` is true; the HTTP authorization predicate does not apply to IPC.

The current matching tool is the nonpackable development executable `CisHealthProbe`; a `cis-health-probe` command has not been published. First change from the repository root into the C# implementation directory so `global.json` selects the pinned SDK:

```text
cd implementations/csharp
dotnet run --project tools/CisHealthProbe -- --pipe country-list-health --view readiness --timeout-ms 1000 --max-report-bytes 4194304
```

After a coordinated Release build, the built DLL can run from the repository root as `dotnet implementations/csharp/tools/CisHealthProbe/bin/Release/net10.0/CisHealthProbe.dll --pipe country-list-health --view readiness --timeout-ms 1000`. This DLL invocation does not build the project. Deployment fragments below use `cis-health-probe` as the intended installed command name; adapt the executable path to the actual matching artifact. Neither development invocation establishes exact-package or publication qualification.

Exactly these options are accepted: required `--pipe`; `--view health|readiness|details` (default health); `--timeout-ms` (default 1000, range 50–60000); `--max-report-bytes` (default 4194304, range 1024–16777216). Duplicate/unknown options, missing values and invalid names/numbers fail. There are no internal retries. Connect, request and complete response share one monotonic timeout budget; individual reads do not each receive a new budget.

| Exit | Meaning |
| --- | --- |
| 0 | Complete valid selected snapshot is Healthy/Ready. Details selects aggregate health. |
| 1 | Complete valid selected snapshot is Unhealthy/NotReady. |
| 2 | Invalid arguments, connection/timeout/framing/protocol/schema/reporting error, or incomplete/inconsistent response. |

Only a valid selected snapshot is written to stdout; errors use bounded stderr. A valid unhealthy snapshot is printed and exits 1. Do not treat receipt of any JSON as success.

The protocol is four-byte unsigned little-endian payload length followed by UTF-8 JSON, one request/response per connection. Request payloads are 1–512 bytes; responses are 1–`MaxReportBytes`, including their envelope. The framing length prefix is outside that payload. The matching client checks complete framing, UTF-8, depth 16, duplicate properties, versions, selected view, required fields, exact scalar types, counts and readiness consistency. Compact views cannot prove omitted per-monitor detail consistency; details verifies the complete list/count relationship. Unknown optional properties do not waive depth/duplicate validation. See [C06–C08](../../../planning/healthcheck/contract-catalog.md) for the exact schema.

## Native transport verification

The ordinary behavioral matrices invoke the shipping methods in the test
process. `HealthcheckHttpTransportTests` builds `UseCisHealthcheck` into an
`ApplicationBuilder`, invokes its `RequestDelegate` with `DefaultHttpContext`,
and observes the actual response stream. Its fixture adapts HTTP containers;
route selection, authorization, status, formatting and cancellation still run
inside Core. `HealthProbeClientTransportTests` invokes the existing internal
`HealthProbeClient.RunAsync` with caller-owned output/error streams. Its wire
tests use real OS pipes and belong to the explicit component suite.

| Previous expensive obligation | Maintained test and execution boundary |
| --- | --- |
| HTTP case/trailing-slash/query matching, reserved errors, method allowance, disabled details and business isolation | Existing `HealthcheckHttpTransportTests` matrices through the shipping RequestDelegate. The real Kestrel known-view control also checks actual listener/header behavior. |
| Healthy/unhealthy/readiness, startup/stopping, complete HEAD decisions, bounded reporting/authorization errors and one-snapshot coherence | Same HTTP assertions and inputs through the in-process pipeline; no host process per mode/data row. |
| Request cancellation, authorization/source cancellation, actual body write completion/failure and later-request isolation | Direct cancellation/body-stream tests plus `ClientCancellationOfAuthorizationDoesNotAffectLaterHealthOrBusiness` against a real Kestrel process. |
| HTTP/pipe detached snapshot equality | `ActualHttpAndPipePublishTheExactSameDetachedSnapshot` invokes the shipping HTTP pipeline and a real `HealthPipeServer` against the same frozen source. Pipe framing still crosses the OS endpoint. |
| Probe CLI argument rejection, numeric boundaries, malformed/fragmented DTOs, frame limits and one total timeout | `HealthProbeClientTransportTests`, preserving the complete independent wire DTO, exact stdout/stderr/exit assertions and OS I/O. `HealthProbeArgumentClassificationBoundaryTests` retains the cheap pre-canceled argument boundary cases. |
| Probe executable entrypoint, health/readiness/details exits 0/1/2, disabled details, default view, stdout/stderr and natural child shutdown | `HealthcheckProbeProcessTests` retains the nine selected-view process rows, one default-view process row and one invalid-argument entrypoint control. |
| Endpoint ACL/identity isolation, connection/server lifetime, shutdown and resource settlement | Existing `HealthcheckPipeTransportTests` and the retained process/lifetime controls. Windows and Linux execution remain separate qualification lanes. |

Each direct test keeps its 15-second failure watchdog; the probe total-budget
case deliberately spends about one second to distinguish a single deadline
from per-read resets. Run the transport component/regression selection with a
two-minute budget after build, and report any overrun rather than increasing
that budget implicitly. Extended forced-GC, identity/container and package
qualification remain explicitly selected work with their own recorded budgets.

Prospective grouped mutation must include the direct probe class in its selected
test project. Those cases prove mutation of shipping logic in their test host.
The retained real Kestrel/probe controls supply the bounded child boundary where
child activation is required: retain the existing source/session/selector and
terminal lifecycle audit for those actual launches. A direct test pass does not
establish mutation inside a child, and a child running unchanged copied Core
does not supply mutation credit. Historical report and proof interpretations
remain tied to their original candidate and test inputs.

## Kubernetes fragments

These are deployment examples. The [25 October Linux container check](../../../planning/healthcheck/evidence/HC12/linux-container-2026-10-25/README.md) exercised the real matching probe with a non-root host in one .NET 10.0.12 container; it did not execute Kubernetes or qualify a published application image. Publish the app and probe from the same verified candidate, include the ASP.NET Core runtime, and verify identity/permissions in the actual image before rollout. Configure supervisor periods, startup allowance and restart policy explicitly. Kubernetes supports HTTP and exec probes; it has no native named-pipe probe field. A startup probe suppresses liveness/readiness probes until startup succeeds. See [Kubernetes probe behavior](https://kubernetes.io/docs/concepts/workloads/pods/probes/).

HTTP container fragment (choose a listener reachable from the kubelet):

```yaml
ports:
  - name: http
    containerPort: 8080
env:
  - name: ASPNETCORE_URLS
    value: http://0.0.0.0:8080
  - name: CIS_HEALTHCHECK_DETAILS_ENABLED
    value: "false"
startupProbe:
  httpGet:
    path: /_health/ready
    port: http
  periodSeconds: 5
  failureThreshold: 30
  timeoutSeconds: 2
readinessProbe:
  httpGet:
    path: /_health/ready
    port: http
  periodSeconds: 5
  failureThreshold: 1
  timeoutSeconds: 2
livenessProbe:
  httpGet:
    path: /_health
    port: http
  periodSeconds: 10
  failureThreshold: 3
  timeoutSeconds: 2
```

Using aggregate health for liveness deliberately exposes every active critical failure to the deployment's restart policy. Noncritical refresh failures do not affect it. Healthcheck does not infer that an unhealthy dependency should be restarted.

Exec alternative, assuming the deployment installs the app and matching framework-dependent probe apphost as `/opt/app/cis-health-probe`:

```yaml
securityContext:
  runAsNonRoot: true
  runAsUser: 10001
  runAsGroup: 10001
env:
  - name: CIS_HEALTHCHECK_PIPE_ENABLED
    value: "true"
  - name: CIS_HEALTHCHECK_PIPE_NAME
    value: country-list-health
  - name: CIS_HEALTHCHECK_DETAILS_ENABLED
    value: "false"
readinessProbe:
  exec:
    command:
      - /opt/app/cis-health-probe
      - --pipe
      - country-list-health
      - --view
      - readiness
      - --timeout-ms
      - "1000"
  periodSeconds: 5
  timeoutSeconds: 2
  failureThreshold: 1
```

Add a startup exec probe with view readiness and an intentional startup allowance; select health for liveness only if that restart policy is desired. Kubernetes treats both exits 1 and 2 as probe failures; operational diagnostics can distinguish legitimate NotReady from endpoint/protocol failure. Allow process-start overhead in `timeoutSeconds` beyond the client's single 1000 ms budget. Use the actual installed executable path or `dotnet` plus its matching DLL, and verify the required runtime is present. The application and exec probe need the same UID and reachable endpoint filesystem context. The container check used a writable `/tmp` containing the real socket; a read-only root filesystem therefore needs an appropriate writable endpoint location. Actual application-image, kubelet, concurrency and deployment-policy qualification remain separate from that bounded check.
