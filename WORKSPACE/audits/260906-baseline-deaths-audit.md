# Why `kills_cost − deaths_cost` is negative for both players — deaths audit

**Read-only audit, `wt/deaths-audit` at `main @ 875c499b`.** Every repo claim below is stamped
against that SHA. Corpus: the 2026-09-05 `@stable` re-baseline
([`../benchmarks/260905-rebaseline.md`](../benchmarks/260905-rebaseline.md), pipeline item 43),
verdict version 8, engine under test `bb89f9fd`, stamped `9cb423d4`. Result dirs read from the
main checkout at `C:/Users/fredr/Desktop/WW3MOD/tools/autotest/tournament-results/260905_rebaseline_*`
(untracked). No game was launched, no test run, nothing built.

## Answer in one paragraph

**Both. It is one accounting rule meeting one deliberate game mechanic, and the mechanic is the
dominant term.** `PlayerStatistics.cs:335` charges `DeathsCost` on *every* death, unconditionally;
`:341-342` then returns early when `e.Attacker` is null or is the victim itself, so `KillsCost`
(`:362`) is only credited for deaths delivered by *another actor*. WW3MOD's doom model makes the
victim itself the usual deliverer of the final blow: `ChangesHealth.Tick` applies its drain as
`self.InflictDamage(self, …)` (`engine/OpenRA.Mods.Common/Traits/ChangesHealth.cs:86`), and every
combat family carries such a drain below 50 % HP — vehicles (`vehicles.yaml:184`), infantry
(`infantry.yaml:1130`), helicopters (`aircraft.yaml:259`), plus the fire ladder
(`infantry.yaml:967-1020`). On top of that, `AutoTarget` **deliberately stops shooting** anything
wearing `critical-damage` (`AutoTarget.cs:244`, enforced `:1467-1472`), so the shooter that did the
work hands the kill to the drain by design. Measured over all 40 matches: **40.3 % of deaths
(3,068 of 7,616) have no attacker at all, and they carry 49.5 % of all value lost ($1,285,650 of
$2,599,250)** — which is exactly the missing half of the ledger. The loss is close to symmetric
between profiles (41.6 % of `@experimental`'s own deaths uncredited vs 43.0 % of `@stable`'s on
S2), so it does not bias the win rate; it does bias the S2 swing metric, by an estimated
~$4,500/match **against `@experimental`**, which is the same order as the effect that metric was
reporting.

## 1. What the verdict counts

The verdict is produced entirely in C#. There is no Lua: the tournament scenarios ship `map.yaml`
+ `rules.yaml` + `tournament-*.yaml` and no script
(`tools/autotest/scenarios/tournament-s1-eco-cal-nn/`).

| field | written at | fed from |
|---|---|---|
| `kills_cost` | `BotVsBotMatchWatcher.cs:612` | `PlayerStatistics.KillsCost` (`PlayerStatistics.cs:45`) |
| `deaths_cost` | `BotVsBotMatchWatcher.cs:613` | `PlayerStatistics.DeathsCost` (`:46`) |
| `units_killed` / `units_dead` | `:608` / `:609` | `:48` / `:49` |
| `unit_types[].lost_count/lost_cost` | `:652-653` | `UnitTypeTelemetry.Lost` (`:233`) |

Everything is driven by one handler, `UpdatesPlayerStatistics.Killed`
(`engine/OpenRA.Mods.Common/Traits/Player/PlayerStatistics.cs:317-372`). Its shape is the whole
finding:

```
:319   if (self.Owner.WinState != WinState.Undefined) return;   // dead-player deaths not counted
:335   playerStats.DeathsCost += cost;                          // UNCONDITIONAL
:338   playerStats.UnitTypeStats.Lost(actorName, cost);         // same line, same condition
:341   if (e.Attacker == null || e.Attacker == self) return;    // <-- THE GATE
:352-357   else if (IPositionableInfo) { attacker.UnitsKilled++;  playerStats.UnitsDead++; }
:360   if (!self.Owner.NonCombatant) attackerStats.KillsCost += cost;
```

