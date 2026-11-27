# Prior manual review lifetime closeout

Observed 26 November 2026. This is an evidence addendum to the historical
[manual-correction verification](r08-manual-corrections-verification.md), whose
handoff section recorded the then-live fixture and pending cleanup. It does not
rewrite that earlier observation or complete the remaining R09 implementation.
WORK-186's canonical closure is a separate controller action.

The finite `rm09c-manual-final-20261126-192324` review instance has expired and
closed. Its actual execution owner is CIS-0030/WORK-186; the control directory's
legacy CIS-0029/WORK-006 location is not a different owner. The lease was 3,600
seconds, with a 4,200,000-ms admission envelope. Expiry was
`2026-11-26T20:24:10.828Z` (21:24 Malta). No lease extension or currently live
review service is implied.

| Retained observation | Result |
| --- | --- |
| Final status | `closed`, stop reason `expired`; visible browser closed; host stopped/exited; stdout, stderr and IPC joined; issuer and its sockets drained. |
| Independent continuity read | HTTP200 after 131,615 ms using the separate read-only fixture identity. It did not rotate the visible user's account. This is one genuine beyond-two-minute observation, not continuous one-hour availability sampling. |
| C# fixture cleanup receipt | `fixtureOwnersDisposed=true`, completed `2026-11-26T20:24:13.9289533Z`. |
| Final runner receipt | Exit0 at `2026-11-26T20:24:16.1541072Z`; `testProcessAndAssemblyCleanupJoined=true`. |
| Retained TRX and report guard | One passed, zero failed/skipped. The guard records total1/passed1/failed0/skipped0 and elapsed3,629.232s. |

The Node status still contains `fixtureCleanupComplete=false`: it was written
before the C# parent disposed its fixture owners. The later C# cleanup and runner
receipts above are the evidence for final parent/assembly cleanup. Their order
must not be replaced by interpreting the earlier status flag as final success.

The manual lease and synthetic credential lifetime are separate from production
session rules. Production still rejects expired credentials, uses the verified
provider deadline and its existing safety margin, and has no automatic refresh
or expired-token exception. Prior short-session observations did not establish a
two-minute production TTL. The earlier Windows native failures and graceful-stop
hang/restoration records remain failed historical observations; this later
successful finite lifetime does not erase them or prove general runtime
reliability. No runtime pin, timeout or trust bypass was used for this closeout.

## Separate unsuccessful preservation attempt

The optional `test.andrew` export is not part of that passing lifetime result.
The explicit reply **Allow this one-time fixture export** was recorded at
`2026-11-26T20:22:40.7657673Z`. Its scope permitted only the isolated synthetic A
credential in memory for this native-data export, with no credential logging,
export or other credential access; it did not extend the original lease.

Both export attempts failed. Their retained sanitized results are respectively
`preserved=false / READ_NOT_AUTHORIZED_OR_AVAILABLE` and
`preserved=false / READ_OR_EXPORT_FAILED`. No snapshot was preserved. These
messages do not establish that no saved data existed, nor do they identify a
specific permission or transport cause. Unsaved browser text was not exported.
The record must not claim that `test.andrew` can be restored from these attempts.

## Exact retained evidence

`manual/` below abbreviates
`.artifacts/management-portal/CIS-0029/WORK-006/rm09c-manual-final-20261126-192324/`;
`186/` abbreviates `.artifacts/management-portal/CIS-0030/WORK-186/`.

| Evidence | SHA-256 |
| --- | --- |
| `manual/status.json` | `592e048c0e49c8f78a441a2b2e896ef12b8538ecb9108b9824317600f3f5007d` |
| `manual/pointer-closed.json` | `89b31b8ef6211c7f8e0fb6e82e715157803f7671bb1dcd665ab759e0f5fe6622` |
| `manual/fixture-cleanup.json` | `d8cec61f82e4ee5ef2ae96778a90a0e488621f9968d546663761e139e782372e` |
| `manual/runner-complete.json` | `a29fd1d8fc2b564cfffd158389da9bff30f93cc13226e702c85a55c73065cd4f` |
| `manual/test-results/manual-review.trx` | `d6eb5223442cc3965c666eae0ad3b7dd2d15651cc6d26f6d44904596e732795d` |
| `186/rm09c-r09-expired186-native-report-guard/native.log` | `c8367b94346bd48a97109258cad44be9881d33d9f4609c89ca9f0ce5e5cec25b` |
| `.cis/local/testing/rm09-completion/r09-fixture-export-user-approval.json` | `13fa39f57aeef65e6b0c6716e79134e6dbb11cbc06bbb21db25ce39dfdb6d29d` |
| `186/rm09c-r09-user-configuration-preserve-approved/native.log` | `0ebb097108609435e876542e2fa6b747e3bcb7d859673ee869886fd0c5fa0345` |
| `186/rm09c-r09-user-preserve-native-route-corrected/native.log` | `67c4cb23dcd48cd93b3988380be36cda6e76d6afe90270f9b3d2b4da3a4afa7f` |

This addendum was prepared by reading retained sanitized records and source
documentation only. It ran no live credential/database/request, native check,
test, CIS transition or task-markdown mutation.
