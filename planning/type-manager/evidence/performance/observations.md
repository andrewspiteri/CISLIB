# Recorded observations

14 October 2026. Medians across 12 measured fresh processes per row; two prior warmup rounds are retained but excluded. Timings and memory are observations on one workstation, not limits or speedup claims. Native lanes ran sequentially.

## Preparation and work placement

| OS | Catalog | Mode | Prepare ms | Bootstrap boundary ms | First use ms | Paired boundary + first use ms | Managed delta KiB |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| windows | small | eager | 48.286 | 48.9166 | 0.042 | 48.957 | 178.0 |
| windows | small | deferred | 49.223 | 0.0004 | 49.891 | 49.891 | 177.2 |
| windows | large | eager | 70.319 | 71.0192 | 0.045 | 71.066 | 1586.7 |
| windows | large | deferred | 68.926 | 0.0004 | 69.598 | 69.599 | 1587.5 |
| linux | small | eager | 224.104 | 225.3723 | 0.041 | 225.447 | 201.6 |
| linux | small | deferred | 212.954 | 0.0004 | 214.101 | 214.101 | 201.6 |
| linux | large | eager | 338.896 | 340.1568 | 0.040 | 340.196 | 1607.1 |
| linux | large | deferred | 308.462 | 0.0004 | 309.418 | 309.419 | 1607.1 |

The paired total is calculated within each process before taking its median. Bootstrap work moves to first use in the deferred control; differences between modes also include ordinary process/JIT/scheduling noise. Memory is the whole-process managed delta after forced collections and query batches, including measurement state.

## Query observations

| OS | Catalog | Mode | First prepared us | First unconfigured-contract match us | Prepared cache ns/call | Lazy cache ns/call | Exact lookup ns/call | Prepared/lazy allocation B/call |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| windows | small | eager | 35.4 | 194.1 | 150.9 | 159.9 | 103.8 | 0.000 / 0.000 |
| windows | small | deferred | 34.2 | 191.2 | 151.9 | 158.5 | 101.8 | 0.000 / 0.000 |
| windows | large | eager | 37.9 | 625.9 | 149.5 | 158.2 | 103.6 | 0.000 / 0.000 |
| windows | large | deferred | 36.3 | 618.2 | 149.1 | 158.6 | 103.1 | 0.000 / 0.000 |
| linux | small | eager | 34.1 | 139.2 | 197.5 | 202.7 | 123.0 | 0.000 / 0.000 |
| linux | small | deferred | 30.6 | 140.2 | 197.9 | 206.0 | 123.5 | 0.000 / 0.000 |
| linux | large | eager | 33.1 | 637.0 | 332.1 | 346.9 | 117.3 | 0.000 / 0.000 |
| linux | large | deferred | 34.8 | 646.2 | 271.8 | 279.3 | 119.6 | 0.000 / 0.000 |

Each per-call observation divides that process's sum of 20 batches by 200,000 calls. Raw batch values, loop controls, min/max process ranges, all allocation scopes and supplemental memory counters remain in the native JSON reports. Zero calling-thread bytes does not claim zero process/native allocation.

## Observed JIT overlap during batch phase

| OS | Catalog / mode | Compiled methods min / median / max | Compilation ms min / median / max |
| --- | --- | --- | --- |
| windows | small-eager | 1 / 1.0 / 1 | 0.077 / 0.125 / 0.219 |
| windows | small-deferred | 1 / 1.0 / 1 | 0.102 / 0.133 / 0.215 |
| windows | large-eager | 1 / 1.0 / 1 | 0.089 / 0.128 / 0.185 |
| windows | large-deferred | 1 / 1.0 / 1 | 0.090 / 0.116 / 0.633 |
| linux | small-eager | 1 / 1.0 / 1 | 0.097 / 0.126 / 0.198 |
| linux | small-deferred | 1 / 1.0 / 1 | 0.082 / 0.112 / 0.203 |
| linux | large-eager | 1 / 1.0 / 1 | 0.089 / 0.137 / 0.191 |
| linux | large-deferred | 1 / 1.0 / 1 | 0.089 / 0.113 / 0.189 |

These are process-wide JIT deltas, including harness code. They do not identify which method compiled or establish that every query ran at its final tier.

## Machine and artifact scope

Intel Core i9-12900H, 14 physical / 20 logical processors, Windows 11 build 26200; the native Linux lane uses Ubuntu under WSL on the same machine. Exact OS/kernel, exposed memory, process architecture, CPU counts, Python, SDK 10.0.401 and .NET 10.0.12 metadata are retained. The same frozen managed deployment is used by both lanes. No package release or application latency claim is made.
