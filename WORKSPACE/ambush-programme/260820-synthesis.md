# Ambush programme — synthesis across nine strands

2026-08-20 · research only, no behaviour change · branch `wt/ambush-synthesis` off `main @ 57822b4e`

**This document exists to surface where the nine strands CONTRADICT each other.** It is deliberately
not a blended summary. Where two strands assert incompatible things, §1 says which is right and
gives the `file:line`. Where they agree, §2 says so briefly and records how each finding was
established. §3 is the shared blind spots — the claims everyone inherited and nobody recomputed.
§4 is the ordered plan.

**Nothing was run.** No game launched, no autotest, no validator, no build. Every verdict below is
read from source in this worktree. Where I could not settle something from code, I say so.

**Headline:** the single most consequential finding is not in any of the nine documents.
**Total invisibility is live in the shipped game today, by two independent routes, and the ruling
the user gave to fix it is already implemented and is part of the cause.** §1.1 and §1.2.

---

## 0. Reading order if you only have five minutes

1. **§1.1** — CV 10 is reachable today. Both strands that argued about it were wrong, and the
   README's guessed reconciliation is wrong in the direction that matters.
2. **§1.2** — a second, much wider invisibility route that no strand identified, and which the
   user's recorded ruling cannot fix because it *is* the mechanism.
3. **§1.3** — the human ambush latch clears. Two docs, including the one designated as outranking,
   say the opposite, and one of them built four failure modes on it.
4. **§4** — why the plan the README proposes would ship a no-op followed by an exploit.

---

# 1. Contradictions

## 1.1 Is total invisibility (CV 10) reachable today? — YES, and everyone was wrong

This is the contradiction the brief named, and it is worse than "one of them is right".

