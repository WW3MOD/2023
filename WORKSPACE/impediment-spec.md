# Impediment — design spec

**Ref: `main @ 557c38d7`**, worktree `wt/impediment-spec`. First written against `main @ 71fefe8c`.
**Design only.** No behaviour changed, no YAML rules edited, no engine C# edited, nothing built, launched or linted.
The one exception the brief asks for — the wrong comment at `vehicles.yaml:304-306` — is **specified here, not applied**; §3.3
gives the replacement text for the implementer.

Every claim about current behaviour carries a `file:line`. Inference is labelled **INFERENCE**. Where
[`hurt-unit-modifiers.md`](hurt-unit-modifiers.md), [`indicator-mechanics.md`](indicator-mechanics.md) or
[`indicator-audit.md`](indicator-audit.md) already settle something it is cited, not re-derived — except where I checked and
found something to add, which is flagged.

---

## ⚠️ CORRECTED 2026-09-12 — the band-product error, and what the sweep for it found

**Superseded in place, not rewritten.** The first draft of this document (`89875c06`, 2026-09-11) contained an arithmetic
error class, reported by the Stage 1 implementer and recorded at `WORKSPACE/DISCOVERIES.md` (2026-09-12 entry). The class:

> **`heavy-damage-attained` is a LATCH, not a band.** `^DamageStates` grants it on `ValidDamageStates: Heavy, Critical`
> (`defaults.yaml:278-280`) while `critical-damage` is granted on `Critical` alone (`:281-283`). Below 25% HP **both** hold,
> both traits are enabled, and `Util.ApplyPercentageModifiers` multiplies them — no max-wins, no dedupe
> (`Util.cs:238-246`). **The bottom band is a product.** The infantry Light/Medium/Heavy tokens are exclusive bands and do
> *not* have this shape, which is exactly why reading one pattern as the other is the trap.

**Two errors were reported. The sweep found three more, and one of them changes a design conclusion.** Full account in
§9; the corrections are applied in place throughout, and every figure that moved is marked ⚠️ at its site.

| # | Where | Was | Is | Source |
|---|---|---|---|---|
| 1 | §2.2 aircraft speed | "preserves today's endpoints exactly — 90 / 75" | Effective floor is 90% × 75% = **67.5%** | Reported |
| 2 | §2.3 | aircraft "twice as accurate" | 75% × 50% = 37.5%, i.e. **2.67×** | Reported |
| 3 | **§1.2 infantry table** | Heavy-band speed "25" | 25% × 0% = **0** — and this is the derivation the whole curve rests on | **Sweep** |
| 4 | §3.2 | "~85% … = 34%" | Misread of my own table: **95% … = 38%** | **Sweep** |
| 5 | §2.5 | "of 60 sites, 46 untouched" | Counts did not reconcile (they summed to 69). Correct figure is **50 untouched, 10 replaced** | **Sweep** |

**Error 3 is the consequential one.** Chasing it showed that three multiplier traits this spec had classified as *ladder
rungs* are in fact *gates wearing a multiplier's clothes* — `Modifier: 0` hard stops, each recorded in-file as a deliberate
user ruling. Reclassifying them (§2.1) both repairs the §1.2 derivation **and** removes an unflagged behaviour change the
first draft would have shipped: infantry regaining movement below 50% HP, and vehicles regaining turret traverse below 50%,
against rulings pinned by NUnit. **The corrected proposal is substantially safer and smaller than the first draft.**

---

## 0. Summary

### 0.1 The shape of the recommendation

| | Recommendation |
|---|---|
| **Name** | **`impediment`** — but as a **second, derived token**, not a rename of `suppressed`. §4. |
| **Range** | **0–100 on every chassis**, all of it reachable on every chassis. Chassis differences live in the conversion weight `k`, never in the range. §1, §5. |
| **Inputs** | `I = clamp(0, 100, S + D)`. `S` = suppression stacks from warheads (decays, as today). `D = round(k × (100 − HP%))`, a permanent floor, decay-exempt. §1. |
| **Scope** | Impediment owns the **graded** axes. Every **gate** — including three `Modifier: 0` traits that look like ladder rungs — stays on the damage tokens. §2.1. |
| **Why not a rename** | Seven live consumers read the raw count as a threshold meaning *"under fire"*. Folding damage in makes an ATGM soldier stop firing at 91% HP, permanently. §2.4. |
| **Engine cost** | One `Info` field on `ExternalCondition` (decay-exempt source) + one small trait. §1.4. |
| **Migration** | 60 token sites, of which **50 are untouched and 10 replaced** — plus 15 infantry traits outside those 60. §2.5. |

### 0.2 ⚠️ `hurt-unit-modifiers.md` §7 is wrong about where the affordance is

That document offers `GrantStackingConditionOnHealthFraction` as *"the one piece of 'damage → graded scalar' machinery that
already exists"* needing **"no new C#"**, then identifies the trap (`ReleaseTo` bypasses `ExternalCondition`). **The trap is
not a caveat on that recommendation — it invalidates it.** Verified both halves:

- `ReleaseTo` calls `self.GrantCondition(Info.Condition)` directly — `GrantStackingConditionOnHealthFraction.cs:103`.
- `ExternalCondition`'s cap is enforced only in `CanGrantCondition`, counting `permanentTokens` (`ExternalCondition.cs:103-105`),
  and its decay loop iterates only `permanentTokens` (`:212-225`) — a dictionary written only by
  `ExternalCondition.GrantCondition` (`:170`).

A health-driven grant lands in **neither**. The resulting stack is uncapped *and* undecaying *and* invisible to the cap
arithmetic the warhead grants rely on. §1.4 prices the real fix.

Second correction to the same section: it offers the commented-out `ConditionModifier@Rank_1` block (`defaults.yaml:296-299`)
as *"the shape a health→suppression link would take"* — a reading [`indicator-mechanics.md`](indicator-mechanics.md) Q1
repeats, flagging as a gap that it never checked whether the trait is live.

**It is not. `ConditionModifier` does not exist in the engine.** `grep -rn "ConditionModifier" engine --include=*.cs`,
excluding the unrelated `DetectableAddativeModifier`, returns nothing (exit 1). That block cannot be uncommented. This
closes `indicator-mechanics.md` honest-gap #4 with a negative answer and removes the last apparent zero-C# route.
**Every version of this feature requires engine work.**

### 0.3 The structural idea the rest of the spec rests on

Suppression and damage are **not** two spellings of one thing. They are two *inputs*, read by two *kinds of consumer*:

- **Graded consumers** ask *"how degraded is this unit?"* — speed, vision, burst, burst-wait, accuracy, turret traverse.
  These want the **sum**. This is the user's model and it is correct for them.
