# The procurement ordering axis already exists — it shipped 2026-08-15 and was never measured against the user's own game

**Repo state:** `main @ 0475fb9a`, worktree `wt/bot-procurement`. **No code changed.** Read-only status
recon plus a measurement spec.

**Dispatched to:** supply "the missing ordering axis" for PIPELINE 63 / 64 / 66, on the diagnosis that
*"the procurement system has a notion of HOW MANY and no notion of WHEN."*

**Finding: that premise is false as of 2026-08-15.** The WHEN axis was built, enabled on both
`@experimental` factions, and measured with pre-registered predictions two days ago. Writing a second one
would be the overcorrection the brief itself warns against — a fourth report with a fifth unit's name on
it. **The open work is a measurement, not a mechanism.**

---

## 1. What is already shipped and ON

All of the following are live in the two `@experimental` faction blocks. None are defaults; every one is
an explicit YAML opt-in, and every one is an ordering construct rather than a quantity.

| Mechanism | Config | Axis it supplies |
|---|---|---|
| `GateResupplyOnAmmoNeed: true` | `ai-america.yaml:202`, `ai-russia.yaml:141` | no truck while nobody is dry |
| `SupplyDemandSizing: true` | `:222`, `:148` | fleet size follows live starving-customer count |
| `SupplySizeFromNeed: true` | `:304`, `:176` | sizes from the bar at which a customer is *served*, not a second independently-tuned bar |
| `SupplyPrecedenceStallCycles: 4` | `:305`, `:177` | **precedence** — bank cash, buy nothing, until the truck is affordable |
| `SupplyTruckFloor: 0` | `:240`, `:156` | no standing floor, so no t=0 truck |
| `UnitFloorPer: medi.* 20` | `:168`, `:123` | the medic floor gets a **denominator**: `min(2, supported/20)` |
| `UnitFloorSupportedTypes` | `:178`, `:129` | the denominator population, symmetric across factions |
| `UnitDelays: aa.* 2000` | `:199`, — | hard capability ban, all three call-in sites |

`SupplyPrecedenceStallCycles` is the item-66 ruling implemented literally. Its `[Desc]`
(`UnitBuilderBotModule.cs:180-205`) quotes the user — *"soldiers out of ammo are useless. That should be
the first priority to solve at all times"* — and names the exact gap the brief describes: *"a pre-empt
that merely SKIPS when it cannot afford the item is not a priority, because the cycle then falls through
and buys a rifleman with the very cash the truck was waiting for."*

`UnitFloorPer` is the item-63 fix, and its `[Desc]` (`:58-77`) states the general form the brief asks
for: *"THIS FIXES A GENERAL DEFECT, NOT A UNIT… At t=0 every census is zero, so every floor is maximally
unmet at exactly the moment its need is lowest."* That is the same sentence as the brief's diagnosis,
written two days earlier by the worker who fixed it.

**Provenance** — all three merged, all ancestors of `main`:

- `eaa00471` 2026-08-15 *"supply: give procurement a precedence axis so dry soldiers outrank cheap buys"*
- `10e07d0e` 2026-08-15 *"composition: a standing floor with no denominator is an opening buy order, not a minimum"*
- `4b6da6db` 2026-08-15 *"supply: rebuild the bank bound as a cash-progress predicate, and retract three claims"*
- merges `d54671d6` (`wt/build-order`) and `1bbfdb7c` (`wt/truck-precedence`)

**PIPELINE was never updated.** Items 63 and 66 still read `[IN FLIGHT wt/build-order]`. That stale tag is
the most likely reason this work was dispatched a second time.

## 2. It was measured, and the numbers are on record

Contrary to the brief's expectation that bot changes ship with no outcome numbers, this pair shipped with
pre-registered predictions and recorded misses.

`449d613b`, seed 4242, paired against a baseline of **zero trucks for either player across a whole match**:

```
first truck ordered   predicted tick 1980  ->  actual 1980
banking spell at buy  predicted 27         ->  actual 27
longest stall run     predicted 2          ->  actual 2   (tolerance 4)
```

