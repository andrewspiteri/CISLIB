# Unit183 inert registration: bounded independent source review

Result: CLEAR for preparing the separately authorized original and exact publication-leaf diagnostic. This is a source review, not executed evidence or mutation credit. No fixture/Core build, test invocation, field write or canonical edit was performed by this reviewer.

## Exact reviewed inputs

- Proposed fixture `execution183-unit-proposal/HealthCollectorInertRegistrationStopBoundaryTests.proposed.cs`: SHA-256 `4056cc9b52df7479eb467f14c9d401a34f1e576c411f5c79f48a072a681f2691`.
- Original fixture preimage: `2f73ac96811a08bd85693f8128cabd35f24df104b1541552e6ee76f8b9950a9e`; full proposed source and `fixture-source-proposal.patch` (`68e06fa7e70e02485983f188e101e22b0bced7d4abea3a3cdb4739e403379836`) read.
- Author's `HOST_BOUND_SOURCE_REVIEW.md`: `f7dc4aa3c0342c1f380c2ae84ae710ba5c0d76a4dc2a450ae844329674a1b574`.
- Actual Runner: `386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8`; coordinator collection partial: `a4d9049a2a1304a40d487584c708e85749f2e6a9f9305855bb690afa48315240`; coordinator main: `3e3a89690b99a372eb4ca75b0e34ddea98551696794b8a43ca8823c26d1dbe04`; service: `d2db8425685ee203d077eed9b477fc528f994670b667a73d19829d28a8148730`; diagnostic: `6ab0405911c39f7f2e0b5e77c3ce80ac31fd7f946996dfbdbc7dded0cb5deaf7`.

The reviewed source tuple is Runner UTF-16 `[5817,5821)`, publication assignment `true` to `false`. The retained raw result identifies original GUID `1b181a18-5aaf-8d5d-91d2-0fca7f4fe5de` and the original Register rejection plus cleanup timeout. The run-local alias1127 does not establish identity across runs or qualify a kill.

## Ownership and error reasoning

The test still performs valid inert registration, without Start, collection, interval, owner acquisition or timer creation. Only an exact InvalidOperationException originating directly in the pinned Register method selects the new stopping-rejection path. EnsureAdmission and unrelated producer/clock exception sites are excluded. The valid-admission assertion is a constant contract assertion with its own exact captured object.

The read-only reflection obtains the actual single source-owned entry and its actual Disposal Task while the entry remains in `entries`, under the real gate. It neither creates a registration handle nor declares a null handle joined. Missing/wrong metadata, entry count or completion type fails closed.

For this exact leaf, Register adds the entry but retires it before publishing a handle. No retirement subscription is installed. The unchanged inert coordinator withdrawal has no operation to cancel: its retirement cancellation, attachment completion and cancellation-source disposal are dispatched synchronously. The subsequent real runner disposal closes/removes the retained entry and invokes its actual second retirement. The actual entry completion is published by SignalProgress after that retirement's finally and retirement-registration disposal. Actual monitor/service drains and the actual entry Task must all succeed before the fallback can proceed.

The resourceError read is exact string metadata under the actual gate, after that entry join. Any retained non-null text is a runtime hold and is carried in the thrown error's Data. This reads the production record as it exists; it does not recover exception objects already discarded by production, and it is not a general proof about arbitrary asynchronous retirement mutations or arbitrary diagnostic-message getters.

Both actual runner Dispose and Stop are initiated. Their still-pending signals do not become success. Only the exact already-recorded valid-admission assertion and the completed real state/entry drains permit release to six actual weak owners: runner, service, collection state, monitor, clock and collector. A live owner remains an Aggregate hold. The unchanged service owns the runner's stopping subscription; its real disposal is joined. No producer or callback work is excused by a GC deadline.

After the weak observations, actual recorded fixture producer exceptions and acquired/timer counters are checked unconditionally, including when the primary is an assertion. Unexpected rejection sites, unknown metadata, state/entry/runner faults or cancellation, producer activity and surviving ownership remain holds. The new branch does not fabricate task completion or weaken the old normal-registration retirement and Stop assertions.

## Bounded next controls

The original positive and exact whole-public-method negative are essential; neither is claimed here. Preserve raw outcomes before classification. A timeout or incomplete cleanup remains a hold.

Two small private instrumentation controls can exercise the new guards using that same negative Core pair. Keep the production fixture source immutable, preserve and invert every private hook, and label the controls as guard tests rather than production-path evidence:

1. At the selected no-handle branch, retain the actual runner in the external control until the public call returns. The real outer weak-owner check must reject it; then release the control's reference. No manufactured task or production field write is needed.
2. At that branch, invoke the actual fixture clock/collector producer, retain its exact actual thrown object, and catch it only in the private control hook. The real producer records its error and counter. The outer unconditional history/counter guards must return an Aggregate containing that same error and the admission assertion. This deliberately exercises the fixture's guard; it does not claim the SUT performed unexpected collection or timer work.

There is no requirement for another Core build, broad campaign, classifier change or verification framework. Actual results and any later source changes require their own review; this note carries zero qualification/gate credit.