`DeathsCost` is above the gate. `KillsCost`, `UnitsKilled` **and `UnitsDead`** are all below it.
That is why the *counts* look perfectly zero-sum while the *costs* do not: both halves of the
count live on the same side of the gate, so an attacker-less death is invisible to `units_dead`,
while its cost still lands in `deaths_cost`. Verified: `units_killed` summed over both players
equals `units_dead` summed over both players in **all 40 matches**; `deaths_cost` equals
`Σ unit_types[].lost_cost` exactly in **all 80 player rows**.

### Case by case

| event | counted as a death? | credited as a kill? | why |
|---|---|---|---|
| shot dead by an enemy unit | yes | yes, to the enemy | normal path |
| **bleed-out / burn-out below 50 % HP** | **yes** | **no** | `ChangesHealth.cs:86` passes `self` as attacker → gate at `:341` |
| **helicopter crash-impact / unsafe landing** | **yes** | **no** | `HeliEmergencyLanding.cs:368`, `:378` — `self.Kill(self)` |
| **helicopter safe-landing burn-out** | **yes** | **no** | `ChangesHealth@CrashBurn` (`aircraft.yaml:259`); the airframe stays on its own team — `TransferToNeutralOnSafeLanding: false` (`aircraft.yaml:253`) |
| passengers inside a destroyed transport | yes | yes, to the transport's killer | `Cargo.cs:1227`, `:1234` pass `e.Attacker` through |
| passengers inside a **crashing** helicopter | yes | yes — **to their own owner** | attacker is the heli (`HeliEmergencyLanding.cs:368`), same player; `e.Attacker != self` so the gate passes |
| friendly fire | yes | yes, to the shooter — the same player | no ally check anywhere in `:341-371`; net zero on that player's diff but inflates both sides of it |
| crushed by a vehicle | **never happens** | — | `Crushable.cs:92` is commented out; mines (`Mine.cs:50`) and `Passable.cs:108` do credit the crusher |
| husk burning out | **no** | no | husks carry neither `Valued` nor `UpdatesPlayerStatistics` (`mods/ww3mod/rules/husks/husks.yaml`) — no double count |
| transform / deploy / undeploy | no | no | `Activities/Transform.cs:119` is `self.Dispose()`; `Disposing` (`PlayerStatistics.cs:434-451`) touches army/assets value only |
| **supply truck / cache despawning empty** | **no** | no | `SupplyProvider.Tick` disposes below `RemoveBelowSupply` ([`economy.md`](../../DOCS/reference/economy.md) §"Serves down to empty") — a real $1,000 loss the ledger never sees |
| unit leaving the map (e.g. `tran`) | no | no | Dispose path. In `s1_cal_b` m1 USA `tran` is `produced 1 / alive 0 / lost 0` |
| crew evacuating to bank rank | no | no | `CreditsRankOnEvacuation : INotifySold` (`:25`) — the sell/dispose path |
| crew killed by their own wreck's blast | cannot happen | — | `VehicleCrew.cs:392-393` returns without spawning when the spall damage would be lethal, so the `crew.InflictDamage(self, …)` at `:469` is sub-lethal by construction. The own-player kill credit this would otherwise produce never fires. |
| deaths after a player's `WinState` resolves | no | no | `:319`. Irrelevant here — all 40 matches ended `time_limit` with both players `Undefined` |
| scripted removals | none exist | — | no Lua in the tournament scenarios |

## 2. Reconciliation — two matches

**There is no per-death event ledger to reconcile against.** The engine writes no
`killed`/`died`/`destroyed` line: `match_1_debug.log` in `s1_cal_b` (1.2 MB, 8,548 lines) contains
exactly three death-adjacent tags — `[husk-settle]`, `[exp-capture] tecn-killed`, and `[exp-crew]`
— none of which names an attacker. So the requested cause→attacker table cannot be built by
grepping the logs. **What follows is reconciled instead from the verdict's own `unit_types`
telemetry, which is an exact, complete per-death ledger by actor type**, plus one independent
cross-check from the logs.

The identity used throughout:

* **uncredited deaths (count), exact per player** = `Σ lost_count − units_dead` — a death is in
  this set iff it passed `:335` and failed the gate at `:341` (or the actor has neither
  `BuildingInfo` nor `IPositionableInfo`; no such actor appears in either match).
