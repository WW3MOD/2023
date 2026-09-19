# DEFCON Escalation — whole-match gameplay review

**Ref:** `wt/escalation-review @ 442859aa` (forked from `main @ 442859aa`). Every file:line below was read
at that ref. No game was launched *by the author*; the runs are specified in §"Runs requested" and
executed by the manager, and their readings are folded into §"Simulation results".
**UPDATED 2026-09-20 with R1, R2, R3 and R4 — all four are in.** They corrected §2.4's headline
number (DEFCON 2 lasts **98 ticks**, not the one tick predicted), showed that the bots never form a
front during Positioning at all, and turned up one finding nobody asked for: on the shipped settings
an even bot-vs-bot match reaches its nuclear phase and then never uses it.

**§B1 IS NOW RULED AND CLOSED**, on a fifth run: R2 repeated with R1's seed, which is the only
controlled comparison in this document. It rejects b1a — holding the border at DEFCON 2 makes the
cease-fire **shorter**, 38 ticks against 98 — and closes §B1 with "there is no geometric fix".
**§B6 is the single top item in §B.** Everything in §B is ready to rule on.

**Scope.** Started as the backlog item "tune the three DEFCON pace durations" and was widened by the
user to a full stage-by-stage review of the mode as a human would experience it, plus an assessment of
the bots under it. Part 1 is the pace derivation; Part 2 is the review; §A and §B are the split the
user asked for.

**Tick rate is 16.67/s (60 ms timestep).** Never 25. Every duration below is given in ticks and in
mm:ss, and `NuclearUnlockSchedule.TicksForMinutes` multiplies before dividing so the minute clocks are
exact (10 min = 10000 ticks = 600.0 s).

**The first thing to say, because it changes what Part 1 can be.** The three pace durations the backlog
item names **no longer exist**. `ebfa8828` replaced `SlowTicks`/`StandardTicks`/`FastTicks` and the
`defcon-pace` dropdown with two ordinary minute dropdowns the host sets directly
(`DefconEscalation.cs:106-183`), and `MarkAsPlaceholder` is already `false` (`:230`). So Part 1 is not
"pick three numbers"; it is "does the arithmetic support the two defaults and the stops they are chosen
from" — and the answer is **yes, the shipped 5 minutes is the number the deployment math produces**,
which is a better outcome than a retune because it means nothing has to move.

---

# Part 1 — the phase clocks, derived

## 1.1 The three quantities

**Reinforcement latency has three terms, not one.** Units are not built at the Supply Route: `SUPPLYROUTE`
carries `ProductionFromMapEdge` for `Infantry, Soldier, Vehicle, Aircraft, Helicopter`
(`mods/ww3mod/rules/ingame/structures.yaml:472-474`), which spawns the unit on the closest map-edge cell
to the SR's `SpawnArea` and then walks it to the SR, or along the rally path
(`ProductionFromMapEdge.cs:113-137`, `:170-179`). So:

    call-in latency = queue time  +  edge -> SR walk  +  SR -> border drive

**Queue time.** `BuildDuration` is unset on every unit in `vehicles*.yaml` and `infantry*.yaml`, so
`ProductionQueue.GetBuildTime` falls through to `cost / 10` ticks (`ProductionQueue.cs:607-609`).
`ClassicProductionQueue@Vehicle` is **sequential** and `SpeedUp` defaults false
(`ClassicProductionQueue.cs:27`), and a player has exactly one producer, so vehicle build times **add**.
Infantry runs on `ClassicParallelProductionQueue` and is effectively free.

**Speed.** `Mobile.Speed` is WDist/tick and is multiplied by the locomotor's terrain percentage
(`Mobile.cs:857-863`). On `Clear` — which is what an open approach is — every wheeled and tracked class
is **70 %** and `foot` is **90 %** (`world.yaml:41-55`, `:107-191`). One cell is 1024 WDist.
`Util.ApplyPercentageModifiers` truncates (`Util.cs:238-246`), so infantry is 22, not 22.5.

| unit | locomotor | `Speed` | on `Clear` | cells/s | s/cell |
|---|---|---|---|---|---|
| Abrams M1A2 | heavytracked | 90 | 63 | 1.025 | 0.98 |
| T-90 | heavytracked | 100 | 70 | 1.139 | 0.88 |
| Bradley / BMP-2 | lighttracked | 100 | 70 | 1.139 | 0.88 |
| BTR-80 | lighttracked-amph | 110 | 77 | 1.253 | 0.80 |
| Humvee | lightwheeled | 150 | 105 | 1.709 | 0.59 |
| infantry | foot | 25 | 22 | 0.358 | **2.79** |

**The Abrams is the pace-setter of a mixed group and infantry on foot is 2.9× slower than it.** That is
why the group below rides transports: a combined-arms push moves at 1.03 cells/s, and a push that walks
its riflemen moves at 0.36 and cannot be timed against the same clock.

**Distance.** `DefconWall` derives the border as the **perpendicular bisector of the two alliance
centroids** (`world.yaml:925-926`, `DefconWall.cs:340-415`), so the perpendicular distance from either
home to the line is exactly half the spawn separation, on any map, by construction. The SR sits at
`spawn + (-1,-1)` (`MapStartingUnits.cs:37`, `SpawnStartingUnits.cs:91`), and the band is
`HalfWidth 1024` = one cell, so a unit stops one cell short of the centre line.

## 1.2 The reference wave

Starting cash is **20000** and passive income is **100 every 50 ticks = 2000/min**
(`PlayerResources.cs:32`, `:63-66`; the mod overrides neither — `economy.md:11`). So **money is not the
constraint during Positioning; travel is.** 20000 buys far more than can be driven to the line in any
plausible phase length.

Reference wave — a combined-arms push affordable from the opening balance, chosen so the group moves as
one and every rifleman is carried:

| | America | Russia |
|---|---|---|
| 4 MBT | abrams 2500 | t90 2400 |
| 2 IFV | bradley 1500 | bmp2 1300 |
| 1 APC | m113 700 | btr 600 |
| cash | 14100 | 13000 |
| **sequential queue** | 4·250 + 2·150 + 70 = **1370 ticks = 82 s** | 4·240 + 2·130 + 60 = **1280 ticks = 77 s** |

America is used in the table — it is the slower side on both axes. A **path-inflation factor of ×1.25**
is applied to the drive: straight-line distance is a floor, and real paths bend round trees, water and
each other, while trees, rough and debris all carry lower terrain percentages than `Clear`. **This is
the softest number in the derivation**, and the run in §"Runs requested" is what would replace it with
a measurement.

## 1.3 The per-map table

`d` is the perpendicular SR→border distance in cells, `e` the edge→SR walk. Multi-spawn maps get two
rows — the closest and the widest pairing the lobby can produce — because which spawns are occupied is
not known until the match starts, which is exactly why the border is derived rather than authored.

| map | spawns | pairing | spawn sep | `d` | `e` | 1 MBT at border | **wave at border** | wave on foot | wave, fwd-deployed |
|---|---|---|---|---|---|---|---|---|---|
| nuclear-winter-ww3 | 2 | s0-s1 | 114 | 57 | 1 | 1:25 | **2:32** | 3:20 | 1:43 |
| polar-disorder-ww3 | 2 | s0-s1 | 115 | 58 | 1 | 1:25 | **2:32** | 3:22 | 1:43 |
| siberian-pass-ww3 | 2 | s0-s1 | 101 | 50 | 1 | 1:16 | **2:24** | 2:56 | 1:41 |
| woodland-warfare-ww3 | 2 | s0-s1 | 130 | 65 | 1 | 1:34 | **2:42** | 3:48 | 1:46 |
| seventh-woods-ww3 | 4 | s0-s3 closest | 42 | 21 | 2 | 0:42 | **1:49** | 1:18 | 1:31 |
| seventh-woods-ww3 | 4 | s2-s3 widest | 129 | 65 | 1 | 1:34 | **2:41** | 3:46 | 1:46 |
| twin-rivers-ww3 | 4 | s2-s3 closest | 64 | 32 | 16 | 1:12 | **2:19** | 2:45 | 1:52 |
| twin-rivers-ww3 | 4 | s0-s2 widest | 131 | 66 | 8 | 1:44 | **2:51** | 4:14 | 1:55 |
| x-lake-ww3 | 4 | s1-s2 closest | 87 | 44 | 0 | 1:07 | **2:14** | 2:29 | 1:37 |
| x-lake-ww3 | 4 | s1-s3 widest | 154 | 77 | 1 | 1:49 | **2:56** | 4:29 | 1:50 |
| arena-tank-duel | 2 | s0-s1 | 52 | 26 | 6 | 0:53 | **2:01** | 1:51 | 1:38 |
| shellmap-open-field | 2 | s0-s1 | 78 | 39 | 6 | 1:09 | **2:16** | 2:36 | 1:43 |

