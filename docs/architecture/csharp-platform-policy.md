---
title: 'C# platform and release boundary'
type: architecture
status: unknown
cis:
  stable_id: cislib.architecture.architecture.csharp.platform.policy
---

# C# platform and release boundary

The implemented target is `net10.0`, with SDK10.0.401 pinned by `global.json`; the selected qualification runtime is10.0.12. Core requires both `Microsoft.NETCore.App` and the approved `Microsoft.AspNetCore.App` shared framework.

Windows11 x64 and Ubuntu24.04 x64, normal managed framework-dependent execution, are the selected initial release lanes. Actual execution is required before advertising qualification. A build on another host, copied report, current self-comparison or historical source checkpoint does not qualify that lane or an additional distribution, architecture or runtime.

Type Manager inventory integration supports conventional untrimmed framework-dependent build output and folder publication for Windows/Linux x64. Single-file, trimming, Native AOT, self-contained deployment and arbitrary custom load contexts are outside that profile. Visual Studio/MSBuild.exe task hosting and Visual Studio `PublishUrl` copying remain unqualified; use the documented native CLI.

Complete platform, coverage, conservative mutation, compatibility and exact-package example verification are open release obligations in [current decisions](../../planning/decisions.md). The simple native workflow in [README](../../README.md) establishes only the checks actually run. Existing SDK/runtime pins and campaign-specific approval ceilings remain applicable when the corresponding release campaign is explicitly performed.