* **uncredited value, exact per match** = `Σ deaths_cost − Σ kills_cost` over both players — every
  attacker-credited death lands in exactly one of the two players' `kills_cost`, including
  friendly fire.

Independent cross-check that the class split below is complete: `[husk-settle]` fires once per
vehicle/helicopter wreck. In `s1_cal_b` it matches the telemetry's vehicle+heli death count
**exactly in 9 of 10 matches** (m1 14=14, m2 11=11, m4 12=12, m5 13=13, m6 16=16, m7 12=12,
m8 15=15, m9 10=10, m10 9=9; m3 15 vs 14). In `s2_exp` m1 it is 74 vs 73.

### Match A — `260905_rebaseline_s1_cal_b/match_1.json`, seed 1017, Stable v Stable, 7,500 ticks

`USA-bot` kills 3,550 / deaths 10,350 · `Russia-bot` kills 2,900 / deaths 12,900.
**Σ deaths 23,250 − Σ kills 6,450 = $16,800 uncredited (72 % of all value lost).**
Uncredited death count: USA 29−19 = 10, Russia 36−20 = 16 → **26 of 65 deaths (40 %)**.

| class | deaths | value lost | counted as a death? | credited as a kill? |
|---|---:|---:|---|---|
| vehicles (`abrams`, `bradley`, `humvee`, `m113`, `mnly`, `msar`) | 13 | 12,900 | yes | **mostly no** — bleed-out below 50 % HP |
| helicopter (`littlebird`) | 1 | 3,000 | yes | **mostly no** — crash-burn / crash-impact |
| infantry incl. ejected crew | 46 | 7,350 | yes | mostly yes — slow drain, usually finished by fire |
| drones (`quadcopterdrone`) | 5 | 0 | yes (count only) | unknown — cost 0, invisible in the value ledger |
| husks, transforms, truck despawns, map exits | 0 | 0 | **no** | no |
| **total** | **65** | **23,250** | — | 39 credited / **26 uncredited** |

**Bound (rigorous, no assumption about which individual died how):** the 26 uncredited deaths must
be drawn from this pool. Even if every one of them were the most expensive *non*-vehicle death
available, that set could carry at most $5,350. So **vehicles and aircraft must supply at least
$11,450 of the $16,800 — ≥68 % of the uncredited value, and ≥72 % of all vehicle/aircraft value
lost in the match is uncredited.** The gap is also *feasible* without invoking any third-party
attacker: the 26 most expensive deaths in the match carry $19,400 ≥ $16,800, so nothing needs to
have been credited to Neutral to close the books.

Uncredited deaths cost **~$646 each against ~$165 for credited ones — 3.9×**. The expensive things
die to themselves; the cheap things get shot.

### Match B — `260905_rebaseline_s2_exp/match_1.json`, seed 1017, Exp (Russia slot) v Stable, 18,000 ticks

`USA-bot` (stable) kills 29,000 / deaths 53,100 · `Russia-bot` (exp) kills 27,750 / deaths 57,600.
**Σ deaths 110,700 − Σ kills 56,750 = $53,950 uncredited (49 %).**
Uncredited death count: USA 127−76 = 51, Russia 177−110 = 67 → **118 of 304 deaths (39 %)**.

| class | deaths | value lost | counted as a death? | credited as a kill? |
|---|---:|---:|---|---|
| **supply trucks (`truk`)** | **35** | **35,000 (32 % of the match)** | yes | **mostly no** — unarmed, `^WheeledVehicle`, bleeds out below 50 % |
| other vehicles (`humvee`, `bradley`, `abrams`, `mnly`, `m113`) | 33 | 23,400 | yes | mostly no — bleed-out |
| helicopters (`littlebird`, `heli`) | 5 | 18,000 | yes | mostly no — crash-burn |
| infantry incl. ejected crew | 223 | 34,300 | yes | mostly yes |
| drones | 8 | 0 | yes (count only) | invisible in the value ledger |
| **total** | **304** | **110,700** | — | 186 credited / **118 uncredited** |