**river-zeta-ww3 is not on the bisector and has to be measured differently.** `184680d4` authors its
border as a **region** — `Water, River, Bridge` plus 210 hand-authored cells closing the channel's four
dry crossings (`mods/ww3mod/maps/river-zeta-ww3/rules.yaml:12-58`) — and a region wins over both the
authored line and the derivation (`DefconWall.cs:104-118`, `:428-437`). Distance is therefore to the
nearest border cell, measured by decoding `map.bin` through `tools/nav-guard/modload.py` (no build, no
launch):

| spawn | SR | nearest border cell | `e` | wave at border |
|---|---|---|---|---|
| s0 (81,76) | (80,75) | **11.0** | 6 | 1:42 |
| s2 (16,6) | (15,5) | 13.9 | 5 | 1:45 |
| s4 (88,35) | (87,34) | 25.5 | 10 | 2:01 |
| s5 (9,45) | (8,44) | 28.2 | 8 | 2:02 |
| s1 (74,5) | (73,4) | 32.1 | 4 | 2:02 |
| s3 (23,75) | (22,74) | **37.0** | 7 | 2:09 |

River Zeta's two banks hold three spawns each — `(9,45) (16,6) (23,75)` west against
`(74,5) (81,76) (88,35)` east, verified statically at `ab2ac8b8` — and the per-spawn spread is **3.4×**
(11 to 37 cells) while the per-bank averages are 22.8 and 26.4. So the map is fair in aggregate and
**very** unfair per seat: a 1v1 on s0 vs s3 gives one player 11 cells to the line and the other 37.
That is a property of the authored river rather than a defect in the derivation, and it is the one map
where the seat you draw changes your opening tempo materially.

## 1.4 What the numbers say

**The wave column is 1:42 – 2:56 across every shipped map and every pairing** — a spread of 1:14 on
maps whose crossing distances differ by a factor of **7** (11 to 77 cells). The compression is the
sequential vehicle queue: 82 s of it is queue, which every map pays identically, and only the drive
varies.

Two figures are worth naming because the stop table below is entirely about where they fall.
**Fastest wave: 1695 ticks (1:42)**, river-zeta s0, which is 11 cells from the authored river border.
**Slowest: 2934 ticks (2:56)**, x-lake s1-s3 at 77 cells. Both are pinned as constants in
`DefconEscalationTest.ThePhaseClockDefaultsMatchTheDeploymentDerivation`.

That gives the three bands the retired pace dropdown was reaching for, and they land on stops the
current option set already offers (`NoRushOptions = { 2, 3, 5, 7, 10, 15 }`, `DefconEscalation.cs:120`):

| stop | verdict | what actually happens |
|---|---|---|
| **2 min** (2000 ticks) | *the phase does not finish* | Clears none of the six 2-spawn maps and no widest pairing — on x-lake s1-s3 not even a single MBT (1:49) arrives. It **does** clear the closest seats of the 4- and 6-spawn maps (river-zeta s0 at 1:42, seventh-woods s0-s3 at 1:49). So it is the aggressive stop rather than a dead one. |
| **3 min** (3000 ticks) | **barely** | The tightest stop that clears **every** map — and it clears the slowest (2:56) by 4 seconds. Nothing left over for a second thought. |
| **5 min** (5000 ticks) | **comfortably** — *and this is the shipped default* | Wave in position with 2:04 to 3:18 spare; income pays for a second wave (10000 credits by 5:00 ≈ 4 more MBT) which also arrives. |
| **7 min** | comfortable to slack | A third wave; the front is decided by stacking rather than by timing. |
| **10 / 15 min** | **dead air** | 20000–30000 unspent credits, a shop, a wall, and nothing on the map that may fire. |

**The default survives derivation unchanged.** 5 minutes was inherited from "what the retired Standard
pace was worth", and the arithmetic independently produces the same number as the comfortable band.
Nothing moves; what changes is that the `[Desc]` now says *why* rather than *where it came from*.

**Two regimes, and the same 5:00 is right in one and slack in the other.** `SpawnStartingUnits`
defaults `ForwardDeploymentClass` to `none` (`SpawnStartingUnits.cs:55`); switched on, the force lands
at `ForwardDeploymentAdvancePercent = 35` of the way to the nearest enemy spawn
(`ForwardDeploymentGeometry.cs:28`), i.e. **0.15 × sep short of the bisector**. The last column of the
table is that regime: the wave is in position in **1:31 – 1:55 on every map**, so 5:00 leaves over
three minutes of nothing. Forward Deployment wants a shorter clock and no mechanism couples them — §B4.

**Forward Deployment does not put anyone inside the wall.** 0.15 × sep is 5 cells on the tightest
pairing (river-zeta s1-s4, sep 33) against a 1-cell `HalfWidth`, so the case decision 11 dismissed as
"map-authored actors standing in the band at load" is not reachable from the lobby either. That is
right by arithmetic rather than by a guard — and §B1b would break it, which is noted there.

## 1.5 The other clocks, checked the same way

**DEFCON 2 has no clock at all** and ends on the first qualifying casualty
(`DefconEscalationState.cs:110-119`). §2.4 is about what that is worth; the arithmetic part is that
both armies are *at the border* when it begins, so the phase's length is bounded by weapon range rather
than by anything a host set.

**First warheads: 10 minutes from DEFCON 1** (`FirstWarheadsDefault = 10`, `DefconEscalation.cs:168`;
the offset reading is decision 21). With a 5:00 no-rush and a DEFCON 2 that lasts seconds, warheads
arrive at **≈ 15:30 on the match clock**, by which time each side has had ≈ 51000 credits — about 20
MBT — through its hands. That is genuinely mid-game: late enough that a conventional decision has been
attempted, early enough that the losing side still has an army to save. It holds.

**The nuclear spiral, in minutes, which the lobby never states.** Side cooldowns are
5000 / 7000 / 9000 / 12000 ticks = **5:00 / 7:00 / 9:00 / 12:00** (`NuclearExchange.cs:187-203`), scaled
by posture at 150 / 100 / 60 % (`NuclearExchangeState.cs:120-123`). `ApplyEscalation` grants
`band + 1`, permanently, capped at 5 (`NuclearExchangeState.cs:551-578`). Playing the ladder out — A
fires 1 kt, B replies 20 kt, A 50 kt, B 100 kt, A fires the ender — the binding path is
`0 → 5:00 → 14:00` on A and `0 → 7:00 → 19:00` on B, so:

| posture | release to apocalypse | match clock at the ending |
|---|---|---|
| Massive (60 %) | ≈ 8:30 | ≈ 24:00 |
| **Flexible (100 %, default)** | **≈ 14:00** | **≈ 29:30** |
| Limited (150 %) | ≈ 21:00 | ≈ 36:30 |

Those are sane RTS match lengths, and the posture control buys a real 12-minute swing. **Nothing tells
the host that** — `NUCLEAR POSTURE` offers three words and no numbers (§B7).

---

# Part 2 — the review, stage by stage

## 2.1 The lobby

**Right.** The timeline draws Escalation as **phase lengths with no ruler** and Skirmish on an absolute
minute ruler, because Escalation's middle phase ends on an event and putting a minute number under it
would be a lie (`LobbyTimelineLogic.cs:35-48`). `FIRST WARHEADS` is captioned `+10:00`, with the plus
sign load-bearing (decision 21). The word DEFCON appears in no string a host can read, and
`DefconEscalationTest.cs:139-142` enforces it. This is a well-made panel.

**Wrong — the first-time host's defaults do not describe the game the mode is about.**
`SpawnStartingUnits.StartingUnitsClass` defaults to `none` (`SpawnStartingUnits.cs:32-34`, not overridden
anywhere under `mods/`). So a default Escalation match opens with **one flag, 20000 credits and an empty
map**, and the first 82 seconds of the phase whose entire purpose is *positioning* have nothing to
position. This is the largest first-impression problem in the mode, and it is not a bug — it is a
default that was correct when the option was added and is wrong now that a phase depends on it. §B2.

