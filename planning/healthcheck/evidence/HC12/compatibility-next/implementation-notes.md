# HC12 current initial-candidate compatibility

Authored from committed baseline `0bf0c4c8d8c0c6646f591c634e66cfa480348aa6`.
The agent changed only its assigned compatibility test/fixture/evidence directories.
It did not build, test, change project dispatch/dependencies, commit or publish.

Native classes:

* `CIS.Tests.CompatibilityTests.Healthcheck.HealthcheckInitialCompatibilityTests`:
  109 cases, including 54 public type checks and 29 configuration rows.
* `CIS.Tests.CompatibilityTests.Healthcheck.HealthcheckCompatibilityGuardTests`:
  18 cases covering eight specified API breaks, removal/addition, eight observation
  mismatches and object-order normalization.

Expected focused selection: project `CompatibilityTests`, filter
`FullyQualifiedName~CIS.Tests.CompatibilityTests.Healthcheck`. Expected authored
total: **127 cases**, pending coordinator native discovery/execution. No new
project reference or friend assembly is needed.

The initial API fixture is independent of reflection output and pins the frozen
HC00 receipt. Native code guards the selected new fixture manifest and all original
HC00 schema/wire inputs before/after execution. Public service/formatter output and
the actual internal protocol reader are the subjects. Test-only reflection reaches
the reader; this is distinct from public API and real transport evidence. The
publication ledger remains empty, with an explicit native assertion against
silently treating this candidate as released history.

Graph discovery started fresh: schema 2, build
`sha256:75f0f6765ee7b666e27bb3535623c28e40283c1996868f232c59d086c242edb7`,
15,146 nodes/81,014 edges, zero errors/warnings. An exact `HealthcheckOptions`
symbol query matched its source line with no truncation. Canonical contracts and
source were read for assertions. A first unsupported `graph find --name` invocation
returned usage only; the exact-ID follow-up succeeded. New source requires the
coordinator's derived graph refresh after integration.

Remaining qualification boundaries: no historical published Healthcheck consumers,
exact package and six-family release qualification, full Windows/Linux receipts,
coverage/mutation and unavailable real Heartbeat/startup-coordinator integration.
No external obligation is waived by these initial-candidate checks.
