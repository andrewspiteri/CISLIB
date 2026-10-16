# Running triage notes (read-only review; not a final verdict)

Source snapshot: canonical ba6eb62d for current-code checks; approved prompts retain their own older versions.

## source-inventory

- F1: reproduced on native Ubuntu using the built checker. A symbolic alias to a Git root includes ignored.txt, whereas the canonical root excludes it. source-inventory-probes.json retains exact paths and outputs. This branch-selection defect is also inherited from the old PowerShell path-string comparison; the current documented Git-root contract still warrants a bounded fix.
- F2: the relative-path claim did NOT reproduce. The existing PowerShell bridge resolved relative repo correctly after Set-Location, with exact canonical output. verify.ps1 and replay.ps1 pass absolute roots. PSDrive handling is a separate unsupported-input question, not an established current-call failure.
- F3: ambient Git environment inheritance is visible in source and predates migration. Repository-isolation hardening belongs to the broader provenance/replay work; no fresh isolated-env counterexample has been executed yet.
- F4: the test project has no build dependency on the checker. The supported eng/test.ps1 bootstraps the checker, and full Developer builds the solution before tests. Bare dotnet test / IDE prerequisites are a separate qualification gap; no stale binary was credited by the completed profiles.
- F5: structured source-capture exceptions lose native stderr detail; raw outer verification logs retain it. Low-severity diagnostic improvement, not loss of all failure evidence. Source capture occurs before Invoke-VerificationStage in verify.ps1, limiting the review's stage-detail explanation.
- F6: no tracked gitlink (160000) or symbolic-link (120000) entries were found in the canonical index. These unsupported-input questions fail closed and are not observed inventory defects for this checkout.
- F7: file-link following is inherited; directory links are skipped exactly as documented. Physical path confinement is still part of unqualified replay work. Do not label this a new regression.
- F8: the case's generic passive-correction provenance text is inherited from the existing entry; a clearer migration-specific label is reasonable cleanup.
- F9: additional suggested controls are not an exhaustive-coverage promise in the docs. Add a focused alias-root regression with F1; evaluate others by actual supported requirements.

## 01 configuration

- F1 (short-name temp-root oracle): production masks ~ while expectations use Path.GetFullPath, but the omitted TemporaryDirectory helper ALREADY normalizes Path.GetTempPath through Path.GetFullPath before creating its owned root. Existing real-alias cases use literal Path.Combine expectations, not the naive relative object branch. The reported concrete default-temp failure is not established; a Windows observation is pending.
- F2: confirmed circular observation. ConfigurationPathProcessFixture writes validatedWorkingDirectory from its own captured variable, not from PreparedConfiguration. Actual before/after Logging.FilePath is genuine storage evidence. Relabel/remove this claimed base-storage observation rather than add API solely for tests.
- F3: confirmed reader weakness: a serialized JSON substring check misses escaped sentinel text outside decoded message/exception/explanation fields. Traverse decoded string leaves for full-output exclusion checks.
- F4: confirmed shape parity gap: null-valued JsonObject indexers accept absent members; the old readers required several keys to exist. Require presence where the fixture contract calls for explicit null; metadata Logging should be absent. The conditional expected-values subset also silently ignores a wrong type.
- Q identity/denominators: resolved by supplied canonical FixtureProcessChecks. It pins .NET 10.0.12, disabled roll-forward, OS/PID, fixture/Core/input hashes before/after, exit 0 and empty stderr, as well as schema/count/unique IDs. These checks are not missing.

## 03 events

- F1: static source confirms EventProjectionProcessTests.Capture lacks the ACTIVITY sentinel check retained by the old correlation reader. Add the small independent no-leak guard; exact projection checks elsewhere do not cover arbitrary extra report fields.
- F2 (macOS label): fixtures differ, but macOS is not a supported verification lane and the old readers only accepted Windows/Linux. Treat as optional consistency, not a supported-lane regression or a new platform commitment.
- Q1/Q5: resolved by FixtureProcessChecks (identities and catalog denominators are checked).
- Q2: resolved: LogValueProcessTests.Find delegates directly to FixtureProcessChecks.Find, so both expectation and executed-input lookup use the same resolver.
- Remaining comparator-control/catalog questions await canonical checks and other approved bundles. Missing supplied context alone is not a finding.

