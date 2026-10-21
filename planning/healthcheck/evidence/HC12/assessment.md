# HC12 qualification assessment

The current follow-up has actual Windows/Linux focused, exact-package consumer,
ordinary Linux identity, initial compatibility, coverage and mutation-control
evidence. Developer04 passed 7,978 cases. See the
[qualification follow-up](qualification-next/README.md) for exact snapshots,
completed subgates and remaining automatic mutation/release/external obligations.
This does not award release acceptance.

## Earlier transport-checkpoint assessment, retained as historical context

This assessment does not award release acceptance. The coordinator's exact-input native receipts establish which tests actually executed and passed. Authored test source, successful compilation, source inspection and a Developer checkpoint each provide different evidence and do not replace the required release gates.

The immediate transport evidence is scoped to real Windows endpoints and child processes on pinned .NET 10.0.12: Kestrel HTTP requests, protected named pipes, and the matching probe executable. An executed owner-ACL assertion can establish the actual local Windows permission boundary for that environment. The native endpoint factory selects `PIPE_REJECT_REMOTE_CLIENTS`, but that source/flag evidence does not establish an executed remote-host denial test. Remote-host and differing-user/elevation access claims require suitable real identities/hosts and their own receipts; inability to reach SMB alone is not such a test.

Linux remains an unexecuted platform lane. The Unix implementation uses the BCL local Unix-domain socket and `CurrentUserOnly` peer boundary. Its actual pinned-runtime socket directory, permissions, umask interaction, namespace/container reachability, fragmented I/O, deadline behavior and stop settlement still require real Linux execution. An unexecuted conditional Linux test branch does not qualify support.

The transport regression set must establish ordinary malformed/disconnected-client isolation, bounded actual client admission, one total monotonic request/response budget, complete overflow/error framing, valid selected-view exit 0/1, and exit 2 for transport/protocol/report failures. Stop tests must observe actual handler settlement after a canceled caller wait. HTTP evidence must cover reserved-path business isolation, case/trailing-slash rules, HEAD's complete serialization/status decision, authorization/cancellation isolation and one-snapshot status/body coherence. These obligations remain tied to the final combined candidate rather than an earlier isolated build.

Full critical-invariant mutation qualification, required coverage, compatibility/replay, exact-package example/probe execution and both supported release platform lanes remain required where the release profile specifies them. A Developer pass does not waive them. Publication history must remain truthful: no released Healthcheck baseline or historical replay is invented when the publication ledger is empty. Current frozen API/wire fixtures can qualify the initial candidate, while actual published-history preservation starts from real immutable publication records.

The actual Heartbeat scheduler/adapter boundary and future generic startup coordinator are absent from this implementation scope. Fact-binding and explicit application-owned startup examples do not satisfy the external portions of HC-AT-32 through HC-AT-36, HC-AT-38, HC-AT-43, HC-AT-52, HC-AT-54 and HC-AT-59. Those remain separately pending until the corresponding real integration exists and executes. No publication or deployment authorization follows from this assessment.