**Imperfect — `NO RUSH` names one of the two rules in force.** The band's detail is just the clock
(`LobbyTimelineLogic.cs:241`), and since `5fef37dc` the phase also forbids *all* fire. §B5.

**Wrong, and it is an off-nominal that reaches the player.** Decision 15 requires exactly two sides and
**nothing enforces it** — `NuclearExchange.cs:28-31` and `:764-768` say so in their own words, and
`DefconWall` derives no line at all from three alliance groups (`:397-402`). A three-way Escalation
lobby therefore gets **no border and a total cease-fire**: for the whole no-rush period nobody can
shoot and nobody is separated, so the correct play is to drive your whole army into an enemy base and
wait for the clock. §B3.

## 2.2 DEFCON 3 — Positioning

**Right, and this is the best-built part of the mode.** The border is derived per match rather than
authored per map, so the 4- and 6-spawn maps get a fair line without anyone knowing in advance which
spawns are used; it is drawn by the trait itself out of the same dictionary that enforces it, so the
picture cannot drift from the rule (`DefconWall.cs:562-590`); it is hatched, because a bare line reads
as a road and a line with cross-strokes reads as border notation; aircraft are held by three layers
ending in a `SetPosition` guard that is a true chokepoint (decision 07); and a refused move order says
so, once per player per 3 s rather than once per unit (`:118-130`). The readout states the rule as
something the player may not do rather than as a mechanism.

**Wrong — the readout does not mention that weapons are cold, and that is now half the phase.**
`DefconReadoutModel.RuleLine(3)` was `"The border is closed. Neither side may cross it."` (`:103`). Since
`5fef37dc` ("Positioning phase: no weapon fires, by any path", 2026-09-16) DEFCON 3 also gates
`Armament.CanFire`, so **nothing fires at all** — not autotarget, not an ordered attack, not force-fire
at bare ground (`DefconFireDiscipline.cs:112-141`). The copy predates the rule, so a player who
force-fires at anything gets silence and no explanation. **Fixed this turn — §A1.**

**Right, and it is what keeps 5:00 alive.** Positioning is not only driving. The SR's `Production@Local`
produces `Building, Defense, Powers` (`structures.yaml:461-471`), so you can fortify your own bank; the
minelayer works, and mines consult no stance, so a mine is a legitimate way to take the first life
later; rally paths are set with waypoint order types replayed per waypoint
(`ProductionFromMapEdge.cs:173-179`), which is a real reinforcement-lane decision. Minutes 3–5 of the
default clock are fortify-and-mine, not idling. **Nothing tells the player any of that** — the readout
states only the prohibition.

**Imperfect — one clock cannot serve a 7× spread in crossing distance.** §1.3. The default is right on
the median and slack on the closest pairings and in every forward-deployed match. §B4.

## 2.3 The 3 → 2 edge

**Right.** The transition banner names the *cause* and then the consequence — "The holding period has
run out. The border is open." — which is what turns a state change into a rule the player infers without
being taught (`DefconReadoutModel.cs:113-131`). The engagement one-shot runs on exactly the tick the
level moves and cancels by provenance rather than by mutating stance (decision 09).

**Wrong — the DEFCON 2 banner is destroyed before it is seen whenever DEFCON 2 is short.**
`DefconTransitionBannerWidget` keeps one `shownLevel` and overwrites it on the next edge (`:142-143`),
and `Draw` holds it for `BannerHoldTicks` = 66 ticks (`:168-173`). Run `260915_012829` went 3→2 at tick
5000 and 2→1 at tick **5001** (recorded verbatim in `DefconEscalation.cs:288-292`). So the player sees
only `OPEN WAR`, and the two `DefconAlert.Play` calls land on consecutive ticks and clip each other.
**The mode's signature phase is invisible in the common case.** Filed to
`WORKSPACE/bugs/discovered.md`; the fix is §B6 rather than §A, because holding the banner longer would
mean four seconds of "the border is open" while the match is already at open war.

## 2.4 DEFCON 2 — Weapons free. The finding of this review.

**The idea is the best one in the design.** No unit shoots because it happened to see something, so the
first casualty is always somebody's decision, and it immediately ends the phase, which is what gives
that decision weight. The implementation is honest all the way down: the rule keys on
`AutoTarget.IsAutoAcquiredSource`, the engine's own provenance test, so there is one definition of "a
shot somebody gave" and not two (`DefconFireDiscipline.cs:143-158`); the readout says "Every shot is one
you order"; the clock field goes to an em dash, deliberately, because the emptied slot is what teaches
the player this rung ends on an event (`DefconReadoutModel.cs:63-71`); and `FIRST KILL ENDS THIS PHASE`
pulses where the countdown was.

**And in practice the phase is over in seconds. MEASURED 2026-09-20: 98 ticks — 5.9 s.**
*(Run `260920_010605_p1901`, `test-escalation-full-match` on the shipped clocks, seed
-1662796604: 3→2 on the clock at tick 5000, 2→1 on a qualifying kill at tick 5098. This
paragraph originally predicted **about one tick** and that was wrong by two orders of magnitude
— the correction, and what survives of the argument, are in §"Simulation results". What
follows is the original reasoning, kept because two of its three legs are confirmed and the third
is what R3 now exists to settle.)* Three things line up:

1. Positioning delivers **both armies to the border**, because the border is the only thing there is to
   position against. The bots do it explicitly — `BorderStagingEnabled: true` clamps an axis to the near
   side rather than ordering it through (`ai.yaml:636-647`) — and a human does it for the same reason.
2. The wall comes down and the fire permission opens **on the same tick**. `ActiveLevels = { 3 }`
   (`DefconWall.cs:390-392`), `HoldFireLevel = Ceiling - 1` (`DefconFireDiscipline.cs:105`).
3. **Tank range exceeds the band.** `HalfWidth` is one cell, so two armies parked on the line are 2–3
   cells apart against weapon ranges several times that. Nobody has to move in order to shoot.

   **MEASURED 2026-09-20 (R3): leg 3 holds, leg 1 does not, and the conclusion survives anyway.**
   Positioning did **not** deliver both armies to the border — at the half-way tick neither side
   had a single ground unit within twelve cells of either open crossing, and the bots spent the
   phase capturing the map's fourteen neutral objectives instead. But the handful that *were* near
   the crossings were in range of each other, and that was enough: fire was ordered three ticks
   after the border opened. **The phase collapses on the first contact anywhere on a 110-cell
   border, not on two armies meeting at a point** — which is a stronger result than the original
   claim, because it cannot be fixed by moving anybody back.

So the decision the phase exists to dramatise is made before the player has read the banner announcing
the phase — and that banner is then overwritten (§2.3). The evidence is already in the tree:
`DefconEscalation.cs:288-292` records a **one-tick** run as an *observability* problem for a test
poller. It is also a design one — and 98 ticks does not rescue it, because the transition banner is
held for 66 ticks (`BannerHoldTicks`), so a 98-tick phase shows the DEFCON 2 banner for two thirds
of its life and is then overwritten. The player gets one banner and a half.

**The configuration in which the phase works is the one that skips Positioning.** `Opening phase =
Weapons free` starts at 2, where `ActiveLevels` never matches so there is no wall at all, both sides
begin at their SRs 50–77 cells apart, and DEFCON 2 then lasts the 2–3 minutes it takes somebody to
drive over and choose to shoot. **The cease-fire is a phase exactly when the armies are apart, and
Positioning's whole job is to put them together.** That is the tension to resolve, and three ways to do
it are ranked in §B1.

**Can a player be griefed or stalemated here?** Two cases, and both are self-limiting:

- **Walk onto the enemy SR and contest it without firing.** `SupplyRouteContestation.Range` is 10 cells
  and the bar depletes on presence, slowing production past `SlowdownThreshold: 50` and then filling a
  defeat bar (`SupplyRouteContestation.cs:30-78`; `BaseTicks: 1500` = 90 s, `MinTicks: 500` = 30 s). A
  defender who does not realise they may shoot *if they order it* loses the match without a shot fired.
  The earliest that can complete is roughly DEFCON2 + 2:30 (the drive, then the bar), so it needs about
  two and a half minutes of confusion. Self-limiting, because one ordered shot both stops it and ends
  the phase — but it is the one way the mode can kill a first-time player who has misread the readout.
