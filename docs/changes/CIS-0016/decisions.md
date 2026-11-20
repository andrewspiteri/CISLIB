---
title: "CIS-0016 decisions"
type: change-decisions
status: Reviewed
change_id: CIS-0016
authority: human-reviewed
---

# Decisions

Only explicit human commands may resolve, defer, or promote a decision.

| ID | Category | Question | Blocking | Required before | Status | Options | Resolution | Rationale | Evidence | Promoted ADR |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| DEC-001 | operations | W01: permit only newly owned temporary current-user Windows key backing for native TLS, within unchanged shared limits and without certificate-store installation? | true | Windows TLS key import | resolved | Approve W01 exception<br>Keep current filesystem scope | Approve W01 exception | Andrew Spiteri explicitly approved W01 in the chat on19November2026. Permit onlynewtemporarycurrent-usernativekeycontainers withUserKeySet,noPersistKeySet/MachineKeySet/trust-storeinstallation/existingcredentialaccess. Firstprobe2leaves;laterqualification16liveimports;8MiBfixedchargeinsideexisting6GiB;actualdrain/disposal,exactownedmetadatajournal/cleanupverification andboundedcrashrestoration. Originalauthorization/CoreCompletehistory and30hours32attempts/restorationreserves remain unchanged. | User explicitly selected Approve W01 exception in this chat on19November2026. Concrete scope, first2leafprobe,16liveimports,8MiBcountedcharge andcleanup/custody requirements are in docs/changes/CIS-0016/windows-tls-key-backend-decision.md; baselineauthorization/completedCoreauthority remain historically intact.<br>docs/changes/CIS-0016/windows-tls-key-backend-decision.md |  |
