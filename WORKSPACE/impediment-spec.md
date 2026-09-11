# Impediment — design spec

**Ref: `main @ 71fefe8c`**, worktree `wt/impediment-spec`, level with `origin/main`.
**Design only.** No behaviour changed, no YAML rules edited, no engine C# edited, nothing built, launched or linted.
The one exception the brief asks for — the wrong comment at `vehicles.yaml:304-306` — is **specified here, not applied**; §3.3
gives the replacement text for the implementer.

Every claim about current behaviour carries a `file:line`. Inference is labelled **INFERENCE**. Where
[`hurt-unit-modifiers.md`](hurt-unit-modifiers.md), [`indicator-mechanics.md`](indicator-mechanics.md) or
[`indicator-audit.md`](indicator-audit.md) already settle something it is cited, not re-derived — except where I checked and
found something to add, which is flagged. **One of them is contradicted on a point of fact: see §0.2.**

---

## 0. Summary, and two things found while writing this

### 0.1 The shape of the recommendation

| | Recommendation |
|---|---|
| **Name** | **`impediment`** — but as a **second, derived token**, not a rename of `suppressed`. §4. |
| **Range** | **0–100 on every chassis.** Identical scale everywhere; chassis differences live in the two conversion curves, never in the range. §1, §5. |
| **Inputs** | `I = clamp(0, 100, S + D)`. `S` = suppression stacks from warheads (decays, as today). `D = round(k × (100 − HP%))`, a permanent floor, decay-exempt. §1. |
| **Why not a rename** | Seven live consumers read the raw count as a *threshold meaning "under fire"* — three in engine C#, four in YAML. Folding damage into that number changes bot and unit behaviour silently. §2.4 quantifies it: an ATGM soldier would stop firing at 91% HP. |
| **Engine cost** | One new `Info` field on `ExternalCondition` (decay-exempt source) + one small trait. Both priced in §1.4. |
| **Migration** | 60 live sites, of which **~46 are left completely alone.** §2.5. |

### 0.2 ⚠️ `hurt-unit-modifiers.md` §7 is wrong about where the affordance is, and it matters

That document offers `GrantStackingConditionOnHealthFraction` as *"the one piece of 'damage → graded scalar' machinery that
already exists"* and says it needs **"no new C#"** to point at a different condition. It then correctly identifies the trap
(`ReleaseTo` bypasses `ExternalCondition`). **The trap is not a caveat on that recommendation — it invalidates it.** I
verified both halves:

- `ReleaseTo` calls `self.GrantCondition(Info.Condition)` directly —
  `engine/OpenRA.Mods.Common/Traits/GrantStackingConditionOnHealthFraction.cs:103`.
- `ExternalCondition`'s cap is enforced only in `CanGrantCondition`, which counts `permanentTokens`
  (`ExternalCondition.cs:103-105`), and its decay loop iterates only `permanentTokens`
  (`ExternalCondition.cs:212-225`) — a dictionary written only by `ExternalCondition.GrantCondition`
  (`:110-172`, the `permanentTokens.Add` at `:170`).

So a health-driven grant lands in **neither** structure. Pointing this trait at the impediment token is not a cheap option
with a known flaw; it is a non-option, because the resulting stack is uncapped *and* undecaying *and* invisible to the cap
arithmetic that the warhead grants rely on. §1.4 prices the actual fix.

Second correction to the same section: it states there is *"no mechanism for 'two inputs, one scalar' short of new C#"*, and
offers the commented-out `ConditionModifier@Rank_1` block (`defaults.yaml:296-299`) as *"the shape a health→suppression link
would take"* — a reading [`indicator-mechanics.md`](indicator-mechanics.md) Q1 repeats, flagging as a gap that it never
checked whether `ConditionModifier` is a live trait.

**It is not. `ConditionModifier` does not exist in the engine.** `grep -rn "ConditionModifier" engine --include=*.cs`,
excluding the unrelated `DetectableAddativeModifier`, returns **nothing** (exit 1). That block cannot be uncommented; it is
not a pattern anyone can follow. This closes `indicator-mechanics.md` honest-gap #4 with a negative answer, and it removes
the last apparent zero-C# route. **Every version of this feature requires engine work.** That is not a reason to avoid it —
the work is small — but it must be in the estimate from the start.

### 0.3 The structural idea the rest of the spec rests on

Suppression and damage are **not** two spellings of one thing. They are two *inputs*, and they are read by two *different
kinds of consumer*:

- **Ladder consumers** ask *"how degraded is this unit?"* — speed, vision, burst, burst-wait, accuracy, turret traverse.
  These want the **sum**. This is the user's model and it is correct for them.
- **Threshold consumers** ask *"is this unit under fire right now?"* — take cover, recall to shelter, hold the ATGM,
  stop moving. These want **suppression alone**, because taking cover is a response to incoming fire, not to being hurt.
  A man at 40% HP standing in an empty field is not under fire and must not behave as if he were.

Collapsing both into one number serves the first group and corrupts the second. The spec therefore keeps `suppressed`
exactly as it is — same warheads, same cap, same decay — and adds `impediment` as a derived total that only the ladders
read. **No warhead is edited. No grant site moves.** That is also, incidentally, cheaper than the rename
`hurt-unit-modifiers.md` §7 costs out at 139 grant sites plus 8 scenario YAML and 13 scenario Lua files (counts re-verified:
139 `Condition: suppressed` lines under `mods/ww3mod`; 8 and 13 scenario files respectively).

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