- **Neither side fires.** Decision 14 already has the user's answer and it needs no engine rule: a
  player who has lost patience fires. The mechanic supplies its own escape hatch.

**One hole recorded so nobody reads it as a bug later.** Being run over by an enemy vehicle ends DEFCON
2. Crushing kills with the crushing locomotor's damage types, that field is empty on every locomotor in
this mod, and the casualty predicate cannot distinguish "no damage types" from a weapon that declares
none (decision 06). As shipped, a crush is a casualty.

## 2.5 DEFCON 1, the release gate, and the ladder

**Right.** The gate is ticked before the state machine's early return, which is load-bearing and is
commented as such (`DefconEscalation.cs:329-337`). The escalation is applied at **impact plus 50 ticks
(3.0 s)** rather than at launch, which is the user's own ruling — "so we see the correlation between the
explosion, and after only a few seconds perhaps we get the message of escalation"
(`NuclearExchangeState.cs:52-63`, `NuclearExchange.cs:260`). One `ApplyEscalation` per release order and
not per warhead, so a six-RV RS-28 does not ratchet the enemy six rungs off one click. The ledger shows
both sides' level and the side-wide cooldown, and `"ESCALATED"` rather than `"ARMED"` because v2 grants
nothing — the enemy fired and your ceiling moved permanently.

**Right, and it is the sharpest piece of balance in the mode.** `ApplyEscalation` grants `band + 1`
(`NuclearExchangeState.cs:556-558`), so **firing a 100 kt hands the enemy the game-enders** — and your
own level never rises, so you cannot answer one. Whoever fires 100 kt has to win inside the enemy's
cooldown or lose. That is a deterrent that falls out of the arithmetic rather than being designed in.

**Imperfect — going first is not merely free, it is repeatable.** A side's own level never rises from
its own fire, and the enemy climbs to `highest-fired + 1` and no further. So the *leading* player's
optimal line is to fire the **smallest** warhead on cooldown forever: the enemy's ceiling reaches 2 once
and then never moves, while the leader keeps a permanent 5-minute area-denial power. Decision 06
accepted "going first is free" knowingly; this is the concrete shape of that cost, and it is low-impact
(a 1 kt has a 4-cell strike radius) rather than urgent. §B8.

**Imperfect — there is no demonstration shot.** Decision 14 specified a 10 % damage floor, which would
have made a warhead into empty ground escalate nobody; v2 deliberately dropped it and escalates
"wherever it lands" (`NuclearExchangeState.cs:553-556`). The consequence is that **a player cannot
signal without escalating**, which removes one real Cold War move from a mode built on them. Recorded as
a known divergence, not a defect.

## 2.6 The endgame

**Right.** Two doors into one room (decision 14): a player firing a game-ender, or the Dead Hand clock
expiring; either way every surviving side is armed, aims what it wants, and Dead Hand places the rest so
the apocalypse is guaranteed whatever an AFK player does. The lobby control is `Nuclear ending`
(`DoomsdayStrike.cs:79`) and the trait's own `[Desc]` carries "Dead Hand", so the 2026-09-10 rename is
consistent in both directions.

**Imperfect — the reply window is 30 s, not the 15 s every design document says.** `500 ticks = 30.0 s`
(`DoomsdayStrike.cs:230`). Decisions 14, 17 and 20 all say fifteen. The code is probably right and the
documents stale — 15 s is very little time to place three aim points — but the mismatch wants resolving
deliberately rather than by whoever next reads one of them. §B9.

## 2.7 The bots under the mode

**They look like an army, and the reasons are specific.** `BorderStagingEnabled` clamps an axis to the
near side of the border rather than ordering it through, and self-heals when the border opens because
the clamp becomes the identity (`ai.yaml:630-647`). `Defcon2DirectFireEnabled` exists because
`PoiOffensiveBotModule`'s entire order vocabulary is `AttackMove`, which the engine's own provenance test
classes as autonomous — so without it the bot's whole ground army would hold fire through the phase; both
profiles set it, deliberately, and the file says so. `MountedTransportBotModule` refuses deliveries
beyond the wall rather than issuing orders that silently do nothing.

**Nuclear policy is doctrinally right.** `NuclearBotModule` fires when it is **losing** — own army value
below 60 % of the strongest enemy's, or its own SR contested below 40 %, for 3 consecutive evaluations,
with a 900-tick floor between launches (`NuclearBotModule.cs:91-129`). That is the nuke as a comeback
tool, which is what the exchange model assumes, and it means a bot will not open with one.
`MayFireGameEnder` defaults true, so a bot will finish a match it has climbed to level 5.

**What I could not assess by reading.** Whether the bot's border staging produces a *line* or a *clump*;
whether it holds artillery at standoff behind the staged line during Positioning; and whether it spends
the slack minutes of the clock on defenses or on stacking more armour at the border. All three are
visual and behavioural, and all three are in §"Runs requested".

---

# §A — IMPLEMENTED THIS TURN

**A1. The Positioning readout now names both rules in force.** `DefconReadoutModel.RuleLine(3)` was
`"The border is closed. Neither side may cross it."` and is now
`"The border is closed. Nothing may cross it, and nothing may fire."` — the same register (what the
player may not do, no mechanism named), stating the rule `5fef37dc` added and the copy never caught up
with. `DefconReadoutTest.RuleLinesAreApprovedCopy` updated; `TheRuleLinesNameNoMechanism` passes
unchanged.

