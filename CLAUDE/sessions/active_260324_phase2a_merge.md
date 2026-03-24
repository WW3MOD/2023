# Session: Upstream Merge — Phases 2-3 Bulk Apply
**Date:** 2026-03-24
**Branch:** upstream-merge-2025
**Working dir:** C:/Users/fredr/Desktop/BACKUP/WW3MOD
**Status:** in-progress

## What Was Done
1. Applied Phase 2a files (Platforms.Default 17, glsl 10, Tests 17) — committed
2. Upgraded LangVersion from 7.3 to 9 in Directory.Build.props — committed
3. Discovered that upstream-only files can't compile without both-modified files (deep deps)
4. Switched to combined approach: 3-way merge via `git merge-file` for both-modified + direct apply for upstream-only
5. **Bulk merge committed:** 1427 files changed, 46630 ins, 15394 del
   - 1178 upstream-only files applied directly
   - 177 both-modified files merged cleanly (3-way merge)
   - 112 both-modified files have conflict markers
   - 48 deleted-upstream files excluded
   - Directory.Build.props resolved (took upstream)
   - combined.frag resolved (took upstream)

## Key Discoveries
- `_merge_upstream_only_engine.txt` contained ~48 files that upstream actually DELETED — these wrote error messages into .cs files
- Phase 2 (upstream-only) cannot be applied in isolation — files depend on both-modified changes
- 3-way `git merge-file` works excellently: 60% clean merge rate
- Renamed files (Shroud→MapLayers, etc.) create new duplicate files that need manual handling

## Remaining: 112 Conflict Files
Files with `<<<<<<< ` markers that need manual resolution.
Categories:
- **Renamed files** (~9): Upstream changed files we renamed. Need to port upstream changes to our renamed versions and delete upstream copies
  - Shroud.cs (ours: MapLayers.cs)
  - TakeCover.cs (ours: InfantryStates.cs)
  - RadarWidget.cs (ours: MiniMapWidget.cs)
  - HiddenUnderFog/Shroud (ours: removed/merged)
  - AffectsShroud.cs, RevealsShroud.cs, CreatesShroud.cs (ours: uses Detectable/MapLayers)
  - RadarPings.cs, ShroudPalette.cs
- **Core engine** (~15): Actor.cs, Map.cs, Player.cs, World.cs, TraitsInterfaces.cs, etc.
- **Traits** (~40): AutoTarget, Mobile, Aircraft, AttackBase, Cargo, etc.
- **Activities** (~12): Fly, Land, Move, Attack, Resupply, etc.
- **Widgets/UI** (~10): CommandBarLogic, ProductionLogic, etc.
- **Other** (~26): Lint, Bot modules, Projectiles, etc.

## Files
- engine/ (all subdirectories)
- _merge_conflicts.txt (list of conflict files)
