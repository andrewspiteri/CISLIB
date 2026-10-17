# Logger row caps and independent outcome matrix

17 October 2026, based on 4d37646d. Closes LOGGER-BINDINGS without changing shipping behavior.

The foundation fixture and reader use input.maxEntryBytes, defaulting to 65536; truncate mode no longer silently chooses a different cap. Two added cases reject an oversized record against a changed row cap and verify actual truncation above 16 KiB under a 32 KiB cap. The first control failed before the reader fix (run 749e62be30224dd58c3173e197bbd9b8: expected InvalidDataException but no exception thrown). Existing corrupted-byte controls now validate their real positive baseline first.

The catalog guard independently pins the seven mode names in order and the exact 98 ordered direct/lazy pair IDs. Three mutation controls cover missing/renamed modes and a missing pair while preserving total row count. Required inventory adds the five native cases.

All 185 selected unit cases and 148 physical integration cases pass on each native lane, zero failures/skips. Raw focused summaries are retained beside this file. These are focused checks; full checkpoint, coverage, mutation, compatibility and release gates are not repeated here. The original roadmap remains paused.