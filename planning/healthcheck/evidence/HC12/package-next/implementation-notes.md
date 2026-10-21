# Exact-package application and transport qualification

This verification utility was authored after commit `0bf0c4c8d8c0c6646f591c634e66cfa480348aa6`. This document records the authored scope; it does not claim a native execution result. The coordinator owns dispatcher wiring, formatting, building, candidate packing and actual executions.

The command is `healthcheck-package` in the development-only dependency checker. Its required options are `--language-root`, `--package`, `--dependency-cache`, `--provenance` and `--output`; `--dotnet` is optional. Every path is resolved by the executing operating system. Use a new or empty output directory for each attempt. Windows and Linux runs require their own actual x64 process, SDK `10.0.401`, and runtime `10.0.12`.

The supplied producer provenance JSON has this shape, with paths relative to `--language-root`:

```json
{
  "packageSha256": "actual supplied nupkg SHA-256",
  "coreSha256": "actual lib/net10.0/CIS.Core.dll SHA-256",
  "sourceInputs": [
    { "path": "src/CIS.Core/...cs", "sha256": "actual source SHA-256" }
  ]
}
```

The source input list must include every actual Core C# file outside `bin`/`obj`, Core's project, `global.json`, `Directory.Build.props` and `Directory.Packages.props`. Additional producer pins are permitted and checked. All supplied producer hashes and all original/copied consumer C# and generated build inputs are checked again after execution. The supplied producer pins are validated evidence, not an independent attestation of the producer process or complete transitive toolchain.

The utility copies the real CountryList application, matching probe and HTTP fixture C# sources unchanged into three isolated nonpackable executable projects. It replaces the development source reference with the exact package constraint `[version]`. The probe keeps assembly identity `CisHealthProbe`, required for its existing internal Core friend access. A generated verification-only module initializer reports the actual loaded Core file path, hash, assembly identity and process runtime on stderr; it rejects any different output location or Core bytes. That generated observer is separately hashed and does not enter the shipping package or original source tree.

Restore uses one private local feed seeded with the supplied Core package and the approved dependency package bytes from `--dependency-cache`. NuGet cache, HTTP cache and CLI home are local to the attempt. Generated build files terminate parent discovery and pin the SDK, framework and roll-forward settings. The utility checks restored package/asset identities, a package-only restored graph, private feed/cache paths, the deployed Core asset, runtime framework pins and absence of build tooling in runtime output. It neither uses repository source projects as consumer dependencies nor publishes the candidate.

The authored behavioral scope is:

- Four real CountryList self-test modes: success, failed priming, actual monitor-capacity registration failure, and failed later required startup. Assertions inspect the actual stage observations, retained critical failure, usable data, reporting admission, consecutive failed refresh count, and owner lease disposal facts.
- A real fixture `pipe-live` mode with the production Kestrel middleware and pipe server. HTTP checks include selected health/readiness transitions, authorized/denied details, complete JSON/HEAD length, exactly one capture per authorized representation, and bypassing business middleware. The matching packaged probe checks exits `0` and `1` against actual mutable state; exit `2` uses the same real endpoint after its server has actually exited.
- The real fixture's existing frozen `parity` mode compares the entire HTTP and matching probe snapshot JSON for health, readiness and details.

There are 25 authored result rows, 25 owned child process executions, and 13 HTTP observations on a successful run. These are development qualification observations, not extra MSTest case counts. Each process retains its actual command, PID, timestamps, exit code, timeout/forced-termination status and separately hashed stdout/stderr logs. HTTP response bodies and their hashes are retained too. Failed and timed-out attempts retain their diagnostic output and remain failed.

`results.json` reports only the native lane that executed. This utility does not establish real Heartbeat/IStartable integrations, hostile remote-host or different-user denial, published probe/example distribution, a historical released Healthcheck baseline, full six-family qualification, coverage/mutation gates or release/publication approval. The original application's source-reference label remains in its unmodified self-test JSON; the utility separately proves its staged package consumption through package assets and actual loaded origin.
