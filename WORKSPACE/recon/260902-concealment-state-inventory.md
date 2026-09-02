# Concealment / detection state — what exists, and what nothing renders

**Date:** 2026-09-02 · **Branch:** `wt/concealment-readout` · **Base:** `main @ 6a7e1839`, 0 commits
behind `origin/main` (verified at start of pass).
**Status:** read-only. No code changed, no YAML changed. **Game never launched**; `run-test.sh`,
`make test` and `./utility.sh --check-yaml` were never invoked.

**Scope:** taking state the simulation *already computes* and showing it to the player. Explicitly NOT
in scope: new detection curves, new concealment states, tuning the stealth maths. Per the user's
standing position — *"Ambush work is a legibility problem, not a mechanics one."*

**Every `file:line` below was opened and read during this pass.** Two subagents were used for breadth;
their citations were re-derived by hand before being written down, and one of their conclusions was
wrong and is corrected in §2.

---

## 1. The state inventory

The question each row answers: *does the simulation maintain this, and does anything draw it?*

| # | Quantity | Where it lives | Range / units | How it updates | Rendered today? |
|---|---|---|---|---|---|
| 1 | **Concealment level** (`CurrentVisibility`) — observer strength required to reveal this actor | `Modifiers/Detectable.cs:73` `[Sync] public int CurrentVisibility` ; computed `:129-131` | **[1, 9]** — clamped by `ClampConcealment` (`:118-125`), ceiling `MapLayers.VisionLayers - 2`, `VisionLayers = 11` (`MapLayers.cs:75`) | every tick, `ITick.Tick` (`:127-138`) | **YES** — grey ring, see row 2 |
| 2 | **`visibility-N` condition** — token form of row 1 | granted `Detectable.cs:228`; declared `:52-54` | one of `visibility-1` … `visibility-9` | re-granted on change only (`:133-137`) | **YES** — `^DetectableRangeCircles`, `infantry.yaml:814+`, ten `WithRangeCircle` traits, `Visible: WhenSelected`, `Color: 888888`, `Type: concealment` |
| 3 | **Per-observer detection margin** — "does *this* enemy's band still carry enough strength at my cell" | `Render/WithSpottedDecoration.cs:82-119`; band test `VisionCovers` `:134-157` | per observer: `visionInfo.Strength` vs `required`, plus range/minrange | recomputed every `RecalculationInterval = 7` ticks (`:33`), cached (`:71-77`) | **NO — computed, then collapsed to `bool`.** The loop returns `true` on the first observer that covers (`:116`) and the graded margin is discarded. The only output is a binary red `!` |
| 4 | **Foliage on the firing line** (`groundShadow` / `airborneShadow`) | `FiringLOS.cs:104` `var (groundShadow, airborneShadow) = map.ShadowLayer[lookupFrom, lookupTo]` | **byte, 0–255**, precomputed per cell-pair for 2–32 cells (`:71-80`) | precomputed at map load; static thereafter | **NO.** Zero consumers under `Traits/Render/` (grep `ShadowLayer` → no files). Consumers are all decision-path: `Armament.cs:429`, `AutoTarget.cs:1440`, `AttackBase.cs:270`, `AttackFollow.cs:466`, `Attack.cs:280`, `MapLayers.cs:363-365` |
| 5 | **Per-weapon LOS threshold** (`ClearSightThreshold`) — the number row 4 is compared against | declared `WeaponInfo.cs:148` *"Maximum shadow value (0-255) from the ShadowLayer that still allows this weapon to fire"*; compared `Armament.cs:429` | 2 … 255 in shipped content | authored, static | **NO.** All **15** occurrences in `mods/` are in `weapons-ballistics.yaml` / `weapons-missiles.yaml`. Zero in chrome, zero in any `rules/` UI template |
| 6 | **Aim countdown** (`Armament.AimingDelay`) | `Armament.cs:220` `public int AimingDelay { get; protected set; }`; default `:101` = **15**; decremented `:354-355`; re-armed on target change `:415` | ticks; 15 infantry default, 30–50 on vehicles (`vehicles-america.yaml:396,646,802,1119`, `vehicles-russia.yaml:218`) | per tick while > 0 | **NO.** `IsAiming` at `:751` is read by nothing outside `Armament`; `AttackFollow.IsAiming` (`:194-226`) is a *different, unrelated* boolean on the attack base |
| 7 | **Ambush latch** (`AmbushSprung`) — has this ambusher already fired / been triggered | `AutoTarget.cs:389` `public bool AmbushSprung => ambushTriggered`; set `:688`, `:776`; cleared only on stance change `:758` | bool; **terminal** until stance reset (`:751` comment) | event-driven | **NO.** Grep for `ambushTriggered\|AmbushSprung` under `Traits/Render/` and `mods/` → **zero files**. The property was deliberately exposed as a read-only view and has no reader |
| 8 | **Cover ladder** (`object-proximity` → `+1/+2/+3`) | `ExternalCondition@ObjectProximity`, `infantry.yaml:759-761` (`TotalCap: 3`); consumed `:762-770` | 1 / 2 / 3 | granted by `ProximityExternalCondition` on `^TreeHusk` | **N/A — the terms exist but are geometrically unreachable.** Not re-derived this pass; see §2c |
| 9 | **Posture terms** — prone +1, dugin +1, moving −1, firing −2 | `infantry.yaml:771-787` | additive into row 1 | condition-driven | **Indirectly** — they move the row-2 ring, but nothing attributes the ring's size to a cause |
| 10 | **Suppression level** | `suppressed` condition; consumed `infantry.yaml:434-442` (speed), `:465-468` (vision) | 0–100, bucketed in tens | per tick | **YES** — ten `WithDecoration@Suppression_N`, `infantry.yaml:589-596+`, `Sequence: pip-suppression-1..10`, `RequiresSelection: true`. **This is the "severity without trend" precedent** |
| 11 | **Spotted flag** | `WithSpottedDecoration`, attached `defaults.yaml:889-894` (`Text: !`, `Color: FF4A3C`, `RequiresSelection: false`) | bool | every 7 ticks | **YES** — binary |
| 12 | **Stance** (`HoldFire/Ambush/FireAtWill`) | `AutoTarget.cs:22` | enum | on order | **YES** — `WithStanceDecoration@Fire` / `@Engagement`, `defaults.yaml:895-903` |