`50b79fa6` then took it to a two-arm result: **USA orders at tick 1980, Russia at 3030, both trucks reach
the field.** The authors also recorded predictions that *missed* — the Russia tick (predicted ~2550-2650,
actual 3030) and a "`spent` flat while banking" test that failed outright.

So the responsiveness measure the brief asks for partly exists. What does **not** exist is a
before/after on the user's own game.

## 3. What is genuinely still open — from the authors' own stated limits

1. **It has never been measured in a lobby game.** `1bbfdb7c` closes with: *"nothing here says this
   reproduces in lobby games rather than tournament map-players."* **The user plays lobby games.** This is
   exactly item 66's own first candidate ("the economy fix reached the tournament/map-player path but not
   the profile the user plays") and it is still unresolved. **This is the highest-value run available.**
2. **The progress predicate guards a hard stall, not a slow drain.** An intermittently-drained balance
   keeps setting new highs and keeps resetting the stall counter. Tolerance cannot fix it — 3 abandons
   Russia a cycle earlier, 2 breaks USA.
3. **A residual drain is unattributed.** The `BuildUnit` hold covers all three sibling `UnitBuilder`
   instances; Russia still spent 550 and USA 200 while banking, from another module type. `BaseBuilder` is
   named as *a candidate, not a finding*.
4. **Tempo cost, stated and unpriced against gameplay:** Russia's truck costs ~27 cycles / ~810 ticks
   (~32 s) of near-silent production on a poor economy. Bounded and visible, measured on one map.

None of these is a missing ordering axis. (1) is an instrument question and (2)–(4) are properties of a
mechanism that works.

## 4. Choosing a map: the parity rule inverts once you account for the offset

The sibling `ResolveStagingAnchor` finding (`260817-combined-arms-rendezvous-postmortem.md:98-121`) is
that the degenerate-case guard **can only fire when both Supply Route coordinates are odd** — and when it
does *not* fire, a **false anchor is published**. So odd/odd is the **healthy** case, and the bug afflicts
**3 placements in 4**, not 1 in 4.

**The trap: Supply Route cell ≠ spawn cell.** `SpawnStartingUnits.cs:91` places the base actor at
`p.HomeLocation + unitGroup.BaseActorOffset`, and `MapStartingUnits.cs:37` defaults that offset to
`CVec(-1,-1)`. No `world.yaml` block overrides it (0 occurrences) and no map places a `supplyroute` actor
explicitly (checked river-zeta, twin-rivers, polar-disorder: 0 each). **So the offset flips both
parities, and a healthy SR requires an EVEN/EVEN spawn.**

Reading the parity off the spawn cell directly gives the opposite answer. Worked example:
`siberian-pass-ww3` has spawns `95,15` and `1,51` — both odd/odd, and it is the only 2-player map where
*both* spawns are. It looks like the ideal clean map and is in fact the worst case: its SRs land at
`94,14` and `0,50`, both even/even, **both polluted**.

Spawn → SR parity for every playable map:

| map | spawn | SR (spawn−1,−1) | anchor |
|---|---|---|---|
| **twin-rivers-ww3** | **112,92** | **111,91** | **healthy** |
| **twin-rivers-ww3** | **112,28** | **111,27** | **healthy** |
| twin-rivers-ww3 | 1,22 / 1,92 | 0,21 / 0,91 | polluted |
| river-zeta-ww3 | 16,6 | 15,5 | healthy |
| river-zeta-ww3 | other five | — | polluted |
| polar-disorder-ww3 | 96,16 | 95,15 | healthy |
| polar-disorder-ww3 | 1,81 | 0,80 | polluted |
| seventh-woods-ww3 | 92,112 | 91,111 | healthy |
| x-lake-ww3 | 128,108 | 127,107 | healthy |
| siberian-pass-ww3 | both | 94,14 / 0,50 | **both polluted** |
| woodland-warfare-ww3 | both | — | both polluted |
| nuclear-winter-ww3 | both | — | both polluted |

