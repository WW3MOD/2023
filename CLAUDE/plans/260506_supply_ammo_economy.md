# Supply & Ammo Economy Overhaul (Phases 1–3)

**Date:** 2026-05-06
**Status:** Spec — not yet implemented
**Scope:** v1 release work
**Author:** brainstormed with Claude (opus 4.7)

This document is **self-contained**. A new chat can pick it up cold and execute. It includes project context, current state, target behavior, file references, decisions made on open questions (with rationale), and a phase-by-phase implementation outline. It does **not** prescribe per-step micro-edits — that's for the implementation plan written from this spec.

---

## 1. Project Context (read first if you're new)

**WW3MOD** is a total conversion of OpenRA Red Alert into a modern WW3 RTS. Engine code lives in-repo at `engine/` (modified `release-20230225`), mod content at `mods/ww3mod/`. The mod replaces RA's factory-based production with a **map-edge reinforcement model**: units are "called in" from off-map via a `Supply Route` building, walk/fly to a rally point, and represent a budget allocation rather than manufactured stock. Buildings spawn locally; units spawn at the map edge.

Key relevant systems for this spec:

- **AmmoPool** (`engine/OpenRA.Mods.Common/Traits/AmmoPool.cs`) — per-unit ammo trait. Already supports `SupplyValue` (cost in supply units to refill one ammo unit) and `CreditValue` (refund per missing ammo when sold/evacuated).
- **CargoSupply** (`engine/OpenRA.Mods.Common/Traits/CargoSupply.cs`) — TRUK-only numeric supply pool (separate from passenger Cargo). Passively rearms nearby allied units, can drop a SUPPLYCACHE actor, can deliver to an `AbsorbsSupplyCache` host (Logistics Center).
- **SupplyProvider** (`engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs`) — proximity rearm trait used by Logistics Center and SUPPLYCACHE. Picks the unit with greatest ammo need, gives 1 pip per cycle.
- **AbsorbsSupplyCache** (`engine/OpenRA.Mods.Common/Traits/AbsorbsSupplyCache.cs`) — drains nearby SUPPLYCACHE actors into the LC's pool.
- **CustomSellValue** (`engine/OpenRA.Mods.Common/Traits/CustomSellValue.cs`) — `GetSellValue()` extension. Already deducts missing ammo (per-pool `CreditValue`) and missing supply on a `SupplyProvider`. **Does NOT deduct missing supply on a `CargoSupply` host** — this is the evacuate-refund bug.
- **RotateToEdge** (`engine/OpenRA.Mods.Common/Activities/RotateToEdge.cs`) — handles "evacuate via map edge for refund" activity. Used by Sellable, AmmoPool's evacuate path, and CargoSupply's evacuate path.
- **Resupply** (`engine/OpenRA.Mods.Common/Activities/Resupply.cs`) — the standard dock-and-rearm activity. Used when a `Repairable`/`Rearmable` unit right-clicks a `RepairsUnits`/`RearmsUnits` host.
- **Repairable / RepairsUnits** — vehicles right-click LC → `Resupply` activity → moves close → `unit.docked` external condition granted via `ProximityExternalCondition@UNITDOCKED` (range `2c0`) → repair pulse + rearm if `RearmsUnits` present.
- **Buildable.Description** (`engine/OpenRA.Mods.Common/Traits/Buildable.cs`) — static string field shown in production tooltip. Rendered at `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/ProductionTooltipLogic.cs:138` via `descLabel.Text = buildable.Description.Replace("\\n", "\n");`. No existing centralized auto-generated tooltip pipeline.

**WDist notation:** `1c0` = 1024 (1 cell). `2c512` = 2.5 cells. **WAngle facing** is counter­clockwise: 0=N, 256=W, 512=S, 768=E.

Workflow rules: never push to remote, commit after every response, no co-author trailers, end every non-trivial response with the structured glyph block from `CLAUDE.md`.

---

## 2. Problem Statement (verbatim from user)