### What does NOT exist — stated plainly so nobody proposes a readout for it

- **There is no predictive detection quantity.** Grepped `engine/**/*.cs` for
  `WillBeSpotted|AboutToBe|PredictDetect|ImminentDetect|SpotRisk|DetectionRisk|DetectionMargin|ConcealmentMargin`
  → **zero files**. Row 3 is the closest thing that exists, and it is a per-frame render-path
  computation, not stored state. Any "about to be seen" readout must derive it in the render path —
  which is fine, and is what proposal P1 does.
- **There is no per-unit record of *who* saw me, or *when*.** `IsSpotted` (`:82-119`) enumerates
  observers and returns on the first hit; nothing is retained. "Why did my ambush fail" cannot be
  answered from stored state today — only re-derived live.
- **There is no aim/spring event log.** Row 7 is a bool with no timestamp.

---

## 2. Corrections to existing documents

### (a) The 9-vs-10 concealment cap — settled: **the cap is 9, by a hard code ceiling**

`Detectable.ClampConcealment` (`Detectable.cs:118-125`):

```csharp
var ceiling = MapLayers.VisionLayers - 2;
return concealment > ceiling ? ceiling : concealment;
```

`MapLayers.VisionLayers = 11` (`MapLayers.cs:75`), so the ceiling is **9**. Introduced by
`1ad638e7` *"Reserve the top vision level for observers: concealment now ceilings at 9"*, and pinned by
a unit test, `engine/OpenRA.Test/OpenRA.Mods.Common/DetectableCeilingTest.cs:25`.