- **Gate consumers** ask *"is this unit under fire right now?"* or *"has this unit crossed a line?"* — take cover, recall to
  shelter, hold the ATGM, stop shooting, stop moving. These want **suppression alone**, or the damage token alone. A man at
  40% HP standing in an empty field is not under fire and must not behave as if he were.

Collapsing both into one number serves the first group and corrupts the second. So `suppressed` stays exactly as it is —
same warheads, same cap, same decay — and `impediment` is a derived total that only the graded consumers read. **No warhead
is edited. No grant site moves.** That is also cheaper than the rename [`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §7
costs at 139 grant sites plus 8 scenario YAML and 13 scenario Lua files (counts re-verified at this ref).

---

## 1. The quantity

### 1.1 Definition

```
I  =  clamp(0, 100, S + D)

S  =  suppression stacks    0 … cap_S    granted by warheads, decays as today
D  =  round(k × (100 − HP%))             derived from health, permanent, decay-exempt
```

`I` is a stacked integer condition named `impediment`, in every way an ordinary `ExternalCondition` — so the existing ladder
idiom (`RequiresCondition: impediment > N && impediment <= N+10`) works unchanged, which is the only shape this codebase
makes easy ([`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §7: no aggregator exists among the 41 files in
`Traits/Conditions/`).

### 1.2 ⚠️ CORRECTED — why `D = 100 − HP%`, and what the corrected arithmetic does to the argument

**The first draft's version of this table read the Heavy-band infantry speed as 25. That was the band-product error:
`SpeedMultiplier@HeavyDamage` (25, on the exclusive `heavy-damage` band, `infantry.yaml:1092-1094`) and
`SpeedMultiplier@CriticalDamage` (0, on the latching `heavy-damage-attained`, `:1114-1116`) are both live in 25–50%, so the
effective speed there is 25% × 0% = 0.** [`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §2 caught this correctly and I
failed to carry it across — the error is mine, not inherited.

Corrected, the raw comparison is:

| Band | HP | **Effective** speed today | `D` at midpoint | Suppression ladder at that `D` |
|---|---|---|---|---|
| Light | 75–100% | 75 | 12 | 80 |
| Medium | 50–75% | 50 | 37 | 60 |
| Heavy | 25–50% | **0** (25 × 0) | 62 | 30 |
| Critical | <25% | **0** (0 persists) | 87 | 10 |

So the original claim — *"reproduces the existing infantry damage ladder to within one suppression tier at every band"* —
**is false as stated.** Today's infantry are at a dead stop below 50% HP; a naive impediment ladder would have them moving
at 30% and 10%.

**But the comparison was the wrong one, and that is the real finding.** The zero is not the bottom of a ladder — it is a
**hard stop**, introduced 2026-08-23 by user ruling, documented at `infantry.yaml:1108-1113` (*"this remains a hard stop
rather than a slow"*) and pinned by `PanicGateAtCriticalDamageTest`, whose doc-comment records the ruling that the panic,
wander and speed gates *"must move TOGETHER"* (`:8-13`). Hold the gate aside and compare ladder to ladder:

| Band | Ladder rung today | `D` at midpoint | Suppression ladder at that `D` | Fit |
|---|---|---|---|---|
| Light | 75 (`:1056`) | 12 | 80 | within one tier |
| Medium | 50 (`:1072`) | 37 | 60 | within one tier |
| Heavy | 25 (`:1092`) | 62 | 30 | within one tier |
| Critical | 10 vision/burst (`:1117`, `:1120`) | 87 | 10 | exact |

**The derivation survives, on the corrected reading.** The identity map matches the *graded* portion of the shipped infantry
ladder to within one suppression tier at every band — which remains good evidence that two ladders tuned years apart by
different hands were converging on one curve. What changed is that the hard stop is now correctly excluded from the fit
**and correctly excluded from the migration** (§2.1, §2.5).

`k` is the **chassis conversion weight**: how much being shot up rattles this chassis at all.

| Chassis | `k` | `D` at 50% HP | `D` at 0% HP | Rationale |
|---|---|---|---|---|
| Infantry | **1.0** | 50 | 100 | Reproduces the graded ladder (table above). |
| Vehicles | **0.6** | 30 | 60 | Today a vehicle is mechanically undamaged above 50% ([`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §2). `k = 0.6` puts a gentle continuous cost on the top half — which is, **INFERENCE**, what the two dead `light-damage-attained` / `medium-damage-attained` tokens were reaching for. That document's §1 marks the same inference; I concur and note it is unprovable from the tree. |
| Aircraft | ⚠️ **1.0** *(was 0.8)* | 50 | 100 | **Changed by the sweep.** Aircraft have no suppression input at all (§2.3), so `I = D` identically and `k` alone sets the entire curve. Any `k < 1` simply makes the top of the scale unreachable for no benefit — which is the exact "3 of 10 levels" failure §5 exists to prevent, and the first draft walked into it. |

**Keep `k` and the effect ladders as separate knobs.** They answer different questions — *how much does damage rattle this
chassis* versus *what does being rattled cost this chassis* — and a vehicle is genuinely low on the first and high on the
second (armour absorbs the shock; a frozen turret is catastrophic).

### 1.3 How a permanent input and a transient one share one scale

**The naive version fails.** If `D` is granted through `ExternalCondition` like any other stack, the decay loop
(`ExternalCondition.cs:212-225`) revokes it at 1 per `ReduceTicks`, and whatever re-asserts it grants it back at the next
interval. The result is a **sawtooth**: impediment oscillates forever on every damaged unit on the map, re-enabling and
re-disabling a `ConditionalTrait` at every tier boundary it crosses. That is worse than the uncapped-stack trap, because it
looks like it works.

**The fix is one field, and the decay loop's own structure makes it cheap.** `permanentTokens` is a
`Dictionary<object, HashSet<int>>` **keyed by grant source** (`ExternalCondition.cs:64`), and the decay loop already
iterates it per source (`:215-225`). So:

> **Add `ExternalConditionInfo.DecayExemptSources`** (or a `bool exempt` parameter on `GrantCondition`). Exempt sources are
> skipped by the reduce loop at `:215` and counted normally everywhere else — `CanGrantCondition`'s `TotalCap` sum at
> `:103-105` needs no change at all.

That yields the wanted semantics: **`D` is a floor, `S` decays down onto it.** A unit at 40% HP with `k = 1` sits at 60
permanently; warhead hits push it toward 100 and it decays back to 60, not to 0. Repair lowers the floor.

**Cap-ordering hazard, and it is benign.** `CanGrantCondition` refuses a grant once the total reaches `TotalCap`
(`:103-105`) — it does not displace. A unit whose `D` alone reaches 100 cannot receive warhead suppression at all. That is
behaviourally correct but means **`S` and `D` are not symmetric at the ceiling**, and any test asserting "this warhead always
adds a stack" will fail against a nearly-dead unit. Worth one NUnit case pinning it deliberately.

