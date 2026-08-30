# Recon: `@experimental` helicopter economy, doctrine, and exit strategy

**Date:** 2026-08-30 · **Researched against:** `main` @ `7de03906` (working tree clean of engine/YAML edits) · **Type:** recon only, no behaviour changed.

Answers the user complaint of 2026-08-30: *"Experimental bots spend way too much on helicopters, sometimes having only helicopters… they are often sacrificed needlessly by flying as a frontline unit… the bots should learn to evacuate units better… if there is no need to use it, it is better to sell it."*

---

## Verdict up front

Two of the four asks are **not** missing features:

- **Evacuation-for-refund is built and already ON for `@experimental`** (`EvacuateWhenIdle: true`, `ai.yaml:2026`). "Selling" via the `Sellable` trait is unreachable for a bot, but it does not need to be — the evacuation order reaches the identical refund code. The *real* gap in ask 4 is narrow and specific: **nothing acts on a partially-armed airframe.** Every ammo-driven evac fires at absolute zero.
- **Rear-support doctrine is built and already ON** (`StandoffEngagement`, `DangerFieldAvoidance`, `MinFrontierDistanceCells: 4`). It is defeated by one line: the squad abandons all of it 8 cells from the target.

The other two are real and structural:

- **The over-buying has a single mechanical cause** and it is not a tuning value. The helicopter production lane is a **uniform random lottery over a queue whose only buildable members are helicopters**, running in parallel with a ground lane that has never heard of aircraft. Its declared weights are inert. §1.
- **The bot picks helicopter targets omnisciently and assesses helicopter risk from fog-limited belief.** Confident targeting, blind risk — the exact combination that produces "sacrificed needlessly". §3.

---

## 1. Economy — why the bot ends up all-helicopter

### 1.1 What the code does today

Six facts compose into the behaviour. Each is independently verifiable.

**(a) The helicopter lane is a separate module on a separate queue.**
`UnitBuilderBotModule@experimental.russia.heli` (`mods/ww3mod/rules/ai/ai.yaml:1835`) and `@experimental.america.heli` (`:1924`) both set `UnitQueues: Aircraft` (`:1839`, `:1928`). The ground twins set `UnitQueues: Vehicle, Infantry` (`ai-america.yaml:57`, `ai-russia.yaml:56`). They are distinct trait instances with distinct `BotTick`s drawing on distinct `ClassicProductionQueue`s (`player.yaml:77` Aircraft vs `:40` Vehicle / `:52` Infantry). **They contend only for cash.**

**(b) Helicopters have no composition target, deliberately.**
`UnitTargetShares` on the ground twins (`ai-america.yaml:571-616`, `ai-russia.yaml:287-316`) lists no aircraft, and says so: *"helicopters are deliberately absent (deferred to their own lane) and so are never chosen by it"* (`ai-america.yaml:561-562`). Consequently the entire composition apparatus — `ForceCompositionMath.SelectDeficit`, `ApplyCeilingEligibility`, `CompositionEnforceTargetCeiling` — **cannot see a helicopter**. There is no share for one to be over.

**(c) The heli twin does not set `CompositionDirected`.**
`CompositionDirected: true` appears exactly twice in the mod: `ai-america.yaml:545` and `ai-russia.yaml:274`, both on the **ground** twins. So the heli lane takes the legacy pick at `UnitBuilderBotModule.cs:830-832`.

**(d) `buildRandom` is permanently true, so the weights never execute.**
`BotTick` calls `BuildUnit(bot, q, idleUnitCount < Info.IdleBaseUnitsMaximum)` (`UnitBuilderBotModule.cs:657`). `idleUnitCount` is written only by `IBotNotifyIdleBaseUnits.UpdatedIdleBaseUnits` (`:616-619`), whose only callers are `SquadManagerBotModule` (`:271`, `:350`, `:432`, `:446`), each passing `unitsHangingAroundTheBase`. On `@experimental` that list is **permanently empty**:

- every ground unit hits `continue` under `IgnoreGroundUnits: true` **without being added** (`SquadManagerBotModule.cs:334-340`);
- helicopters fail `IsAirSquadUnit` on its `!a.Info.HasTraitInfo<AIHelicopterRoleInfo>()` clause (`:362-370`) and hit the same `continue`;
- every fixed-wing airframe is unbuildable, so none exist to be added.

`0 < 8` is therefore true from tick 0 to the end of the match. `IdleBaseUnitsMaximum: 8` (`ai.yaml:1837`, `:1926`) is decorative.

**(e) With `buildRandom` true the pick is uniform.**
`ChooseRandomUnitToBuild` (`UnitBuilderBotModule.cs:1589-1595`) is `buildableThings.Random(world.LocalRandom)`. `UnitsToBuild` then survives only as a **membership filter** (`:839-840`). The declared weights — `heli: 80 / littlebird: 40 / tran: 15` (`ai.yaml:1930-1933`), `hind: 80 / mi28: 50 / halo: 15` (`:1841-1844`) — are **dead numbers**. This is the same defect the ground twin documents against itself at `ai-america.yaml:540-542` and fixed with `CompositionDirected`; **the heli twins never received that fix.**