The shipped YAML comment already says so, and is correct (`infantry.yaml`, above
`^DetectableRangeCircles`): *"visibility-0, -11 and -12 therefore CANNOT be granted… **NOR CAN
visibility-10**… The @Detectable10 / 4c ring below is therefore **DEAD YAML TODAY** — kept
deliberately, because `Detectable.cs:49-51` keeps level 10 declared so this ring survives a revert of
the ceiling."*

**Both disputing documents are wrong, in different ways, and this is exactly the trap the brief
warned about:**

- `WORKSPACE/ambush-programme/README.md:119` says CV tops out at 9 **because the cover ladder is
  dead**. Right number, wrong reason — today it is 9 because of the clamp, and it would still be 9 if
  the cover ladder were repaired tomorrow.
- `WORKSPACE/ambush-programme/260820-synthesis.md:66-74` says the clamp is `[1,10]` and that CV 10 is
  reachable by a rank-3 sniper (`5+1+1+3`). The **arithmetic** is right and the **conclusion is
  stale**: `ApplyAddativeModifiers` may well produce 10, but `ClampConcealment` truncates it to 9
  before it is assigned or granted. This document predates `1ad638e7`.

A subagent tasked with this question reported the synthesis doc as "ACCURATE" and the README as
"STALE". That is backwards on the number and should not be repeated: **read `Detectable.cs:118-125`.**

### (b) Reveal is **not** strictly greater any more

`MapLayers.IsDetected` (`MapLayers.cs:600-603`):

```csharp
return resolvedVisibility >= (concealment < 2 ? 2 : concealment);
```

`>=`, not `>`, changed by `1ff73ae5` *"Reveal is non-strict: a matching observer detects"*. The
2026-08-20 audit's §1.2/§1.5 assert a strict compare and derive the ring ladder from band *N+1*. The
shipped YAML comment flags this explicitly as a trap: *"this ladder reused band N+1's Range while the
comparison was strict, and every circle moved out one band (~3 cells) when the comparison did. Do not
'restore' the old numbers without also restoring the strict compare — they are one fact, not two."*
Anyone reading the audit's distance table should treat it as superseded.

### (c) Not re-derived this pass

The `object-proximity` / tree-husk geometry (row 8) is taken from
`WORKSPACE/recon/260820-ambush-cover-detection-audit.md` §1.4 **unverified by me**. I flag it because
the brief warns three parties got this wrong by checking the grant and not the geometry — I did not
check the geometry either, so I am not adding a fourth opinion. It does not affect any proposal below.

---

## 3. Proposals, ranked

One candidate was **dropped after investigation: "show the player how concealed he is."** That ships.
`^DetectableRangeCircles` (`infantry.yaml:814+`) draws a grey ring at the detection radius for the
selected unit, driven by `visibility-N`. It is live, attached, and its tier→radius ladder is derived
from code. Do not propose it again.

---

### P1 — "Someone is about to see you" (the ring goes amber before it goes red)

1. **What the player experiences.** Today a red `!` snaps on the instant an enemy sees his soldier —
   there is no *before*. With this, as an enemy closes on a hidden squad the concealment ring warms
   from grey through amber and starts to pulse; the closer the nearest watcher gets to the strength it
   needs, the faster it pulses. The player gets roughly three cells of warning to stop moving, and can
   see whether the situation is getting worse or recovering when the enemy turns away. The red `!` is
   unchanged and still means *seen*.
2. **Which existing state.** The per-observer margin already computed in
   `Render/WithSpottedDecoration.cs:82-119`. `var required = detectable != null ?
   detectable.CurrentVisibility : 1;` (`:93`), then `VisionCovers(observer, self, required)` (`:107`)
   loops the observer's `Vision` traits testing `visionInfo.Strength < requiredStrength` (`:139`) and
   range (`:146`). **The margin `strength − required` and the range slack are both in hand at `:139-147`
   and thrown away** — the method returns `bool` (`:153`).