### 1.4 Engine work, priced

| Item | Size | Verified by |
|---|---|---|
| `DecayExemptSources` on `ExternalCondition` — skip exempt sources in the reduce loop (`:215`) | **~15 lines.** The loop is already per-source. | NUnit: grant exempt + non-exempt, tick past `ReduceTicks`, assert only the non-exempt fell. |
| New trait `GrantImpedimentFromHealth` — ticks on `Interval`, computes `D`, calls `ExternalCondition.GrantCondition(self, source, …)` / `TryRevokeCondition` to reach the target count | **~60 lines**, and the pure-math half lifts verbatim from `GrantStackingConditionOnHealthFraction.CalculateStacks` (`:84-97`) — already `static`, already pure, already covered by `GrantStackingConditionOnHealthFractionTest.cs`. | NUnit on the math; that test file is the template. |
| **Do not** modify `GrantStackingConditionOnHealthFraction` itself | — | It ships twice driving the `onfire` overlay (`vehicles.yaml:217-222`, `aircraft.yaml:281-286`), and both uses are safe **precisely because `onfire` has no decay configured**. Routing it through `ExternalCondition` would start decaying the fire ramp. Write a sibling. |

The API already exists and is public: `ExternalCondition.GrantCondition(Actor, object source, int duration = 0,
int remaining = 0)` at `:110`, and `TryRevokeCondition(Actor, object source, int token)` at `:179`.

### 1.5 Reading the value out

`GetConditionCount` on the actor, as the three existing engine readers already do for `suppressed`
(`GarrisonManager.cs:92`, `StancePositioningExecutor.cs:112`, `PoiOffensiveBotModule.cs:468`).

---

## 2. What it modifies, per chassis

### 2.1 ⚠️ CORRECTED — the principle, and the ladder/gate boundary the sweep moved

**Impediment owns the graded axes. Every gate stays on the damage tokens.** The graded axes are speed, vision, burst,
burst-wait, inaccuracy and turret traverse. Gates — stop shooting, take cover, panic, bail out — are untouched, along with
cosmetics (smoke, trails, pips) and separate systems (bleed-out, crew ejection).

**What the sweep added: a multiplier trait can BE a gate.** A `Modifier: 0` is not the bottom rung of a ladder; it is a hard
stop expressed in a multiplier's syntax, and `Util.ApplyPercentageModifiers` annihilates everything else on that axis when
it fires. Three such traits exist, each recorded in-file as a deliberate ruling, and **the first draft wrongly scheduled all
three for replacement**:

| Trait | Condition | What it is | First draft | Corrected |
|---|---|---|---|---|
| `SpeedMultiplier@CriticalDamage: 0` (`infantry.yaml:1114-1116`) | `heavy-damage-attained` | Infantry dead stop below 50% HP. Ruling 2026-08-23, `:1108-1113`; pinned by `PanicGateAtCriticalDamageTest`. | REPLACED | **GATE — untouched** |
| `TurretTurnSpeedMultiplier@CriticalDamage: 0` (`vehicles.yaml:290-292`) | `heavy-damage-attained` | Vehicle turret lock below 50% HP. Rationale at `:273-289`: *"A burning hulk whose crew is climbing out must not still be traversing and shooting."* | REPLACED | **GATE — untouched** |
| `SpeedMultiplier@CriticalDamage: 0` (`vehicles.yaml:270-272`) | `critical-damage` | Vehicle rolls to a stop below 25% HP. `:280-281`: *"a hit vehicle coasting to a halt reads correctly."* | REPLACED | **GATE — untouched** |

Had the first draft shipped, a vehicle at 49% HP would have regained turret traverse (0 → 78) and infantry at 49% HP would
have regained movement (0 → 40), both against recorded rulings, and one of them against an NUnit corpus scan. **This is the
single largest correction in the document.**

Consequence for vehicles specifically: **on vehicles, impediment becomes purely additive.** It adds the graded degradation
vehicles have never had and removes nothing. That makes Stage 4 far less risky than the first draft implied.

### 2.2 The proposed ladders

Ten tiers of 10 points, matching the infantry suppression idiom (`infantry.yaml:434-464`) so the authored shape is one the
codebase and `SuppressionMathTest` already understand.

**Infantry** — identical to today's suppression ladder, per §1.2:

| `impediment` | 1–10 | 11–20 | 21–30 | 31–40 | 41–50 | 51–60 | 61–70 | 71–80 | 81–90 | 91–100 |
|---|---|---|---|---|---|---|---|---|---|---|
| Speed | 90 | 80 | 70 | 60 | 50 | 40 | 30 | 20 | 10 | 0 |
| Vision | 90 | 80 | 70 | 60 | 50 | 40 | 30 | 20 | 10 | 0 |
| Burst | 90 | 80 | 70 | 60 | 50 | 40 | 30 | 20 | 10 | 0 |
| BurstWait | 110 | 120 | 130 | 140 | 150 | 160 | 170 | 180 | 190 | 200 |
| Inaccuracy | 120 | 140 | 160 | 180 | 200 | 220 | 240 | 260 | 280 | 300 |

⚠️ **The speed-0 gate is retained and composes.** Infantry below 50% HP remain at a dead stop: the ladder's speed column ×
gate 0 = 0. The ladder's speed column is therefore only observable **above** 50% HP, where it is driven by suppression and by
`D ≤ 50`.

**Vehicles** — today's five-tier suppression values preserved exactly on tiers 1–5 and extended through tier 10, plus a
speed column vehicles have never had. All three vehicle gates retained:

| `impediment` | 1–10 | 11–20 | 21–30 | 31–40 | 41–50 | 51–60 | 61–70 | 71–80 | 81–90 | 91–100 |
|---|---|---|---|---|---|---|---|---|---|---|
| Turret | 85 | 70 | 55 | 40 | 25 | 20 | 15 | 10 | 5 | 0 |
| Inaccuracy | 115 | 130 | 150 | 175 | 200 | 215 | 230 | 245 | 260 | 275 |
| BurstWait | 105 | 110 | 120 | 130 | 150 | 160 | 170 | 180 | 190 | 200 |
| Speed | 100 | 100 | 95 | 90 | 85 | 75 | 65 | 55 | 40 | 25 |
| Vision | — | — | — | — | — | — | — | — | — | — |

Tiers 1–5 of turret / inaccuracy / burst-wait are **today's shipped vehicle suppression values verbatim**
(`vehicles.yaml:369-415`), so a vehicle at full suppression and full health behaves exactly as it does now. Vehicles keep no
vision ladder, per the in-file ruling at `vehicles.yaml:359-360`. The speed column is new and is the §6.2 decision.

