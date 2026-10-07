# Ideas — scout proposals

One line per file: angle, date, ref read. These are proposals, not queue items; promote into
`PIPELINE.md` only on a manager/user decision.

- [`2026-10-07_opportunities-player-experience.md`](2026-10-07_opportunities-player-experience.md) — PLAYER EXPERIENCE (stranger's first hour: onboarding, feedback, readability); 9 ranked proposals; 2026-10-07; `main @ c276679c`.
- [`2026-10-07_opportunities-robustness.md`](2026-10-07_opportunities-robustness.md) — ENGINE ROBUSTNESS, second pass (complements `DOCS/design/261007_engine-robustness-scout.md`, cited as [S1 #n]); 9 ranked findings — #1 live desync: `Cloak.ShouldHide` writes `[Sync] remainingTime` from render and host-only bot queries (mines/engineers); plus render pass outside `RunUnsynced`, replay-verify gate, query-purity NUnit guard, cheats-only `ValidateOrder` split; 2026-10-07; `main @ e3ad8efe`.
