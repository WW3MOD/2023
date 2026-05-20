# Discoveries → CLAUDE.md promotion (autoburn 2026-05-20)

Branch: `auto/discoveries-promote`
Worktree: `WW3MOD-auto-discoveries-promote`

## Summary

- **Reviewed:** 15 DISCOVERIES.md entries
- **Promoted:** 6 (across 3 commits)
- **Skipped:** 9 (project-specific, already-fixed, or duplicated)
- **CLAUDE.md growth:** 429 → 446 lines (+17 lines, ~4%)

## Promotions

| # | DISCOVERIES entry | CLAUDE.md section | Commit |
|---|---|---|---|
| 1 | 2026-05-18 Empty MiniYaml values must be a bare trailing colon, not `""` | `YAML Conventions` → new "Empty string values" subsection | `66362045` |
| 2 | 2026-03-23 OpenRA maps MUST have `Rules: rules.yaml` | `YAML Conventions` → new "Maps require Rules" subsection | `66362045` |
| 3 | 2026-03-23 ReloadAmmoPool FullReloadTicks/FullReloadSteps are dead code | `YAML Conventions` → new "ReloadAmmoPool dead fields" subsection | `66362045` |
| 4 | 2026-05-09 Build cache occasionally skips single-file edits; touch + make | `Testing` (above "Building while the game is running") | `48da9442` |
| 5 | 2026-05-09 AttackTurreted overrides CanAttack and short-circuits before base | `Architecture & system reference` → new "OpenRA engine debugging gotchas" subsection | `4ac8f389` |
| 6 | 2026-05-09 Activity.IsCanceling is always false inside OnLastRun | Same subsection as #5 | `4ac8f389` |

Each promoted entry is a short paragraph (1–2 sentences) with a link back to `WORKSPACE/DISCOVERIES.md` for the full original context.

## Skipped

| DISCOVERIES entry | Reason |
|---|---|
| 2026-05-18 Handicap unreachable in V5 player row | Project-specific lobby state (deferred to v1.1) |
| 2026-05-13 CohesionMoveModifier diagnosis (EdgeLine/Approach bugs) | Deep diagnosis of one trait; not a broadly applicable pattern |
| 2026-05-09 Test mode trace pattern (`Game.LocalTick % 25 == 0`) | Niche debugging idiom; better fit for `DOCS/recipes/AUTOTEST.md` |
| 2026-05-03 GrantConditionOnPrerequisite ownership-change crash | Already fixed in engine code; project history |
| 2026-03-23 SupplyProvider ammo-per-cycle scaling | Tuning history; already shipped |
| 2026-03-21 IProductionSpeedModifier pattern | Architecture-level detail; belongs in `DOCS/reference/architecture.md` |
| 2026-03-21 Supply Route contestation replaces ProximityContestable | Design history of one trait; project-specific |
| 2026-03-21 Initial setup | Setup note, no pattern |
| 2026-03-21 MCP map actor facing (WAngle ints) | Already covered by existing WAngle table in CLAUDE.md |

## Verification

- `wc -l CLAUDE.md` → 446 lines (was 429)
- All three commits clean; no co-author trailers
- Spot-checked subsections render correctly: H3 headings, fenced code preserved, backtick spans balanced
- No broken markdown links introduced (all link targets are project-relative paths that already exist)
- DISCOVERIES.md untouched (promotion is a copy with reframing, not a move)

## Files touched

- `CLAUDE.md` (3 commits, +17 lines net)
- `WORKSPACE/autoburn/discoveries-promote.md` (this report)