> When supply trucks evacuate now I get the full amount (1000) back, even if it is empty. We need to subtract the value of supplies that has been consumed.
>
> While we are at it, I want to fix supplies in general. We have the logistics center that also carries supplies that we need to verify is working too.
>
> Supply trucks should be able to "force move" to a logistics center to offload their remaining supplies to the logistics center, the standard order without the force modifier should be to repair and resupply like all other vehicles do.
>
> Currently supply trucks can just be close to logisitics centers to resupply, but they should have to enter it and dock with it to resupply, like other vehicles.
>
> We need to also go through all units and make sure that the value of their ammo is set and balanced. No ammo should be completely free, even though some things like low caliber bullets should be very cheap.
>
> The unit weapons and the price for the ammo should be defined in the YAML in such a way that we can read it automatically and print that info in the unit description (in the build menu) so that we see the weapon name, the total ammo, the ammo cost per shot and total ammo cost for that weapon as well as the total ammo cost for all weapons (if multiple).
>
> In general I want the description to be generated based on the YAML values more, for now we do it so it works perfectly for weapons, but keep in mind that we might want to implement some centralised code to handle that for other things in the future, like armor, speed, mobility etc.

---

## 3. Decisions Made (opinionated calls)

These were resolved without asking the user further. Each entry: **decision** + **why** + **how to revisit** if it turns out wrong.

### D1. One field per shot — `SupplyValue` and `CreditValue` should be set equal in YAML
**Decision:** Treat `SupplyValue` (refill cost) and `CreditValue` (refund value) as a single per-shot cost. Convention: set them to the same number. Don't unify the fields in code (avoids breaking the engine API), but document the convention and use a helper that returns "ammo cost per shot" prefering `SupplyValue` and falling back / cross-checking against `CreditValue`.

**Why:** The economy is closed-loop. A HIMARS rocket that costs 1500 supply to refill should also be worth 1500 credits when refunded. Today only the 2 HIMARS pools set `CreditValue` at all (both equal to `SupplyValue`); the pattern is already implicit. Standardizing is a doc + balance-pass change, not an engine change.

**Revisit if:** balance feedback shows refund value should differ from refill cost (e.g., to disincentivize buy-fire-evacuate cycles). At that point, decouple in YAML.

### D2. Logistics Center docking = `unit.docked` external condition is required
**Decision:** Change `SupplyProvider.IsValidTarget` to require the target to have `unit.docked` external condition active, rather than the current "any non-moving target within range" check. Lower `SupplyProvider.Range` on LC from `3c0` to `2c0` (matches the existing `ProximityExternalCondition@UNITDOCKED` range so docked = in range).

**Why:** Reuses LC's existing `unit.docked` infrastructure. No new Cargo/Passenger plumbing needed. "Docked" is now semantically explicit, not implicit-via-not-moving. Vehicles already use this exact path (`Repairable` → `Resupply` → moves to LC → docks → resupplied). Trucks just need the same path.

**Revisit if:** the proximity threshold feels wrong in playtest, or if "docked" should require literal entry into the building footprint. (For literal entry, switch to `Cargo`/`Passenger` model, much more code.)

### D3. Truck → LC right-click = move-and-dock; ctrl+click = deliver supply
**Decision:** Invert the current order modifier. Add `Repairable` to TRUK so right-click on LC routes through the standard `Repairable` → `Resupply` activity. Modify `DeliverSupplyOrderTargeter` (`CargoSupply.cs:591`) to require `TargetModifiers.ForceMove` (Ctrl). Also: when truck reaches the LC and is undamaged, the existing `Resupply` activity bails out without rearming because `RearmsUnits` isn't present on LC. We work around that by adding a tiny "wait at LC" activity after `Resupply` for trucks specifically (or by giving the truck a dummy `Rearmable` referencing a sentinel ammo pool — see implementation notes below).

**Why:** Matches user's spec exactly. Force-move on stationary actors is the established mod convention for "non-default action" (e.g., Force-Move = pure movement bypass for SmartMove). Reusing `Repairable` keeps the docking flow consistent with all other vehicles.

