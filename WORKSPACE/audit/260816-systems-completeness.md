# Headline gameplay systems — completeness audit

**Repo state:** `main @ 55459146`, clean tree, in sync with `origin/main` (0 commits behind).
**Date:** 2026-08-16. **Method:** read-only static trace. No game launch, no autotest runs.
**Scope:** the nine "Big systems" / economy entries in `WORKSPACE/RELEASE_V1.md` Phase A.

The question asked of every system: is it *wired* (reachable in a normal game, not gated behind
an unsatisfiable prerequisite, a `false` default, a lobby option that ships off, or a host actor
that appears on zero maps), is it *complete* against its own stated design, and what does a
player actually see today.

## Headline result

**No system in this slice is completely inert.** The `ForwardStaging` / `afld`-`hpad` failure mode
— shipped, structurally unreachable, inert for weeks — does not repeat in any of the nine. Every
system traced to a reachable code path attached to actors that exist in shipped rules.

The tracker errs in the **opposite** direction: **Supply Route contestation is LIVE AND COMPLETE**
(control bar, production slowdown, notifications — all three ship and run) while
`RELEASE_V1.md:38` still marks it `[ ]` open. Similar drift on the helicopter line, which
describes a design that was deliberately reversed in YAML on 260509.

The single genuine blocker is not a completeness gap at all — it is a **lockstep desync** in the
cargo eject-rally path that would break any network game or replay.

---

## Verdict table

| System | Verdict |
|---|---|
| Garrison overhaul (Phases 1–6) | LIVE BUT INCOMPLETE |
| Cargo system (Phases 2A–E) | LIVE BUT INCOMPLETE |
| Helicopter crash + crew overhaul | LIVE BUT INCOMPLETE (design reversed, tracker stale) |
| Stance rework (4 phases) | LIVE AND COMPLETE |
| Supply Route contestation | LIVE AND COMPLETE (tracker stale) |
| Three-mode move system | LIVE AND COMPLETE |
| Vehicle crew system | LIVE BUT INCOMPLETE |
| Infantry mid-cell redirect | LIVE BUT INCOMPLETE |
| Supply & ammo economy overhaul | LIVE — 4 of 5 "Verify" points confirmed, 1 defect |

---

# 1. Cargo system (Phases 2A–E) — LIVE BUT INCOMPLETE

The cargo/supply core is one of the more thoroughly finished systems in the repo: reachable,
order-driven, NUnit-covered (16 test files), and matching `DOCS/reference/economy.md` everywhere
checked. `Cargo:` is attached in 17 places across shipped rules; TRUK's
`Buildable.Prerequisites: ~techlevel.low` is satisfiable (`player.yaml:211`); the cargo panel is
real chrome with a per-frame `IsVisible` closure (`CargoPanelLogic.cs:240-246`) that deliberately
avoids the hidden-container-never-ticks trap. 2A (auto-rearm), 2B (panel), 2C (mark+unload) and
2E (supply drop + merge) are complete. 2D (rally points) is built but sync-unsafe. Phase 3
(template sidebar for pre-loaded transport purchasing) was never started — zero hits repo-wide.

### **[BLOCKER]** Eject rally points are a client-local write that the simulation reads

- **Perceived:** works perfectly in skirmish against the AI. In a network game the clients
  diverge the first time anyone sets an eject rally point; in a replay the rally never fires at all.
- Evidence: `engine/OpenRA.Mods.Common/Orders/EjectRallyOrderGenerator.cs:62` calls
  `cargo.SetEjectRally(passengerActorId, target)` directly and **yields no `Order`** — that is the
  gate. The destination lands in a plain `Dictionary<uint, Target> ejectRallyPoints`
  (`Cargo.cs:190`, not `[Sync]`, written only at `:485`). The simulation then reads it inside an
  activity that runs on every client: `UnloadCargo.cs:131` `cargo.GetEjectRally(actor.ActorID)`,
  and at `UnloadCargo.cs:157-160` issues a real `w.IssueOrder(new Order("Move", actor,
  rallyTarget, false))` when the target is valid. On the ordering client the passenger walks; on
  every other client `rallyTarget` is `Invalid` and it stands still. Divergent unit positions.
- Confidence: **high** (traced end to end, both halves read directly).
- Fix size: **small** — wrap the set in an `Order` plus an `IResolveOrder` case on `Cargo`, ~30 lines.

### **[SHOULD-FIX]** A supply truck cannot replenish a dropped cache

- **Perceived:** silently nothing. Ctrl+click a crate with a loaded truck and there is no cursor
  and no order. Supply flows one way onto the ground and only comes back by draining it.
