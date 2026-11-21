# Durable operation observations

Operations uses two conditional seconds timers when a passive Operations
composition and Core module collection are selected. Histogram counts supply
attempt counts; there are no duplicate operation counters, active gauges,
transaction commit measurements or per-native-phase instruments.

| Logical name | Meaning | Dimensions | Reserved combinations |
| --- | --- | --- | --- |
| `cis.operation.execute.duration` | One durable attempt from entry before authorization through its return or original fault. Prepared means staged work. | `operation`, `outcome` | 16 aliases × 13 outcomes = 208 |
| `cis.operation.lookup.duration` | One authoritative lookup from entry before authorization through actual method-owned read cleanup. | `operation`, `outcome` | 16 aliases × 9 outcomes = 144 |

Execute outcomes are `prepared_succeeded`, `prepared_rejected`,
`replayed_succeeded`, `replayed_rejected`, `key_conflict`, `result_expired`,
`incompatible`, `contended`, `retry_required`, `denied`, `cancelled`, `failed`
and `rollback_only`. Lookup outcomes are `found_succeeded`, `found_rejected`,
`not_found`, `result_expired`, `incompatible`, `unavailable`, `denied`,
`cancelled` and `failed`. Cancellation requires the supplied cancellation token
to be requested; an unexpected cancellation fault is `failed`.

Only a known registered alias is observed. Unknown names never become labels.
Keys, trusted/requested scopes, resource identifiers, fingerprints, caller or
instance identifiers, business codes, results, SQL and exception messages are
absent from dimensions. Every actual exported sample retains central
`application` and `environment` labels, the standard fourteen finite duration
bounds and infinity. Expected exporter families are
`cis_operation_execute_duration_seconds_{bucket,sum,count}` and
`cis_operation_lookup_duration_seconds_{bucket,sum,count}`.

Each method captures one complete host binding before authorization. Attaching
collection during an active call does not replay it into the new binding.
Unavailable or invalid monotonic time omits the duration; it never invents a
zero measurement. First completion makes at most one recording attempt.
Ordinary metric/logger failures preserve original results, faults and cleanup.

The two streams reserve 352 combinations within unchanged global capacities.
With the existing default incoming HTTP/Core/RM-02 composition and no other
conditional module, the catalog has 26 streams and 3,808 combinations (previously
24 and 3,456). Explicit stream overrides below 208 or 144 fail startup. Disabled
collection or `CollectCoreModules = false` leaves Operations usable and adds no
operation streams. The collector verifies the exact registration-owned
instrumentation identity and prevents cross-host attachment.

The [Operations observation guide](../../docs/modules/operations/observability.md)
describes logging and actual ownership. Passing evidence belongs to
[CIS-0018 verification](../../docs/changes/CIS-0018/verification.md); controlled
SDK collection tests do not establish SQL durability or release qualification.
