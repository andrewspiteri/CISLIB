# Codex handoff — CISLIB Healthcheck parallel pilot

Use this only after the user reviews the proposed Technical Intent and authorizes the intended implementation scope. This package is not authorization to publish, restart the old logging roadmap, create external review payloads, or change unrelated modules.

## Goal

Implement the Healthcheck module from its existing definition using contract-first, conflict-aware task planning. Prove that tests, documentation, and independently owned implementation components can be prepared in parallel, while shared build/contract changes remain coherent. Preserve all required behavior, defaults, and acceptance obligations.

## Read first

Read repository `AGENTS.md`, current `planning/decisions.md`, relevant `planning/logging/plan-amendments.md`, `docs/modules/logging/environment-options.md`, and `docs/architecture/csharp-focused-tests.md`. Then read the included source Healthcheck definition, Technical Intent, Contract Catalog, Task Plan, and acceptance mapping. Read Heartbeat v0.2 only for affected integration/lifetime contracts.

The inspected commit was `8254022298b8f42c2d7f2f01f1df5e418b44c4aa`. Verify the current branch and uncommitted work. A newer implementation, current API, active file owner, or later user amendment takes precedence over assumptions from this planning snapshot. Preserve existing edits and original source/evidence files.

## Execute HC00 first

Reconcile the concrete draft choices, particularly optional ASP.NET integration packaging and the Healthcheck options entry point. Verify actual CoreConfigurationException and other shared type signatures; do not invent duplicate equivalents. Freeze the public and necessary internal seams, JSON schema, examples, defaults, ownership manifest, and source/standard hashes together. Record any genuine unresolved cross-task decision before dispatch; ask only a targeted question when repository/source evidence cannot resolve it.

Create the minimum actual interface/value/option types and nonshipping reference facade needed to compile contract consumers. A throwing service stub is not a shipping implementation. Pre-create only the approved bounded project/reference/fixture changes through the shared-file coordinator. Do not create a project for each task.

After the reviewed baseline is established, derive/dispatch the task assignments using the current authorized execution mechanism. `tasks.json` is draft planning data; inspect CIS's real schema before attempting import. Do not invent a CLI/API or claim an import happened. No new CIS orchestration engine is required to run this pilot.

## Parallel working rules

Each active task has one exclusive write claim. Tests and docs own separate files from production code. Read shared inputs from the pinned baseline; do not independently redefine contracts or copy diverging rules into task prompts. Add new files only inside assigned paths. All solution/project/central package/lockfile/shared build changes go through the coordinator's short transactional queue. The queue applies desired changes to the latest baseline, not whole stale file replacements.

Use separate normal worktrees and isolated test endpoint/output identities. Coordinate any genuinely shared build resources. Do not hold a whole-repository read/write lock for an entire feature or full test run. Do not bypass Type Manager build layout constraints by inventing custom output redirections.

When a shared decision must change, update the canonical contract once and notify the affected task owners with the changed section/hash. Retest the affected combined behavior. The original BRD and historical receipts are not rewritten to conceal drift.

## Verification rules

Write native C# tests against contract-derived expected behavior. They can be authored/compiled before runtime behavior exists, but are not passing integration evidence. Run actual C# implementation tests when their prerequisites exist. Keep one focused validation loop per meaningful edit/coherent batch; use the existing checkpoint/release gates at their proper boundaries. Do not replace native tests with Python, create empty-success reports, weaken gates, or add skip markers merely to turn an incomplete branch green.

Qualify all six required families and claimed platforms. Retain exact candidate/package/contract/runtime identities with reports. Execute documentation examples against matching source/package candidates before calling them verified. Keep future IStartable/real Heartbeat evidence pending when the actual component is unavailable; a mock or example-only loop is not a substitute.

## Simplicity and scope

Keep Healthcheck a sibling module in the existing CIS.Core library. Use one clear state coordinator, ordinary policy classes, and a bounded optional collector runner. No general rules engine, startup framework, DI framework, environment snapshot/lifecycle framework, watchdog, rollback engine, or durable event bus belongs in this task. Preserve current EnvironmentOptions defaults/behavior and existing approved dependency closures.

Do not use long positional constructors, dense multi-statement lines, or many partial files to split one class among agents. Coherent feature-sized tasks are deliberate; do not expand them into hundreds of class/method-level tickets with repeated full validation.

## Report back

Report actual artifacts and evidence, not estimated completion: current contract revision; implemented/verified/integrated task status; changed files outside ownership (if any); shared queue and contract-change events; source acceptance coverage including pending external portions; native commands/outcomes; build/test/integration time; human interventions; and remaining risks. Mark the early proof separately from complete feature acceptance and publication qualification.

No commit, push, release, publication, product approval, or external data transmission is implied by this handoff. Obtain the relevant authorization before those actions.