- Evidence: the gate is `DropsSupplyCache.cs:705` — `DeliverSupplyOrderTargeter.CanTargetActor`
  returns false unless the target carries `AbsorbsSupplyCache`, which exists only on
  `logisticscenter` (`structures.yaml:396`). `SUPPLYCACHE` (`misc.yaml:370-446`) has no such trait.
  This is the explicit ask in `RELEASE_V1.md:52` ("targetable by other supply trucks to replenish").
- Confidence: **high**. Fix size: **medium** — the merge arithmetic already exists in
  `DropSupplyCacheHere`; needs a targeter branch and an activity.

### **[POLISH]** A dropped cache dies silently

- **Perceived:** a crate holding up to 750 supply pops out of existence with a generic death — no
  explosion, no scaling with remaining contents. `RELEASE_V1.md:52` asks for "large explosion on
  death, size scaling with remaining supplies".
- Evidence: `misc.yaml:370-446` carries no `Explodes` and no `SpawnActorOnDeath`.
- Confidence: high. Fix size: small (YAML + a weapon def).

### **[COSMETIC]** Cargo panel vanishes on multi-select

- **Perceived:** box-select two loaded BMPs and the cargo UI disappears entirely rather than
  showing a merged or first-of view.
- Evidence: `CargoPanelLogic.cs:210-212` early-returns when `selected.Length != 1`.
- Confidence: high. Fix size: small.

### **[COSMETIC]** Cache is proximity-capturable by anything that walks past

- Evidence: `misc.yaml:407-411` — `ProximityCapturable` Range `1c512`, `Sticky: true`, CaptorTypes
  `Player, Vehicle, Tank, Infantry`. Any enemy drifting within 1.5 cells flips it permanently.
  May be intended war-booty behaviour; `economy.md:135` lists capture as a recovery route but does
  not state it is this cheap. Confidence: medium on intent, high on mechanism.

### Tracker correction

`RELEASE_V1.md:52`'s premises are stale. The cache **is** a real supply actor
(`SupplyProvider`, `misc.yaml:427`), **does** have its own bar (`SupplyProvider.cs:974`
`ISelectionBar`), and is **not** indestructible — `Health: 5000` / `Armor: Light` /
`Targetable: Ground, Structure` (`misc.yaml:400-407`) against 22 500–75 000 HP for real
structures. Only the explosion and the truck-replenish half of that item remain open.

---

# 2. Supply & ammo economy overhaul — LIVE, one defect

P1–P3 are structurally shipped and the economy is the best-documented system in the repo
(`DOCS/reference/economy.md`, normative over code). The five-point **Verify** list at
`RELEASE_V1.md:48` has never been confirmed; four of the five now check out statically.

| Verify item | Result |
|---|---|
| Empty TRUK refund = 250 | **CONFIRMED.** `Valued.Cost: 1000` (`vehicles.yaml:537`), `SupplyProvider.SupplyCreditValue: 750` (`:572`); `MissingSupplyValue` prorates to 750 at empty (`SupplyProvider.cs:1210-1219`) and `CustomSellValue.cs:49-53` subtracts it. 1000 − 750 = **250**. |
| Cannot refill within 3c0 unless docked at 2c0 | **CONFIRMED.** LC `SupplyProvider.Range: 2c0` (`structures.yaml:387`) with `DockedCondition: unit.docked` (`:390`), matched by `ProximityExternalCondition@UNITDOCKED Range: 2c0` (`:400-404`). The old 3c0 is gone; delivery is re-checked at the moment of effect via `SupplyProvider.InAuraRange`. |
| Right-click LC behaviour | **CONFIRMED.** Plain right-click → `RestockOrderTargeter` priority 7 (`DropsSupplyCache.cs:660`), which requires a `DockedCondition` host and fires when the truck is not full **or** damaged (repair + refill). Ctrl+click → `DeliverSupplyOrderTargeter` priority 6, explicitly gated `if (!modifiers.HasModifier(TargetModifiers.ForceMove)) return false;` (`:699`). Matches spec exactly. |
| Multi-pool tooltip | **DEFECT — see below.** |
| Tier-cost feel | **Structurally complete**, subjective half unverifiable statically. 63 live `AmmoPool` blocks across the mod; 60 carry an explicit `SupplyValue`. The only three without are on the **orphan `a10` / `frog` actors** (`aircraft-america.yaml:697,699`; `aircraft-russia.yaml:720`) — deliberately hidden for v1 per `RELEASE_V1.md:168`. P3's "~63 pools priced" is accurate. |

### **[SHOULD-FIX]** Multi-pool tooltip grand total ignores `ReloadCount` — off by up to 100×

- **Perceived:** hovering any two-pool unit in the production sidebar shows a "Total ammo cost"
  line wildly larger than the unit's own price, and contradicting the per-pool lines printed
  directly above it. On the Bradley the tooltip reads `Total ammo cost: 5100` on a unit that
  costs **1500**, while its own two pool lines say 45 and 600.
