# Explicit UTC and decoded privacy checks

17 October 2026, based on 68ccea9f. Closes DISCOVERY-UTC and PRIVACY-CHECKS. No shipping behavior changes.

Discovery readers require an explicit Z or +00:00 suffix and a parsed zero offset before time comparisons. Two accepted representations and two rejected representations execute independently of the host time zone. The existing wrong-runtime control now changes both observed and projected runtime strings, so equality alone cannot satisfy it.

The configuration validation exclusion scope now walks decoded string leaves and object keys through nested maps/arrays. Escaped plus, non-ASCII and control characters cannot hide forbidden text in a different report field. Event capture restores its independent Activity sentinel check alongside collector/projection sentinels, using the same small recursive helper. Six controls verify these exclusions after valid baselines.

All 96 selected integration cases pass per native lane, zero failures/skips; raw summaries are retained. Ten required reader controls are included, not ten extra child-process scenarios. Focused checks do not repeat the full checkpoint, coverage, mutation, compatibility or release gates. Roadmap remains paused.

CIS navigation before this delta: build b6d9d8f14155b9420fa518c22d629bcffda71a5f0736d7a05b284357a74664e5, fresh class lookup for DiscoveryProcessTests, no diagnostics or truncation. Maintained file applications remain outside configured graph coverage.