# WP07-S02-T03 Claude review disposition

The user directly approved the exact pending 25-file,179162-byte read-only transfer by replying "go ahead". The payload SHA-256 is d8a8516c8490c2742903ba03ebd9a634515248e2cc7c7d186cb269a39e07cea0; its exact bytes are retained compressed in claude-approved-payload.txt.gz. Claude exited0 in one turn with tools/MCP/delegation disabled and returned CHANGES_REQUIRED. The actual unmodified result is claude-review.json. It did not execute tests.

| Finding | Local change | Focused control |
| --- | --- | --- |
| F1 inherited mutable state | Checked reference values must be sealed and derive directly from System.Object; no inherited state is accepted. | inherited-state / ARCH-OWNERSHIP-SHAPE |
| F2 nested runtime/provider caller fields | Normalize nested runtime/logger names and include the Microsoft provider namespace in the field-signature check. The actual current compiler-generated types retain no forbidden caller fields. | nested-runtime-options, nested-logger-dto, provider-options / ARCH-RUNTIME-CALLER-STATE |
| F3 readonly environment storage | Every non-setup property requires matching static readonly backing storage. Nested container types are rejected. The pinned current implementation has no generated nested cache, so no speculative exception is added. | environment-missing-backing, environment-nested-state / ARCH-ENVIRONMENT-MUTABLE |
| F4 ownership wording | Limit the static claim to declared writable collection types; readonly interfaces/views can alias callers and require executed copying tests. | Current guide wording plus existing owned collection/capture/frozen-result behavior |
| F5 packed DLL binding | Each DLL hash must also equal both inspected and built assembly hashes in the actual report. | core-dll, helper-dll / ARCH-PACKAGE-CONTENT |
| F6 observed-path diagnostic | Reject duplicate observed paths before dictionary construction. | duplicate-observed / ARCH-PACKAGE-FACTS |

All42 focused C# ownership/package cases pass locally with zero failures/skips after these fixes. Final Windows and Ubuntu Developer profiles each pass5473tests/16stages with zero failures/skips on snapshot010e139bfcf2ba08d9de86d85370f1a52b67e3d3. Both independently execute all42 ownership controls. Exact outcomes are recorded in verification.json. The original reviewed33-control profiles are retained under pre-review, distinguished from the final results. This is local closure of Claude's findings; no second external review or follow-up PASS is claimed.

The optional notes about future generated capture shapes, additional generated XML content and buffer constructor provenance remain outside this T03 change. Current real metadata, exact dependency/nuspec checks, documented byte-transfer limits and later T04 source/binary fixture qualification remain authoritative. No shipping source/API/dependency or Type Manager implementation changes are made.