**A2. The two phase-clock defaults now record their derivation.** `NoRushOptions`, `NoRushDefault` and
`FirstWarheadsDefault` carried the *provenance* of their numbers ("what the retired Standard pace was
worth"); they now carry the arithmetic of §1 — the wave window, the band each stop falls in, and the
fact that the default was re-derived and did not move.

**A3. A test pins the derivation so a silent retune is caught.**
`DefconEscalationTest.ThePhaseClockDefaultsMatchTheDeploymentDerivation` asserts the minute→tick
identity for every offered stop at a 60 ms timestep, that the default no-rush clears the slowest
measured wave (2:56) while the smallest offered stop does not, and that the first-warhead default is
10000 ticks. The measured figures are constants in the test with the derivation in its comment — a unit
test cannot drive a unit across a map, so what it pins is the *conclusion*, which is the thing a revert
would lose.

**A4.** Two `WORKSPACE/bugs/discovered.md` entries (banner collapse; three-side Escalation) and one
`WORKSPACE/DISCOVERIES.md` entry (the deployment arithmetic and its two regimes).

Nothing in §A touches Skirmish: `RuleLine` is unreachable at `NoLevel`, and the two clocks are read only
by `DefconEscalationState` in `Escalation`.

---

# §B — LIST FOR LATER, ranked by impact

**B1. Give DEFCON 2 a reason to last. (Highest impact; a design ruling, not a fix.)** §2.4. Three ways,
cheapest first:

- **(b1a) The border stands at DEFCON 2 as well** — `DefconWall.ActiveLevels: 3, 2`. One field. DEFCON 2
  becomes "you may shoot across the line, you may not cross it", which is the most exact reading of the
  user's own "a first strike of some kind becomes possible, and making it is what takes the game to 1",
  and the phase then lasts precisely as long as both sides hold their fire. Costs: it contradicts the
  field's present `[Desc]` ("DEFCON 2 is hold-fire and DEFCON 1 is open war, and in both of those the
  line is gone"), and it does not lengthen a *bot* match much, because a staged bot with
  `Defcon2DirectFireEnabled` fires as soon as the permission opens.
- **(b1b) A demilitarised zone** — raise `HalfWidth` from 1024 to roughly 8 cells so the armies are held
  outside weapon range and have to *close* at DEFCON 2, buying 20–40 s of real "who shoots first".
  Thematically the best answer and still one field. Costs: `tools/nav-guard/defcon_wall_audit.py` must be
  re-run (a wider band is strictly safer for sealing, but connectivity is measured, not argued); 8 cells
  is 48 % of the approach on river-zeta's closest pairing and 30 % on arena-tank-duel, so it probably has
  to be derived from `d` rather than fixed; and it breaks §1.4's Forward Deployment clearance, which is
  only 5 cells on the tightest pairing.
- **(b1c) A minimum dwell at DEFCON 2** — hold the level for N ticks after a qualifying casualty. Cheap
  and wrong: autonomous fire would stay held while units are dying, which is a rule the readout would be
  lying about.

**RECOMMENDATION REVERSED 2026-09-20 BY R3 — READ THIS BEFORE RULING.** This paragraph read
"Recommendation: **b1a**, because it is one field, it matches the design's own language, and it is
trivially reversible." R3 dated every step of R1's cease-fire and **b1a buys nothing**: both bots
issued direct-fire orders three and four ticks after the border opened, at units already inside
weapon range, so leaving a two-cell band standing changes neither the start of fire nor the kill.
**b1b buys only the closing time** — about 22 s for eight cells at infantry speed — and the
dominant term looked like neither: 95 of R1's 98 ticks were time-to-kill.

**THEN R2 MOVED IT BACK, PARTLY.** Running the same scenario with the border actually held at
DEFCON 2 gave a 224-tick cease-fire against R1's 98, and its log dates first fire at tick 5076
rather than 5003 — so the standing wall DID delay contact, by 73 ticks, which is the effect b1a
was supposed to have and which the paragraph above says it does not. **Both readings rest on single
unseeded samples and they disagree.**

**RULED 2026-09-20 ON THE CONTROLLED PAIR. b1a IS REJECTED.** R2 was re-run with R1's seed, making
the two matches identical up to tick 5000 and divergent only in the rule under test. Result: first
fire at tick 5003 in BOTH arms — the standing wall delayed contact by zero ticks, so the unseeded
run's 73-tick "delay" was noise of the same size as the effect — and the cease-fire came out
**shorter** with the wall up, **38 ticks against 98**. The mechanism is in the volley: with the band
impassable each unit's valid-target set narrows, so ten USA units including an Abrams all picked the
SAME soft target, against three units over two targets with the band down. Concentrated fire kills
faster and ends the phase sooner. b1a is counterproductive, not merely useless.

**b1b is untested and must not be built on this evidence**; **b1c is rejected on a different
objection from the one written above** (it falsifies `FIRST KILL ENDS THIS PHASE`, not the fire
rule). **§B1 CLOSES: there is no geometric fix, and §B6 is the whole of it.** The full ruling, with
the volley, is the "§B1, RULED" section at the end of "Simulation results".

**B2. Escalation's `Starting Units` default.** §2.1. A default Escalation match opens with nothing to
position. `Motorized` (3 vehicles + 15 infantry + support) is the package that matches the fiction. The
obstruction is that `startingunits` is one global lobby option shared with Skirmish, so an
Escalation-only default needs either per-mode defaults (`ILobbyOptions.LobbyOptions(MapPreview)` already
receives the map but not the mode, and the mode is another option's value — so this is real plumbing) or
a straight change of the shipped default for both modes. Worth an explicit ruling, because it is the
mode's first impression.

**B3. Enforce two sides, or make three sides survivable.** §2.1. Decision 15's constraint is unenforced
and the failure is not graceful. Cheapest: refuse to start Escalation unless the combatants resolve into
exactly two alliance groups — `DefconWall` already computes the group count (`:352-395`) and
`NuclearExchange` already warns (`:764-768`), so the information exists; what is missing is a lobby-side
refusal. Second cheapest and much weaker: when no border derives, do not hold fire at DEFCON 3 either,
so the match is at least an ordinary one.

**B4. Derive the no-rush default from the map, and from Forward Deployment.** §1.3, §1.4.
`ILobbyOptions.LobbyOptions(MapPreview map)` already receives the map, and `MapPreview` carries the spawn
points, so a default of "3 minutes on a map whose spawns are 60 cells apart, 5 on one 130 apart" is
expressible and needs no new option type. It must snap to an offered key, because a default that is not
a key of its own `Values` throws `KeyNotFoundException` on client join (`LobbyCommands.cs:770`,
`TraitsInterfaces.cs:717`). Coupling it to Forward Deployment is harder: that is another option's value
rather than a map property. Cost: the lobby default stops being predictable from the mod files, which is
a real downside for anyone debugging.

**B5. Say "weapons cold" on the timeline's `NO RUSH` band and in its tooltip.** §2.1. Held out of §A
because the band's detail is currently `"5:00"` and the label-fit tests hand-count widths
(`LobbyOptionsLogic.cs:427` names the above-the-fold budget), so a much wider detail string needs the
chrome test re-measured rather than assumed.

**B6. Draw one banner when two edges land inside the hold window.** §2.3. Instead of queueing (which
would claim the border just opened while the war is already on) or overwriting (which is what happens
now), draw a single combined banner naming both causes: the phase happened, it lasted N seconds, and a
life was taken. Plus an event-log line naming the first casualty's type and owner, so the match record
shows *who chose*. This is what makes the mode's signature moment visible, and if B1 is declined it is
the whole fix.

**B7. Put numbers on `NUCLEAR POSTURE`.** §1.5. The control buys a 12-minute swing in how long the
nuclear phase runs and offers three adjectives. The tooltip can state the cooldown range in minutes
without touching the wire format, which is the entire reason decision 20 chose a word over a number.

**B8. Decide whether repeated small strikes should be free.** §2.5. Lowest-cost option: scale the
firer's own cooldown up when it repeats a band at or below its highest-fired, so the tenth 1 kt costs
more than the first. Low impact; list it, do not chase it.

**B9. Reconcile the reply window: 30 s in code, 15 s in three decisions.** §2.6. One number, one ruling;
the code is probably right.

**B12. The bots do not form a front during Positioning, and the phase is named for doing exactly
that.** R3. Three to four axes per side exist from tick ~1000 with pools of 20—63 units, so
`BorderStagingEnabled` is not the constraint — the axes are aimed at objectives, and the border is
not an objective. What the bots actually do with the five minutes is capture the map's fourteen
neutral oil derricks and logistics centres, which is defensible play and is not what the phase is
called. **This is a bot-quality item rather than a mode item** and it blocks two of §2.7's three
questions (artillery standoff is unanswerable with no line to stand off from). The lever, if the
user wants a front, is a POI-like attractor on the near side of the border during the levels
`DefconWall.ActiveLevels` names — which is a new scoring input to `PoiOffensiveBotModule`, not a
one-field change, and it should not be built until §B1/§B6 are ruled on, because a bot that masses
on the line makes the cease-fire *shorter*, not longer.

**B11. Decide whether an even match declining to go nuclear is the drama or the anticlimax.**
R1, above. Both bots reached the nuclear phase and then sat in it for 6.9 minutes without firing,
because the bot policy fires only when losing and neither ever was; the match ended on the host's
Dead Hand clock instead. There may be nothing to fix — decision 13 argues in as many words that a
deterrent which is never used has not failed — and if the user does want matches to reach the
exchange more often the levers are `NuclearBotModule`'s `LosingArmyRatioPercent` (60) and
`LosingStreakRequired` (3) rather than anything in the mode itself. **Raised because the
alternative is meeting it in a played match and reading it as a broken feature.** The corollary is
operational and is the part not to lose: **bot-vs-bot runs cannot tune the ladder, the cooldowns or
the postures, because an even match never reaches them.**

**B10. `NoRushOptions`' top two stops are dead air at every configuration.** §1.4. Do **not** remove the
keys — they are wire-visible and an out-of-set value throws on client join
(`LobbySettingsNotification.cs:39`). The safe form is a tooltip saying what the long stops are for (a
fortification game), or nothing at all. Recorded so nobody "fixes" it by deleting keys.

---

# Runs requested

**The manager executes these; the worker may not launch.** Ordered by what they settle. The one
rule they share: every reading below is a **tick stamp or a count**, not an impression, because the
two claims they are meant to falsify are both numeric.

**R1 — the whole match on shipped clocks, to measure DEFCON 2's real duration and the wave.**
`./tools/autotest/run-test.sh --hidden --speed 8 --timeout 900 test-escalation-full-match`
Extract from `lua.log` / `debug.log` / the run dir:
- `DEFCON 3|2|1` tick stamps (`DefconEscalation` logs each transition; `LevelReachedTick` holds them).
  **`tick(2→1) − tick(3→2)` is the number this whole review turns on.** §2.4 predicts ≈ 1 tick.
- the tick and owner of the first casualty, and whether it was a shot or a crush.
- `NUCLEAR RELEASE GATE OPEN` tick — expected exactly `tick(DEFCON 1) + 10000`.
- every `NUCLEAR EXCHANGE` line: band fired, firer, cooldown charged, levels before/after. Gives the
  real spiral length against §1.5's predicted ≈ 14:00 at Flexible.
- how the match ended, and at what tick.
- **for the wave measurement:** the tick each side's first vehicle is produced and the tick it first
  comes within 2 cells of the border. That difference against §1.3's polar-disorder row (**2:32**,
  d = 58) is what replaces the ×1.25 path factor with a measured one. If the run's own logging does
  not carry unit positions, a unit count within 10 cells of the border sampled every 250 ticks is
  enough to bracket it.

**R2 — the same match with the border held at DEFCON 2, to price §B1a before ruling on it.**
Same command, with `DefconWall: ActiveLevels: 3, 2` added to the scenario's `rules.yaml` in a scratch
copy. Extract the same DEFCON tick stamps. **The single question: does `tick(2→1) − tick(3→2)` become
seconds instead of one tick, or does a staged bot with `Defcon2DirectFireEnabled` fire across the line
immediately anyway?** If the second, §B1a is not worth ruling on and §B6 is the whole fix.

**R3 — what the bots do with the slack minutes of Positioning.** From R1's run dir, or a screenshot
pass if R1 does not capture frames: two captures during DEFCON 3, at roughly tick 2500 and tick 4800.
Questions, all of which I could only read and not see (§2.7): is the staged force a **line along the
border or a clump**; is artillery held behind it at standoff or parked in it; and does the bot spend
the last two minutes on **defenses** or on more armour at the border. A third capture one second after
`tick(3→2)` would also show whether the border annotation disappears cleanly.

**R4 — the readout copy in situ (§A1).** One capture at DEFCON 3 showing the readout strip, to confirm
`"The border is closed. Nothing may cross it, and nothing may fire."` fits its slot at the shipped
`Bounds.Width` without truncating. `demo-defcon-readout` is the rig; the string grew by 12 characters
over the line it replaces, which is the one thing a unit test cannot check.

---

# Simulation results

## R1 — the whole match on the shipped clocks. **Run `260920_010605_p1901`, PASS, 6 min 21 s wall.**

`./tools/autotest/run-test.sh --hidden --speed 8 --timeout 900 test-escalation-full-match`, from
`main @ ee301478`. **Seed `-1662796604`** (the harness default, recorded in `result.json`) — that
number is what makes R3 a set of pictures of *this* match rather than of another one.

### What it confirms

| claim | predicted | measured |
|---|---|---|
| 3→2 fires exactly on the no-rush clock | tick 5000 | **tick 5000** |
| the minute→tick identity at a 60 ms timestep | 5 min = 5000 ticks | exact |
| first warheads is an offset from DEFCON 1, not the match clock | 5098 + 10000 = 15098 | **release at 15101**, 3 ticks late — the gate is polled |
| the wall stands at 3 and only at 3 | — | `wall 3=999/1000, 2=0/20, 1=0/3781` |

The single missed DEFCON 3 sample is the opening tick, before `IWorldLoaded` has derived the line.

### The correction: DEFCON 2 lasted 98 ticks, not one

§2.4 predicted "about one tick". It is **98** — 5.9 s. That is wrong by two orders of magnitude and
the error is worth naming precisely, because the *shape* of the finding survives and the *mechanism*
I gave for it may not.

- **What survives.** 5.9 s is not a phase. Nobody reads a banner, weighs a first strike and issues an
  order in six seconds — and the transition banner is held for 66 ticks, so the DEFCON 2 banner is on
  screen for two thirds of the phase and is then overwritten by DEFCON 1's (§2.3). Everything §B1 and
  §B6 propose still applies, unchanged.
- **What does not.** My stated reason was "tank range exceeds the one-cell band, so nobody has to move
  to shoot". If that were the whole story the first kill would land in a handful of ticks. 98 ticks is
  about 6 cells of tank movement, or an acquisition plus a burst plus a kill — so **either the armies
  were not actually in contact when the wall fell, or they were and it simply takes ~6 s to kill
  something.** Those two readings lead to opposite rulings: the first says §B1b's demilitarised zone
  would buy real time, the second says it would buy almost none. **A tick count cannot tell them
  apart. R3's contact frames can** — which is why R3's four captures were re-aimed from "just after
  the wall drops" onto the measured window at 5010 / 5040 / 5070 / 5100.
- Both bots were busy inside those 98 ticks: **118 and 121 orders**, 16 and 21 of them from
  `PoiOffensiveBotModule`. The phase is not a stall; it is a scramble nobody can see.

### The finding nobody asked for: the ladder was never climbed

    nuclear USA{launches=1|band=5|fired=FinalExchange|reason=NotLosing|streak=0|committed=false}
    nuclear RUS{launches=1|band=5|fired=FinalExchange|reason=NotLosing|streak=0|committed=false}

Both bots fired **exactly once each, both game-enders, both inside the Dead Hand final exchange**.
The release gate opened at tick 15101 and for the next **6900 ticks — 6.9 minutes — neither side
fired a single ladder warhead**, because `NuclearBotModule` fires when it is *losing* and in an even
bot-vs-bot match neither ever was.

So the match ended through **door 2**, the Dead Hand clock the scenario set at tick 22000, and never
through door 1, a player deciding. That is three things at once:

1. **The deterrent working exactly as designed.** Decision 13 rejected a match clock to force the
   spiral, on the grounds that "a deterrent that is never used has not failed". This run is that
   sentence happening.
2. **A hard limit on what bot runs can tune.** The 1/20/50/100 kt ladder, the 5/7/9/12-minute
   cooldowns and the three postures were **not exercised at all** by a whole match on shipped
   settings. No amount of bot-vs-bot running will tune them; that needs an asymmetric match or a
   human. Any future plan that says "we will tune the exchange from tournament runs" is planning
   against this result.
3. **A question about the mode's shape.** A default Escalation match between evenly matched sides
   reaches its nuclear phase and then declines it for seven minutes. Whether that is the intended
   drama — a cold war that stays cold — or an anticlimax is a ruling rather than a bug, but it should
   be made knowingly. Filed as **§B11**.

### Raw readings, for the record

    phases(recorded)  3@1  2@5000  1@5098
    release@15101 (due 15098)      ending@22001 (time limit 22000)
    wall  3=999/1000  2=0/20  1=0/3781
    orders  defcon3[USA 5484 / RUS 5470]   defcon2[USA 118 / RUS 121]
            defcon1-prerelease[USA 11610 / RUS 11683]   defcon1-released[USA 9063 / RUS 9059]
    doomsday{phase=2|placements=2|closes=22500}   final-exchange placement seen=true
    stop=deadline tick=24001 level=1

## R4 — the readout copy in situ. **Run `260920_011522_p2650`, ten frames. No change needed.**

`./tools/autotest/run-test.sh --background --speed 4 demo-defcon-readout`, window 1728×918 at 100 %
display scale.

The §A1 rule line **wraps to two lines** — "The border is closed. Nothing may cross it, and nothing"
/ "may fire." — fully drawn, not clipped, on a strip tall enough to hold both.

**That is the shipped design rather than an overflow, and the decisive evidence is frame
`003_03-defcon2-emdash.png`**, which shows the DEFCON 2 strip at the same width reading "Your units
will not fire on their own. Every shot is one you" / "order." over a third row,
`FIRST KILL ENDS THIS PHASE`. So:

- **DEFCON 2's rule line is 67 characters; the new DEFCON 3 line is 65.** The longest rule line in
  the mod is *unchanged* by §A1, and the width at which a rule wraps was already being crossed by a
  line that has shipped for weeks.
- The widget says so itself: *"Both blocks are variable height — the rule line wraps"*
  (`DefconReadoutWidget.cs:27`); `stripHeight` is computed from `ruleLines.Count` (`:259-264`); and
  the strip is anchored at `DrawBottom() - stripHeight`, so it grows **upward**. A second line pushes
  nothing off screen, which is what the frame shows.
- DEFCON 3's block is now two rows where DEFCON 2's has always been three. It is still the shorter of
  the two.

**Verdict: accept the two-line rule as it stands.** No shortening, no width change, no re-capture. A
one-line version would have to drop either the crossing rule or the firing rule, and the whole point
of §A1 is that the phase has two of them.

*(One thing the frames cannot settle, stated rather than implied: whether a two-line rule is
**pleasant**, as opposed to correct and legible. It is the same shape DEFCON 2 already ships, so if
it reads badly here it has been reading badly there too — that would be a copy pass across all three
lines, not a fix to this one.)*

## R3 — what the bots do with the no-rush period. **Run `260920_012415_p3511`, PASS, 70 s wall.**

`./tools/autotest/run-test.sh --background --speed 8 --seed -1662796604 --timeout 240 wip-escalation-r3`,
from `main @ ee301478`, window 1728×918. `result.json` records seed `-1662796604`, so **this is R1's
match** and the tick labels below are R1's tick labels.

### The census, which is the headline

Ground attackers within 12 cells of each open crossing (NW centre 41,38 / SE centre 56,59):

| tick | phase | USA near NW/SE | USA total | RUS near NW/SE | RUS total |
|---|---|---|---|---|---|
| 2500 | DEFCON 3, half way | **0 / 0** | 58 | **0 / 0** | 60 |
| 4800 | DEFCON 3, 12 s left | 2 / 1 | 82 | 2 / 4 | 79 |
| 5010 | DEFCON 2 | 3 / 2 | 83 | 3 / 4 | 80 |
| 5100 | DEFCON 1 | 4 / 2 | 83 | 6 / 3 | 79 |

**At the halfway point of the phase named "Positioning", with about sixty ground units each, neither
side had a single one within twelve cells of either crossing.** Twelve seconds before the border
lifted it was three of eighty-two and six of seventy-nine. The wide frames corroborate: no visible
concentration anywhere along the amber line, on either side, in any of the three wide captures.

*(Honest limit: the census samples two points, not the whole 110-cell border. What it proves is that
the two wide crossings nearest the direct axis were empty. The claim that the army was **dispersed**
rather than **massed somewhere I did not sample** rests on the wide frames, read at 0.25 zoom.)*

### Why — and it is not the border-staging guard failing

`BorderStagingEnabled` works, and the log shows it was never the constraint. `[exp-offense] reeval`
reports **3–4 axes per side from tick ~1000 onward**, with free pools of 20–63 units. The axes exist;
they are simply not aimed at the border, because **the border is not a target**. What the bots spent
Positioning on instead is the map's economy: `[exp-capture]` is the single most frequent tag in the
run at **1220 lines**, against 14 neutral objectives (12 oil derricks and 2 logistics centres, listed
in the tick-8 ownership snapshot), and frame `007` catches the hover tooltip on one of them reading
*"Oil Derrick — USA-bot"*.

So the bots play the opening as an economic expansion behind a closed border, which is defensible
play and is **not** the "army formed up against the line" the phase's name implies. §2.7's question
"line or clump" has a third answer: **neither — a dispersal.** The near-side clamp only bites when an
axis' objective lies across the line, and during Positioning almost none did.

### **The 98 ticks decomposed — and it inverts the §B1 recommendation**

The log dates every step of R1's cease-fire:

    tick 5000        DEFCON 2 (clock expired)
    tick 5003-5004   10 x [exp-defcon2-fire] -- 3 USA orders, 7 Russian
    tick 5098        DEFCON 1 (enemy action destroyed ar.russia, owner Russia-bot)

**Contact existed immediately.** Both bots issued direct-fire orders three and four ticks after the
border opened, at units they could already see and already reach — `tl.america`, `e3.russia`,
`ar.russia`, `e2.america` and one `m109` Paladin, which are exactly the 5 and 7 units the census puts
near the crossings. So the phase was not spent closing. **It was spent killing: 95 of the 98 ticks
are the time it takes small-arms fire to kill one automatic rifleman.**

That is decisive for §B1, and it goes against what I recommended:

- **§B1a — the border stands at DEFCON 2 — buys nothing.** The units that opened fire at tick 5003
  were within weapon range of each other. Leaving the two-cell band up separates them by two more
  cells, which infantry and a Paladin shoot straight across. Fire would still start at ~5003 and the
  kill would still land at ~5098. **This was my recommendation and it is wrong.**
- **§B1b — a demilitarised zone — buys only the closing time.** Eight cells at infantry speed is
  about 22 s, so it turns a 6-second phase into a ~30-second one. Real, and the only geometric lever
  that does anything at all — but it costs a `defcon_wall_audit.py` re-run and it collides with
  Forward Deployment's 5-cell clearance on the tightest pairing (§1.4).
- **Neither touches the dominant term.** Time-to-kill is not geometry. No change to the border makes
  the first casualty a considered decision, because the casualty is the *outcome* of a firefight that
  begins the instant fire is permitted, not a discrete act.

**So the ranking inverts: §B6 — make the phase visible — is the whole fix, and §B1 is a small
optional improvement on top of it.** If DEFCON 2 is structurally a trigger rather than a phase, then
the thing to repair is that the player never sees the trigger fire (the banner is overwritten inside
its own 66-tick hold), not the length of something that was never going to be long.

### What renders correctly, and it is worth saying

The border is the best-looking thing in the mode. Frames `002`, `006` and `007` show a translucent
salmon band with a bright amber centre line and perpendicular hatch strokes running the full
north-west to south-east diagonal — unmistakably **a rule rather than terrain**, legible at zoom 2 and
zoom 1 and still findable fully zoomed out at 0.25. Frame `011`, at DEFCON 2, shows bare ground where
it was: it comes down cleanly and leaves nothing behind. §A1's new rule line is live and correct in
every DEFCON 3 frame, wrapped to two lines in the bottom-right strip.

### Not assessable, and why

**Artillery standoff.** With three to six units near each crossing there is no line to stand off from.
One `m109` Paladin appears among the ten units that opened fire at tick 5003 — which is a battery in
the front rank rather than behind it — but a single gun among ten units is an anecdote, not a
finding. The question becomes answerable only if the bots ever form a front, so it is blocked behind
the dispersal above rather than being a separate defect.

**Defenses versus armour in the slack minutes.** Comparing `002` (t2500) against `006` (t4800) at the
same viewpoint shows more units in frame and no new structures, and the ground-attacker totals rose
58→82 and 60→79. So the answer is *armour, not defenses* — but the two enclosures visible in both
frames are the map's own neutral logistics centres rather than anything built, and I could not
distinguish a purchased defense from a captured one at this zoom. Treat it as suggestive.

*(The HUD cash readings in these frames are the Observer slot's, not a bot's — the harness's own
client sits in that seat and `PlayerResources` pays it. 25000 at t2500 and 29600 at t4800 are exactly
20000 plus 100 per 50 ticks, which is the giveaway. Do not read bot spending off them.)*

## R2 — the border held at DEFCON 2 (§B1a priced). **Run `260920_012525_p3659`, PASS, 5 min 30 s.**

`./tools/autotest/run-test.sh --hidden --speed 8 --timeout 900 wip-escalation-r2`, from
`main @ ee301478`, on the scratch copy carrying `DefconWall: ActiveLevels: 3, 2`.

**The override applied, and the check for it was built in.** The scenario's Lua asserts the wall only
at levels 3 and 1 and merely *counts* it at level 2 into the READINGS line. R1 read `wall 2=0/20`;
R2 reads **`wall 2=45/45`**. The border stood through the whole cease-fire.

### **THIS RUN IS NOT CONTROLLED, AND THAT GOVERNS EVERYTHING BELOW.**

R2 ran without `--seed` — my R2 spec named none, which was my omission — so it rolled
`1341496613` against R1's `-1662796604`. **The two runs are different matches.** Every
across-run number here is one unseeded sample against another and none of it is evidence about the
rule by itself. What *is* sound is the **within-run decomposition**, because each run dates its own
steps out of its own log.

### Across the two runs — indicative only

| | R1 (wall down at 2) | R2 (wall up at 2) |
|---|---|---|
| seed | -1662796604 | 1341496613 |
| 3→2 | tick 5000 (the clock, exactly) | tick 5000 (the clock, exactly) |
| 2→1 | tick 5098 | tick 5224 |
| **DEFCON 2 duration** | **98 ticks (5.9 s)** | **224 ticks (13.4 s)** |
| orders during DEFCON 2 | USA 118 / RUS 121 | USA 258 / RUS 264 |
| ladder climbed? | no — both `FinalExchange`, `NotLosing` | no — both `FinalExchange`, `NotLosing` |
| ending | Dead Hand clock, tick 22001 | Dead Hand clock, tick 22001 |

The order totals scale with the duration almost exactly (2.2× against 2.3×), so the *rate* of bot
activity inside the phase is unchanged: it is the same thing happening for longer, not a different
thing happening.

### Within each run — the decomposition, and it is sound

Both logs date first fire and the kill:

    R1   5000 DEFCON 2  ->  5003 first [exp-defcon2-fire]  ->  5098 kill      =   3 + 95
    R2   5000 DEFCON 2  ->  5076 first [exp-defcon2-fire]  ->  5224 kill      =  76 + 148

**The standing wall delayed contact from 3 ticks to 76.** That is precisely the effect §B1a was
supposed to produce, and R3's section of this document says it produces none. **I was too strong, and
this is the second time the evidence has moved my §B1 position.**

What changed my mind after R3 was the observation that fire began 3 ticks after the border opened, so
the units were "already in range and two more cells would not matter". R2's first fire at 5076 says
otherwise: with the band standing, the bots' units spent 76 ticks finding a position they could
shoot from at all. The reason is visible in R3's finding rather than in either log — the armies are
*dispersed*, not lined up, so whether a firing position exists at tick 5003 depends on where a
handful of units happen to be, and a 2-cell impassable strip changes which of them can see each
other.

### What this does and does not change

- **The ruling does not change. §B6 is still the fix.** 224 ticks is 13.4 seconds. A phase a player
  participates in — reads a banner, weighs a first strike, gives an order — is not 13 seconds any
  more than it is 6. Doubling something far too short leaves it far too short.
- **The reason changes, and §B1a is no longer "buys nothing".** It is "buys roughly a doubling of a
  phase that is an order of magnitude too short to matter". That is a real effect for one field, and
  it is worth having *if* the phase is being fixed by other means; it is not worth having on its own.
- **The time-to-kill term is unexplained** — 95 ticks against 148 — and is where the seed noise lives.
  Nothing here separates "the wall made the shooting less effective" from "a different match had
  different units in contact".

### The one run still worth taking — REQUESTED, TAKEN, AND IT SETTLED IT (see below)

**Re-run R2 with `--seed -1662796604`.** That makes it identical to R1 up to tick 5000 and divergent
only in the rule under test, which turns the 3-vs-76 contact delay from a suggestion into a
measurement and settles the time-to-kill term. **§B1 is the largest design ruling in this review, I
have now had its recommendation wrong in one direction and overcorrected in the other, and both
positions rested on single unseeded samples.** Six minutes is cheap against a ruling made on that
basis. Until it exists, §B1 should not be ruled at all.

**That run was taken. It is the next section, and it settled §B1 against b1a.**


### Checked and clean, so nobody else chases it

R2's release fired at tick 15251 against a due 15224 — 27 ticks late, where R1 was 3 late — which
looks like a drifting clock and is not one. `NuclearReleaseGate.Tick` decrements once per tick at
DEFCON 1 with no RNG and no wall-clock, so release is exactly `DEFCON 1 + 10000`. The scenario
observes it through `NuclearBotModule`'s own reason token leaving `NotReleased`, and that module
evaluates every `EvaluationInterval = 50` ticks. Both 3 and 27 are inside one evaluation window. The
gate is exact; the observation is coarse.

### **R2 REPEATED WITH R1's SEED — the controlled pair. Run `260920_013650_p4329`, PASS.**

`./tools/autotest/run-test.sh --hidden --speed 8 --seed -1662796604 --timeout 900 wip-escalation-r2`,
from `main @ 91ebded4`. `result.json` seed `-1662796604`; check line `wall 2=8/8`. **Identical to R1
up to tick 5000 and divergent only in the rule under test.** This supersedes the unseeded reading
above wherever the two disagree.

| | R1 — wall DOWN at 2 | R2 seeded — wall UP at 2 |
|---|---|---|
| 3→2 | tick 5000 | tick 5000 |
| **first `[exp-defcon2-fire]`** | **tick 5003** | **tick 5003** |
| 2→1 | tick 5098 | **tick 5038** |
| **DEFCON 2 duration** | 98 ticks (5.9 s) | **38 ticks (2.3 s)** |
| orders during DEFCON 2 | USA 118 / RUS 121 | USA 61 / RUS 54 |
| first victim | `ar.russia` (automatic rifleman) | `tecn.russia` (technician) |

**Two results, and the first one retracts a correction rather than making one.**

**1. The 3-vs-76 contact delay was the seed, not the rule.** First fire lands at tick 5003 in *both*
arms of the controlled pair. The standing wall delayed contact by **zero ticks**. So the unseeded
R2's apparent 73-tick delay — which I used above to walk back R3's conclusion — was noise of the
same magnitude as the effect being looked for. **R3's reading was right and the walk-back was wrong.**

**2. The wall made the cease-fire SHORTER, 98 → 38 ticks, and there is a mechanism.** The full
`[exp-defcon2-fire]` volley explains it:

    wall UP    tick 5003   TEN USA units -> tecn.russia#370        (all ten, one target)
               tick 5004   four Russian replies
               tick 5038   tecn.russia destroyed
    wall DOWN  tick 5003   THREE USA units -> ar.russia#391, #404  (three, two targets)
               tick 5098   ar.russia destroyed

With the band impassable, each unit's set of valid targets is smaller, so the per-unit target picks
**converge**: ten guns including an Abrams onto one 250-credit technician, against three guns spread
over two riflemen when the band is down. Concentrated fire on a soft target kills in 35 ticks; split
fire on riflemen takes 95. **So b1a does not merely fail to lengthen the phase — it shortens it, by
narrowing the target set and thereby concentrating the volley.** That is the opposite of its purpose.

*(Confidence, stated because I have been wrong here twice. The 98→38 difference is controlled and
is a fact about this pair. The mechanism — narrower valid-target set causes concentration — is read
off one volley and is an inference, not a measurement. It is a good inference because it predicts the
sign of the effect and the target counts, but a second seed would settle it.)*

*(Worth recording for its own sake: the first life taken in an Escalation match was a **technician**,
killed by ten units including a main battle tank. The casualty rule counts it — anything destroyed by
enemy action — which is decision 06 working as written.)*

---

## §B1, RULED: there is no geometric fix. B6 is the fix, and B1 closes.

**b1a — the border stands at DEFCON 2 — is REJECTED on measurement.** Controlled A/B: contact
unchanged, phase shortened from 98 ticks to 38, with a mechanism that predicts the sign. One field
of change for an actively counterproductive effect.

**b1b — a demilitarised zone — is NOT REJECTED but must not be built on the present evidence.** It
was never tested and its prior is now poor from two directions. Contact happened at tick 5003 in both
arms along a 110-cell border, and R3 shows the armies *dispersed* rather than massed — so widening
the band moves which handful of units is nearest rather than removing contact. And by b1a's own
mechanism a wider band narrows valid-target sets further, which concentrates fire and shortens the
phase again. If anyone wants it, it needs its own seeded pair before a line of code, and the cheaper
question comes first: **is there any band width that stops first contact happening within a few ticks
somewhere along 110 cells?** R3 suggests not.

**b1c — a minimum dwell — is REJECTED, and my original objection to it was wrong.** I wrote that it
would mean "autonomous fire stays held while units are dying, which is a rule the readout would be
lying about". That is not so: autonomous fire *is* held for the whole of DEFCON 2 and a dwell does not
change that, so the readout's rule line stays true. The real objection is the other line — a dwell
falsifies **`FIRST KILL ENDS THIS PHASE`**, which the readout pulses in those words and which is the
causal link the entire design rests on. Blurring "your decision ended it" is a worse cost than a short
phase.

**So the phase's length is not fixable by moving anybody or anything.** DEFCON 2 is a **trigger**, not
a phase, and every attempt to make it a phase by geometry either does nothing (b1b, expected) or backfires
(b1a, measured). **§B6 — draw one honest banner when the two edges land inside the hold window, and log
who took the first life — is the whole of the fix**, and it is now the top item in §B without
qualification.

**One constructive note that costs nothing to act on.** The mode *already ships* a configuration in
which the cease-fire is a real phase, and §2.4 found it before any of these runs: **`Opening phase =
Weapons free`**. Starting at DEFCON 2 raises no wall at all, both sides begin 50–77 cells apart with
nothing built, and the first contact is the two to three minutes it takes somebody to drive over and
choose to shoot. A host who wants the cease-fire drama has it today. That is worth a sentence in the
option's tooltip and no code at all.