**Revisit if:** the dummy-Rearmable approach causes tooltip pollution (see Phase 1 implementation notes for cleaner alternatives).

### D4. Drop-cache deploy stays
**Decision:** Keep TRUK's deploy-drop-as-SUPPLYCACHE behavior (current `DeployOrderTargeter "DropCargoSupply"` in `CargoSupply.cs:547`). It's useful for forward bases where no LC is nearby.

**Why:** Orthogonal to LC delivery. User didn't ask for removal. Keep features that work.

### D5. Phase 2 description model = augment, not replace
**Decision:** Keep static `Buildable.Description` for the human-written intent ("Main battle tank for armored ground warfare."). Add a new interface `IProvideTooltipDescription` that traits implement to contribute a structured stat block, **appended below** the static description in the tooltip. Phase 2 implements the interface for AmmoPool/Armament weapons. Future phases (armor, speed, mobility) implement the same interface from the relevant traits.

**Why:** Three reasons. (1) YAGNI — we don't need to refactor 80+ unit descriptions today. (2) Authors retain creative control over the one-line summary. (3) The interface is the centralization the user asked for; future trait additions get descriptions for free.

**Revisit if:** the auto block grows to dwarf the static description. At that point, consider replacing the static field for units that have full auto coverage.

### D6. Ammo balance scaling = caliber-tier table (not per-weapon hand-tuning)
**Decision:** Phase 3 sets `SupplyValue == CreditValue` from a small lookup table by weapon role/caliber. ~6–8 tiers covering: small-arms bullets (cheap), medium-caliber (moderate), heavy MG/autocannon (significant), tank shells (high), ATGM/AT rockets (very high), guided missiles & artillery rockets (premium). Per-shot tuning happens during playtest.

**Why:** 253 ammo pools → hand-tuning is grindy and error-prone. A tier table makes the cost-grid legible and balanced relative to itself. Hand-tunable later via per-pool overrides.

**Revisit if:** specific weapons feel mispriced in playtest. Easy: just edit the per-pool number.

---

## 4. Phase 1 — Supply Economy Mechanics

**Goal:** Trucks evacuate with correct partial refunds. Trucks must dock at LC to refill. Right-click LC = repair+rearm; Ctrl+click LC = deliver supply. LC's own SupplyProvider verified working.

### 4.1 Files touched

- `engine/OpenRA.Mods.Common/Traits/CustomSellValue.cs` — add `CargoSupply` deduction
- `engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs` — require `unit.docked`, drop the moving-truck branch
- `engine/OpenRA.Mods.Common/Traits/CargoSupply.cs` — flip `DeliverSupplyOrderTargeter` to require ForceMove; remove (or deprecate) the auto-go-to-LC fallback in `AutoRefillIfEmpty` since LC will handle docking via the standard order
- `mods/ww3mod/rules/ingame/structures.yaml` (LOGISTICSCENTER) — `SupplyProvider.Range: 2c0`, possibly add a sentinel `RearmsUnits` block (see 4.4)
- `mods/ww3mod/rules/ingame/vehicles.yaml` (TRUK) — add `Repairable: RepairActors: logisticscenter` and (per 4.4) either a sentinel `Rearmable` or a custom `SupplyClient` trait

### 4.2 Evacuation refund fix (the headline bug)

**Bug:** `CargoSupply.AutoRefillIfEmpty` calls `RotateToEdge(self, true, self.GetSellValue())`. `GetSellValue()` deducts missing ammo (`AmmoPool.CreditValue`) and missing `SupplyProvider` value, but never inspects `CargoSupply`. So an empty truck (cost 1000, supply value 750) refunds 1000.

**Fix:** in `CustomSellValueExts.GetSellValue` (`CustomSellValue.cs:34-49`), add:

```csharp
// Deduct value of missing CargoSupply pool (supply trucks)
var cargoSupply = a.TraitOrDefault<CargoSupply>();
if (cargoSupply != null)
{
    var missingUnits = cargoSupply.Info.MaxSupply - cargoSupply.SupplyCount;
    missingAmmoValue += missingUnits * cargoSupply.Info.CreditValuePerUnit;
}
```