**`twin-rivers-ww3` is the only map where two players can both sit on a healthy anchor** — spawn slots at
`112,92` and `112,28`. It is the right measurement map *if* spawn slots can be pinned; if they cannot,
every other map gives at least one polluted player.

**Caveat that may make all of this moot:** a fix for the anchor is in flight on `wt/combined-arms-*`. If
it merges before the run, map choice stops mattering and any map may be used. **Check that first** — it is
cheaper than pinning spawn slots.

## 5. The run to schedule, and the bar, pre-registered

**One before/after pair is not the right shape here, because there is no "before" to build — the change
already shipped.** What is missing is a reading on the profile the user plays. Requested:

**Run A — lobby game, `@experimental` vs `@experimental`, `twin-rivers-ww3`, spawn slots `112,92` and
`112,28`, one match to a verdict or ~30 min.** This is the arm that has never been run.

**Run B (only if a paired control is wanted) — the same lobby configuration at the same seed with
`SupplyPrecedenceStallCycles: 0`** on both factions. That single flag reverts precedence while leaving the
demand gate and sizing intact, so it isolates the axis under test rather than the whole branch.

### Metrics, all readable from one `debug.log`

| # | metric | how |
|---|---|---|
| M1 | **responsiveness** = first `[supply]` truck order tick − first `ammo-need=True` tick, per player | `[composition]` census carries `ammo-need=`; the order tick is the truck's `LogPick` |
| M2 | opening call-in order, first 6 buys per player | `[composition]` pick lines |
| M3 | tick of first medic, and own infantry count at that tick | census `medi.*` and infantry terms |
| M4 | truck count alive over time, and `earned>0` | census `truk` term (`inWorld+inCargo`) |

### Pass bar — stated before the run