**(f) The Aircraft queue's buildable set is helicopters only.**
Every fixed-wing carries `Prerequisites: ~disabled`: `A10` (`aircraft-america.yaml:458-459`), `F16` (`:582-583`), `FROG` (`aircraft-russia.yaml:477-478`), `MIG` (`:599-600`).

### 1.2 Why the observed behaviour follows

Every 30 ticks (`FeedbackTime`) after world-tick 2500 (≈2.5 min at 16.67 tps), the heli lane picks **one of three helicopters with probability 1/3 each** and buys it, bounded by nothing except `UnitLimits`. And `UnitLimits` counts **world actors only** (`UnitBuilderBotModule.cs:465-468`), so call-ins still walking in from the map edge are invisible to it and the cap overshoots by however many cycles fit inside the delivery time.

The lane's own ceiling:

| | limit × cost | attack-air subtotal | with transports |
|---|---|---|---|
| America | heli 4×6000, littlebird 2×3000, tran 2×2000 | **30,000** | 34,000 |
| Russia | mi28 3×6000, hind 4×4000, halo 2×2000 | **34,000** | 38,000 |

`ai-america.yaml:343` states a realistic mid-game army is **~15–22k**. **The helicopter lane alone is permitted to spend roughly double the entire intended ground army, out of the same treasury.** That is the whole answer.

**`V_fit` (`1000 × cost / target` — the army value at which one unit is already over its share):**

| America | cost | target ‰ | V_fit | Russia | cost | target ‰ | V_fit |
|---|---|---|---|---|---|---|---|
| bradley | 1500 | 140 | 10,714 | btr | 600 | 70 | 8,571 |
| humvee | 500 | 40 | 12,500 | bmp2 | 1300 | 140 | 9,286 |
| abrams | 2500 | 190 | 13,158 | t90 | 2400 | 190 | 12,632 |
| m113 | 700 | 30 | 23,333 | truk | 1000 | 40 | 25,000 |
| truk | 1000 | 40 | 25,000 | tunguska | 1700 | 50 | 34,000 |
| m109 | 1800 | 60 | 30,000 | giatsint | 1800 | 35 | 51,429 |
| strykershorad | 2500 | 50 | 50,000 | grad | 1500 | 25 | 60,000 |
| **heli / littlebird / tran** | 6000 / 3000 / 2000 | **none** | **∞** | **mi28 / hind / halo** | 6000 / 4000 / 2000 | **none** | **∞** |

**No army size ever makes a helicopter "over share", because it has no share.**

### 1.3 There is no aggregate airframe cap — and the field that looks like one is dead

No "max N aircraft" and no air-value-share bound exists anywhere in `mods/ww3mod/rules/ai/` or `engine/…/BotModules/`. The only aggregate bound is the sum of the six per-type `UnitLimits`.

`AIHelicopterRoleInfo.AIBuildLimit` (`engine/OpenRA.Mods.Common/Traits/Air/AIHelicopterRole.cs:46`) and `AIBuildPriority` (`:43`) — set per template (`aircraft-america.yaml:312-313` heli=4, `:122-123` littlebird=2) — are **declared and read nowhere**. A full-tree `grep` returns only the declarations. Anyone reaching for the obvious lever gets a no-op.

### 1.4 Proposals

**P1 — cut `UnitLimits` on both heli twins. YAML only. Small. Low risk. Ship first.**

| file:line | from | to |
|---|---|---|
| `ai.yaml:1932` (`hind`) | 4 | **1** |
| `ai.yaml:1933` (`mi28`) | 3 | **1** |
| `ai.yaml:1936` (`heli`) | 4 | **1** |
| `ai.yaml:1937` (`littlebird`) | 2 | **1** |

*(line numbers are the `UnitLimits` entries inside the blocks at `:1835` and `:1924`; verify at edit time)*

Attack-air ceiling becomes America **9,000** (heli 6000 + littlebird 3000), Russia **10,000** (mi28 6000 + hind 4000) — i.e. exactly the user's "one, maybe two". Transports stay at 2 each; they are already demand-gated (`GateTransportOnDemand: true`, `ai.yaml:1852`/`:1941`) and are not what the complaint is about. **Caveat:** because `UnitLimits` lags by the call-in flight time (§1.1), expect transient 2-of-a-type. That is acceptable and is itself inside "one, maybe two".

**P2 — make the mix directed instead of uniform. YAML only, but needs an engine check first. Small. Medium risk.**
Set `CompositionDirected: true` + a `UnitTargetShares` over the heli roster on each heli twin, so the lane stops spending 6000 on an Apache as often as 2000 on a transport. Suggested shares — America `heli: 500, littlebird: 300, tran: 200`; Russia `mi28: 500, hind: 300, halo: 200`.
**Unverified premise, must be checked by the implementer:** whether `CompositionDirected`'s census is scoped to `compositionTypes` only. If it is, P2 fixes the *mix within air* but does **not** bound aggregate air spend — P1 or P3 is still required. Do not ship P2 alone as the answer to the complaint.

**P3 — the structural fix: give air a real value share. Engine + YAML. Medium. High risk. Benchmark-gated.**
Fold helicopters into the ground twins' `UnitTargetShares`, add `Aircraft` to their `UnitQueues`, and retire the separate heli twins. Helicopters then live under `CompositionEnforceTargetCeiling` like everything else. A target of **200‰** for the heavy attack heli yields `V_fit = 30,000` — one Apache at a 30k army, a second only past 60k, which is precisely the requested shape.
**Why it is high risk:** the target vectors currently sum to 1008 and are renormalised by `SharesPerMille`, so adding 200‰ dilutes every existing slot by ~17%. That is a whole-army balance change, not a helicopter change. **This invalidates any in-flight ladder baseline** and cannot be evaluated by reading.