| Source | Claim |
|---|---|
| `260820-ambush-legibility.md` §7.2 | **Reachable.** `5 (Sniper/SF) + 3 (cover) + 1 (prone) + 1 (dug in) = 10` |
| `260820-ambush-cover-detection-audit.md` §1.4 | **Not reachable.** "infantry CV tops out at `3 + 1 (prone) + 1 (dugin) + 4 (rank 4) = 9`" |
| `README.md` §7 (the manager's reconciliation) | "tier 10 is very probably **not reachable today** — the cliff is latent, not live… **The clamp is therefore cheap now and urgent later**" |

**Verdict: reachable. The legibility strand's conclusion is right and its arithmetic is wrong. The
audit's arithmetic is wrong. The README's reconciliation is wrong, and it is the premise under a
user ruling.**

### The correct arithmetic

Every live additive term on infantry, from code:

| Term | Δ | Site |
|---|---|---|
| Base `Detectable.Vision`, standard infantry | 3 | `mods/ww3mod/rules/ingame/infantry.yaml:96-97` |
| Base `Detectable.Vision`, **Sniper** | **5** | `infantry.yaml:1541-1542` |
| Base `Detectable.Vision`, **Special Forces** | **5** | `infantry.yaml:1987-1988` |
| `prone` | +1 | `infantry.yaml:716-718`; granted on `!moving` at `:294` |
| `dugin` | +1 | `infantry.yaml:719-721`; `TimeToBeStill: 200` at `:139-142` |
| `rank-veteran == 1..4` | **+1/+2/+3/+4** | `mods/ww3mod/rules/defaults.yaml:211-222` |
| `object-proximity == 1/2/>=3` | +1/+2/+3 | `infantry.yaml:707-715` — **dead**, §2.2 |
| `firinganyweapon` | −2 | `infantry.yaml:727-729` |
| `moving` | −1 | `infantry.yaml:730-732` |

Composition is plain addition then clamp to `[1, 10]` (`Detectable.cs:88-95`;
`MapLayers.VisionLayers = 11` at `MapLayers.cs:75`).

**A Sniper or Special Forces soldier, standing still, at rank 3:**

> `5 (base) + 1 (prone) + 1 (dug in) + 3 (rank 3) = 10`

At rank 4 it is 11, clamped to 10. **No cover term is required.** Reveal needs an observer strength
*strictly greater* than the level (`MapLayers.cs:578`: `ResolvedVisibility[puv] > visibility`), and
`ResolvedVisibility` cannot exceed 10 (`MapLayers.cs:244-250` scans down from `VisionLayers - 1`;
`^StandardVision` tops out at strength 10, `defaults.yaml:47-84`). **So CV 10 is undetectable by any
standard-vision observer at any range.**

### Why each party got it wrong

- **The legibility strand** used the `+3` cover term (dead) and omitted `rank-veteran` entirely.
  Right answer, wrong route — which is why it survived scrutiny and why the README's guess at *how*
  it was wrong went astray.
- **The audit** counted `rank-veteran` — indeed §1.3 flags it as "the largest live concealment
  modifier in the game" — but computed the ceiling from base infantry `Vision: 3`, while its own
  §1.5 table two rows below lists `Sniper ^SN (Vision: 5)`. **It had both halves and never
  multiplied them.** This is the cover-ladder error's exact sibling: each premise checked, the
  combination not.
- **The README** guessed the reconciliation ("legibility included the unreachable +3, so tier 10 is
  probably not reachable") and flagged it as unchecked. The guess is half right — legibility *did*
  include the dead term — but the conclusion drawn from it is false.

### The load-bearing consequence

The README §7 sequencing note says the invisibility cliff "is latent, not live" and becomes live
"the moment anyone repairs the cover ladder", concluding the clamp is *"cheap now and urgent
later"*. **It is live now.** Any player fielding a sniper that survives to rank 3 has an
undetectable unit today, in shipped rules, with no bug and no repair required.

### And one further correction the audit makes worse

Audit §3.8 states `^SF` is `Prerequisites: ~disabled` and therefore **"no player can field it
today"**. **This is wrong.** `^SF` and `^SN` are abstract templates; the fielded actors are the
faction variants, which override `Buildable` outright:

- `SF.america` — `Prerequisites: ~player.america, ~techlevel.infonly` (`infantry-america.yaml:102-107`)
- `SN.america` — same (`infantry-america.yaml:88-92`)
- `SF.russia` / `SN.russia` — same shape (`infantry-russia.yaml:88-89`, `:103`)

Both units are fully fieldable, and they are precisely the units that reach CV 10. This is the trap
`CLAUDE.md` names in its first hard rule: an RA-shaped `Buildable` block was read as the delivery
mechanism, on the template rather than on the actor that ships.

---

## 1.2 The second invisibility route — nobody found it, and the user's ruling is the mechanism

**No strand identified this. It is wider than §1.1 and it is not fixed by anything currently
proposed.**

Every CV table in all nine documents models detection as `observer band strength` vs `target CV`,
with terrain mentioned only in prose. But `MapLayers.AddSource` subtracts a per-sightline forest
term from the **observer's** strength before stamping it, and then floors it:

```
var modifiedStrength = strength - shadowModify;
if (modifiedStrength < 1)
    modifiedStrength = 1;
visibilityCount[index][modifiedStrength]++;
```
— `engine/OpenRA.Game/Traits/Player/MapLayers.cs:371-376`

`shadowModify` comes from `Map.ForestGroundShadow` (`engine/OpenRA.Game/Map/Map.cs:1102-1120`),
whose own reference table (`:1098`) for uniform density-10 tree cells crossed on the sightline is:
**1→1, 2→2, 3→4, 4→6, 5→8, 6→10.**

Now combine the three floors:

- observer strength is floored at **1** (`MapLayers.cs:373-374`),
- target CV is floored at **1** (`Detectable.cs:90-91`),
- reveal is **strictly greater** (`MapLayers.cs:578`).

**`1 > 1` is false.** Therefore:

> **Any unit standing behind six or more dense tree cells is invisible to any ground observer
> between 2 and 32 cells away, at any CV — including CV 1, including while moving and firing.**

And it does not take six. Four dense cells cost the observer 6 strength; the strength-10 band only
exists within 4 cells (`defaults.yaml:48-50`), so a point-blank observer contributes 4. A merely
*stopped* rifleman is CV 4 (`3 + prone`). **`4 > 4` is false — a plain stopped rifleman four cells
deep in trees is invisible to a soldier standing three cells away.**

**Two qualifications, both from `Map.RecomputeShadowFrom` (`Map.cs:1126-1170`):**

- **Shadow is only computed for viewer-target pairs 2–32 cells apart** —
  `FindTilesInAnnulus(fromUV, 2, 32, true)` at `:1128`. At ≤1 cell there is no entry, so an observer
  literally adjacent sees at full strength. **The forest route therefore has a one-cell escape
  hatch; the CV-10 route in §1.1 does not** (`10 > 10` is false at any range). That makes §1.1 the
  closer literal match to the user's *"standing on top of him"*, and §1.2 the wider problem.
- **The curve is baked into `shadows.bin` at map load** and only regenerates via
  `utility --regen-shadows` (PITFALL comment at `:1172-1176`). So the values actually in play on any
  given map are whatever its cached bin holds, which may predate the current curve. **This is an
  argument for measuring rather than deriving**, and it is why §4 Step 4 proposes a scenario.

That is the user's complaint restated in code:

> *"it feels a bit strange that you cannot find an enemy if you are basically standing on top of
> him."*

### Why this matters more than §1.1

The user's ruling was recorded (README §7) as:

> **"clamp minimum detectability to 1 unconditionally. No unit is ever undetectable by standard
> vision."**

**That clamp already exists and has always existed** — `Detectable.cs:90-91`, `if (detectable <= 0)
detectable = 1`. Implementing the ruling as written is a **no-op**. Worse, the floor is one of the
three ingredients that produce the invisibility: a floored observer cannot strictly-exceed a floored
target.

The user's words were *"their visibility should be at least 1 at all times"*, meaning **"always
findable"**. Translating that into the codebase's vocabulary — where `Vision` counts *upward for
stealth* and where a floor of 1 already exists — inverted the meaning. The sign convention that the
audit §1.1 correctly names as "the first thing to get wrong" got the ruling itself.

**The fix surface is the comparison and the floors, not a clamp.** Both routes (§1.1 ceiling, §1.2
shadow) reduce to the same root: a strict `>` between two quantities that are both clamped into the
same 1–10 range. Options are a strength-11 band, a CV ceiling of 9, flooring observer strength at 2,
or `>=` — with materially different balance blast radius. **That is a user decision and it should be
put to them as one question, not as the clamp currently on the books.**

*Established: read + derived, not measured. The arithmetic is over five code sites, each verified.
Nobody has watched a soldier fail to be seen.*

---

## 1.3 Does the human ambush latch clear? — a three-way dispute, and the majority is wrong

| Source | Claim |
|---|---|
| `260820-coordinated-ambush.md` §5, §7 | The ungated (human) path **clears** `ambushTriggered` when a scan returns no target (`:746`). Only the gated path is terminal. |
| `260820-ambush-failure-modes.md` F2/F8/F13 | **One-way latch.** "The **only** clearing path is `ResetAmbushState()`" — therefore "**bots re-arm and humans do not** — the AI has the affordance the player lacks." |
| `260820-ambush-cover-detection-audit.md` §2.2 | Stated inside *"What a human actually gets — the stock path"*: "**The latch is terminal.** … A group that springs once stays sprung." |

**Verdict: the coordinated-ambush strand is right. The other two are wrong, and the failure-modes
reading is exactly backwards.**

`engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:735-747`:

```
if (target.Type == TargetType.Invalid)
{
    ambushPreAimTarget = Target.Invalid;
    if (stage3)
        // SPRUNG is terminal until stance reset (design §5.2), so DO NOT clear ambushTriggered
        // here — only clear the tracking counters. …
        ResetStage3Tracking();
    else
        ambushTriggered = false;
    return;
}
```

The `else` is the stock path — the only path a human unit ever takes, since `stage3` requires
`enable-ambush-tactics`, which no human ever carries (§2.1). **Humans re-arm; bots stay sprung, on
purpose.**

### Root cause — an engine docstring that is false in its own file

Twelve lines below, `ResetAmbushState`'s summary comment reads:

> *"This is the **ONLY** path that clears the terminal SPRUNG latch (ambushTriggered)"*
> — `AutoTarget.cs:996-998`

It is not. `:746` also clears it. **Two independent research strands read the comment, did not read
the branch above it, and reported the comment as behaviour.** The audit compounded it by placing the
claim in the section explicitly about the human path — the one path where it is false.

### What collapses

Per the claim extraction, failure-modes' **F2, F5(a), F8 and F13** all rest on the terminal reading.
F13's headline — that the AI has a re-arm affordance the player lacks — inverts. Any implementation
plan that proposes "give humans a way to re-arm the ambush" is solving a problem that does not
exist, and would be **removing** the deliberate bot-side behaviour if applied uniformly.

---

## 1.4 Is there an aim delay? — YES, and the outranking document says no

| Source | Claim |
|---|---|
| `260820-coordinated-ambush.md` | `Armament.AimingDelay` is **15 ticks on infantry, 30–50 on vehicles**, charged in full after the spring; the Ambush tooltip's *"zero aim delay"* is **false** |
| `260820-ambush-cover-detection-audit.md` §2.5 | **"Aim delay — there isn't one, for infantry."** "The one real shot-timing delay is `Armament.FireDelay` (default 3 ticks)" |
| `260820-ambush-player-loop.md` | "no turret-turn delay — the alpha strike lands as one volley" |

**Verdict: the coordinated-ambush strand is right.**

- `Armament.cs:45` — `public readonly int AimingDelay = 15;`
- `Armament.cs:327` — `if (IsReloading || IsWaitingBurst || IsAiming || IsTraitPaused)` inside
  `CanFire`, so a non-zero `AimingDelay` **blocks the shot**.
- `Armament.cs:677` — `IsAiming => AimingDelay > 0`.
- `Armament.cs:289-290` — decremented one per tick.
- `Armament.cs:347-350` — **reset to the full value whenever the target changes**, which is exactly
  what happens when the trap springs.
- Vehicle overrides 30–50: `vehicles-america.yaml:387,635,761,1056`;
  `vehicles-russia.yaml:216,458,576,702,963`. **No infantry override exists**, so infantry take the
  15-tick default — 0.9 s at `Timestep: 60`; vehicles 1.8–3.0 s.

The audit found `FireDelay` and stopped looking. README §5 records that *another* worker made and
then self-corrected precisely this error before reporting. The audit made it and did not.

The audit's separate point — that infantry `FacingTolerance` defaults to 512 (360°), so pre-aiming
buys no *facing* advantage — is correct and unrelated. Both facts are true; only the conclusion
drawn from them is wrong.

---

## 1.5 Is the volley simultaneous? — no, and the audit is off by a factor of four

| Source | Claim |
|---|---|
| `260820-ambush-player-loop.md` | `TriggerNearbyAmbushAllies` "fires the whole group **on the same tick**" |
| `260820-coordinated-ambush.md` | Sets a latch and **never makes any ally shoot**; scans re-arm at **16–32 ticks**; volley smears over **0.96–1.92 s** |
| `260820-ambush-cover-detection-audit.md` §2.2 | "scans are re-armed to a random **3–8 ticks** (`:199,202,1157`)" → "spread over a few ticks" |

**Verdict: the coordinated-ambush strand is right on both halves.**

`AutoTarget.cs:976-993` — `TriggerNearbyAmbushAllies` sets `allyAutoTarget.ambushTriggered = true`
and nothing else. It never calls `Attack`. The player-loop strand's "same tick" is wrong.

On the interval: the audit cites `AutoTarget.cs:199,202` — which are the **engine defaults**
(`MinimumScanTimeInterval = 3`, `MaximumScanTimeInterval = 8`). The mod overrides them on
`^CamoSoldier` at `infantry.yaml:289-290` to **16 / 32**, inherited by every soldier including the
Rifleman, Sniper and SF. At `Timestep: 60` ms that is **0.96–1.92 s**, not 0.18–0.48 s.

---

## 1.6 A latent bug both sides of the dispute half-saw

`AutoTarget.cs:984-991`:

```
if (allyAutoTarget != null && allyAutoTarget.Stance == UnitStance.Ambush && !allyAutoTarget.ambushTriggered)
    allyAutoTarget.ambushTriggered = true;

// Also trigger garrisoned buildings in Ambush stance
var gm = ally.TraitOrDefault<GarrisonManager>();
if (gm != null)
    gm.TriggerAmbush();
```

**The stance check gates only the latch assignment. The garrison call has no stance check at all** —
the comment claims one the code does not apply. Any garrisoned building within
`AmbushCoordinationRadius` (10 cells) is force-triggered regardless of its stance. The
coordinated-ambush strand flagged this; failure-modes described the same call and did not.
**Confirmed from code — this is a real defect, and it is the third instance in this file of a
comment asserting behaviour the adjacent code does not implement** (see also §1.3 and §3.3).

---

## 1.7 Icon or word? — the two design strands recommend opposite things

Not settleable from code; recorded because the programme currently contains two mutually exclusive
recommendations and no one has noticed.

- **`260820-ambush-legibility.md`** recommends *"one new glyph, not six"* — a white `!` — and
  explicitly says **"Do not glyph"** for four of six states, putting the detail in a
  selected-unit ledger. "Six states, one new glyph. **The economy is the design.**"
- **`260820-ambush-cover-genre-survey.md`** §9 recommends the opposite: *"**Name the state in
  words, on the unit** … **Not an icon**, not a tint, not an overlay"*, and calls it *"the single
  best idea"*, with *"Everything else in this list is secondary to this."*

Three specific collisions, all of which the survey itself supplies the ammunition for:

1. **Channel.** The survey names the existing selection-only concealment ring as an instance of the
   genre's dominant failure: *"a legibility feature at alpha 25 on selection-only is one a player
   can play for fifty hours without ever consciously seeing"* (§6i, §9). The legibility strand's
   centrepiece — Option B item 3, the concealment ledger — lives in that same channel.
2. **Count.** The survey's "Avoid #1" is *"ship exactly **one** concealment indicator, at simulation
   granularity"*, on the CoH evidence that two indicators at different granularities teaches players
   which one lies. Legibility's Option B ships **three** (white `!`, gauge tier, panel ledger).
3. **Inference from absence.** Legibility §4 declines to glyph "hidden" because *"absence of red `!`
   already carries it once the player learns the vocabulary"*. The survey's §6c and §6i are two
   sections arguing that relying on the player learning an unstated vocabulary is what kills these
   systems — and legibility's own §3 identifies the same ambiguity (absence of `!` also means "no
   enemy anywhere near me") and solves it *for the white `!`* while leaving it standing for state 1.

**My reading:** the survey is the stronger argument and it is better evidenced — ten games, with a
self-declared uncertainty ledger. The legibility strand's economy argument is sound *given* its
premise that the medium must be a glyph, and it never tests that premise. But this is a user
decision, and it should be put as one.

---

## 1.8 Does the audit outrank the other eight?

README §4 instructs: *"treat it as outranking the other eight wherever they disagree."*

**That instruction is wrong as stated, and following it would have propagated four errors.** §1.1,
§1.3, §1.4 and §1.5 are all cases where the audit is wrong and a strand it outranks is right.

The audit's errors are not random. **Every one of them lives in an override layer:**

| Where the audit read | Where the answer was |
|---|---|
| `AutoTarget.cs:199,202` — engine scan default 3–8 | `infantry.yaml:289-290` — mod override 16–32 |
| `Armament.FireDelay` (engine default 3) | `Armament.AimingDelay` (engine default 15, overridden on 9 vehicles) |
| `^SF` / `^SN` template `Prerequisites: ~disabled` | `SF.america` / `SN.america` — `Buildable` overridden |
| base infantry `Detectable.Vision: 3` | Sniper/SF `Vision: 5`, documented in its own §1.5 table |

The audit earned its authority on the husk geometry, which is the one finding that had a second
independent pass — and there it is right and everyone else was wrong. **The correct rule is
narrower than the README's: the audit outranks where it did the second pass. Elsewhere it carries
a systematic bias toward the base layer, and should be checked against the mod's overrides before
being cited.**

---

# 2. What is agreed AND verified

Findings more than one strand reached independently. **Almost nothing here was measured** — I mark
each item individually rather than disclaiming once, because the classes differ.

## 2.1 The widened ambush is gated to bots — *read (grep), independently re-verified*

`enable-ambush-tactics` gates Stage-2 halt-before-contact and the Stage-3 spring table. Grepping
`mods/` and `tools/` for the token returns exactly: the `AutoTarget` field
(`defaults.yaml:320`), the grantor seam `ExternalCondition@ambushtactics` (`defaults.yaml:345`),
two `ai.yaml` bot blocks (`:825-831` `@experimental`, `:2214` `@stable`), and **six autotest Lua
files that grant it by hand**. **There is no human grant path.** I ran this grep myself; it agrees
with the player-loop strand, the audit §2.3, and the blast-radius strand.

**This is the strongest single candidate for the user's original complaint**, and it is a gating
question rather than a design one.

## 2.2 The `object-proximity` cover ladder is dead — *read + derived; premises re-verified, arithmetic not re-derived*

The +1/+2/+3 ladder (`infantry.yaml:707-715`, the largest term in the stack) has one emitter:
`ProximityExternalCondition@ObjectProximity` on `^TreeHusk` (`husks.yaml:118-121`, `Range: 384`),
with 22 per-actor overrides at 182–640 WDist. I re-verified each geometric premise:

- `^TreeHusk` carries `Building: Footprint: x` and **no `Passable`** (`husks.yaml:105-107`) — it
  blocks infantry.
- A living `^Tree` **does** carry `Passable: PassClasses: tree` (`decoration.yaml:12-14`) — walkable.
- Sub-cell offsets are five quantised positions of magnitude ≤ 299/256 WDist
  (`MapGrid.cs:117-125`); the mod does not override them.
- Building `CenterPosition` is the **bounding-box** centre, not the occupied cell
  (`Building.cs:206-210,354`) — so for the `Dimensions: 2,2` husks the trigger point sits at a cell
  corner. I re-derived two cases by hand (`T01.Husk`, `T05.Husk`) and both come out several hundred
  WDist beyond their radii.

**The conclusion is robust. I did not reproduce the audit's exact 244–771 figures**, and my own
hand-derivations differ in detail while agreeing in sign by a wide margin. Nobody has watched a
soldier fail to receive the condition.

## 2.3 Stance does not touch detectability — *read, three strands agreeing*

`stance-ambush` / `stance-holdfire` are granted in five places and consumed by **zero**
`RequiresCondition` sites in `mods/`. The user's complaint is literally true. Note the audit's
correction stands: the *stance* (`UnitStance.Ambush`) is consumed heavily in C#; only the *condition
token* is inert.

## 2.4 Prone gives no damage reduction — *read, agreed by two strands*

`InfantryStates.cs:200-203` applies `ProneDamageModifiers` only against warheads declaring a
matching `DamageTypes` token; commit `1802191e` (2024-02-13) stripped them from every live weapon,
leaving one dead `EmpBomb` line. Prone still shrinks the hitshape (radius 30 → 20), which is a
hit-probability effect, not damage.

## 2.5 Positional damage cover is capped at 20% and is really rock cover — *read + derived*

`DensityModifiesDamage` tiers at 15/30/50 → 94/88/80% (`infantry.yaml:37-45`). One tree contributes
density 10 — **below the floor, so exactly zero**; one rock cell contributes 50 — the full 20%
instantly. Buildings give 80–97%. The shipped comment at `infantry.yaml:41` ("a lone treeline barely
helps") is wrong and the audit correctly flags it.

## 2.6 The benchmark baseline is unusable — *read (git) + inherited measurement*

Newest record `260729`; 188 bot-module commits since, including `b8d2e601`, a deliberate change to
the `@stable` control. The prior ambush A/B (N=10, marker-verified) measured inside the noise band
with the two rungs disagreeing in sign. **The N=10 numbers are inherited from a run card by the
blast-radius strand, not re-measured by anyone in this programme.**

## 2.7 Nothing in this programme was measured — *meta*

All nine strands were doc-only by instruction. `test-case01b-detect` was authored specifically to
measure time-to-first-shot spread and **has never been run**. `test-case01-forest-ambush` is ~1000
commits stale and asserts cost-weighted losses, never simultaneity — **do not cite it as evidence
coordination works**, notwithstanding that the player-loop strand leans on its numbers throughout.

---

# 3. What everyone assumed and nobody checked

The cover-ladder error had a specific shape: *every party checked who grants the condition; nobody
checked whether you can reach it.* These are its siblings.

## 3.1 The maximum was never recomputed with all live terms at once

Four documents contain per-modifier tables. **Not one multiplies out the maximum using every live
term simultaneously.** The audit lists `rank-veteran` as the largest live modifier *and* lists the
Sniper's base 5, in the same section, and combines neither. The legibility strand omits rank
entirely. Recomputing the ceiling from the union of the tables everyone had already written takes
about a minute and inverts a user ruling (§1.1).

## 3.2 Nobody checked the recorded ruling against the code it rules on

The ruling "clamp minimum detectability to 1" was recorded, sequenced against the cover-ladder
repair, and carried into a live open question — **without anyone opening `Detectable.cs` to see that
the clamp is already there** (`:90-91`), or noticing that a floor at 1 on both sides of a strict
comparison is a mechanism for invisibility rather than a cure for it (§1.2). The ruling was
translated into the codebase's vocabulary and inverted in the process.

## 3.3 Comments were read as ground truth, three times

- `AutoTarget.cs:996-998` — `ResetAmbushState` claims to be "the ONLY path that clears the terminal
  SPRUNG latch". **False in its own file** (`:746`). Two strands inherited it (§1.3).
- `AutoTarget.cs:987` — "Also trigger garrisoned buildings **in Ambush stance**". **The code applies
  no stance filter** (§1.6).
- `infantry.yaml:41` — "a lone treeline barely helps". **It helps zero.** The audit caught this one,
  which shows the check is cheap when someone does it.

**In all three the comment is adjacent to the code that contradicts it.** A grep that returns a
comment is not a finding.

## 3.4 The observer side of the comparison was never modelled

Every CV table in every document is a table of *target* properties, with the observer represented
only as a fixed `^StandardVision` ladder. The forest shadow term subtracts from the **observer**
(`MapLayers.cs:371`), and the audit is the only document that mentions it — in prose, in two
sections, without folding it into its own ladder table, and it renders the subtraction as taking the
observer "to 0" when the code floors it at 1 (`:373-374`). That floor is the whole finding (§1.2).
The shipped concealment gauge cannot show this either: it is viewer-independent by construction, so
**every ring the game draws is a no-forest special case.**

## 3.5 Template versus fielded actor

§1.1's SF/Sniper error. `CLAUDE.md`'s first hard rule warns about exactly this class ("verify how
WW3MOD actually uses a system before trusting old logic"), and the trap still caught the document
designated as the most rigorous. Worth a standing check: **in this mod, if a conclusion depends on a
`Buildable` block, it is probably wrong.**

## 3.6 Engine default versus mod override

§1.4 and §1.5. Two of four audit errors are this. The mechanical countermeasure is cheap: after
reading a default in `engine/`, grep `mods/` for the field name before quoting the number.

## 3.7 Line-number drift is pervasive and nobody reconciled it

The claim extraction surfaced eleven cases where two documents cite different line ranges for the
same construct (`ConcealmentScore` at `:330-346` vs `:338-346`; the `Building.cs` shadow recalc at
`:372-397` vs `:377-396`; the tooltip at `:361-369` / `:373` / `:372-373`; `^StandardVision` at
`:47-84` vs `:47-90`). None is individually load-bearing. Collectively they mean **citations in this
programme are approximate**, and a reader who greps a cited line and finds something else should
widen the search rather than conclude the claim is false.

## 3.8 One unchecked premise nobody has retired

The coordinated-ambush and predictive-detection strands both rest on **a paused `Mobile` reporting
`CurrentMovementTypes == None`**, and therefore revoking `moving` and granting `prone`. The
predictive-detection strand flags this explicitly as *"the crux of the whole design and the first
thing a run must confirm"*. **It is still unconfirmed.** Every "stopping conceals for free" claim in
the programme depends on it.

---

# 4. The ordered plan

The README's §8 order is *(1) synthesis, (2) run `test-case01b-detect`, (3) decide the bot gate,
(4) re-baseline*, with a §7 sequencing rule that the visibility clamp lands **before** any cover-ladder
repair. **§1.1 and §1.2 break that sequencing rule's premise**, so the order below differs at the top
and converges with it after.

### Step 1 — Re-put the invisibility ruling to the user. *Blocking; nothing else should start.*

The ruling on the books is a no-op (§1.2) and it was made on the belief that the cliff was latent
(§1.1). Both are false. The user should be told three things and asked one question:

1. Total invisibility is **live today**, on Sniper and Special Forces at rank 3+ (§1.1).
2. There is a **second and wider route** through forest shadow that affects every unit including a
   plain stopped rifleman, and that route is unaffected by any clamp (§1.2).
3. The clamp they asked for **already exists** and is part of the mechanism.

The question is which fix surface they want, because they differ in blast radius: a strength-11
vision band (narrow, fixes §1.1 only), a CV ceiling of 9 (narrow, fixes §1.1 only), flooring
observer strength at 2 (fixes §1.2, shifts nothing else), or changing `>` to `>=` (fixes both,
**shifts every detection radius in the game by one band** — almost certainly too blunt).

**Why first:** every downstream decision is priced off it, and the README's instruction to land the
clamp before repairing the cover ladder would, if followed as written, produce a commit that changes
nothing and then a cover-ladder repair that ships the exploit anyway. That is the exact outcome the
sequencing rule was written to prevent.

### Step 2 — Correct the programme's own record. *Cheap, no user input needed.*

The README currently states as fact several things §1 shows are false: that CV tops out at 9, that
the cliff is latent, that the audit outranks the other eight unconditionally, and (via the audit)
that the human latch is terminal and there is no aim delay. **Four workers have now been dispatched
against wrong premises in this project in a fortnight.** Amending the README costs minutes; leaving
it costs the next dispatch.

### Step 3 — Decide the bot-only gate. *User decision, unblocked, highest value per unit of effort.*

§2.1 is verified by four independent parties and by my own grep. It is the strongest candidate for
the user's original complaint, and it is one condition
(`GrantConditionOnHumanOwner@ambushtactics`, mirroring the shipped `@tacpos` idiom at
`defaults.yaml:40-45`). It needs the user's sign-off because it is a behaviour change to a
default-off gate, and because `AmbushMinSpringThreshold` / `AmbushHighSpringThreshold` have never
been tuned against human play.

**Sequencing note:** this is independent of Steps 1–2 and could be decided in the same conversation.

### Step 4 — Spend the two cheap runs, together. *After Step 3, before any implementation.*

Two runs settle disproportionate amounts, and both are single scenarios:

- **`test-case01b-detect`** — authored for exactly this, never run once. Settles §1.5's volley
  spread directly and would confirm or refute the 16–32-tick derivation.
- **A new one-soldier concealment scenario** — a soldier behind 6+ dense tree cells, an observer
  adjacent, assert visibility. Settles §1.2 empirically, which is the finding with the largest
  consequence and the thinnest evidence (five code sites, zero observations).

Also worth folding in: the audit predicts `test-visual-gauge-truth` and
`test-visual-concealment-gauge` both fail their own premise checks (both omit `prone`). One run of
either settles it, and their output should not be trusted before then.

**Per `DOCS/recipes/AUTOTEST.md` and the standing rule: budget the RED run for each.** Runs
serialize through the manager; ask for the slots explicitly.

### Step 5 — Re-take the `@stable` baseline. *User-gated, ~60–80 min, before any ambush measurement.*

Unchanged from README §8. 188 bot-module commits including a deliberate change to the control
(§2.6). No ambush measurement means anything until this lands.

### Step 6 — Only then, the design fork: icon or word (§1.7).

A user decision between two well-argued and incompatible recommendations. It should not be taken
before Steps 1 and 3, because what the readout has to *say* depends on whether the behaviour gate
opens and on how invisibility is resolved.

### Explicitly NOT in this plan

- **Repairing the cover ladder (§2.2).** It is a real defect, but repairing it widens §1.1 from two
  units to all infantry. It must not land before Step 1 resolves.
- **Anything on the hunt phase, Take Cover, or the white `!`.** The user's standing instruction is
  that nothing on stances, ambush, concealment or cover is built until they say so.
- **Dogs.** Correctly parked in the README, and §1.2 strengthens the case for parking: if
  invisibility has a terrain route that no detector unit can counter without a rule change, dogs
  cannot be the answer to it.

---

## 5. What I could not settle, and where I may be wrong

- **§1.2 is the finding I would most like a second pass on.** It is six code sites and no
  observation, and it is the one that most changes what the user is told. I did go back and read the
  population path (`Map.RecomputeShadowFrom`), which is what produced the two qualifications now in
  §1.2 — the 2–32 cell annulus and the baked `shadows.bin`. The residual risk is the cache: if
  shipped maps carry a `shadows.bin` generated under an older curve, the live numbers differ from
  the ones I derived. **The mechanism (dual floors plus strict `>`) does not depend on the cache and
  stands regardless; the depth threshold at which it bites does.**
- **I did not re-derive the audit's husk distances** (§2.2). My two hand-worked cases agree in sign
  by a wide margin, so I am confident in the conclusion and not in the numbers.
- **§1.7 is a judgement, not a finding.** I said the survey has the better argument; that is my
  reading of two documents, and the user may reasonably prefer the glyph economy.
- **The claim extraction for six of the nine documents was done by subagents**, and I have their
  claim tables rather than the full texts of `player-loop`, `failure-modes`,
  `cover-protection-and-take-cover`, `predictive-detection` and `bot-blast-radius`. Every
  contradiction I adjudicate in §1 I verified against code myself, but **there may be contradictions
  in those five documents that the extraction did not surface and I therefore never saw.**
- **`test-case01b-detect` has still never been run**, so §1.5's volley figure remains arithmetic —
  better-sourced arithmetic than the audit's, but arithmetic.
