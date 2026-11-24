I reviewed **[`842f60e8`](https://github.com/andrewspiteri/CISLIB/commit/842f60e8d78c077442f6aa4de3a4b6c289fc31d5)**, which is still the head of `codex/rm08-central-configuration`, against the RM-08 specification and existing Configuration behavior.

**I found three P2 correctness issues that I recommend fixing before merging.** They concern recovery after expired authority, preservation of configuration defaults, and generation-integrity checks during re-registration.

## 1. [P2] Control renewal can bypass configuration reconciliation after an authority gap

**Locations:** [ManagedWorkAdmission.cs — beginning a control attempt](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/src/CIS.Core/Management/Registration/ManagedWorkAdmission.cs#L173-L194) and [ManagementConfigurationReconciler.cs — accepting retained readiness](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/src/CIS.Core/Management/Hosting/ManagementConfigurationReconciler.cs#L85-L96).

The new configuration bridge learns about an expired authority window through admission-change notifications. However, `BeginAttempt` replaces the previous `Ready` state with `Reconciling` **without first checking whether the previous confirmation has expired**. A subsequent successful control response can therefore reopen admission using the previously completed configuration reconciliation.

A concrete sequence is:

1. The application accepts release R1 and receives control authority valid for 15 seconds.
2. A scheduling pause lasts 20 seconds. No admission, status, or health query observes the expired authority during that period.
3. The server’s desired assignment changes to R2.
4. The management heartbeat resumes before the next configuration poll is due, and its control request succeeds.
5. The configuration reconciler sees `gapObserved == false` and retained configuration readiness as true. It performs no authoritative configuration read, and managed admission reopens with R1.

The recurring control path goes directly into `BeginAttempt`, so an intervening observation that detects expiry is not guaranteed. This makes recovery depend on whether another component happened to inspect the gate first. [Control call path](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/src/CIS.Core/Management/Registration/ManagementRegistration.Control.cs#L39-L52).

**Recommended correction:** detect and retain expiry or an untrusted timing gap before replacing the previous control state. Feed that gap into the existing configuration reconciliation owner, and require the new reconciliation epoch to complete before reopening admission.

**Focused regression case:** advance the clock beyond the authority lifetime without calling `TryAdmit`, `CaptureSnapshot`, or status APIs; then return a successful control response while holding the configuration read open. Admission should remain closed until that authoritative read settles.

## 2. [P2] Server composition rejects payloads that existing Configuration accepts

**Primary location:** [ManagementConfigurationComposition.cs — merging an object over a lower value](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/applications/CIS.Management.Service/Configuration/ManagementConfigurationComposition.cs#L66-L78).

When the lower value is `null` and an upper source supplies a partial object, server composition starts with an empty `JsonObject`. Existing Configuration restores the object type’s declared defaults before applying that upper source.

For a nullable `Child` with declared defaults `Left = 1` and `Right = 2`:

| Input | Value |
|---|---|
| Common source | `{"Child":null}` |
| Application source | `{"Child":{"Right":9}}` |
| Existing Configuration result | `{"Child":{"Left":1,"Right":9}}` |
| New server composition | `{"Child":{"Right":9}}` |

The expected behavior already has an explicit test: [`AnUpperPartialObjectAfterALowerNullUsesTheDeclaredChildDefaults`](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/tests/CIS.Core.UnitTests/Configuration/ConfigurationMappingTests.cs#L45-L54).

When `Left` is required in the effective shape, the new composition fails its final completeness check. Release creation consequently returns a structural rejection for a source pair that the existing Configuration runtime accepts. [Final composition validation](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/applications/CIS.Management.Service/Configuration/ManagementConfigurationComposition.cs#L12-L27).

**The same compatibility problem affects collection elements.** Array and dictionary validation forces `complete = true` for their elements, even while validating a partial source. An element such as `{"Timeout":10}` is rejected if another required member should receive its declared default. The existing mapper fills those missing members recursively inside collections. [New collection validation](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/applications/CIS.Management.Service/Configuration/ManagementConfigurationStructure.cs#L116-L127) · [Existing default completion](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/src/CIS.Core/Configuration/Mapping/ConfigurationValuePipeline.cs#L392-L409).

**Recommended correction:** make structural validation and server composition preserve the existing declared-child-default semantics, including objects restored after `null` and object elements in replacement collections. Relaxing the publication check alone is insufficient because final release composition can still reject the result.

**Focused regression cases:** exercise the nullable-child example and partial object elements in both arrays and dictionaries through publication and release creation, then verify that runtime acceptance produces the same effective values.

## 3. [P2] Re-registration loses equal-generation conflict detection for the same cohort

**Location:** [ConfigurationCentralSnapshots.cs — clearing observed identity and provenance](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/src/CIS.Core/Configuration/Refresh/ConfigurationCentralSnapshots.cs#L232-L240).

`RebindSnapshotTargetAsync` preserves `HighestGeneration` but unconditionally clears `ObservedDesired` and `ObservedProvenance`. The subsequent equal-generation conflict check only runs when `ObservedDesired` exists. [Conflict check](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/src/CIS.Core/Configuration/Refresh/ConfigurationCentralSnapshots.cs#L480-L499).

That creates this recovery gap:

1. A running application accepts generation `10`, release R10.
2. It receives a new registration ID for the **same application, environment, schema, and cohort**.
3. A fresh authenticated response supplies generation `10` with release R11—or the same release with different publication references.
4. The generation is not lower than the retained high-water mark, and the previous equality facts have been cleared. The conflicting content can be accepted.

The actual registration-recovery bridge preserves the cohort when constructing the replacement target, so this is a reachable integration path. [Replacement-target construction](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/implementations/csharp/src/CIS.Core/Management/Hosting/ManagementConfigurationReconciler.cs#L66-L69).

Changing the registration ID does not permit the same assignment-map generation to identify different content for the same cohort. This is a missing recovery-integrity check against an inconsistent authenticated response.

**Recommended correction:** preserve the release and source-reference comparison facts across same-cohort re-registration, excluding the old registration ID from that comparison. Discard the old session’s conditional token and snapshot envelope. Continue allowing a genuinely different cohort to resolve a different release at the same map generation.

**Focused regression cases:** re-register into the same cohort and reject both a changed release and changed source references at the existing generation. Keep the existing different-cohort, same-generation acceptance case.

## Verification and review scope

I reviewed the service/backend paths, Configuration acceptance and session handling, C# client/store integration, bootstrap and credential ownership, and the TypeScript client.

- **All 181 checked-in Node runtime tests passed** in this environment. I ran them against the reviewed sources using Node 24.22.3’s TypeScript transformation. The repository’s pinned TypeScript 5.9.3 compilation and type checks remain unverified here.
- **I could not rerun the C# or PostgreSQL suites**, because this environment has no `dotnet` executable. The three findings above are established by source tracing; the defaults finding is also supported by an existing regression test.
- The branch’s [verification record](https://github.com/andrewspiteri/CISLIB/blob/842f60e8d78c077442f6aa4de3a4b6c289fc31d5/docs/changes/CIS-0027/verification.md) documents substantial additional native testing. Those recorded results are separate from the tests I executed during this review.

I did not identify further confirmed defects in the reviewed transaction/audit/replay paths, reference retention and locking, conditional snapshot handling, restart-required staging, or credential cleanup. **My recommendation is to address these three findings and add the focused regression cases before merging.**