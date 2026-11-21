---
title: Durable operation observations
type: guide
status: Draft
---

# Logging and Metrics

Operations observes the public Execute and GetResult boundaries through a scoped
decorator around the sequential coordinator. It captures the registered alias
and one optional host binding before scope authorization, then completes exactly
once after return, original fault or actual lookup-owner cleanup. The coordinator
retains its explicit transaction and native-callback ownership.

Normal calls are quiet by default. Set `OperationDefinitionOptions.LogCalls = true`
for safe normal-call Debug summaries on that definition. The option is copied
when the immutable definition is constructed. Failed and rollback-only attempts
emit one safe Warning summary even in quiet mode. Expected cancellation is quiet.
Summaries contain only operation alias, method, fixed outcome and nullable elapsed
seconds. They contain no exception, key, scope, result, credential, SQL, caller or
business message. RM-02 remains the owner of native transaction diagnostics and
commit/cleanup facts; Operations never repeats those facts as commit success.

Select optional collection through existing `AddCisMetrics` in the same provider.
Final passive operation composition works before or after Metrics registration;
registration performs no database work or resolver/handler construction. The
collector attaches the exact instrumentation owner and creates only the two
timers in the [fixed metric contract](../../../contracts/metrics/operations.md).
Core contains no collector SDK, aggregation, observation history or scheduler.

An Execute timer's Prepared outcome describes successful preparation, including
a final business rejection when appropriate. Only the application's later
`TransactionCompletion` establishes Committed, NotCommitted or Unknown. Lookup
Found describes an original committed result; the timer waits for actual fresh
read-owner cleanup even if that cleanup reports diagnostics.

One captured binding is used throughout an active call. Late attachment does not
replay it. Missing monotonic timing is omitted, never zero. Invalid or throwing
ordinary diagnostics cannot replace authorization/audit/codec/native faults,
cancellation tokens or business results. No raw input is admitted as an alias.

No collector, disabled collection and Core module opt-out leave operations fully
usable. A selected collector keeps the existing global stream/cardinality limits;
undersized explicit operation profiles fail activation rather than growing them.
Separate hosts own separate instrumentation and SDK aggregates.

Focused controlled tests exercise the real coordinator, RM-02 owner and SDK
exporter to qualify these observation boundaries. They do not prove PostgreSQL
atomicity, a committed business effect or a network failure. Consult the current
[verification record](../../changes/CIS-0018/verification.md) for executed
candidates, lanes and limitations.