3. **Tier.** `SAFE WIN`. The expensive part — the spatial query, the observer loop, the asymmetry
   gate, the cadence cache — is already written and already runs.
4. **Shape.** Return the best margin instead of a bool from `VisionCovers`/`IsSpotted`, and drive
   `WithRangeCircle`'s colour/alpha from it. `WithRangeCircle` already implements
   `IRenderAnnotationsWhenSelected` and already groups by `Type: concealment`. Stays entirely in the
   render path.
5. **Honest risk.** (i) **The asymmetry rule must survive.** `WithSpottedDecoration`'s `[Desc]`
   (`:20-22`) is explicit: an enemy that can see you but that *you* have not spotted must not light the
   mark, *"it would be a wallhack"*. A graded warning is a strictly stronger information leak than the
   binary one, so the `observer.CanBeViewedByPlayer(viewer)` gate (`:105`) must be kept and the
   proposal must be reviewed as a wallhack question, not a UI question. (ii) **Desync.** The same
   `[Desc]` records that driving this from a granted condition *"is the shape of two shipped desyncs in
   this repo"*. Must stay render-only. (iii) **Noise:** a pulsing ring on every selected soldier in a
   firefight could be visual soup — mitigate by only pulsing for units not yet spotted.
6. **Proof it does not exist.** `VisionCovers` returns `bool` (`WithSpottedDecoration.cs:134`,
   `:153`); the only consumer is `cachedSpotted` (`:75`), a `bool` field (`:56`). Grep of
   `engine/**/*.cs` for `WillBeSpotted|AboutToBe|PredictDetect|ImminentDetect|SpotRisk|DetectionRisk|DetectionMargin|ConcealmentMargin`
   → **zero files**. No trait implements `ISelectionBar` for anything detection-related.

---

### P2 — "Your ATGM can't shoot through that treeline"

1. **What the player experiences.** He parks a missile team behind trees, orders a shot, and the unit
   just… doesn't fire, with no explanation. With this, the target cursor tells him the shot is blocked
   by foliage before he commits, and the unit shows a small "no clear shot" mark while it is holding a
   target it cannot engage. Crucially it is per-weapon: the same vehicle's chaingun can be shown as
   clear while its ATGM is shown as blocked.
2. **Which existing state.** The precomputed foliage value on the firing line and the per-weapon
   threshold it is compared against. `FiringLOS.cs:104`: `var (groundShadow, airborneShadow) =
   map.ShadowLayer[lookupFrom, lookupTo];` — compared in `Armament.FireBarrel`'s gate,
   `Armament.cs:429`: `if (!FiringLOS.HasClearLOS(self, target, Weapon.ClearSightThreshold)) return
   null;`. Threshold declared at `WeaponInfo.cs:148`.
3. **Tier.** `AMBITIOUS` — not because the state is hard to get (it is `O(1)` and already cached), but
   because the cursor/targeting seam is a different and less-travelled part of the codebase than a unit
   decoration, and "which armament does the cursor speak for" is a real design question.
4. **Shape.** Two halves, separable. The cheap half is a `WithDecoration` on the unit gated on a
   render-path "holding a target I cannot shoot" test — reuses existing decoration machinery. The
   expensive half is the target cursor, which needs the order-generator seam and has no existing
   per-weapon precedent.
5. **Honest risk.** (i) The brief flags this as *"very likely what players actually perceive as cover
   working"* — that is a **hypothesis**, and this pass did not confirm it; confirming it needs play,
   not code. (ii) Cursor feedback that is wrong is worse than none, and `HasClearLOS` has three
   early-out paths that make it *permissive* — `IndirectFire` units always return `true` (`:49-51`),
   under 2 cells always `true` (`:78-79`), and beyond 32 cells it falls back to a different check
   entirely (`:81-83`). A readout must reproduce all of them or it will lie at close range.
   (iii) Adjacency: PIPELINE item 71 names `ClearSightThreshold` as the real cover mechanism. **Item 71
   is a behaviour/design item; this is a readout and does not overlap its content** — but they should
   be sequenced by whoever owns 71.
