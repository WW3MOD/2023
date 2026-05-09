# Upstream Merge Plan: release-20230225 → release-20250330

## Overview
Merge 2 years of upstream OpenRA changes into WW3MOD's heavily modified fork.

**Upstream:** 1307 commits, 3226 files changed (38.6k ins, 22.9k del)
**Our modifications:** 487 engine files, 278+ with significant changes

## Scope

### What we WANT from upstream
- Bug fixes (gameplay, networking, rendering)
- Performance improvements
- New engine features (docking rework, pathfinder improvements, graphics)
- .NET 8 improvements, dependency updates
- Lua scripting additions
- Map editor improvements

### What we DON'T want
- mods/ra/ and mods/cnc/ content changes (we have our own mod)
- Fluent localization system (massive churn, low value for us)
- D2k-specific changes (irrelevant)

## File Categorization

### Phase 1: New Upstream Files (SAFEST — ~440 files)
Files that didn't exist before. No conflict risk.
- Copy from upstream tag directly into engine/
- Build & verify after each batch

### Phase 2: Upstream-Only Engine Changes (SAFE — 1226 files)
Files upstream changed that WW3MOD hasn't touched.
Apply in sub-phases by directory:

| Priority | Directory | Files | Risk | Notes |
|----------|-----------|-------|------|-------|
| 2a | OpenRA.Platforms.Default/ | 17 | Low | Platform/rendering |
| 2b | glsl/ | 10 | Low | Shaders |
| 2c | OpenRA.Test/ | 17 | Low | Tests |
| 2d | OpenRA.Server/ | 1 | Low | Server |
| 2e | OpenRA.Utility/ | 1 | Low | Utility |
| 2f | OpenRA.Game/ (new files + untouched) | 185 | Medium | Core engine |
| 2g | OpenRA.Mods.Common/UpdateRules/ | 80 | Low | Update rules (may skip) |
| 2h | OpenRA.Mods.Common/Scripting/ | 51 | Low | Lua bindings |
| 2i | OpenRA.Mods.Common/Lint/ | 33 | Low | Linting |
| 2j | OpenRA.Mods.Common/UtilityCommands/ | 38 | Low | Utility |
| 2k | OpenRA.Mods.Common/Installer/ | 16 | Low | Installer |
| 2l | OpenRA.Mods.Common/Traits/ | 355 | Medium | Bulk of changes |
| 2m | OpenRA.Mods.Common/Widgets/ | 155 | Medium | UI |
| 2n | OpenRA.Mods.Common/Activities/ | 31 | Medium | Unit behaviors |
| 2o | OpenRA.Mods.Common/Warheads/ | 10 | Medium | Combat |
| 2p | OpenRA.Mods.Cnc/ | 129 | Low | C&C specific |
| 2q | Remaining | ~47 | Low | Misc |

**Build & verify after each sub-phase.**

### Phase 3: Both-Modified Engine Files (CAREFUL — 294 files)
These need manual merging. Apply by system to keep changes coherent.

| Priority | System | Files | Risk | Notes |
|----------|--------|-------|------|-------|
| 3a | Core types (WAngle, WDist, WPos, WVec, CPos, CVec, MPos) | 6 | High | Foundation types |
| 3b | Actor, Player, World, Game | 4 | High | Core framework |
| 3c | Map system | 4 | High | We rewrote MapLayers |
| 3d | Graphics | 11+2 | High | Rendering pipeline |
| 3e | Input | 2 | Medium | We modified InputHandler |
| 3f | Traits/Interfaces | 138+1 | High | Heaviest area |
| 3g | Activities | 20 | High | Movement, attack |
| 3h | Projectiles | 6 | Medium | Bullet, Missile |
| 3i | Warheads | 5 | Medium | Damage system |
| 3j | Widgets | 28 | Medium | UI |
| 3k | Orders | 6 | Medium | Command system |
| 3l | HitShapes | 5 | Low | Collision |
| 3m | Effects | 4 | Low | Visual effects |
| 3n | Settings, Network, Sound | 4 | Medium | Config |
| 3o | Remaining (Lint, Scripting, etc.) | ~15 | Low | Misc |

**For each file: review upstream diff, identify conflicts with our changes, merge carefully.**

### Phase 4: Non-Engine Files (SELECTIVE)
- mods/common/ content: take selectively (bug fixes, new features)
- mods/ra/, mods/cnc/: skip (we have ww3mod)
- packaging/: review but probably skip
- .github/: skip

### Phase 5: Deleted/Renamed Files (CAREFUL — 209 deleted, 49 renamed)
- Check if we still reference deleted files
- Apply renames if we haven't already renamed them differently

## Process Per Batch
1. Generate upstream diff for the batch
2. Apply changes (checkout from upstream tag, or patch)
3. Build (`./make.ps1 all`)
4. Fix compilation errors
5. Commit with descriptive message
6. Note any behavioral changes in discoveries

## Known Complications
- **Fluent localization**: Upstream added fluent string system. Massive churn across UI files. May need to adopt or carefully exclude.
- **Docking system rework**: Upstream rewrote dock/repair/rearm. We have custom SupplyProvider/QuickRearm. Careful merge needed.
- **Pathfinder changes**: Upstream improved pathfinding. We haven't modified pathfinder much, should be mostly clean.
- **Shroud→MapLayers**: We renamed Shroud. Any upstream shroud changes need manual porting to MapLayers.
- **TakeCover→InfantryStates**: Same — upstream changes to TakeCover need porting to InfantryStates.
- **Crushable→Passable**: Same pattern.

## Estimated Effort
- Phase 1: 1-2 sessions (new files, mechanical)
- Phase 2: 3-5 sessions (bulk apply, build-fix cycles)
- Phase 3: 8-12 sessions (manual merge, most work)
- Phase 4: 1-2 sessions (selective)
- Phase 5: 1 session (cleanup)
- **Total: ~14-22 sessions**

## Decision: What to skip
Consider skipping these upstream changes entirely:
- [ ] Fluent localization (massive churn, we can add later)
- [ ] UpdateRules (migration scripts, not needed for our fork)
- [ ] mods/ra/ and mods/cnc/ content (we have ww3mod)
- [ ] D2k anything
- [ ] packaging/ (we have our own build)
- [ ] .github/ workflows

## How to Apply Upstream-Only Files
For files we haven't touched, we can extract them directly from the upstream tag:
```bash
# For a single file (upstream path → engine/ path):
git show release-20250330:OpenRA.Game/SomeFile.cs > engine/OpenRA.Game/SomeFile.cs

# For a batch:
while read file; do
  git show release-20250330:"$file" > "engine/$file" 2>/dev/null
done < batch_list.txt
```

## How to Merge Both-Modified Files
For each file:
1. Get upstream diff: `git diff release-20230225..release-20250330 -- <file>`
2. Read our current version: `engine/<file>`
3. Manually apply upstream changes that don't conflict with our modifications
4. Build and test
