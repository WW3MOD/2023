# Recon — trees, concealment & cover mechanics (2026-07-28, main @ 33747425)

> ## ⚠ PARTLY REFUTED — read the corrections before using this document
>
> **Reviewed 2026-08-19 at `de78a1ed`.** Two of this recon's central claims are wrong. One of them
> (*"prone grants nothing"*, §Q4) was **wrong at this document's own SHA** — it is not a case of the code
> moving underneath it — and it is the claim that steers a reader away from the mechanic that actually
> answers *"why can't I hide?"*. Corrections are struck in place below rather than deleted, so the
> reasoning error stays legible.
>
> **Ground truth for infantry visibility, stances and concealment is the newer, verified**
> [`260819-infantry-visibility-stances.md`](260819-infantry-visibility-stances.md) **— prefer it over this
> document wherever the two disagree.** Its §10 records the same corrections from the other direction.
>
> | Claim | Verdict |
> |---|---|
> | "Prone grants nothing" / "only additive-modifier user is veterancy" (§Q4) | **FALSE at its own SHA.** Prone is `+1` and automatic; 13 modifiers live, not 1 |
> | "~7 dense tree cells to hide a `Vision: 3` infantryman" (§Q4, summary) | **FALSE — it is 3–5**, fewer still when prone. Curve went superlinear the day this published |
> | Its open question on dead trees / frozen shadow | **SETTLED, the unhelpful way** — shadow is frozen at map load and dead trees still block sight |
> | Trees as actors, densities, locomotor passability, `TerrainModifiesDamage` + `BlocksSight` dormant | **HOLD** — unchanged and still accurate |

Read-only recon (worker 02078863, engine-modernization study). Answers PIPELINE item 20. All claims code-verified with file:line by the recon worker; line refs are as of main @ 33747425.

## Executive summary — do trees conceal today? YES, mechanically — but ~~weakly and~~ graded, not binary.

Trees are **destructible neutral actors** (not terrain tiles) that feed a per-cell **density field**, baked into a precomputed **shadow layer** which **subtracts from projected vision strength** on the line between viewer and cell. A unit is detected only when surviving vision strength on its cell strictly exceeds its `Detectable.Vision` — so tree density between viewer and target genuinely reduces detection. The concealment plumbing exists end-to-end. ~~The catch is **magnitude**: each fully-dense tree cell on the sightline removes only **1** strength point (density 10 ÷ 10); a stock infantryman (`Detectable.Vision: 3`) needs ~7 such cells between it and every viewer to vanish — impractical for a small forest. That weakness (not absence of mechanics) is why Stage-3 ambush scenarios reached for `Detectable: Vision: 9` — which is really a **range** trick (visible only inside the 4c strength-10 ring), not a trees trick.~~

**Corrected 2026-08-19 — the "weakly" verdict does not survive.** The per-cell figure is only the sub-knee
slope: `Map.ForestGroundShadow` (`Map.cs:1102-1121`) doubles the rate above `ForestShadowKneeDensity = 20`,
giving **1→1, 2→2, 3→4, 4→6, 5→8** for uniform density-10 cells. A `Vision: 3` infantryman therefore needs
**3–5 cells depending on the viewer's range band, not ~7** — and **2–4 if he is standing still**, because
stopping grants prone `+1` (`infantry.yaml:294,716-718`). A small copse does conceal a halted squad. What
remains true is the *deliberate* weakness of a thin line: 1 cell is still worth only 1. The `Vision: 9`
range trick is still correctly described as a range trick.

Passability, projectile-blocking, and firing-LOS-through-density are all **live**. A terrain **damage/cover** modifier (`TerrainModifiesDamage`) and a **hard sight-block** trait (`BlocksSight`) both **exist in the engine but are wired to nothing** — dormant seams.

## Q1 — What trees ARE: destructible actors, not tiles

- `^Tree` template `mods/ww3mod/rules/ingame/decoration.yaml:2-47`; concrete `T01`–`T17`, `TC01`–`TC05`, `ICE*`, `UTILPOL*`, `^Box` inherit.
- Burnable: `Inherits@FireMechanic: ^BuildingAffectedByFire` (decoration.yaml:5).
- Blocks projectiles: `BlocksProjectiles` Height 256, `MaxBypass: 4`, `BypassChance: 80` (decoration.yaml:7-11).
- Passable to `tree` PassClass (decoration.yaml:12-14); footprint density via `Building: Density` (e.g. T01 `10` at :102-105, T15 uses 15).
- Destructible: `Health: HP: 2500`, `Armor: Wood`, `Targetable: Trees`, husk on death (`SpawnActorOnDeath`). Default warhead EXCLUDES Trees (`weapons-defaults.yaml:10`); many ballistics opt in via `ValidTargets: … Trees`.
- Vehicles cannot crush trees (`Passable.CrushedByRelationships` defaults None; no locomotor `Crushes` lists tree) — a tank paths around or the tree must be shot/burned down.
- Separate `TerrainType@Tree` paint exists (`tilesets/temperat.yaml:44-47`) — no locomotor gives it a speed → Tree-*painted* cells impassable to all. Tree actors sit on Clear tiles.