- Evidence: `ProductionTooltipLogic.cs:213` computes `pools.Sum(p => p.Ammo * p.SupplyValue)`.
  The per-pool line it sits under computes batch math —
  `batches = ceil(Ammo / ReloadCount); total = batches * SupplyValue`
  (`AmmoPool.cs:90-96`). The grand total omits the `ReloadCount` divisor entirely.
  Bradley (`vehicles-america.yaml`, block at `:278`): pool 1 `Ammo: 900, ReloadCount: 100,
  SupplyValue: 5` → correct 9 × 5 = **45**; pool 2 `Ammo: 8, SupplyValue: 75` (ReloadCount
  defaults 1) → **600**. Correct total **645**; printed total **5100**.
  645/1500 = 43%, which is exactly the "IFV ATGM ~40%" target at `economy.md:167` — confirming
  645 is the intended figure and 5100 is the bug.
- Confidence: **high** (arithmetic checked against shipped YAML and against the normative doc).
- Fix size: **trivial** — one line; reuse the same `ceil(Ammo/max(1,ReloadCount)) * SupplyValue`
  the per-pool path already uses.

### Tracker correction

`RELEASE_V1.md:45` describes P1 as landing "via new `RefillFromHost`/`Restock`". `RefillFromHost`
does not exist anywhere in the tree — it was introduced at `6ccc4bca` and removed at `7a32e3df`
("Rip CargoSupply: TRUK is now a SupplyProvider"). The behaviour survives under `RestockSupply`
(`engine/OpenRA.Mods.Common/Activities/RestockSupply.cs:46`). Doc drift only; no gameplay gap.

### Pre-existing spec violation, restated for the release view

`economy.md:23,28` already records it, but it belongs in a completeness audit: **no aircraft can
rearm or repair anywhere today.** Every airframe names `hpad`/`afld` in `RearmActors` /
`RepairActors`, both hosts carry `Buildable.Prerequisites: ~disabled`
(`structures.yaml:432`, `:500`), nothing in the repo provides `disabled`, and neither is
pre-placed on any of the ten shipped maps. `logisticscenter` is not named by any aircraft. This
is the exact failure mode the audit was looking for — it is simply already known and documented.
Per the doc's own header the table row is authoritative and the code owes the fix; **how** to
close it is an open design decision.

---

# 3. Garrison overhaul (Phases 1–6) — LIVE BUT INCOMPLETE

The core genuinely runs. `GarrisonManager` / `GarrisonProtection` / `WithGarrisonDecoration` /
`AttackGarrisoned` are attached to `^CivBuilding` (`civilian.yaml:63,109,115,118`) and to
`GTWR`/`PBOX`/`HBOX` (`structures-defenses.yaml:127,222,312`). `GarrisonPortOccupant` is on
infantry (`infantry.yaml:66`) with `Targetable` correctly gated `!garrisoned-at-port` (`:61`), so
directional targeting *replaces* normal targeting rather than stacking. Every gating field
defaults ON: `Indestructible = true` (`GarrisonManager.cs:85`), `DynamicOwnership = true` (`:89`),
suppression thresholds 30/60/30/50 (`:95-108`), all read in `Tick` (`:628-645`, `:704`). No lobby
option gates it. The 260504 stabilization round fully landed. Phases 1, 2, 3, 5, 6 and half the
suppression work are live.

Map coverage is adequate for civilian buildings — garrisonable `V01`–`V13`/`V19` appear on 6 of
10 maps. The purpose-built bunkers are thinner: `PBOX` on 1 map, `HBOX` on 1, `GTWR` on 1, and
all three carry `Prerequisites: ~disabled` (`structures-defenses.yaml:191`, `:278`, `:91`) so
they can never be built either. That is the `afld`/`hpad` pattern at much lower stakes.

### ~~**[SHOULD-FIX]** Suppression "duck" is computed every tick and read by nothing~~ — WITHDRAWN 2026-08-17

The dead-field half was right. The conclusion drawn from it was wrong, and acting on the
recommendation would have shipped a bug.

**"Soldiers under moderate fire keep firing at full rate" is false.** `AttackGarrisoned` fires
the deployed soldier's *own* `Armament` (`AttackGarrisoned.cs:288-301`), and an `Armament`
captures its burst / burst-wait / inaccuracy modifiers from its own actor
(`Armament.cs:253-258`) — i.e. from the soldier. The ten-tier `^SuppressionEffects` ladder
(`infantry.yaml:381-392`) therefore already degrades garrison fire, and it starts biting at
suppression 1, not 30. Gating rate-of-fire in `DoGarrisonedAttack` as recommended would have
applied a **second** penalty on top of `^SuppressionBurstMultiplier`, with a cliff at exactly 30.