Truck baseline numbers (current YAML): `MaxSupply: 15`, `CreditValuePerUnit: 50` → full supply = 750 credits. Truck base cost = 1000. **Empty truck evacuate refund: 1000 − 750 = 250**. Half-empty: 1000 − 375 = 625. Full: 1000.

Also affects sell (right-click sell on truck) — that's correct behavior, not a regression.

### 4.3 LC docking — require `unit.docked`

**Today:** `SupplyProvider.IsValidTarget` (`SupplyProvider.cs:285-326`) accepts any rearmable in range. For trucks, accepts any stationary truck within 3c0 (the `Mobile.CurrentMovementTypes.HasFlag(Horizontal)` check at line 318-320).

**Change:**
1. Lower LC `SupplyProvider.Range` from `3c0` to `2c0` in `structures.yaml:389`.
2. In `IsValidTarget`, replace both target paths (rearmable AND cargo-supply) with a single docked check: target must have a `unit.docked` `ExternalCondition` active. (The `replenish-vehicles` / `replenish-soldiers` external-condition gate already exists for the rearm path; we tighten by also checking `unit.docked` is granted.)
3. The "must be standing still" check on trucks becomes redundant once docking is required (a truck at the LC's footprint can't keep moving without breaking the dock proximity), so it can be removed.

