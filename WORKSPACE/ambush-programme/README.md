# Ambush, concealment & cover — research programme

**Started 2026-08-20. RESEARCH ONLY. Nothing here has been implemented, and nothing may be
implemented until the user explicitly says so.** Their words: *"I will let you know when we are
ready to implement, until then just ask me"* and *"it is my wish that you really get to the bottom
of this before we start implementing."*

This folder is self-contained. A fresh session should be able to read this file and continue
without any prior transcript.

---

## 1. How this started, and the mistake that must not repeat

The user reported a **behaviour** problem:

> *"it is really hard to stay hidden, and in ambush stance that should take care of itself."*

A previous session researched it, established that no stance modifies detectability, and shipped
**legibility around the absence** — a concealment gauge showing how visible a unit is — then
reported that as the answer. The user pushed back:

> *"Did you just skip that because that is not how it works. What else have we skipped?"*

**Root cause, recorded as manager decision 08: "legibility first" is a SEQUENCING rule, not a SCOPE
rule.** When the user describes behaviour the game lacks, that is a feature request, not a
misconception to be corrected with a readout. Verifying the behaviour is absent is step one, not
the answer. Three asks got that treatment (ambush-hides, stop-and-take-cover-on-contact, the white
`!`), and the TAKE_COVER button was deleted the same day without asking.

## 2. The user's vision — quote this to workers, do not paraphrase

- **Ambush changes BEHAVIOUR, never detectability.** *"There is no fixed visibility adjustment from
  the stance itself."* A running soldier about to be spotted by an enemy **we can already see**
  stops running; stopping is what drops his visibility.
- **The ambush proper:** a group holds fire until any one of them is detected, then all fire at
  once, targets pre-acquired, no aim delay. The scene: a forest road, dug-in soldiers, an enemy
  column reaching the middle before anything happens.
- **A third phase:** only soldiers with a shot fire; **the rest switch to temporary "hunt"
  behaviour while the ambush is active** — same stance, different behaviour, *"while we are still
  visible and fighting."* **The exit condition is undefined and is the trap** — get it wrong and
  the squad hunts forever and never re-arms.
- **"Instinct" is the design principle:** *"small things that soldiers can do on their own, that
  will feel like the soldier's instinct… should not feel invasive."*
- **Take Cover is about PROTECTION, not concealment** — best-protected cell first, least-visible
  second, deeper into forest better than the edge.

### Rulings already given (do not re-ask)

| Ruling | Detail |
|---|---|
| Stop-and-resume ships FIRST | Reroute is a later stage. "Resume when clear" is load-bearing and needs a hard definition. |
| Aim delay | Only address it **if one already exists**. One does — see §3. |
| Protection | Separate job; decide after the audit. |
| Take Cover | Both automatic **and** a button. The button was deleted 2026-08-19 and needs restoring if built. |
| SDK / build | Unrelated, but settled 2026-08-20: `global.json` stays pinned; the user installs the .NET 6 SDK. |

---

## 3. What the research actually found

**Nine strands were dispatched. Eight reported. Every strand that examined a mechanism found it
already built and quietly broken, not missing.** The assignment was never "design ambush" — it was
"find out why the ambush that shipped does not work."

### 3.1 The headline: the widened ambush is gated to bots

`enable-ambush-tactics` is the gate for Stages 2–4 (halt-before-contact, the stationary
hide-and-spring state machine, the coordinated spring). It is granted:

- by `LaneAmbushBotModule` to bot-posted units (`LaneAmbushBotModule.cs:451,474`), and
- by **Lua, artificially, in every autotest that covers the feature** —
  `test-case01-forest-ambush`, `test-case01b-detect`, `test-ambush-detection`,
  `test-ambush-convoy`, `test-ambush-fast-convoy`, `test-ambush-enemy-stops` all call
  `GrantCondition("enable-ambush-tactics")`.

`AutoTarget.cs:93` describes the gate as *"a human opt-in / bot ledger commit / test map grants"* —
but **no human opt-in path ships.** `LaneAmbushBotModule.cs:48` states it outright: *"humans /
Normal / Rush / Turtle never instantiate it."*

**So a human clicking the Ambush button gets the narrow stance. The bots get the feature.** Five
passing autotests do not contradict this; they pass *because* they grant the gate by hand.

### 3.2 Concealment: the largest cover term is dead — and not for the obvious reason

`object-proximity` (`+1/+2/+3`, the biggest term in the concealment stack, consumed at
`infantry.yaml:704-715`) has **exactly one emitter in the entire mod**:
`ProximityExternalCondition@ObjectProximity` on `^TreeHusk` (`husks.yaml:118-121`). **Living trees
emit nothing at all.**

