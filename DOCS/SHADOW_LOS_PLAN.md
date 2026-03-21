# Phase 2: Shadow Falloff & Firing LOS Threshold

Planned for a future session, after engagement stances (Phase 1) are working.

## Overview

Two connected changes to the pre-computed shadow system:

### 1. Distance-Based Shadow Falloff

Currently, density on the line between source and target accumulates uniformly — a tree 1 cell from the viewer blocks the same as a tree 30 cells away. This should change to simulate peeking around nearby obstacles:

**Proposed falloff curve (distance from viewer to obstacle):**
| Distance | Shadow contribution |
|----------|-------------------|
| 0-1 cells | 0% (no penalty — you can peek around it) |
| 1-2 cells | ~25% of density |
| 2-3 cells | ~60% of density |
| 3+ cells | 100% (full blocking, current behavior) |

**Implementation:** Modify `SetShadowLayer()` in `engine/OpenRA.Game/Map/Map.cs` (line ~898). When accumulating `totalGround += DensityLayer[tile] / 10f`, multiply by a falloff factor based on the tile's distance from `fromUV`.

### 2. Per-Unit Clear Line of Sight (Firing LOS Threshold)

Each unit gets a `ClearSightThreshold` value (byte, 0-255) on its weapon/armament. This is the maximum shadow value from `ShadowLayer[myCell][targetCell]` that still allows firing.

- Low threshold (e.g., 2): needs nearly clear LOS (snipers, ATGMs)
- High threshold (e.g., 15): can fire through light cover (MGs, area weapons)
- IndirectFire weapons: threshold = 255 (ignore shadows entirely)

**When a unit can't fire (shadow > threshold):**
- Target is visible (spotted by ally with better vision)
- But this unit doesn't have clear enough LOS from its current cell
- Engagement stance determines what happens:
  - Hunt/Balanced: move to closest cell where shadow <= threshold
  - Defensive: find covered cell where shadow <= threshold
  - Hold: pick different target or wait

**This replaces `AnyBlockingActorBetween` in targeting checks.** The shadow-informed runtime check still validates against dynamic obstacles (walls built after map save) but uses shadow data as the primary fast check.

### 3. Unify Projectile Bypass Curve

`BlocksProjectiles.AnyBlockingActorsBetween()` currently has its own proximity bypass (4 cells bypass, 4-7 falloff). Unify with the shadow falloff curve so there's one system to tune.

### Key Files
- `engine/OpenRA.Game/Map/Map.cs` — SetShadowLayer() algorithm
- `engine/OpenRA.Mods.Common/Traits/BlocksProjectiles.cs` — AnyBlockingActorsBetween()
- `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs` — target selection LOS checks
- `engine/OpenRA.Mods.Common/Activities/Attack.cs` — TickAttack() movement decisions
- `engine/OpenRA.Game/Traits/Player/MapLayers.cs` — AddSource() shadow application