**Recommendation: ship P1 now; treat P3 as the follow-up that makes P1 unnecessary.**

---

## 2. Doctrine — why they fly as frontline units

### 2.1 What the code does today

The rear-support concept is **built and enabled**. `HelicopterSquadBotModule@experimental` (`ai.yaml:1981`) sets:

- `StandoffEngagement: true` (`:1997`) — issues `AttackMove` toward a cell, not a bare `Attack` on a distant actor, so `AutoTarget` engages at weapon standoff.
- `DangerFieldAvoidance: true` (`:2003`) — leashes the engage cell to an AA-safe cell and detours around believed AA (`HelicopterStates.cs:608-621`).
- `MinFrontierDistanceCells: 4` (`:2020`) — walks the standoff cell rearward until it is 4 **coarse** control-field cells (≈8 map cells) behind the believed enemy frontier (`HelicopterStates.cs:624-633`, `PushHeliBehindFrontier` `:299-320`).
- `AirDangerSpikeUnits: 25` (`:2006`) — withdraw when new AA lights up on the field.

### 2.2 Why the observed behaviour follows anyway — four independent causes

**(i) The squad abandons the entire standoff apparatus 8 cells from the target.**
`HelicopterApproachState` hands off unconditionally:
```
var distToTarget = (owner.CenterPosition - owner.TargetActor.CenterPosition).HorizontalLength;
if (distToTarget < WDist.FromCells(8).Length)
    owner.FuzzyStateMachine.ChangeState(owner, new HelicopterAttackRunState());
```
(`HelicopterStates.cs:600-605`). `HelicopterAttackRunState` (`:703`) issues bare `Order("Attack", …)` on a single actor (`:785`) — no standoff, no leash, no frontier push, no detour. **This is the frontline behaviour, and it is reached on every successful approach.**

**(ii) The frontier standoff is set to exactly the distance that triggers the handoff.** `MinFrontierDistanceCells: 4` coarse ≈ **8 map cells**; the attack-run trigger is **8 map cells**. The standoff is dimensioned to place the squad precisely on the boundary where it stops applying.

**(iii) The attack-run state has no danger check at all.** The `AirDangerSpikeUnits` withdraw appears **once**, in the approach state (`HelicopterStates.cs:584`). `HelicopterAttackRunState.Tick` (`:712-789`) checks only `SendDamagedUnitsHome`, `ShouldFlee` (health-only, `:386-389`), the hit-and-run timer, and target validity. **Once committed, a 6000-cost airframe cannot withdraw on danger — only on damage already taken**, which for a helicopter is too late. Apache `HitAndRunCooldown: 200` (`aircraft-america.yaml:307`) ≈ 12 s at 16.67 tps of committed exposure.

**(iv) A lone Apache is deliberately committed.** `AllowSoloAttackHeli: true` + `MinAttackSquadSize: 1` (`ai.yaml:1990-1991`) launch a single attack heli whenever spendable income is below `PairUpIncomeThreshold: 6000` (`:1992`; `HeliPackageMath.ShouldLaunchPartial`, `HelicopterSquadBotModule.cs:813-814`). A bot that spends to zero routinely is below 6000 most of the time, so **solo commitment is the common case, not the exception.**

**(v) The per-template AA standoff a designer wrote does nothing.** `AvoidAntiAirRange` (Apache 5, littlebird 8 — `aircraft-america.yaml:310`, `:120`) is declared at `AIHelicopterRole.cs:40` and **read nowhere in the engine**. So is `EngagementRange` (`:25`).

### 2.3 Proposals

**P4 — stop committing lone helicopters. YAML only. One line. Lowest-risk highest-value change in this document.**
`ai.yaml:1991`: `MinAttackSquadSize: 1` → **`2`** (or remove `AllowSoloAttackHeli` at `:1990`). Under P1 (limit 1 per type) a pair means one Apache + one littlebird, which is the intended mixed package. **Interaction with P1: if both ship, verify a pair can still form** — with `heli: 1` and `littlebird: 1` the only possible pair is one of each, and if `AttackSquadSize: 2` cannot be met the squad may never launch. Ship P1 and P4 together and check that squads still form.

**P5 — push the standoff genuinely behind the line. YAML only. One line. Low risk.**
`ai.yaml:2020`: `MinFrontierDistanceCells: 4` → **`8`** (≈16 map cells), so the standoff cell no longer lands inside the 8-cell attack-run trigger radius. Cheap and reversible; effect is only measurable in a match.

**P6 — gate the approach→attack-run handoff. Engine C#. Small. Medium risk. Benchmark-gated.**
Add `AttackRunHandoffCells` to `HelicopterSquadBotModuleInfo`, **default 8** (the current literal, so baseline is preserved and `@stable` is untouched until opted in), read at `HelicopterStates.cs:600-605` in place of the hard-coded `WDist.FromCells(8)`. Set **0 on `@experimental`** to disable the close-in run for `AttackHeavy` roles and keep them in the standoff approach.
**Real risk to state plainly:** if the standoff approach cannot itself kill anything, disabling the attack run turns helicopters into expensive spectators. Whether `AttackMove` + `AutoTarget` at Hellfire range is sufficient to actually destroy targets **cannot be determined by reading** — it needs a match.