- **M1 ≤ 1200 ticks (~48 s) for BOTH players.** This is the responsiveness axis and the primary bar.
- **M2: zero medics and zero trucks in the first 4 call-ins**, both players. Guards the t=0 overcorrection.
- **M3: first medic only at ≥20 own infantry** (the user's own ratio, and what `UnitFloorPer: 20` encodes).
- **M4: `truk ≥ 1` alive at some point**, both players. This is the item-56 **instrument check** — a match
  where no truck is ever bought is an instrument failure, not a negative result, and must not be recorded
  as one.

### My predictions, registered

- **M1 USA: PASS.** Measured 1980 against an `ammo-need` onset previously logged near 1240 ⇒ ~740 ticks.
- **M1 Russia: FAIL, at roughly 1700–1800 ticks.** Russia ordered at 3030 on the tournament path and its
  banking spell is the long one. **I expect the primary bar to fail on one of two arms**, and I am
  registering that rather than setting the bar at 2000 where both would pass. If Russia comes in under
  1200 in a lobby game, that is itself the answer to open question (1) — a lobby economy is richer, the
  bank crosses sooner, and the tournament figure was pessimistic.
- **M2, M3: PASS both players.** `UnitFloorPer` was verified offline via `--composition-plan`, and the
  denominator is symmetric across factions.
- **M4: PASS.** Two-arm truck delivery is already on record at seed 4242.

**If M1 fails on Russia, do not raise a quantity.** The named cause is the unattributed residual drain
(§3.3), and the fix is to attribute that spender — not to widen the stall tolerance, which is already
measured as unwidenable in both directions.

---

# ADDENDUM 2026-08-17, after the run was granted: the run is NOT EXECUTABLE, and my own §5 prediction is refuted

**The run was granted and I did not spend it.** Two findings, and the second one retracts the bar I
registered above.

## A. There is no way to run a bot-vs-bot LOBBY game headlessly

- `Launch.Map` → `Game.LoadMap` (`Game.cs:1176-1197`) → `CreateAndStartLocalServer` →
  `JoinServer(CreateLocalServer(mapUID), "")` at `Game.cs:287`. `CreateLocalServer` takes
  `bool isSkirmish = false` (`Game.cs:1140`) and **the launch path never passes true.**
- `SkirmishLogic.ClientJoined` — the one file-driven lobby restore, which reads
  `skirmish.ww3mod.yaml` from the support dir and can set Map, Options, faction, team **and
  `SpawnPoint`** per slot (`SkirmishSlot`, `:25-45`) — early-returns on
  `server.Type != ServerType.Skirmish` (`:167-168`). So it never fires on a `Launch.Map` run.
- Every other `slot_bot` issuer is `Widgets/Logic/Lobby/*` — lobby UI. Driving it needs synthetic
  input, which is a standing refusal in this project.

So the granted configuration requires either a harness change (make the launch path create a
skirmish-type server, or add a `Test.LobbySetup` hook issuing `slot_bot` + spawn/faction orders before
the existing `state Ready`) or UI automation. **Neither is a run, and neither was granted.**

## B. A tournament substitute would answer a DIFFERENT question — established, not assumed

I checked this before considering a substitute, because spending the run on the wrong economy is worse
than not running.

`b91b5a88` (the map-player economy fix) states it directly: *"`Playable` … reads 'occupies a lobby slot'
… **No shipped map declares a `Bot:` map player, so the shipped game was never affected**"*, and
*"`Player.Playable` is `true` for every client-slot player"*.

**Therefore a lobby bot has ALWAYS had an economy.** The `56bf7355` finding that anchored this whole
subsystem — *"the gate is fine; the bot is broke"*, `cash=0` on 194 of 195 snapshots where a truck was
wanted — was measured on the **map-player path with the economy bug live**. It was never a property of
the game the user plays.

## C. Consequence: the merged precedence fix is INERT on the user's profile — this refutes my §5

`PlayerResources` defaults (`PlayerResources.cs:32,63,66`) are **`DefaultCash = 20000`** and
**`PassiveIncome = 100` every 50 ticks**; `player.yaml:164-169` leaves all of them commented out, so the
engine defaults stand and both are lobby dropdowns the user sets.

`ShouldBankForSupply`'s own comment (`UnitBuilderBotModule.cs:1096-1098`): *"True only when the fleet is
short and the truck is **genuinely unaffordable** — if we could afford it, `ChooseSupplyFleetShortfall`
would already have bought it and we would never be asked."*

**A 1000-cost truck against 20,000 starting cash is never unaffordable, so the bank never engages.**
`SupplyPrecedenceStallCycles: 4` — the mechanism merged for item 66, the one that implements the user's
precedence ruling — **cannot fire in a default lobby game.**

**My §5 prediction is wrong, and wrong structurally rather than numerically.** I predicted USA ~740 PASS
and Russia ~1700–1800 FAIL on the strength of banking spells. In a lobby game neither player banks at
all. Corrected prediction: **M1 is small for both players** — bounded by the `GateResupplyOnAmmoNeed`
onset and the build-cycle interval, not by affordability — and the 27-cycle / ~810-tick tempo cost
recorded in `1bbfdb7c` does not apply to the user either.

## D. What this does to item 66

The user's report — *"almost no supply trucks being built"* — came from lobby play, where the bot was
never broke. **The merged fix targets affordability, and affordability was a tournament-path artifact.**
So item 66 should be treated as **still open on the user's profile**, with the affordability explanation
eliminated rather than confirmed. Remaining candidates, none tested:

1. `DesiredTrucks` returns 0 because the demand predicate reads low in lobby conditions
   (`SupplySizeFromNeed: true` was the robustness fix for exactly this and is unverified in lobby).
2. `UnitLimits` truk cap, or the composition ceiling striking the slot.
3. The truck IS bought promptly in lobby and the user's complaint is about *fleet size* rather than
   first-order latency — in which case M4, not M1, is the metric that matters.

**Do not raise a quantity to chase this.** That is the exact pendulum this item is made of.

## E. What I recommend instead

The cheapest instrument that answers D is **not a match at all**: `--composition-plan` already replays
the shipped argmax offline (it is how `UnitFloorPer` was verified), and a run of it at a lobby-realistic
starting balance would show whether the truck is ever selected when affordability is removed as a
blocker. That costs no game session and no serial-queue slot. If a live match is still wanted afterwards,
the harness hook in §A should be built first so the lobby arm is actually reachable.