`IsDucking` and its only input `SuppressionDuckThreshold` were deleted for that reason; a
`// PITFALL:` now sits at the former declaration site. The real defect was never simulation —
it was that none of this was drawn. See the `[POLISH]` item below, now fixed.

### **[SHOULD-FIX]** Phase 4 sidebar panel was never rewritten; it is still the text placeholder

- **Perceived:** selecting a garrison shows a plain-text list ("north: Rifleman [8/10] (80%
  cover)") with tiny `X` buttons instead of unit icons — visibly a debug panel inside a finished UI.
- Evidence: `mods/ww3mod/chrome/ingame-player.yaml:623-700` is `Label@PORT_LABEL_n` +
  `Button@EJECT_PORT_n Text: X`; `GarrisonPanelLogic.cs:61-75` binds only labels.
- Confidence: high. Fix size: medium (~150 lines: icon widget + logic rebind).

### **[SHOULD-FIX]** Panel force-fire and exit-move orders (design item 8) never built

- **Perceived:** the player cannot direct a specific port's soldier at a target, or order him out
  to a chosen spot. The only two actions are Eject-one and Eject-all.
- Evidence: `GarrisonPanelLogic.cs:51` and `:204` are the only `IssueOrder` calls in the file.
- Confidence: high. Fix size: medium.

### ~~**[POLISH]** Suppression state is invisible in the panel (design item 10)~~ — FIXED 2026-08-17

This was the whole defect, and it was larger than "the panel": the building's pip grid
(`WithGarrisonDecoration`) rendered three rows — damage, class, ammo — and no suppression row
either, while the soldier's own `^SuppressionPips` carry `RequiresSelection: true` and the
soldier is a 40%-alpha ghost standing on the building's cell.

Fixed presentation-only, no new simulation state and no new order: a fourth pip row below ammo
draws `pip-suppression-1..10` for every occupant with suppression > 0 (shelter included — see
below), and the panel prints the live level. The hardcoded `(80% cover)` is now derived from the
soldier's enabled `DamageMultiplier` traits, so retuning `DamageMultiplier@GarrisonCover` in
`infantry.yaml` retunes the readout instead of silently making it a lie.

**Shelter occupants matter here and the original framing missed them.** A soldier recalled at
`SuppressionRecallThreshold` keeps its suppression in shelter (`ExternalCondition` is an `ITick`
trait, and `TraitDictionary.ApplyToAllTimed` does not filter on `IsInWorld` — so it keeps
decaying even though `RecallToShelter` removed the actor from the world), and
`SuppressionRedeployThreshold` refuses to redeploy it until that decays below 30. That is why a
port sits empty with soldiers standing by, and it is now the visible part.

### **[COSMETIC]** `mods/ww3mod/chrome/garrison-panel.yaml` is dead weight

Near-duplicate of the live inlined panel, not registered in `mod.yaml`'s ChromeLayout list
(`mod.yaml:164-203`) — anyone editing it will see no effect. Confidence high; fix trivial.

### **[COSMETIC]** The pending decision at `RELEASE_V1.md:148` names a trait that does not exist

It says the decision "touches `EnterGarrison`, `GarrisonManager`, `WithGarrisonDecoration`".
A repo-wide grep for `EnterGarrison` across `engine/` and `mods/` returns **zero hits** — entry
runs through stock `Enter` / `RideTransport`. Scoping that decision against a phantom file will
mislead whoever picks it up.

**Player-facing summary:** the system works and looks reasonable in the field — soldiers enter
houses, the building flips colour, pips appear, they fire out of the correct sides, only
attackers inside a port's arc can shoot back, the house degrades to rubble rather than dying, and
heavy fire drives them inside. What is unfinished is the *interface* to it. The `[T]` marker is
honest. (Updated 2026-08-17: the suppression readout is now built; **do not** add the
rate-of-fire hook this paragraph used to recommend — it double-applies, see the withdrawn item
above. What remains on the interface list is the icon panel and the four unreachable orders.)

---

# 4. Helicopter crash + crew overhaul — LIVE BUT INCOMPLETE (design reversed)

`HeliEmergencyLanding` is on `^Helicopter` (`aircraft.yaml:168`), inherited by all six helis, with
per-heli overrides (`aircraft-america.yaml:13`, `aircraft-russia.yaml:13`). Damage transitions
fire at `HeliEmergencyLanding.cs:173-196`; autorotation glide runs in `Activities/Air/
HeliAutorotate.cs`; crash at `HeliCrashLand.cs:64`. All reachable.

**The `RELEASE_V1.md:35` line describes a design that was deliberately reversed in YAML on
260509.** The safe-land → neutral → repairable → capturable chain is cut in two places on purpose:

- `aircraft.yaml:186` — `TransferToNeutralOnSafeLanding: false`, overriding the engine default
  `true` (`HeliEmergencyLanding.cs:105`). **No neutral transfer ever happens.**
