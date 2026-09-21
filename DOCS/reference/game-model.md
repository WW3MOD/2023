# How WW3MOD Differs from Red Alert (game model)

WW3MOD is NOT Red Alert with new sprites. The entire gameplay model is different. **Do not assume any Red Alert mechanic still applies.** This doc is the full mental model; the hard rules are summarized in `CLAUDE.md`.

## Reinforcement Model (no factories)

There are **no Construction Yards, Barracks, War Factories, or Naval Yards**. Units are NOT "built" — they are **called in as reinforcements from off-map reserves** via the **Supply Route** building. Think of the Supply Route as a radio/logistics hub that requests reinforcements, not a factory that manufactures units.

- **The Supply Route is a fixed sector beachhead, NOT a buildable factory.** Every player **starts with exactly one** SR, spawned near their map-edge spawn point. You do not build it, you do not choose where it sits, and you cannot build a second one. The intended way to get a second SR is to **capture** a neutral one placed by the mapmaker — but note SR capture is **not wired in the code yet** (`SUPPLYROUTE` has no `Capturable`/`CaptureManager`; details in [`supply-route.md`](supply-route.md)). An SR is **untargetable, not indestructible** — via `Targetable: TargetTypes: NoAutoTarget`, which makes it an invalid target for every warhead in the mod; it has a full `Health: HP: 75000`, and the `Armor: Indestructable` line is inert and protects nothing (see [`supply-route.md`](supply-route.md)). **A warhead that skips the target-type test therefore reaches it**, which is why `SUPPLYROUTE` carries an explicit `-Vaporizable:` against `VaporizeWarhead` (2026-09-08). Today an SR only leaves a player's hands by being **contested down** or when that player is **defeated** (`OwnerLostAction` flips it Neutral). **Read [`supply-route.md`](supply-route.md) before any AI/strategic-layer design touching SRs** — it's the canonical mental model and the trap that keeps recurring.
- **Supply Route is the single core production building.** It produces ALL unit types (infantry, vehicles, aircraft) via `ProductionFromMapEdge` — units spawn at the map edge nearest the SR and march/fly to the rally point.
- **Buildings and defenses** are the exception — they spawn locally at the Supply Route via a separate `Production@Local` queue.
- **HPAD (Helipad)** and **AFLD (Airfield)** are **rearm/repair support buildings**, not production prerequisites. Helicopters and planes CAN be produced without them. HPADs/AFLDs let aircraft rearm faster on-map instead of flying back to the map edge. Future plans include capturable HPADs on maps.
- **"Buying" a unit** = calling in a reinforcement from reserves. **"Rotating out" a unit** = sending it back to the map edge to recover its budget cost. This is the economy loop.
- **Unit costs represent budget allocation**, not manufacturing cost. A destroyed unit is a permanent loss of that budget.
- **The AI YAML lists `supplyroute` under `ConstructionYardTypes` / `VehiclesFactoryTypes` / `BarracksTypes`.** That's OpenRA-trait integration so the production queues wire up — it is **not** a statement that the SR is a factory in the strategic sense. Any strategic-planner code that reads "the AI has a ConstructionYard" must treat that as the player's sector beachhead, not as an expandable industrial base.

## No tech tree / building prerequisites

There is no "build barracks → build war factory → build radar → unlock X" progression. Tech levels exist (`~techlevel.low/medium/high`) but they are granted automatically based on game time or other conditions, not by constructing specific buildings. Any unit the player's tech level allows can be called in immediately.

## Map-edge spawning

Units don't appear at the production building — they enter from a map edge, then walk/fly across the map to the rally point.

