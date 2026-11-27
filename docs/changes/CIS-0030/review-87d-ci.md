# Review correction: portal CI preparation

Owner: CIS-0030/WORK-199. Review target: `87d18ba6`.

The service job selected portal integration tests after preparing only portable
SDK output. That left the production `.next` output absent and the required
Next response-close correction unprepared on a clean dependency installation.
The version workflow also launches headed Microsoft Edge and changes its native
window bounds, which requires a display on the Ubuntu worker.

## Correction

The existing Ubuntu service job now verifies the runner-provided Edge executable,
Xvfb, `xvfb-run` and `xauth`, records their versions, and builds the production
portal with the already selected npm 11.12.1 CLI. It requires a nonempty
`.next/BUILD_ID`. The direct workspace invocation avoids the local convenience
script's nested bare `npm` selecting a different bundled package manager.
It also exports `CIS_TEST_NODE_EXECUTABLE` from that pinned Node process into
`GITHUB_ENV`: the existing selected HTTP process tests require its absolute path
rather than a PATH fallback.

The actual existing `dotnet test` selection runs under
`xvfb-run --auto-servernum --server-args='-screen 0 1920x1200x24 -nolisten tcp'`.
Its real exit remains the input to the existing strict TRX guard. The browser
stays `msedge`; the version workflow stays headed with `viewport: null` and
actual CDP window-bound checks. No test filter, deadline, runner, dependency,
runtime pin, test framework or browser package is changed. Missing host tools
fail clearly instead of triggering an unbounded replacement download.

The [Ubuntu 24.04 runner inventory](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md)
lists Microsoft Edge and Xvfb. It does not explicitly establish `xauth` for every
image revision, so the workflow checks it on the actual worker. The
[Playwright headed-CI guide](https://playwright.dev/docs/ci#running-headed)
documents wrapping the actual test command with Xvfb. These sources describe
prerequisites; they do not prove a run of this changed workflow.

## Validation and limits

All five final actual bash blocks passed syntax checks. The pinned local
production build and readonly Next startup guard passed, as did the existing
headed Edge native-window/Save/Deploy/rollback regression (1/1, no skips).
The complete affected portal pure selection passed 212/212. Independent source
review found no further required change after the absolute Node export.

The first production build's compiler-cache forecast was too small; the next
phase was refused before execution. Exact newly owned disposable caches were
retired after actual native drainage with retained manifests, and a repeat
production build passed under a conservative 128 MiB forecast. Production
output, all failures/reports and historical costs were retained. The
[verification record](review-87d-verification.md) records this disposition.

No actual Ubuntu Actions run has occurred for this new payload. The corrected
job therefore remains ready for remote execution, with that proof pending;
Windows results are not an Ubuntu CI pass. No new runtime/package installation,
browser download, remote publication, merge or release was performed here.