**Side-effect:** LC will only refill clients that are physically at the LC. Infantry inside garrison ports near an LC will no longer get LC-driven refills (they get truck refills as today via CargoSupply's shelter-passenger handling). This is desired — LC is now strictly a dock-and-fill station.

### 4.4 Standard right-click on LC for trucks

**Problem:** `Resupply` activity bails early when target is undamaged AND no rearm host applies (`Resupply.cs:71-86`). Trucks at full HP would never trigger the activity because their refill path is `CargoSupply`, not `Rearmable`.

**Two options. Pick (B) — was leaning (A) but on second look (B) is cleaner.**

**(A) Sentinel-pool approach.** Add a hidden 1-capacity ammo pool to TRUK named `supply-refill`, with no armament, no pip decoration, and no `ReloadAmmoPool` (so it never auto-reloads). Add `Rearmable: AmmoPools: supply-refill, RearmActors: logisticscenter` to TRUK. Add `RearmsUnits` to LC. The `Resupply` activity then drives the dock-and-wait flow. While `unit.docked` is granted, an `INotifyDockClient.Docked` callback on TRUK calls `CargoSupply.AddSupply(N)` per tick proportional to LC's `RearmDelay`, drawing from LC's `SupplyProvider` pool. Pros: reuses `Resupply` activity end-to-end (target lines, cancel-on-damage, AI integration). Cons: requires a phantom AmmoPool on TRUK, and we have to make sure the sentinel pool never produces stray UI artifacts (no pip, no reload bar, no `WithAmmoPipsDecoration` slot).

**(B) Custom dock-and-refill activity.** New activity `RefillFromHost` similar to `Resupply` but specialized: target an LC, move within `unit.docked` range, then per-tick transfer 1 supply unit from LC's `SupplyProvider` to TRUK's `CargoSupply` until full or canceled. New `Repairable`-like targeter on TRUK ("Restock" order) that resolves to this activity. Damage routing still works via the existing `Repairable` trait — when right-clicked while damaged, queue `Resupply` for repair THEN `RefillFromHost` for supply. Pros: no sentinel pool, no phantom YAML, semantics match what they actually do. Cons: ~80 lines of new activity code (mostly mirroring `Resupply` boilerplate).

**Recommendation: (B).** The sentinel-pool approach in (A) reads cleanly until you implement it, then turns into a maze of "make sure the pip doesn't render / reload doesn't fire / sell value isn't affected" defensive flags. (B) is more upfront code but each line means what it says. Implementer can flip back to (A) if (B) balloons unexpectedly during impl.

Whichever option: TRUK already has the `unit.docked` external-condition slot (declared on `^Vehicle` template, `vehicles.yaml:24`), so no YAML add needed for the slot itself. Cursor on right-click LC differs between damaged (`repair` cursor via `Repairable`) and undamaged (`enter` or new `restock` cursor via the new targeter).

### 4.5 Force-move on LC = deliver supply

In `CargoSupply.cs`, `DeliverSupplyOrderTargeter.CanTargetActor` (`CargoSupply.cs:591-614`):

```csharp
public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
{
    if (!modifiers.HasModifier(TargetModifiers.ForceMove))  // NEW
        return false;
    // ... existing checks
}
```

Also update the cursor when ForceMove modifier active to clearly distinguish the deliver intent (we already have `DeliverCursor: enter` — still appropriate).

### 4.6 LC own SupplyProvider verification

Existing config (`structures.yaml:385-394`) is functionally correct. After Phase 1 changes:

- `TotalSupply: 3000`, `SupplyCreditValue: 3000`, `Range: 2c0` (lowered from 3c0).
- LC absorbs SUPPLYCACHE drops via `AbsorbsSupplyCache` (range `2c512`, `TransferRate: 50`). **Keep range > SupplyProvider range** so caches dropped at the dock can be absorbed even if pushed slightly.
- LC depletes only when feeding docked clients. No regen.
- Trucks deliver supply via Ctrl+click → `DeliverSupply` order → `DropSupplyCache(allUnits)` at LC's location, which merges into existing cache OR LC absorbs it via `AbsorbsSupplyCache` next tick. ✓ Already wired through `existingCache` merge in `CargoSupply.DropSupplyCache` (`CargoSupply.cs:626-639`).

**Verification step (during impl):** Add a minimal NUnit test in `engine/OpenRA.Test/` covering: (a) refund math with partial supply, (b) `IsValidTarget` requires `unit.docked` condition, (c) LC supply pool decrements by `SupplyPerUnit` cost when refilling a truck pip.

### 4.7 Auto-go-to-LC fallback when CargoSupply empty

Currently `CargoSupply.AutoRefillIfEmpty` (`CargoSupply.cs:671-697`) handles three behaviors:
- `Hold` — sit
- `Auto` — drive to nearest LC
- `Evacuate` — RotateToEdge

**Change:** the `Auto` path's `TryQueueMoveToLogisticsCenter` (`CargoSupply.cs:699-718`) currently just `MoveTo(targetCell, 3)`. After Phase 1, we want it to issue the standard "Restock" order so the truck docks properly and waits to be refilled by the LC's SupplyProvider. Re-route through the Repairable path. Implementation note: queue a `Resupply` activity directly with the LC as host, exactly as `Repairable` does on right-click.

---

## 5. Phase 2 — Auto-Generated Weapon Block in Production Tooltip

**Goal:** Production tooltip shows the static `Description` followed by a structured per-weapon stat block: weapon name, total ammo, cost per shot, weapon total cost. Plus a unit grand-total of ammo cost.

### 5.1 Files touched

- `engine/OpenRA.Mods.Common/Traits/IProvideTooltipDescription.cs` — **new** interface
- `engine/OpenRA.Mods.Common/Traits/AmmoPool.cs` — implement interface to emit a weapon line
- `engine/OpenRA.Mods.Common/Widgets/Logic/Ingame/ProductionTooltipLogic.cs` — wire up the auto-block
- (Optional) `engine/OpenRA.Mods.Common/Traits/Armament.cs` — implement interface if AmmoPool is the wrong owner (see 5.3)

### 5.2 Interface design

```csharp
public interface IProvideTooltipDescription : ITraitInfoInterface
{
    // Returns formatted lines (already \n-joined, no trailing newline) or null/empty to skip.
    // priority controls ordering: lower = earlier. Conventional values:
    //   100 — weapons (this phase)
    //   200 — armor / health
    //   300 — speed / mobility
    //   400 — capabilities (cargo capacity, special abilities)
    string ProvideTooltipDescription(ActorInfo ai, Ruleset rules, out int priority);
}
```

Implemented on `TraitInfo` (not the runtime trait) so it's available before actor instantiation in the production menu. This matters because the tooltip renders for all buildable units, including those not yet built.

### 5.3 Where the weapon-block lives

Recommendation: **`AmmoPoolInfo` owns one line per pool, plus a small aggregator owns the grand-total line.**

Why split: `AmmoPoolInfo` already knows its pool's `Ammo` count, `SupplyValue`, and `Armaments` (linked armament names) — perfect for the per-weapon line. But it doesn't know about other pools on the same actor, so it can't write the grand-total. A second small implementer (either a new `AmmoSummaryInfo` trait we add to `^Vehicle`/`^Infantry`/etc., or piggy-backed on `BuildableInfo` directly) iterates every `AmmoPoolInfo` on the actor and emits the total line. Skipped if the actor has 0–1 pools.

Both implementers use `IProvideTooltipDescription`. The `AmmoPoolInfo` line uses priority 100; the aggregator uses 110 so the total prints under all per-weapon lines.

### 5.4 Output format (locked)

Below the static description, blank line, then for each weapon (ordered by `Armament.Name`, primary first):

```
[Weapon Name]
  Ammo: 100 × 5 supply = 500
```

Where:
- `[Weapon Name]` is the weapon's `Tooltip` if defined, else the weapon's YAML key.
- `100` = `AmmoPool.Ammo`
- `5` = `AmmoPool.SupplyValue` (suffix `supply` is fixed copy)
- `500` = product

If multiple armaments share a pool (e.g., burst weapons with linked barrels), list the pool once with all weapon names joined by `+`.

If actor has 2+ pools:

```
Total ammo cost: 750
```

If no `AmmoPool` traits, emit nothing for this section.

**Localization:** wire labels through Fluent (`mods/ww3mod/languages/...`) under keys like `tooltip.weapon-ammo-line` so future translations work. Default English fallbacks defined in C#.

### 5.5 Tooltip widget integration

In `ProductionTooltipLogic.cs:138`, after the static `Description` is set:

```csharp
var staticDesc = buildable.Description?.Replace("\\n", "\n") ?? "";

var lines = new List<(int Priority, string Text)>();
foreach (var provider in actor.TraitInfos<IProvideTooltipDescription>())
{
    var text = provider.ProvideTooltipDescription(actor, mapRules, out var priority);
    if (!string.IsNullOrEmpty(text))
        lines.Add((priority, text));
}

var autoBlock = string.Join("\n", lines.OrderBy(l => l.Priority).Select(l => l.Text));
descLabel.Text = string.IsNullOrEmpty(autoBlock)
    ? staticDesc
    : staticDesc + "\n\n" + autoBlock;
```

Note: `actor.TraitInfos<IProvideTooltipDescription>()` returns all `TraitInfo` instances on the actor that implement the interface. This is the standard idiom (used elsewhere — e.g., `actor.TraitInfos<PowerInfo>()` at `ProductionTooltipLogic.cs:118`).

Tooltip widget already wraps and resizes (`MaxTooltipWidth = 350`) — no widget-side changes needed.

### 5.6 YAML cleanup (light)

Once the auto-block ships, the manual `\n - 7.62mm machine gun\n - …` lines in unit `Description` strings duplicate auto-info. Phase 2 closes with a light pass to **remove the duplicate manual lines** for units where auto-block fully covers them. Static description shrinks to the one-line intent. Skipped for units where the manual bullets cover non-weapon info (cargo capacity, armor — those come in later phases).

This pass is mechanical: ~80 unit definitions across `vehicles*.yaml`, `infantry*.yaml`, `aircraft*.yaml`, `structures*.yaml`. Bulk regex find/replace, eyeball-verify, commit.

---

## 6. Phase 3 — Ammo Value Balance Pass

**Goal:** Every `AmmoPool` in the mod has a non-trivial `SupplyValue` (and matching `CreditValue`) reflecting weapon role/caliber. No more defaulting to 1.

### 6.1 Files touched

`mods/ww3mod/rules/ingame/*.yaml` only. No engine changes. Approximately:
- `infantry.yaml` (~10 pools across template definitions)
- `infantry-america.yaml`, `infantry-russia.yaml` (faction overrides)
- `vehicles.yaml` (~5 pools)
- `vehicles-america.yaml`, `vehicles-russia.yaml` (~30 pools each)
- `aircraft.yaml`, `aircraft-america.yaml`, `aircraft-russia.yaml` (~20 pools)
- `structures-defenses.yaml` (turrets, ~10 pools)

Total: ~253 pools touched.

### 6.2 Tier table (starting values, balance during playtest)

| Tier | Role / Caliber | `SupplyValue` (= `CreditValue`) | Examples |
|------|---|---:|---|
| T0 | Sidearm / pistol | 1 | Pistol, SilencedPPK |
| T1 | Small-arms (SMG, 5.56mm/7.62mm rifle) | 2 | M16, AK74, MP5, conscript rifle |
| T2 | LMG / DMR / 7.62mm sustained | 3 | M249, PKM, Marksman rifle |
| T3 | HMG / 12.7mm-14.5mm | 5 | M2 Browning, KPVT |
| T4 | Autocannon 20–30mm / IFV main gun | 15 | Bradley 25mm, BMP 30mm |
| T5 | Tank main gun (105–125mm) | 80 | Abrams 120mm, T-90 125mm |
| T6 | Anti-tank rocket (disposable) | 25 | RPG-7, AT4, M72 LAW |
| T6 | Anti-tank guided (wire/laser) | 100 | TOW, Konkurs, Kornet |
| T7 | MANPAD / surface-to-air missile | 60 | Stinger, Igla |
| T8 | Air-to-ground missile | 200 | Hellfire, Vikhr |
| T9 | Cruise / artillery rocket / SRBM | 1500 | HIMARS PrSM (already at 1500) |
| T10 | Air-dropped JDAM / heavy bomb | 800 | F-15E payload |
| T11 | Mines (placement) | 25 | minelayer |

Numbers are **starting points**. Phase 3 ships, then we play and tune. The tier structure is the deliverable; the absolute numbers aren't sacred.

### 6.3 Convention

- Set `SupplyValue` and `CreditValue` to the same number.
- **Never set `SupplyValue: 0`.** Even pistols cost 1.
- For pools with `Ammo: 1` (one-shot weapons like RPG, ATGM mounts), the `SupplyValue` IS the per-use cost.
- For pools with high `Ammo` (rifles at 100), per-shot stays cheap (T1=2 → 200 supply for full reload).

### 6.4 Truck / LC capacity cross-check

After tier values land, sanity-check that one full TRUK (`MaxSupply: 15` × `SupplyPerUnit: 50` = 750 effective supply) can refill a sensible number of typical units. Target: one truck refills ~3–5 IFVs from empty, or ~1 MBT, or ~30+ infantry full reloads. If off, tune `SupplyPerUnit` up/down on TRUK rather than re-touching all 253 pools.

Same for LC: `TotalSupply: 3000` should cover ~4 trucks worth of refilling, plus immediate-area resupply. Probably fine; verify in playtest.

### 6.5 Test plan

- Build clean.
- 1v1 on a small map. Buy a TRUK (1000 cost). Don't fire any weapons. Evacuate via Supply Route. Expect refund = 1000 (full pool, full HP).
- Same TRUK, but rearm 5 IFVs to full first (drains ~half the pool). Evacuate. Expect refund ≈ 600–650.
- Empty TRUK (no supplies left). Evacuate. Expect refund = 250.
- Tank with damaged HP and partial ammo, evacuate. Refund = baseHP-ratio × (cost − missing-ammo-credit). Verify formula matches expectation.
- Build a TRUK, right-click LC. Verify truck moves to LC, dock condition activates, supply pool refills 1 unit per cycle (visible bar climbing).
- Same TRUK with supply, ctrl+click LC. Verify cursor changes, truck moves to LC, drops supply (LC pool climbs, truck pool drops to 0).
- Park a truck within 3c0 of LC but >2c0. Verify it does NOT auto-refill (Phase 1 docking rule).
- Production tooltip on a tank: shows static description + auto-block with main gun line and ammo cost.
- Production tooltip on a unit with no ammo (engineer): shows only static description, no empty auto-block.
- Production tooltip on a 2-pool unit (Bradley w/ 25mm + ATGM): shows two weapon lines + grand-total.

---

## 7. Risks & Open Questions Deferred to v1.1+

- **Sentinel-pool approach** (D3) might cause a stray empty ammo pip on the truck. Mitigation: omit `WithAmmoPipsDecoration` for the sentinel pool. If still ugly, migrate to custom `SupplyClient` trait.
- **AI restocking trucks** — supply trucks may not autonomously go to LC to refill in current AI. Phase 1 fixes the player-driven path; AI behavior verified separately. If broken, log to BACKLOG.
- **Phase 2 description for non-weapon traits** (armor, speed, cargo capacity, special abilities) — not in this spec. Same `IProvideTooltipDescription` interface scales there. Owner: future phase.
- **HoldFire trucks** — TRUK has `InitialResupplyBehavior: Evacuate`. Verify no interaction between Hold/Auto/Evacuate behaviors and the new docking flow.
- **Force-move into a fully-stocked LC** — should refuse and show "blocked" cursor. Add to `CanTargetActor`: `if (sp.CurrentSupply >= sp.Info.TotalSupply) return false;` (already there at line 609).

---

## 8. Implementation Order Recommendation

Within Phase 1, do work in this order to keep intermediate states playable:

1. Add `CargoSupply` deduction to `GetSellValue`. Build, test refund math by hand. Commit.
2. Lower LC `SupplyProvider.Range` to `2c0`. Commit.
3. Tighten `SupplyProvider.IsValidTarget` to require `unit.docked` external condition. Commit.
4. Flip `DeliverSupplyOrderTargeter` to require ForceMove. Commit.
5. Add `Repairable: RepairActors: logisticscenter` to TRUK (so damaged trucks repair on right-click). Commit.
6. Implement option (B) — new `RefillFromHost` activity + new `Restock` order targeter on TRUK. Right-click on LC queues repair (if damaged) then refill. Commit.
7. Update `CargoSupply.AutoRefillIfEmpty.Auto` path to issue the new `Restock` order on the nearest LC instead of a raw `MoveTo`. Commit.
8. Add tests in `engine/OpenRA.Test/`. Commit.
9. PLAYTEST. Update RELEASE_V1.md. Move to Phase 2.

Phase 2 / Phase 3 are independent of Phase 1 once Phase 1 is committed.

---

## 9. Definition of Done

**Phase 1:**
- [ ] Empty TRUK evacuate refund < full TRUK refund (math verified)
- [ ] TRUK can't refill within 3c0 of LC unless docked (within 2c0)
- [ ] Right-click TRUK on LC → truck moves and refills, no manual stop
- [ ] Ctrl+click TRUK on LC → truck delivers supply
- [ ] LC's `SupplyProvider` continues to refill all docked clients (vehicles + infantry within range)
- [ ] AI behavior unchanged (or at least no worse)
- [ ] Tests pass

**Phase 2:**
- [ ] Tooltip shows auto-block on at least one infantry, vehicle, aircraft, structure
- [ ] No-weapon units show no auto-block
- [ ] Multi-weapon units show grand-total line
- [ ] Localization keys exist (Fluent files ready for translation)
- [ ] Manual weapon-bullet lines removed from `Description` strings where redundant

**Phase 3:**
- [ ] Every `AmmoPool` in `mods/ww3mod/rules/ingame/*.yaml` has explicit `SupplyValue` set
- [ ] Every `SupplyValue` declaration has matching `CreditValue`
- [ ] Tooltip values look sensible for a sample of units across tiers
- [ ] One full TRUK refills ~3–5 IFVs (cross-check)
