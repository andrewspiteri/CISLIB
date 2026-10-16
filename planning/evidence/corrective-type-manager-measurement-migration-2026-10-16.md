# Native Type Manager measurement utility

16 October 2026, based on `7607804b`. Corrective development tooling only. Shipping code, public APIs, dependencies, frozen evidence and original roadmap state are unchanged. No Python execution or Claude transfer.

The existing C# checker now provides `type-manager-measure prepare|run` and `type-manager-measure-controls`. Preparation builds the same synthetic catalogs (two libraries of 32 classes, eight of 512), exact candidate Core, original C# host/contracts and pinned approved dependency DLLs. It records templates, generated source, deployment inventories and hashes. Run independently pins the fixture report, requires the same compiled harness/template/runtime policy, checks every deployed file and Core before/after execution, rejects linked deployment entries and alternates eager/deferred and catalog order in fresh native processes. It preserves explicit runtime/architecture checks, tiered compilation/PGO settings, bounded child execution, warmups, sample minima and all original distributions. There is no timing threshold or qualification claim.

Windows and Ubuntu replay the same **26 frozen deployment files**, verifier and exact candidate. Each lane completes **52 fresh measurements: four warmup observations and 48 measured observations**, twelve samples for each of four catalog/mode groups, 1,000 query iterations, 23 distributions per group. Every child reports valid=true on .NET 10.0.12/x64. Seven actual rejection controls per OS cover changed independent report pin, incomplete producer, changed harness, changed template provenance, changed deployed Core, extra deployment file and insufficient samples; all reject before any measured observations and leave complete=false.

Compiled verifier: `8f1920551fd9b3577056fa27adbb2b95d589feec1922e009347dafc5ac8ac1a7`.
Candidate Core: `2d8a7be20a2ae171cce3c2865fb43b91d5854e401f7e633e397bbbd4a397bef5`.
Fixture report: `289692074a006914183f13d3432dd490eead734bd08922528d78b18a27992720`.

Evidence under `.artifacts/corrective/tm-measurement`:
- fixtures/results.json has the independently selected fixture report hash above.
- windows/results.json: `6deb41645a9e121b18d2d35c9183c7b03f1bb77571ed0cd413511c972a2649b6`.
- linux/results.json: `811fee5f1f52835dcf72753477d6a2bb1d963fedb1b0d65b6dafa49afea104a2`.
- windows-controls/results.json: `5995de1c9f57db26e2a2894efd19c584a715b7000626219bfc34bff8727e6296`.
- linux-controls/results.json: `b108d54112f7c412e7fb81d7df4d03b04c296db3767536f932790519a21cee16`.

Complete reports, original logs/commands/PIDs/exit/output hashes, generated fixtures, frozen verifier and audit.json are retained. The OS measurements ran sequentially; Ubuntu used a persistent owned directory and archived its full run/control outputs. Build and formatting checks pass without warnings/errors. These measurements verify the native harness, not general performance or final release qualification.

Retired `eng/measure-type-manager.py`: 17,697 bytes, SHA-256 `e96a8af1b50d3bed585fd97dbdec0cdaf716b1dfe5f5bd0223a6e241f285a889`. Git retains its original source. **No maintained implementation Python remains.** Five historical implementation helpers and 106 Python files in frozen planning/evidence remain unchanged. Shell invocation glue remains; assessment of substantive PowerShell policy is a separate corrective boundary.

Final graph snapshot: schema 2, 10,935 nodes/53,832 edges, zero diagnostics, build `f75fabf8d292c10a002c80694ee4187c7a3cf6fbe9ed7c510be22b57a28dfd3f`, compiler/semantic extractors 6/3. Before retirement, the exact measurement root was fresh/untruncated at depth one, three nodes/three traversals, limit ten, no proposed edges. Navigation is not semantic completeness. A full Developer checkpoint follows this batch; the earlier 6,434-case snapshot predates it. The original logging roadmap remains paused.