**P7 — carry the danger check into the attack run. Engine C#. Small. Low risk. Strongly recommended.**
Replicate the `AirDangerSpikeUnits` withdraw from `HelicopterStates.cs:584` inside `HelicopterAttackRunState.Tick`. Gate behind a new `WithdrawOnSpikeInAttackRun`, **default false**, set true on `@experimental`. This directly addresses "sacrificed needlessly": today the only way out of an attack run is having already been shot.

**P8 — wire or delete the four dead `AIHelicopterRole` fields. Engine C#. Small. Low risk.**
`AvoidAntiAirRange`, `EngagementRange`, `AIBuildLimit`, `AIBuildPriority` are all declared-and-never-read (`AIHelicopterRole.cs:25,40,43,46`). Either read `AvoidAntiAirRange` as a minimum believed-AA standoff inside `HeliDangerNav.LeashedEngageCell`, or delete all four. **Leaving them is the worst option** — they are a trap for the next person tuning helicopter behaviour from the templates.

---

## 3. Offence only into scouted ground

### 3.1 What a helicopter consumer can know today

| primitive | fog-legal? | what it answers |
|---|---|---|
| `DangerFieldLayer.AirDanger(player, cell)` | **yes** (stamped from the belief store) | "how much AA do I *believe* covers this cell" |
| `BeliefStore.Contacts(player)` → `BeliefContact.LastSeenTick` (`BeliefStore.cs:53`, exposed `:288-293`) | **yes** | "when did I last see this contact" |
| `ControlField.FrontierDistanceAt` | **yes** | "how far behind the believed frontier is this" |
| `Shroud` / `IsExplored` | — | **not read by any bot module anywhere** (`grep` over `Traits/BotModules/` returns nothing) |

**The load-bearing consequence: air danger of 0 means "no believed AA", which is indistinguishable from "never observed".** The module's own comment concedes this at `HelicopterSquadBotModule.cs:175-177` — *"Unscouted cells carry no belief data (air-danger reads 0), so this geometry cap is what 'no deep penetration into unscouted territory' rests on before first contact."*

**The scout path has that geometry cap. The attack path has no equivalent.** `CarefulScoutEmployment: true` + `ScoutMaxDistanceCells: 40` (`ai.yaml:2055-2057`) bound the littlebird's recon penetration precisely because zero danger cannot be trusted. Nothing bounds an attack squad's.

### 3.2 The asymmetry that produces the observed losses

**Target selection is omniscient. Risk assessment is fog-legal.**

- `FindClosestEnemy` (`HelicopterStates.cs:357-370`) scans `owner.World.Actors` filtered on owner, relationship, husk and aircraft — **no visibility filter of any kind.** The squad picks the nearest enemy anywhere on the map, seen or unseen.
- The attack-run re-target (`:751-756`) uses `World.FindActorsInCircle` — also omniscient.
- `Squad.IsTargetVisible` exists (`Squads/Squad.cs:104`, `TargetActor.CanBeViewedByPlayer`) and **no helicopter state calls it**. Its only consumer is `ProtectionStates.cs:46`, which architecture.md establishes is unreachable code.
- Meanwhile `DangerFieldAvoidance` routes around **believed** AA only.

So the bot flies confidently at a target it should not be able to see, through AA it has no belief about, and reads the unmeasured approach as safe. **That is the mechanism behind "sacrificed needlessly by flying as a frontline unit".**

One partial mitigation exists and is weak: `IsTargetTooHot` (`:379-384`) is omniscient and refuses when `aaCount > owner.Units.Count * 2` within 10 cells. For a solo heli (§2.2 iv) that means **more than two** AA units. A single Tunguska never trips it.

### 3.3 Proposals

**P9 — penetration bound on the attack path. Engine C# (one Info field) + YAML. Small. Low risk. Direct prior art.**
Add `AttackMaxDistanceCells` to `HelicopterSquadBotModuleInfo`, **default 0 = off**, mirroring `ScoutMaxDistanceCells` exactly. Reject an attack objective farther than N cells from the squad's own Supply Route. Set **40** on `@experimental`, matching the scout bound (own half + contested middle). This is the cheapest honest answer to "only for offensive action on rare occasions when an area is scouted".

**P10 — require a recently-observed contact before an offensive mission. Engine C#. Medium. Medium risk.**
The primitive already exists: `BeliefContact.LastSeenTick`. Add `OffensiveRequiresRecentSighting` (default false) + `SightingMaxAgeTicks` (suggest **1500** ≈ 90 s). An attack objective qualifies only if a believed contact within `MissionTargetRangeCells` of it has `LastSeenTick` within the window. This is literally "only attack scouted ground", expressed in the vocabulary the influence stack already provides, with **zero new sensing machinery.**

**P11 — make heli target selection fog-legal. Engine C#. Medium size, HIGH behavioural risk. Benchmark-gated.**
Filter `FindClosestEnemy` (`HelicopterStates.cs:357`) and the attack-run re-target (`:751`) through `CanBeViewedByPlayer`, or re-point them at the belief store. This is the *correct* fix and it aligns helicopters with the influence-stack invariants. **It will make the bot substantially less aggressive** and could plausibly stop helicopters attacking at all on maps with heavy fog. Do not ship it with P6 in the same change — if aggression collapses you will not know which one did it.