> **CORRECTED 2026-08-20 by the ground-truth audit — read this, the first version was wrong.**
> The first pass through this (and the manager's own grep, and every message sent to the user
> before the audit landed) concluded "you must burn the forest down before it hides you." That is
> **not right**, and the truth is worse. The husks do emit — but the trigger radius is 0.18–0.63
> cells, centred inside a cell `^TreeHusk` **blocks**: it carries `Building: Footprint: x` with no
> `Passable`, unlike a living `^Tree`, which is walkable. The nearest sub-cell a soldier can
> occupy is 244–771 WDist away against radii of 182–640, so **zero of 23 husk types are
> reachable.** Burning the forest down does not help either. The ladder is dead outright, not
> merely misdirected. See `WORKSPACE/recon/260820-ambush-cover-detection-audit.md` §1.4.
>
> This is a worked example of why the synthesis exists: a confident grep by three separate
> parties produced a plausible, quotable, wrong story, and only a fourth pass that checked the
> *geometry* rather than the *grant* caught it.

The dead ladder was documented in `WORKSPACE/recon/260728-trees-concealment.md` on **28 July**,
restated in `260819-infantry-visibility-stances.md` on **19 August** (*"this is almost certainly
not intended"*), and never acted on.

### 3.2a Three more defects, from the ground-truth audit

- **`dugin` has two timer bugs** (`GrantConditionOnMovement.cs:44,52-80`). The still-counter is
  armed only by a stop *transition*, so **a map-placed soldier who is never ordered anywhere never
  digs in** — capped at CV 4 forever. And the counter is not reset when movement resumes, so
  `dugin` can be granted mid-stride and held for the rest of the leg: `moving` (−1) and `dugin`
  (+1) are not actually exclusive.
- **The −2 firing penalty is `primary`-armament only** (`GrantConditionOnAttack.cs:25` defaults to
  `{"primary"}`, not overridden at `infantry.yaml:722-726`). **Firing an RPG, a grenade launcher or
  the `garrisoned` armament costs no detectability at all.**
- **Infantry CV tops out at 9**, so the CV-10 "invisible to standard vision" state is unreachable
  in any shipped configuration.

> **RESOLVED 2026-08-20 by the bug-filing verification pass — and it widened.** The clamp is
> `[1,10]`. Base infantry reach **9** via veterancy, which *both* prior accounts omitted. Level
> **10** is reachable only by `^SN` and `^SF` — and both currently carry
> `Prerequisites: ~disabled`.
>
> So both strands were partly wrong, and the consequence is bigger than either: **total
> invisibility goes live the moment EITHER the cover ladder is repaired OR the Sniper is made
> buildable.** The second is a one-word YAML edit that nobody would ever connect to concealment.
> §7's sequencing was written against the cover fix alone and is therefore too narrow — the
> visibility clamp guards two independent doors, not one.

### 3.3 Stance does not touch detectability at all

`stance-ambush` and `stance-holdfire` are granted in four places
(`defaults.yaml:309-310,570-571,670-671,682-683`) and consumed by **zero** `RequiresCondition`
sites in `mods/`. Every `DetectableAddativeModifier` keys on `object-proximity`, `prone`, `dugin`,
`firinganyweapon`, `moving`, `rank-veteran` or `!airborne`. This part of the user's complaint is
literally true.

### 3.4 The volley is not simultaneous, and "zero aim delay" is false

- `TriggerNearbyAmbushAllies` sets a flag on each nearby ambusher and **never makes any of them
  shoot** — each fires on its own next scan. WW3MOD overrides the infantry scan interval to
  **16–32 ticks** (on `^CamoSoldier`, inherited by the Rifleman) against an engine default of 3–8,
  drawn randomly per unit. At 60 ms/tick the "simultaneous" volley smears over **1–2 seconds**.
- `Armament.AimingDelay` is **15 ticks on infantry, 30–50 on vehicles**. It resets when an armament
  sees a new target, and pre-aiming never touches it — `PreAimAtTarget` only rotates facing and
  never reaches an armament. The delay is charged **in full after the trap springs**: ~3 s of an
  MBT standing in the open not shooting. The Ambush tooltip promises *"zero aim delay."*

The two stack and are the same order of magnitude; fixing either alone leaves about half the lag.

### 3.5 Protection: prone is functionally dead

- Of the mod's **109 `DamageTypes:` declarations, exactly one** carries a `Prone*` token
  (`weapons-superweapons.yaml:399`). `InfantryStates.cs:200-203` only applies `ProneDamageModifiers`
  to warheads declaring a match, so **going prone reduces damage from one superweapon and nothing
  else.** Every bullet is `BulletDeath`, every shell `ExplosionDeath`.
- **Dug in is concealment only** — zero damage reduction.
- `DensityModifiesDamage` (`infantry.yaml:37-45`) works but is capped at 20%. **CORRECTED: the
  "standing next to a house is maximal forest cover" line that appeared here, and was reported to
  the user, is wrong** — no building in the mod carries `Building.Density` at all; `Density:` is
  declared in `decoration.yaml` and nowhere else. The asymmetry beside it *is* real, but it is
  rocks, not houses: one rock cell is 50 and maxes the tier instantly, one tree is 10 and falls
  below the 15 floor.
- **The dominant protective effect is not damage reduction at all.** `ClearSightThreshold`
  (`Armament.cs:364`) refuses the shot outright once foliage on the line exceeds the weapon's
  threshold. A rifle at threshold 4 simply cannot fire through 4+ dense tree cells. Nothing in the
  UI says so; this is almost certainly what players perceive as "cover working".
- **Map density is static.** `UpdateDensityForBuilding` and the shadow-update queue exist with
  their callers commented out (`Building.cs:377-396`, `World.cs:514-517`). A forest shelled flat
  still grants full cover, full concealment, and still refuses rifle shots. **Take Cover would
  confidently march a squad onto burnt ground and report success.**

### 3.6 What the genre does

**No RTS answers "can the enemy see me right now."** CoH, WARNO, Steel Division and Gates of Hell
all model concealment richly and decline to state it. Wargame: Red Dragon shipped the indicator (a
hidden unit's label blinked, stopping when spotted) and **WARNO removed it**, with players calling
the removal a bugfix. The one game that answers it is Close Combat (1996).

**Best idea to steal:** Close Combat's per-unit **state word** — `Hiding`, `Ambushing`,
`Holding Fire`, `Can't See`, `Too Close` — coloured **green when the unit is obeying your order,
red when it is countermanding it, white when it has no order and is acting on local conditions.**
The player's question is not "what colour is my cover", it is *"is my plan happening."*

**Failure mode most relevant to us:** a smart threshold that silently overrides an explicit order.
Eugen's sliders un-arm Hold Fire and players wrote a forum thread titled *"i told you to hide."*
`AmbushTactics` triggers 3–5 (BestStrikeDegrading / Saturation / Overrun) are exactly that class of
heuristic and will read as disobedience unless they are legible as *reasons*.

### 3.7 Bots and the benchmark

Ambush is on **both** bot profiles — `@experimental` plus the `@stable` twin since `b8d2e601`
(2026-08-02) at identical tuning. It has **already been A/B'd at N=10** with a marker-verified
control and measured as noise, with the two rungs disagreeing in sign. Dose is capped at four units
(`MaxAmbushes: 2` × `UnitsPerAmbush: 2`), which is both the safety argument and why a whole-match
score aggregate could not detect it.

**The benchmark baseline is stale and unusable.** Newest record is `260729`; since then 188 commits
have touched BotModules, including `b8d2e601` (a deliberate change to the control) and `06f0605a`
(correcting the byte-identity claims that promotion expired). **No ambush measurement can be
trusted until the baseline is re-taken.**

---

## 4. The documents

| File | What it covers |
|---|---|
| `260820-ambush-player-loop.md` | The player's minute-to-minute loop; found the bot-only gate (§3.1) |
| `260820-coordinated-ambush.md` | Group spring, simultaneity, aim delay (§3.4) |
| `260820-cover-protection-and-take-cover.md` | Does any position protect you; Take Cover spec (§3.5) |
| `260820-ambush-legibility.md` + `-mockup.html` | The readout: one white `!`, plus a 5-row concealment ledger |
| `260820-ambush-cover-genre-survey.md` | Ten RTS surveyed; §11 is a ledger of 16 unconfirmed claims |
| `260820-predictive-detection.md` | "About to be seen" — 3 of 4 pieces already exist |
| `260820-ambush-failure-modes.md` | 19 ranked ways Ambush reads as a broken game |
| `260820-ambush-bot-blast-radius.md` | Bot/benchmark impact + a measurement plan (~80 matches, 2.5 h) |

**Ninth strand — landed after the first version of this README, and it corrected it.** The
ground-truth detection audit lives at `WORKSPACE/recon/260820-ambush-cover-detection-audit.md`
(kept in `recon/` because that is where code-verified ground truth belongs). It is the only
document here that has been through a second independent pass, and that pass caught a real error
in its own author's work.

> **WITHDRAWN: "treat the audit as outranking the other eight" was bad advice and is revoked.**
> The synthesis found that following it would have propagated **four** errors. Its mistakes all
> sit in one layer — engine default versus mod override, abstract template versus fielded actor,
> base `Vision: 3` versus the `Vision: 5` it documents itself two rows below. A document can be
> the most carefully verified in a set and still be systematically wrong about *which* value
> ships. **Read `260820-synthesis.md` for adjudicated verdicts instead; it settled six
> cross-strand contradictions from code.** The named exceptions below are examples of the
> pattern, not the complete list.

**The exception: the audit is wrong about aim delay.** Its §2.5 concludes "there is no aim delay
for anyone." It examined `FireDelay` and `FacingTolerance` and never looked at `AimingDelay`,
which is 15 ticks on infantry, 30–50 on nine vehicles, reset on every target change
(`Armament.cs:345-350`) and blocking the shot at `:327`. The coordination strand is right here and
the audit is not. This is the same wrong-field trap that caught an earlier worker, twice, in the
same file.

**Also corrected by the same pass: three documents claim humans never re-arm after springing an
ambush. They do** — `AutoTarget.cs:746` clears the latch on the human path. The "SPRUNG is
terminal, DO NOT clear" comment those documents quote sits in the *bot-gated* branch. Nobody
should go fix that bug; it is not there.

### The defect list

All 18 defects are filed in `WORKSPACE/bugs/discovered.md`, each with a symptom, `file:line`, an
**Established:** line separating read-from-code from derived-by-arithmetic, and a **Confirm by:**
line. Read those rather than mining the design docs — the docs are the reasoning, the bug list is
the actionable form, and the bug list is the one that has been re-verified against source.

---

## 5. What is NOT verified

Take this seriously — almost nothing here was measured.

- **The 1–2 s volley spread is derived from YAML and the timestep, not observed.** The MiniYaml
  merge was not traced to confirm nothing later overrides the scan-interval fields.
- **The cover-margin and transit-loss numbers are reasoned, not simulated.**
- **The genre figures are community-extracted**, not developer-documented; §11 of the survey lists
  16 specific unconfirmed items, including that the popular CoH "yellow 25% / green 50%" numbers
  are contradicted by the stat dumps.
- **No game was launched by any strand.** All nine were doc-only by instruction.
- **`test-case01b-detect` exists, was authored specifically to measure time-to-first-shot spread,
  and has never been run once.** It would settle §3.4 directly. This is the highest-value single
  run available.
- **Two capture scenarios merged nine days ago look calibrated one tier low.**
  `test-visual-gauge-truth.lua:10-16` reasons a map-placed rifleman "sits on tier 3" and derives a
  22c ring; `test-visual-concealment-gauge.lua:21-27` asserts "stopped ⇒ tier 3". Both omit
  `prone`, which `ProneCondition` grants on `!moving`, making a stationary soldier tier 4 (19c).
  The audit predicts both fail their own premise checks. One run of either settles it, and it is
  worth spending **before** trusting their output. Note the gauge-truth scenario reasons
  *correctly* about the `dugin` bug and then misses prone — which is exactly why it reads
  convincingly.
- **The husk-reachability enumeration in §3.2 is arithmetic over four code sites, not
  measurement.** Each premise (radii, impassability, the strict `<`, sub-cell quantisation) was
  verified; nobody watched a soldier fail to receive the condition.
- Two workers self-corrected mid-report — one had written the wrong aim-delay verdict off
  `FireDelay` (3 ticks, negligible) before finding `AimingDelay`; another had claimed the project
  draws nothing for concealment before finding `^DetectableRangeCircles` already ships. Good
  behaviour, but it means these reports are one careful pass, not two.

**Do not cite `test-case01-forest-ambush` as evidence the coordination works.** It is 22 days and
~1000 commits stale, was a false green until `e14dced3`, and measures cost-weighted losses without
ever asserting simultaneity.

---

## 6. Traps for whoever implements

- **RNG stream identity.** The obvious fix for the ragged volley — force allies to scan immediately
  — routes through `ScanForTarget`, which re-arms its timer off `SharedRandom` and would shift the
  shared RNG stream, breaking the frozen `@stable` baseline. **The codebase already solved this
  exact problem for target preemption; copy that pattern verbatim.**
- **Do not drive visibility marks from granted conditions** — that is the shape of two shipped
  desyncs in this repo.
- **The hunt phase should be a computed predicate, not a set-and-restore of engagement stance.**
  The engine already has an `EngagementStance` axis (`HoldPosition/Defensive/Hunt`) orthogonal to
  the fire stance, so "same stance, hunt behaviour" needs no fire-stance change. Deriving it means
  there is no state to forget to unwind, and the "squad hunts forever" failure becomes impossible
  by construction rather than merely unlikely.
- **The white `!` cannot ship as-is.** The nearest existing state, `haltedForAmbush`, is AI-only.
  §6 of the legibility doc lists what is needed.
- **`WithSpottedDecoration` is the observer-set precedent to inherit, not reinvent** — it counts
  only enemies you are already aware of, so a "hidden" mark must be its exact complement (same
  observer set, same filter, same `any` reduction, negated) plus a requirement that someone is
  actually looking. Different observer sets would let both marks light or both go dark.

---

## 7. Rulings and open questions

### RULED 2026-08-20 — total invisibility is a bug, not a tier

Asked whether a vanishing ring is the right way to say "undetectable", the user rejected the
premise of the question:

> *"I think it is an error that they can become fully invisible… it feels a bit strange that you
> cannot find an enemy if you are basically standing on top of him… I think their visibility should
> be at least 1 at all times."*

> **RE-RULED 2026-08-20, and this one IS a go to implement.** Told that their first ruling was a
> no-op, the user chose **"both, comparison first"**: make the reveal comparison non-strict *and*
> cap the concealment ceiling, closing the mechanism-level route first because it is the one no
> unit choice can avoid. This lifts the research freeze **for this fix only** — everything else in
> this folder is still research-only until they say otherwise.
>
> The comparison change sits in the detection path shared with `@stable`, so it **requires a
> benchmark re-take**, which is user-gated and cannot be run autonomously. Per project policy
> `@stable` inherits the improvement rather than being gated off it — but the commit must say so
> plainly, so the next baseline is re-taken knowingly.

**THE FIRST RULING WAS A NO-OP — kept here because the reasoning is the reusable part.**

The synthesis established why, and it is a language problem rather than a design one. The user
said *"visibility should be at least 1 at all times"*, meaning **always findable**. In this
codebase the axis runs the other way: `Vision` / concealment value counts **upward** for stealth,
and a floor of 1 **already exists** (`Detectable.cs:90-91`) — it is one of the three ingredients
*producing* the invisibility. Implementing the words literally changes nothing. Do not build it.

**And the cliff is live today, not latent.** The earlier guess in this section — that tier 10 was
unreachable and the clamp merely urgent-later — is wrong in the direction that changes the plan. A
Sniper or SF at rank 3, stopped, computes CV 10 from `5` base + prone + dug-in + `rank-veteran`,
with no cover term needed at all. Reveal is a strict comparison and `^StandardVision` stops at
strength 10, so no vision band can exceed it.

> **One tension left open, deliberately.** `^SN` (`infantry.yaml:1535`) and `^SF` (`:1980`) both
> carry `Prerequisites: ~disabled`, so neither is *buildable* today — which is what the earlier
> "latent" reading rested on. But not-buildable is not not-existing: a map can place either
> directly, which is exactly how the capture scenarios put units on the field. Whether "live
> today" means *in a normal match* or *only on a map that places one* has *not* been settled, and
> the two readings imply different urgencies. Settle it before acting.

**A second invisibility route exists that no strand found**, and it needs no Sniper. Forest shadow
subtracts from the **observer's** strength (`MapLayers.cs:371`) and floors it at 1; target CV also
floors at 1 (`Detectable.cs:90-91`); the comparison is strict (`MapLayers.cs:578`). `1 > 1` fails
— so deep forest hides **anything from anyone** at 2–32 cells, whatever the CV. This one is
mechanism-level and does not depend on unit type at all. Its *depth threshold* is baked per-map
into `shadows.bin`, and there is a one-cell escape hatch; none of it has been observed running.

### OPEN — dogs as a detector unit

The user raised RA's dogs as a way to detect hidden units and explicitly asked for an opinion,
while saying they are unsure. Recorded as a separate feature, deliberately **not** coupled to the
ruling above: if the visibility floor exists, dogs are optional and judged on whether they are fun;
if dogs are the *only* counter to invisibility, they become compulsory in every match, which is a
much worse constraint than the problem they solve. Decide the clamp first, then dogs on their own
merits.

---

## 8. Suggested next steps (not started, not authorised)

1. **Synthesis pass across all nine docs that surfaces where they CONTRADICT each other** rather
   than blending them. Then one plan to the user.
2. Run `test-case01b-detect` once — the only cheap measurement available (§5).
3. Decide whether §3.1 (the bot-only gate) is the answer to the user's original complaint. It is
   the strongest candidate, and it is a gating question, not a design one.
4. Re-take the `@stable` benchmark baseline before any ambush measurement (§3.7). User-gated.
