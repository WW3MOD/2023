# WW3MOD v1 Release Tracker

> Single source of truth for v1 scope. Update continuously as items are tested, fixed, deferred, or cut.
>
> **Status legend:** `[ ]` open · `[~]` in-progress · `[T]` testing · `[x]` done · `[v1.1]` deferred · `[cut]` won't-fix v1
>
> **Scope is locked.** New features need explicit "yes, add to v1" from the user. Otherwise → `BACKLOG.md` or `Pending decisions` below.

## Phase

**Currently in: Phase A — Stabilize**

- **Phase A — Stabilize** — get every "needs playtesting" system verified or fixed. No new features.
- **Phase B — Tier-1 fixes** — bugs and gameplay gaps that block release.
- **Phase C — Polish** — sounds, icons, descriptions, open polish threads.

---

## Phase A — Stabilize

Systems built but not verified end-to-end. Each needs a focused playtest pass to confirm working / surface bugs.

### Big systems needing playtest
- [T] **Garrison overhaul** (Phases 1–6) — indestructible buildings, dynamic ownership, directional targeting, suppression integration, visuals · *playtest 260503_1241*
- [T] **Garrison playtest 260504 — observations checklist** *(reported live during playtest)*
  - [T] **Soldier blinks up/down between basement and port (~10×/sec) at distance** — *Fixed 260504 (this turn)*: Z-axis inconsistency between `GarrisonManager.Tick` (clamps Z to terrainZ every tick) and `AttackGarrisoned.DoGarrisonedAttack` (wrote `pos + portOffset` without Z clamp, only when `CurrentTarget` valid AND in arc). When a target dipped in/out of arc each scan, the writer alternated and the soldier visibly oscillated by `portOffset.Z` (200–427 WDist). Most soldiers stable because they had a steadily-in-arc target; this one was at borderline range. Now both call sites clamp Z to terrain. Verify with the same setup (long-range fight at arc edge).
  - [T] **Two basement soldiers visually swap places (~1/sec)** — *Fixed 260504 (this turn)*: pip rendering iterated `shelterPassengers` in storage order. When a soldier was recalled, `shelterPassengers.Add(soldierRef)` appended them to the end — so each deploy/recall cycle reorders the list (e.g. `[A,B]` → `[B,A]`) and the pips visually swap. `WithGarrisonDecoration.AllSoldiers` now sorts shelter section by `ActorID` (spawn order) so pip slot assignment is stable regardless of internal Add/Remove churn. **Underlying cycling (open):** a recalled soldier's suppression doesn't fully decay during the 50-tick suppression lockout (~2s), so the next deploy may immediately re-suppression-recall the same or another already-suppressed soldier. Cosmetically masked by the sort, but if combat feels like soldiers waste ammo deploying/recalling without firing, score-penalize already-suppressed soldiers in `FindBestShelterSoldier` or extend `SuppressionLockoutTicks` to ~125 (5 sec, more than full decay).
  - [T] **Pre-entry stop + queue skip-ahead** — *Fixed 260504*: (a) collapsed `MoveCooldownHelper` cooldown to (0,1) for Enter (was the source of the visible 0.8-1.2s pause when destination cell registered as blocked); (b) RideTransport.TickInner now checks `Cargo.HasSpace` per-tick during Approaching and `Cancel(keepQueue:true)` on full so shift-queued soldiers skip-ahead to the next building. Verify in playtest.
  - [ ] **Littlebird minigun inaccuracy** — too inaccurate, needs balancing. *Quick-fixed 260504: cut Inaccuracy from 0c768 → 0c256 in `7.62mm.Minigun`.* Verify in playtest.
  - [T] **Garrisoned soldiers can't be rearmed** — *Fixed 260504 (commit 56a31d89)*: removed the `!currentTarget.IsInWorld` early-exit in `ResupplyTarget` (CargoSupply.cs:330). Shelter passengers are intentionally out-of-world; SetTarget already skipped the move-toward and condition-grant correctly. Verify both port AND shelter rearm in playtest.
  - [ ] **Supply truck → building target = transfer supplies** *(new feature)* — truck targets a garrison building, cursor shows wrench/transfer icon, truck drives up and dumps cargo into the building. Building gains its own supply bar (gold/orange like trucks); soldiers inside/nearby drain it; can be replenished by another truck. Building with supply behaves like a parked supply truck.
  - [T] **Supply truck deploy → drop cache as box** — *Fixed 260504*: commit e573530b (Mar 25) replaced TRUK's `DropsCrate` (which provided `IIssueDeployOrder`) with `CargoSupply` and the deploy-order entry point was lost — only the sidebar `DROP_SUPPLY` button worked. Added `IIssueDeployOrder` + `DeployOrderTargeter("DropCargoSupply")` to `CargoSupply`; deploy fires `UnloadCargoSupply` with `ExtraData = (uint)supplyCount` routing through the existing `DropSupplyCache` path (auto-merges with any existing cache on the cell). `CanIssueDeployOrder` gates on `supplyCount > 0` and cell containing only self or a SUPPLYCACHE. **Scope locked to TRUK-only**: removed `CargoSupply` from TRAN, HALO, HIND helicopters in `aircraft-america.yaml` and `aircraft-russia.yaml`. The earlier "any transport carries supply" experiment is rejected; CargoSupply lives on TRUK alone. Verified no auto-regen anywhere in the supply chain — TRUK pool, LC pool, SUPPLYCACHE pool all only drain. Supply only enters the map via fresh trucks bought from map edge.
  - [T] **Garrison port↔shelter chaotic switching** — *Fixed 260504*: added 4 hysteresis fields to GarrisonManagerInfo (`MinDeployTicks=75`, `RedeployBlackoutTicks=30`, `TargetConfirmTicks=10`, `StickyTargetTicks=50`) plus bumped `IdleRecallTicks` 125→250. Sticky targets keep ports committed through brief arc/LOS gaps. State cleanup symmetric on RecallToShelter / OnPassengerExited / DeployToPort. All YAML-tunable. Playtest will tune values.
  - [ ] **Garrison pips left-aligned, too wide** — *Quick-fixed 260504: pips now centered (math already correct), and column count is dynamic based on capacity: 4→2×2, 6→3×2, 8→4×2, 12→6×2. Capped at 6 cols.* Verify in playtest.
  - [ ] **Vehicle crew ejection too generous** — too many crew survive vehicle destruction. Want: crew dies more often. Bonus: rare cosmetic "burning crawl-out" — crew exits visibly on fire and dies a moment later, doomed-looking. Ratio TBD; needs design pass.
  - [ ] **Helicopter safe-crash-land too common** — too many helis end up as neutral wrecks littering the map. Should be rare, not the default. Likely needs probabilistic gate in `HeliEmergencyLanding.DamageStateChanged` (e.g., `AutorotationChance` percent on heavy damage; fail = StartCrash instead).
  - [ ] **Littlebird rotor still spins after safe landing** — user reports rotor anim still playing post-landing. YAML setup looks identical to other helis (`rotor-stopped` condition gates `still-rotor` overlay), and `HeliAutorotate.Tick` calls `OnRotorsStopped` after `RotorWindDownTicks`. Needs investigation — possibly `airborne` condition not being revoked, or `still-rotor` sequence frame is still showing visible blades. Verify behavior on all helicopters as a sweep.
  - [T] **Garrisoned soldiers stuck at portholes after building damaged, can't be moved** — *Fixed 260504*: ordering bug in `GarrisonManager.RecallToShelter` — `DeployedSoldier = null` ran before `RevokePortCondition`, which silently no-oped (it reads `PortStates[i].DeployedSoldier` and skips if null). Each suppression-recall cycle leaked another `garrisoned-at-port` token; tokens accumulate, so when the building eventually dies/sells/ejects and only the latest token gets revoked, the leaked stack keeps `Mobile.PauseOnCondition: garrisoned-at-port` active → soldiers visible at port world positions but can't be selected/moved. Fix mirrors the order used in `OnPassengerExited`/`Killed`/`EjectGarrisonPassenger`/`Unload`: revoke first, then nullify. Existing leaked soldiers in a current playtest are unrecoverable (lost tokens) — fresh game required to verify.
  - [T] **Soldiers in 1HP rubble building can't be evacuated** — *Fixed 260504*: real cause was `Cargo.IsEmpty()` returning true when all soldiers are deployed at ports (port deploy removes them from Cargo passenger list). Both Unload UI entry points (Cargo's `DeployOrderTargeter` and `GarrisonPanelLogic`'s Eject All) gated on `IsEmpty()`, so the player couldn't issue Unload during active combat — exactly the state most likely at 1HP rubble. Fix: added `GarrisonManager.HasAnyOccupants` (port + shelter), GarrisonManager owns its own `IIssueOrder` for Unload-when-shelter-empty-but-ports-occupied, Eject All button uses `HasAnyOccupants`. NOT a damage-state condition issue — investigation notes at `docs/superpowers/specs/2026-05-04-rubble-evacuation-investigation.md`.
  - [T] **Rubble building offers full protection** — *Fixed 260504*: added `RubbleProtection` field to `GarrisonProtection` (default 30%, vs Base 95 / Critical 70). Active when `Health.HP <= 1`. Soldiers can stay inside but take ~70% of incoming damage, incentivising evacuation rather than camping rubble.
  - [T] **Port + shelter double-display (same soldier rendered in both basement and port)** — *Defensive fix 260504*: static analysis didn't pin a deterministic asymmetry path (data flow looks correct: `DeployToPort` → shelter remove + port set; `RecallToShelter` → port null + shelter add, both with `suppressNotifications` gating). Likely a frame-edge / sub-tick interleave under heavy suppression cycling. Fix is defensive: `WithGarrisonDecoration.AllSoldiers` now deduplicates port+shelter via HashSet (port wins); `OnPassengerEntered` rejects adds for actors already at a port or already in shelterPassengers (prevents duplicate-pip ghost). If `cargo.PassengerCount` mismatches rendered shelter count in playtest, that points at the still-undiscovered state-leak source.