---

## 4. Evacuation and selling

### 4.1 What the code does today — most of this ask already ships

**The refund path.** `HelicopterSquadBotModule.Evacuate` (`:1739-1765`) issues `Order("Evacuate", h, false)` (`:1747`). That resolves in `DeliversCash.ResolveOrder` for `Type == "Rotation"` (`DeliversCash.cs:82-86`) → `GoDonateCash` (`:94-105`) → `RotateToEdge`. `^Helicopter` carries `DeliversCash@Rotation` (`aircraft.yaml:198`).

`RotateToEdge` has a **first-class aircraft branch** (`RotateToEdge.cs:131-148`): it picks an edge cell near the owner's own Supply Route, sets `aircraft.EvacuatingOffMap = true`, and flies out via `Fly` (`:225-231`) — no ground pathing. `DoSell` (`:378-408`) pays `fixedRefund × hp / maxHP` (`:383-389`) through `PlayerResources.ChangeCash` (`:399`), then `Dispose()`s the actor (`:407`). **`Sellable` is not required** — the 3-arg constructor (`:72-82`) bypasses it entirely.

`Evacuate` also drops the heli from `idleHelicopters`, every `activeSquads` entry, `managedHelicopters`, `stagedTo`, `idleTicks` and the blackboard (`:1754-1761`), and re-adoption is blocked (`:1391-1392`, `:562-564`), so nothing re-tasks it and cancels the exit. **This is well-built.**

**What is live on `@experimental` (`ai.yaml:2026-2033`, `:2103-2105`):** `EvacuateWhenIdle: true`, `EvacuateIdleTicks: 500` (≈30 s), `EvacuateHomeRadiusCells: 12`, `MissionTargetRangeCells: 60`, `EvacuateForwardIdle: true`, `EvacuateIdleTransports: true`, `TransportIdleEvacuateTicks: 900` (≈54 s). The `@stable` block (`:1954`) sets none of them.

Two branches reach `Evacuate` (`HeliEmploymentMath.Decide`, `HelicopterSquadBotModule.cs:1995-2014`):
1. `!hasUsableAmmo && !canRearm` (`:2002-2003`) — **fires on `@stable` too**, it is not flag-gated.
2. `contactEverObserved && !hasWorthwhileTarget && (nearHome || evacuateForwardIdle) && idleTicks >= evacuateIdleTicks` (`:2010-2011`) — **this is the user's "if there is no need to use it, sell it", already built and on.**

**Selling proper is NOT reachable for a bot, and does not need to be.** `Sellable` appears on structures only (`structures.yaml:135,437`; `structures-defenses.yaml:62,86,183,270,509`) and on **zero** airframes. The `"Sell"` order is issued only from the player UI (`GlobalButtonOrderGenerator.cs:91`); no bot module issues one. State this plainly to the user: the bot cannot *sell*, but it can *evacuate*, and evacuation reaches the same `DoSell` refund.

### 4.2 The actual gap

**Nothing acts on a partially-armed airframe.** `HasUsableAmmo` returns true if **any** pool holds a round (`HelicopterSquadBotModule.cs:1709-1720`); `AirframeEvacMath.Decide` returns `None` while `loadedPools > 0` (`Air/AirframeEvacMath.cs:85-86`). `AirframeReadiness.AmmoReadyToFight` with no rearm host is `loadedPools > 0` (`AirframeReadiness.cs:109-115`). And `SendLowAmmoUnitsHome` (`HelicopterStates.cs:113-124`) is **misnamed** — its predicate is `!HasAmmo(ammoPools)`, i.e. *dry*, not *low*.

The user asked for exactly this: *"before it runs completely dry on ammo it can be evacuated."* **It is the one thing in ask 4 that is genuinely missing.**

Note also that `AmmoEvacMath.cs` — despite the name — is the **ground-vehicle** decision. Its only callers are `PoiOffensiveBotModule` (`:155`, `:2695`) and its NUnit suite. **It has no aircraft caller and is not the helicopter path.**

### 4.3 Proposals

**P12 — evacuate on low ammo, not empty ammo. Engine C#. Small. Low risk. This is the ask.**
Add `EvacuateAmmoPercent` to `HelicopterSquadBotModuleInfo`, **default 0 = off (baseline preserved)**. When set, an attack heli with no rearm host whose remaining rounds across all pools are at or below N% becomes evac-eligible without waiting for zero. Suggest **34** on `@experimental` — roughly "one salvo left", which leaves the airframe able to defend itself on the way out. Implement as a new branch in `HeliEmploymentMath.Decide` beside `:2002`, keeping the class pure and NUnit-pinnable.

**P13 — evacuate an unrepairable damaged airframe. Engine C#. Small. Medium value.**
The refund is HP-scaled (`RotateToEdge.cs:383-389`), so **an airframe's salvage value only ever decreases**. With no repair host on any shipped map, a heli below `ReEngageHealthPercent` that `SendDamagedUnitsHome` parks can never recover and can only lose more value. Add `EvacuateBelowHealthPercent` (default 0 = off); set to Apache's `FleeHealthPercent` 35 (`aircraft-america.yaml:306`) on `@experimental`. Banking 35% of 6000 beats losing 100% of it later.

