# changelog-gen — autoburn run report

**Branch:** `auto/changelog-gen`
**Date:** 2026-05-21

## Approach

`CHANGELOG.md` did not exist — this is the first one. Wrote from scratch, organized by gameplay system rather than chronologically.

The scope is the full divergence from OpenRA `release-20230225` to current HEAD. There is no `release-20230225` git tag in this fork; the pin commit is `038ad57e Update target engine version to release-20230225` (Feb 25, 2023, by penev92, on the upstream side of the fork). That commit was the diff base.

```
git log 038ad57e..HEAD --oneline | wc -l   →  1538 commits
```

## Scope choices

The natural epochs that emerged from skimming early-to-late commits:

1. **Bootstrapping (2023 Q1)** — RA clone, faction conversion America/Russia, error-fix sweeps. Mostly noise; not given dedicated section.
2. **Supply Route foundation (May–Jun 2023)** — core game model change; gets its own section.
3. **Combat traits (Apr–Jul 2023)** — `TargetDamage`, directional armor, suppression seeds, blocking.
4. **Shadow LOS system (Jan 2024)** — replaces engine LOS with precomputed `shadows.bin`; own bullet.
5. **Drones / Cargo / Mines (Jan 2024)** — engineer mines, drone operator jammer, DCVs.
6. **Helicopter rework (Aug 2024 → Mar 2026)** — span of years; consolidated.
7. **The Modern Era (Mar 2026+)** — most of the interesting work happened here. Subdivided into: ambush/garrison/scenario, engagement stances, vehicle crew, ballistic missiles, nuclear weapons, AI overhaul, upstream merge to `release-20250330`.
8. **Combat balance + economy (Apr–May 2026)** — tier values, autotest harness, supply economy revamp.
9. **AUTOTEST harness (May 2026)** — engine-gated Test.Mode; Lua API; batch runner; tournament harness.
10. **Lobby redesign (May 2026, phases 0–12)** — visible-to-player UI overhaul.
11. **Cohesion system (May 2026)** — click-anchored intent classifier + cover-aware bidding.

Within each section, entries are themed — Features / Balance / Bug fixes / Performance / UI/Visuals / Tooling / Dev / Docs — but the document is structured by **system** at the top level so a reader looking for "what changed about garrisons" finds it in one place rather than scattered across 7 thematic sections by date.

Noise filters applied:
- Skipped commits like `1`, `2`, `wip`, `error fix`, `error fixes`, `cleanup`, `Merge branch …`, `todo` unless they belonged to a coherent series.
- Doc-only commits collapsed into the Documentation section.
- Hundreds of bug fixes summarized as "selected" with structural examples.
- Hundreds of internal refactors collapsed into a single "Internal" footer.

## Commit count covered

- Diff base: `038ad57e` (Feb 25, 2023)
- HEAD: `8443991c` at start of session (now `7ba39a62` after the CHANGELOG commit)
- Total: 1538 commits

## Existing-file decision

`CHANGELOG.md` did not exist (only `README.md` at repo root). Created fresh — no extend-vs-rewrite decision needed.

## Files touched

- `CHANGELOG.md` — created, 409 lines
- `WORKSPACE/autoburn/changelog-gen.md` — this report

## Length

CHANGELOG.md is 409 lines — under the 500-line cap. Could be expanded with more bug-fix specifics but the goal was skim-able quality. Each section reads as a "what this mod is and how it got here" overview rather than a release-by-release dump.

## Commits

1. `7ba39a62` — Add CHANGELOG.md covering ~1538 commits since OpenRA release-20230225
2. (next) — autoburn report