## Q2 — Passability

- Infantry locomotors (`FOOT*`): `Passes: field, tree, sandbag, fence` (world.yaml:30,45,62,78) → pass through tree cells at the **underlying tile speed** (Clear 90) — **forests slow no one today**.
- All vehicle locomotors: `Passes: field` only → **blocked** by tree actors; cannot crush through.

## Q3 — Vision/LOS: density→shadow attenuation is LIVE; hard sight-block is DORMANT

Live path: `Building.Density` → `Map.DensityLayer` (`Map.cs:990-999`, **stacks** with `+=`) → `SetShadowLayer` accumulates `groundShadow = Σ density/10` (airborne `/5`, height test at `Map.cs:1129-1131`) per cell-pair (`Map.cs:1111-1137`), cached in `shadows.bin` (`Map.cs:252-253,469-491`) → `MapLayers.AddSource` applies `modifiedStrength = strength − shadowModify` floored at 1 (`MapLayers.cs:357-373`) → `ResolvedVisibility` = max surviving strength (`MapLayers.cs:244-256`); `IsVisible(cell, v)` = `> v` (`MapLayers.cs:571-576`).

Dormant: `BlocksSight` trait + `IBlocksSight` fully built (`BlocksSight.cs`), zero YAML users, its only consumer `BlockingActorsBetween` has **no callers**.

## Q4 — ~~Detection: terrain enters ONLY indirectly via the shadow-reduced vision field~~ — **PARTLY REFUTED 2026-08-19**

> **Correction, 2026-08-19 (verified at `de78a1ed`).** The struck sentences below were **wrong when this
> document was written** — not overtaken later. This is the most damaging error in this recon, because it
> points a reader away from the exact mechanic that answers the user's standing question *"why can't I
> hide?"*. Ground truth, with a full derivation, is
> [`260819-infantry-visibility-stances.md`](260819-infantry-visibility-stances.md) §3.2 and §10.
> The first sentence of the heading still holds: terrain proper does enter only through the shadow field.

`Detectable.IsVisibleInner` (`Detectable.cs:93-116`) consults: occupied cells, threshold = `DetectableInfo.Vision` + `IDetectableAddativeModifier`s (floored 1, capped VisionLayers-1), `MapLayers.AnyVisible`, radar/CB-radar. **No terrain** ~~, no stance, no prone input. Only additive-modifier user in the mod is veterancy (defaults.yaml:211-222, rank → +1..+4). Prone grants nothing.~~

**What is actually true:**

- **Prone grants `+1`, and it is the main concealment lever in the game.** `DetectableAddativeModifier@Prone`
  (`rules/ingame/infantry.yaml:716-718`, `RequiresCondition: prone`, `VisionModifier: 1`). **It was present at
  this recon's own SHA** — `git show 33747425:mods/ww3mod/rules/ingame/infantry.yaml` has it at line 675. The
  claim was false on the day it was written.
- **Prone is automatic and keys on being stopped.** `InfantryStates.ProneCondition` is
  `deployed || suppressed > 30 || !moving || critical-damage` (`infantry.yaml:294`). The `!moving` term means
  **infantry are prone whenever they are standing still** — prone is not a command and there is no stance to
  set. That is why the honest answer to *"how do I hide my infantry"* is **"stop them"**, and why it feels like
  there is no lever: there is one, it is just never named.
- **Stance is genuinely absent** — the modifier list keys on `prone`, `dugin`, `moving`, `firinganyweapon`,
  `object-proximity` and rank, and **no stance condition appears anywhere in it.** The struck "no stance"
  clause was right; it was bundled with two wrong ones.
- **"Only additive-modifier user is veterancy" misses nine of thirteen.** Live users: `@Rank_1..4`
  (`defaults.yaml`), `@Ground` (`aircraft.yaml`), `@InCover1..3`, `@Prone`, `@Dugin`, `@Firing`, `@Moving`
  (`infantry.yaml`). Sign convention matters: a **positive** modifier makes a unit **harder** to see, a
  negative one easier — firing is `−2`, prone is `+1`, dug-in a further `+1` at twelve seconds.

**Consequence for anything built on this section:** a stopped rifleman is at threshold 4, not 3, and after
twelve seconds 5 — so the tree-cell arithmetic in the next paragraph is computed against the wrong baseline as
well as the wrong shadow curve. See the struck note below it.

~~Concealment arithmetic: infantry `Vision: 3` needs sightline shadow ≥ 7 to hide (7 fully-dense tree cells); sniper/spec-ops `Vision: 5` need ≥ 5; `Vision: 1` actors essentially unconcealable. Density stacking (overlapping footprints, T15=15) pushes deep-forest cells to 20-30 (+2-3 shadow/cell) — deep forest works better than the per-cell figure suggests; a thin treeline barely dents detection.~~