**Aircraft** — ⚠️ **rebuilt to meet what Stage 1 shipped.** With `k = 1.0`, `I = D = 100 − HP%`:

| `impediment` | 1–10 | 11–20 | 21–30 | 31–40 | 41–50 | 51–60 | 61–70 | 71–80 | 81–90 | 91–100 |
|---|---|---|---|---|---|---|---|---|---|---|
| Speed | 98 | 96 | 94 | 92 | **90** | 85 | 81 | 77 | 72 | **67** |
| Inaccuracy | 107 | 113 | 120 | 126 | **133** | 159 | 186 | 213 | 240 | **266** |
| BurstWait | 107 | 113 | 120 | 126 | **133** | 159 | 186 | 213 | 240 | **266** |

The bold values are **anchors, preserved exactly** against the post-Stage-1 tree:

| HP | `D` | Tier | Today (post-Stage-1, effective) | Proposed |
|---|---|---|---|---|
| 50% (Heavy onset) | 50 | 41–50 | speed 90, inacc 133, bw 133 (`aircraft.yaml:400-402`, `:416-418`, `:422-424`) | 90 / 133 / 133 — **identical** |
| 0% | 100 | 91–100 | speed 90 × 75 = **67.5**; inacc 133 × 200 = **266**; bw 133 × 200 = **266** (`:403-405`, `:419-421`, `:425-427`) | 67 / 266 / 266 — **identical** (67 vs 67.5 is integer truncation, §2.6) |

**Answering the Stage 1 implementer's third point directly: §2.2's aircraft ceiling is raised to meet what Stage 1 shipped,
rather than the softening being declared intentional.** The first draft's 250 / 200 ceiling was not a considered choice; it
was an unreachable tier on a `k = 0.8` scale, computed against a pre-Stage-1 endpoint that was itself wrong by the heavy-band
factor. Both faults are fixed.

⚠️ **One honest residual: the interior of the Critical band is milder than today, and that is inherent.** Today inaccuracy
jumps to 266 the instant HP crosses 25%; the ladder reaches 266 only at 0%. At HP 20% (`D = 80`) today gives 266 and the
proposal gives 213. **That is the cliff-to-ramp trade, not accidental relief** — the endpoints match exactly and the ramp is
monotone throughout. If the user wants no interval milder than today, the ladder must be front-loaded to reach 266 by tier
71–80, which makes tiers 81–100 flat. Listed as §6.6.

### 2.3 ⚠️ CORRECTED — ruling 2, and what Stage 1 shipped

The pre-Stage-1 values, and the effective figures the first draft got wrong:

| | `heavy-damage-attained` (<50%) | `critical-damage` (<25%) | **Effective below 25%** |
|---|---|---|---|
| `InaccuracyMultiplier` (was) | 75 | 50 | **37.5% — i.e. 2.67× more accurate**, not 2× |
| `BurstWaitMultiplier` (was) | 75 | 50 | **37.5% — i.e. 2.67× faster**, not 2× |
| `SpeedMultiplier` | 90 | 75 | **67.5%**, not 75% |

**The first draft said "twice as accurate and fires twice as fast". So does
[`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §2.** Both are understated by the heavy-band factor of 1.33. Neither is
wrong about the *direction*, and neither changes the ruling — but anyone sizing a replacement ladder from those sentences
sizes for one band and lands a third off. `hurt-unit-modifiers.md` is corrected in the same commit as this revision.

**Stage 1 has shipped** (`2cda2e3f`, merged `557c38d7`): the four values became the exact reciprocals, 133 and 200, so the
penalty mirrors the bonus it replaced and no fresh balance number was invented. Effective below 25% is now
133% × 200% = **266%**. The speed column was correctly left alone. The stacking is now recorded in-file at
`aircraft.yaml:412-415`.

**Aircraft have no suppression at all.** There is no `ExternalCondition@Suppression` on `^NeutralAirborne`
(`aircraft.yaml:2-17`; the only `suppress`-shaped token is `suppress-eject`, an unrelated ejection gate at `:16-17`, `:252`)
and no suppression ladder anywhere in the file. So on aircraft **`I = D` identically** — which is what forces `k = 1.0`
(§1.2) and what makes aircraft the most independent stage (§7).

### 2.4 The consumer that breaks silently: threshold reads

This is the strongest argument against a plain rename. Seven sites read the raw count against a fixed threshold:

| Site | Threshold | What happens if damage feeds the same number |
|---|---|---|
| `infantry.yaml:1771` | `^AT` Armament `PauseOnCondition: !ammo-primary \|\| suppressed >= 10` | With `k = 1`, `D ≥ 10` at **91% HP**. An ATGM soldier who has taken one scratch stops firing, permanently, with full ammo. |
| `infantry.yaml:1991` | engineer repair `PauseOnCondition: suppressed >= 10` | Engineers stop repairing at 91% HP, forever. |
| `infantry.yaml:317`, `:342` | `ProneCondition: … \|\| suppressed > 30 \|\| …` | Prone from 69% HP down — and into `InfantryStates.IsProne`, which the token path does *not* set (§6.1). |
| `GarrisonManager.cs:100` | `SuppressionRecallThreshold = 60` | Garrisoned infantry below 40% HP are recalled to shelter and **stay** recalled, because `D` never decays. |
| `StancePositioningExecutor.cs:108` | `MaxSuppressionToMove = 30` | Experimental-bot units below 70% HP stop repositioning, permanently. |
| `PoiOffensiveBotModule.cs:464-468` | `PrepSuppressionRadius` | Prep-fire coordination reads damaged units as already suppressed and declines to fire. |

`GarrisonManager.cs:94-99` already carries a PITFALL against exactly this class of mistake. **Under the two-token design all
seven are untouched and keep their present meaning**, at no cost.

### 2.5 ⚠️ CORRECTED — migration table

Live consumer lines at this ref, comment lines excluded: **`heavy-damage-attained` 36** (35 consumers + 1 grant,
`defaults.yaml:278-280`) and **`critical-damage` 24** (23 consumers + 1 grant, `:281-283`). 60 lines total.

**The first draft claimed "of 60 sites, 46 are untouched" and its own class counts summed to 69.** Re-derived line by line:

| Class | Count | Sites | Disposition |
|---|---|---|---|
| Vehicle Armament disarm gates | **23** | `vehicles-america.yaml:88,247,380,408,537,663,819,949,979,1014,1139`; `vehicles-russia.yaml:69,200,234,359,480,628,768,897,906,938,1058`; `vehicles-ukraine.yaml:52` | **UNTOUCHED.** A gate. Pinned in-game by `test-arty-no-fire-at-critical`; `vehicles.yaml:286-289` notes turretless vehicles are covered *only* by this. |
| Cosmetics — smoke, trails | **11** | `vehicles.yaml:193`; `aircraft.yaml:501,510`; `aircraft-america.yaml:283,440,571,678`; `aircraft-russia.yaml:254,458,570,695` | **UNTOUCHED.** Impediment is not a health readout. |
| Damage pips | **3** | `defaults.yaml:215,246`; `infantry.yaml:747` | **UNTOUCHED.** With the health bar off (`SelectionBarsAnnotationRenderable.cs:168-181`) these are the mod's only health indicator. |
| Behaviour gates — fire, panic, wander, prone | **5** | `infantry.yaml:210,332,400,404,1090` | **UNTOUCHED.** Pinned by `CriticalWoundFireGateTest.cs:42` and `PanicGateAtCriticalDamageTest.cs:38`, both of which **read the shipped YAML, not a fixture**. `infantry.yaml:1090` is the prone grant — **gated, §6.1**. |
| `ProneCondition` reads | **2** | `infantry.yaml:317,342` | **UNTOUCHED.** |
| ⚠️ **`Modifier: 0` gates** | **3** | `infantry.yaml:1116`; `vehicles.yaml:272,292` | **UNTOUCHED — reclassified by the sweep, §2.1.** The first draft scheduled all three for replacement. |
| Token grants | **2** | `defaults.yaml:279,282` | **UNTOUCHED.** The gates above still read them. |
| **Infantry graded ladder** | **4** *(of 60)* | `infantry.yaml:1118,1121,1124,1127` | **REPLACED.** ⚠️ Plus **15 traits outside this count** — `:1056-1070` (Light ×5), `:1072-1086` (Medium ×5), `:1092-1106` (Heavy ×5) — which key on `light-damage` / `medium-damage` / `heavy-damage` and so appear in neither grep. **19 infantry traits are replaced in total; only 4 are inside the 60.** The first draft did not state this and undercounted the infantry edit by 15. |
| **Aircraft graded ladder** | **6** | `aircraft.yaml:402,405,408,411,414,417` | **REPLACED** by the §2.2 aircraft ladder. |
| **Vehicle graded ladder** | **0** | — | **NOTHING REPLACED.** All three vehicle multiplier traits are gates (§2.1); the vehicle ladder is purely additive. |
| Dead tokens | 2 grants | `defaults.yaml:266-268`, `:272-274` | **DELETED.** `light-damage-attained` / `medium-damage-attained` have zero consumers repo-wide ([`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §1). Silences ~20 `CheckConditions` warnings. |

