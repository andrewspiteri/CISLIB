# Pipes183 actual original/one-leaf result peer

CLEAR for adoption of the reviewed new fixture, with no score credit. Read-only review; no reviewer build, native/test/validator entry, source edit or retry. This supplements source peer `pipes-owned-cts183-source-peer01.md` SHAf2a7358d27225fbcbbccc372c24591b86f0e246f176378dcbe7b5a5f9bac1434.

Actual root runs are under `C:/miscwork/portfolio/cislib/.artifacts/hcg/execution183-pipes-proposal/runs`:

| Actual run | Native console exit | TRX | SHA-256 |
| --- | ---: | --- | --- |
| original01 | 0 | 1passed,0failed/skipped;9.5818ms | 6c07661b8b2cf1863ef817e1d860778c1f4a498a6e40dd6944687e04c1d441aa |
| missing-disposal01 | 1 | 0passed,1failed,0skipped;16.6727ms | f0cd09f8026cab6cda88bfb197cff3d9fd9b8548adae669db673c574f313a936 |

The negative TRX's start/end wall interval is30.5413ms; its recorded duration is16.6727ms. Both runs bind GUID11d5f2f6-f121-8e6d-92e4-c05ac857a42e, exact class `CIS.Tests.UnitTests.Healthcheck.HealthPipeServerResourceDisposalTests`, method/display `DisposingAnInertServerReleasesItsActualOwnedCancellationHandle` and the respective original/negative DLL location.

Root supervisor results are620bytes each: original SHA `51a296cabbe7d4dcd8b9c2b792836b2114858c1a2588716bae109cead466f2e8`; negative `545a02ec7183e1c5fb41e9db49be88f5e5786606e978a9f77706b17c02d09f78`. Actual originalPID36560 and negativePID37548 both have expiredfalse, killAttemptedfalse, reapedtrue, drainedtrue, logCopyTaskCompletedtrue and empty holds. These are supervisor-owned process/log facts, not a separate whole native-kernel or mutation audit. Exact input records retain the90s runsettings,120s outer/15s reap/15s log-drain, zero retries and5000 shutdown environment source. No deadline supplied the negative result.

The actual negative ErrorInfo is the direct MSTest assertion `Assert.IsTrue(observedHandle.IsClosed)`, observedfalse, at fixture44. Its stack then reaches the SAME public row's ExceptionDispatchInfo rethrow92. There is no Aggregate/cleanup/timeout error replacing the assertion. The frozen source's control flow reaches that rethrow only after both real ancillary Stop/Dispose calls were initiated and their actual tasks joined, no unexpected source/timer activity was recorded, and same actual CTS-owner cleanup completed and closed the observed handle. Thus this is the intended public-disposal resource assertion after genuine fixture cleanup, not an early assertion masking a cleanup hold. No direct WaitHandle/SafeWaitHandle disposal was added.

Physical read-only comparisons independently observed143files in each prepared host tree with identical paths/content except `CIS.Core.dll` and `.pdb`. Both fixture DLLs remain `4ab601a01a48b8890414d0cca719351c9a1ef844f5a6198750f698f7961a6156`; fixture source remains `328a83cb11aa85209fce41ed9fe8f99b8af0e0aec29f9b29cc491fdc5b0568b5`. Original pair6c06cf58/7acc5a82 and negative paira543c984/e222ae50 match the exact supervisor allowlist. `root-negative-stage01.json` SHA `b8bddd24bcb57da906fbb07dcf8e4469b1ed1d9677b312856b16c0d104b1a0e9` records staging only after the original actual pass.

All146original shipping source manifest entries were independently checked against physical unchanged originals. The private variant differs only at Server: original809ba1a493d6176e354f84209ea556ae21eb726e626b35eb7cc777956cdba12f→7482fb9d88ef8ae585246ff966b89f8a599a8022ddb68257a63b9b4f1f489c39. Private source text exactly equals original text with UTF-16[3374,3399) `stoppingSource.Dispose();` replaced by`;`; the other145match. Root's strict isolated Core build log reports succeeded/0warnings/0errors. Source-delta receipt SHA `2b614ebd4f2046f4d92effadf28ce38b2e6bbc2acd8f609f1b2787e51dabb29c` matches that observation. No new reviewer Core build or PE/PDB framework was used.

Supervisor source0a63d7af3c19cb8fd8a2c6bc2558d3c60e4888b23f2714326078bbb166ac6494 and DLL581dac2771aabfc4ef5ba4f94f3d35b74a08fea7a572c661c69746dbd8575c06 were read/pinned. It checks exact fixture/Core input hashes before launch and again before result, reports native exit separately, never retries and never infers assertion credit. Native console exit1 here is the expected single failed test, not a claimed crash or a reason to clear another hold.

No false-positive or cleanup concern found for this precise inert resource leaf. This manual original/negative control does not update the union, establish a Stryker disposition, qualify other server paths, or retroactively add this new row to immutable active180's621-case cohort. Future canonical/native cohort evidence remains separate. No classifier policy, exclusions or90% requirement changed.