**Corrected 2026-08-19 — the cell count is ~2× too pessimistic, and it was stale on arrival.** The shadow
response is **no longer linear**: `Map.ForestGroundShadow` (`Map.cs:1102-1121`) is linear only to a knee at
`ForestShadowKneeDensity = 20`, and each further unit of crossed density counts double above it. Its own
reference table for uniform density-10 cells reads **1→1, 2→2, 3→4, 4→6, 5→8, 6→10**. Against the
`^StandardVision` ladder (strength 10 @0-4c, 9 @4-7c, 8 @7-10c, 7 @10-13c) and the hide test
`strength − shadow ≤ Vision`, a **moving** `Vision: 3` infantryman needs **5 cells** at point-blank and **3**
at 10-13 cells' range — i.e. **3–5, not 7.** The engine comment says so directly: *"4 dense cells → 6, enough
to hide stock Vision-3 infantry from a moderate-range viewer."*

**And that is still the pessimistic case, because it ignores prone (above).** A **stopped** rifleman is at
threshold 4, which buys another cell — **4 at point-blank, 2-3 at range** — and after twelve seconds dug-in
takes it to 5. The two errors in this section compounded in the same direction: the doc overstated the trees
needed *and* omitted the stance bonus, so it concluded forests were impractical for concealment when the
shipped numbers make a small copse work for a halted squad.

The change landed the same day this recon was published, so **the ~7 figure was never right in `main`.** The
sign of the qualitative conclusion is unchanged and still holds: **a thin 1-cell treeline barely dents
detection (density 10 → 1) — that weakness is deliberate**, and it is the reason the curve was made
superlinear rather than simply steeper.

## Q5 — Combat interaction

- Projectile blocking LIVE: consumed by `Bullet.cs:309`, `Missile.cs:1029`, `InstantHit.cs:78`, `AreaBeam.cs:236`, `Railgun.cs:154` via `BlocksProjectiles.AnyBlockingActorsBetween` (4-cell proximity bypass).
- Firing-LOS LIVE: `FiringLOS` (`FiringLOS.cs:70-82`) uses the shadow layer at 2-32c (BlocksProjectiles fallback beyond) — tree density can make a spotted target un-shootable from a given cell.
- `TerrainModifiesDamage` (`TerrainModifiesDamage.cs:29-58`): stock `IDamageModifier`, reads the actor's cell **terrain type** — **wired to no unit** (zero YAML matches). No "forest reduces damage" today. Caveat: keys on painted terrain type, NOT tree actors.
- Closest cover precedents: directional/rear armor (`ArmorDirectionPercent`), prone/suppression damage multipliers — geometric/state, not terrain.

## Q6 — Seams for terrain concealment/cover (mapped, not designed)

| # | Seam | Note |
|---|---|---|
| 1 | `MapLayers.AddSource` (MapLayers.cs:357-373) | Highest-leverage: extra attenuation keyed on target-cell terrain drops into the live vision-vs-shadow arithmetic |
| 2 | `Detectable.IsVisibleInner` (Detectable.cs:93-116) | Bump `detectable` when actor stands in cover — local, viewer-independent |
| 3 | `Map.SetShadowLayer` (Map.cs:1111-1137) | Shadow curve retune; PRECOMPUTED → needs `shadows.bin` regen per map |
| 4 | `Building.Density`/`DensityLayer` (Building.cs:141, Map.cs:990-999) | Retune tree density values, or add terrain-type density contribution |
| 5 | Wire `TerrainModifiesDamage` onto units | Cheapest damage-cover win; needs tree-actor-aware variant or Tree-painted terrain |
| 6 | `FiringLOS.cs:70-82` per-weapon threshold | Snipers need clear LOS, MGs shoot through light cover (shadow-los-plan §2) |
| 7 | `BlocksSight` on `^Tree` + one consumer call | Binary hard block if ever wanted; plumbing compiles today |

## Open question (NOT verified by this recon)

Whether `ShadowLayer` updates when a tree actor **dies** (density removed mid-game) or stays baked from load — `shadows.bin` is a load-time cache. If stale-after-death, deforestation-by-fire/artillery does NOT open sightlines. Verify before building anything on dynamic density.

## Incidental gotchas

- `RevealsShroud.cs` actually defines `RevealsMap` (full-map passive revealer, hardcoded strength 10, NO shadow lookup). Graded shadow-attenuated vision is the `Vision` trait (`Vision.cs`). Edit `Vision.cs`, not `RevealsShroud.cs`.
- Airborne shadow (`/5`) < ground shadow (`/10` divisor semantics: airborne accumulates at half the rate… verify divisor direction before tuning) — trees shadow ground sightlines more than high ones; helicopter spotting asymmetry already exists.
- `shadows.bin` regen required for every map (`river-zeta-ww3`, `woodland-warfare-ww3`) after seam #3/#4 changes.