## 02 console

- F1: the swapped-stream control still rejects an observable routing violation through the line-count guard. Reason-specific predicate protection would test redundant implementation branches; no production acceptance failure was established. Optional test diagnostic improvement, not a required new control merely to mirror implementation.
- F2: confirmed low provenance gap: ConsoleCompositionFixture, ConsoleOwnershipFixture and ConsoleStartupFixture parse and hash separate file reads. Bind parsing and hashing to one byte array in a bounded follow-up.
- Identity/resolver questions are resolved by FixtureProcessChecks and the shared Find delegate. Whole-stdout JSON parsing permits pretty JSON, while physical log stream checks are separate. No missing physical-stream evidence should be inferred from a bounded prompt.

## 04 exceptions

- F1: supported independent-expectation gap: ExceptionContentExpectations derives expected probe labels/counts from ExceptionContentSources.Create, the same builder supplying input. Removing omitted probes can erase the observation as well as its expected count. Restore independent labels/counts rather than introduce instrumentation infrastructure.
- F2: missing native binding alone is rejected by current actual diagnostics. The C# exception-reader-probes.json reproduction shows that a missing binding PLUS forged null diagnostics is accepted. Classify as a malformed-metadata guard gap; do not claim current fixture echoes catalog bindings.
- F3: reproduced: an observer message-read count changed from 1 to JSON 1.0 is accepted. Existing integer-shape guards cover other count fields but omit message/stack observations.
- F4: the captureCalls literal is inherited bookkeeping, while getter-count and post-capture mutation observations are real. Clarify claims; no new instrumentation layer solely for a counter.
- F5: supported explicit-null vs missing-member parity gap in null-projection mode.
- F6: separate parse/hash reads are the same low provenance gap as console.
- Q5 is refuted: ExceptionQualificationFixture records actual exception.GetType().Name and ParamName, not binding echoes. Shared identity/resolver checks and the hard-coded private sentinel are present.

## 05 logger

- F1: supported row-binding gap: LoggerFoundationExpectations hard-codes 16,384 for truncate and 65,536 otherwise, rather than binding maxEntryBytes to the selected row. This is a verification-reader issue, not a demonstrated runtime cap failure.
- F2: ComparatorRejectsCorruptedOriginalBytes does not first validate its unmodified fixture result; Execute does not itself call Validate. Separate positive case tests still run. Low control self-containment improvement.
- Q1: current LoggerOutcomeTests pins 125 unique rows but loops only catalog-supplied modes; it does not independently pin the seven mode names / exact 98 ordered-pair IDs from the old reader. Independent catalog-completeness guard gap; current catalog is not claimed malformed.
- Shared identity/path questions are resolved by FixtureProcessChecks.

## 06 rendering support

- F1: supported UTC-shape gap: DateTimeOffset.Parse accepts offset-less values using host-local offset, so a UTC host cannot distinguish a missing UTC designator. Restore an explicit UTC token/roundtrip-kind check.
- F2: runtime-corruption control is rejected by another equality guard if the exact pin were removed. The exact pin itself is present. Treat as optional control isolation, not proof that the current reader accepts a wrong runtime.
- F3: no repository contract requires every malformed display report to throw InvalidDataException. Current HumanRenderingTests explicitly expects DecoderFallbackException for invalid UTF-8. A blanket wrapper is optional taxonomy change, not a proven parity loss; preserve actual specific controls.
- F4: confirmed documentation overstatement: stable application facts are compared across five native children; all seven have unique instance IDs, while two controlled projections intentionally change BuildVersion.
- Missing-context questions: HumanRenderingTests retains eleven faults and spacing/order control; JsonlReuseTests retains nine faults and layout control; RenderingStoryTests retains eleven faults and four positive variants plus matrix/criteria checks. These controls are not absent merely because omitted from this prompt. Full Ubuntu Developer evidence for ba6eb62d exists and is later than the old prompt snapshot. DiscoveryProcessTests is DoNotParallelize.

## 07 scopes