- `aircraft.yaml:300-302` — comment records that `RepairableBuilding@CrashDisabled` and
  `Targetable@VehicleRepair` were "removed 260509: the crash-disabled-then-repair-and-fly-again
  concept was scrapped." **Not repairable.**
- `aircraft.yaml:192-195` — `ChangesHealth@CrashBurn: -2% / 5 ticks` gated `crash-disabled`, so a
  safe-landed airframe bleeds to zero in ~12.5 s and explodes.

Capture-by-pilot-entry is the one surviving link and it *is* mechanically reachable:
`HeliEmergencyLanding.cs:338-339` sets `AllowForeignCrew = true`;
`Orders/EnterAlliedActorTargeter.cs:40-45` admits non-allied targets when that flag is set;
`Activities/EnterAsCrew.cs:69-71` calls `ChangeOwner`. Roles line up (`crew.pilot.*` Role: Pilot,
`crew.yaml:61,125`, vs `CrewSlots: Pilot` on every heli).

### **[SHOULD-FIX]** "Safe land = neutral + repairable" is scrapped in YAML but still on the v1 list

- **Perceived:** nothing wrong in-game — the roadmap item is a phantom. A player never encounters
  a neutral repairable helicopter, so the release-note promise is *stale*, not visibly defective.
- Evidence: `aircraft.yaml:186`, `aircraft.yaml:300-302`.
- Confidence: high. Fix size: one-line doc edit to close it, or ~half a day to reinstate the design.

### **[SHOULD-FIX]** Capture-by-pilot yields an immobile, unarmed wreck that explodes in ~12 s

- **Perceived:** the player walks a pilot into a downed enemy heli, it changes hands, and then
  nothing happens and it blows up. A live path to a worthless prize.
- Evidence: speed pinned to zero (`aircraft.yaml:260-262`), firepower zeroed (`:275-277`), and the
  only un-gate — `CheckDisabledRecovery` requiring `health.DamageState < Heavy`
  (`HeliEmergencyLanding.cs:411-416`) — can never be satisfied because the repair traits that
  would raise HP were the ones deleted.
- Confidence: high. Fix size: small — gate `CrashBurn` off when re-crewed, or drop the capture path.

### **[SHOULD-FIX]** Helicopter husks on water don't sink — NOT BUILT

- **Perceived:** heli wrecks sit on the sea surface indefinitely.
- Evidence: grep for `sink` across `mods/ww3mod/rules/` returns zero hits; no sinking trait is
  attached anywhere. Confidence: high. Fix size: medium (trait, or a water-terrain husk variant).

### **[POLISH]** Littlebird rotor still spins after safe landing

- The wind-down path *is* reachable — `HeliAutorotate.cs:60-67` grants `rotor-stopped` after
  `RotorWindDownTicks: 60`, and the littlebird has the overlay set
  (`aircraft-america.yaml:259-271`). Likelier culprit: `littlebird.Husk` carries an
  **unconditional** spinning rotor — `husks-aircraft.yaml:253-254`, `WithIdleOverlay: Sequence:
  rotor` with no `RequiresCondition` — so it is the *wreck* that spins, not the landed airframe.
- Confidence: medium. Fix size: small. (Open item at `RELEASE_V1.md:55`.)

### Vocabulary check, flagged as requested

The code maps **Heavy (<50%) → survivable autorotation** (`HeliEmergencyLanding.cs:99`) and
**Critical (<25%) → uncontrolled crash + `self.Kill`** (`:102`, `:360-367`). In the user's
vocabulary "critical damage" means `DamageState.Heavy`, so "critical = total loss" would mean
*Heavy* = total loss — whereas shipped behaviour gives Heavy a controlled glide and a survivable
landing. Worth one sentence of confirmation before anyone "fixes" it in either direction.

---

# 5. Vehicle crew system — LIVE BUT INCOMPLETE

Genuinely wired. `VehicleCrew` is on real tanks with full slot data
(`vehicles-america.yaml:288-302`: `CrewSlots: Driver, Gunner, Commander`, `CrewActors`,
`SlotConditions`, `EjectionOrder: Commander, Gunner, Driver`). No gating condition, no lobby
option, no `false` default.

- **Slot ejection — complete.** Triggers at `VehicleCrew.cs:173` on `EjectionDamageState`
  (default `Heavy`, `:61`), staged via wait-for-stop and countdown (`:207-247`), spawns crew with
  inherited burn stacks (`CrewFireStackOffset: -3`, `:80`; grant at `:359-362`). Death is total
  loss for anyone still inside (`:250-266`).