**P14 — tighten the idle window. YAML only. Trivial.**
`ai.yaml:2027`: `EvacuateIdleTicks: 500` → **300** (≈18 s). Retires a target-less airframe sooner. Low confidence that it matters much; free to try.

**Correction to fold in:** three in-tree comments justify heli evacuation by "stopping its upkeep drain" (`HelicopterSquadBotModule.cs:1451`, `:1530`, `:2000-2001`) and one Info `[Desc]` repeats it (`:259`). **`InfersUpkeep` is attached to exactly two templates — `vehicles.yaml:113` and `infantry.yaml:154` — and to no aircraft.** An idle helicopter costs **zero** upkeep. The economic case for heli evacuation is **capital at risk**, not drain. Recorded in `DISCOVERIES.md`.

---

## 5. Further ideas (my own — the user asked)

Each rated by my confidence that it is worth doing.

**I1 — Use helicopters as spotters, not shooters. Confidence: high.**
Helicopters have the best vision in the game and the influence stack is entirely belief-driven. A heli holding station **behind** the line continuously refreshes `BeliefStore` contacts, which directly feeds `DangerFieldLayer`, `ControlField`, and — concretely — `PoiOffensiveBotModule.BombardStaticPositions` (`:3006`), whose aim point is the *believed* cell centre. One surviving spotter is worth more to a 25k ground army than one dead gunship. This reframes the user's "used behind to provide support" as a **role**, not merely a standoff distance, and it needs no new sensing: park the heli at the frontier standoff and let vision do the work. Would need a new `Role: Spotter` behaviour or a `SpotterStandoff` mode on `AttackHeavy`.

**I2 — Never fly outside the friendly AA umbrella. Confidence: medium-high.**
The bot already buys AA (`strykershorad` 50‰, `tunguska` 50‰) and already computes believed-AA envelopes for the enemy. The mirror — "is this cell covered by *my* AA" — is cheap to derive from own-actor positions (own units are not a fog problem) and would keep helicopters over ground the army actually holds. Composes naturally with P9.

**I3 — Make `IsTargetTooHot` scale with airframe value, not unit count. Confidence: medium.**
`aaCount > owner.Units.Count * 2` (`HelicopterStates.cs:381-383`) treats a 6000-cost Apache and a 2000-cost transport identically, and gets *more* permissive as the squad grows — the same count-normalisation error architecture.md documents for the suppression threshold. A value-weighted test would refuse a solo Apache against one Tunguska, which is currently allowed.

**I4 — Retreat on danger, not only on damage. Confidence: high.** This is P7; listing it here because it is the single change most directly aimed at "sacrificed needlessly".

**I5 — Do not buy an airframe while a cheaper ground need is unmet. Confidence: high, but it *is* P3.** The reason the bot buys a 6000 Apache while its infantry starves is that the two lanes never compare. Any fix that puts helicopters in the same argmax as ground units solves this for free.

**I6 — Rename `SendLowAmmoUnitsHome`. Confidence: high, cost trivial.** Its predicate is "dry" (`HelicopterStates.cs:118`). The name has already misled once — it reads as though the partial-ammo case is handled when it is not, which is exactly the gap in §4.2.

---

## Change classification

### Pure YAML tuning (`mods/ww3mod/rules/ai/`)
| # | change | size |
|---|---|---|
| P1 | heli-twin `UnitLimits` → 1 per attack type | trivial |
| P4 | `MinAttackSquadSize: 1` → `2` | trivial |
| P5 | `MinFrontierDistanceCells: 4` → `8` | trivial |
| P14 | `EvacuateIdleTicks: 500` → `300` | trivial |
| P2 | `CompositionDirected` + heli `UnitTargetShares` | small — **verify census scope first** |

### Requires engine C#
| # | change | size |
|---|---|---|
| P6 | `AttackRunHandoffCells` (default 8 = baseline) | small |
| P7 | danger-spike withdraw inside attack-run state | small |
| P8 | wire or delete 4 dead `AIHelicopterRole` fields | small |
| P9 | `AttackMaxDistanceCells` (default 0 = off) | small |
| P12 | `EvacuateAmmoPercent` (default 0 = off) | small |
| P13 | `EvacuateBelowHealthPercent` (default 0 = off) | small |
| P10 | `OffensiveRequiresRecentSighting` + `SightingMaxAgeTicks` | medium |
| P11 | fog-legal heli target selection | medium code, large behaviour |
| P3 | fold air into ground composition | medium code, large balance |

---

## `@stable` impact

Per CLAUDE.md: `@stable` inheriting a genuine improvement is settled policy; **silent** drift is not. New behavioural Info fields on shared traits must default to baseline.