- F1: REFUTED by canonical OperationScopeQualificationTests.CompleteScopeComparatorRejectsChangedIdsValuesOrderTypesAndFields: all six named corruption controls already exist as native unit rows. Adding duplicate process controls is unnecessary.
- F2: child parse/hash double read is a supported low provenance gap. The parent-side concern is resolved: RunAsync pins and verifies the supplied input path hash; LogValueProcessTests.Find directly delegates to FixtureProcessChecks.Find.
- F3: supported inherited expected-reason ambiguity: CountingMap non-enumerating members throw the same InvalidOperationException as the depth guard without incrementing traversals. A distinct sentinel type or counted accessor can make the evidence unambiguous if this observation is retained.
- F4: optional unsupported-platform label consistency; Windows and Ubuntu are supported lanes, macOS is not newly promised.
- Reentrant scope question concerns intentional no-overwrite behavior; no production defect established. Empty stderr and process identity are checked by the shared parent helper.

## Probe updates

- Configuration F1 concrete default-temp failure is not established: configuration-alias-probe.json confirms that a direct naive short-path oracle differs, but the actual helper-normalized owned base produces the same path as production. No files were created in Program Files. Treat this finding as refuted for the reported current-helper path; retain the useful lexical-alias mechanism observation.

## 08 values

- F1: REFUTED by the omitted LogValueNormalizationProcessTests.Rows. It selects the exact complement of OriginalKinds from event-value-cases.json plus the normalization catalog, then calls LogValueProcessTests.Verify. UUID/timestamp and unrecognized kinds are not silently dropped. Required bindings independently retain minima 37 portable and 22 normalization process executions.
- F2: integration Verify accepts either ArgumentException or ArgumentOutOfRangeException, and the child Validate does not bind an exact category for invalid rows. This broadness is real, but independent LogValueTests.UnsafeNativeIntegersNeverChangeToString and NonfiniteNativeNumbersFailWithAStableArgumentCategory already pin exact out-of-range categories and parameter/ActualValue behavior. Do not infer absence of type-specific guards from this bundle. Per-row integration binding is a possible low improvement, not an established Core defect.
- F3: child parse/hash double read is the same low provenance gap. Parent input hashes are already pinned by FixtureProcessChecks.
- F4: unsupported-platform label consistency only; no new supported lane is promised.
- F5: lifecycleApiPresent probes Initialize only. Rename the observation to its actual scope if retained; this single field does not prove absence of all conceivable lifecycle APIs. No production startup call or public lifecycle regression was established.
- Shared identity questions are resolved. Adapter Project is explicitly test-only serialization, so value-process rows establish factories/owned values and not production rendering; separate renderer tests cover that behavior.

## 09 flush

- F-1: confirmed intended-reason parity gap. FlushReaderMigrationTests retains 88 faults but asserts exception type only. The old readers asserted each intended reason; several new expectation failures share generic messages and InvalidOperationException can also originate from JSON accessors. Restore stable reasons directly in the existing matchers and rows, without a new framework.
- F-2: confirmed current-documentation drift: flush-coordination still promises exact-reason controls; flush-stress still describes a present-tense Python reader and zero retained progress controls; flush-results still says deleted imported helpers remain. Correct the current narrative alongside the bounded reader work, preserving historical receipts.
- Q identity/physical bytes: LifecycleProcessChecks delegates to FixtureProcessChecks and compares actual physical bytes / releases file leases for boundary and result modes. This resolves PID/runtime/hash and physical file-byte questions. Full physical inventory document/membership parity and duplicate stdout-key rejection are not visibly established by these shared helpers; retain as bounded follow-up questions, not a blanket parity assertion.
- Q3 stress mapping is resolved by FlushAcceptanceArchitectureTests.Validate: it pins all eight literal mode IDs and bidirectional acceptance-criterion bindings to the original source plan. The 88-control inventory binding is present.
- Q4: zero maintained Python implementation files remain; frozen historical readers are not active importers.
- The controlled zero-pending TimedOut oracle question needs a targeted scenario review; no runtime defect is inferred from the simplified fixture formula.

## 10 lifecycle

