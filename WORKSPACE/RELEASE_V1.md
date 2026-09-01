# WW3MOD v1 Release Tracker

> Single source of truth for v1 scope. Update continuously as items are tested, fixed, deferred, or cut.
>
> **Status legend:** `[ ]` open · `[~]` in-progress · `[T]` testing · `[T:trusted]` code-verified spot-check (fix is in the tree, no contradicting later commit; not yet AUTOTEST-confirmed) · `! [T]` urgent + testing · `[v1.1]` deferred · `[cut]` won't-fix v1
>
> **Scope is locked.** New features need explicit "yes, add to v1" from the user. Otherwise → `BACKLOG.md` or `Pending decisions` below.
>
> **Items pass AUTOTEST or playtest → removed entirely.** Commit history is the archive. No `[x]` graveyard, no "Recently completed" section.

## Phase

**Currently in: Phase A — Stabilize**

- **Phase 0 — Tooling** — autotester / harness friction that speeds up other tasks
- **Phase A — Stabilize** — get every "needs playtesting" system verified or fixed. No new features.
- **Phase B — Tier-1 fixes** — bugs and gameplay gaps that block release.
- **Phase C — Polish** — sounds, icons, descriptions, open polish threads.

---

## Phase 0 — Tooling

- [ ] **Automation workflow track (260513)** — multi-phase plan in [`WORKSPACE/automation/README.md`](automation/README.md) covering focus-steal fix, test lanes, visual feedback, autonomous queue, overnight orchestrator, notifications, agent status board. Read it first; tracker items below are subsumed
- [ ] **Autotester launches focused, interrupting work in another window** — flash on launch steals focus when I'm typing in another window. Want: launch minimized so it doesn't pull focus. *(Subsumed by automation Phase 0)*
- [ ] **Autotester launch position should follow current terminal, not be fixed-left** — earlier attempt landed on "opposite side of focused window" but I'm often jumping between windows when it spawns. Best behaviour TBD: per-session override + default to opposite-of-active-terminal? Discuss before implementing — come with your thoughts

---

## Phase A — Stabilize

### Big systems
- [T] **Garrison overhaul** (Phases 1–6) — indestructible buildings, dynamic ownership, directional targeting, suppression integration, visuals
- [ ] **Cargo system** (Phases 2A–E) — TRUK auto-rearm, mark+unload, rally points, supply drop, merge
- [ ] **Helicopter crash + crew overhaul** — critical=total loss, safe land=neutral+repairable, capture-by-pilot-entry
- [T:trusted] **Stance rework** (4 phases) — modifiers (Click/Ctrl/Ctrl+Alt/Alt), resupply behavior, cohesion, patrol. **CORRECTED 2026-09-01 — this was `[ ]` open and that was wrong in the expensive direction.** All four selector logics are wired (`ingame-player.yaml:335, 404, 473, 542`); the four modifier axes are real branches, not tooltip text (`StanceSelectorLogic.cs:54-59`: Ctrl+Alt → `SetTypeDefault`, Alt → `DoNow`, Ctrl → `SetUnitDefault`, else plain set); the backing state lives on `AutoTarget` (`:22, :28`, order handlers `:578-587`) and reaches shipped actors through `^AutoTarget` (`defaults.yaml:388-391`); patrol is end-to-end (button `ingame-player.yaml:214-231` → `game.yaml:162` → `CommandBarLogic.cs:202-218` → `PatrolOrderGenerator`). **Not release work.** One deliberate carve-out worth knowing: F16 alone sets `AutoTarget: EnableStances: false` (`aircraft-america.yaml:600`).
- [~] **AI overhaul** — foundation doc 260511 (`WORKSPACE/ai/foundation_260511.md`) — 3-layer brain (Perception/Strategy/Tactics), 5 phases. Tiers 0–3.1 from 260321 stay as executors. **Tournament harness shipped overnight 260511→260512 (~20 commits prefixed `ai: tournament`)**: dual `ModularBot@normal/@v2`, IMatchScorer/IWinRuleEvaluator plug-ins, full score formula (army+kills+income), deterministic seeds, 8× SpeedMultiplier, framerate cap, mirror-paired benchmarks, autonomous loop scaffold. **READ FIRST:** `WORKSPACE/ai/WAKEUP_CHECKLIST_260512.md`. Phase 2+ AI brain work is now underway in the @experimental track (autoburn loop — `WORKSPACE/ai-bench/` + `PIPELINE.md`): strategic/tactical split, unit-role resolver, and influence stack Stages 0/A/B/C shipped (260719–260722)
- [T:trusted] **Supply Route contestation** — graduated control bar, production slowdown, notifications. **CORRECTED 2026-09-01 — LIVE AND COMPLETE; all three parts ship and are reached.** Trait applied at `structures.yaml:303` inside `SUPPLYROUTE:` (opens `:222`); bar via `ISelectionBar, IAlwaysVisibleBar` (`SupplyRouteContestation.cs:126`, value at `:876`); slowdown via `IProductionSpeedModifier` (`:860`, tapering on `SlowdownThreshold`, set to 50 at `structures.yaml:314`); notifications at `:518-520, :543-545, :580-586, :847-854`. The actor is reachable via `StartingUnits` (`world.yaml:450`ff, `BaseActor: supplyroute`) — its `Buildable.Prerequisites: ~disabled` gates only the build sidebar, as the game model intends. **Its one live defect is a *wording* bug, not a mechanism gap** — see PIPELINE R9.
- [~] **Three-mode move system** — Move/Attack-Move/Force-Move, SmartMove wrapping. **CORRECTED 2026-09-01 — the three modes are LIVE; the "SmartMove wrapping" half is INFANTRY-ONLY, which is why this is `[~]` and not `[T:trusted]`.** Plain `Move` and `ForceMove` both issue from `Mobile.cs:1071` (resolved `:1093`, `:1125`), force modifier at `:1209, :1225-1230`, hotkey `ForceMove: V` (`game.yaml:177`); Attack-Move has its button (`ingame-player.yaml:93-100`) and the `AttackMove` trait on the shared templates (`defaults.yaml:403, 655, 743, 765`). **But `SmartMove:` appears in shipped rules exactly once — `infantry.yaml:54`, under `^Infantry:`.** Vehicles, aircraft and naval do not carry it. **Do not read this line as "done" for vehicles.**
- [ ] **Vehicle crew system** — slot ejection, re-entry, commander substitution
- [ ] **Infantry mid-cell redirect** — tune `RedirectSpeedPenalty` (currently 50%)