6. **Proof it does not exist.** `grep -rn ClearSightThreshold mods/` → **15 hits, all in
   `weapons-ballistics.yaml` and `weapons-missiles.yaml`**; zero in chrome, zero in any UI template.
   `grep -rln ShadowLayer engine/OpenRA.Mods.Common/Traits/Render/` → **no files**. Every
   `HasClearLOS` call site is a decision path (`Armament.cs:429`, `AutoTarget.cs:1440`,
   `AttackBase.cs:270`, `AttackFollow.cs:466`, `Attack.cs:280`) — none is a render path.

---

### P3 — "Your ambush is still holding" / "it has sprung"

1. **What the player experiences.** A squad set to Ambush currently shows a gold `A` whether it is
   lying in wait or has already fired everything and is in a normal firefight. With this the glyph
   distinguishes the two: armed-and-waiting versus sprung. The player can see at a glance which of his
   ambushes are still live, and — because the latch never clears on its own — that a sprung squad needs
   its stance re-set to re-arm it.
2. **Which existing state.** `AutoTarget.cs:389`: `public bool AmbushSprung => ambushTriggered;`,
   documented at `:383` as a *"read-only view of the internal `ambushTriggered` latch. SPRUNG is
   terminal"*. Set at `:688` (damage-triggered) and `:776` (detection-triggered); cleared only at
   `:758` on stance change.
3. **Tier.** `SAFE WIN`. The property exists, is already public and read-only, and
   `WithStanceDecoration` is already attached to every combat unit (`defaults.yaml:895-903`).
4. **Shape.** A second glyph state on the existing `WithStanceDecoration@Fire`, or a sibling
   `WithDecoration` gated on the latch. No new state, no new interface.
5. **Honest risk.** (i) `ambushTriggered` is deliberately **not** `[Sync]` (`AutoTarget.cs:410`
   says so) — so this must be render-only, same discipline as P1. (ii) Low information value if
   the player never noticed the latch was terminal; its value is mostly in *teaching* that, which is
   real but modest. (iii) It makes an existing rough edge more visible rather than fixing it, and the
   fix is PIPELINE item 70's territory — if 70 lands and changes spring semantics, this readout must be
   re-checked.
6. **Proof it does not exist.** `grep -rn "ambushTriggered\|AmbushSprung"` across
   `engine/OpenRA.Mods.Common/Traits/Render/` and `mods/` → **zero files**. All twelve hits in the
   engine are inside `AutoTarget.cs` itself. The property has no reader anywhere.

---

### P4 — "They're aiming, not asleep"

1. **What the player experiences.** After an ambush springs, or after any unit is given a new target,
   there is a beat where nothing happens and the unit looks broken. With this, a short bar under the
   unit fills while it brings its weapon on — and because vehicles take two to three times as long as
   infantry, the player learns which of his units are slow to commit. He can see the pause is the game
   working, not the game hanging.
2. **Which existing state.** `Armament.cs:220`: `public int AimingDelay { get; protected set; }`,
   default `15` at `:101`, decremented each tick at `:354-355`, re-armed on target acquisition at
   `:415`. Shipped overrides are vehicle-only: `AimingDelay: 50` (`vehicles-america.yaml:396`), `35`
   (`:646`), `30` (`:802`), `40` (`:1119`), `50` (`vehicles-russia.yaml:218`).
