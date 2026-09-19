# DEFCON Escalation — whole-match gameplay review

**Ref:** `wt/escalation-review @ 442859aa` (forked from `main @ 442859aa`). Every file:line below was read
at that ref. **No game was launched for this document** — the runs it needs are specified in
§"Runs requested" and their results belong in §"Simulation results", which is deliberately empty.

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

**And in practice the phase lasts about one tick.** Three things line up to guarantee it:

1. Positioning delivers **both armies to the border**, because the border is the only thing there is to
   position against. The bots do it explicitly — `BorderStagingEnabled: true` clamps an axis to the near
   side rather than ordering it through (`ai.yaml:636-647`) — and a human does it for the same reason.
2. The wall comes down and the fire permission opens **on the same tick**. `ActiveLevels = { 3 }`
   (`DefconWall.cs:390-392`), `HoldFireLevel = Ceiling - 1` (`DefconFireDiscipline.cs:105`).
3. **Tank range exceeds the band.** `HalfWidth` is one cell, so two armies parked on the line are 2–3
   cells apart against weapon ranges several times that. Nobody has to move in order to shoot.

So the decision the phase exists to dramatise is made before the player has read the banner announcing
the phase — and that banner is then overwritten (§2.3). The evidence is already in the tree:
`DefconEscalation.cs:288-292` records the one-tick run as an *observability* problem for a test poller.
It is also a design one.

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

Recommendation: **b1a**, because it is one field, it matches the design's own language, and it is
trivially reversible. But the honest summary is that DEFCON 2 will be short in every bot match under all
three, so **B6 may matter more than B1.**

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

*Empty by design. The runs requested in the accompanying report are the manager's to execute; their
readings belong here. Two things in this document are predictions rather than measurements and are what
those runs would replace: §1.2's ×1.25 path-inflation factor, and §2.4's claim that DEFCON 2 lasts
about one tick in a bot-vs-bot match on the shipped clocks.*