- F1: confirmed intended-reason gap across shutdown/cleanup/creation controls, analogous to flush. Existing generic messages plus framework InvalidOperationException cannot independently establish the intended rejecting reason. Keep the fix direct in existing readers/control rows, retargeting coupled mutations when needed.
- F2: documentation scope concern is supported, but absence of incoming-expiry coverage is REFUTED. IncomingRuntimeTests.OneStopDeadlineClosesAdmissionAndAbandonsUndispatchedInput runs the actual incoming pipeline via incomingPipeline:true and checks timeout, two abandoned inputs, closed admission, no late dispatch, distinct eventual Completion and immutable repeat/diagnostic result. The corresponding queue tests also check expiry and no late commit. incoming-pipeline.md explicitly distinguishes internal output-stage seams from the public composition. Link this evidence and label stop-drain.md's older scope instead of adding duplicate tests solely because omitted from a prompt. Full public exact-candidate release qualification remains open.
- F3: confirmed current-guide drift: stop-drain.md still says public creation, DisposeAsync and cleanup work remain following tasks despite lifecycle-cleanup.md describing completed development behavior. Reconcile current guidance without rewriting frozen receipts.
- Q1: resolved by canonical source and the migration receipt. StopCleanupTests has 13 dynamic fixture modes plus two StartupKeepsItsSafeFailureWhenWorkerCleanupThrows rows = 15 unit cases, separate from 166 integration executions. No double count was established.
- Q3: shared helper identity checks are present. Q4 ordering is AreEquivalent rather than exact legacy order; IDs/denominators matter for execution, and catalog ordering is not established as a public behavior contract. Q5 incoming loss is handled in LogFlushResult context from the other bundle.
- R1/R2 supplied fix context was found consistent by Claude. Internal-only startup-launch seam questions are not demonstrated public leaks.

## 11 provider implementation and native readers

- F1: confirmed current foundation-guide drift, including ISupportExternalScope, structured/EventId mapping and copy timing. Direct provider-created scopes copy at BeginScope; LoggerFactory host scopes copy on accepted event capture. Update the current guide or clearly label its historical foundation slice.
- F2: REFUTED as a missing-disposition finding. planning/evidence/corrective-provider-scopes-hosts-2026-10-16.md already has an explicit superseded-semantics table for all named modes, rejection reasons and EventId budget changes, grounded in the 15 October behavior addendum. Preserve that approved behavior; do not restore obsolete Python expectations.
- F3: supported explicit-field gap: protected env/version checks use != fake, which accepts absence. Require the automatic fields to be present strings before verifying protected values.
- F4: supported evidence inconsistency among standalone provider process tests versus ProviderReportProcess. Some lack command/PID/report retention and independent post-run lease acquisition. Bound any consolidation to physical test support, preserving actual bytes and identities; no release evidence is inferred from pass/fail alone.
- Q1 cap concern is resolved: all relevant Provider* fixtures explicitly set MaxEntryBytes=16384. The default 65536 decision is not contradicted by these selected fixture caps.
- Q3 long range behavior is resolved by LogValue.Integer's actual safe-range guard and native exact-category unit tests; long does not silently escape the safe range.
- Q4 ownership wording duplicates bundle 12 F-1 below.

## 12 provider deleted readers / bounded Core

- F-1: confirmed ownership wording mismatch. runtime-contract.json says disposal stops provider-owned scopes, while CoreLoggerProvider.Dispose only disables calls/releases the borrowed host source; creator-owned handles survive and remain creator-disposed. Correct wording to the existing implementation and user ownership decision; do not implement automatic cross-flow scope disposal.
- F-2: REPRODUCED against built Core in provider-map-probes.json. A nested map's invalid surrogate key at index 1022 (the first omitted key after exhausting the node budget) throws ArgumentException; moving it to index 1023 yields accepted truncated capture. Map validates a key before checking remaining/depth, unlike the root capture loop. A bounded pre-key budget/depth check is a plausible correction with one focused boundary regression. This is an internal capture reproduction, not a full public runtime execution.
- The native-reader parity checklist is mostly addressed by bundle 11 and the existing provider migration receipts; missing files from this particular prompt are not missing implementation.
- Q1 incompatible borrowed frames are intentionally invalid per the 15 October correction and the explicit migration disposition; do not reinstate obsolete truncated-frame semantics.
- Q2 long safe-range validation is present. Q3 event-name source-generator conventions need their actual supported contract, not an inferred naming expansion. Q4 bounded root metadata scanning is intentional in source and qualified separately.

## 13 queues and diagnostics