- **Re-entry — implemented and reachable.** `CrewMember.cs:49-97` issues `EnterAsCrewMember`;
  `Activities/EnterAsCrew.cs:30-74` reserves and fills; `VehicleCrew.cs:446-489`
  (`HasEmptySlot` / `CanAcceptRole` / `ReserveSlot` / `FillSlot`) restores the slot condition,
  lifting the corresponding speed/firepower penalties.
- **Commander substitution — NOT BUILT.**

Note: the 260507 rework (`WORKSPACE/archive/plans/260507_crew_evac_plan.md`) that would have
*deleted* re-entry was never executed — `EvacResolver.cs` and `OnFireFromHealth.cs` do not exist.
The shipped system is the older design, which is what `RELEASE_V1.md:40` still describes.
Consistent, not drifted.

### **[SHOULD-FIX]** Commander substitution not implemented

- **Perceived:** silently nothing. Lose the commander and `has-commander` stays off permanently —
  a decapitated tank is permanently degraded unless the player happens to walk a *commander-role*
  survivor back in. A driver or gunner cannot promote into the empty slot.
- Evidence: no promotion code path exists in `VehicleCrew.cs`; the slot API is `FillSlot` /
  `ReserveSlot` only (`:462-489`). Grep for commander/promote/substitute across `engine/` returns
  only descriptive prose (`CrewMember.cs:23`, `VehicleCrew.cs:24`).
- Confidence: high. Fix size: medium (~a day: promotion rule, condition swap, tests).

### **[POLISH]** Re-entry has no discoverability

- **Perceived:** the feature works but players will not find it — the only cue is a cursor change
  on hover. Evidence: `CrewMember.cs:26-30`. Confidence: high. Fix size: small.

### Not a defect — do not "fix"

Ejected crew burning to death is **intended** per the user ruling recorded at
`DOCS/reference/game-model.md:49-55`; a fix was built and deliberately reverted at `36ad9865`.

---

# 6. Stance rework (4 phases) — LIVE AND COMPLETE

All four axes ship, are attached to real actors, and are reachable from the UI.

- **Modifiers (Click / Ctrl / Ctrl+Alt / Alt).** `Settings.cs:315-321` —
  `AttackMoveModifiers = Alt`, `ForceMoveModifiers = Ctrl`,
  `ForceAttackModifiers = Ctrl | Alt`; consumed in `UnitOrderGenerator.cs:178-179`, `:217-218`.
  Command-bar buttons exist for all of them (`ingame-player.yaml:94,114,135`).
- **Resupply behaviour.** `ResupplyBehaviorSelectorLogic` (`ingame-player.yaml:555`), Hold/Auto/
  Evacuate; backed by `InitialResupplyBehavior` / `InitialResupplyBehaviorAI`, defaulted `Auto`
  on `^AutoTarget` (`defaults.yaml:322-323`) with four deliberate `Evacuate` overrides
  (TRUK, m270, grad, tos) per `economy.md:44`.
- **Cohesion.** `CohesionSelectorLogic` (`ingame-player.yaml:486`), Tight/Loose/Spread; defaults
  `Loose` (`defaults.yaml:320-321`); `CohesionMoveModifier` on the World actor
  (`world.yaml:279`); `CohesionSlotMemory` on `^Combatant` (`defaults.yaml:20`), which every
  infantry and vehicle actor inherits.
- **Patrol.** `PatrolOrderGenerator.cs:28` (click to add waypoints, click Patrol again to
  confirm) → `PatrolActivity` (`Activities/Patrol.cs:25`), with circular-vs-bounce detection
  (`:41-46`) and attack-move legs (`:61`). Button wired at `ingame-player.yaml:215`,
  `CommandBarLogic.cs:208-219`.

Plus a third fire-stance axis (`StanceSelectorLogic`, FireAtWill/Ambush/HoldFire) and an
engagement axis (`EngagementStanceSelectorLogic`, Hunt/Defensive/HoldPosition).

**Ambush works for humans**, contrary to what the `defaults.yaml:339-345` comment might suggest
on a skim: the base behaviour — hold fire until spotted, then spring and coordinate allies within
`AmbushCoordinationRadius` (10 cells) — runs ungated at `AutoTarget.cs:618-622`, `:711-713`,
`:727-734`. Only the Stage-3 widened-ambush *scoring* is gated behind `AmbushTacticsCondition`,
which only bot-posted ambushers hold. That is a deliberate bot-only enhancement, not a hole.

### **[COSMETIC]** Contradictory comment on `StancePositioningExecutor`

`defaults.yaml:24-26` says "Default-OFF: … Humans / @stable / @normal never satisfy either ⇒
byte-identical", but `defaults.yaml:40-45` immediately below adds
`GrantConditionOnHumanOwner@tacpos` granting `enable-tactical-positioning` to **every**
human-owned combatant, described there as "Phase-3 human enablement (RATIFIED default-ON)". The
second is what ships; the first is stale and directly contradicts it. Anyone reasoning about
whether human units do tactical repositioning will read the wrong half. Confidence: high.
Fix size: trivial (delete the stale sentence).

