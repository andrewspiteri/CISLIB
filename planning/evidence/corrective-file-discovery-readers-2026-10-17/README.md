# File probe and discovery evidence readers

17 October 2026, based on b32b3f0d. Addresses continuation review 21-FB, 21-FC and 19-F1.

The unsafe-inventory lease reacquisition probe now uses the existing shared fixture runner. It clears DOTNET_SYSTEM_IO_DISABLEFILELOCKING, checks executable/Core hashes, actual child PID and observed platform/runtime, preserves command/report evidence, and checks parent environment/current directory. The three native Ubuntu startup cases failed before this fix when the parent disabled Unix file locking; all three pass with the same poisoned parent after the fix. Raw failing TRX is retained without normalization.

Five discovery corruption controls now require their specific binding, runtime, Core name, Core bytes or initialization-instance failure reason. Runtime corruption still changes both observed and projected values so equality cannot mask the pinned-runtime assertion. Existing case identities and minima are unchanged. The file guide now distinguishes unreadable-member preflight checks from later corrective post-intent deletion-denial coverage.

All 62 selected FileInventoryProcessTests, FileAcquisitionProcessTests and DiscoveryProcessTests pass on Windows (5615a2b0348f4c9a80a662934a26f7d0) and native Ubuntu (cb94edc3a352444baf95f7e212e5f927), with no failures or skips. Ubuntu disabled-locking rerun: 8d2c741827414b34bc615dc1d65f4495, three passed.

Review 21-FA is refuted by source inspection: JsonlInputSelector opens the computed canonical expected member, never the untrusted inventory name. Removing the canonical-name comparison would open the existing canonical member and make the traversal/foreign-name negative control fail. Missing foreign files do not mask that guard. No mutant execution is claimed.

Focused results do not repeat full Developer or release qualification. The roadmap remains paused.
