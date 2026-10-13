# Initial Type Manager development baseline

`v1-development` retains an unpublished reference assembly, the consumer source
compiled against it, and that consumer's original compiled DLL/runtime files.
`baseline-hashes.json` preserves their original SHA-256 values. This is an initial
development baseline, not evidence of a previously published Type Manager release.
The reference DLL contains only the Type Manager API; other Core module additions
are allowed by the one-directional API comparison.

Run `eng/check-type-manager-compatibility.ps1 -CoreAssembly <absolute Core DLL>`
from the C# directory after restoring the pinned local tools. The script verifies
baseline hashes, runs ApiCompat with parameter-name and attribute checks, replays
the unchanged compiled consumer, and rebuilds/runs the historical source against
the candidate. The candidate directory must contain its normal runtime dependency
DLLs. Each run records candidate/consumer hashes and real execution output.

The consumer implements the interface and exercises its complete query surface,
factory options, report, generic extension, exact identity lookup and missing-file
diagnostics. Internal-only report/exception constructors are deliberate initial
API choices. This check supplements the runtime behaviour and schema tests; it is
not by itself the full roadmap compatibility gate or a package-consumer test.
