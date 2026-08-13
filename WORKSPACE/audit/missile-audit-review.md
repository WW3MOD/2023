# Adversarial review of the four missile audits

**Reviewed ref:** `main @ fbb226b7`, 0 commits behind `origin/main`.
The four reports stamp `main @ dc899995`. The only commit between the two is `fbb226b7`
("pipeline: reconcile 42-54…"), which touches `WORKSPACE/HOTBOARD.md` and `WORKSPACE/PIPELINE.md`
and nothing else (`git diff --stat dc899995..HEAD`). **Engine and mod-rules content is byte-identical
to the audited ref**, so every citation below is checkable at either SHA.

Read-only. No build, no test run, no game launch. Nothing staged, committed, or edited outside this file.

**Mandate:** find where the reports are wrong. This is not a summary and does not attempt balance.

---

## Verdict line per numbered claim

| # | Claim | Verdict |
|---|---|---|
| **1** | W1's D1 — the `flyStraight` latch | **Mechanism CONFIRMED. Worked timeline WRONG — three independent errors. Trigger condition WRONG (it is not short range, and not a fleeing target).** |
| **2** | `MaximumLaunchAngle: 1000` consequence — W2 vs W3 | **W2 is RIGHT, W3 is WRONG.** No tick-1 dip, no self-destruct. But W1/W2/W3 all *over-rate* the residual severity. |
| **3** | W2's invisible explosion | **CONFIRMED for ATGM. NOT ESTABLISHED for WGM — W2's own evidence contradicts it.** Reachability partially settleable statically; W2 punted unnecessarily on the ATGM half. |
| **4** | Missiles can never vanish | **CONFIRMED.** Nine-path enumeration is exhaustive; I found no removal outside `Explode()`. W2's T5 caveat is over-cautious and can be closed. |
| **5** | Damage-cliff numbers | **ATGM 3300× CONFIRMED exactly. Hellfire 138× CONFIRMED as an order of magnitude only** — it rests on an assumed tank orientation and the code blends facets rather than picking one. `PercentFromEdge`-from-corner **CONFIRMED**. |
| **6** | W4's fleet table | **CONFIRMED on 9 weapons spot-checked** (ATGM, WGM, WGM.bradley, Hellfire, MANPAD, Stinger, SAM, AAM, TimerWolf) **plus the full 31-row defaults table. One wrong default found.** The manager's turn-rate error survived nowhere. |
| **7** | Cross-report contradictions | W1's `loopRadius` D6 **CONFIRMED**. Two further contradictions found (`RangeLimit` semantics; WGM air-effect scope). |

---

## Errors found, ranked by damage if acted on

### E1 — CRITICAL. W1's D1 trace omits `leadTarget`. The quantity it tabulates is not the quantity the code tests.

This is the single most damaging error in the four reports, because D1 is nominated as "the reported
bug" and its trace is what a fix would be tuned against.

`Missile.cs:1005-1008`:

```csharp
var leadTarget = WVec.CalculateLeadTarget(pos, lastTargetPosition, targetPosition, 1, speed);
var tarDistVec = targetPosition + leadTarget + offset - pos;
var relTarHorDist = tarDistVec.HorizontalLength;      // ← passed to HomingTick at :1016
```

`relTarHorDist` is the horizontal distance to the **lead point**, not to the target. That is the
value assigned to `currentDistance` at `:834`, compared against `minDistanceToTarget` at `:839`, and
compared against `3 * loopRadius` at `:651`.

