# Why missiles miss humvees — and why fixing that would not save the AT screen

Investigation only. **Nothing in this document has been applied.** No weapon, unit or
engine file was edited. Worked on branch `wt/missile-miss` against `main` @ `8b71d83a`
(the commit that widened the humvee hitshape 440 → 470).

Every number is tagged **[measured]** (from a real run, by this work or a previous one),
**[simulated]** (from the two scripts added here), **[calc]** (arithmetic on shipped
constants) or **[reasoned]** (argued, not computed). That distinction has mattered
repeatedly in this project and the tags are load-bearing — the headline conclusion rests
on **[calc]**, not on the simulation.

---

## 0. Answer, up front

**The fuse is not broken, and neither of the two candidate causes is the problem.**

1. **The fuse straddle is real in the code but already covered, and the two obvious
   "fixes" both make ATGM *worse*.** The segment-closest-approach block below the PITFALL
   comment is not dead — it is the single biggest contributor to ATGM's hit rate. Removing
   it drops the kill rate against a stationary humvee from 77% to 45%. **[simulated]**

2. **Lead error against a fast mover is real and costs about 22 points of kill rate**
   (77% stationary → 52% against a humvee running straight at speed 150). **[simulated]**
   That is a genuine effect and it is the biggest accuracy term I found.

3. **And it barely matters.** The user's worry — that a mass of humvees overruns a
   well-equipped AT screen — is **correct**, but accuracy is not the mechanism. The
   binding constraint is rate of fire:

   > ATGM `Range` 20c0 minus `MinRange` 3c0 is a 17408-wdist envelope. A humvee at
   > `Speed: 150` crosses it in **116 ticks (4.6 s)**. ATGM's `BurstWait` is **200 ticks
   > (8.0 s)**. **Every AT specialist gets exactly ONE missile per closing humvee, and no
   > amount of accuracy can buy a second.** **[calc]**

   So M AT specialists can never stop more than M humvees per approach. At the shipped
   ~52% kill rate a screen of 6 stops about 3. **Raising the hit rate to a perfect 100%
   raises that from 3 to 6 — and the screen still leaks at 7 attackers.** **[simulated]**

**If you want the AT screen to hold against massed humvees, the lever is `BurstWait`,
`MinRange` or `Range` — not accuracy, not the fuse, not the hitshape.** Option 1 in §6.

---

## 1. What "a missile hits a humvee" actually means

Worth stating exactly, because two of the three effects here were not in anyone's model.

