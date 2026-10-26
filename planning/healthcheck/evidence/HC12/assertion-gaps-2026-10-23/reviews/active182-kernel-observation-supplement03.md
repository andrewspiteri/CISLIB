# Bounded182 kernel observation

Read-only supplement to the corrected prefix note eb2a232b and two-host supplement05e10a88; neither was modified. No build, entry, subject retry, SSH, source/canonical/policy change or score credit.

Root-retained `C:/miscwork/portfolio/cislib/.artifacts/hcg/active182-kernel-observation01.tar.gz` is4766bytes/SHA `a5e0850b5cc724baab07dc65c6df32ed22ffcad064394f2f5996dc1899301290`. Its readback at `active182-kernel-readback01/active182-kernel-observation01` records observation2026-10-24T12:22:07Z and the five exact per-file byte lengths below. All relative paths preserve the original run/kernel names.

| Original relative process file | Bytes | Actual SHA-256 |
| --- | --- | --- |
| unit-complete-partition143-run01/kernel/actual-process.1779540 | 6723 | b53c430948402bf1c58c1572840092e3c752d69b9ae74f78b416bfea5f4c575b |
| unit-complete-partition143-run01/kernel/actual-process.1781851 | 6723 | 45bb1282f1a31148988ef482dae69204f9fbe7b051b622912e5a05fdcec1555a |
| unit-complete-partition143-run01/kernel/actual-process.1751794 | 10664 | 5bfed46dda633e07d2bd694b771e4dfc1f67565fe4bb77ea77f1a32fcb21370b |
| unit-complete-partition170-run01/kernel/actual-process.1798969 | 6723 | 7dfed082a7b40b21f4b4132a78e189243ba1847b12aff53a93d705a89d2d49f2 |
| unit-complete-partition170-run01/kernel/actual-process.1784319 | 9344 | 7a70cbfd539b71a0dea1d524cf7f4ba508631d858367054d72be0a931f789fd8 |

All five files record successful execve of the task's original `/data/cis-healthcheck-20261021-01a15a72/dotnet/dotnet`. The three host argv records identify their original UnitTests testhost.dll/runtimeconfig/deps, exact matching host diagnostic path, roleclient and parent1751794 for143 or1784319 for170. Their actual environment records contain shutdown timeout5000 and ACTIVE_MUTATION_0 values1279,1294 and1038 respectively. This strengthens the prior host/selector identity; environment presence alone does not prove actual parent wait/callback behavior, or imply that another retained runner-slot environment value was simultaneously selected.

Each host's final line15 is an abnormal kernel terminal record:

| HostPID | Actual raw trace timestamp | Terminal record |
| --- | --- | --- |
| 1779540 | 1792833887.708397 | killed by SIGABRT (core dumped) |
| 1781851 | 1792837686.864226 | killed by SIGABRT (core dumped) |
| 1798969 | 1792842756.242805 | killed by SIGABRT (core dumped) |

There is no normal exit_group(0) or exited0 alternative in these three files. The observed SIGABRT terminals support the original parent crash-abort records; they do not change the earlier source explanation or clear any hold.

The two parent files are finite OPEN prefixes. Their exec argv names the original VSTest console and Runner0 diagnostics, with parentprocessid1751314 and1783794 respectively. Neither contains a terminal exit, wait/reap or SIGCHLD record for the three hosts. All five files show CLONE_THREAD creation, but those thread files, the actual process-creation thread records, signal-source records, complete parent callback/reaping and full inherited lifetime inventory are outside this five-file packet. Host argv parent IDs are not a substitute for those missing kernel creation joins. No whole-role/kernel/capture audit, executable-body hash validation, signal origin, timeout attribution or full lifecycle completion is claimed.

No further generic parent-prefix request is made. Root retains the complete active143/170 originals for strict whole postclosure receiving. Seven observed143 aborted sessions and the separate170 aborted session remain holds; this packet adds terminal facts for only the specified three hosts.