### 1.2 Why `D = 100 − HP%` and not something invented

This curve is **derived from the shipped infantry ladder, not chosen**. Take the four infantry damage bands, read off the
speed each currently produces, and ask what suppression level produces the same speed:

| Band | HP | Speed today | `D = 100−HP` at band midpoint | Speed the suppression ladder gives at that `D` |
|---|---|---|---|---|
| Light | 75–100% | 75 (`infantry.yaml:1056-1058`) | 12 | 80 (tier 2, `:438-440`) |
| Medium | 50–75% | 50 (`:1072-1074`) | 37 | 60 (tier 4, `:444-446`) |
| Heavy | 25–50% | 25 (`:1092-1094`) | 62 | 30 (tier 7, `:453-455`) |
| Critical | <25% | 10 vision/burst (`:1117-1122`) | 87 | 10 (tier 9, `:459-461`) |

**The identity map reproduces the existing infantry damage ladder to within one suppression tier at every band**, in the
same direction, monotonically. That is a strong result: it means the two ladders the mod already ships were tuned, by
different hands at different times, to almost exactly the same curve — which is the best available evidence that a single
scale is the right model rather than a tidy-looking imposition on it.

`k` is the **chassis conversion weight**: how much being shot up rattles this chassis at all.

| Chassis | `k` | `D` at 50% HP | `D` at 0% HP | Rationale |
|---|---|---|---|---|
| Infantry | **1.0** | 50 | 100 | Reproduces the shipped ladder (table above). |
| Vehicles | **0.6** | 30 | 60 | Today a vehicle is mechanically undamaged above 50% and then falls off a cliff ([`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §2). `k = 0.6` puts a gentle, continuous cost on the top half — which is, **INFERENCE**, what the two dead `light-damage-attained` / `medium-damage-attained` tokens were reaching for (that document's §1 marks the same inference; I concur and note it is still unprovable from the tree). |
| Aircraft | **0.8** | 40 | 80 | Between the two. An airframe has no crew to button up but also no cover to take. Weakest of the three numbers; see §6.4. |

**Keep `k` and the effect ladders as separate knobs, and do not merge them.** They answer different questions — *how much
does damage rattle this chassis* versus *what does being rattled cost this chassis* — and a vehicle is genuinely low on the
first and high on the second (armour absorbs the shock; a frozen turret is catastrophic). One knob doing both jobs is how a
system becomes unreasonable about.

### 1.3 How a permanent input and a transient one share one scale

This is the hard part of the brief and it has a specific answer.

**The naive version fails.** If `D` is granted through `ExternalCondition` like any other stack, the decay loop
(`ExternalCondition.cs:212-225`) revokes it at 1 per `ReduceTicks`, and whatever re-asserts it grants it back at the next
interval. The result is a **sawtooth**: impediment oscillates between the health floor and some value below it, forever, on
every damaged unit on the map. Every ladder tier boundary it crosses re-enables and re-disables a `ConditionalTrait`. That
is worse than the uncapped-stack trap, because it looks like it works.

**The fix is one field, and it is the decay loop's own structure that makes it cheap.** `permanentTokens` is a
`Dictionary<object, HashSet<int>>` **keyed by grant source** (`ExternalCondition.cs:64`), and the decay loop already
iterates it per source (`:215-225`). So:

> **Add `ExternalConditionInfo.DecayExemptSources` (or a `bool exempt` parameter on `GrantCondition`).** Sources registered
> as exempt are skipped by the reduce loop at `:215` and counted normally everywhere else — `CanGrantCondition`'s
> `TotalCap` sum at `:103-105` needs no change at all.

That yields exactly the semantics wanted: **`D` is a floor, `S` decays down onto it.** A unit at 40% HP with `k = 1` sits at
60 permanently; warhead hits push it toward 100 and it decays back to 60, not to 0. Repair raises the floor by lowering `D`.
No sawtooth, no uncapped stack, one cap, one scale.

**Cap-ordering hazard, and it is benign.** `CanGrantCondition` refuses a grant once the total reaches `TotalCap`
(`:103-105`) — it does not displace. So a unit whose `D` alone reaches 100 cannot receive warhead suppression at all. That
is behaviourally correct (it is already maximally impeded) but it means **`S` and `D` are not symmetric at the ceiling**,
and any test asserting "this warhead always adds a stack" will fail against a nearly-dead unit. Worth one NUnit case
pinning it deliberately rather than discovering it.

### 1.4 Engine work, priced

| Item | Size | Verified by |
|---|---|---|
| `DecayExemptSources` on `ExternalCondition` — skip exempt sources in the reduce loop (`:215`) | **~15 lines.** The loop is already per-source. | NUnit: grant exempt + non-exempt, tick past `ReduceTicks`, assert only the non-exempt one fell. |
| New trait `GrantImpedimentFromHealth` — ticks on `Interval`, computes `D`, and calls `ExternalCondition.GrantCondition(self, source, …)` / `TryRevokeCondition` to reach the target count | **~60 lines**, and the pure-math half is liftable verbatim from `GrantStackingConditionOnHealthFraction.CalculateStacks` (`:84-97`), which is already `static`, already pure, and already has a test file (`engine/OpenRA.Test/OpenRA.Mods.Common/GrantStackingConditionOnHealthFractionTest.cs`). | NUnit on the math; the existing test file is the template. |
| **Do not** modify `GrantStackingConditionOnHealthFraction` itself | — | It ships twice driving the `onfire` overlay (`vehicles.yaml:217-222`, `aircraft.yaml:281-286`), and both uses are **safe precisely because `onfire` has no decay configured**. Routing it through `ExternalCondition` would start decaying the fire ramp. Leave it; write a sibling. |

The public API the new trait calls already exists and is public: `ExternalCondition.GrantCondition(Actor, object source,
int duration = 0, int remaining = 0)` at `:110`, and `TryRevokeCondition(Actor, object source, int token)` at `:179`.

### 1.5 Reading the value out

`GetConditionCount` on the actor, as the three existing engine readers already do for `suppressed`
(`GarrisonManager.cs:92`, `StancePositioningExecutor.cs:112`, `PoiOffensiveBotModule.cs:468`). Nothing new is needed for a
gauge to read `impediment` — but see §5 on what it should expect to see.

---

## 2. What it modifies, per chassis

### 2.1 The principle

**Impediment owns the five degradation axes. Everything else stays where it is.** The axes:
speed, vision, burst, burst-wait, inaccuracy, turret traverse. Everything that is a *gate* (stop shooting, take cover,
panic, bail out), a *cosmetic* (smoke, trails, pips) or a *different system* (bleed-out, crew ejection) is untouched by
this work and keeps reading the damage-state tokens it reads today.

That division is what keeps the migration small, and it follows from §0.3: gates are threshold consumers.

### 2.2 The proposed ladders

Ten tiers of 10 points, matching the infantry suppression idiom (`infantry.yaml:434-464` etc.) so the authored shape is one
the codebase and `SuppressionMathTest` already understand.

**Infantry** — identical to today's suppression ladder, because §1.2 showed the damage ladder already agrees with it:

| `impediment` | 1–10 | 11–20 | 21–30 | 31–40 | 41–50 | 51–60 | 61–70 | 71–80 | 81–90 | 91–100 |
|---|---|---|---|---|---|---|---|---|---|---|
| Speed | 90 | 80 | 70 | 60 | 50 | 40 | 30 | 20 | 10 | 0 |
| Vision | 90 | 80 | 70 | 60 | 50 | 40 | 30 | 20 | 10 | 0 |
| Burst | 90 | 80 | 70 | 60 | 50 | 40 | 30 | 20 | 10 | 0 |
| BurstWait | 110 | 120 | 130 | 140 | 150 | 160 | 170 | 180 | 190 | 200 |
| Inaccuracy | 120 | 140 | 160 | 180 | 200 | 220 | 240 | 260 | 280 | 300 |

**Vehicles** — today's five-tier vehicle suppression values, re-spread over ten tiers, plus a speed column it has never
had. Turret traverse keeps its steep curve and reaches 0 only at the top, replacing the current hard lock at 50% HP:

| `impediment` | 1–10 | 11–20 | 21–30 | 31–40 | 41–50 | 51–60 | 61–70 | 71–80 | 81–90 | 91–100 |
|---|---|---|---|---|---|---|---|---|---|---|
| Turret | 92 | 85 | 78 | 70 | 62 | 55 | 45 | 35 | 20 | 0 |
| Inaccuracy | 108 | 115 | 122 | 130 | 140 | 150 | 165 | 180 | 190 | 200 |
| BurstWait | 102 | 105 | 110 | 115 | 120 | 130 | 140 | 150 | 165 | 180 |
| Speed | 100 | 100 | 95 | 90 | 85 | 75 | 65 | 55 | 40 | 25 |
| Vision | — | — | — | — | — | — | — | — | — | — |

Vehicles keep no vision ladder, per the in-file ruling at `vehicles.yaml:359-360` (*"No speed reduction (armored vehicles
keep moving), milder effects than infantry"*). ⚠️ Note the proposal **does** give vehicles a speed column, which that comment
argues against for *suppression*. The justification is that impediment is not suppression: a vehicle at 20% HP should slow,
and it already does today (50 at `vehicles.yaml:181-183`, 0 at `:270-272`). The table above is materially **gentler** than
today at the bottom (25 vs 0) and materially **harsher** in the top half (95–85 where today there is nothing). That is a
real balance change and it is listed as a user decision in §6.2.

**Aircraft** — the narrow ladder, and the direction reversed (ruling 2). No vision, no burst, no turret:

| `impediment` | 1–10 | 11–20 | 21–30 | 31–40 | 41–50 | 51–60 | 61–70 | 71–80 | 81–90 | 91–100 |
|---|---|---|---|---|---|---|---|---|---|---|
| Speed | 98 | 96 | 94 | 92 | 90 | 87 | 84 | 81 | 78 | 75 |
| Inaccuracy | 110 | 120 | 130 | 140 | 155 | 170 | 185 | 200 | 225 | 250 |
| BurstWait | 105 | 110 | 115 | 120 | 130 | 140 | 150 | 165 | 180 | 200 |

The speed column preserves today's endpoints exactly — 90 at `heavy-damage-attained`, 75 at `critical-damage`
(`aircraft.yaml:400-405`) — spread continuously. The other two columns are the **sign fix**.

### 2.3 Ruling 2, stated precisely: the aircraft inversion

Confirmed by direct read of `^WhenDamagedAir` (`aircraft.yaml:399-417`):

| | `heavy-damage-attained` (<50%) | `critical-damage` (<25%) |
|---|---|---|
| `InaccuracyMultiplier` | **75** (`:406-408`) | **50** (`:409-411`) |
| `BurstWaitMultiplier` | **75** (`:412-414`) | **50** (`:415-417`) |

Lower inaccuracy is *more* accurate; lower burst-wait is a *shorter* gap between bursts. So an aircraft below 25% HP today
is **twice as accurate and fires twice as fast** as an undamaged one. Every other family moves both in the punishing
direction (infantry 150 → 400, `infantry.yaml:1068`, `:1126`). [`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §8
declines to call this a bug on its own authority, correctly — it read the numbers and found no comment stating intent, and
I found none either. **The user has now ruled it a bug**, which settles it; this spec treats it as one.

**Aircraft have no suppression at all.** There is no `ExternalCondition@Suppression` on `^NeutralAirborne`
(`aircraft.yaml:2-17` — the only `suppress`-shaped token there is `suppress-eject`, an unrelated ejection gate at `:16-17`,
`:154-174`, `:252`), and no suppression ladder anywhere in the file. So on aircraft **`I = D` identically**, and the aircraft
ladder is a pure rewrite of the damage ladder with no merge to do. This makes aircraft the **cheapest and most independent
stage** — see §7.

### 2.4 The consumer that breaks silently: threshold reads

This is the strongest argument against a plain rename, and it is why §0.3 keeps two tokens. Seven sites read the raw count
against a fixed threshold:

| Site | Threshold | What happens if damage feeds the same number |
|---|---|---|
| `infantry.yaml:1771` | `^AT` Armament `PauseOnCondition: !ammo-primary \|\| suppressed >= 10` | With `k = 1`, `D ≥ 10` at **91% HP**. An ATGM soldier who has taken one scratch stops firing, permanently, with full ammo. |
| `infantry.yaml:1991` | engineer repair `PauseOnCondition: suppressed >= 10` | Same: engineers stop repairing at 91% HP, forever. |
| `infantry.yaml:317`, `:342` | `ProneCondition: … \|\| suppressed > 30 \|\| …` | Prone from 69% HP down. Partly redundant — `heavy-damage-attained` already grants `prone` at 50% (`:1088-1090`) — but it moves the line 19 points and into `InfantryStates.IsProne`, which the token path does *not* set (§6.1). |
| `GarrisonManager.cs:100` | `SuppressionRecallThreshold = 60` | Garrisoned infantry below 40% HP are recalled to shelter and **stay** recalled, because `D` never decays. |
| `StancePositioningExecutor.cs:108` | `MaxSuppressionToMove = 30` | Experimental-bot units below 70% HP stop repositioning, permanently. |
| `PoiOffensiveBotModule.cs:464-468` | `PrepSuppressionRadius` | Prep-fire coordination reads damaged units as already suppressed and declines to fire. |

`GarrisonManager.cs:94-99` already carries a PITFALL against exactly this class of mistake — adding a second suppression
penalty at the garrison because the soldier's own ladder already applies. The same hazard, one level up.

**Under the two-token design all seven are untouched and keep their present meaning.** That is the single largest reason to
prefer it, and it costs nothing: the warheads already grant `suppressed` and keep doing so.

### 2.5 Migration table

Live consumer lines, counted at this ref by grep with comment lines excluded: **`heavy-damage-attained` 36** (35 consumers
+ 1 grant at `defaults.yaml:278-280`) and **`critical-damage` 24** (23 consumers + 1 grant at `:281-283`). The brief's 37
and 22 are within one of these; the difference is comment-line classification, not substance.

The headline: **of 60 sites, 46 are untouched.**

| Class | Count | Sites | Disposition |
|---|---|---|---|
| **Vehicle Armament disarm gates** | **23** | `vehicles-america.yaml:88,247,380,408,537,663,819,949,979,1014,1139`; `vehicles-russia.yaml:69,200,234,359,480,628,768,897,906,938,1058`; `vehicles-ukraine.yaml:52` | **UNTOUCHED.** A threshold gate, not a ladder. Pinned in-game by `test-arty-no-fire-at-critical` and argued in-file at `vehicles.yaml:286-289` (turretless vehicles are covered *only* by this). |
| **Cosmetics — smoke, trails** | **10** | `vehicles.yaml:193`; `aircraft.yaml:501,510`; `aircraft-america.yaml:283,440,571,678`; `aircraft-russia.yaml:254,458,570,695` | **UNTOUCHED.** Damage-state-driven art. Impediment is not a health readout and must not drive these. |
| **Damage pips** | **3** | `defaults.yaml:215,246`; `infantry.yaml:747` | **UNTOUCHED.** With the health bar off (`SelectionBarsAnnotationRenderable.cs:168-181`) these are the mod's only health indicator ([`indicator-audit.md`](indicator-audit.md) §0). |
| **Behaviour gates — fire, panic, wander, prone, bail** | **6** | `infantry.yaml:210,332,400,404,1088-1090`; `vehicles.yaml` crew ejection via `VehicleCrew.cs:55` | **UNTOUCHED.** All are threshold consumers per §0.3, and three are pinned by NUnit corpus scans — `CriticalWoundFireGateTest.cs:42` (`const string Condition = "heavy-damage-attained"`) and `PanicGateAtCriticalDamageTest.cs:38` (`const string Gate = "!heavy-damage-attained"`), both of which **read the shipped YAML, not a fixture**, so a careless fold fails them loudly. That is the good case. `infantry.yaml:1088-1090` is the prone grant — **gated, see §6.1**. |
| **Vehicle prone/ProneCondition reads** | **2** | `infantry.yaml:317,342` | **UNTOUCHED** (they read `critical-damage`, a gate). |
| **Infantry modifier ladder** | **14** | `infantry.yaml:1056-1070` (Light ×5), `:1072-1086` (Medium ×5), `:1092-1106` (Heavy ×5), `:1114-1128` (Critical ×5) — 20 traits across 4 bands | **REPLACED** by the §2.2 infantry ladder. ⚠️ `SpeedMultiplier@CriticalDamage` (`:1114-1116`) must keep its trait key: `PanicGateAtCriticalDamageTest` looks it up by that exact `@`-key, and the in-file comment at `:1108-1111` says a rename *"would silently empty the scan instead of failing it"*. If the trait is deleted rather than renamed, delete the test's expectation in the same commit. |
| **Vehicle modifier ladder** | **3** | `vehicles.yaml:181-183` (speed 50), `:270-272` (speed 0), `:290-292` (turret 0) | **REPLACED** by the §2.2 vehicle ladder. The turret line is where the in-file rationale at `:273-289` lives; that comment must be rewritten with the change, not left arguing for a hard lock that no longer exists. |
| **Aircraft modifier ladder** | **6** | `aircraft.yaml:400-417` | **REPLACED** by the §2.2 aircraft ladder, with the sign fixed. |
| **Dead tokens** | **2** grants | `defaults.yaml:266-268`, `:272-274` | **DELETED.** `light-damage-attained` / `medium-damage-attained` have zero consumers repo-wide ([`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §1, which establishes this thoroughly and whose finding I did not re-derive). Their purpose — a vehicle light/medium ladder — is exactly what the continuous curve now provides. Deleting them silences ~20 `CheckConditions` warnings. |
| **Suppression ladders** | 50 infantry + 15 vehicle traits | `infantry.yaml:434-587`; `vehicles.yaml:369-415` | **RETARGETED** from `suppressed` to `impediment` — the condition name changes, the values do not (they become the §2.2 tables). The `suppressed` *declaration* and decay stay (`infantry.yaml:429-433`, `vehicles.yaml:362-366`) because the threshold reads in §2.4 still need them. |

**`SuppressionMathTest.cs` must be updated with the ladders, not deleted.** It hardcodes the full tier tables as expected
values (`:29-67` onward) and its own doc-comment states its purpose: *"Suppression is implemented via YAML conditions (not
C# logic), but the tier boundaries, modifier progressions, and decay math can be validated. This catches regressions if
someone changes the YAML values accidentally."* It is the only mechanical guard on these numbers.

---

## 3. The double-application defect

### 3.1 What is actually wrong

`vehicles.yaml:304-306` reads:

```
# Crew-condition-driven vehicle disability — used by vehicles with VehicleCrew trait
# Replaces blanket SpeedMultiplier@CriticalDamage / TurretTurnSpeedMultiplier@CriticalDamage
# Use ^CrewedVehicle2 for Driver+Gunner, ^CrewedVehicle3 for Driver+Gunner+Commander
```

**Line 2 is false.** The crew traits use different `@`-keys (`@NoDriver`, `@NoGunner`, `@CommanderDrives`,
`@CommanderGuns`, `@NoCommander`) from the damage traits (`@HeavyDamage`, `@CriticalDamage`), so MiniYaml keeps **both
sets**, and there is no `-SpeedMultiplier@…` or `-TurretTurnSpeedMultiplier@…` removal anywhere in the repository
([`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §5, whose grep I did not repeat). Both sets multiply through
`Util.ApplyPercentageModifiers` (`Util.cs:238-246`), which is a bare product with no max-wins and no dedupe.

**And they fire on the same HP crossing.** `VehicleCrew.EjectionDamageState = DamageState.Heavy`
(`VehicleCrew.cs:55`, applied at `:264`), i.e. crew begin bailing below 50% — the identical line at which
`SpeedMultiplier@HeavyDamage` (50, `vehicles.yaml:181-183`) and `TurretTurnSpeedMultiplier@CriticalDamage` (0,
`:290-292`) fire. I verified the field and the comparison; the `[Desc]` at `VehicleCrew.cs:52-54` states the 50% band in
words.

Concretely, on any of the 13 crewed vehicles below 50% HP with the gunner gone: turret traverse is `0% × 0%`, and speed is
`50% × 40%` (commander driving) or `50% × 0%`. **Anyone sizing this work off that comment under-counts by one multiplier.**

### 3.2 How crew loss and impediment relate — the ruling this spec makes

**Two inputs, one quantity — but crew loss is not one of them.** Crew loss is a *third* system and stays separate. Reasons:

1. It is **discrete and irreversible**, not graded. A gunner is present or dead; there is no 40%-of-a-gunner.
2. Its effects are **role-shaped, not degradation-shaped**: "the commander is driving" is a different vehicle, not a worse
   one. `InaccuracyMultiplier@CommanderGuns: 200` (`vehicles.yaml:341-343`) says a specific man is doing a specific
   unfamiliar job.
3. It already has a coherent internal model across `^CrewedVehicle2` (`:307-321`) and `^CrewedVehicle3` (`:323-352`) that
   nothing in this work improves.

**What this work owes it is the collision, not the merge.** Today `50% (damage) × 40% (crew)` = 20% speed, unintended and
undocumented. Under impediment the damage half becomes `~85% (impediment ≈ 30 at 50% HP, k = 0.6)`, so the same vehicle is
`85% × 40%` = 34%. **The double-application does not disappear — it becomes gentler and, more importantly, continuous and
legible.** Whether to go further and make the crew multipliers replace rather than compose is a real question and is listed
in §6.3.

### 3.3 The comment fix, ready to apply

The brief asks that the wrong comment be fixed as part of this work. **Not applied here** (this branch edits no rules), but
this is the replacement text, to land with Stage 0:

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
# (:181) and TurretTurnSpeedMultiplier@CriticalDamage (:290) fire. A crewed vehicle below 50% that
# has lost its gunner therefore has turret traverse 0% x 0% and speed 50% x 40%. Crew loss is a
# SECOND, DELAYED EXPRESSION of the 50% line, not an independent axis. Size any change here for two
# multipliers, not one.
```

---

## 4. Naming

### 4.1 The candidates

| Candidate | For | Against |
|---|---|---|
| **`impediment`** | The user's own word, and this project's vocabulary rulings are load-bearing precisely because they are the user's. Runs the **same direction as the grant model**: more stacks = more impediment. Zero collisions — no engine C#, no YAML, no test references the string today. | Slightly formal; nobody will type it in chat. Not the word a player would use. |
| **`effectiveness`** (inverted) | Reads more naturally on a gauge — high is good, which is how every health bar in every game works. Matches military-doctrine vocabulary, which serves the standing project goal of doctrine-grounded realism. | **Mechanically backwards, and this is decisive.** Every input is a `GrantExternalCondition` warhead — 139 grant sites — and a grant can only *add*. An inverted quantity would need warheads that *revoke* stacks from a pool that starts full, which `ExternalCondition` cannot express: `GrantCondition` adds (`:110-172`), and there is no "start at N" field. It would also invert every one of the 65 existing ladder traits' comparisons. This is not a naming choice; it is a different and much larger implementation. |
| **`suppressed`** (keep) | Free. Zero rename, zero scenario churn (8 scenario YAML + 13 scenario Lua reference the token). | The user has ruled the name wrong, and they are right for a mechanical reason, not an aesthetic one: three engine modules read the count as *"this unit is under fire"* (§2.4). Under the merged quantity that sentence becomes false, and the code keeps believing it. |
| `degradation`, `attrition`, `wear` | — | `attrition` and `wear` both already mean something else in an RTS (losses over time; equipment decay). `degradation` is `impediment` with more syllables. |

### 4.2 Recommendation

**`impediment`.** The deciding argument is not that it is the user's word, though that matters — it is that **it runs the
same direction as every grant site in the mod**, and `effectiveness` does not. The gauge-readability argument for
`effectiveness` is real but belongs to the display, which can invert for presentation exactly as
`DetectabilityGrade.cs:64-71` already inverts concealment into exposure (`Exposure = 10 − concealment`). That precedent
settles it: **a display may invert; the simulation quantity should not.**

⚠️ **But note what that same precedent cost.** [`indicator-audit.md`](indicator-audit.md) §3.4 records the inversion as the
diamond's *"second intuition hazard"* — the grade runs on exposure while the `[Sync]` field runs on concealment, they run
opposite ways, and `DetectabilityGrade.cs:43-46` carries a warning about precisely this confusion. If the impediment gauge
inverts for display, **write the warning comment at the same time as the inversion, not afterwards.**

**Keep `suppressed` as the warhead-granted input token** (§0.3), unrenamed. Two names for two things that genuinely differ
is clarity, not clutter — and it means no warhead, no scenario YAML and no scenario Lua file is touched by this work.

---

## 5. Range and distribution — what a mark would have to gauge

**Not a mark design.** This section exists because [`indicator-audit.md`](indicator-audit.md) §3.4 records exactly how the
diamond failed: its underlying value spans only 3 of 10 levels on a vehicle, so on every vehicle in the game the diamond is
*"permanently a solid diamond that shifts only between `F0B232` and `F09425` — two ambers roughly 14 units apart in one
channel"*. The hollow/solid step — the channel the code itself calls *"the coarse channel"* (`WithSpottedDecoration.cs:80-81`)
— never fires on a vehicle at all. **That happened because the gauge was designed before anyone wrote down the distribution
of what it gauges.** So:

### 5.1 Reachable range, per chassis

| Chassis | `S` reaches | `D` reaches | `I` reaches | Full scale from one input alone? |
|---|---|---|---|---|
| **Infantry** | 0–100 (`TotalCap: 100`, `infantry.yaml:431`) | 0–100 (`k = 1.0`) | **0–100** | **Yes, from either.** |
| **Vehicles** | 0–50 (`TotalCap: 50`, `vehicles.yaml:364`) | 0–60 (`k = 0.6`) | **0–100** | **No — 0–60 from damage, 0–50 from fire. The top 40 points require both.** |
| **Aircraft** | **0** (no suppression exists, §2.3) | 0–80 (`k = 0.8`) | **0–80** | Damage only. **The top 20 points are unreachable on an aircraft.** |

### 5.2 What that means for anyone drawing this

Three statements a gauge designer must have before choosing a resolution:

1. **Infantry span the whole scale and are the only chassis that do.** A ten-step gauge is honest on infantry.
2. **Vehicles reach the top only under combined load.** A vehicle in a firefight at 40% HP sits near 80; a vehicle idling
   at 40% HP sits at 36; an undamaged vehicle under heavy fire sits at 50. All three are common. So a vehicle's
   distribution is **broad but bottom-weighted**, and it never pins — which is the opposite of the diamond's vehicle
   failure, and is the point.
3. **Aircraft top out at 80 and move only with health**, so on aircraft the mark is a slow, monotone, one-way ramp that
   never recovers without repair. **A gauge that reads as "under fire right now" would be lying on an aircraft.** If the
   mark is meant to carry transient pressure, aircraft need either a suppression input (they have none today) or a
   different mark.

### 5.3 The distribution question this spec cannot answer

How often does a unit actually sit in each decile in play? That is an observation, not a derivation, and **nothing here has
been launched or measured.** The honest position is that §5.1 bounds the range and §5.2 bounds the shape, but the *density*
— whether infantry cluster at 0–20 and 80–100 with an empty middle, which is what a 10-tier gauge would most want to know —
is unmeasured. A combat-sim run (`tools/combat-sim/`, per [`DOCS/recipes/BALANCE.md`](../DOCS/recipes/BALANCE.md)) or an
instrumented autotest would settle it, and **should be run before any gauge resolution is chosen.** That is the one piece
of evidence whose absence caused the original mistake.

---

## 6. Decisions for the user — NOT taken here

### 6.1 ⚠️ GATED — should a badly damaged unit keep going prone?

**This touches concealment and therefore sits behind the standing user gate** (`PIPELINE.md:580`: *"Nothing on stances,
ambush, concealment or cover may be implemented until the user says so"*). **Flagged, not specified.**

What already happens today, with no new coupling — the chain is
[`hurt-unit-modifiers.md`](hurt-unit-modifiers.md) §6 and I did not re-derive it:

- `GrantCondition@HeavyDamageProne` grants `prone` below 50% HP (`infantry.yaml:1088-1090`), on **all** `^Infantry`.
- `prone` adds `DetectableAddativeModifier@Prone: VisionModifier: 1` (`infantry.yaml:789-791`) — **+1 required observer
  strength, i.e. harder to see.**
- `prone` also selects `HitShape@Cover` radius 20 over `HitShape@Standing` radius 30 (`infantry.yaml:144-151`) —
  **harder to hit.**
- So a wounded man is currently *stealthier and smaller*, which is an inversion: damage makes him harder to kill.
- And the split that document calls its sharpest finding: in the 25–50% band a *moving, unsuppressed* wounded soldier holds
  the `prone` **token** (concealment + small hitbox) while `InfantryStates.IsProne` is **false**, so he gets no prone damage
  reduction, no prone speed modifier, and **renders standing up** (`InfantryStates.cs:182-222`). No test covers it.

**The decision:** whether impediment should feed prone at all, and whether the existing damage→prone grant should survive.
Three shapes exist (leave it; move it onto an impediment threshold; remove the damage-driven grant and let suppression own
prone) — **but all three are inside the gate and none is specified here.** If the user opens the gate, the token/flag split
above should be settled in the same conversation, because it is a live defect either way.

### 6.2 Should vehicles take a continuous speed penalty above 50% HP?

Today a vehicle is mechanically undamaged above 50% and then loses half its speed in one step
(`vehicles.yaml:181-183`). §2.2 proposes a gentle continuous ramp instead (100 → 95 → 90 → 85 in the top half) and a
**gentler** floor (25 rather than 0). This is the largest single balance change in the spec and it affects every vehicle
engagement. Options: adopt as proposed; keep the cliff by setting `k = 0` above 50%; or adopt the ramp but keep the 0 floor.

### 6.3 Should crew-loss multipliers compose with impediment, or replace it?

§3.2 rules them a separate system that **composes**, which keeps today's behaviour (gentler, per §3.2's arithmetic) and is
the smaller change. The alternative — crew loss *replaces* the impediment speed/turret terms on the 13 crewed vehicles — is
what the wrong comment claimed was already happening, and is defensible: it would make "the gunner is dead" a clean
statement rather than a second multiplier. It costs `-SpeedMultiplier@…` removal lines that have **no precedent anywhere in
the repository** (§3.1), so it is a new idiom, not a tweak.

### 6.4 Are the three `k` values right?

`k = 1.0 / 0.6 / 0.8` (§1.2). The infantry value is **derived** and I am confident in it. The vehicle value is a judgement
about how much armour should absorb. **The aircraft value is the weakest number in this document** — it is interpolation
with no evidence behind it, and aircraft have no suppression to blend with, so `k` alone sets the entire aircraft curve.
Worth a combat-sim pass rather than a ruling.

### 6.5 Should the `prone`/panic/fire gates eventually move onto impediment thresholds?

This spec leaves all six behaviour gates on the damage tokens (§2.5) on the §0.3 argument. That is deliberate and
conservative. A later pass could move some of them — but each is pinned by an NUnit corpus scan reading the shipped YAML
(`CriticalWoundFireGateTest.cs:42`, `PanicGateAtCriticalDamageTest.cs:38`), so each move is a test edit too, and the tests'
doc-comments record user rulings about *why* those gates sit at 50%. Not now.

---

## 7. Staging plan

Each stage is independently verifiable and leaves `main` working. **No stage depends on a later one.**

| # | Stage | Content | Verified by | Risk |
|---|---|---|---|---|
| **0** | **Clear the ground** | Fix the `vehicles.yaml:304-306` comment (§3.3). Delete the two dead token grants (`defaults.yaml:266-268`, `:272-274`). | `.\make.ps1 test` — ~20 `CheckConditions` warnings disappear; `LINT_BASELINE_PRUNE` if any were baselined. **Zero behaviour change.** | None. |
| **1** | **Aircraft sign fix** | Invert the four aircraft multipliers (`aircraft.yaml:406-417`) so damaged aircraft degrade. Keep the band structure. | `.\make.ps1 check` + `dotnet test`. One autotest if the user grants a slot. | Low, and **entirely self-contained**. |
| **2** | **Engine: the two pieces** | `DecayExemptSources` on `ExternalCondition`; new `GrantImpedimentFromHealth` trait. **Ships inert** — no YAML references either. | NUnit only (§1.4). Nothing in the game changes. | Low. Reviewable in isolation. |
| **3** | **Infantry onto impediment** | Declare `impediment` on `^Infantry`; retarget the 50 suppression ladder traits; delete the 20 damage-ladder traits; add `GrantImpedimentFromHealth` with `k = 1`. `suppressed` and all seven threshold reads untouched. | `SuppressionMathTest` updated with the new tables; `CriticalWoundFireGateTest` / `PanicGateAtCriticalDamageTest` must stay **green without edits** — they are the proof the gates were not disturbed. | **Highest.** This is where the balance actually moves. |
| **4** | **Vehicles onto impediment** | Same for `^Vehicle`, `k = 0.6`, plus the §6.2 decision. 23 disarm gates untouched. | As above + `make nav-guard` is irrelevant here; combat-sim for the speed ramp. | High — see §6.2. |
| **5** | **Aircraft onto impediment** | Replace the (already sign-fixed) aircraft bands with the continuous ladder, `k = 0.8`. | `dotnet test`; combat-sim for `k`. | Low. |

### Which stage can be cut

**Stage 5.** Once Stage 1 has fixed the sign, aircraft have a correct four-value damage ladder that punishes in the right
direction, and — because aircraft have no suppression at all (§2.3) — impediment on an aircraft is *only* a smoother
rendering of that same ladder. Nothing else in the system reads it, nothing is left inconsistent, and the §5.2 finding that
an aircraft's impediment can never carry transient pressure is an argument that the extra machinery buys little there.
Cutting it leaves aircraft on `heavy-damage-attained` / `critical-damage` bands, which is exactly where they are today, only
pointing the right way.

**Stage 1 can also ship alone and immediately**, before anything else is decided. It is the user's ruling 2, it is four
numbers, and it is the only part of this document that fixes something that is unambiguously broken right now.

**Stages 0, 2, 3 are not cuttable.** 0 removes a comment that would actively mislead the implementer of 3; 2 is the whole
mechanism; 3 is the feature.

---

## 8. What this spec does not settle

1. **No measurement of any kind.** Nothing launched, nothing linted, no combat-sim run. Every number in §2.2 is reasoned
   from shipped values, and the §5.3 density question — the one that caused the diamond's failure — is open.
2. **The three `k` values**, especially aircraft (§6.4).
3. **Whether the vehicle speed ramp (§6.2) is an improvement or a nerf** in play. It is gentler at the bottom and harsher
   in the top half; which dominates is an empirical question.
4. **The prone inversion (§6.1)** is gated and deliberately unspecified.
5. **I did not enumerate the scenario `.lua` and scenario `rules.yaml` files** that would need attention if a later pass
   moves the threshold gates. Under this spec none is touched, because `suppressed` does not move — but that guarantee
   holds only as long as §6.5 stays unactioned. Counts at this ref: 8 scenario YAML, 13 scenario Lua.
6. **`WithSpottedDecoration` and the diamond are untouched by this work**, and this spec deliberately says nothing about
   what the mark should become. §5 is input to that conversation, not the start of it.