- [T] **Supply truck resupply bar + LC refill** — *Shipped 260504*: TRUK now has the same 3-stance resupply bar as ammo units (Hold / Auto / Evacuate). Default = Evacuate (overrides ammo-unit Auto default — zero-micro for new players, opt-in to LC logistics). Auto seeks nearest friendly Logistics Center with supply > 0, refills CargoSupply pip-by-pip via LC's existing SupplyProvider; falls through to Evacuate if no LC available. SupplyProvider extended to accept CargoSupply targets in addition to Rearmable ammo pools. Spec/plan: `docs/superpowers/{specs,plans}/2026-05-04-supply-truck-resupply-and-rubble-evac*.md`. Verify in playtest.
- [T] **Supply & ammo economy overhaul (260506, plan: `CLAUDE/plans/260506_supply_ammo_economy.md`)** — Phases 1–3 shipped:
  - **P1 mechanics:** empty-truck evacuate refund now deducts missing CargoSupply (full TRUK = 1000, empty = 250); LC SupplyProvider Range 3c0 → 2c0 + new `DockedCondition: unit.docked` gate (clients must literally dock to refill, SUPPLYCACHE keeps proximity behaviour because it leaves the field empty); right-click LC default = repair (+ refill) via new `RefillFromHost` activity and `Restock` order (priority 7, above Repairable's 5), Ctrl+click = deliver supply via the existing `DeliverSupply` order; auto-refill empty trucks now route through the same Restock activity instead of a raw MoveTo; new `ExternalCondition.IsGranted` helper. Tests: `engine/OpenRA.Test/OpenRA.Mods.Common/CargoSupplyEconomyTest.cs` (refund math + LC drain math). 8 commits.
  - **P2 tooltip:** new `IProvideTooltipDescription` interface (priority bucketed: 100 = weapons, 200 = armor, 300 = mobility, 400 = capabilities); `AmmoPoolInfo` implements it for one line per pool with weapon name + `Ammo × supply = total`; `ProductionTooltipLogic` appends auto-block under static description and adds a grand-total line for 2+ pools. 1 commit.
  - **P3 balance:** ~63 active AmmoPools across 9 YAMLs now have explicit `SupplyValue` + matching `CreditValue` per a tier table (T0=1 pistol, T1=2 rifle, T2=3 LMG/DMR, T3=5 HMG, T4=15 autocannon, T5=80 tank gun, T6a=25 RPG / T6b=100 ATGM, T7=60 MANPAD, T8=200 air-to-ground, T9=1500 cruise/MLRS, T11=25 mines). 6 commits.
  - **Verify in playtest:** empty TRUK refund = 250; truck cannot refill within 3c0 of LC unless docked at 2c0; right-click LC = repair+refill on damaged truck, refill-only on healthy; Ctrl+click LC = drop-to-LC; production tooltip on tanks/aircraft shows weapon block + grand-total when multi-pool; balance feel of T1 vs T5 vs T8 ammo costs.
- [ ] **Cargo system** (Phases 2A–E) — TRUK auto-rearm, mark+unload, rally points, supply drop, merge
- [ ] **Helicopter crash + crew overhaul** — critical=total loss, safe land=neutral+repairable, capture-by-pilot-entry
- [ ] **Stance rework** (4 phases) — modifiers (Click/Ctrl/Ctrl+Alt/Alt), resupply behavior, cohesion, patrol
- [ ] **AI overhaul** (Tiers 0–3.1) — bot modules, multi-axis attacks
- [ ] **Supply Route contestation** — graduated control bar, production slowdown, notifications
- [ ] **Three-mode move system** — Move/Attack-Move/Force-Move, SmartMove wrapping
- [ ] **Vehicle crew system** — slot ejection, re-entry, commander substitution
- [ ] **Infantry mid-cell redirect** — tune `RedirectSpeedPenalty` (currently 50%)

### Known design issues
- [ ] **Tank frontal armor stalemate** — sim shows pen 50 vs 700 thickness = 7% dmg per hit. Either rework armor model or rebalance pen values.
- [~] **Buildings invisible / fog visibility model** — quick-fixed 260503 by short-circuiting `FrozenUnderFog.IsVisible` to `return true` (see TODO in `engine/OpenRA.Mods.Common/Traits/Modifiers/FrozenUnderFog.cs`). Symptom: buildings rendered invisible but still blocked sight; not clickable. Root cause appears to be the strict `IsVisibleInner` path landed in commit `2d7603bf` ("Fix buildings visible through fog") — `frozen.Visible` defaults to `true` and `state.IsVisible = !frozen.Visible` is `false` for all newly-spawned buildings, so they hide on first render before any sight pass. **Proper fix needed:** investigate `FrozenActor.Visible` initial state when shroud is off / map starts revealed; figure out whether buildings should ever go to fogged state at all in WW3MOD.
- [ ] **Visibility / fog design decisions for v1** — Open questions raised during garrison playtest:
  - Should buildings block line of sight at all? Old solution: only trees & static cover blocked sight. With buildings now indestructible (1 HP minimum), it might be fair for them to block — but hiding units behind a building is micro-intense and unintuitive, bad gameplay.
  - Should "fog" be a visibility *modifier* (weather-style, partial reduction) on top of shroud/sight, vs a binary "in fog or not"?
  - What lobby options ship with v1: just shroud/fog toggles, or richer fine-tuning (sight range modifiers, weather modes)?
  - **Decision needed before:** Phase A garrison playtest can fully complete; SR contestation depends on visibility working too.

---

## Phase B — Tier-1 Fixes

### Active bugs
- [T] **TECN capture order lost when shot at + panicking** — *Fixed 260504 (commit be46cde9)*: ScaredyCat.Panic() now snapshots Enter-derived task intents (Capture/Demolish/RideTransport/EnterAsCrew) as (target, orderString) tuples before CancelActivity. After PanicDuration expires, each is re-issued as a queued order so the unit returns to its original task. Note: same pattern exists in `InfantryStates.PanicTraitEnabled` (onfire-triggered); this fix only covers ScaredyCat — flag InfantryStates if the burning-engineer scenario surfaces.
- [T] **Helicopters evacuated near map edge bypass missile fire** — *Fixed 260504 (commit 98742d4e)*: aircraft RotateToEdge now targets a WPos `AircraftOffMapCells = 5` past the boundary instead of the edge cell. New `Aircraft.EvacuatingOffMap` flag suppresses the off-map "nudge to center" repulsion in GetRepulsionForce so Fly can carry the heli across the boundary without oscillating. Despawn fires only when truly past the edge (IsClearOfMapEdge). Removed obsolete IsOnMapEdge helper.
- [T] **Shift+G on attack-ground orders converts them to move orders** — *Round 2 fix 260504 (commit dd6cc18f)*: Round 1 (539d5ea3) caught Attack/AttackFollow.AttackActivity/AttackOmni.SetTarget but missed FlyAttack (returned by AttackAircraft.GetAttackActivity for helis/planes), and relied on a fragile TargetLineNodes fallback. Round 2 introduces `IAttackActivity { Target Target; bool ForceAttack }` marker interface implemented on all 4 attack activity classes via explicit interface members. GroupScatterHotkeyLogic checks `is IAttackActivity`, reads target directly, preserves ForceAttack vs Attack distinction. Future attack activity classes are auto-covered by implementing the interface.
- [ ] Artillery fires all ammo at once when critically damaged
- [T] **Artillery turret doesn't turn after stop** — *Verified 260508 via test harness*: Paladin's turret DOES rotate before firing (PASS). Sub-task: tuning — stop→turret-turn delay can be cut roughly in half; aim time after rotation is fine as-is. *Tuned 260508*: `m109` (Paladin) `AttackTurreted.SetupTicks` 50 → 25.
- [ ] **Artillery: manual targeting and force-attack-ground both broken** — *Reported 260508 via test harness, two-symptom set*:
  - Manual click on enemy vehicle (t90) shows the **move** cursor instead of the attack cursor — the targeter doesn't accept the t90 as a valid actor target. Auto-target on the same vehicle works (Paladin auto-engaged it earlier the same launch).
  - Ctrl+click force-attack on terrain accepts the order (red waypoint indicator appears) but Paladin never aims or fires on the point. Order received, never executed.
  - Paladin has `AttackTurreted.ForceFireIgnoresActors: True` and no `RequiresForceFire`. Suspects: (a) `ForceFireIgnoresActors: True` may be too aggressive — filtering out actor targets even on normal click; (b) `PersistentTargeting: False` overriding queued orders when an auto-target is already live; (c) HoldFireWhileMoving / SetupTicks path swallowing the order during setup countdown; (d) `NoSelfDefenseInterrupt` from the 260506 setup-aim work blocking new orders. Needs investigation in AttackTurreted.cs / AttackBase.cs along the "received order → activity execution" path.
- [ ] **Heavy artillery (Paladin) deliberately ignores infantry** — *Noted 260508*: by design via `^AutoTargetArtillery` (Heavy/Medium priorities only, no Infantry). Crew evacuating a destroyed vehicle is not auto-targeted. Decision needed: keep as-is (heavy shells shouldn't waste on lone soldiers), or add a low-priority Infantry entry so it picks them up when no vehicle target exists?
- [ ] **Some enemy soldiers untargetable (mutual)** — *Reported 260508*: Player couldn't target some enemy soldiers, and they couldn't target the player either. Likely a Targetable / TargetTypes mismatch or condition-gated targetability bug (possibly garrison-port directional, suppression-related, or a stale ITargetable state). Needs repro details — which unit type, what stance, were they near a garrison port?
- [ ] **Crew still ejects on vehicle death** — *Reported 260508*: User thought we shipped a fix to gate crew ejection on vehicle destruction, but crew is still ejecting. Check git history for the relevant commit (VehicleCrew / EjectOnHusk / suppress-eject condition) — may have been planned but not implemented, or implemented but regressed. Confirm before fixing.
- [T] **Aircraft can't spawn if waypoint is blocked** — *partially fixed 260505 alongside ground-unit fix below; aircraft branch never had the rally-path gate (lines 96-110), so this bug may have a separate cause. Worth re-testing now that ground production no longer stuck — if aircraft still get blocked, investigate candidate-cell occupancy / aircraft repulsion.*
- [T] **Ground unit production stuck at 100% / blocked until rally moved** — *Fixed 260505*: `ProductionFromMapEdge.Produce` was gating spawn on `pathFinder.PathExistsForLocomotor(centerCell, firstDest)`. If the rally-point waypoint was unreachable (water, walled, etc.) the candidate was discarded → `location = null` → `Produce` returned false → queue retried forever at 100%. User confirmed: changing rally point to a reachable cell unblocked production immediately. Removed the path-existence check from both the SpawnArea path and the legacy fallback. `move.MoveTo(..., evaluateNearestMovableCell: true)` (line 191) already handles unreachable destinations by routing to the nearest reachable cell — same behaviour as a manual move order. Bad rally points no longer block spawn; the unit just spawns and pathes as close as it can. *Reported 260503, again 260505*
- [ ] **Bridge pathing — units walk off the bridge** — infantry (and possibly vehicles) move outside the bridge footprint into water/shore cells. Likely cause: locomotor permits the shore/water cells flanking the bridge, OR bridge sprite art is wider than its passable footprint. `engine/OpenRA.Mods.Common/Traits/Buildings/Bridge.cs` + locomotor terrain weights. *Reported 260503, screenshot in conversation*
- [T] **Garrison: only first soldier of a batch enters** — Mitigation already in code (GarrisonManager.cs:198-208, 261-273): both `OnPassengerEntered` and `CheckOwnershipAfterExit` use `ChangeOwnerInPlace(updateGeneration: false)` specifically to keep in-flight Enter activities from other allied soldiers valid. Verify in next playtest — if bug is gone, flip to `[x]`. *Reported 260503*
- [T] **Stop order doesn't cancel garrisoned firing** — *Fixed 260504*: AttackGarrisoned now overrides OnStopOrder, calling new GarrisonManager.OnStopOrder which clears forceTarget, all PortState targets, PlayerOverride flags, and resets ambushTriggered. Matches Mobile-unit Stop semantics (cancel current intent, baseline behavior resumes per stance). Verify in playtest — note: with FireAtWill, soldiers will re-pick the same enemies on next scan tick (this is correct behavior; for permanent silence use HoldFire stance). *Reported 260503*
- [T] **Soldiers under fire abandon Enter-building order** — *Fixed 260504*: added `MoveToTargetRaw`/`MoveIntoTargetRaw` to IMove that bypass WrapMove. Enter uses raw variants on lines 108, 122. Side effect (intentional): Capture, Demolish, RideTransport, Infiltrate etc. all benefit — any "go to that thing and act" order stays focused. *Reported 260503*
- [ ] **Allied shared vision blinks rapidly (~3-4 Hz) for ~2s before stopping** — *Reported 260505, USA Abrams in process of dying, user playing allied 2-team — first time seen.* Static analysis on 260505 ruled out the most likely candidates without finding a cause: (a) Abrams Vision is NOT condition-gated — `^StandardVision` (defaults.yaml:21) defines `Vision@1..@10` with no `RequiresCondition`, and the Abrams pulls it via `^TrackedVehicle` → `^Vehicle` (vehicles.yaml:12). So `IsTraitDisabled` cannot toggle. (b) No `VisionModifier`/`RevealsShroudMultiplier` (the engine `IVisionModifier` impls) appear on any vehicle template — only on infantry (`^SuppressionVisionModifier`, damage-state mods at lines 989+) and aircraft (drone disable, operator lost contact). So `Range` cannot toggle either. The only two paths that would force `AffectsMapLayer.Tick` to call `UpdateCells` (Vision.cs / AffectsMapLayer.cs:120-142) are gated on these. (c) `EjectOnHusk` is *fully commented out* — VehicleCrew handles ejection, and `INotifyDamageStateChanged` only sets `ejecting=true/false` (a private field, not a vision-affecting condition). (d) No mechanic found that would flicker `self.Owner` on an un-garrisoned, un-temporary-owned Abrams during damage — `TemporaryOwnerManager` is one-shot, garrison ownership only applies when entering/exiting buildings. **Cannot reproduce from static analysis.** Per user request: don't guess a fix. Wait for the bug to recur and gather more info — ideally a replay (`-Replay` flag) or the exact moment screenshot. Specific things to note next time: was there an engineer or another tank attacking, was a medic/healer nearby (HP could've oscillated above/below 50%), was the Abrams stationary or moving, did it actually die or recover, and was anything else happening visually (e.g. a smoke effect on top). helicopter→helicopter missiles fly to the side and silently vanish at the impact point with no explosion, when they should `FlyStraightIfMiss` past the target. Probably target-loss bug or `FlyStraightIfMiss` not gated correctly. `engine/OpenRA.Mods.Common/Projectiles/Missile.cs`. *Reported 260503*
- [x] **Lobby category headers showed as broken boxes** — fixed 260503 in `engine/OpenRA.Mods.Common/Widgets/Logic/Lobby/LobbyOptionsLogic.cs`. Per user, dashes dropped entirely — labels now render as plain `INFANTRY`, `VEHICLES`, `AIRCRAFT`.
- [x] **C4 destroyed indestructible buildings** — fixed 260503 in `engine/OpenRA.Mods.Common/Traits/Demolishable.cs`. Root cause: `Demolishable.Tick` called `self.Kill` directly, bypassing `IDamageModifier`. Replaced with `health.InflictDamage(self, attacker, new Damage(health.HP, ...), false)` so GarrisonManager's clamp leaves indestructible buildings at 1 HP (rubble / damaged sprite). Normal Demolishables still die from HP-worth of damage. Side effect: avoids one trigger of the expensive shadow recalc.
- [ ] Helicopter husks on water don't sink
- [ ] ATGM units can't unload while shooting (attack lock)
- [ ] Parallel queues build paused units
- [ ] Walking sequence speed mismatches locomotor on different terrains
- [ ] Aircraft returns to base prematurely (with ammo remaining)
- [ ] Helicopter rearm blocked when helipad occupied
- [ ] Mobile sensor (CounterBatteryRadar) doesn't work
- [ ] Flying Fortress range hits short
- [ ] Units advance into minrange when attacking
- [ ] TECH/DR palette issue
- [ ] Projectiles disappear at north edge
- [ ] Russian scout helicopter husk on water (visuals remain)
- [ ] River Zeta: neutral SAM capturable, broken capturable building

### Drone fixes
- [ ] DR animations — prepare runs idle, drone launches before prep finishes
- [ ] Drone autotarget of other drones broken
- [ ] Anti-drone weapon too effective — freeze mid-air, fall when battery dies?
- [ ] Drone death needs crash animation

### Aircraft polish
- [ ] Edge spawn/leave for planes
- [ ] Helicopter landing refinement (slow before landing, faster turn to avoid overshoot)
- [ ] Apache shouldn't shoot guns at structures
- [ ] Ballistic missile tilt fix — Iskander/HIMARS missiles don't pitch properly on arc
- [ ] **Iskander/HIMARS shockwave radius too large** — *Reported 260508*: explosion shockwave is oversized for the warhead. Tune `ShockwaveDamageWarhead` Range / Falloff in the Iskander and HIMARS weapon definitions.

### Combat / suppression / bypass
- [ ] Suppression tuning — playtest vehicle values, per-weapon fine-tuning
- [ ] Bypass system refinement (ATGM tree handling, range-based hit chance)
- [ ] Flametrooper effective vs unarmored
- [ ] Units out of ammo reject attack orders (don't freeze aiming)
- [ ] **No-ammo units must reject attack-move + go idle if ammo runs out mid-attack-move** — *Reported 260508*: A unit with no ammo should not accept an attack-move order at all. If it's already executing attack-move and runs out of ammo, it should drop to idle (or seek resupply per Resupply stance) rather than continue charging into combat. Needs design pass — interaction with Resupply stances (Auto seek / Hold flag / Evacuate), whether the unit completes the move portion or stops in place, and how this propagates to mixed-unit groups (one unit dry shouldn't strand the rest).
- [ ] Shoot at last known location for stationary targets
- [ ] WGM should not fire if it won't hit
- [ ] **WGM (Bradley/BMP) loses track during normal flight** — *Reported 260508*: Wire-guided missiles often miss by multiple tiles, halfway to target, looking like the shooter died mid-flight — but it happens with the shooter alive and well, normal circumstances. Different from "shouldn't fire if it won't hit" — this is mid-flight tracking failure. Investigate `Missile.cs` guidance update path and whether `FlyStraightIfMiss` or target-loss logic is firing incorrectly. May be related to the helicopter→helicopter missile bug.
- [ ] Ballistics deprioritize targets if hit chance too low

### Supply Route
- [ ] Captured SR handling — what spawns link, neutral SRs between players
- [ ] Primary SR selection UI
- [ ] **Right-click own SR = Evacuate (regression)** — *Reported 260508*: After enabling click-to-attack-enemy-SR / click-to-defend-allied-SR, right-click on the player's own SR now reads as "defend our own flag" instead of "evacuate". Should restore the previous behavior: right-click own SR = evacuate order, with the "enter" mouse cursor. Likely an order-priority / cursor-resolution issue in SR-related order generators.
- [ ] **SR rally point should accept any order type (move / attack-move / force-attack / etc.)** — *Reported 260508*: Currently the SR waypoints work as plain move orders. We should be able to queue any order on the SR — defend, attack-move, force-attack, etc. — and have those orders transferred to the produced units when they spawn. Ideally the SR accepts any order a unit would accept, and acts as a proxy that re-issues the order to each new reinforcement. Touches: ProductionFromMapEdge spawn handoff, rally-point UI / order parsing, and how queued orders are stored/applied per unit.

### AI
- [ ] AI builds Logistics Centers, rearms
- [ ] AI conscripts don't abandon capture for squad orders
- [ ] AI stops firing at buildings marked for capture
- [ ] AI garrisons defense buildings
- [ ] AI uses attack-move for aircraft

### Misc gameplay
- [ ] Disable Tesla Trooper & futuristic units (small task)
- [ ] Helicopter force-land tuning + crew bloat fix + crew vehicle re-entry testing

---

## Phase C — Polish

### Sounds (the big gap)
- [ ] Unit firing sounds
- [ ] Explosion sounds
- [ ] Unit voice responses

### Visuals
- [ ] Unit icons
- [ ] Per-unit rot/bleedout sprites (currently uses generic e1)
- [ ] Unit description box sizing

### Open development threads
- [ ] **Garrison Phase 4** — sidebar icon panel rewrite
- [ ] **Cargo Phase 3** — template sidebar for pre-loaded transport purchasing

### Performance pass
- [ ] Pre-release perf pass (see "Pending decisions" → Performance pass for approach)
- [ ] **6-player skirmish slow on MacBook** — *Reported 260508*: User tried a 6-player skirmish on macbook, ran very slowly. Bigger investigation — we've already done some perf work, so **first step: read git history for prior perf changes (shadow-cache freeze, density layer, AI tick budgets, etc.) so we don't go in circles**. Then add logging/profiling to find current bottlenecks. Aligns with the "Performance pass before v1" pending decision (B + C: tick-budget log channel + dotnet-trace flame graph).

---

## Pending decisions

> Items raised during work that need a "yes / no / defer" call before they're scoped into v1 or sent to backlog.

- [x] **Buildings & line of sight in v1** — RESOLVED 260503: buildings no longer block line of sight, and the runtime shadow recalc cannot fire from any path. Done in three layers: (1) `Density: 100` removed from `^BasicBuilding` so no building actor contributes to `DensityLayer`. (2) The `QueueShadowUpdate / UpdateDensityForBuilding` calls in `Building.AddedToWorld / RemovedFromWorld` are commented out, and the `Map.FlushPendingShadowUpdates()` call in `World.Tick` is commented out — even if some other actor with Density were to be added/removed mid-game, nothing would queue or process. (3) The four engine methods (`QueueShadowUpdate`, `UpdateShadowForCells`, `FlushPendingShadowUpdates`, `UpdateDensityForBuilding`) are tagged `CURRENTLY UNUSED (260503)` in their docstrings so they're discoverable if dynamic density work resumes. Shadow data is now built once at map load (via `SetShadowLayer` fallback when `shadows.bin` is absent, or read from cache) and frozen for the entire game. Trees / rocks / ice / tank traps keep their density values and still block sight via the static cache. The two existing `shadows.bin` files were deleted so they regenerate fresh on next load with only static decorations contributing.
- [decision] **Fog richness in v1** — ship just shroud-on/off and fog-on/off lobby toggles, or invest in finer modes (weather fog, sight-range modifiers, per-faction sensor differences)? Probably v1 should be simple, richer modes go to v1.1.
- [decision] **Infantry self-defense baseline + AT soldier rebalance** — proposal from user 260503: every (or most) infantry should carry a basic firearm so they aren't helpless against other infantry. Specialists keep their specialist weapons but also have the firearm. Specifically: AT soldiers carry a rifle + 2 missiles (down from 3, to balance the firearm addition). Open questions before implementing: which specialists become hybrids vs which stay pure specialist? What's the damage/range gap between "real" riflemen and a hybrid's secondary firearm — is the hybrid's pistol/SMG meaningfully weaker so riflemen still have a role? Do engineers/medics also get a sidearm, or stay defenseless (gameplay risk-vs-realism tradeoff)? Does this change AI compositions?
- [decision] **Playtest session logging (developer mode)** — current logs are startup warnings only (`debug.log`, `perf.log`, `client.log` total ~10 KB after a session, none of it useful for following gameplay). Proposal: add a "Developer Logging" lobby/settings checkbox that opens a `gameplay.log` channel and instruments key events: player orders (build/move/attack/cancel), production state changes (queued/started/completed/failed/blocked with reason), unit lifecycle (spawn/death/capture), and a per-tick frame budget summary. Lets me read the file post-session and reconstruct what happened. **Decide:** ship in v1 (so I can keep using it through release), or build it, use it during dev, gate it as dev-only and not ship in v1?
- [decision] **Performance pass before v1** — options: A) you run VS profiler like before (thorough, slow), B) I add a tick-budget log channel (cheap, gives me data to read offline, less detail than profiler), C) `dotnet-trace` + PerfView snapshot during a heavy battle (free, gives flame graphs without VS), D) all three. Recommendation: B + C — B during normal play to spot regressions, C for the deep dive once. VS only if B/C miss something.
- [decision] **Garrison entry flow + visuals** — current behavior: soldier walks to *center* of building, plays prone animation on top of the roof, ~1s later pip appears at the bottom and sprite hides. User wants: (a) consider soldier "inside" once they reach the building footprint (not the center); (b) visual feedback on the transfer (building flash or similar); (c) the green chevron / protection % overlay redesigned — replace with vehicle-health-style pips, where building damage state ↔ protection level granted to occupants. Touches `EnterGarrison` activity, `GarrisonManager`, `WithGarrisonDecoration`. Needs design pass before implementing.
- [decision] **Targeting code review session** — user wrote custom advanced targeting that scores all candidates by type/distance/specialist priority (e.g. snipers prefer high-value), some AI-made changes since. Not broken, but worth a dedicated session to walk through scenarios end-to-end and possibly restructure. **Decide:** schedule a session in v1, or defer to v1.1 polish.
- [decision] **Helicopter formation flying ("flock-style")** — *Raised 260504*: when multiple helicopters move to the same point, they all aim for the exact same cell and end up jostling under their `Repulsable` repulsion field — looks janky. Idea: when N helis are within group radius and given the same destination, treat the group as a formation — they fly parallel offsets and turn together (like a flock). Conditional: only when "close to others heading to the same point". Many cases where it can't apply (different destinations, mixed unit types, single heli) so it must gate cleanly. Implementation sketch: a group-formation modifier akin to `CohesionMoveModifier` that distributes the destination across helicopters with formation offsets, perpendicular to the move heading. **Decide:** v1 polish vs v1.1 — touches `Aircraft.cs` movement and probably needs a new trait for formation membership/leader tracking. Probably v1.1 unless the jostling is bad enough to count as a v1 blocker.
- [decision] **Shadow / visibility recalc cost vs. dynamic obstacles** — destroying buildings or trees triggers a full shadow/visibility recalc that causes noticeable lag (~1 sec). User raised: ideally damaged buildings (and trees) would have *reduced* density / partial visibility cost rather than binary block-or-not, but the recalc is too expensive to run mid-game. **Three branches:** A) drop "buildings/trees affect visibility" entirely (eliminates the perf concern AND the user's "hiding behind buildings is bad gameplay" concern in one move) · B) keep static-only (current static caching, accept that destroyed buildings keep blocking sight, document as known limit) · C) optimize the recalc (incremental, partial, deferred) so dynamic density becomes feasible. C is the most expensive engineering work. Decision intersects with the visibility-design pending decisions above. C4 indestructibility fix already removes one major recalc trigger.

---

## Deferred to v1.1 / Won't fix v1

- [v1.1] Per-Supply-Route production queues (needs engine changes)
- [v1.1] Ukraine as third faction
- [v1.1] Ammo costs money (full economy rework)
- [v1.1] Tier 2 hotkey overhaul (Alt/Ctrl modifier polish)
- [v1.1] Lobby option dropdowns (army upkeep, kill bounties, short-game threshold)
- [v1.1] Map editor improvements (more civilian structures, road tiles)
- [v1.1] Engine upgrade to release-20250330 (12–22 sessions)
- [v1.1] River Zeta shellmap overhaul
- [v1.1] Unit description overhaul & auto-generated stats
- [v1.1] Rename tech levels to "DEFCON"
- [v1.1] Move widgets to edges, free up UI space

---

## Recently completed

> Items move here as they ship. Keep the most recent ~10; older entries fall off.

- [x] **Shroud OFF by default** (260503) — `ExploredMapCheckboxEnabled: true` in `mods/ww3mod/rules/player.yaml`