The whole hit test is **two-dimensional**. `FindActorsInCircle` discards height outright
(`WorldUtils.cs:79-85`, comment: *"Target ranges are calculated in 2D, so ignore height
differences"*) and `RectangleShape.DistanceFromEdge` zeroes Z before measuring
(`Rectangle.cs:109-116`). **A detonation directly overhead counts as distance zero no
matter how high it is.** Altitude cannot cause a miss. **[calc]**

`Warhead@Target` is `TargetDamage` with the default `Spread` of `WDist(1)`
(`TargetDamageWarhead.cs:23`), so the 10000-damage warhead applies **only if the impact
point is inside the humvee's hitshape** — a 470 × 1000 rectangle rotated with the humvee
(`vehicles-america.yaml:69-73`). Outside it, all that is left is `Warhead@Spread`:
`Spread: 64`, `Damage: 2000`, and no `Penetration`, so the default 1 against an effective
thickness of 8 gives `2000 * 1 / 8` = **250 before falloff**
(`DamageWarhead.cs:216-231`). Sixteen near misses to kill. It is noise.

And damage inside the rectangle is **scaled by distance from the centre**:
`CenterProximityPercent` normalises against the half-diagonal, 552
(`Rectangle.cs:123-127`), and the result is appended as a damage modifier
(`TargetDamageWarhead.cs:81-97`). With `Health.HP: 4000` since `ff14ece3`:

> a landed ATGM kills only if the impact is within **331 wdist of the humvee's centre**.
> An impact in the corners of the hitshape does real, non-lethal damage. **[calc]**

That last point invalidates an assumption in the existing
`tools/combat-sim/scripts/humvee-hitshape-ladder.py`, whose docstring asserts
*"P(hit) == P(kill)"*. That was true at 8000 HP with the old numbers; it is not true now.
Its `missiles/kill` column is optimistic. I have not edited it — see §6, option 6.

`TopAttack: true` is a **damage** flag only, selecting `Distribution[3]` = 80
(`DamageWarhead.cs:127-131`, `weapons-missiles.yaml:6`). It has zero effect on the
trajectory (`javelin-terminal-geometry.md` §1.2 established this and I re-confirmed it).

---

## 2. The fuse verdict

### 2.1 The straddle is real, and it is general — much more general than ATGM

`Missile.cs:1179-1183` warns that when `Speed > CloseEnough` the per-tick distance sample
can straddle the proximity sphere. The exact condition is geometric: an endpoint-only test
misses a sphere of radius `CE` when the chord the missile cuts is shorter than one tick of
travel, i.e. when the perpendicular miss distance `b` satisfies

```
2 * sqrt(CE^2 - b^2)  <=  Speed        =>        b  >=  sqrt(CE^2 - Speed^2/4)
```

**If `Speed >= 2*CE` the right-hand side is imaginary and the condition holds for every
`b` — the missile can pass through the exact centre of its own fuse sphere without a
single endpoint sample landing inside it.** **[calc]**

| weapon | Speed | CloseEnough | straddle band | |
|---|---|---|---|---|
| `ATGM` | 300 | 298 *(default)* | b ≥ 258 | narrow, top 14% of the radius |
| `WGM` / `.bradley` | 300 | 298 | b ≥ 258 | narrow |
| `Ataka` / `.AA` | 400 | 298 | b ≥ 221 | top 26% |
| `Hellfire` / `.Littlebird` | 500 | 298 | b ≥ 162 | top 46% |
| `Hellfire.strykershorad` | 400 | 298 | b ≥ 221 | top 26% |
| `MANPAD` | 450 | 192 | **all b** | whole sphere |
| `Stinger` / `9M311` | 600 | 256 | **all b** | whole sphere |
| `SurfaceToAirMissile` | 800 | 400 | **all b** | whole sphere, exactly on the boundary |
| `AirToAirMissile` | 800 | 400 | **all b** | whole sphere |
| `TimerWolf_Missiles` | 850 | 298 | **all b** | whole sphere, worst in the file |

**[calc]** from `weapons-missiles.yaml`. `CloseEnough` is the `Missile.cs:203` default of
298 for every weapon that does not declare it, which is all of the ATGM family.

So the PITFALL comment understates the problem: it names Hellfire, but **the entire
surface-to-air and air-to-air inventory is in the total-straddle regime**, not just the
marginal one. That is the general finding, and it is the most interesting thing in this
document that is not about humvees.

### 2.2 …and it is already covered, by code that is doing far more work than anyone realised

The segment-closest block at `Missile.cs:1188-1214` is armed on every tick of every one of
these missiles. It is gated only on `state != States.Freefall`, and Freefall is entered
only by the range-limit predicate at `:998`, which fires the fuel-out termination in the
same tick — so it never switches off in practice.

I measured how much it is worth by ablating it in simulation:

| target motion | shipped | clause 9 removed | Δ |
|---|---|---|---|
| stationary | **77.2%** | 45.0% | −32 |
| straight @105 | **74.6%** | 54.0% | −21 |
| straight @150 | **51.7%** | 42.9% | −9 |
| turning @105 | **55.6%** | 45.2% | −10 |

kill rate per missile, **[simulated]**, `atgm-terminal-hit-rate.py --trials 8000`.

The mechanism is worth understanding because it is *not* what the comment claims. Three
points are in play, not two:

```
P1 = targetPosition                          the humvee
P2 = targetPosition + leadTarget             clause 9's centre  (Missile.cs:1194)
P3 = targetPosition + leadTarget + offset    clause 4's centre, and what the missile steers at
```

Clause 4 (`:1163`) fuses on the **offset** aim point; clause 9 fuses on the **un-offset**
lead point and, when it fires, **relocates the impact to the closest point of the swept
segment to P2** (`:1207`). Near intercept the lead term is small, so P2 is close to the
humvee — and clause 9 therefore converts what would have been a detonation up to 724 wdist
away into one at the trajectory's closest approach to the actual target. **It is not a
straddle guard that occasionally saves a shot. It is the primary hit mechanism, and it
partially cancels `Inaccuracy: 512`.** That is why 50–73% of simulated shots end
`segment_closest` — matching the **[measured]** 377-of-640 (59%) in the retained corpus.

### 2.3 The `offset`-versus-aim-point divergence at `Missile.cs:893-903` is real and benign

The comment is correct that nothing bounds `offset` by `CloseEnough`, and correct that
clauses 4 and 9 measure to different points. What it does not say is that this asymmetry
is **load-bearing in the missile's favour**: because clause 9 excludes the offset, a
missile whose aim point was thrown 500 wdist wide still fuses when its path grazes the
real target. Making the two clauses agree — by adding `offset` to `:1194` — would delete
most of §2.2's benefit. **Do not "tidy" this.**

### 2.4 There *is* a genuine tick-order defect, and fixing it makes things worse

`relTarDist` is computed from the pre-move position (`Missile.cs:1103-1105`), the missile
then moves (`:1134`), and the fuse is tested against the **stale** distance while `pos`
has already advanced (`:1163`). A clause-4 detonation therefore lands up to one full tick
of travel — 300 wdist, more than the humvee's entire 470 width — past the point that
satisfied the fuse. **[calc]**

It looks like a bug. I simulated both repairs:

| target motion | shipped | detonate where the fuse was satisfied | test the fuse after the move |
|---|---|---|---|
| stationary | **77.2%** | 54.9% | 52.0% |
| straight @105 | **74.6%** | 58.3% | 57.7% |
| straight @150 | **51.7%** | 42.1% | 46.7% |
| turning @105 | **55.6%** | 43.6% | 44.0% |

**[simulated]**. Both are worse, everywhere, by 15–22 points. The reason is §2.2: firing
clause 4 a tick earlier, or at an earlier position, pre-empts clause 9 — and clause 9 is
the accurate one. The overshoot is load-bearing for the same reason the offset asymmetry
is.

**Verdict on the fuse: leave it alone.** All three plausible edits are net-negative and
two of them look like obvious corrections. This is the most useful thing this
investigation produced, because it is the change someone would otherwise make on sight.

---

## 3. Lead error — the real accuracy term

`CalculateLeadTarget` (`WVec.cs:168-176`) is a first-order intercept estimate:
`targetVelocityPerTick * floor(horizontalRange / missileSpeed)`. Against a humvee it puts
the aim point **half the current range ahead of the target** — 150 wdist of target motion
per tick against 300 wdist of missile speed. The estimate is only sound when the missile
is much faster than the target; at 2:1 it is not.

Consequence, from the triangle inequality: the effective fuse radius measured in *true*
distance to the humvee swings with aspect. Head-on the aim point sits between missile and
target and the missile fuses out to ~596 wdist; in a tail chase it sits beyond the target
and the missile must close to ~199 before clause 4 will fire. **[calc]**

Simulated kill rate, per missile:

| target motion | landed | **killed** | missiles/kill | median miss |
|---|---|---|---|---|
| stationary | 84.1% | **77.2%** | 1.29 | 158 |
| straight, speed 105 *(measured cap)* | 78.6% | **74.6%** | 1.34 | 222 |
| straight, speed 150 *(nominal)* | 55.9% | **51.7%** | 1.93 | 303 |
| turning at `TurnSpeed 19` | 59.3% | **55.6%** | 1.80 | 284 |
| reversing 1500 wdist out | 64.0% | 60.4% | 1.66 | 254 |
| reversing 1000 wdist out | 82.5% | 77.3% | 1.29 | 196 |

**[simulated]**, 16k shots per case, swept over 8 approach bearings × 8 humvee headings
(neither averages out: the inaccuracy cloud is square in world axes while the hitshape is
a rectangle in the actor frame).

Note the gap between the two speed figures. The prior run **[measured]** the humvee's
actual cap on clear terrain at **105 wdist/tick**, not the nominal 150
(`javelin-terminal-geometry-run.md` §4) — and the difference between those two rows is 23
points of kill rate. **Which figure is right is the single largest uncertainty in this
document.** A humvee at 105 is a modest problem; a humvee at 150 is the user's problem. §7
says how to settle it.

### How the simulation was checked

It is a new instrument, so its agreement with reality is worth stating rather than
assuming. Three independent checks, none of them tuned:

- **Detonation rate 100%** in every case, against **[measured]** 556 shipped flights with
  zero survivals and zero fuel-outs.
- **End-reason split** 50–73% `segment_closest`, against **[measured]** 377/640 = 59%.
- **Terminal `vFacing`** settles at −8…0, against a **[measured]** shipped band of −2…−15.
  This is the loosest of the three and the vertical channel is an analogue rather than a
  port of `HomingInnerTick` — but §1 established that altitude cannot cause a miss, so the
  vertical enters only through the 3-D fuse distances. Sweeping the starting altitude
  across the measured 150–800 band moves the stationary kill rate by **1.5 points**.

---

## 4. Mass versus defence — the user's actual question

Hit rate does not answer "can N humvees overrun M AT specialists". Three shipped numbers do:

```
ATGM Range      20c0 = 20480     weapons-missiles.yaml:7
ATGM MinRange    3c0 =  3072     weapons-missiles.yaml:8   cannot fire inside this
ATGM BurstWait   200 ticks       weapons-missiles.yaml:9   Burst defaults to 1, so this
                                                           IS the shot-to-shot cycle
```

Envelope 17408 wdist. Crossing time **116 ticks at speed 150, 166 at the measured 105**.
Shot cycle **200 ticks**. Both crossing times are shorter than one reload, so the result
does not depend on which speed figure is right:

> **Each AT specialist fires exactly one missile at a humvee closing from maximum range.**
> The 3-round magazine (`infantry.yaml:1650`) is irrelevant to a single approach. **[calc]**

Running the engagement tick by tick, with missile flight time, with several launchers able
to commit to the same target before the first missile lands, and with non-lethal hits
leaving a live humvee — humvees reaching contact, mean of 120 approaches at speed 150:

```
            M=2      M=4      M=6      M=8     M=12
N=2         1.0      0.3      0.2      0.2      0.3
N=4         3.1      1.9      1.1      0.6      0.4
N=6         5.0      3.9      3.0      1.7      0.8
N=8         6.9      6.0      4.6      3.9      2.0
N=12       11.0     10.0      8.8      7.8      5.7
N=16       14.9     14.2     12.9     11.8      9.6
```

**[simulated]**, `atgm-screen-throughput.py`. A screen of 6 AT specialists — 1800 credits,
a serious investment — stops **three** humvees, and lets three through, out of six.

And the decisive column, isolating accuracy by forcing the kill probability rather than
simulating it (M = 6):

| forced kill probability | attackers needed to leak the screen |
|---|---|
| 25% | 3 |
| 40% | 4 |
| 55% | 5 |
| 70% | 6 |
| **100%** | **7** |

**[simulated]**. **Perfect missiles buy four extra kills and the screen still fails at
seven attackers**, because M launchers firing once each can never produce more than M
kills. The cost ratio at the breakpoint sits between 0.97× and 1.25× — the attacker pays
roughly what the defender paid to punch through, which is a weak result for a dedicated
anti-armour unit in a prepared position.

**The user's fear is confirmed, and the diagnosis is rate of fire, not accuracy.**

---

## 5. What was NOT the problem

Recorded so nobody re-opens these.

- **Missiles failing to detonate.** 556 shipped flights, zero survivals, zero fuel-outs
  **[measured]**. The simulation independently reproduces 100% detonation. Whatever "they
  miss" looks like on screen, the missile explodes.
- **Altitude / the top-attack dive.** There is no dive (`CruiseAltitude: 10c0` is
  unreachable; the missile flies a 7–8° glide), `TopAttack` is a damage flag, and the hit
  test is 2-D anyway.
- **The hitshape.** It was widened 440 → 470 at `8b71d83a`. That is worth about 5 points
  of landed rate against a stationary target **[measured, by the prior ladder script]** and
  is swamped by everything in §3 and §4.

---

## 6. Candidate changes, ranked, with blast radius

Nothing here is applied. Ranked by how much of §4 they actually fix.

### 1 — Cut `ATGM BurstWait` — **fixes the actual problem**
`weapons-missiles.yaml:9`, `200` → `120` or below. **Weapon-scoped.**
At 120 an AT gets a second shot inside the 166-tick crossing at the measured humvee speed,
though still not at the nominal 150 — for two shots at *both* speeds it must go below 116.
This is the only option in the list that raises the ceiling on kills per approach rather
than raising the fraction of a fixed shot budget that connects.
**Also touches:** every ATGM user, which is `AT` only (`infantry.yaml:1646`) — `WGM` and
`Ataka` are separate weapons and are unaffected. It makes the AT specialist stronger
against *everything*, not just humvees, including tanks it is already good against, so it
is a general anti-armour buff and should be sized against the tank matchups too. The
3-round magazine starts to bind: at `BurstWait 120` an AT empties in 240 ticks and then
needs `truk`/`logisticscenter` resupply, which shifts the pressure onto the supply system.

### 2 — Raise `ATGM Range` or drop `MinRange` — same lever, different end
`weapons-missiles.yaml:7-8`. **Weapon-scoped.**
Widening the envelope buys crossing time, which buys shots, with the same arithmetic as
option 1: the envelope must exceed `BurstWait * humveeSpeed` = 30000 wdist at speed 150
for a guaranteed second shot, so `Range` would have to go to roughly **32c0** at the
current `MinRange` — a very large change. Dropping `MinRange` to 0 adds only 3072 wdist,
which is **20 ticks** of crossing at speed 150 — a tenth of what a second shot costs, so
it buys nothing on its own — and it would additionally unmask the latent tick-1
airburst lead spike documented at `javelin-terminal-geometry.md` §3.2, which is currently
held off *by* `MinRange: 3c0`. **I would not drop `MinRange`.**

### 3 — Slow the humvee — **unit-scoped, and the widest blast radius here**
`vehicles-america.yaml:76`, `Speed: 150`. Buys crossing time for every AT weapon at once
and closes the §3 lead-error gap as a side effect (the 150 → 105 rows differ by 23 points
of kill rate). But the humvee's speed is its identity — it is the fastest vehicle in the
game and the reason it is used as a scout and a transport — and this touches every
engagement it has, not just the AT one. **High risk of fixing this problem by deleting the
unit's role.**

### 4 — Give `Warhead@Spread` a real radius — accuracy-scoped, cheap, general
`weapons-missiles.yaml:29-32`: `Spread: 64` and no `Penetration`. Raising `Penetration` to
~10 (matching Light thickness, exactly the fix already applied to `Hellfire` and `Ataka`
for the heli case, see `weapons-missiles.yaml:281-291`) and `Spread` to ~192 would make
near misses do meaningful damage instead of 250. Converts §3's "landed vs killed" gap into
attrition and makes two near-misses kill.
**Also touches:** every ATGM target, including infantry and tanks — a 192 splash with
useful penetration is a real anti-infantry buff on a weapon whose `ValidTargets` is
`Vehicle, Defense, Water`, so it would splash onto anything nearby. Sizeable, and it does
**not** help §4's ceiling: it raises `p`, and §4's forced-`p` table says that is worth at
most four kills.

### 5 — Reduce `ATGM Inaccuracy` — accuracy-scoped, smallest real effect
`weapons-missiles.yaml:12`, `512` → e.g. `256`. Straightforward, weapon-scoped, and it
moves `p` upward. §4 caps the value of that. Note it interacts with §2.2 in a non-obvious
way: much of the inaccuracy is *already* being cancelled by clause 9, so the marginal
return on tightening it is lower than the raw number suggests.

### 6 — Correct the stale docstring in `humvee-hitshape-ladder.py` — docs-scoped, free
It asserts `P(hit) == P(kill)`, which stopped being true when HP went 8000 → 4000 at
`ff14ece3`. Its `missiles/kill` column is optimistic and someone will quote it. Same for
the `javelin-probe-lib.lua` comment that says the humvee is "left at its shipped 8000 HP".
**No behaviour, no risk.** I left both alone because the brief was to change nothing.

### Explicitly NOT recommended
- **Any edit to the fuse** (§2.2, §2.3, §2.4). All three candidates measured worse.
- **Widening the hitshape further.** `8b71d83a`'s own comment records that at 500 wide the
  humvee stops being the hardest target in the game; and §4 caps the value anyway.

---

## 7. What to run, in what order — I have launched nothing

Two runs. The first is the one that matters.

**Run 1 — `test-javelin-reversal-sweep`, unmodified, already exists.**
```
tools/autotest/run-test.sh test-javelin-reversal-sweep --missile-trace --timeout 330
tools/autotest/analyze-atgm-hit-rate.py <run-dir> --weapon ATGM
```
It already fires ~171 ATGMs at a moving humvee and the trace already records
`damage_to_target` per missile (`MissileTrace.cs:435`) — nothing new has to be recorded,
only read differently, which is what the new analyzer does.
**Settles:** the measured kill rate against a moving humvee, and therefore whether §3's
simulated table is trustworthy at all. If measured and simulated disagree by more than a
few points, **discard §3 and everything in §6 that depends on it**.
**Does not settle:** §4. The rig fires on a 10-tick throttle with a 30-tick reload
override in its own `rules.yaml`, deliberately not the shipped `BurstWait: 200`, so it
cannot measure the rate-of-fire ceiling. §4 is `[calc]` from three YAML constants and does
not need a run.

**Run 2 — `test-atgm-humvee-motion`, new, added by this work, NEVER RUN.**
```
tools/autotest/run-test.sh test-atgm-humvee-motion --missile-trace --timeout 330
tools/autotest/analyze-atgm-hit-rate.py <run-dir> --by-launcher
```
Eight lanes at one trigger range, each pinning a motion state (control/straight, stopped,
reversed, turning), so a launcher cell identifies the condition.
**Settles:** whether the *spread across motion states* in §3 is real, which is the specific
claim that "the humvee's movement is what makes missiles miss".
**Does not settle:** the absolute rate — its engagement band is 4–6 cells, not the 20-cell
band a real approach starts from.
**Health warning:** it is unrun. The library only perturbs a target already moving at
≥ 80 wdist/tick, so the stopped and turning lanes may perturb less often than the
reversing ones; the verdict note reports `perturbs` per lane and **a lane showing near-zero
is measuring the control condition, not its nominal one.** Run 1 first, and if it is
enough, Run 2 may not be worth the slot.

**Do not** re-run `test-javelin-stationary-tail` for this question. Its target is a `t90`,
not a humvee.

---

## 8. What I could not settle

- **Whether the humvee moves at 150 or 105.** The nominal is 150; the prior run
  **[measured]** 105 on clear terrain, and §3 says the two differ by 23 points of kill
  rate. I did not investigate why — `Mobile.Speed` is modified by terrain speed
  multipliers and by the `lightwheeled` locomotor, and I did not read either. **This is
  the largest single uncertainty here** and it is cheap to resolve from Run 1's
  `launch_tgt` deltas.
- **Whether my terminal simulation is right in absolute terms.** It reproduces three
  measured aggregates (§3) but has never been checked against a per-record measured hit
  rate, because the retained trace corpus lives on the Windows box and this work ran on
  macOS. Run 1 is the check. Until then §3 is simulation and §6 options 4 and 5 rest on it.
- **The vertical channel.** It is an analogue of the horizontal one, not a port of
  `HomingInnerTick`. §1 argues altitude cannot cause a miss and the sensitivity sweep says
  1.5 points across the measured band — but if `HomingInnerTick` does something I have not
  anticipated near intercept, that argument is where it would break.
- **Whether the AA missiles in §2.1's total-straddle regime are actually losing shots.**
  I established the geometry and that clause 9 covers them, but I did not measure any AA
  engagement, and their `Inaccuracy` (400 against `CloseEnough` 400) is proportionally far
  larger than ATGM's. **If the user has ever felt that SAMs or Stingers under-perform,
  that is the thread to pull, and it is a bigger one than this.**
- **Whether option 1 breaks the AT-versus-tank matchup.** Cutting `BurstWait` nearly
  doubles the AT specialist's damage output against everything. I sized it against
  humvees only.

---

## Artifacts added by this work

| file | what it is |
|---|---|
| `tools/combat-sim/scripts/atgm-terminal-hit-rate.py` | terminal-geometry simulator; ports the fuse arithmetic and the 2-D damage model, sweeps motion state, and ablates the two fuse mechanisms |
| `tools/combat-sim/scripts/atgm-screen-throughput.py` | N-humvees-vs-M-AT engagement model; draws its damage from the above so the two cannot drift |
| `tools/autotest/analyze-atgm-hit-rate.py` | reads any `result.missiles.jsonl` and reports landed/killed rate — the measurement instrument for §7, tested against a synthetic trace |
| `tools/autotest/scenarios/test-atgm-humvee-motion/` | motion-state sweep scenario, **never run** |
