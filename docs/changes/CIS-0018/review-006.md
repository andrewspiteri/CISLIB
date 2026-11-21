---
title: WORK-006 public substitution and package review
type: review
status: Draft
---

# Independent source and public-package review

Reviewer: the assigned PostgreSQL writer, reviewing the separate public-helper
author's frozen WORK-006 source. Approximately9 active source minutes, charged
to WORK-006. No native command, database access or product-source edit was made.
The review covers ScriptedDurableOperationService, its25 authored unit cases,
the extended existing CommonTestingPackageTests, CommonTesting README, and the
Operations testing/example guides. It does not review the reviewer's own SQL.

No material required source finding remains in this reviewed scope.

The helper has one locked ordered queue and one independently bounded recording
list. Both default to16 and reject configured values outside1..65536. Admission
checks recording capacity, script presence and exact execution/lookup generic
types before dequeuing or recording. There is no eviction or silently consumed
next script on failure. Calls returns detached read-only immutable metadata
objects. Execution retains the exact borrowed context within its finite count;
lookup retains none. Input, handler, returned result and exception histories are
not retained in recordings. Pending scripted references remain test-owned and
count-bounded; the guide correctly disclaims a bound on arbitrary queued payload
size.

The implementation returns exact public typed outcomes and original exception
instances. Task.FromException preserves a scripted unrelated
OperationCanceledException as Faulted. A supplied precanceled token returns a
canceled task with that token before script consumption/recording, including
the recheck inside the admission lock. The helper invokes no handler, authorizer,
normalizer, journal, weak context admission, SQL, native capability or transaction
completion. Prepared, business decisions and scripted RM-02 Unknown are separate
facts; a RollbackOnly script deliberately does not poison the supplied context.
The authored cases exercise these boundaries and the finite default limits.

The actual package source extends the existing RestPackage local-feed fixture.
It packs the currently loaded Core/CommonTesting payloads without rebuilding,
checks packaged DLL hashes, restores into a fresh owned cache from the local
feed only, validates exact selected dependencies, builds a separately named
outside-friend consumer, and asserts deployed and child-loaded current bytes.
The child consumes only public APIs and verifies exact context, no handler call,
independent Prepared/Unknown/Found facts, queue/recording exhaustion and original
faulted-OCE behavior. It also compares embedded SQL to the public schema API's
exact token substitution; the parent compares the embedded hash to the actual
Core nupkg installation entry. It reads SQL without executing it and checks that
provider/collector/test-framework assemblies were not loaded. The existing
fixture retains child-drain and current-original-assembly safeguards.

The documentation links the actual Core coordinator tests and real-provider
consumer, explains original-result recovery and explicit owner completion, and
distinguishes scripting from native durability. CountryData remains the single
maintained tutorial with its database-free default. No Linux SDK package pass,
native uncertain-commit proof or release result is inferred from authored source.

## Responsibility and necessity

The requested finite public substitution boundary is delivered directly over
IDurableOperationService. A handwritten application stub is possible; the small
shared ordered helper provides the explicitly requested bounded consumer support
without reproducing authorization, orchestration or a SQL engine. The existing
package fixture is extended rather than replaced. No new DI, mocking, workflow,
native harness, persistence or lifecycle framework is introduced.

## Final evidence alignment

An additional4 active source minutes, charged to WORK-006, independently aligned
the final public-helper/package cohort. No native execution, database access or
product-source change was performed by this reviewer. The root-generated
`.artifacts/operations/CIS-0018/integrated-receipt.json` contains46 source hashes
and9 native reports. This review checked the three public-helper/package C#
source hashes, current Core/CommonTesting DLL hashes, both helper TRX hashes and
the final Windows public-package TRX hash against the actual retained files.
They match. The current Core SHA256 is
`76df3a0cada0f502bd063a56509b74d684dd1e2503fca83a1990e6deb11c6340`;
CommonTesting is
`186e29188d446f188ff3d910c9788c27bb11659481e90bd5e6225916daa6b517`.

The final ScriptedDurableOperationServiceTests reports each record25passed,
0failed,0not-executed on actual Windows and Ubuntu24.04. The final Windows
CommonTestingPackageTests report records1passed,0failed,0not-executed. This is
the existing actual fresh-local-feed/cache, pack, restore, build and outside-friend
child consumer described above, not a self-comparison or authored-only package
recipe. Its deployed/loaded current DLL and embedded-versus-nupkg SQL assertions
executed successfully. Retained output records package hashes
`CIS.Core=6335ff6d624e896bcdfd72ad92b796c297396296d04010bc38fb1bc35ecc372e`
and `CIS.CommonTesting=8a2fa9c12960596e6084427d0c45d559c8055226a9b3aec68fdf7810ce741765`,
version0.1.0-dev.1, SDK10.0.401 and runtime10.0.12. Reading the packaged SQL remains
distinct from executing it against PostgreSQL.

The final bundled-validator logs for cislib-operations, cislib-transactions and
cislib-metrics each record “Skill is valid!” under
`.artifacts/operations/CIS-0018/WORK-006/skill-final-validate`.
These are attributed root executions, read here rather than rerun or claimed as
reviewer executions. The focused Operations skill links the actual contract,
Core, provider, retention, observation, consumer and testing guides, and preserves
explicit ownership, authority, result/commit distinctions and evidence limits.
Root separately reports6 minutes reviewing the approved current README/AGENTS,
Operations/CommonTesting/Transactions routes and55 local link targets; this
review does not relabel that work as an independently executed link sweep.

The integrated receipt also records the final current SDK/affected-catalog
cohort; its implementation and provider/retention behavior have separate assigned
reviews. This review assesses only WORK-006 public substitution, actual package
boundaries and documentation. The full Linux SDK-backed package route remains
unexecuted by the approved profile. Neither scripted Unknown nor package SQL
reading establishes native recovery, deployment, network-fault, coverage,
mutation, performance or release qualification. WORK-006 completion still uses
the canonical task dependencies and integrated closeout; this review grants no
additional execution or publication authority. No material required finding
remains in the reviewed final public-helper/package scope.