**Reconciled: 23 + 11 + 3 + 5 + 2 + 3 + 2 = 50 untouched; 4 + 6 = 10 replaced; 50 + 10 = 60.** Total traits edited across
the whole migration is **25** (19 infantry + 6 aircraft) plus the additive vehicle ladder and the two dead-token deletions.

**`SuppressionMathTest.cs` must be updated with the ladders, not deleted.** It hardcodes the full tier tables (`:29-67`
onward) and its doc-comment states its purpose: *"This catches regressions if someone changes the YAML values
accidentally."* It is the only mechanical guard on these numbers.

### 2.6 ⚠️ NEW — two engine facts that bound every ladder above

Both surfaced by Stage 1 and verified here independently. They do not change the tables but they change what the tables can
be expected to achieve.

1. **`BurstWaitMultiplier` does not scale reload, and does not scale the within-burst gap either.** Despite its `[Desc]`
   reading *"Modifies the reload time"* (`BurstWaitMultiplier.cs:14`), the only consumer applies it to `Weapon.BurstWait`
   — the delay *between* bursts (`Armament.cs:765-768`). `Weapon.ReloadDelay` goes through a separate `reloadModifiers`
   list (`Armament.cs:753`) and `Weapon.BurstDelays` is untouched. **So a `Burst: 1` weapon is barely affected by any
   burst-wait column in §2.2**, and rate-of-fire degradation is uneven across the roster. If a uniform rate-of-fire effect
   is wanted, `ReloadDelayMultiplier` is the unclaimed axis ([`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §3 lists it
   as touched by neither suppression nor damage).
2. **`Util.ApplyPercentageModifiers` truncates** — `return (int)a` after accumulating in `decimal` (`Util.cs:238-246`). A
   10-tick `BurstWait` at 133% is 13 ticks, not 13.3 and not 14. Small base values lose a meaningful fraction, and a
   ten-tier ladder on a small base will have tiers that are numerically distinct but behaviourally identical. **This is a
   direct input to the gauge-resolution question in §5** and argues against a fine-grained readout on any axis with a small
   base value.

---

## 3. The double-application defect

### 3.1 What is actually wrong

`vehicles.yaml:304-306` reads:

```
# Crew-condition-driven vehicle disability — used by vehicles with VehicleCrew trait
# Replaces blanket SpeedMultiplier@CriticalDamage / TurretTurnSpeedMultiplier@CriticalDamage
# Use ^CrewedVehicle2 for Driver+Gunner, ^CrewedVehicle3 for Driver+Gunner+Commander
```

**Line 2 is false.** The crew traits use different `@`-keys (`@NoDriver`, `@NoGunner`, `@CommanderDrives`, `@CommanderGuns`,
`@NoCommander`) from the damage traits (`@HeavyDamage`, `@CriticalDamage`), so MiniYaml keeps **both** sets, and there is no
`-SpeedMultiplier@…` or `-TurretTurnSpeedMultiplier@…` removal anywhere in the repository
([`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §5, whose grep I did not repeat). Both multiply through
`Util.ApplyPercentageModifiers`.

**And they fire on the same HP crossing.** `VehicleCrew.EjectionDamageState = DamageState.Heavy` (`VehicleCrew.cs:55`,
applied `:264`) — crew begin bailing below 50%, the identical line at which `SpeedMultiplier@HeavyDamage` (50,
`vehicles.yaml:181-183`) and `TurretTurnSpeedMultiplier@CriticalDamage` (0, `:290-292`) fire.

⚠️ **Corrected for band precision.** On any of the 13 crewed vehicles **in the 25–50% band** with the gunner gone: turret
traverse is `0% × 0%`, speed is `50% × 40%` (commander driving) or `50% × 0%`. **Below 25%** the vehicle's own
`SpeedMultiplier@CriticalDamage` (0, `:270-272`) joins and speed is zero regardless of crew. The first draft said "below
50%" for both, which spans two bands whose arithmetic differs.

### 3.2 ⚠️ CORRECTED — how crew loss and impediment relate

**Two inputs, one quantity — but crew loss is not one of them.** Crew loss stays a separate system:

1. It is **discrete and irreversible**, not graded. There is no 40%-of-a-gunner.
2. Its effects are **role-shaped, not degradation-shaped**: `InaccuracyMultiplier@CommanderGuns: 200`
   (`vehicles.yaml:341-343`) says a specific man is doing a specific unfamiliar job. That is a different vehicle, not a
   worse one.
3. It already has a coherent model across `^CrewedVehicle2` (`:307-321`) and `^CrewedVehicle3` (`:323-352`).

**What this work owes it is the collision, not the merge.** Worked through, with the §2.1 correction applied:

| Vehicle at 40% HP, gunner dead, commander driving | Today | Under impediment (`k = 0.6`, `D = 36`, tier 31–40) |
|---|---|---|
| Speed | 50% (damage) × 40% (crew) = **20%** | gate absent above 25% · ladder 90% × 40% (crew) = **36%** |
| Turret | 0% (damage gate) × 0% (crew) = **0%** | **0%** — both gates retained, unchanged |

⚠️ The first draft put the ladder term at "~85%" and the product at "34%". Both were wrong: 85 is the 41–50 tier, and `D` at
50% HP is 30 → tier 21–30 → 95. The table above uses 40% HP (`D = 36`) so the comparison is against a band where today's
value is unambiguous. **The double-application does not disappear — it becomes gentler, continuous and legible.** Whether to
go further is §6.3.

### 3.3 The comment fix, ready to apply

Not applied here (this branch edits no rules). Replacement text, to land with Stage 0:

```yaml
# Crew-condition-driven vehicle disability — used by vehicles with VehicleCrew trait.
# Use ^CrewedVehicle2 for Driver+Gunner, ^CrewedVehicle3 for Driver+Gunner+Commander.
#
# These do NOT replace the damage multipliers above — an earlier version of this comment claimed they
# did and was wrong. The crew traits use different @-keys (@NoDriver, @NoGunner, @CommanderDrives,
# @CommanderGuns, @NoCommander) from the damage traits (@HeavyDamage, @CriticalDamage), so MiniYaml
# keeps both sets and Util.ApplyPercentageModifiers multiplies them together — there is no max-wins
# and no dedupe. There is no -SpeedMultiplier@... removal anywhere in the repository.
#
# They also fire on the SAME HP crossing: VehicleCrew.EjectionDamageState is DamageState.Heavy
# (VehicleCrew.cs:55), so crew start bailing below 50% — exactly where SpeedMultiplier@HeavyDamage
# (:181) and TurretTurnSpeedMultiplier@CriticalDamage (:290) fire. A crewed vehicle in the 25-50%
# band that has lost its gunner therefore has turret traverse 0% x 0% and speed 50% x 40%. Below 25%
# SpeedMultiplier@CriticalDamage (:270) joins and speed is zero regardless of crew. Crew loss is a
# SECOND, DELAYED EXPRESSION of the 50% line, not an independent axis. Size any change here for two
# multipliers, not one.
```

---

## 4. Naming

| Candidate | For | Against |
|---|---|---|
| **`impediment`** | The user's own word, and this project's vocabulary rulings are load-bearing because they are the user's. Runs the **same direction as the grant model**: more stacks = more impediment. Zero collisions — no engine C#, no YAML, no test references the string. | Formal; not the word a player would use. |
| **`effectiveness`** (inverted) | Reads better on a gauge — high is good. Matches doctrine vocabulary, which serves the standing project goal of doctrine-grounded realism. | **Mechanically backwards, and decisive.** Every input is a `GrantExternalCondition` warhead — 139 grant sites — and a grant can only *add*. An inverted quantity needs warheads that *revoke* from a pool starting full, which `ExternalCondition` cannot express (`GrantCondition` adds, `:110-172`; there is no "start at N" field). It would also invert all 65 existing ladder comparisons. Not a naming choice — a different and much larger implementation. |
| **`suppressed`** (keep) | Free. Zero rename, zero scenario churn (8 scenario YAML + 13 scenario Lua). | The user has ruled the name wrong, and they are right for a mechanical reason: three engine modules read the count as *"this unit is under fire"* (§2.4). Under a merged quantity that sentence becomes false and the code keeps believing it. |
| `degradation`, `attrition`, `wear` | — | `attrition` and `wear` already mean something else in an RTS. `degradation` is `impediment` with more syllables. |

**Recommendation: `impediment`.** The deciding argument is that it runs the same direction as every grant site in the mod,
and `effectiveness` does not. The gauge-readability case for `effectiveness` is real but belongs to the display, which can
invert for presentation exactly as `DetectabilityGrade.cs:64-71` already inverts concealment into exposure. **A display may
invert; the simulation quantity should not.**

⚠️ **But note what that precedent cost.** [`indicator-audit.md`](indicator-audit.md) §3.4 records the inversion as the
diamond's *"second intuition hazard"* — the grade runs on exposure while the `[Sync]` field runs on concealment, and
`DetectabilityGrade.cs:43-46` carries a warning about precisely this confusion. If the impediment gauge inverts for display,
**write the warning comment at the same time as the inversion, not afterwards.**

**Keep `suppressed` as the warhead-granted input token**, unrenamed (§0.3). Two names for two things that genuinely differ
is clarity, and it means no warhead, no scenario YAML and no scenario Lua file is touched.

---

## 5. Range and distribution — what a mark would have to gauge

**Not a mark design.** This section exists because [`indicator-audit.md`](indicator-audit.md) §3.4 records how the diamond
failed: its underlying value spans only 3 of 10 levels on a vehicle, so on every vehicle the diamond is *"permanently a
solid diamond that shifts only between `F0B232` and `F09425`"*, and the coarse hollow/solid channel never fires there at
all. **That happened because the gauge was designed before anyone wrote down the distribution of what it gauges.**

### 5.1 ⚠️ CORRECTED — reachable range, per chassis

| Chassis | `S` reaches | `D` reaches | `I` reaches | Full scale from one input alone? |
|---|---|---|---|---|
| **Infantry** | 0–100 (`TotalCap: 100`, `infantry.yaml:431`) | 0–100 (`k = 1.0`) | **0–100** | **Yes, from either.** |
| **Vehicles** | 0–50 (`TotalCap: 50`, `vehicles.yaml:364`) | 0–60 (`k = 0.6`) | **0–100** | **No** — 0–60 from damage, 0–50 from fire. The top 40 points need both. |
| **Aircraft** | **0** (no suppression, §2.3) | 0–100 (`k = 1.0`, ⚠️ was 0–80) | **0–100** | Damage only, but **the whole scale is now reachable** — the first draft left the top 20 points dead. |

### 5.2 What that means for anyone drawing this

1. **Infantry span the whole scale from either input.** A ten-step gauge is honest on infantry.
2. **Vehicles reach the top only under combined load.** A vehicle in a firefight at 40% HP sits near 80; idling at 40% HP,
   36; undamaged under heavy fire, 50. All three are common, so a vehicle's distribution is **broad but bottom-weighted**
   and never pins — the opposite of the diamond's vehicle failure, and the point.
3. ⚠️ **Aircraft now span 0–100, but move only with health.** The mark is a monotone one-way ramp that never recovers
   without repair. **A gauge reading "under fire right now" would still be lying on an aircraft** — that finding survives
   the `k` change unaltered; only the range claim moved.
4. ⚠️ **Tier distinctness is not guaranteed at the bottom of any axis with a small base value** (§2.6, truncation). Two
   adjacent tiers can be numerically different and behaviourally identical.

### 5.3 The distribution question this spec cannot answer

How often does a unit actually sit in each decile in play? That is an observation, not a derivation, and **nothing here has
been launched or measured.** §5.1 bounds the range and §5.2 the shape, but the *density* — whether infantry cluster at 0–20
and 80–100 with an empty middle, which is what a 10-tier gauge most wants to know — is unmeasured. A combat-sim run
(`tools/combat-sim/`, per [`DOCS/recipes/BALANCE.md`](../DOCS/recipes/BALANCE.md)) or an instrumented autotest would settle
it, and **should be run before any gauge resolution is chosen.** That is the evidence whose absence caused the original
mistake.

---

## 6. Decisions for the user — NOT taken here

### 6.1 ⚠️ GATED — should a badly damaged unit keep going prone?

**Touches concealment and therefore sits behind the standing user gate** (`PIPELINE.md:580`). **Flagged, not specified.**

What already happens today, per [`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §6 (not re-derived):

- `GrantCondition@HeavyDamageProne` grants `prone` below 50% HP (`infantry.yaml:1088-1090`), on **all** `^Infantry`.
- `prone` adds `DetectableAddativeModifier@Prone: VisionModifier: 1` (`:789-791`) — **+1 required observer strength**.
- `prone` also selects `HitShape@Cover` radius 20 over `HitShape@Standing` radius 30 (`:144-151`) — **harder to hit**.
- So a wounded man is currently *stealthier and smaller*: damage makes him harder to kill.
- And the split that document calls its sharpest finding: in the 25–50% band a *moving, unsuppressed* wounded soldier holds
  the `prone` **token** (concealment + small hitbox) while `InfantryStates.IsProne` is **false**, so he gets no prone damage
  reduction, no prone speed modifier, and **renders standing up** (`InfantryStates.cs:182-222`). No test covers it.

**The decision:** whether impediment should feed prone at all, and whether the damage→prone grant should survive. Three
shapes exist — leave it; move it onto an impediment threshold; remove the damage-driven grant and let suppression own prone
— **all inside the gate, none specified here.** If the gate opens, settle the token/flag split in the same conversation.

### 6.2 ⚠️ Should vehicles take a continuous speed penalty at all?

Narrower than the first draft's version of this question, because §2.1 now retains all three vehicle hard stops. The
remaining choice is the new speed column in §2.2:

- **As proposed:** a gentle ramp (100 → 95 → 90 → 85 in the top half) *on top of* the retained gates. A vehicle at 40% HP
  goes from 50% speed today to 90% × (nothing else) = 90%, because `SpeedMultiplier@HeavyDamage: 50` would be folded in.
- **Conservative:** retain `SpeedMultiplier@HeavyDamage: 50` (`vehicles.yaml:181-183`) as a fourth gate too. Then 40% HP
  gives 50% × 90% = 45%, marginally worse than today, and the ladder only ever adds.

**I recommend the conservative variant**, and note it makes Stage 4 almost risk-free. The aggressive variant is a real buff
to damaged vehicles across the whole 25–50% band and should be taken deliberately if at all.

### 6.3 Should crew-loss multipliers compose with impediment, or replace it?

§3.2 rules them a separate system that **composes** — today's behaviour, gentler. The alternative, crew loss *replacing*
the impediment speed/turret terms on the 13 crewed vehicles, is what the wrong comment claimed was already happening. It
would make "the gunner is dead" a clean statement rather than a second multiplier, but costs `-SpeedMultiplier@…` removal
lines that have **no precedent anywhere in the repository** (§3.1) — a new idiom, not a tweak.

### 6.4 ⚠️ Are the `k` values right?

Reassessed after the sweep:

- **Infantry `k = 1.0` is derived** (§1.2) and I am confident in it.
- **Aircraft `k = 1.0` is now derived too**, from reachability: with no second input, any `k < 1` strands the top of the
  scale. It was `0.8` by interpolation in the first draft and that was the weaker reasoning.
- ⚠️ **Vehicle `k = 0.6` is now the weakest number in this document.** It is a judgement about how much armour absorbs, with
  nothing behind it, and it sets where the whole vehicle distribution sits (§5.2). Worth a combat-sim pass rather than a
  ruling.

### 6.5 Should the gates eventually move onto impediment thresholds?

This spec leaves all gates on the damage tokens (§2.1, §2.5) on the §0.3 argument. That is deliberate and conservative. A
later pass could move some — but each is pinned by an NUnit corpus scan reading the shipped YAML
(`CriticalWoundFireGateTest.cs:42`, `PanicGateAtCriticalDamageTest.cs:38`), so each move is a test edit too, and the tests'
doc-comments record user rulings about *why* those gates sit at 50%. Not now.

### 6.6 ⚠️ NEW — should the aircraft ladder be front-loaded so no interval is milder than today?

§2.2 preserves both aircraft anchors exactly but is milder than today between 0% and 25% HP, because a ramp is replacing a
cliff (266 at 20% HP today, 213 proposed). Options: accept the ramp as the intended trade; or front-load so 266 is reached
by tier 71–80, flattening tiers 81–100. **This is the decision the Stage 1 implementer asked to be taken deliberately, and
§2.2 takes the first half of it** — the ceiling is raised to meet what shipped. The interior remains open.

---

## 7. Staging plan

Each stage is independently verifiable and leaves `main` working. **No stage depends on a later one.**

| # | Stage | Content | Verified by | Risk |
|---|---|---|---|---|
| ~~1~~ | ~~**Aircraft sign fix**~~ | ✅ **SHIPPED** `2cda2e3f`, merged `557c38d7`, 2026-09-12. Four values to their exact reciprocals; stacking recorded in-file. | Done. | — |
| **0** | **Clear the ground** | Fix the `vehicles.yaml:304-306` comment (§3.3). Delete the two dead token grants (`defaults.yaml:266-268`, `:272-274`). | `.\make.ps1 test` — ~20 `CheckConditions` warnings disappear; `LINT_BASELINE_PRUNE` if any were baselined. **Zero behaviour change.** | None. |
| **2** | **Engine: the two pieces** | `DecayExemptSources` on `ExternalCondition`; new `GrantImpedimentFromHealth`. **Ships inert** — no YAML references either. | NUnit only (§1.4). Nothing in the game changes. | Low. Reviewable in isolation. |
| **3** | **Infantry onto impediment** | Declare `impediment` on `^Infantry`; retarget the 50 suppression ladder traits; replace **19** damage-ladder traits; add `GrantImpedimentFromHealth` with `k = 1`. `suppressed`, all seven threshold reads and the speed-0 gate untouched. | `SuppressionMathTest` updated with the new tables. `CriticalWoundFireGateTest` / `PanicGateAtCriticalDamageTest` must stay **green without edits** — that is the proof the gates were not disturbed. | **Highest.** Where the balance actually moves. |
| **4** | **Vehicles onto impediment** | Declare `impediment` on `^Vehicle`, `k = 0.6`, add the §2.2 ladder. **Purely additive** — all three gates and the 23 disarm gates retained (§2.1). Plus the §6.2 decision. | `dotnet test`; combat-sim for `k` (§6.4). | ⚠️ **Much lower than the first draft implied**, because nothing is replaced. |
| **5** | **Aircraft onto impediment** | Replace the six sign-fixed band traits with the continuous §2.2 ladder, `k = 1.0`. Plus the §6.6 decision. | `dotnet test`; the two anchors in §2.2 are the assertions. | Low. |

### Which stage can be cut

**Stage 5, and the case is now stronger.** Stage 1 has landed correct endpoints in the right direction; because aircraft
have no suppression (§2.3), impediment on an aircraft is *only* a smoother rendering of that same ladder. Cutting it leaves
aircraft on two bands that are already right at both ends, and the §5.2 finding that aircraft impediment can never carry
transient pressure argues the extra machinery buys little there. **The cost of cutting is the cliff at 25% HP, nothing
else.**

**Stages 0, 2, 3 are not cuttable.** 0 removes a comment that would actively mislead the implementer of 3; 2 is the whole
mechanism; 3 is the feature. **Stage 4 is now nearly cuttable too** — being purely additive, it can be deferred without
leaving anything inconsistent — but it is where most of the user-visible improvement for vehicles lives.

---

## 8. What this spec does not settle

1. **No measurement of any kind.** Nothing launched, nothing linted, no combat-sim run. Every number in §2.2 is reasoned
   from shipped values, and the §5.3 density question — the one that caused the diamond's failure — is open.
2. **Vehicle `k = 0.6`** (§6.4), now the weakest number here.
3. **Whether the vehicle speed column (§6.2) is an improvement**, and whether the conservative variant is preferred.
4. **The aircraft interior softening (§6.6).**
5. **The prone inversion (§6.1)** is gated and deliberately unspecified.
6. **How unevenly §2.6's burst-wait finding lands across the roster.** I established that `BurstWaitMultiplier` reaches only
   `Weapon.BurstWait`; I did **not** enumerate which weapons have `Burst: 1` and are therefore nearly immune to the
   burst-wait column on all three chassis. That enumeration is a prerequisite for trusting the burst-wait columns.
7. **`WithSpottedDecoration` and the diamond are untouched by this work.** §5 is input to that conversation, not its start.

---

## 9. ⚠️ The sweep for the band-product class — what I checked and what I found

The brief asked whether any *other* figure was computed from a single band where the product applies. **I swept every
statement in the document that asserts a current effective value.** Method: enumerate the places where two traits on the
same axis can be simultaneously live, then check every sentence that quotes a number from those axes.

**There are exactly three products in the corpus**, because `heavy-damage-attained` is the only latching token that shares
an axis with an exact-band token:

| Axis | Traits | Effective below 25% | Stated correctly first time? |
|---|---|---|---|
| Aircraft speed | `heavy-damage-attained` 90 × `critical-damage` 75 | 67.5% | ❌ error 1 |
| Aircraft inaccuracy / burst-wait | `heavy-damage-attained` × `critical-damage` | 37.5% then; 266% now | ❌ error 2 |
| Infantry speed | `heavy-damage` 25 × `heavy-damage-attained` 0 | 0 | ❌ error 3 — **found by the sweep** |
| Vehicle speed | `heavy-damage-attained` 50 × `critical-damage` 0 | 0 | ✅ — correct only because the 0 annihilates |

**Where the class does NOT apply, checked and clear:**

- **Infantry vision, burst, burst-wait, inaccuracy.** All four use the *exclusive* `light-damage` / `medium-damage` /
  `heavy-damage` / `critical-damage` bands (`infantry.yaml:1059-1127`), so exactly one trait per axis is live at any HP.
  §1.2's Critical row (vision 10, burst 10) and §2.2's infantry table are single-band and correct.
- **Vehicle turret.** One trait only (`vehicles.yaml:290-292`).
- **Suppression ladders, both chassis.** Every tier is `suppressed > N && suppressed <= N+10` — mutually exclusive by
  construction (`infantry.yaml:434-464`, `vehicles.yaml:369-415`). No products. `SuppressionMathTest`'s single-valued
  lookup functions are correct.
- **§2.4's seven threshold reads.** Single conditions; no arithmetic.
- **§5.1's range table.** Bounds on a scale, not products.

**Two further errors of a different class, found by the same sweep:**

- **Error 4 (§3.2):** "~85% … = 34%" was a misread of my own vehicle table — `D` at 50% HP is 30, which is tier 21–30 (95),
  not tier 41–50 (85). Not a band product; ordinary carelessness. Corrected, and the worked example moved to 40% HP where
  today's value is unambiguous.
- **Error 5 (§2.5):** the migration table's class counts summed to 69 against a stated total of 60, and the headline "46
  untouched" matched neither. Re-derived line by line to **50 untouched / 10 replaced**, and the infantry edit was found to
  be undercounted by 15 traits that appear in neither grep because they key on the exclusive band tokens.

**And one design conclusion changed**, which is the reason a sweep beats a patch: chasing error 3 revealed that three
`Modifier: 0` traits had been misclassified as ladder rungs (§2.1). Fixing that repairs the §1.2 derivation, removes two
unflagged behaviour changes, and makes Stage 4 purely additive.

**What I did not sweep:** the two companion research documents, beyond the §2 aircraft claim in
`hurt-unit-modifiers.md` that the brief named (corrected in the same commit). `indicator-mechanics.md` and
`indicator-audit.md` contain many derived numbers about *rendering*, not about damage multipliers, and the class does not
apply to them — but I did not verify that claim exhaustively.