---

# 7. Supply Route contestation — LIVE AND COMPLETE (tracker stale)

**`RELEASE_V1.md:38` marks this `[ ]` open. It ships, and all three named pillars work.**

Wired to the real actor: `SupplyRouteContestation` is on the shipped `SUPPLYROUTE` block
(`structures.yaml:263-272` — Range `10c0`, `SlowdownThreshold: 50`, `BaseTicks: 1500`,
`FriendlyRecoveryMultiplier: 3`), with `WithRangeCircle@Contestation` at `:258-262`.
`SUPPLYROUTE` spawns unconditionally for every player via `BaseActor: supplyroute` on every
`StartingUnits@*` (`world.yaml:316-388`) — the `Buildable.Prerequisites: ~disabled` at
`structures.yaml:246` blocks *building* one, not the starting spawn. The tick has no TestMode
gate, no `RequiresCondition`; the only guard is
`WinState != Undefined || NonCombatant || !Playable` (`SupplyRouteContestation.cs:218`).
Detection reaches real units — `CaptorTypes = {Player, Vehicle, Tank, Infantry}` (`:32,159`)
overlaps `^Soldier`'s `Types: Infantry` (`infantry.yaml:177-178`) and `^Vehicle`'s
`Types: Vehicle` (`vehicles.yaml:67-68`).

- **Control bar:** `ISelectionBar.GetValue/GetColor` (`:636-660`), green above threshold, yellow
  below, red for the defeat bar. `IAlwaysVisibleBar.ShowBarWithoutSelection` (`:663`) is consumed
  at `SelectionDecorationsBase.cs:94`, so it appears **without** selecting the SR. Plus a periodic
  `FlashTarget` (`:258-264`).
- **Production slowdown:** `IProductionSpeedModifier` (`:620-632`), linear from 100% at threshold
  to 0% at empty, consumed at `ProductionQueue.cs:366` and applied in `TickInner:338-350`. The SR
  carries `Production@Local` and `ProductionFromMapEdge` (`structures.yaml:315-319`), so it is in
  the filtered set for every shipped queue.
- **Notifications:** contestation start (`:317-319`), defeat warning (`:342-344`), passive
  (`:362-368`), reinstated (`:607-614`), rate-limited and filtered to owner/allies.

### ~~**[SHOULD-FIX]** Passive notification claims income is frozen; income is not frozen~~ — FIXED 2026-08-22 (`wt/sr-message`)

> Both notifications now read "Production frozen." The same change also stopped the freeze line
> firing for a player being defeated in the same tick. See DISCOVERIES 2026-08-22.

- **Perceived:** the player reads "Production and income frozen" and then watches credits keep
  accruing — the game tells them something untrue at the tensest moment of a match.
- Evidence: `isPassive` is read in exactly three places (`SupplyRouteContestation.cs:283`, `:403`,
  `:623`) — the production modifier and the elimination check. No `PlayerResources` / income
  consumer exists anywhere.
- Confidence: high. Fix size: small (correct the string, or wire an actual income gate).

### **[POLISH]** Aircraft, helicopters and ships cannot contest

Parking a gunship over the enemy SR does nothing. `^Aircraft` / `^Helicopter`
(`aircraft.yaml:95,136`) carry no `ProximityCaptor`; the only air one is `^NeutralAirborne` with
`Types: Plane` (`aircraft.yaml:52-53`), and `Plane` is not in `CaptorTypes`. Naval's is commented
out (`naval.yaml:36`). Likely intentional; flagged so the choice is explicit. Fix: one line per
template.

### **[COSMETIC]** Ship queue never slows

`ClassicProductionQueue@Ship` (`player.yaml:65`) has no matching `Produces` on the SR, so the
modifier finds no producer and returns 100. Moot while naval does not ship.

### Related, correctly still open

SR **capture** remains unwired exactly as `supply-route.md:67-74` and `CLAUDE.md` state — no
`Capturable` / `CaptureManager` on `structures.yaml:202`, `CaptureNotification` commented out at
`:216`. `RELEASE_V1.md:103-104` are correctly open. This is not a contestation defect.

Note also that `RELEASE_V1.md:64`'s claim that SR contestation "depends on visibility too" is not
borne out — the trait uses an `ActorMap` proximity trigger, not vision. **Nothing blocks
contestation on the unresolved fog decision.**

---

# 8. Three-mode move system — LIVE AND COMPLETE

