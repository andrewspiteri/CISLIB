# HC13 qualification reconciliation

The user-authorized transport/application commit is
`0bf0c4c8d8c0c6646f591c634e66cfa480348aa6`. The later qualification work remains
uncommitted. The [HC12 follow-up](../../HC12/qualification-next/README.md) and
[receipt](receipt.json) supplement historical receipts, including the earlier
7,839-case checkpoint; those records are unchanged.

Developer04 passed all 16 stages and 7,978 native cases, with no failures/skips.
Windows initial compatibility passed another 127 selected cases. Linux passed
991 selected cases across five families, plus a separately executed seven-case
bootstrap regression. These scopes and their source snapshots remain separate.
Package consumers on both systems used the same supplied package/Core bytes.
Linux ordinary-user rejection and recovery were exercised at socket modes 0755
and 0777. The probe's previously uncaught permission SocketException now returns
bounded exit 2.

Measured Healthcheck coverage passed 95% line/90% branch thresholds. Twenty explicit
critical mutation controls passed with clean restoration. Automatic state mutation
measured 196/217 (90.32%) in its tool-classified runnable subset, without timeout
credit. Four collateral compiler rollbacks were separately compiled and detected;
complete mutation/rollback/ignored dispositions and broader module/release gates
remain open. Those observations do not award blanket release qualification.

Acceptance retains all HC-AT-01 through HC-AT-59 requirements. HC-AT-51 is now
locally verified for the executed Linux matched-client/endpoint/permission context.
Local classification is 43 LocallyVerified, 14 Partial, one PendingExternal and
one PendingRelease. Overall remains Pending; completeSourceCases remains zero
under its existing all-scope definition. HC-AT-58 credits the completed subgates
but retains the complete mutation/release/external obligations.

The real publication ledger remains empty. Initial frozen API/configuration/wire
checks executed; no published historical baseline is invented. Real Heartbeat,
watchdog and future IStartable guarantees remain external. Tested ordinary Linux
identities do not establish privileged-root, alternate/elevated Windows identity,
remote-host or arbitrary container behavior.

Three subagents authored separate compatibility, architecture/mutation and
package/coverage work. The coordinator alone changed shared structure and executed
native jobs, with isolated outputs for concurrency. Rework included a false Unix
mode assumption, a genuine Linux probe failure, verifier receipt expectations,
shared launcher locks, formatting and the bootstrap copy. These are recorded in
the retained attempts. No matched serial baseline or complete cost telemetry
exists; this pilot makes no causal speedup claim. Publication and deployment were
not performed.