### Supply & ammo economy
- [T] **Supply & ammo economy overhaul** (260506, plan: `WORKSPACE/archive/plans/260506_supply_ammo_economy.md`) — P1–P3 shipped (15 commits):
  - **P1:** empty-truck refund deduction; LC `Range 3c0→2c0` + `unit.docked` gate; Ctrl+click = deliver, default = repair+refill via new `RefillFromHost`/`Restock`. Tests in `CargoSupplyEconomyTest.cs`
  - **P2:** new `IProvideTooltipDescription` interface; `AmmoPoolInfo` adds weapon block + grand-total to production tooltip
  - **P3:** ~63 AmmoPools across 9 YAMLs given explicit `SupplyValue`/`CreditValue` per tier table (T0=1 → T9=1500)
  - **Verify:** empty TRUK refund = 250; cannot refill within 3c0 unless docked at 2c0; right-click LC behaviour; multi-pool tooltip; tier-cost feel
- [ ] **Verify unit sell value at different ammo levels** — broader than the TRUK refund check above. Spent ammo should be deducted from cashback at evac for ALL units (tanks, infantry with reload). Sweep

### Active items in flight
- ! [ ] **Dropped supply cache: real supply actor + destructible** — current cache from TRUK deploy doesn't act as a supply actor with its own bar, and may be indestructible. Should be very destructible (large explosion on death, size scaling with remaining supplies), targetable by other supply trucks to replenish, possibly auto-replenish via stance on the cache. Needs design discussion before code. (Underlying TRUK deploy → drop cache shipped 260504, commit b3699b63.)
  - _**Re-verified STILL OPEN 2026-09-01 (`main @ bd8e7290`).** This is PIPELINE **R12**, and the mechanism is unchanged: `DropsSupplyCache.cs` gates on the target carrying `AbsorbsSupplyCache` — the drop gate at **`:321`** and the direction-resolution gate at **`:693`** (both cites have drifted; a 2026-08-19 note said `:300` and `:698`). **Exactly one actor in the whole mod has that trait** — the logistics centre, `structures.yaml:560` inside `LOGISTICSCENTER:` (opens `:392`), `Range: 2c512, TransferRate: 50`. **On the maps with no Logistics Centre a truck is the only route by which ground supply returns, so the economy has a hole in it**; the player gets no cursor and no order, and nothing explains why._
  - _**Two in-tree comments still carry the old `structures.yaml:418` cite** — `CrateProximityCaptureTest.cs:13` and `misc.yaml:424`. Pre-existing drift, not corrected here; fix them if you are in those files anyway._