- **P6, P7, P9, P10, P12, P13 all add Info fields to `HelicopterSquadBotModuleInfo`, a trait declared on BOTH twins** (`ai.yaml:1954` and `:1981`). Every one is specified above with a **baseline default** (`0` / `false` / the existing literal `8`), opted in per-profile via YAML. Follow that exactly.
- **P1, P4, P5, P14, P2 are YAML edits inside the `@experimental` blocks only** and cannot reach `@stable`.
- **P8 touches `AIHelicopterRole`, which is on the actor templates, not the bot** — so wiring `AvoidAntiAirRange` changes behaviour for **every profile including `campaign`** the moment it is read. It cannot be profile-gated at the template. Either gate the *read* behind a bot-module flag, or accept it as a deliberate cross-profile improvement and **say so in the commit message** so the next benchmark baseline is re-taken knowingly.
- **P3 changes `UnitTargetShares`, which live in the faction files shared by both profiles** — it will move `@stable` unless duplicated per profile. Treat as a deliberate, announced change.
- **Already true today and worth flagging:** the ammo-driven evac branch (`HelicopterSquadBotModule.cs:2002-2003`) is **not flag-gated and already runs on `@stable`**, contrary to the `EvacuateWhenIdle` `[Desc]` at `:261-262` which claims the whole feature is experimental-only. The `[Desc]` overstates the gating.

---

## Benchmark / tournament gated (batch these into one user request)

These cannot be validated by reading or by a single autotest. **Each needs a match or a benchmark run, and launches are user-gated and serialized.**

1. **P1 + P4 together** — does a squad still form when each attack type is capped at 1 and the minimum package is 2? *(This one may be answerable by a single short match rather than a tournament.)*
2. **P6** — does the standoff approach actually destroy anything with the attack run disabled, or do helicopters become spectators?
3. **P11** — how far does aggression fall when target selection becomes fog-legal?
4. **P3** — whole-army balance after a ~17% dilution of every existing composition share.
5. **P5, P7, P12, P13** — direction is defensible from reading, but the *magnitudes* (8 coarse cells, 34% ammo, 35% health) are guesses that only a match can price.

**Do not run 2–5 individually.** Batch P1+P4+P5+P7+P12+P13 as one candidate configuration against `@stable`, and hold P3/P6/P11 for a separate round — they are the three that can plausibly make the bot worse.

---

## Watch

**What I could not verify by reading, in descending order of how much it would hurt if I am wrong:**

- **Whether `CompositionDirected`'s census is scoped to `compositionTypes` only.** P2's entire value depends on it. I read `SelectDeficit`'s contract second-hand from `architecture.md` rather than reading `ForceCompositionMath.cs` line by line. **If the census spans the whole army, P2 is much stronger than I have credited and may subsume P1.** Check this first — it is the cheapest way to change the shape of the recommendation.
- **Whether the attack-run handoff is genuinely reached in a real match.** My causal claim in §2.2(i) is a reading of the state machine. I did not observe a match. If squads mostly die during *approach* rather than during the run, P6 is aimed at the wrong state and P7/P9 carry the whole load.
- **Whether disabling the attack run leaves helicopters able to kill.** Stated as a risk under P6 because I genuinely do not know; `AttackMove` + `AutoTarget` at Hellfire range *should* engage, but "should" is doing real work in that sentence.

**The claim I would most bet is wrong:** my suggested magnitude for P3 (200‰ for the heavy attack heli). I derived it from `V_fit` arithmetic alone, with no reference to whether an Apache is actually *worth* a fifth of an army's value at current balance. Treat the 200 as a worked example of the method, not as a recommendation.

**A second one I hold loosely:** that `UnitLimits` overshoot (§1.1) is small in practice. Delivery time from map edge is map-dependent and I did not measure it; on a large map the transient could be worse than "one extra".

**What I deliberately did not check:** whether `Order.StartProduction` on an unbuildable item is rejected by the queue. This matters for one secondary path — the `AirStrikeUnits` air-strike window (`ai.yaml:1429-1442`) requests the *cheapest* named type by ordinal tie-break, which selects `a10`/`frog` (both `~disabled`) over `heli`/`mi28`. If those orders are silently rejected the window is inert and is **not** a heli source; if they are not, it is a second uncapped one. Since helicopters are exempt from `RequestIsOverCompositionCeiling` (`UnitBuilderBotModule.cs:1749-1751`, they are not in `compositionTypes`), a request that *did* land would be unbounded. **Worth ten minutes before anyone tunes that window.** → **Settled; see the addendum below.**

---

# Addendum (2026-08-30) — the `AirStrikeUnits` window is inert, and that is not entirely good news

**Answer: outcome 1. The window has never produced an aircraft, so P1/P2/P3 do not leak and need no second gate.** But it is inert by accident rather than by design, and two one-line edits uncork it as exactly the uncapped second source the question feared.

## The chain, each link verified

**1. The pick is always the disabled fixed-wing.** `RequestCheapestBuildable` (`AdaptiveProductionBotModule.cs:558-580`) filters candidates on `world.Map.Rules.Actors.ContainsKey(u)` **only** (`:561`) — existence in the rules, *not* buildability — then orders by `UnitCost` and breaks ties with `StringComparer.Ordinal` (`:562-563`). America's pool is `heli, a10` (`ai.yaml:1436`), both cost 6000, and `"a10" < "heli"`. Russia's is `mi28, frog` (`:1479`), both 6000, and `"frog" < "mi28"`. **The disabled airframe wins both tie-breaks.**

**2. The helicopter is never reached on a later iteration.** `RequestUnitProduction` is `void` (`TraitsInterfaces.cs:749`; impl `UnitBuilderBotModule.cs:760`), so it cannot report failure. `RequestCheapestBuildable` `return 1`s immediately after the call (`:575-576`). The loop's only `continue` is the `alreadyRequested >= 2` in-flight cap (`:572-573`), which the drop-on-failure below resets to 0 every cycle. **`heli`/`mi28` is never requested by this path.**

