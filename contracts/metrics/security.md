# Security metric catalog

These three logical instruments belong to the security operation/state owners.
Core defines them through the Metrics facade; the optional collector selects them
only when security is explicitly registered and Core collection is enabled.
Existing hosts and catalogs remain unchanged when security is absent.

| Logical name | Exported name | Kind / unit | Dimensions and boundary |
| --- | --- | --- | --- |
| cis.security.authorization.decisions | cis_security_authorization_decisions_total | Counter / Count | outcome=allow or deny; profile=registered alias or unresolved. Exactly once for the final composite native authorization result, including additional native requirements. |
| cis.security.certificate.reloads | cis_security_certificate_reloads_total | Counter / Count | outcome=adopted, rejected or deferred; profile=registered alias or unresolved. A materially changed candidate transition, not every periodic check. |
| cis.security.certificate.remaining | cis_security_certificate_remaining_seconds | Observable gauge / Seconds | profile=registered alias. Retained active-leaf expiry minus current time, clamped to zero; omit when no leaf expiry is known. |

The counter schemas use the fixed key sequence outcome,profile; the gauge uses
profile. There are no histograms, credential IDs, subjects, resources, policies,
raw issuer URLs, certificate serials/thumbprints or exception-text dimensions.
The native HTTP instruments retain their existing request/attempt boundaries;
security does not record another incoming duration or transport exchange count.

Registration declares a finite maximum profile capacity. With the default16
aliases and one unresolved category, the collector reserves34,51and17 combinations,
102total across three streams. Smaller overrides are rejected. A deliberately
higher profile bound must fit existing global stream/definition/cardinality limits;
neither it nor telemetry failure grants more authority or changes business results.
SDK zero-dimension and overflow samples retain the established additional meaning.

Core sources start without a recording attachment. The exact host collector may
attach one reusable handle bundle only when Core/security collection is selected.
No-op operation, disabled Core collection and both registration orders preserve
their existing semantics. Counters remain cumulative for the provider lifetime.
The remaining gauge reads bounded owned memory only and performs no source I/O
or renewal work during a scrape. Callback admission/settlement remains collector
owned before credential/status owners are torn down.

Every actual exported sample retains EnvironmentOptions application/environment,
including native/overflow samples. The implemented security and consumer tasks
record the actual scrape evidence in CIS-0016; this catalog alone is not a passing
collection, renewal, federation or deployment result.
