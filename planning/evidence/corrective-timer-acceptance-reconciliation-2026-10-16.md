# Timer acceptance-envelope reconciliation

The former Python acceptance driver re-read custom duplicated report envelopes. It is retired after full native semantic reader migration. This table records the disposition of all thirteen legacy corruption IDs; it does not claim thirteen newly executed native cases or current shared-package/both-lane full qualification.

| Historical control | Current native check / disposition |
| --- | --- |
| missing-report | FocusedTestsTests.MissingOrMalformedNativeReportsCannotPass and VerificationEvidenceTests.RejectsIncompleteOrInvalidEvidence (MissingReport). |
| missing-case | Required case/method minima in case-inventory.json and VerificationEvidenceTests MissingRequiredCase / MissingGroupMethod. Every timer mode has an exact catalog check. |
| duplicate-case | FocusedTestsTests duplicate-execution and VerificationEvidenceTests DuplicateDiscoveryId / DuplicateCaseId reject duplicate native identities. |
| failed | FocusedTestsTests failed-case / failed-run and VerificationEvidenceTests FailedCase reject failed execution. |
| assembly | FixtureProcessChecks compares each actual child's Core/fixture hashes against the loaded files and checks those files again after execution. The old second serialized declaration is absent. Full Developer source/artifact qualification remains separate. |
| task | FileTimerQualificationMapTests task mutation and native shared catalogs bind original story/task/criteria. The custom envelope's duplicate task field is absent. |
| base | FileTimerReader is compiled into the rebuilt test/fixture projects. No dynamically imported helper or independently declared base hash remains to reconcile. Full candidate source-inventory qualification remains separate. |
| controls | All 97 original semantic controls have required native method minima. Existing native MissingRequiredCase / MissingGroupMethod controls prove incomplete required evidence fails. |
| verifier | C# comparers and corruption controls compile into the current native test project; child DLL before/after hashes are checked. No independently named Python verifier hash remains. This does not substitute for the full source/artifact qualification lane. |
| inventory | Each family compares exact ordered catalog membership/uniqueness and before/after catalog SHA-256. Architecture checks match fixture declarations to the same inventories and preserve complete classification. |
| platform | Shared process checks compare actual host OS/PID/runtime; acceptance-map platform mutation and native UnknownCaseLane controls preserve lane applicability. |
| stdout | Readers parse the actual drained original stdout directly, stored in native TRX. No separately reserialized report is trusted. Native VerificationEvidenceTests Hash/ReportHashMismatch rejects changed archived native evidence. |
| semantic-consistent-bytes | Qualification's original task mutation changes sameDisposal after a valid physical child; the full comparer rejects it at original task identities. It does not depend on a checksum to detect semantic corruption. |

Selected native checks re-execute the 23 focused-runner controls and ten timer-map checks on each OS. The 45 shared evidence-validator cases pass on Windows; their existing wrapper requires PowerShell, unavailable in this Ubuntu environment. The attempted broader Ubuntu selection is recorded as failed and earns no pass credit. Retained populated evidence inside the validator's own tests qualifies validator behavior only; current timer behavior comes from newly executed fixture children. Historical report hashes and script evidence remain historical.
