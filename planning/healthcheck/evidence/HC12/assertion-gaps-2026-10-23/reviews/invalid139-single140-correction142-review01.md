# Correction to Invalid139 and Single140 source clearances

The earlier independent source-review notes are retained unchanged. Their unqualified fault-propagation clearance was incomplete. Root identified a concrete path, independently confirmed here in exact Invalid139 companion32fbe94e and Single140 companion1b848a87:

1. The real source records and rethrows an unknown OCE. The shipping server converts that source fault into a bounded reporting-error response.
2. The original healthy/recovery-response assertion fails, so the synchronous scenario captures AssertFailed as `primary`.
3. Release eligibility requires `primary is null`; `Require*Phase`, the only reader of `facts.Errors()`, is skipped.
4. Real client/timer/service/terminal cleanup may nevertheless complete normally. Without another hold, the returned observation contains only the original AssertFailed, with an empty graph. Review skips its phase check and rethrows that assertion directly, omitting the actual source cause.

This is a real cause-retention gap; the earlier phase-helper negative controls did not exercise the original body assertion path. It must not be waived or treated as a direct mutation assertion. Root requested a new private142 repair adding recorded facts errors unconditionally to cleanup holds, plus real original-BODY controls proving preservation of BOTH the actual source exception and the original assertion by identity. Old packets, original controls, prior notes and canonical Developer139 inputs remain preserved. No source patch or test was executed by this reviewer.

Bounded analogous source-only scan of current Unit Pipes completion capsules found unconditional ineligible-path additions for Receiving(source.UnexpectedFailures +clock.Failures), Internal(source.Failures), PostCapture(source.Failures +clock.UnexpectedFailures), Canceled(clock phase.Failures), SavedStart(source.Failures), and PendingProbe(source/context Failures). ActiveCancellation and IntegrationStopCancellation run their actual source-return/timer/fault Require* cleanup check whenever actual source Entered succeeds, regardless of an original body assertion; the same sourceOCE-to-response-assert path therefore remains a runtime hold there. This is a bounded branch review, not a global proof that every unknown or concurrent failure is retained.

The actually closed135/137 source-counter campaigns supply separate actual evidence and do not execute these new Integration139/140 capsules. Their raw outcomes are not rewritten by this correction. Their exact current tuple/cohort receiving remains independently audited.
