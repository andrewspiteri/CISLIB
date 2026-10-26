# Disabled-endpoint startup prerequisite, 26 October

The response-isolation fixture now checks the explicitly enabled Unix endpoint
after joining actual startup and before returning its server or creating a
client. Previously, an Options mutation could make the server's validated copy
disabled: startup then legitimately succeeded without an endpoint, and the
fixture consumed its 15-second connection watchdog. The retained Options197
host aborted during the second row. That historical failure remains unchanged.

The eight-line change is in
`HealthPipeCanceledResponseIsolationTests.Startup.cs`, preimage
`2e7b1687472860ccfc9a9add845d1fd7e89fb333788a77ac2481cbc462f17f2b`,
afterimage `38aefa6c73c8e6118b02c7644f2de4841cf218a5fe76627d6340a66194ac2be5`.
It observes the actual `CoreFxPipe_` path once with `File.GetAttributes`.
Only `FileNotFoundException` records absence; all other filesystem failures
propagate through the existing owned cleanup. The assertion is inside the
startup owner's original try/catch, retaining both actual Stop/Dispose drains,
their original bounds and failure handling. It creates no extra connection or
timer. Windows behavior and both original response-isolation rows are preserved.

Independent source review found no startup-publication race for this fresh,
no-client fixture: Unix bind/listen precedes successful Start completion. The
check establishes presence at this prerequisite, while the original real
request/response, cancellation, timer, source and cleanup assertions remain.
Review: [independent source review](source-review.md), SHA
`dd408fbcf12d264b3e79a2b565d417b8e18e7a82a0349c43770ccb96ceaa62a9`.

## Executed verification

Focused canonical Windows run `d954ecbfd4eb418aa963fa3d2364e6a3` passed both
original Boolean rows, zero failures/skips and zero strict build warnings or
errors. Its actual wrapper exit was 0. This run compiled the change but does
not execute the Unix-only prerequisite.

- [Focused native summary](windows-focused-summary.json).
- [Actual native TRX](../../../../../../.artifacts/csharp/focused/d954ecbfd4eb418aa963fa3d2364e6a3/selected.trx).

The isolated Ubuntu-control assembly is a frozen Options197 source derivative
with only this startup helper changed; it does not include the later issue-count
or Pipes child-exit changes. Its strict build passed with zero warnings/errors.
Discovery and the existing C# receiver verified all 1,396 original identities,
all 2,721 source paths, unchanged 146 shipping sources and all four PE/PDB pairs
(270 documents). These are build/discovery/source facts, not executed test
methods. The metadata receipt is
`c822989679777ff5796b0f5b5c73e75aa8a29260830b84203c3753b17b1522ba`,
at `.artifacts/hcg/unit199-fixed-cohort-receiving01/actual01/receipt.json`.

## Ubuntu selected control

The actual control ran once per phase on `192.168.88.161`, under the authorized
`/data` task directory. Both phases closed with wrapper exit 0:

| Phase | Actual returned cases | Host completion | Whole controlled phase |
| --- | --- | --- | ---: |
| Healthy | Both original Boolean rows passed | One normal completion, Executed 2 | 7.387 s |
| Active empty-options initializer | Both rows failed at the new endpoint prerequisite | One normal completion, Executed 2 | 7.774 s |

The active case bodies returned in **0.1058392 s and 0.0032399 s**, with the
actual startup-helper assertion and original test method in each stack. There
was no connection timeout, Aggregate/cancellation wrapper, aborted/canceled
completion, expiry, forced kill or ownership hold. Actual owned hosts exited
0 and their streams drained. These observations close the demonstrated
two-row startup wait; they do not establish full 811-case host completion or
four-worker throughput. Whole-phase times include input verification, native
startup, shutdown and final verification, rather than just test bodies.

- [Healthy receipt](ubuntu-healthy-receipt.json).
- [Active receipt](ubuntu-mutant-receipt.json) and byte-identical
  [actual failed rows](ubuntu-mutant-raw-results.jsonl), SHA
  `ee274d4ae476673bdce3c7be50ab48dd4a406fcfc11313dfcd5600c7a51d0252`.
- [Unchanged maintained classifier receipt](classifier-receipt.json), SHA
  `96ad06732b72ce4ebb051cb8726d99bd6c11568cb3a8895198c4f87413e8af15`.
  The immutable `621dc846...` checker's existing DirectAssertion method was
  invoked once per actual failed row and returned **true for both**. This
  classification-only check does not assess process ownership or qualify a gate.

The last pilot restored Core before its retained snapshot; that restored body
has no mutation helper. The control therefore explicitly uses separately
retained 194 instrumentation. The [source/IL proof](selector-source-il.json)
joins the complete initializer mutation tuple, proves all 146 shipping sources
physically equal and binds the actual empty-constructor branch, eight skipped
setters, declaring types and shared return in Core
`6be36bda073ee4e4b2932eb8da803456e4ca77e4fe44e491874a267c749f56c5`.
The original 197 request's two complete cached property bags and **32,120 ms**
deadline are retained. Source, collector path and the separately proved helper
are explicitly rebound; no status or numeric ID alone establishes identity.
The [control source review](control-source-review.md) records those limits.
Matching instrumented Core/PDB symbols are not claimed.

All **1,294 regular files / 158,582,155 bytes** passed full Windows C# receiving,
without exclusions or extra files. Tar, original-post comparison and exact
archive-member comparison also returned 0. The complete readback remains at
`.artifacts/hcg/unit199-selected494-closed-receiving01/readback01`.

- Archive SHA: `c568cd46a395e3e28883c9aa75a458a3dac54b74471007a97b8bc818b4f6c1c7`.
- Manifest SHA: `2c22b40185ddc9e4305b85247ba09cfbda31b1aaf5ae47f312b906740b2f3963`.
- [Full receiving receipt](full-receiving.json), SHA
  `f4e97ebeac76ea23d82576591526eae20e6508236df4dcde7a3409f715ec8950`.
- [Independent recorded review](independent-review.md), SHA
  `ad5db370c60c0e97ac98ee0a1e003ee0ce4c8d7930560a786bb5537af56d390f`.
  All four actual rows join to the real host protocol; both completion frames
  match the console receptions. All 35 actual host-thread members exited
  naturally with 0; complete kernel and input-closure checks found no hold.

The maintained three-report union remains
**2,111/3,104 = 68.009021%, unqualified**. All five historical Options197 aborts
remain held, and this selected control grants zero campaign credit. The next
execution work is matched finite-cohort calibration for the large requests,
followed by fresh campaign evidence; the shared valid-configuration assertion
gaps also remain. No timeout, abort or old report was converted into kill credit.

## Graph context and limits

Task-start graph build `0edc6a526a1d702cbd964b6aa480421d4a663787cec8145e0fc2b5b0e86f4549`
was fresh, with 24,494 nodes, 119,286 edges, zero errors and 264 extraction
warnings. Exact fixture startup-symbol lookup and its compiler-call
neighbourhood were bounded at four and eight nodes; both queries reported
truncation. Canonical fixture/server source was read directly before making
ownership or runtime claims. C# compiler-call observations use extractor
`cis.csharp.semantic-facts/3`; ownership conflicts keep this a partial graph.
The status API reports no graph schema version or capability matrix, so those
are not inferred. Broader semantic coverage and unexecuted test families are
omitted from the claims above. Final refresh/status records are retained under
`.artifacts/hcg/unit199-final-graph-*`; derived `.cis/local` state is not
hand-edited or committed.