Every order path exists and is reachable. Modifiers resolve in `UnitOrderGenerator.cs:178-179`,
`:217-218` from `Settings.cs:315-321`. `AttackMove` — required for `AttackMoveActivity` to scan
and engage at all — is attached on `^AutoTarget` (`defaults.yaml:336`) and again on
`aircraft.yaml:49`, `infantry.yaml:84`, `vehicles.yaml:64`, so it covers the whole roster.
Force-Move bypasses `IWrapMove` deliberately (`Mobile.cs:1032`). Rally points carry the same
three modes with distinct line colours (`RallyPointIndicator.cs:24-28,165`).

`SmartMoveActivity` is the most carefully-reasoned file in this audit — it correctly refuses to
pin a dry unit (`:104`, using the same `AmmoPool.CannotFight` predicate as resupply dispatch so
the two cannot drift), filters allied heal targets out of move-interrupts (`:110-117`), and
filters paused armaments so an empty weapon cannot cancel a move (`:121-129`).

### **[COSMETIC / verify-intent]** `SmartMove` is attached to infantry only

`SmartMove:` appears in exactly one place in all of `mods/` — `infantry.yaml:53`, on `^Infantry`.
No vehicle or aircraft template carries it, so the "pause to fire at targets in weapon range
while moving" behaviour is infantry-only. This is very likely **correct by design**: infantry
must stop to shoot, whereas `AttackTurreted` vehicles (fifteen of the seventeen armed ones per
`economy.md:204`) already fire on the move without stopping, and the two `AttackFrontal`
artillery pieces should not stop. `TestGlobal.cs:398` states it as settled fact ("`Move` is
wrapped by every `IWrapMove` trait — `SmartMove` on `^Infantry`"). Recorded only so the choice is
visible rather than incidental. Confidence: high on the mechanism, medium on intent.

---

# 9. Infantry mid-cell redirect — LIVE BUT INCOMPLETE

Wired. `Mobile.CanRedirectMidCell` defaults `false` (`Mobile.cs:97`) but is set `true` on
`^Infantry` (`infantry.yaml:50`), which `^Soldier` (`:168`) and civilians (`:336`) inherit — every
infantry actor in normal play. `RedirectSpeedPenalty` exists (`Mobile.cs:100`, default 50) and is
explicitly `50` in YAML (`infantry.yaml:51`), matching `RELEASE_V1.md:41`. Three reachable code
paths, all on the same flag: the mid-cell interrupt `IsInterruptible = mobile.Info.
CanRedirectMidCell` (`Move.cs:492`, honoured at `Activity.cs:214`), position rebasing on cancel
(`Move.cs:436-437`), and smooth start-from-current-position (`Move.cs:227`). Infantry visibly do
turn immediately mid-cell — better than vanilla.

### **[POLISH]** The redirect penalty is a one-shot dip the acceleration curve erases in ~5 ticks

- **Perceived:** sharp reversals feel almost free, so tuning the number named in the tracker has
  little leverage — which is probably why the item is still open.
- Evidence: `Move.cs:125-137` applies the cut **once**, in `OnFirstRun`, by scaling
  `mobile.CurrentSpeed`. `Move.cs:553-568` then re-accelerates `CurrentSpeed` toward
  `movementSpeedForCell` every tick using `Acceleration = {3,2,1}` (`Mobile.cs:44`, no infantry
  override). A 180° turn at `Speed: 25` halves 25→12, then climbs back in roughly five ticks
  (~0.2 s). It also only bites above 90° (`angleDiff > 256`, `Move.cs:133`) — a 90° corner is
  entirely free.
- Confidence: high on the mechanism, medium on whether this is a defect or accepted feel.
- Fix size: small — hold the reduced ceiling for N ticks rather than lowering the percentage.

---

## Recommended priority order

1. **Cargo eject-rally desync** (BLOCKER) — the only finding that breaks a game rather than
   disappointing a player. Small fix.
2. **Multi-pool tooltip grand total** — trivial fix, and it is the first number a player reads
   about the mod's headline economy. Currently self-contradicting on screen.
3. **Truck cannot replenish a dropped cache** — closes the urgent item at `RELEASE_V1.md:52` and
   completes the supply loop on the seven maps with no Logistics Center.
4. ~~**Garrison `IsDucking`**~~ — withdrawn 2026-08-17. The graduated tier was never missing;
   `^SuppressionEffects` already applies it to garrison fire. Adding the hook would double-apply.
   The readout gap it was really pointing at is now fixed.
5. **Heli capture yields a wreck that explodes** — decide: gate `CrashBurn` off when re-crewed,
   or remove the capture path. Either way stop offering the player a dead end.
6. **Commander substitution** — the largest genuinely-missing feature in this slice.

Tracker hygiene, near-free: flip SR contestation to `[T]`, correct the helicopter line to match
the 260509 reversal, drop `RefillFromHost` from the P1 description, remove the `EnterGarrison`
reference from the garrison decision, and delete the stale "Default-OFF" sentence at
`defaults.yaml:24-26`.