**Which edge is not reliably the Supply Route's.** *(Corrected 2026-09-02 — the previous unconditional "nearest the Supply Route's SpawnArea hint" was wrong on 9 of the 10 shipped maps.)* The SR anchors the choice only when the map authors a `spawnarea` actor: `FindClosestSpawnAreaForOwner` scans `ActorsWithTrait<SpawnArea>()` and returns null when there are none (`Activities/RotateToEdge.cs:101-109`), at which point the caller falls back to `self.Location` — the **unit's own cell** (`:163-168`). Only `river-zeta-ww3` ships `spawnarea` actors (6, one per `mpspawn`); the other nine maps have zero, as do most autotest scenarios *(counted 2026-09-02)*. So on almost every map both reinforcement entry and evacuation resolve from wherever the actor happens to be standing, **not** from the SR. Before asserting "it heads toward the SR", run `grep -c spawnarea` on the map. The anchor mechanism itself is documented in [`economy.md` §"The evacuation anchor is the `spawnarea` actor"](economy.md).

This means:

- Production has inherent travel time (units have to traverse from edge to SR rally before being usable)
- Enemy can ambush reinforcements en route — and the same is true in reverse: your AI/units can ambush the enemy's reinforcement lane
- SR position is **fixed near the player's spawn edge** by the map — it isn't a player decision. The only positioning lever the player has on their own SR is the rally point, which determines where units muster *after* arriving

## Engine code still has old RA patterns

Many engine files still contain classic RA assumptions (e.g., `HasAdequateAirUnitReloadBuildings` checking for 1 airpad per aircraft). When you encounter these patterns, understand they may not apply. Always check how WW3MOD actually uses the system before assuming the old logic is correct. The `SkipRearmBuildingCheck` YAML property on `UnitBuilderBotModule` was added specifically to bypass one such legacy check.

## Capturing neutral buildings consumes the technician

Neutral income/tech buildings (derricks, etc.) are taken by technicians (TECN) via the `^CapturesNeutralBuildings` template (`infantry.yaml:956`), which sets **`ConsumedByCapture: true`** (`infantry.yaml:962`). A *successful* capture removes the TECN from the game — so the technician pool is a **consumable**, not a persistent squad: it shrinks by one on every capture success, on top of every combat loss. With a unit limit like `tecn: 3`, capturing two or three buildings can exhaust the live pool until production replaces it, at which point no further captures are possible. For any capture-focused AI, availability of technicians — not coordinator logic — is the binding constraint.

**Soldiers clear; only technicians own.** The rule is uniform across every capturable building — there is no per-building exception. Line infantry use `^CapturesOccupiedBuildings` (`infantry.yaml:934`), a single `Captures@OCCUPIED` trait on the `building-occupied` type, restricted to enemy-owned targets (`ValidRelationships: Enemy`, `:953`) and carrying `CaptureToNeutral: true` + `EnterBehaviour: Exit` (`:954-955`). A soldier walking into **any** enemy-held building drops it to **Neutral** instead of claiming it, then walks back out alive.

That covers all 23 capturable actors, defences and production included: `AFLD`, `AGUN`, `AMMOBOX1-3`, `BARL`, `BIO`, `BRL3`, `CRAM`, `CTFLAG`, `FCOM`, `FTUR`, `GUN`, `HGATE`, `HOSP`, `HPAD`, `HSAM`, `LOGISTICSCENTER`, `MISS`, `MSLO`, `OILB`, `SAM`, `VGATE`.

Ownership by capture is therefore **exclusively** the technician's, and a neutralised building — however it got that way — then needs a TECN walked into it like any other neutral. Both flags live on `CapturesInfo` (`Captures.cs`) and default to stock capture-and-be-consumed, so `^CapturesNeutralBuildings` is untouched and the technician is still consumed on every success.