W1's §7.1 table (`| tick | speed | 3·loopRadius | relTarHorDist | state |`) lists `2048, 2273, 2473,
2648, 2798, 2923` — successive differences `+225, +200, +175, +150, +125`, i.e. exactly
`245 − speed`. That is **physical separation with zero lead**. W1 dismissed the lead term at §6:

> "At short range, `distanceToTarget < speed` ⇒ `ticksToReachTarget = 0` ⇒ **zero lead**"

`ticksToReachTarget = distanceToTarget / projectileSpeed` (`WVec.cs:172`). At W1's own tick 1 that is
`2048 / 20 = 102`, not 0. The zero-lead simplification holds only when the target is inside one tick
of travel — under 450 wdist for a MANPAD at terminal speed, and under **20** wdist at tick 1. W1
applied it across a 6-tick window where it is wrong by two orders of magnitude.

**Re-deriving W1's own scenario with the lead term** (MANPAD, `MaximumLaunchSpeed 20`,
`Acceleration 25`, `VerticalRateOfTurn.Facing 6`; helicopter receding at 245 from 2048;
`loopRadius = speed × 6400/942`; `speed` at tick *k* = `20 + 25(k−1)`; lead is collinear so
`rel = d + 245·⌊d/speed⌋`):

| tick | speed | d (physical) | lead | `relTarHorDist` | `3·loopRadius` | Hitting? |
|---|---|---|---|---|---|---|
| 2 | 45 | 2248 | 12005 | **14253** | 917 | no |
| 3 | 70 | 2423 | 8330 | **10753** | 1427 | no |
| 4 | 95 | 2573 | 6615 | **9188** | 1936 | no |
| 5 | 120 | 2698 | 5390 | **8088** | 2446 | no |
| 6 | 145 | 2798 | 4655 | **7453** | 2955 | **no** ← W1 says Hitting fires here |
| 9 | 220 | 2948 | 3185 | 6133 | 4483 | no |
| 11 | 270 | 2923 | 2450 | **5373** | 5502 | **yes** |

Two things follow, and both destroy the conclusion:

1. **`Hitting` does not engage at tick 6.** `relTarHorDist` is 7453 against a threshold of 2955.
   It engages around tick 11.
2. **`relTarHorDist` is monotonically *decreasing* over the whole acceleration ramp**, because the
   lead multiplier `(speed + 245)/speed` collapses from ~6.4× to ~1.5× faster than the physical gap
   grows. `minDistanceToTarget` therefore tracks it *down* every tick, and
   `currentDistance > minDistanceToTarget + CloseEnough` is **false at every tick**. The latch does
   not fire in this scenario at all.

W1's claim that `min` is "pinned at the launch distance — a distance it can never recover" is the
inverse of what the code computes.

**Two further, independent errors in the same trace:**

- **Ordering.** W1: *"At tick 6 the `Hitting` latch closes and the `flyStraight` test is evaluated
  for the first time … → `flyStraight = true` on the same tick."* Impossible. `state = States.Hitting`
  is assigned at `Missile.cs:654`, inside `HomingInnerTick`, which is called at `:850` — **eleven
  lines after** the test at `:839`. On the tick `Hitting` is first entered the test reads the *old*
  state and is skipped. The first evaluation is always the following tick. (Once `flyStraight` is
  true, `:848` short-circuits the ternary and `HomingInnerTick` is never called again — so the state
  machine also freezes.)
- **Tick 1 is not the launch distance.** `lastTargetPosition` is declared at `Missile.cs:252` and
  **never assigned in the constructor** (`:268-341`), so it is `WPos` default `(0,0,0)` on tick 1.
  `vectorDiffPerTick = targetPosition − (0,0,0)` is the target's *absolute map position* treated as a
  per-tick velocity, giving a lead of millions of wdist. W1's "2048 ← `min` pinned here" is not the
  value the code sees. See **M1** below — no report caught this.

#### What the latch actually does — because the defect is real, just not this one

The mechanism at `:839` is genuine and the manager has already verified the code reads as described.
Restating the trigger correctly, since a fix depends on it:

`relTarHorDist ≈ d · |speed ± v_target| / speed` for a collinear target. The missile therefore
measures a quantity that jumps whenever **the ratio** changes, not only when the missile actually
misses. At MANPAD terminal speed 450 against a 245 helicopter:

- approaching: `rel = 0.456 · d`
- receding: `rel = 1.544 · d`

**A helicopter that reverses course mid-engagement triples `relTarHorDist` in one tick with no change
in physical range.** With `min` sitting at the approaching value, `1.544d > 0.456d + 192` holds for
any `d > 177`. `flyStraight` latches, both axes freeze (`:847-850`), and recovery at `:843` requires
`rel < 192`, i.e. physical `d < 124` on a receding target — unreachable once steering has stopped.

That is a real, severe, and *deliberately triggerable* defect: it is exactly "the missile stopped
tracking and flew straight", and evasive manoeuvring is precisely what a helicopter does under AA
fire. But it is **not** range-gated, so W1's headline framing — *"short range is markedly worse …
the whole 2–4 cell band the user reported is covered"* — is unsupported. The discriminator is
target-velocity *change*, not launch range.

W1's proposed one-line fix (reset `minDistanceToTarget` at the `Homing → Hitting` transition,
§8 footer) addresses only the acceleration-ramp artefact I have just shown does not fire. **It does
not fix the direction-reversal case at all**, because that happens well inside the `Hitting` phase.
The root defect is that `:839`/`:843` compare a *lead-inflated* distance against `CloseEnough`, a
*physical* proximity constant. Fixing the units is the real repair.

---

### E2 — HIGH. W3's `MaximumLaunchAngle` self-destruct prediction is wrong. W2 is right, and settles the contradiction.

Resolving the direct contradiction the brief flagged. I read `TickFacing` and the ordering myself.

`Util.TickFacing(int facing, int desiredFacing, int rot)` — `Util.cs:30-44` — is a modular
shortest-arc step in `& 0xFF`. Tracing MANPAD tick 1 exactly:

- Constructor `:311` → `vFacing = −6` (`Clamp(+23, −16, −6)`; `(sbyte)(1000 >> 2) = −6` confirmed).
- `Tick:911` `ticks == 0 + 1` → `state = Homing`, `speed = velocity.Length ≈ 20`, `loopRadius = 135`.
- `HomingTick` → `HomingInnerTick`. `relTarHorDist ≫ 3·loopRadius (405)` → cruise branch `:798-812`.
- `:802` `vDist = new WVec(−diffClfMslHgt − 512, −20, 0) = (−512, −20, 0)` → `Yaw.Facing = 62`.
- `:809` `.Clamp(−vRot, +vRot)` = `Clamp(62, −6, 6)` = **+6**.
- `:897` `vFacing = TickFacing(−6, +6, 6)`: `leftTurn = (−12)&0xFF = 244`, `rightTurn = 12`; neither
  `< 6`; `rightTurn < leftTurn` → returns `(−6+6)&0xFF` = **0**.
- `:899-902` builds the move vector **from the post-turn `vFacing` = 0** → `move.Z = 0`.

`pos.Z` stays 0; `:1050` tests `0 < 0` → false. **No dip, no ground hit, no removal.** W2's tracing
is correct at every step and I reproduce it independently.

W3's arithmetic (`−20 × sin(8.44°) ≈ −2.9`) is right; its *premise* — that the constructor's `−6`
drives the first displacement — is false, because `:897` precedes `:899`. W3 flagged its own doubt
(§4.3: *"a missile removed on tick 1 should not be seen flying. Something in my composition is
therefore either wrong or incomplete"*) and was right to. **W3's D1 severity rating of "critical"
must be struck.**

**But all three reports over-rate what is left.** W3 says critical, W1 High, W2 Medium. The true
residual:

- `maxLaunchAngle` is read at exactly three places — `:359`, `:388` (both inside
  `DetermineLaunchSpeedAndAngleForIncline`, which requires `TerrainHeightAware && predClfDist > 0`
  and is dead in this mod for the reason W1 establishes in its §1.1) and `:434`. **The clamp affects
  the launch value only.**
- The cruise branch clamps `desiredVFacing` to `±VerticalRateOfTurn.Facing` = ±6 anyway (`:809`), so
  the missile could never pitch above +6 during cruise regardless of the launch angle.
- Cost: `vFacing` reaches +6 at tick 2 instead of tick 1. At MANPAD's tick-1/2 speeds (20/45), the
  foregone climb is **single-digit wdist**.

`MaximumLaunchAngle: 1000` is a config-hygiene error worth correcting, but it is **Low severity, not
critical/High/Medium**. Anyone reading W3's ranking would prioritise it over the actual defects.

---

### E3 — HIGH. W2's headline over-claims its scope: WGM is not established as affected, and W2's own cited evidence says so.

The core of claim 3 is **CONFIRMED**, and I verified every link independently:

- `^MediumExplosionEffects` (`weapons-effects.yaml:544`), `Warhead@Effect: CreateEffect` at `:553`
  with `ValidTargets: Ground, Ship, Trees, Mine` at `:554` — no `Air`. ✓
- `CreateEffectWarhead.IsValidAgainstTerrain` (`:149-157`) returns `IsValidTarget(TargetTypeAir)` when
  `dat > AirThreshold`; `Warhead.AirThreshold = new(128)` (`Warhead.cs:45`). ✓
- `DoImpact:121-122` returns early — **no sprite, no sound** — when `actorAtImpact == None` and
  terrain is invalid. ✓
- `ActorTypeAtImpact` (`:67-88`) requires `DistanceFromEdge(victim, pos).Length <= 0`, i.e. the impact
  must be *inside* a hitshape. A near-miss is `None`. ✓ (Note: for `RectangleShape` the WPos overload
  clamps to the `VerticalTopOffset` plane and then discards Z — `Rectangle.cs:124-135` → `:109-115` —
  so a detonation *directly above* a tank at any altitude reads 0 and **does** render. Only
  horizontally-outside near-misses go invisible. That is the case that matters.)

**The over-claim.** W2 writes: *"`ATGM` and `WGM` have no air-valid effect warhead at all … That is
the entire ground ATGM layer on both sides, and every one of its airborne detonations is completely
invisible and silent."* W2 asserts this for WGM without checking WGM's altitude.

`WGM` sets **`CruiseAltitude: 100`** (`weapons-missiles.yaml:77`) — **below** the 128 threshold. The
mod's own comment sitting nine lines above the Ataka block makes exactly this argument for the
identical value (`weapons-missiles.yaml:154-159`):

> *"Ataka's `CruiseAltitude` of 100 sits below the 128 `AirThreshold` at which `CreateEffectWarhead`
> promotes a position to the `Air` target type, so even a range-limit self-destruct … reads as terrain
> rather than air."*

W2 quotes the Ataka block elsewhere but does not apply its reasoning to WGM. WGM's cruise-phase and
fuel-out detonations sit at ~100 and resolve to **terrain**, so the `Ground` effect fires and they are
**visible**. WGM can transiently exceed 128 (the cruise branch oscillates around the setpoint, and at
speed 300 with `vFacing = 6` a tick moves Z by ~44), but that is an unquantified overshoot, not the
systematic hole W2 describes.

`ATGM` is a completely different animal: **`CruiseAltitude: 10c0` = 10240** (`:20`). An ATGM that
latches `flyStraight` during cruise fuel-outs at Z ≈ 10240 — eighty times the threshold. There the
defect is real and unambiguous.

**Consequence if acted on:** the repair plan would add a `Warhead@EffectAir` to WGM (and by
inheritance `WGM.bradley`, arming the Bradley and BMP-2) on the strength of a scope claim that is not
established, and would attribute Bradley/BMP-2 "vanishing" missiles to a cause that is at most
marginal for them. Adding the warhead is harmless; **believing it fixed the Bradley is not.**

**W2 punted unnecessarily on the ATGM half.** §F.1 calls the Z of `pos` at `Explode()` *"the single
most important unknown"* and says it "cannot be resolved by reading". For the fuel-out path (T4) it
can: `CruiseAltitude: 10c0` and `flyStraight` freezing `vFacing` (`:849`) put the detonation at
cruise altitude by construction. For the proximity paths (T3/T8) it is genuinely trajectory-dependent
and the punt is fair — the aim point sits at `targetPosition.Z + AirburstAltitude` = 32
(`:984`, `:17`), so a detonation satisfying `relTarDist < 298` can be anywhere from Z=0 to Z≈330,
straddling the threshold. **Verdict: half of §F.1 is UNVERIFIABLE STATICALLY, half was an unnecessary punt.**

---

### E4 — MEDIUM. W2's Hellfire 138× rests on an assumed geometry; the code blends facets rather than picking one.

The **ATGM 3300× is exact and I re-derived it from source end to end:**

- `TargetDamage`: `Spread` default `WDist(1)` (`TargetDamageWarhead.cs:24`); gate
  `closestDistance > Spread.Length → continue` (`:64-65`); modifier
  `PercentFromEdge(victim, args.ImpactPosition)` (`:67`).
- Centre hit: edge distance 0 → passes. `PercentFromEdge` = 100. `ArmorDirectionPercent` returns
  `distribution[3]` = **10** immediately for `TopAttack` (`DamageWarhead.cs:129-134` — checked
  *before* the directional branch). Abrams `Thickness: 700`, `Distribution: 100,40,15,10,10`
  (`vehicles-america.yaml:482-483`) → thickness `700*10/100` = 70. `Penetration 100 − 70 = +30 ≥ 0`
  → no reduction → **10000**.
- `SpreadDamage` at 0: `Falloff {100,37,14,5,0}` (`SpreadDamageWarhead.cs:28`), `effectiveRange[i] = i*Spread`
  (`:52`) = `{0,64,128,192,256}` → falloff 100. `Penetration` unset → default **1**
  (`DamageWarhead.cs:24`) → `2000*1/70` = **28**. Total **10028**.
- Half-cell miss, 512 from centre: `quadrantSize = (365,790)` → edge distance `512−365` = **147**.
  `TargetDamage`: `147 > 1` → **0**. `SpreadDamage`: `DamageCalculationType` default `HitShape`
  (`:34`) → `falloffDistance = 147`; `GetDamageFalloff(147)` → `Lerp(14, 5, 19, 64)` =
  `14 + (−171/64)` = `14 − 2` = **12**. `2000*1/70 = 28`, then `ApplyPercentageModifiers(28,[12,100])`
  = `(int)3.36` = **3**.

**10028 / 3 = 3343×. CONFIRMED.** The extraordinary claim survives.

**The Hellfire 138× does not survive at the same standard.** Hellfire has no `TopAttack`, so
`ArmorDirectionPercent` falls into the directional branch (`DamageWarhead.cs:141-193`), which does
**not** select a facet — it computes four alignment modifiers and returns
`(int)(frontDamage + leftDamage + rightDamage + rearDamage)`, a continuous blend of
`distribution[0..2]` driven by `victim.Orientation.Yaw − args.ImpactOrientation.Yaw`. W2's
*"Side facet → `distribution[1]` = 40"* is the value only at an exact 90° alignment, and W2 never
states the tank's assumed heading. Compounding it, `SpreadDamageWarhead:110-115` **overwrites**
`ImpactOrientation` with the impact→victim direction whenever `falloffDistance > 0`, so the alignment
in a near-miss is a function of *where the missile landed*, not where it came from.

The **73** and the **138×** should carry an explicit "assuming exact broadside" qualifier and be
treated as illustrative. The 3300× should not — `TopAttack` short-circuits the blend, which is
precisely why it is exact. W2 presents both with the same confidence.

`PercentFromEdge`-from-the-corner is **CONFIRMED**: `Rectangle.cs:118-122` computes
`total = |(quadrantSize.X, quadrantSize.Y)|` and returns `100*(total − fromEdge.HorizontalLength)/total`,
while the callers at `:141-147` pass the **raw relative position**, not a distance from any edge.
`isqrt(365²+790²) = isqrt(757325) = 870`; nose `100*(870−790)/870 = 9`; corner `0`; mid-side `58`.
All three of W2's figures reproduce. (W2 cites `:117-121`/`:135-137`; actual `:118-122`/`:137-147` —
one-line drift, substance correct.)

---

### E5 — LOW. W4's defaults table gets `RangeLimit` semantically wrong, and it contradicts W2.

W4 line 51: `` `RangeLimit` | `WDist.Zero` (= no limit) ``.

`Missile.cs:288`: `var limit = info.RangeLimit != WDist.Zero ? info.RangeLimit : args.Weapon.Range;`
and the `[Desc]` at `:107` says *"Zero for defaulting to weapon range. **Negative** for unlimited fuel."*
Zero means **defaults to weapon `Range`** — the opposite of "no limit". W2 has this right
(§A: *"defaults to weapon `Range` when zero (`Missile.cs:288`)"*), so the two reports contradict.

Inert in practice — all 14 live missile weapons set `RangeLimit` explicitly — but the brief
specifically asked whether any "defaulted" marking is wrong, and this one is. It is the sort of error
that becomes load-bearing the moment someone adds a missile weapon without a `RangeLimit`.

**Everything else in W4 checks out.** I verified all 31 rows of the projectile-defaults table, all 8
weapon-level defaults, all 10 warhead defaults, and spot-checked **nine** weapons field-by-field
against the YAML — ATGM, WGM, WGM.bradley, Hellfire, MANPAD, Stinger, SurfaceToAirMissile,
AirToAirMissile, TimerWolf_Missiles — including every `def`-marked cell. All raw→facing conversions
(`20→5`, `8→2`, `60→15`, `35→8`, `25→6`, `5→1`, default `24→6`) are correct.

**On the second half of claim 6:** the manager's wrong turn-rate default **survived nowhere**. W1 §0
records the correction and — correctly — shows nothing moved, because all four `LoopRadius` call sites
pass `VerticalRateOfTurn`. W2 §0 opens with it. W3 §4.3 records `WAngle(24).Facing == 6` explicitly.
Clean.

One presentational risk in W4: `` `MaximumLaunchAngle` | raw `1000` → facing 250 `` is true of
`WAngle.Facing` but the code applies `(sbyte)(Angle >> 2)` = **−6**. Reading only W4's table, "facing
250" looks like a steep upward angle. W1/W2/W3 all carry the `sbyte` cast; W4's row does not.

---

## What all four reports missed

### M1 — `lastTargetPosition` is read uninitialised on tick 1. Not mentioned in any of the four reports.

`Missile.cs:252` declares `[Sync] WPos pos, lastPos, lastTargetPosition;`. The constructor
(`:268-341`) assigns `pos` and `lastPos` and **never assigns `lastTargetPosition`**. It is first
written at `:1010`, *after* it is read at `:1005`.

So on tick 1, `CalculateLeadTarget(pos, (0,0,0), targetPosition, 1, speed)` computes
`vectorDiffPerTick = targetPosition − WPos.Zero` — **the target's absolute map coordinates
interpreted as a per-tick velocity**. For a target at cell (30,25) that is `(31232, 26112, 0)`;
multiplied by `ticksToReachTarget` (102 for a MANPAD at launch speed 20 and 2 cells) it yields a lead
vector of ~4.2 million wdist.

Consequences on tick 1:

- `desiredHFacing = velVec.Yaw.Facing` (`:847`) aims at a garbage point roughly in the direction of
  the target's position *from the map origin*, not from the missile. `hFacing` is then turned by up
  to `hRot` toward it — **up to 5 facings (7°) for MANPAD, 15 (21°) for Hellfire**, corrected from
  tick 2 at the same rate.
- `minDistanceToTarget` is seeded from a garbage value, so W1's "min pinned at launch distance" is
  wrong for a second reason.
- It is a `[Sync]`-adjacent read of an uninitialised field feeding a `[Sync]` field (`hFacing`).
  Deterministic (the value is a fixed default), so not a desync — but it is exactly the class of
  thing the project's own "sync changes need a determinism trace" rule exists to catch.

This is the same root cause as **E1**: the lead machinery is trusted uncritically. All four reports
cite `:1005`; only W1 discusses it, and only to dismiss it with a false generalisation.

### M2 — Tree-gating silently defeats itself on the three operator-retargeting weapons.

`Armament.cs:580-584`: when the `MissChancePerDensity` roll redirects a shot into a tree, it sets
`args.PassiveTarget = treeOnLine.CenterPosition` and **`args.GuidedTarget = Target.Invalid`**.

In `Missile.Tick:937-978`, `OperatorRetargetTicks > 0` (50 on `WGM`, `Ataka`, `Hellfire`) plus
`!args.SourceActor.IsDead` means `targetValid` is **false from tick 1**. The countdown starts
immediately and, after ~50 ticks scaled by veterancy, `FindRetargetCandidate()` swings the missile
onto the nearest valid enemy — while `:970-971` resets `flyStraight` and `minDistanceToTarget`.

**A shot the tree-gating deliberately made miss is silently un-missed a couple of seconds later**, on
exactly the three weapons the tree-gating was written for (`ClearSightThreshold`/`FreeLineDensity`/
`MissChancePerDensity` are set on WGM, Ataka and Hellfire — `weapons-missiles.yaml:41-43`, `:108-110`,
`:172-174`). Neither W3 (fire control, which covers the tree roll) nor W1 (which covers
`FindRetargetCandidate` and pronounces it "correct") connects the two. Not a crash, but it means a
balance lever is not doing what its comment says.

### M3 — The `flyStraight` recovery path is measured in the wrong units (root cause of E1).

`:843` `if (flyStraight && currentDistance < info.CloseEnough) flyStraight = false;` compares a
lead-inflated distance against a physical proximity constant. Against a receding target at MANPAD
terminal speed the inflation is 1.544×, so recovery demands a physical range of 124 rather than 192 —
and against a target receding faster than the missile it can never be satisfied at all (the map
carries aircraft at `Speed: 525`, `aircraft-america.yaml:572`, versus MANPAD's max 450). No report
identifies that `:839` and `:843` are unit-inconsistent; W1 attributes the non-recovery solely to
"the missile has stopped steering", which is true but secondary.

### M4 — Items the reports left open that can be closed statically.

- **W1 §9.6 / D14 and W2 D7 (`JamsMissiles` double-detonation).** Both flag it as unverified. It is
  settleable in one grep: the only `JamsMissiles` in the mod is **commented out**
  (`vehicles-america.yaml:491-493`). W2 got this right; W1 left it open unnecessarily. The code
  defect is real (`Explode` at `:866` does not `return`, and `Tick` can call it again at `:1096`),
  but it is fully dormant.
- **W2 §B.2 T5 (off-map inside the arming window), named as its weakest link.** Closeable. All three
  `Arm: 5` weapons travel `45+70+95+120+145 = 475` wdist (MANPAD) in five ticks — **under half a
  cell**. It requires a launcher firing outward from within ~0.46 cells of the map boundary. "Very
  close to unreachable" is safe; W2's "marginally yes" is over-cautious rather than wrong.
- **Removal paths (claim 4).** I searched independently and confirm the enumeration is exhaustive.
  `world.AddFrameEndTask(w => w.Remove(this))` at `:1144` is the only removal in the file;
  `World.Remove(IEffect)` (`World.cs:419`) and `World.RemoveAll` (`:430`) are the only engine-side
  removals, and `RemoveAll`'s sole caller is `FlashTarget.cs:39` filtering on `is FlashTarget`.
  `Armament.cs:590-592` adds the projectile unconditionally (`Missile.Create` never returns null).
  A double-`Explode` queues `Remove` twice, which is a no-op on a `List`. **No path removes a missile
  without `Explode()`. CONFIRMED.**

---

## What is safe to fix now vs. what needs the Phase 0 trace

**Safe to fix now — verified from source, no trace needed:**

| Fix | Why it is safe |
|---|---|
| Add `Warhead@EffectAir: CreateEffect` (`ValidTargets: Air`) to **ATGM** | Purely additive; renders something where nothing rendered. `CruiseAltitude: 10c0` guarantees the >128 case exists. Do it for WGM too if you like — but **do not record WGM as a fix for a confirmed defect** (E3). |
| `MaximumLaunchAngle: 1000` → a real upward value on MANPAD/Stinger | Config-only, one clamp site (`:434`), effect bounded to the launch tick. Re-rank it **Low**, not critical. |
| `RangeLimit` documentation correction in W4 | Doc-only (E5). |
| Initialise `lastTargetPosition = args.PassiveTarget` in the constructor (M1) | One line; replaces a garbage read with the value tick 2 would use. Strictly reduces tick-1 steering error. Changes `@stable` behaviour — say so in the commit message per `CLAUDE.md`. |
| `return` after `Explode(world)` at `:866` (jam double-detonation) | Dormant today; correct regardless. |

**Needs the Phase 0 trace before any code change:**

| Item | What the trace must answer |
|---|---|
| **The `flyStraight` latch (D1)** — highest priority | Log every `flyStraight` transition with tick, `state`, `relTarHorDist`, `minDistanceToTarget`, **the physical target distance separately from the lead-corrected one**, and target velocity. Without the physical/lead split the trace will reproduce W1's error. The hypothesis to test is now **direction-reversal**, not short range. |
| **The unit fix at `:839`/`:843`** | Whether to compare physical distance instead of lead distance is a behavioural change to every missile including `@stable`. Do not ship it on static reasoning alone. |
| **ATGM detonation altitude (T3/T8)** | W2's §F.1 bucketing (`<0 / 0–128 / >128`) is the right instrument, but bucket **per termination path** — T4 is predictable from `CruiseAltitude`, T3/T8 are not. |
| **WGM's actual Z distribution** | Settles E3. If WGM never exceeds 128, its air warhead is cosmetic parity, not a fix. |
| **The damage cliff (W2 D2)** | Numbers are confirmed; whether the *cliff* is the felt problem depends on the real miss-distance distribution, which W2 correctly says it cannot derive. |
| **W1 §7.2 hovering-helicopter reconstruction** | W1 itself least trusts this, and rightly. My re-derivation agrees with its *conclusion* (a hovering target is hit, because `relTarHorDist` decreases monotonically with zero lead) but by a different route, so treat the agreement as weak corroboration, not confirmation. |

**Do not act on:** W3's D1 severity ranking; W1's §7.1 timeline and its "2–4 cell band" framing; W1's
proposed `minDistanceToTarget` reset as a *sufficient* fix; W2's attribution of the invisible-explosion
defect to WGM/Bradley/BMP-2.
