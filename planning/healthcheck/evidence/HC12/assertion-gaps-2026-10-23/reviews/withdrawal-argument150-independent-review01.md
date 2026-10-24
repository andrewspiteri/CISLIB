# Withdrawal150 independent bounded source/control review

Reviewer: `/root/resume_unit_runtime88`. Finding: **clear for the reviewed narrow source change and recorded controls**. No build, test/control execution, canonical edit, SSH or policy change was performed by this reviewer. Final physical source/PE/PDB receiving evidence was not yet supplied at this review; this is not an attestation of that later receipt or a campaign/gate outcome.

Reviewed proposal: `.artifacts/hcg/unit-withdrawal-argument150-proposal/HealthCancellationObservationBoundaryTests.proposed.cs`, SHA256 `07beb1b76f480182c6972242f5f097bf01247268df0c962d21a71937ddd5f34d`, over original `8b99bcdb7f25412ee8a736d741d658d87249c35da2b8b196f6c5fde6cd877088`.

Independently reversed the single `source-amendment02.json` replacement (one occurrence) in memory and recovered the complete original LF-normalized file. The other original test method and the entire Finish/clock/callback implementation therefore remain unchanged.

The actual `state.WithdrawCollection(target)` still runs exactly once at the original position after the actual reservation, TryEnter and real synchronous cancellation observer are established. Only ArgumentException from that exact subject call is converted to a local acceptance assertion. The returned task is assigned only from the actual call; the return flag is set only after actual synchronous return. No replacement task, completion, exception, cancellation, preflight diagnostic or source-policy hook is introduced. The added nonnull assertion checks the actual returned task before original assertions use it.

Source `HealthStateCoordinator.Collection.cs:201` withdraws under its gate then Dispatches; its CancelOperation at296 projects the actual observer error through FromException before completing its cancellation path. The predetermined real target and controlled observer make the narrow no-propagation assertion meaningful. This is not a broad no-throw wrapper for arbitrary resource work.

On an ArgumentException throw, `withdrawalCallReturned` remains false. Finish's `actualWorkJoined` therefore remains false even though primary is now an assertion: its existing pending-managed-signal shortcut cannot apply. The real state Complete, callback registration Dispose, independently initiated monitor/service drains, all observed drains and their original watchdogs remain mandatory. Any cleanup error is retained alongside the primary in AggregateException. Unknown non-ArgumentException subject errors enter the unchanged outer primary catch and receive the same strict cleanup; they remain runtime errors. No unknown error is relabeled or allowed by primary equality/type heuristics.

Read the recorded original2 passed TRX identities: `13ccea9e-4dfe-81ac-9271-da6ebfea698b` False and `1b7df328-42be-8ea0-b3de-8645250b53d1` True. Read complete Controls.cs and the actual four variant receipts, each containing both real async rows, completed full-row tasks, returned stacks through unchanged Finish, direct assertion type and unchanged maintained classifier acceptance. Receipt hashes:

- message-negation: `e1d7096d97742af71b726205d657a4bf7c6846096fe87ea5a8e6a4d5683d2172`.
- identifier-negation: `9d42715d83f4f2359bf83857ef56ed769ab49e1bb21c1e55b9474c83b0e1ab0b`.
- projection-loop-or: `e1354388013e82577091c255fb92dfb637d0fffb18cc6ba76512eba2647511e4`.
- projection-offset-subtract: `1c6f3004b8c742be8cb5a4503c2877e16f30773723d79fabd001efc0dfeb9c81`.

These are actual full-fixture manual source controls over copied147 variants, not native Stryker results. The return-flag and shortcut conclusions are explicitly source proofs; they are not claimed as instrumented runtime observations. No broader unknown-error injection was supplied or executed in this bounded review. The original143 eight runtime rows, four abnormal hosts and all other holds remain unchanged, with zero mutation or qualification credit.