3. **Tier.** `SAFE WIN`, and the cheapest of the four.
4. **Shape.** A new `ISelectionBar` implementation. **An existing interface fits exactly** —
   `ReloadBar` (`Traits/Render/ReloadBar.cs`) already shows "minimum remaining reload across named
   armaments" as a 0..1 bar; an aiming bar is the same trait with a different numerator. `ISelectionBar`
   is `{ float GetValue(); Color GetColor(); bool DisplayWhenEmpty; }` (`TraitsInterfaces.cs:301`) and
   bars stack, so this needs no new UI machinery at all.
5. **Honest risk.** (i) **This is the closest of the four to an existing pipeline item.** PIPELINE item
   70 covers *"the tooltip promises a zero aim delay that does not exist"* — that item's content is
   fixing the promise and the spring timing; this is showing the countdown. They are compatible but
   they touch the same fact, and 70 is user-gated. **Flag before starting.** (ii) Bar clutter: units
   already stack health plus any other `ISelectionBar`; a fourth row on every selected unit may be too
   much. Gate it on `AimingDelay > 0` via `DisplayWhenEmpty: false`. (iii) Modest felt value on
   infantry, where the delay is 15 ticks (~0.9 s at `Timestep: 60`).
6. **Proof it does not exist.** `grep -rn "IsAiming\|AimingDelay" engine --include=*.cs` excluding
   `Armament.cs` returns **only** `AttackFollow.cs:190-226` and the Cnc `Leap*` activities — all of
   which manipulate `AttackBase.IsAiming`, a **different boolean**, and none of which render. No
   `ISelectionBar` implementation references an armament aim state. `grep -rn AimingDelay mods/`
   returns **only** vehicle stat overrides.

---

## 4. The one run that would settle a question this pass could not

The brief asked me to verify, not repeat, the claim that `test-case01b-detect` was authored to measure
spring timing and has never been run. **Verified, with one important correction.**

- The scenario exists: `tools/autotest/scenarios/test-case01b-detect/`, titled *"CASE-01B — Forest
  ambush, DETECT variant (fire-lane measurability)"*.
- It measures per-defender fire-lane metrics — did each defender ever fire, ticks-to-first-shot, shot
  counts, casualties.
- **Correction to the framing:** it does **not** assert a verdict. It ends in `Test.Skip(note)`
  (`test-case01b-detect.lua:273`) and says so in its own header — *"The value is the fire-lane metrics,
  not a pass/fail verdict."* So it is a **measurement instrument, not a test**, and "it has never been
  run" means the measurement has never been taken, not that a test is failing.
- **No run artifacts found** under `tools/autotest/runs/` (does not exist) or anywhere in `WORKSPACE/`.
  Its git history is three commits — authoring (`4846a60a`), a logging addition (`44c2b513`), and a
  bulk `Test.Skip` sweep (`3b901473`) — none of which record a run.

**The command, for whoever holds the launch slot:**

```
./tools/autotest/run-test.sh test-case01b-detect
```

**What would count as the answer.** Read `result.json` from the run directory — *not* piped through
`tail`. The scenario reports, per defender, ticks-from-attacker-launch to that defender's first shot.
The number that matters is the **spread** across the five defenders. If the spread is under ~10 ticks,
the coordinated spring reads as a volley and P3's premise is weak. If it is 30+ ticks (~2 s), the
spring is perceptibly ragged, which independently corroborates PIPELINE item 70 and makes P4's aiming
bar more valuable. **This is currently unmeasured — the 16–32 tick figure in circulation is derived
from YAML, not observed.**

---

## 5. Summary of what is and is not there

**Already rendered, do not propose:** concealment level (grey ring), spotted flag (red `!`), stance
(gold `A`), suppression (ten-hue pip).

**Computed and rendered by nothing:** the per-observer detection margin (P1), the foliage-vs-weapon
firing gate (P2), the ambush latch (P3), the aim countdown (P4).

**Does not exist at all:** any predictive detection quantity, any record of who saw you or when, any
timestamped ambush event. A readout of "why did my ambush fail" cannot be built from stored state
today — the closest reachable answer is P1 plus P3, which together let a player watch it fail live
rather than reconstruct it afterwards.