**Bound:** the 118 uncredited deaths could carry at most $29,700 if they were all non-vehicle, so
**vehicles and aircraft supply ≥ $24,250 — ≥56 % of the uncredited value, and ≥39 % of all
vehicle/aircraft value lost.** Feasible without third-party credit (top 118 deaths carry $90,900).

**The single largest loss line on this rung is the supply truck, on both sides.** Across the
corpus: 10.8 % of all value lost in `s1_cal_b`, 5.3 % in `s1_exp`, **21.2 % in `s2_cal` and 25.6 %
in `s2_exp`**. An unarmed $1,000 logistics vehicle that inherits `^Vehicle`'s doom drain, driving
between the SR and the front, is the cheapest thing on the map to push below half HP and then
abandon.

### How far this gets, and where it stops

The class-level books close: uncredited value and uncredited count are both exact, the bound on
the vehicle share is rigorous, and the vehicle death counts are corroborated independently by
`[husk-settle]`. What is **not** established is the split *within* the uncredited set — how much
is the 50 % bleed-out, how much is helicopter crash-burn, how much is the fire ladder on crew who
inherited their wreck's burn stacks (`VehicleCrew.cs:455-458`). Those three all route through the
same `e.Attacker == self` gate and are indistinguishable in the artefacts.

## 3. Verdict — (a), (b), or both

**Both, with (a) dominant.**

**(a) A real, deliberate, non-combat killing blow — CONFIRMED.** WW3MOD's doom model is explicit
and documented in-tree: *"`critical-damage` is not a damage tier in WW3MOD, it is a DOOM MARKER:
every family that carries it is already dying without further help"*
(`mods/ww3mod/rules/ingame/structures.yaml:75-81`), and *"The marker is read as a REFUSAL TO
SHOOT"* (`:83-84`). The drain is `self.InflictDamage(self, …)` (`ChangesHealth.cs:86`); the refusal
is `AutoTarget.cs:244` + `:1467-1472`. **The dominant mechanism is the vehicle drain:**
`PercentageStep: -1, Delay: 5, StartIfBelow: 50` (`vehicles.yaml:184-187`) = 250 ticks from half HP
to zero, versus infantry's `Delay: 50` (`infantry.yaml:1130-1133`) = 2,500 ticks. Ten times faster
on the units that cost ten times more — which is precisely the shape the data shows.

**(b) An accounting asymmetry — CONFIRMED, and it is the `DeathsCost`/`KillsCost` line order, not a
bug in the scorer.** `PlayerStatistics.cs:335` vs `:341`. Note the asymmetry is *internally
inconsistent*: `units_dead` sits below the gate and so silently agrees with `units_killed`, while
`deaths_cost` sits above it and does not. A reader comparing the count columns concludes the ledger
balances; the cost columns say otherwise. That is the trap the re-baseline hit.

**HYPOTHESIS (unproven): most uncredited *helicopter* value is crash-burn rather than being shot
out of the sky.** `littlebird` at $3,000 and `heli` at $6,000 are the priciest single deaths in the
corpus and `HeliEmergencyLanding` gives them two separate uncredited exits. *Check that would
settle it:* add the attacker's name (or `self`) to a death diagnostic behind the existing `diag()`
in `BotVsBotMatchWatcher`, then re-run seed 1017 — the sim is deterministic per seed (the
re-baseline record establishes this: `s1_cal` and `s1_cal_b` are byte-identical), so one match
reproduces the exact ledger above with causes attached.

**REFUTED: supply starvation.** The re-baseline's stated hypothesis was *"supply starvation
attrition and/or non-combat losses"*. The first half does not exist — no supply or ammo trait in
the engine inflicts damage or calls `Kill`, and the mod defines no health drain gated on empty
ammo. Running dry makes a unit useless, never dead. (The truck finding is adjacent but is a
different thing: trucks *are* shot, then bleed out.)