- [ ] **Supply truck → building = transfer supplies** *(new feature, not started)* — building gains supply bar; soldiers inside/nearby drain it
- [ ] **Vehicle off-map evac flight (extension of heli fix)** — same off-map-fly-before-sold treatment for vehicles, shorter distance. Past the boundary: targetable but unselectable. Goal: prevent border-camp evac that dodges incoming fire, plus better visuals than vanishing at edge tile
- [ ] **Littlebird rotor still spins after safe landing** — needs investigation (sweep all helis)
- [T:trusted] **Iskander/HIMARS shockwave radius too large** — tuned 260509 (commit 9578557c). `MaxRadius` values verified in `weapons-explosions.yaml`: Iskander 4c0 (line 495), HIMARS 2c512 (line 532). Feel needs human eye in next playtest

### Known design issues
- [~] **Buildings invisible / fog visibility model** — quick-fix 260503: `FrozenUnderFog.IsVisible` short-circuits to `return true`. Proper fix: investigate `FrozenActor.Visible` initial state and whether buildings should fog at all in WW3MOD
- [ ] **Visibility / fog design decisions for v1** — open questions raised during garrison playtest:
  - Should buildings block line of sight at all? Old solution: only trees & static cover. Hiding behind a building is micro-intense and unintuitive — bad gameplay
  - Should "fog" be a visibility *modifier* (weather-style, partial) on top of shroud/sight, or binary?
  - What lobby options ship with v1: just toggles, or richer fine-tuning (sight range modifiers, weather modes)?
  - **Decision needed before:** Phase A garrison playtest can fully complete; SR contestation depends on visibility too

---

## Phase B — Tier-1 Fixes

### Active bugs
- [ ] **Heavy artillery deliberately ignores infantry** *(noted 260508)* — by design via `^AutoTargetArtillery`. Decision: add low-priority Infantry, or keep heavy-only?
- [ ] **Some enemy soldiers untargetable (mutual)** *(reported 260508)* — needs repro: unit type, stance, near garrison port?
- [ ] **Bridge pathing — units walk off the bridge** — *Investigated 260509 (read-only):* `Bridge.cs:158,322` correctly overrides `Map.CustomTerrain[c]` for footprint cells, so the bridge cells DO get `Bridge` terrain type. But the `foot` locomotor (`world.yaml:28-42`) permits `Beach: 80`, `RiverShallow: 40`, `Shallow: 30` — so infantry can legally walk along the shore *next* to a bridge. Pathfinder cost is inverse of speed, and `Beach: 80` vs `Bridge: 100` is a ~25% penalty per cell — small enough that even a 1-2 cell shortcut along the beach can beat going across the bridge. Likely fixes: (a) reduce `Beach`/`Shallow` passability for `foot` (breaks beach landings), (b) widen bridge footprint to cover the shore approach cells, (c) add a per-bridge guide cell that pulls paths onto the deck. Vehicles may have the same issue; check `wheeled`/`tracked` locomotor speeds for shore terrains
- [ ] **Allied shared vision blinks rapidly (~3-4 Hz) for ~2s** *(reported 260505, USA Abrams dying, allied team)* — static analysis ruled out condition-gated Vision, VisionModifiers, EjectOnHusk, owner flicker. Cannot reproduce. Wait for recurrence — note attacker, healer presence, HP%, motion, replay if possible
- [T:trusted] **Helicopter→helicopter missiles silently vanish on impact** — fixed 260510. The 260509 airburst-gate diagnosis was directionally right but not the active path for Hellfire (`AirburstAltitude=0` so the airburst trigger never fired). Real root cause: heli HitShape is Circle Radius 32, and Hellfire's `PerCellIncrement` Inaccuracy 16 puts a 22-cell shot ~100 wdist off centre on average — well outside `TargetDamage`'s 1-wdist default Spread, so the missile only ever delivered SpreadDamage falloff. With default `Penetration=1` vs Heavy heli `Thickness=20` that damage gets divided by 20, leaving 5-50 damage per missile (the "vanish"). Fix combo: (a) `Missile.cs:1067` mid-tick segment-aim-point proximity check so fast missiles (Speed > CloseEnough) don't fly past target between ticks; (b) `Missile.cs:1059` airburst gate on `!flyStraight` (per the 260509 diagnosis — clean even if not the active path here); (c) Hellfire `Warhead@Spread.Penetration: 1→20` so SpreadDamage actually penetrates Heavy heli armor. Result: Apache one-shots a Mi-28 at 22 cells (autotest `test-heli-vs-heli-missile`). All other missile autotests still pass
- [ ] Helicopter husks on water don't sink
- [ ] ATGM units can't unload while shooting (attack lock)
- [ ] Walking sequence speed mismatches locomotor on different terrains
- [ ] **Mobile sensor (CounterBatteryRadar) doesn't work** — *Investigated 260509 (read-only):* the wiring chain looks complete: MSAR has `CounterBatteryRadar: Range: 42c0, RequiresCondition: deployed` (`vehicles.yaml:352`); Paladin has `Detectable.CounterBatteryRadar: 1, CounterBatteryRadarDetectableCondition: firing` + `GrantConditionOnPreparingAttack: Condition: firing, RevokeDelay: 100` (`vehicles-america.yaml:585-598`); `Detectable.cs:110,115` consults the layer; `MapLayersExts.AnyVisibleOnCounterBatteryRadar` exists. So mechanically it should fire when (a) MSAR is deployed, (b) Paladin is in MSAR's 42c0 range, (c) Paladin is firing or within 100 ticks of a shot. Likely "doesn't work" reasons: user testing without deploying the MSAR, the 4-second reveal window too short for any UI feedback, or no audio/icon cue so the player doesn't realize it briefly revealed. Needs reproduction with deployed MSAR + active enemy artillery to confirm
- [ ] **River Zeta: neutral SAM** — always invisible (probably deprecated Cloak trait). Should have low visibility (Cloak replacement) but not invisible, and capturable by technician. Plus broken capturable building elsewhere on the map

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

