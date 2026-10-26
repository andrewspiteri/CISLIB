# Eight-worker short mutation profiles

User direction on 24 October 2026 requests higher parallelism and more available
memory for the Ubuntu campaigns. Use these profiles for the new Unit/runtime and
Pipes launches: `concurrency=8` per campaign, up to 16 workers across both. The
1,000 ms additional timeout, separate mutants, complete source/test scopes,
coverage modes, no-bail behavior and 90% thresholds remain unchanged.

The measured server has 32 logical CPUs across 16 physical cores and 125 GiB RAM,
with about 121 GiB available. The initial sample showed 87–89% CPU idle and no
swap use while both two-worker campaigns were active. Their process address/data
limits and session/user cgroup CPU and memory limits are unlimited; no .NET heap
limit is configured. RAM can already grow automatically. The 4 GB/3.5 GB campaign
guards govern output storage, and the 2 GB reserve governs free disk space.

The previous runs keep their original two-worker configurations. Stryker fixes
worker count at startup, so higher parallelism requires new immutable inputs and
outputs. Keep the previous runs progressing while preparing the replacements;
retain their actual partial outputs and stop receipts before freeing closed
roots. Do not transfer partial statuses or timeout credit into the new reports.

Eight workers is the initial measured-capacity choice, not a promised fourfold
speedup. Confirm actual worker startup, emitted adaptive budgets, memory use,
CPU use and request throughput after launch. A session timeout still includes
Stryker's baseline-derived estimate; these settings do not impose a hard
15-second ceiling or bypass cleanup and host-validation holds.
