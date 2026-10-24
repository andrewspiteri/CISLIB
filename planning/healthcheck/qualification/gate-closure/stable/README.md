# Unit execution profile189

This complete Unit/runtime profile uses two workers, `disable-mix-mutants=true`
and the maintained 10,000 ms additional timeout. Every source path, test filter,
coverage mode, no-bail setting and 90% threshold remains maintained.

The existing configuration validator accepts it by removing only the added
no-mixing flag before comparing the complete maintained configuration. No
qualification rule was relaxed. The matching private launcher adds optimized
process tracing with `--seccomp-bpf` and keeps all raw capture, ownership,
restoration and fresh-control requirements.

This is an adaptive allowance, not a hard ten-second limit. It provides startup
margin after the eight-worker/1,000 ms profile produced substantial aborted
requests. Read the [actual investigation](../../../evidence/HC12/unit-startup-investigation-2026-10-24/README.md)
for control results, launch state and remaining holds. A profile file supplies
no mutation credit or release qualification.
