# WORKSPACE/ideas — scout proposal intake

Dated, ranked opportunity lists from read-only scout passes. **Not a queue**: nothing here is scheduled until
the manager promotes it into `WORKSPACE/PIPELINE.md`. Each file carries its own read ref; re-verify a premise
before dispatching from it.

- [`2026-10-07_opportunities-player-experience.md`](2026-10-07_opportunities-player-experience.md) — PLAYER EXPERIENCE (stranger's first hour: onboarding, feedback, readability); 9 ranked proposals; `main @ c276679c`.
- [`2026-10-07_opportunities-bots.md`](2026-10-07_opportunities-bots.md) — BOT/AI competence; 11 ranked proposals (sticky-target slot bug, dry-soldier freeze, retreat dissolved by churn, no own-SR recall, US Apache can't pair, …); `main @ c276679c`.
- [`261007_test-coverage-scout.md`](261007_test-coverage-scout.md) — TEST COVERAGE: NUnit holes in the rebuilt mechanics (#1: no test pins that no weapon lists `NoAutoTarget`, the SR's only protection — agent confirmed zero `ValidTargets` lines name it at e3ad8efe); source-only, no test run; `main @ c276679c`.
- [`2026-10-07_opportunities-player-experience.md`](2026-10-07_opportunities-player-experience.md) — PLAYER EXPERIENCE (stranger's first hour: onboarding, feedback, readability); 9 ranked proposals; 2026-10-07; `main @ c276679c`.
- [`2026-10-07_opportunities-robustness.md`](2026-10-07_opportunities-robustness.md) — ENGINE ROBUSTNESS, second pass (complements `DOCS/design/261007_engine-robustness-scout.md`, cited as [S1 #n]); 9 ranked findings — #1 live desync: `Cloak.ShouldHide` writes `[Sync] remainingTime` from render and host-only bot queries (mines/engineers); plus render pass outside `RunUnsynced`, replay-verify gate, query-purity NUnit guard, cheats-only `ValidateOrder` split; 2026-10-07; `main @ e3ad8efe`.