**3. The composition ceiling waves it through.** `RequestIsOverCompositionCeiling` returns `false` when `Array.IndexOf(compositionTypes, name) < 0` (`UnitBuilderBotModule.cs:1750-1752`). No aircraft is in `compositionTypes` (§1.1b), so the request is admitted unconditionally and reaches `BuildUnit(bot, name)`.

**4. `Order.StartProduction` is never issued — the request dies one step before the queue.** `BuildUnit(bot, name)` (`:1561-1587`) reads `buildableInfo.Queue` and loops over it (`:1572-1577`) to find a queue with nothing already queued. **`a10`'s `Queue` is the empty default.** `BuildableInfo.Queue` initialises to `new()` (`Traits/Buildable.cs:27`); `A10`'s own `Buildable` block declares *only* `Prerequisites: ~disabled` (`aircraft-america.yaml:458-459`); and nothing is inherited, because `mods/ww3mod/rules/ingame/aircraft.yaml` contains **zero `Buildable:` traits and zero `Queue:` lines** across all eight templates (`^NeutralAirborne`, `^Airborne`, `^AirRadar`, `^Aircraft`, `^Helicopter`, `^Drone`, `^WhenDamagedAir`, `^AircraftAffectedByEMP`). So the `foreach` body never executes, `queue` stays `null`, and the method returns `false` at `:1586` **without issuing an order at all.**

**5. Belt and braces — the queue would refuse it anyway.** Had `a10` carried a `Queue`, `ProductionQueue.ResolveOrder` drops it twice: `!bi.Queue.Contains(Info.Type)` (`:446-447`) and, decisively, `if (BuildableItems().All(b => b.Name != order.TargetString)) return;` (`:450-451`). `BuildableItems()` returns `buildableProducibles` (`:298`) = `Producible.Where(a => a.Value.Buildable)` (`:169`), and `Buildable` is set true only by the tech-tree callback `PrerequisitesAvailable` (`:246`). Nothing in `mods/ww3mod/` provides the `disabled` prerequisite — `grep -rn "ProvidesPrerequisite" mods/ww3mod/ | grep -i disabled` returns nothing — so `~disabled` is unsatisfiable and `a10` is never in `BuildableItems()`. `ClassicProductionQueue.BuildableItems()` only narrows further (`:79-82`, returns `NoItems` when disabled).

**6. The request is consumed either way.** The FIFO drain removes the entry whether or not `BuildUnit` succeeded (`:643-645`), so `RequestedProductionCount` returns to 0 and the window re-requests `a10` on its next `EvaluationInterval: 300` (`ai.yaml:1446`) — forever, silently, with no debug line marking the failure.

## Consequences

**For the cap proposals: no change.** P1, P2 and P3 do not leak. No second gate is required for correctness today.

**A documented bot behaviour has never fired, and its tuning is fiction.** `AirStrikeNeedWeight: 100`, `AaWeakThreshold: 2000`, `NeedBudgetReservePct: 200` (`ai.yaml:1440-1442`, `:1483-1485`) have never influenced a match, and `CompositionNeedMath.AirOpportunityScore` has never selected a purchase. Anyone tuning those numbers is tuning a no-op. This should be said out loud somewhere the next person will see it.

**It is a loaded gun, and the trigger is one line.** Either making a fixed-wing buildable, or simply dropping `a10`/`frog` from `AirStrikeUnits`, immediately re-points the window at `heli`/`mi28` — which then rides the FIFO lane that applies **no `UnitsToBuild`, no `UnitLimits`, no `UnitDelays`** (`:1561-1587`) and is **exempt from the composition ceiling** (step 3). That is precisely the uncapped second heli source. It is dormant, not absent, and nothing in the YAML says so.

## Two further proposals

**P15 — filter `RequestCheapestBuildable` on actual buildability. Engine C#. Trivial. Low risk.**
`AdaptiveProductionBotModule.cs:561`: intersect the pool with the player's queue `BuildableItems()` names instead of `Rules.Actors.ContainsKey`. This converts a silent permanent no-op into either a real behaviour or an honest nothing. **Ship it with eyes open**: on the current roster it makes the window start buying `heli`/`mi28`, so it must land *after* P16 or together with it, never before.

**P16 — a type absent from `compositionTypes` must not be treated as unbounded. Engine C#. Small. Medium value, and it generalises.**
`RequestIsOverCompositionCeiling` reads "no composition slot" as "no ceiling" (`:1750-1752`). For a type deliberately excluded from composition — which is exactly the helicopter situation — that is backwards: **absence of a share becomes absence of a bound.** This is the same root as §1.1(b), reached by a different path. Suggested shape: an explicit `FifoUnslottedPolicy` (default `Admit`, preserving baseline and `@stable`) with a `Refuse` setting used by `@experimental`, so an unslotted expensive type cannot be bought on the demand lane at all. Cheap insurance that makes P15 safe.

**Revised recommendation:** P15 and P16 are not urgent, because nothing leaks today. They are what stops the next person's innocuous-looking edit from silently re-creating the exact bug this recon was written about.