**Also ruled out** as contributors: husk double-counting (no `Valued`), transforms including the
LC 2×2 crane (Dispose), crushing (commented out), scripted removal (no Lua), off-map departure and
evacuation (Dispose). Friendly fire is credited to the shooter and is net-zero on that player's own
diff, so it cannot produce a negative sum either; it is unmeasurable from the four published counts
(`units_killed(A) − units_dead(B)` yields only the *difference* of the two players' self-kills) and
remains the one unquantified residual in the per-player split.

## 4. Does it move the two profiles equally?

Near-symmetric on the win rate, **materially asymmetric on the swing metric.**

| batch / profile | own deaths uncredited | value per credited kill |
|---|---:|---:|
| `s1_exp` @experimental | 80 / 217 = **36.9 %** | $351 |
| `s1_exp` @stable | 87 / 207 = **42.0 %** | $373 |
| `s2_exp` @experimental | 672 / 1,617 = **41.6 %** | $299 |
| `s2_exp` @stable | 736 / 1,712 = **43.0 %** | $312 |

Both profiles lose ~40 % of their units to something that credits nobody, so this does not decide
games and the 3/10 and 2/10 win rates stand untouched. But the *swing* metric
(`kills_cost − deaths_cost`, the S2 ladder metric) under-credits each bot in proportion to how its
**opponent's** units die, and those are not equal. Estimating uncredited value borne per side
(`deaths_cost(P) − kills_cost(opponent)`, exact up to the friendly-fire residual above):

* **S1:** exp 46,650 vs stable 47,750 over 10 matches — a wash, ~110/match.
* **S2:** exp 260,550 vs **stable 305,450** — stable's losses go uncredited by 44,900 more, i.e.
  **`@experimental`'s kills are under-credited by ~$4,490 more per match than `@stable`'s.**

The re-baseline reports the S2 swing Δ as median +7,300 / mean +3,020 in Exp's favour. **The
correction is the same size as the signal, and points the same way** — Exp's combat parity on S2
is, if anything, slightly understated. It stays well inside the ±$46,300 calibration noise band, so
this changes no conclusion in that card; it does mean the swing metric should not be read at finer
resolution than the bias.

**Consequence for the ladder:** `kills_cost − deaths_cost` is not a combat scoreboard. Its absolute
level is ~50 % attrition that nobody was credited for, and ~a quarter of its S2 magnitude is
supply-truck attrition rather than army-versus-army fighting. As a *paired difference within one
match* it is still usable, with a per-match bias of a few thousand.

## Not verified

* **The re-baseline card says "38 of 40". The correct figure is 35 of 40** matches with both
  players negative (75 of 80 player rows). The five exceptions each have exactly one positive
  player: `s1_cal_b` m3 and m9, `s1_exp` m4, m7 and m9 — all on S1, none on S2.
* **No per-death causal attribution exists in the artefacts**, so the split *inside* the uncredited
  set (vehicle drain vs heli crash-burn vs crew fire) is bounded, not measured.
* **Friendly fire is unquantifiable** from the published fields, which leaves the per-player
  uncredited-value split an estimate. The per-match totals are exact.
* **The `heli` actor** (USA, $6,000, one death in `s2_exp` m1) was classified as an aircraft from
  its name and cost only; its rules were not read.
* **Only two matches were reconciled in detail**, as scoped. The corpus-wide numbers (40.3 % /
  49.5 % / the profile table) are computed over all 40 but are class-level, not per-death.
* **Nothing was rebuilt or re-run.** Every engine claim is a reading of the source at `875c499b`,
  which the re-baseline record establishes is one docs-only commit plus later work away from the
  `bb89f9fd` tree the corpus was produced on; the traits cited here were not touched in between,
  but that was checked by reading, not by diffing every path.
* **Tick→second narration.** `TournamentConfig.cs:101` converts `TimeLimitSeconds * 25`, while the
  mod's default game speed is `Timestep: 60` = 16.667 tps (`mods/ww3mod/mod.yaml:382`,
  `rules/player.yaml:192`). The harness forces `gamespeed override: fastest` (`Timestep: 40` =
  25 tps), so the conversion is self-consistent *for the harness* — but the same 7,500 ticks is
  450 s at the mod's normal speed, and every duration quoted in this file is in ticks for that
  reason. Not investigated further; flagged because it sits adjacent to the known 25-tps
  documentation error class.

**Ref stamp:** `wt/deaths-audit` at `main @ 875c499b`, 2026-09-06. Corpus `9cb423d4` / code
`bb89f9fd`.
