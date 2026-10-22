# Immutable HC00 input mirrors

These four files preserve the exact original bytes selected by the unchanged
`planning/healthcheck/baseline.json` receipt. They were copied from the retained
pre-amendment snapshot `.artifacts/hcg/inputs-04/source/` after checking each source
against its receipt SHA-256, then checked again after the copy. The retained
snapshot is provenance for this recovery; native checks use these tracked mirrors.
No build, test or qualification result is implied by copying them.

The current canonical `contracts/healthcheck/v1/report.schema.json`,
`conformance/healthcheck/common/fixtures.json` and both directories' `README.md`
include the user-approved optional registration diagnostic amendment. They remain
the current contract, fixture inventory and documentation. The immutable receipt
still selects its original expectations through
the explicit `path-map.json`; `HealthcheckCompatibilityInputs` pins that map's
bytes and requires exactly these four mappings and the original file hashes. There
is no fallback to current files. Every other HC00 wire/schema input is checked at
its original receipt path.

The historical fixture inventory's relative paths retain their original
`conformance/healthcheck/common` meaning; it is an archived inventory rather than
an independently relocatable fixture tree. Original selected wire fixtures remain
at those unchanged canonical paths. Existing API/configuration fixtures, their
manifest hashes, the HC00 receipt and the empty publication history are unchanged.
The local Git attributes preserve the four mirrored files byte for byte.