**Taking an enemy building is TWO in-place ownership flips, with a Neutral state in between.** *(Promoted
2026-09-20 from DISCOVERIES, re-read at `a21583fd`.)* The soldier's clear runs `ChangeOwnerInPlaceSync` to
Neutral; the technician's capture runs it again from Neutral to the new owner. **Any analysis that models
capture as one flip is wrong, and anything that treats a `NonCombatant` owner as "cannot happen mid-match" is
wrong too.** The consequences of that path — which notifications fire, and which two channels it skips — are
in [`architecture.md` §"`ChangeOwnerInPlaceSync` skips the remove/re-add bracket"](architecture.md#changeownerinplacesync-skips-the-removere-add-bracket-so-the-absence-of-inotifyownerchanged-on-a-trait-is-the-bugs-signature-not-its-absolution).

Two related facts that are easy to get backwards:

- **`Capturable` itself carries NO relationship filter.** `Capturable.cs` declares only `Types`; every piece of
  gating — who may capture, from what relationship, to whom — lives on the `Captures` side. Reading the target
  actor to find out who can take it answers nothing.
- **A `Creeps`-owned actor may still be an ENEMY.** On `nuclear-winter-ww3`, `Creeps` is `NonCombatant: True`
  *and* `Enemies: Multi0, Multi1` (`map.yaml:25-29`): `NonCombatant` and neutral-relationship are independent
  properties. `UnitLifecycleLogger.IsInteresting` filters on the former, so a Creeps-owned structure is
  untracked at spawn — and because the in-place path fires no `ActorAdded`, it stays untracked after a player
  captures it.

> **User ruling, 2026-08-14:** *"This should be same for all buildings, a technician is always needed to capture anything, and a soldier can 'clear' it (turn neutral)."* This replaced a narrower rule shipped a day earlier that applied clearing only to `^TechBuilding` via a separate `building-occupied-tech` capture type; that type and the second `Captures` trait are gone.

> Correcting an earlier claim: this section used to state that soldiers were "*not* consumed". That was never true — `Captures.cs:41` defaults `ConsumedByCapture` to `true` and no override existed, so a soldier **was** removed from the game on a successful capture until these changes.

**Clearing has no unit cost, and that is a known live risk.** The soldier survives, so denial is repeatable and limited only by the `CaptureDelay`, which is **500 ticks = 30.0 s** (`infantry.yaml:948`). *(Corrected 2026-09-20: this line read `CaptureDelay: 1000` / 60 s, which was the value until it was halved on 2026-09-03 because clearing an oil derrick read as interminable. An earlier correction on 2026-08-27 had already replaced a "~40 s" figure that was RA-era 25-tps arithmetic on a mod running at 16.67.)* The delay is spent **standing adjacent, before the soldier enters** — `CaptureManager` gates entry on `currentTargetDelay >= CaptureDelay`, incremented once per tick from `Enter`'s Approaching state — so it is the whole of the "waits, then enters, then clears" a player sees; the walk to get there is the only other cost and is distance-dependent. Pinned by `CaptureClearDurationTest`. One rifleman can walk an enemy base turning every AA gun, SAM, airfield and silo Neutral. A Neutral defence never fires but still holds its footprint, so the former owner can neither use nor rebuild there without spending a technician — and `CaptureCoordinatorBotModule` has **no logic to reclaim its own neutralised structures** and is capped at three technicians. Against a bot this is close to unanswerable; tracked in `WORKSPACE/bugs/discovered.md`.

## Ejected vehicle crew burn to death — intended, not a defect

**User ruling, 2026-08-10:** *"The crew is supposed to burn sometimes, when the vehicle is heavily damaged… sometimes it just looks cool (in a dark way) to see your enemies crawling out of the vehicle only to burn and die."* A fix was built and deliberately **reverted at `36ad9865`**, with the `CrewFireDurationTicks` knob removed rather than left dormant — the symbol exists nowhere in code or YAML today.

The mechanism, for anyone who rediscovers it and assumes it is a bug: `VehicleCrew.cs:359-362` grants `onfire` to each ejected crewman with **no duration**, unlike the `VehicleCookoff*` warheads (`weapons/weapons-explosions.yaml`), which set one — note these are *weapons*, not a trait, and the variants vehicles actually reference are `VehicleCookoffTiny` (`Duration: 25`, `:46`) and `VehicleCookoffLarge` (`Duration: 150`, `:68`). Against `ChangesHealth@BurnDamage_3` (`infantry.yaml:879-883`, −1% MaxHP every 8 ticks while `onfire == 3`), an undurated grant means **every ejected crewman dies eventually; the randomness is only in *when***. Noted without reopening: the ruling says "burn *sometimes*" while the mechanism is "always, eventually" — if that gap matters it will surface as a gameplay observation, not a test failure.

**A second, longer window on the same path, and it is not subsumed by an `IsDead` check.** `VehicleCrew`'s `INotifyKilled.Killed` only clears slot bookkeeping, and the ejection path is a wait-for-stop plus a countdown — so there is a window of hundreds of ticks, bounded by the 1 %-per-5-tick `ChangesHealth` bleed, in which a vehicle sits at `DamageState.Heavy` with `Actor.IsDead == false`. **Any rule keyed on "the crew has bailed" is therefore a different predicate from "the launcher is dead", and a test for one must assert the launcher is still alive or it silently measures the other path.**

**Binding consequence for tests:** an evacuation phase must assert **who got out**, never **who is still alive**. Post-ejection survival is not a property the game guarantees, so a survivor-count assertion is a coin flip that no threshold can stabilise (the 12 → 8 → 6 walk of 2026-05-09 was three attempts at exactly that). `test-evac-suite` is built this way — it counts a **peak** crew delta (`out = peak - before`), so later burn deaths cannot move the number.

## The Escalation endgame — the rules a designer relies on

*(Promoted 2026-09-20 from `DISCOVERIES.md`; verified against code and shipped YAML at `main @ 554895ba`.)* Only reachable in the **Escalation** game mode. The default mode is still `Skirmish`, which is a strict no-op — no level, no conditions, no clock (`DefconEscalationInfo.ModeDefault`, guarded by `DefconEscalationTest.SkirmishIsAStrictNoOp`). The machinery is in [`architecture.md` §"The Escalation endgame"](architecture.md); what follows is what the rules *are*.

**Both nations fire the SAME NUMBER of warheads, and the map decides it.** `N = round(playableCells / CellsPerImpact)`, clamped to `[MinPackage, MaxPackage]` — `SizeFor` is `(playableCells + cellsPerImpact / 2) / cellsPerImpact` then clamped (`engine/OpenRA.Mods.Common/Traits/World/FinalExchangePackage.cs:46-60`), so it **rounds half up, it does not truncate.** Shipped values are `CellsPerImpact: 2400`, `MinPackage: 2`, `MaxPackage: 6` (`mods/ww3mod/rules/world.yaml:769-775`), giving on the ten shipped maps:

| N | maps |
|---|---|
| 2 | arena-tank-duel, shellmap |
| 3 | nuclear-winter, river-zeta, siberian-pass |
| 4 | polar-disorder, woodland-warfare |
| 6 | seventh-woods, twin-rivers, x-lake |

The floor is **2, not 1**: arena-tank-duel's 2048 playable cells round to 1, and a one-warhead "exchange" on a duelling map is a coin toss rather than an ending. The ceiling is **6** because that is what the Sarmat's re-entry bus carries. `FinalExchangePackageTest` pins the whole table, so a retune shows up as a table diff rather than as a moved number. **This replaced a static asymmetry**: `AimPoints` was 6 on the Sarmat and 1 on the B83, so Russia fired six warheads against America's one on every map from arena-tank-duel to x-lake.

**The window is 250 ticks = 15.0 s**, not 10 s (`world.yaml:840`; the mod's timestep is 60 ms — see [`conventions.md` §"`Timestep` is MILLISECONDS PER TICK"](conventions.md)). It was raised to 500 on 2026-09-16 on the argument that Russia's ender asked for six clicks and America's for one; that asymmetry is gone with the map-derived package, so the raise was **reversed back to 250 on 2026-09-20** rather than overruled. Setting it to 0 fires every package on the trigger tick with no interaction at all.

**Each nation has exactly one game-ender, and they are a matched pair.** America: **UGM-133A Trident II D5 / W88** (`MissileStrikePower@TridentW88`, `mods/ww3mod/rules/player.yaml:242`), which replaced the B83 at `165642f5`/`aebdbc95` — *the B83 is no longer America's ender and no longer appears in Escalation.* Russia: **RS-28 Sarmat**. **Ender-ness is decided by YIELD, not by name, condition string or order name**: `NuclearGameEnders.Is` reads `NuclearReleaseLadder.RungForYield(tons) == NuclearRung.GameEnder` (`NuclearGameEnders.cs:77-86`), so a new 2 Mt power is picked up with no edit to either caller. The Tsar Bomba is excluded by the ladder's own `SandboxOnlyAboveTons` constant rather than by name.

**Inside the window a game-ender's own `MissileDelay` does not apply.** It is replaced by `FinalExchangeMissileDelay: 100` (`world.yaml:793`). The 500-tick delay on the powers exists so a target has thirty seconds of beacon to react to; inside the exchange there is nothing to react *with* — production is halted, the score frozen, the map revealed — so warning time is a property of a weapon **in play**, and this is exactly where there is no play left. Lowering `MissileDelay` on the powers themselves would change every ordinary match instead.

### A BAND is not a WEAPON — bands are permissions over SETS, and readiness is a property of the set

*(Promoted 2026-09-20 from `DISCOVERIES.md`, verified at `main @ a21583fd`.)* The nuclear ladder has five rungs and the arsenal has more than a dozen powers, so **a band is a bucket, not a warhead.** `NuclearReleaseLadder.RungForYield(tons)` is a pure yield → rung function over four ceiling constants (`engine/OpenRA.Mods.Common/Traits/World/NuclearReleaseLadder.cs:140-158`), and several powers land in each rung — so every band a side holds contains two or more distinct weapons, at different yields, usually one per nation's design lineage.

Four consequences, all of which have been got wrong at least once:

- **Anything that reasons from "the side's 20 kt" to "*the* 20 kt power" is wrong.** There is no such power.
- **A per-band regeneration interval is the wait between EXHAUSTING a band and getting it back, not the wait between shots.** A side holding two powers at a band fires both back to back and only then starts the clock. Whether that is the intended economy is a **balance** question, not a defect.
- **A readout over a band must take the MINIMUM across the side, not the power that was just fired and not the maximum.** A firer's band box staying lit immediately after a launch reads like a bug and is correct — the side genuinely still has a loaded warhead there. Taking the fired power, or the max, draws a countdown over a band the player can fire this tick, **which is the readout lying in the direction that loses matches.** A side is also a *team*: two players each hold their own faction's warhead at a band, and what the side can do is whatever returns soonest.
- **The support-power bin cannot answer "did it fire exactly once".** A launch, a regeneration timer and a retaliation window lapsing **all** move a band off `ready`, so a probe reading only that token cannot distinguish them; a launch **count** needs a counter on the firing module. The bin is still the right instrument for the other half of the same assertion — proving a side held a LOADED warhead when it declined, which is what makes a zero launch count evidence of *restraint* rather than of an empty magazine.

**Both national ladders ARE faction-locked, and the file you would read to check is the wrong one.** The powers' tier lines live in the merged `Player:` node at `mods/ww3mod/rules/player.yaml:211-243` — `powers.america` for the American entries, `powers.event, player.<nation>` for the enders — deliberately kept in one block because a tier table split across two files is a table nobody can read. `mods/ww3mod/rules/ingame/nuclear-arsenal.yaml` therefore looks ungated when read alone, and it was read alone and written up as "every side holds both ladders" on 2026-09-13; that conclusion was **retracted the next day** and is false. **Reading one file and concluding "no gate" is the specific mistake a merged top-level node invites, and nothing in the file being read says the other half exists** — see [`conventions.md` §"A merged top-level node means one file cannot answer 'is this gated?'"](conventions.md).

### A game-ender IS reachable in Skirmish, through one lobby dropdown

**"Game-enders are never purchasable in Skirmish" is true only while the unlock CLOCK is running**, and the lobby ships a dropdown that stops it. The Skirmish ceiling everyone quotes — `HighestPurchasableRung = HundredKiloton`, one rung below `GameEnder` and deliberately not host-overridable — is applied **inside the `Active` branch only**:

```
Active       = IntervalTicks > 0 && !sandbox && mode != DefconGameMode.Escalation
ReleasedRung = Active ? NuclearUnlockSchedule.RungAt(...) : NuclearReleaseLadder.Highest
```

(`NuclearUnlockClock.cs:320`, `:328-330`; `IsBandPurchasable` likewise returns true for every band when `!Active`, `:347-349`.) An interval of **0 is not a degenerate value** — it is the first entry of `IntervalOptions = { 0, 5, 7, 10, 15, 20 }` (`:123`), labelled as the host's own opt-out, and `world.yaml:943` registers `NuclearUnlockClock:` bare, with no `IntervalLocked` and no override, so the dropdown ships visible and unlocked. **Skirmish + "No wait" grants `nuclear-release-gameender` from the first tick**, and nothing catches the launch on the way out: `NuclearExchange` is a strict no-op outside Escalation, so the warhead simply detonates in an ordinary match with no exchange opened.

> **The general rule: a ceiling enforced inside the ACTIVE branch of a feature is not a ceiling — it is a property of the feature being switched on.** Three separate correct-looking reads of the design ruling (`NuclearUnlockSchedule.cs`, `NuclearUnlockClock.IsBandPurchasable`, and the file header) all describe the guarded path, and none of them is where the value comes from when the clock is suspended. **Practical consequence: do not assume a game-ender detonation implies a running final exchange** — any YAML-only swap of a game-ender's `Explodes: Weapon:` for an exchange-only variant is unsafe for exactly this reason.

### Pre-captured structures: the border decides ownership, and today NOTHING stays neutral

`PreCapturedStructures` no longer decides ownership by a distance ratio. On a map that has a DEFCON border it asks `DefconWall` where that border is and assigns each neutral capturable structure to the **nearest contender on its own side of the border**, leaving neutral only what the band itself touches (`PreCapturedStructures.cs:300-371`, `SideOfFootprint` at `:400-409`). The old `MiddleBandPercent: 10` ratio (`:214`) survives **only** as the fallback for a map where no border resolves.

**The shipped consequence, measured rather than reasoned about: all 90 eligible structures across the nine bordered maps get an owner, and zero stay neutral.** The ratio rule left 19 of them neutral, including every headline case the design note was calibrated around — woodland-warfare's `bio` "Nuclear Reactor", x-lake's central `bio`, both river-zeta `LOGISTICSCENTER`s. **Neither layer is buggy.** The borders were authored to a stated acceptance criterion of "no capturable inside the band" (`WORKSPACE/audit/positioning-borders-260919.md`), so the two pieces of work were each correct in isolation and compose into "everything is claimed at world load". **If a structure staying neutral matters, the fix is in that MAP's band, not in the trait** — widen the band over the structure and it goes neutral again, by the rule rather than by a percentage.

Two eligibility facts that are easy to get backwards, both swept for rather than assumed:

- **Explosive barrels are capturable structures.** `BARL` and `BRL3` inherit `^TechBuilding` (`civilian.yaml:772, 793`), so they carry `CaptureManager` + `Capturable` like an oil derrick, and fifteen sit Neutral across `siberian-pass-ww3` and `seventh-woods-ww3`. The trait excludes them because they carry `-Selectable:` — *if a player cannot select it, it is not a structure they can own* — which is also the cheapest principled filter for anything else enumerating "capturable structures".
- **`MSLO` on `nuclear-winter-ww3` is owned by `Creeps`, not `Neutral`** (`mods/ww3mod/maps/nuclear-winter-ww3/map.yaml:1146-1148`), and it is the **only** non-Neutral capturable structure on any of the ten maps. The trait's owner filter is `OwnsWorld`, so it is out of scope — but a `NonCombatant` owner test would hand a Missile Silo to whichever player is nearest. The choice between those two filters is therefore **not** academic, whatever a comment may say.

## Related reference

- [`supply-route.md`](supply-route.md) — canonical Supply Route mental model
- [`economy.md`](economy.md) — supply/ammo economy details
- [`architecture.md`](architecture.md) — engine layout, custom traits, modified systems
