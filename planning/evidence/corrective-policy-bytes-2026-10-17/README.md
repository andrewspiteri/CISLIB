# Type Manager policy byte provenance

17 October 2026, based on 745f2231. Addresses continuation 23-F1 and F2 and the bounded BOM-reader parity question.

Example verification and measurement now use the existing Read(path, out hash) helper for runtime/toolchain policy values and recorded hashes. One owned byte array supplies both. No new configuration abstraction or shipping behavior was added. The shared verifier JSON reader accepts the former reader's optional UTF-8 BOM while hashing all raw bytes, including that BOM. Pinned reads validate the raw bytes before parsing.

Two C# cases check BOM/no-BOM values, exact SHA-256, retained parsed ownership after a later file replacement and rejection of the changed file against the original pin. Both pass without failure/skip on native Windows 9b5c3611fa714607aac0899d05b744f5 and Ubuntu c58f7f9b309a47fc906b63fe5aa94924. Corrected caller code compiled in the focused checker bootstrap. The long-running example, inventory and measurement workflows were not repeated by this focused check. The acceptance evidence map's update date is now explicit.

No new performance, compatibility, coverage or release claim. The roadmap remains paused.
