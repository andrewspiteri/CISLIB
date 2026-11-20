# Retained dependency restore correction

**Approved:** Andrew Spiteri answered “Approve the two retained-package mappings.”
The exact two-entry change is now applied. Existing dependency versions and hashes
still require comparison with the prior lock before restore qualification.

The approved Npgsql 10.0.4 package has the expected net10 dependency on
Logging.Abstractions >=10.0.0. Its actual downloaded nuspec matches the intake
metadata. No additional Npgsql dependency was discovered.

A clean task-owned package cache exposed two existing dependencies whose names
are absent from the repository's source mapping:

| Existing package | Version in prior integration lock at `66e291f4` |
| --- | --- |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.6 |
| Microsoft.Extensions.Logging.Configuration | 10.0.6 |

These are already in the baseline integration dependency closure, not newly
selected providers or shipping dependencies. Both restore attempts returned
NU1100 because source mapping excluded nuget.org for these names. Copying the
retained package cache did not resolve NuGet's range/source selection. The copied
NuGet v2 content hashes match the prior lock, and raw archive hashes separately
match their cache sidecars; the two hash kinds are recorded distinctly.

## Exact proposed change

In `implementations/csharp/NuGet.config`, add only these entries to the existing
`nuget.org` packageSource mapping:

```xml
<package pattern="Microsoft.Extensions.Configuration.EnvironmentVariables" />
<package pattern="Microsoft.Extensions.Logging.Configuration" />
```

Keep every source and existing mapping, including the approved Npgsql entry.
Do not disable mapping, add a wildcard/feed, change direct references, select a
new package version or alter Core/CommonTesting shipping dependencies. Re-run
the targeted restore and compare every pre-existing resolved version/content
hash against the prior lock before accepting the updated Npgsql lock. A version
difference is not approved by this correction.

The module scope and shared 24 active hours / 28 attempts / 2,700 restoration
seconds / 6 GiB ceiling / 64 MiB reserve / included 1 GiB Docker allowance stay
unchanged. The initial profile described one new Npgsql mapping, so this exact
two-entry correction is presented for a specific decision. Source work and
independent review for other already-authorized outcomes can continue; dependent
integration/package qualification remains unexecuted until restore succeeds.

The fresh owned PostgreSQL fixture is running privately on loopback with normal
durability; no existing database or registry credentials were used. It remains
inside its recorded ownership and resource bounds while this decision is pending.

CIS accepted the actual approval as decision
`CISLIB-RM02-2026-11-21:retained-mappings2:supporting-scope`.
The first amendment was recorded, but rebinding WORK-003 was rejected because its
executable contract had already started. That task and its original scope are
preserved verbatim. A narrow supporting task owns only this approved two-entry
correction and fresh/locked restore comparison, funded by transferring one hour,
one logical attempt and 300 restoration seconds from WORK-003. Shared limits,
historical costs and product obligations remain unchanged. The intermediate
amendment and rejected updates remain in the event history.

CIS rejected transferring protected per-task restoration seconds. The final distribution transfers only 3,600 active seconds and one attempt; all original per-task restoration reserves remain intact, with WORK-006 assigned zero separate restoration seconds. The shared 2,700-second restoration reserve is unchanged.

A fresh graph evaluation selected four existing transitive packages at 10.0.0
instead of their retained 10.0.6 versions. That result was rejected and preserved
as `WORK-006/fresh-resolved-versions-rejected.lock.json`; no downgraded candidate
was built or tested. The retained baseline lock entries were restored unchanged,
then supplemented only with the actual NuGet-generated Npgsql 10.0.4 entry.
Ordinary locked-mode restore must independently validate this merged graph and
package content before use. No dependency declaration, existing version pin or
source was changed to obtain it. This is a version-preserving lock refresh, not
a claim that unconstrained fresh resolution selected the prior versions.
