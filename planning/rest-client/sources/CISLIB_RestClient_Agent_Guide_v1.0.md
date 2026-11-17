# CISLIB REST Client — Agent Guide

**Version:** 1.0  
**Date:** 17 November 2026  
**Companion specification:** [CISLIB_RestClient_Implementation_Specification_v1.0.md](CISLIB_RestClient_Implementation_Specification_v1.0.md)

## 1. Purpose and authority

Use this guide when implementing the REST client, reviewing its behavior, or adding a consumer. The companion specification defines the proposed contract. Read the current repository instructions, accepted specifications, existing implementation, dependency policy, and CIS workflow before changing code. Report material conflicts with newer accepted decisions; do not silently replace either contract.

This package specifies work; it does not establish that the APIs already exist. Verify namespaces, types, registration methods, configuration keys, and metrics against the target checkout. Keep proposed APIs distinct from implemented APIs in examples and handoffs. Do not fabricate registration helpers or CLI commands.

The reviewed baseline is CISLIB main at [7b5923c031027b932f730e1cf45b2d51270b17cc](https://github.com/andrewspiteri/CISLIB/commit/7b5923c031027b932f730e1cf45b2d51270b17cc). Read its current `AGENTS.md`, `docs/architecture/csharp-dependency-policy.md`, `.github/instructions/cis-import.instructions.md`, and the Metrics contract/collection guide before implementation. Recheck the actual checkout rather than treating this reference as the latest forever.

Implement C# first, preserving common consumer semantics for later languages. Do not create other language implementations, a general HTTP framework, a new test harness, or a new development workflow. This guide does not authorize installing a skill.

## 2. Use the existing implementation workflow

Import the specification through the repository's established CIS requirements workflow. Produce acceptance-led implementation tasks using existing conventions. Keep tasks small enough to assign clear ownership and demonstrate their behavior with focused checks.

Settle shared contracts before dependent work. Parallelize independent implementation, tests, and documentation using explicit file ownership; serialize changes to shared contracts and project/solution files where required by repository instructions. Do not introduce additional approval checkpoints beyond applicable existing requirements and the user's authorization.

Read the current plan and status before continuing work. Reuse valid completed results. Rerun checks for affected behavior and required gates, not every previous module. Report a concrete blocker or incompatible dependency decision promptly without turning routine implementation choices into repeated permission requests.

## 3. Reuse the native implementation

Use `HttpClient` and its supported handler infrastructure for HTTP transport, connection management, and native diagnostics. Reuse `System.Text.Json` for the default JSON representation. Follow the repository's approved client lifetime strategy; do not create and dispose a complete client/connection pool per call.

Use factory/host integration only where available under the actual project dependency policy. Keep optional transport, hosting, or resilience dependencies inside the appropriate adapter package. Do not silently add packages to dependency-free contracts. Do not build replacements for native pooling, HTTP serialization, telemetry collection, or general resilience infrastructure.

Core's current approved closure is precise: Logging/DI abstractions 10.0.12 plus the approved ASP.NET framework reference. The proposed `CIS.RestClient.Resilience` integration must record its own compatible dependency closure without expanding Core or borrowing the Metrics exception. A selected retry policy with no registered executor is a configuration error, not permission to ignore retries.

Public consumer contracts use CISLIB-owned request, result, policy, and context types. Framework HTTP messages, handlers, serializer options, and telemetry types belong behind the adapter or in explicitly platform-specific configuration. Normal C# asynchronous and cancellation conventions remain appropriate.

## 4. Preserve the result contract

The public methods return `RestResult<T>` asynchronously. Its top-level properties are `Success`, `ErrorCode`, `ErrorMessage`, `Value`, and `Context`; value presence is `Context.HasValue`. `Value` is the actual decoded `T`, not serialized JSON or an untyped substitute.

Expected HTTP, remote business, transport, timeout, and response-decoding failures follow the specification's result mappings. Do not throw a business exception because the remote service rejected an operation. Caller cancellation remains a canceled task. Programming defects and invalid configuration must not disappear into an indiscriminate `catch (Exception)`.

Keep portable error codes separate from remote business codes and HTTP status in context. Decode successful payloads according to the requested contract. A response mapper may recognize a business failure carried in HTTP `200` and produce an unsuccessful result. Ordinary HTTP failure bodies must not be mistaken for the success payload.

Distinguish a valid empty response from malformed content. A permitted no-content response can succeed with `Context.HasValue == false`; malformed expected JSON is a decoding failure. Follow the specification's treatment of JSON `null` and value types. Consumers must check both success and value presence before using `Value`.

Representable JSON `null` is a present value. `RequireValue` enforces presence; a mapper enforces non-nullness. Do not infer runtime top-level reference nullability from C# annotations. The dispatch count begins after authentication immediately before the primary transport, so pre-dispatch rejection or a handler short circuit has zero dispatches.

## 5. Make cancellation effective

The total deadline covers preparation, authentication/customization callbacks, attempts, upload, response-body consumption, decoding, and retry delays. Link it with caller cancellation and propagate the resulting cancellation through every owned asynchronous operation.

When the deadline expires, cancel the underlying work and await its cancellation and cleanup before returning the timeout result. Do not merely race a timeout task against HTTP work and abandon the losing operation. Release owned responses, content, and streams on every exit path. Keep cancellation resources alive until owned work is finished; any streaming API needs explicit lifetime ownership.

Cancellation is cooperative: signaling the deadline does not promise instantaneous termination. Extensions must observe cancellation. Local cancellation also cannot guarantee that the remote server did not already process the request. Distinguish the originating cancellation sources using the specification's precedence rule. Configure native timeout behavior consistently on an owned client; never mutate a shared client's timeout per request.

## 6. Apply only explicit retry policies

Retries are disabled until explicitly configured. Preserve three per-call choices: **inherit** the client policy, **none** to disable retries, and **replace** with the complete supplied policy. Do not silently merge policies or layer independent retry owners.

The standard preset allows two retries, giving at most three attempts. It starts local exponential backoff at 500 ms, applies the specified jitter, and caps the local delay at five seconds. Eligible transient statuses include `408`, `429`, `502`, `503`, and `504`; use the specification's complete eligibility rules rather than retrying every failure.

A valid `Retry-After` is a server minimum. The final wait must not be shorter than either that minimum or the computed local delay. The five-second local cap must not shorten a server-directed wait. If the remaining total deadline cannot accommodate a retry, stop; do not send earlier or reset the deadline.

Default method eligibility is replayable `GET`, `HEAD`, and `OPTIONS`. Other methods require explicit assurance of actual idempotency and replayability, including `PUT` and `DELETE`. For operations relying on a server-supported idempotency key, preserve the same key across attempts. Merely adding an arbitrary header does not establish idempotency.

Build a fresh request and replayable content for each attempt. Do not assume a consumed stream can be reused. Do not retry decoding errors, business failures, or authentication failures under the standard transient policy. Avoid nested retries in authentication, native handlers, and outer wrappers.

## 7. Keep extensions ordered and observable

Use per-call callbacks for customization and authentication, the agreed ordered handlers for reusable HTTP behavior, and the response mapper for business interpretation. Preserve the fixed workflow; do not introduce a configurable stage graph.

Customize the request before authentication. Authentication/signing must observe the final method, destination, relevant headers, and content representation; later extensions must not invalidate signed fields. Respect the specification's once-per-call and per-attempt callback boundaries. Callback state must be isolated between concurrent calls, and authentication must not mutate shared default headers. Apply the defined redirect credential policy.

Honor method-specific rules: TRACE cannot carry a body or credentials. Apply its guard after customization as well as before dispatch; do not run inherited authentication for TRACE.

An HTTP exchange handler does not by itself cover decoding or business classification. Preserve the outer logical-operation boundary so result mapping, deadline handling, cleanup, and logical metrics include the complete operation.

## 8. Instrument and verify proportionately

Follow the current CISLIB Metrics specification and metric catalog. Record the public logical operation once, including its final result after decoding and mapping. Reuse native HTTP transport metrics. Explicit retry attempts have their own owned boundary; do not equate native histogram counts with logical calls or guaranteed network-attempt counts.

Use bounded registered dependency/operation aliases and finite outcome categories. Never label metrics with raw URLs, query strings, request IDs, remote business codes, exception messages, or credentials. Preserve mandatory application/environment identity and disabled-metrics behavior. Do not buffer bodies to manufacture byte measurements.

At the reviewed baseline the collector selects inbound native HTTP metrics and drops unselected instruments. Implement the specification's bounded outgoing `System.Net.Http` selection and REST catalog/owner attachment; do not assume outgoing collection already works or add an independent provider. Use the host meter factory and preserve exact meter ownership, stream capacities and two-host isolation. Verify actual exported identity and values with existing fixtures.

Use the repository's existing native C# MSTest conventions and fixtures. Focus checks on result classification, empty/malformed bodies, callback order and isolation, retry eligibility and stable keys, `Retry-After`, total deadlines, and cleanup. Cancellation checks must demonstrate that in-flight owned work observes cancellation and finishes cleanup, not merely that the public method returns quickly. Use deterministic time and controlled transport responses where existing seams allow them. Do not create a new harness, broad benchmark/mutation campaign, large evidence bundle, or unrelated test expansion.

## 9. Consumer example

This example uses the proposed API; verify it against the implementation before copying it into an application.

```csharp
RestResult<ResultContract> result = await _restClient.Post<ResultContract>(
    url,
    new RestRequestOptions
    {
        Body = RestBody.Json(command)
    },
    ct);

if (!result.Success)
{
    // Handle the documented ErrorCode and ErrorMessage.
    // Context contains HTTP and remote-error details where available.
}
else if (result.Context.HasValue)
{
    ResultContract value = result.Value;
    // Use the decoded result.
}
else
{
    // Handle a successful response without a value.
}
```

Use the response mapper for a remote envelope that reports business failure inside HTTP `200`. Configure retries explicitly for that dependency/operation; do not add an independent retry loop around this call. Let caller cancellation propagate normally.

## 10. Implementation-agent handoff

> Read the current repository instructions and CIS workflow, then import the companion REST Client specification using the existing process. Verify the current APIs and dependency rules, identify material conflicts, and create acceptance-led tasks with clear file ownership. Implement the agreed C# client using native .NET facilities, `RestResult<T>`, effective cancellation, explicit retry policies, ordered extensions, and existing Metrics integration. Parallelize independent work after shared contracts are settled. Use focused native MSTest checks and required repository gates. Update consumer examples and the metric catalog. Report implemented behavior, verification, and remaining gaps; do not create a new framework, harness, approval workflow, or skill.
