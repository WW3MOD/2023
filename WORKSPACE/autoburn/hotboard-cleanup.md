# Hotboard + Discoveries cleanup (autoburn 2026-05-21)

Branch: `auto/hotboard-cleanup`
Worktree: `WW3MOD-auto-hotboard-cleanup`

## Summary

| File | Before | After | Delta |
|---|---:|---:|---:|
| `WORKSPACE/HOTBOARD.md` | 27 lines | 22 lines (4 wins dropped + 1 trailing blank consumed) | −5 |
| `WORKSPACE/DISCOVERIES.md` | 15 entries, 66 lines | 15 entries, 66 lines | 0 |

HOTBOARD's `Recent Wins (last 5)` header had drifted to 9 entries — pruned to honor the cap. DISCOVERIES had no qualifying drops on this branch (see "Skipped" below).

## Rotations

### HOTBOARD — `hotboard: rotate 4 older Recent Wins (cap is 5, had 9)` (`fcb855b7`)

Dropped, in order of removal:

| Entry | Date | Why dropped |
|---|---|---|
| WGM/Hellfire accuracy + tree gating | 260510 | 6th most recent; experimental package still tracked elsewhere (`WORKSPACE/EXPERIMENTAL_NOTES.md`) |
| Artillery turret stowed while driving | 260509 | 7th — shipped, git log retains it |
| SR rally per-waypoint order types | 260509 | 8th — shipped, git log retains it |
| Crew = real soldiers (5 commits) | undated | 9th, oldest; partly duplicates the "Crew evacuation overhaul (260509)" entry still under Working on |

### DISCOVERIES — no rotations

Cross-checked against this branch's `CLAUDE.md`. None of the DISCOVERIES entries are duplicated in it. (Sibling branch `auto/discoveries-promote` promoted 6 entries to CLAUDE.md and explicitly kept the originals as "full context" — but those changes haven't landed on `auto/hotboard-cleanup`, so dropping the originals now would orphan info that doesn't exist on this branch yet.)

## Skipped (per "conservative — when in doubt, LEAVE IT")

### HOTBOARD — Working on (all 4 kept)

| Entry | Status verified | Why kept |
|---|---|---|
| Automation workflow track (260513, plan only) | `WORKSPACE/automation/README.md` exists; "Awaiting user pass on Phase 0" | In-flight, awaiting user input |
| AI overhaul — Stage A+B/B.4 shipped | Shipped commits in git log (`81493aef`, `73d5be6d`, `b1e94f00`, `beeda8f0`) match the description; entry itself says "Awaiting playtest" + "TECN order-overwriting still reported" | Still in-flight on the open thread |
| Crew evacuation overhaul (260509) | `WORKSPACE/archive/plans/260507_crew_evac_plan.md` exists; "Awaiting playtest" | In-flight |
| Pathfinding friendly-blocker scope (260506) | `WORKSPACE/plans/260506_pathfinding_friendly_blockers.md` exists; "Not started" | Tracked open work |

### HOTBOARD — Recent Wins (5 kept)

Kept the 5 most recent + impactful, in order:

1. DOCUMENT recipe + DOCS/gameplay/ (260512)
2. Screenshot evaluation (260512)
3. Economy refactor (260511, branch `main`)
4. Balance session (260510, branch `balancing`)
5. Heli→heli missile vanish fixed (260510)

### DISCOVERIES — full list, all kept

| Entry | Reason kept |
|---|---|
| 2026-05-18 Handicap unreachable in V5 player row | Active v1.1 deferral; "Decision deferred to v1.1 — needs usage telemetry first" |
| 2026-05-18 Empty MiniYaml values bare trailing colon | Broadly applicable; not in this branch's CLAUDE.md |
| 2026-05-13 CohesionMoveModifier diagnosis | Cohesion work still active (recent commits `34c06f71`, `0277cbde`, `6cdc356c`); diagnosis still load-bearing |
| 2026-05-09 AttackTurreted overrides CanAttack | Broadly applicable engine debugging gotcha; not in this branch's CLAUDE.md |
| 2026-05-09 Activity.IsCanceling is always false in OnLastRun | Same — broadly applicable |
| 2026-05-09 Build cache occasionally skips single-file edits | Same — broadly applicable workaround |
| 2026-05-09 Test mode trace pattern (`Game.LocalTick % 25 == 0`) | Useful AUTOTEST idiom |
| 2026-05-03 GrantConditionOnPrerequisite ownership-change crash | Engine fix shipped, but explains a non-obvious trait architecture (per-player managers); future engine debug aid |
| 2026-03-23 OpenRA maps MUST have `Rules: rules.yaml` | Broadly applicable; not in this branch's CLAUDE.md |
| 2026-03-23 ReloadAmmoPool FullReloadTicks/FullReloadSteps dead code | Live YAML trap; not in this branch's CLAUDE.md |
| 2026-03-23 SupplyProvider ammo-per-cycle scaling | Tuning history; system still active |
| 2026-03-21 IProductionSpeedModifier pattern | Architecture pattern still in use |
| 2026-03-21 Supply Route contestation replaces ProximityContestable | Both systems still in code; design history |
| 2026-03-21 Initial setup | Low value but doesn't meet "duplicated" or "system no longer exists" criteria — conservative leave |
| 2026-03-21 MCP map actor facing | WAngle table in CLAUDE.md covers directions, but this entry adds the specific MCP failure mode + error string — not a strict duplicate |

## Files touched

- `WORKSPACE/HOTBOARD.md` — trimmed Recent Wins from 9 → 5 (commit `fcb855b7`)
- `WORKSPACE/autoburn/hotboard-cleanup.md` — this report

## Verification

- `wc -l WORKSPACE/HOTBOARD.md` → 22 lines (was 27); under the 40-line CLAUDE.md cap
- Recent Wins section now matches its own "(last 5)" header
- No commits with co-author trailers
- DISCOVERIES.md untouched
- Branch is doc-only — no engine / YAML / code changes