### Combat / suppression / bypass
- [ ] Suppression tuning — playtest vehicle values, per-weapon fine-tuning
- [ ] Flametrooper effective vs unarmored
- [ ] Units out of ammo reject attack orders (don't freeze aiming)
- [ ] **No-ammo units must reject attack-move + go idle if ammo runs out mid-attack-move** *(reported 260508)* — needs design pass: interaction with Resupply stances, whether to complete move or stop in place, mixed-group handling
- [ ] Shoot at last known location for stationary targets
- [ ] Ballistics deprioritize targets if hit chance too low

### Supply Route
- [ ] Captured SR handling — what spawns link, neutral SRs between players
- [ ] Primary SR selection UI

### AI
- [ ] AI builds Logistics Centers, rearms
  - _**SCOPE NARROWED — the "rearms" half is answered by a user ruling, and the answer is NOT what an earlier attempt assumed.** A branch wired all seven airframes to the `logisticscenter` as a rearm host and was **REVERTED** (`68e8b885`). The ruling: **airplanes rearm at the AIRFIELD (`afld`), helicopters at the HELIPAD (`hpad`).** Verified 2026-09-01 at `bd8e7290`: `RearmActors: hpad` on littlebird (`aircraft-america.yaml:235`), HELI (`:398`), HIND (`aircraft-russia.yaml:233`), MI28 (`:418`); `afld` on A10 (`aircraft-america.yaml:533`), FROG (`aircraft-russia.yaml:577`), MIG (`:676`). TRAN, HALO and F16 have no `Rearmable`; the two `.Airstrike` variants strip it._
  - _**Both hosts are unbuildable and unplaced, so "AI builds Logistics Centers" cannot be extended to "AI builds rearm hosts" — there is nothing to build.** `Buildable.Prerequisites: ~disabled, ~techlevel.medium` on HPAD (`structures.yaml:596`, opens `:580`) and AFLD (`:664`, opens `:648`), and **no shipped map places either** (grepped all ten dirs under `mods/ww3mod/maps/`; maps store actors as readable text, so the grep is valid). **Do not re-propose the logistics-centre approach.**_
  - _**⚠️ CORRECTION to the shorthand that has been circulating: "if neither host exists they EVACUATE" is TRUE FOR HELICOPTERS ONLY.** `EvacuateWhenUnrearmable:` appears exactly once in `mods/` — `aircraft.yaml:195`, under `^Helicopter:` (opens `:160`). `^Helicopter` inherits `^Airborne`, **not** `^Aircraft`. **A10, FROG and MIG all inherit `^Aircraft`, carry `RearmActors: afld`, and have no evacuation fallback at all** — with no `afld` on any map, a spent plane neither rearms nor evacuates. That gap is the live part of this line._
  - _**And the trait excludes bots** — `IncludeBotOwners = false` (`Traits/Air/EvacuateWhenUnrearmable.cs:28`, enforced `:44`), set to `true` by no YAML anywhere. So even the helicopter behaviour is player-side only. Note the path is `Traits/Air/`, not `Traits/`._
- [ ] AI conscripts don't abandon capture for squad orders
- [ ] AI stops firing at buildings marked for capture
- [ ] AI garrisons defense buildings
- [ ] AI uses attack-move for aircraft

### Misc gameplay
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
- [ ] **6-player skirmish slow on MacBook** *(reported 260508)* — first step: read git history for prior perf work (shadow-cache freeze, density layer, AI tick budgets) before re-investigating. Then profile

---

## Pending decisions

> Items raised during work that need a "yes / no / defer" call before they're scoped into v1 or sent to backlog.

- [decision] **Fog richness in v1** — ship just shroud/fog toggles, or invest in weather fog / sight-range modifiers / per-faction sensors? Lean simple for v1, richer goes v1.1
- [decision] **Infantry self-defense baseline + AT soldier rebalance** *(260503)* — give most infantry a basic firearm; AT soldiers rifle + 2 missiles (down from 3). Open: which specialists become hybrids? Sidearm damage gap? Engineers/medics? AI comp impact?
- [decision] **Playtest session logging (developer mode)** — proposal: lobby checkbox opens `gameplay.log` channel for orders, production state changes, unit lifecycle, per-tick frame budget. Decide: ship in v1 or dev-only
- [decision] **Performance pass before v1** — A) VS profiler (thorough), B) tick-budget log channel, C) `dotnet-trace` + PerfView. Recommend B+C; VS only if those miss it
- [decision] **Garrison entry flow + visuals** — wants: (a) "inside" on footprint not center; (b) transfer flash; ~~(c) replace green chevron with vehicle-health-style pips tied to damage state~~. Touches ~~`EnterGarrison`~~, `GarrisonManager`, `WithGarrisonDecoration`. Needs design pass
  - _**CORRECTED 2026-08-19 at `de78a1ed`. Two of the three strikes above matter to whoever picks this up.**_
  - _**`EnterGarrison` does not exist and never did.** A grep across `engine/` and `mods/` returns **zero** hits — every occurrence in the repo is in `WORKSPACE/` prose citing this line or each other. Garrison entry runs through the **stock `Enter`/`EnterTransport` path**: the bot issues `new Order("EnterTransport", …)` against the building actor (`GarrisonBotModule.cs:331`, with the comment at `:327` saying so explicitly). There is no bespoke entry activity to open — **scoping (a) or (b) against a phantom file will mislead whoever picks this up.** First flagged at `audit/260816-systems-completeness.md:240`; struck here so the wrong pointer stops travelling._
  - _**Ask (c) is substantially DELIVERED**, so do not re-scope it as open. `WithGarrisonDecoration` now renders a four-row pip grid — `SlotRows = 4` with `DamageRow`, `ClassRow`, `AmmoRow` and a `SuppressionRow` added at `97414046` (`WithGarrisonDecoration.cs:84-88`) — which is the "health-style pips" shape the ask describes, extended past damage state to ammo and suppression. **Its honest residue:** the ten suppression frames are one chevron in ten hues, so the grid conveys severity but not trend (`cargo-garrison-status-260819.md` §4-A7), and **the shipped grid has never been screenshotted** — commit `97414046` says so in its own message._
  - _**Still genuinely open:** (a) footprint-vs-center placement and (b) the transfer flash. Neither was touched by the suppression work._
- [decision] **Targeting code review session** — custom scoring (type/distance/specialist) with AI-era edits since. Not broken but worth a walkthrough. Schedule in v1 or defer to v1.1?
- [decision] **Helicopter formation flying ("flock-style")** *(260504)* — same-destination helis jostle under `Repulsable`. Sketch: group-formation modifier akin to `CohesionMoveModifier` distributing perpendicular offsets. Probably v1.1 unless blocker
- [decision] **Shadow / visibility recalc cost vs. dynamic obstacles** — branches: A) drop buildings/trees from visibility entirely, B) keep static-only (current), C) optimize recalc (incremental/deferred). C is the expensive path. Intersects fog/visibility decisions above

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
- [v1.1] Airstrike support powers (A-10, Su-25) — hidden for v1. `AirstrikePower` + lobby option commented out in `player.yaml`/`world.yaml`; A10/FROG actor defs left orphan. Re-enable by uncommenting; needs balance pass