- F-1: confirmed public XML mismatch: LogOutputDiagnostics.QueueFull is per-output saturation, but its summary calls it incoming. The actual counters are separate. Correct the comment; no accounting implementation change is indicated.
- F-2: confirmed current output-queues.md historical/current narrative drift, including a present-tense retired Python comparator and already-completed diagnostics/lifecycle scope.
- F-3: optional unsupported-platform consistency, inherited and outside the two supported native lanes.
- F-4: supported exact-keyset gap: expected-null fault can be renamed while preserving count, because missing JsonObject lookup compares equal to null. DiagnosticsExpectations and OutputStateExpectations should pin exact key names, with a focused null-key substitution control.
- Core R1 snapshots/readonly construction and the 23-mode JSONL binding are reported consistent in supplied source. No shipping accounting defect established here.
- Shared child identity checks and exact source-catalog hash comparisons are present. In-process comparator controls and physical positive process rows are distinct evidence, as the migration receipts record.

## 14 deleted queue readers / Core context

- F-1 duplicates bundle 13's XML mismatch and should have one correction/disposition.
- Q2 reserved writer names is REFUTED: LogWriterRegistration rejects exact console/file names in its constructor before composition.
- Q3 enum initialization is resolved: LogOutputState.Running is explicitly zero.
- Q5 suppression race is REFUTED in current code: PrepareIncoming returns commit closures, and RecordSuppressed/RecordDuplicateKeyEviction execute from those closures under the incoming admission gate, after the abandoned-preparation check. They are not performed speculatively during preparation.
- Q1 shutdown timer cleanup fault occurs after immutable stop publication (including a timed-out result); a timed-out path has already run incoming/output abandonment. No stranded-work counterexample was executed. Keep this as a call-order review question, not a confirmed cross-output defect.
- Missing native comparator context is supplied across the earlier queue/provider/lifecycle bundles and existing receipts. Do not count context omission as a second missing migration.

## 15 governance guidance

- G1: latest human approval explicitly supersedes the older stop direction for this exact manifest. This receipt records the exact authorization and completed/pending review scope. A dated AGENTS entry is useful for future repository-only readers; it must not imply broad authorization for changed payloads.
- G2: supported low terminology ambiguity: Core's dependency boundary means Change Impact Studio tooling/CLI, not the CIS namespace of this library.
- Q1: graph-inputs explicitly excludes implementations/csharp/verification, which contains maintained C# file applications. Record that navigation limitation; do not modify the user-owned exclusion file during review.
- Q2 PowerShell policy remains recorded open, with source inventory newly migrated and release evidence validation next. Q3 configuration schema 1 and graph store schema 2 are different artifacts, not a schema mismatch.
- Contract provenance pointer C1 can be clarified, but the fileRecovery pointer specifically refers to the 15 October file correction, and newer incoming corrections are linked by the separate current feature/review reports. No lost runtime change inferred.

## 16 case inventory

- F1/F2: supported current lane/source metadata inconsistency. Full native Ubuntu Developer now passes all required cases, so the review's prediction of an unexercised first Ubuntu failure is obsolete. Reconcile duplicated method-lane declarations and Windows-only descriptive text to the actual supported lanes; do not weaken minima or omit cases to resolve wording.
- F3: supported current inventory provenance drift: some source notes still describe Python execution after native migration. An additive migration note is appropriate; frozen receipts remain historical.
- F4: requirement links are absent/empty for several added rows. This is optional traceability work under the existing schema, not a demonstrated validator failure or proof that methods did not execute.
- Q1 Planned immutable-config wording predates the user's static-container simplification and writable setup policy. Amend the planned item against the explicit user decision later; do not reintroduce the rejected complex model.
- Existing F1 JSONL and R1 snapshot method bindings are confirmed present. New source-inventory delta changes are reviewed in bundle 24; the old inventory prompt is not relabeled current HEAD.

## Additional physical-byte probe

- Bundle 11 Q2 is REPRODUCED in process-bom-probe.json: a native C# child emits known UTF-8 BOM bytes to raw stderr; current ProcessRunner's StreamReader consumes the BOM, and Encoding.UTF8.GetBytes(capturedText) cannot restore it. This is a verification transport/evidence gap, not proof that production emits a BOM. Retain original stream bytes before strict decoding for physical framing checks, with one focused emitter regression. Claims that reconstructed strings are exact original pipe bytes need qualification until then.
