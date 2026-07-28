# Undetected-behavior + literal-ambush design (Ambush stance widening)

**Date:** 2026-07-22
**Type:** Research + critical design. **Doc-only** — no code was written for this.
**Author context:** written from a ground-truth code read (every `file:line` below was verified against `main`, working tree at write time). Cite these when implementing; do not trust memory.
**Origin:** user idea — "units undetected by the enemy behave differently; widen the Ambush concept across moving / attack-moving / idle; stationary ambushers hold until the worthwhile-engagement metric peaks then spring." The user explicitly asked for criticism. §3 is the critique; some of the user's premises do not survive contact with the code.

---

## 0. TL;DR for the implementer

1. **Ambush already exists and is real** (not stock OpenRA): idle Ambush units silently pre-aim and hold fire until spotted-by-enemy or damaged, then coordinate a group volley (`AutoTarget.cs:511-580`). The user's idea is a *widening* of this, not a from-scratch build.
2. **Three of the user's premises are wrong or shaky** and are corrected in §3: (a) prone does **not** reduce detection in this engine, so "go prone to stay hidden" is cosmetic; (b) per-unit strength scans are **not** meaningfully costly at ambush cadence — the fear that drove "use map-layers instead" is largely unfounded; (c) "let the tank pass, shoot it in the back" is actively **punished** by the suppression model for the exact unit the user pictured (AT Specialist), and is doctrinally the *late* spring, not the optimal one.
3. **Two things the user got right and didn't know:** rear/directional armor is genuinely implemented (`DamageWarhead.cs:121-198`), so rear shots do matter; and human-owned units **do** get the influence-stack layers computed (`InfluenceStack.cs:38-48`) — the layers are available to a human-facing behavior, contrary to the worry in the brief.
4. **Recommended build** (§5–§6): gate all new behavior behind a default-off condition; ship in 4 small stages, the first of which (the Phase-3 S4 opt-out) is independently valuable and near-free.

---

## 1. Current-state survey (what exists today, file:line)

### 1.1 The stance axes
`AutoTarget.cs:20-26` declares four orthogonal enums on the single `AutoTarget` trait:
```csharp
public enum UnitStance { HoldFire, Ambush, FireAtWill }          // FIRE discipline — WHEN to fire
public enum EngagementStance { HoldPosition, Defensive, Hunt }   // POSITIONING — WHERE to be
public enum CohesionMode { Tight, Loose, Spread }
public enum ResupplyBehavior { Hold, Auto, Evacuate }
```
Ordinal comparisons are load-bearing (`Stance < UnitStance.Ambush`, `stance <= UnitStance.HoldFire`). Ambush is the middle fire tier. Fire discipline and positioning are **independent axes** — the whole Phase-3 S4 gap (§4.1) is a consequence of the positioning executor reading the *wrong* axis. Full survey: `WORKSPACE/plans/260722_stance_tactical_survey.md`.

### 1.2 What Ambush does today
All in `AutoTarget.cs`. State: `Target ambushPreAimTarget`, `bool ambushTriggered` (`:245-247`).

- **Idle (`AmbushTickIdle`, `:511-541`):** scans at full range (`ScanForTarget`, `:514`); if a target exists, **pre-aims silently** (`PreAimAtTarget`, `:543-561` — turret `FaceTarget` for turreted units, body turn for infantry, *no firing*); then computes `isSpotted = self.CanBeViewedByPlayer(targetOwner)` (`:529`) and opens fire (`Attack(target, false)` — `allowMove:false`, fires from position) only if `isSpotted || ambushTriggered` (`:531`). On spot it calls `TriggerNearbyAmbushAllies` (`:537`).
- **Coordination (`TriggerNearbyAmbushAllies`, `:563-580`):** `FindActorsInCircle` at `AmbushCoordinationRadius = 10` cells (`:83-84`); sets `ambushTriggered = true` on every ally in Ambush stance and calls `GarrisonManager.TriggerAmbush()` on garrisoned buildings. **This already gives "one spotted member springs the group"** — whoever the enemy sees first pushes the trigger onto everyone in radius.
- **Retaliation (`Damaged`, `:441-491`):** `if (Stance < UnitStance.Ambush) return;` — HoldFire never retaliates; Ambush/FireAtWill do. Ambush sets `ambushTriggered` and returns fire.
- **Idle-scan gate (`TickIdle`, `:493-509`):** `if (Stance < UnitStance.Ambush) return;` then Ambush routes to `AmbushTickIdle`, FireAtWill to normal `ScanAndAttack`.
- **Opportunity fire while moving/busy (`AttackFollow.cs:156-157`):** only runs when `Stance >= FireAtWill`. **Consequence that matters a lot for the user's idea:** an Ambush unit that is *moving* neither pre-aims (that path is idle-only) nor opportunity-fires. **A moving Ambush unit today has no special behavior at all.** The "moving / attack-moving" half of the user's request is genuinely unbuilt.
- Reset on stance change: `ResetAmbushState` (`:583-587`) from `SetStance` (`:263-282`).
- Condition wiring: grants `stance-ambush` (`defaults.yaml:305-309`); nothing in the movement/prone system reads it.

### 1.3 Detection — how the sim answers "is X seen by the enemy"
- Per-actor: `Actor.CanBeViewedByPlayer(Player)` (`Actor.cs:591-599`) → `ShouldHide` modifiers (usually 0-1, e.g. Cloak) then `IDefaultVisibility.IsVisible`. For mobile units that resolves to `Detectable.IsVisibleInner` (`Detectable.cs:93-116`), a loop over the actor's occupied cells calling the O(1) cell test.
- Per-cell O(1): `MapLayers.IsVisible(PPos,int)` (`MapLayers.cs:571-577`) is a bounds check + one flat-array byte read + compare (`ProjectedCellLayer.Index`, `ProjectedCellLayer.cs:28-31`). (Shroud is renamed `MapLayers` in this fork; per-player instance is `Player.MapLayers`, `Player.cs:70`.)
- **This is already sim-legal shared logic** — the existing Ambush code calls `CanBeViewedByPlayer` every idle scan (`AutoTarget.cs:529`), and humans get Ambush too. No render-player dependency, deterministic.
- **No "any enemy sees X" helper exists.** You pay `#enemy-players × per-actor cost`, exactly what `SightingThreatLayer.cs:207` and `BeliefStore.cs:219` already do. For the typical 1v1 that is one call.

### 1.4 Prone / suppression / pinning
- **Prone exists** (`InfantryStates.cs`): condition-driven, `ProneCondition = deployed || suppressed > 30 || !moving || critical-damage` (`infantry.yaml:252`), `ProneSpeedModifier = 60`, `ProneDamageModifiers`, `ProneOffset`, `prone-` sequence prefix. **Not tied to any stance.** Any move order stands the unit up (`!moving` clause).
- **Prone gives no detection benefit.** There is a damage modifier and a smaller visual, but **no prone/stance detection modifier** in the engine (survey Q4: "MISSING — no prone/stance-in-cover damage or detection modifier"; only weapon `MissChancePerDensity`). This kills the "go prone to stay hidden" premise (§3.1).
  > **⚠ Correction (2026-07-29): this line is WRONG.** `DetectableAddativeModifier@Prone` (`RequiresCondition: prone`, `VisionModifier: 1`, `infantry.yaml:684-686`) DOES grant +1 detection tier (harder to see), consumed in `Detectable.IsVisibleInner` (`Detectable.cs:93-116`, via `Util.ApplyAddativeModifiers` at `:99`). The survey Q4 quoted here missed the modifier. Fuller note at §3.1; corrected engine-level statement at `DOCS/reference/architecture.md:251`.
- **Suppression does not silence normal weapons.** It is a graduated YAML condition (`suppressed`, cap 100 infantry / 50 vehicle) driving speed/vision/burst/accuracy multipliers (`infantry.yaml:339-504`), prone at >30, but **no general fire-halt**. The *only* weapons that stop firing under suppression are three `PauseOnCondition: suppressed >= 10` armaments: **the AT Specialist's ATGM (`infantry.yaml:1652`)**, a repair arm (`:1865`), and an SF/demolition arm (`:2136`). This is the crux of §3.3.
- "Pinned" as a named mechanic does not exist; high suppression (speed multiplier → 0 at 91-100) is the functional equivalent.

### 1.5 The influence stack (belief / danger / control) — and WHO gets it
`InfluenceStack.Participates` (`InfluenceStack.cs:38-48`) is the gate:
```csharp
if (player == null || player.NonCombatant || player.Spectating) return false;
if (player.IsBot) return player.BotType == ExperimentalBotType;   // "experimental", ai.yaml:43
return player.Playable;                                           // human combatant
```
**Correcting the brief's central worry:** human-owned combatants **do** participate (`:47`), so `BeliefStore` / `DangerFieldLayer` / `ControlField` are all computed for a human player and readable by a human-facing behavior. The real constraints are narrower: (1) among **bots**, only `@experimental` gets a field — Normal/Rush/Turtle/@stable do not; (2) nothing reads `RenderPlayer` — data is keyed per `Player`, so a consumer must query with a specific participating player.
- Read APIs are O(1): `DangerFieldLayer.GroundDanger(Player, CPos)` / `AirDanger(...)` (`DangerFieldLayer.cs:543-557`), `ActiveCells(Player)` (`:574`); `ControlField.ScoreAt/OwnerAt` (`:531-538`); `BeliefStore.Contacts(Player)` (`:289`).
- Cadence: all three `UpdateInterval = 25` ticks, round-robin staggered one participant per sub-slot (`BeliefStore.cs:171-187`, `DangerFieldLayer.cs:295-310`, `ControlField.cs:286-301`). So each player's field refreshes ~once/25 ticks (≈1 s at 25 ticks/s).
- **Semantics for our use:** `DangerFieldLayer(myPlayer, cell)` = danger the *enemy* projects onto `cell`, derived from my fog-legal belief of enemy contacts. That is a usable proxy for "how strong is the enemy near this cell," at O(1), fog-correct, already there for humans + @experimental bots. **But it is danger-weighted, not value-weighted** — it cannot see a juicy undefended supply truck (intensity ∝ weapon threat, ~0 for a truck). That gap matters (§3.2).

### 1.6 Positioning executor & order arbitration
- `StancePositioningExecutor.cs` (Phase-2/3, default-off) nudges *idle* units to threat-facing cover within a leash, keyed on **EngagementStance** and `deployed`/`MaxSuppressionToMove=30`. It does **not** opt out of the **Ambush fire-stance** (`RequiresCondition` at `defaults.yaml:28` has no `!ambush` clause). So today a human-placed Ambush unit can be walked off its chosen cell by the executor — the un-ambush bug (§4.1).
- Order arbitration (survey Q5): bot squad orders re-fire `queued:false` every ~75 ticks and will stomp any activity a stance layer queues, unless the unit is registered in a commitment ledger (`PoiGoalGuard.Ledger`). Human orders are protected by the idle-gate (`IsIdle`).
- Determinism (survey Q6): `CanBeViewedByPlayer` and integer scans are sim-legal; never read `RenderPlayer`/`LocalPlayer`; order any HashSet/Dictionary by ActorID before it gates a synced decision; `SharedRandom` is in the sync hash, `LocalRandom` is not.

### 1.7 Directional / rear armor — it's real
`DamageWarhead.ArmorDirectionPercent` (`DamageWarhead.cs:121-198`) modifies effective armor thickness by the angle between the victim's facing (`victim.Orientation.Yaw`) and impact direction, using a 5-element `Distribution` = front,side,rear,top,bottom. Live example: heavy tank `Distribution: 100,50,25,10,10` (`vehicles.yaml:22`) → **rear takes ~4× effective damage vs front.** BTR-80 `100,80,80,80,60` (`vehicles-russia.yaml:52`). **This is computed inside the normal damage pipeline — a rear shot costs nothing extra; the bonus is automatic whenever geometry puts the shooter behind the target.** So the user's "shoot the tank in the back" instinct has genuine mechanical backing — the disagreement in §3.3 is about *timing*, not whether rear shots matter.

---

## 2. Detection-query feasibility + cost (the user's cost worry, measured)

**Question the brief raised:** is per-unit detection / per-unit strength summation "costly"?

- **Per-unit detection** (`CanBeViewedByPlayer`): a short footprint loop of O(1) array reads plus a couple of interface calls. For a 1-cell infantryman vs one enemy player that is ~1-2 array reads. The idle Ambush path already pays this every 3-8 ticks (`AutoTarget.cs:529`, scan interval `:133,136`). At N ambushers this is `N × #enemy-players` cheap calls. **Verdict: negligible.** Stagger by `ActorID % interval` (existing precedent, `AffectsMapLayer.cs:179-183`) if N is ever large.
- **Per-unit local strength scan** (`FindActorsInCircle` over a ~10-cell kill-zone, sum armed value of fog-visible enemies): `FindActorsInCircle` is a spatial-hash query; a kill zone holds maybe 5-30 actors; summing an integer value with a `CanBeViewedByPlayer` filter per candidate is trivial at a 25-tick cadence. This is the **same order of work** the game already does in `CohesionMoveModifier`, `SightingThreatLayer`, and the belief store every recompute. **Verdict: the user's fear is largely unfounded.** A local actor-scan at ~1 s cadence, staggered, is not a perf problem.
- **Map-layer read** (`DangerFieldLayer.GroundDanger`): genuinely O(1), but (a) only populated for humans + @experimental bots, and (b) danger-weighted, so value-blind. It is a fine *corroborating* cheap aggregate where it exists, but it cannot be the sole worthwhile-engagement signal without missing high-value/low-threat targets.

**Design consequence:** don't contort the design to avoid a cost that isn't there. Use a **local fog-filtered actor-scan** as the primary worthwhile-engagement metric (it can weigh both threat *and* value); optionally corroborate with a `DangerFieldLayer` read where the layer exists. Do not make the behavior *depend* on the layer, or it silently dies for control bots and becomes value-blind for everyone.

---

## 3. Critical review of the user's logic

Numbered: **[HOLDS]** survives the code, **[MISTAKEN]** contradicted by the code, **[UNDERSPECIFIED]** needs a decision.

**3.1 [MISTAKEN] "Go prone / stop to stay hidden."** Prone confers **no detection reduction** in this engine (§1.4). Whether an ambusher is seen depends solely on whether it occupies a cell the enemy's `MapLayers` reveals — posture is irrelevant to that test (`Detectable.IsVisibleInner`, `Detectable.cs:93-116`). The half of the intuition that *does* hold is **stopping**: a moving unit advances into the enemy's revealed cells and gets seen; a unit that halts short stays out of them. So the correct primitive is **"halt before you enter enemy vision,"** not "crouch." Prone can still be applied as **cosmetic** (it reads as an ambush visually) and gives its damage modifier once shooting starts, but do not sell it as concealment. If real prone-concealment is wanted, that is a **new engine mechanic** (a detection modifier gated on prone), out of scope for v1 — see Open Fork A.

> **⚠ Correction (2026-07-29): the "prone confers no detection reduction" premise of this section is FALSIFIED.** Prone DOES grant concealment: `DetectableAddativeModifier@Prone` (`RequiresCondition: prone`, `VisionModifier: 1`, `infantry.yaml:684-686`) adds +1 to the required-vision tier, applied in `Detectable.IsVisibleInner` (`Detectable.cs:93-116`, `Util.ApplyAddativeModifiers` at `:99`) — a prone unit needs the enemy to reveal its cell at a *higher* vision layer to be seen, i.e. it is harder to detect (same mechanism as `@Dugin` / `@InCover1-3`, and the mirror of `@Firing` −2 / `@Moving` −1). Consequences for this design: (1) "go prone to stay hidden" is **real, not cosmetic**; (2) **Open Fork A's option (b)** ("add a new prone/stationary detection-reduction modifier") is largely **moot** — the mechanic already ships, so the fork narrows to whether v1 *leans on* it. What still holds: **stopping** (not advancing into revealed cells) remains the dominant lever — prone's single tier is a smaller effect layered on top, not a substitute for halting. Corrected engine-level statement: `DOCS/reference/architecture.md:251` (fixed 2026-07-28). The original 2026-07-22 write trusted an incorrect detection survey; recon `WORKSPACE/recon/260728-deploy-prone.md` found the `@Prone` modifier.

**3.2 [UNDERSPECIFIED / partly MISTAKEN] "Use aggregated map-layer reads instead of per-unit strength calcs because the latter are costly."** The cost premise is wrong (§2). Worse, the map layer the user is reaching for (`DangerFieldLayer`) is **value-blind**: it scores weapon threat, so it cannot represent the classic reinforcement-lane target — an undefended supply-truck convoy (game model explicitly calls out lane ambushes, `game-model.md` "Map-edge spawning"). A pure danger-layer trigger would *ignore* the juiciest ambush in the game. **Recommendation:** primary metric = local actor-scan that sums a weighted `threat + value` over fog-visible enemies; use the danger layer only as an optional O(1) corroborator where present. This keeps the behavior working for control/@stable bots (who have no layer) and for the truck-convoy case.

**3.3 [MISTAKEN as stated] "Hold while worthwhile is increasing; after N decreasing checks, spring — shoot the tank in the back as it drives away."** Three problems:
- **Doctrinally it's the late spring, not the optimal one.** A worthwhile-engagement aggregate over the kill zone *peaks* when the column is fully inside. Springing on the *decrease* means you deliberately wait until targets have started *leaving* the zone — giving up shots at the moment of maximum targets-in-range. Real AT-ambush doctrine initiates at peak density-in-zone (lead vehicle reaching the far limit), with the highest-casualty weapon first — not on the way out.
- **The suppression model punishes exactly the user's unit.** The AT Specialist's ATGM has `PauseOnCondition: suppressed >= 10` (`infantry.yaml:1652`). If you let the tank pass and it (or its escort) returns fire, your AT gunner accrues suppression and **stops launching** before it can exploit the rear arc. The "shoot it in the back as it leaves" fantasy is the scenario most likely to end with your AT gunner suppressed and silent. First-strike-from-concealment (pre-aimed alpha volley, §1.2) avoids this because the first missile is away before any return fire lands.
- **What *does* hold:** rear armor is real (§1.7), so *if* rear geometry is available at the optimal initiation moment, it's a bonus — but you don't need to wait past the peak to get it (an L-shaped ambush gets flank/rear arcs at peak). And there is a genuine, defensible *optimal-stopping* kernel buried in the idea: **hold while the situation is improving, spring when the best available strike is about to degrade.** The fix is the *signal*: not "aggregate started decreasing" but "the best engageable target now in range is about to leave range, and nothing better is arriving." §5 reframes the state machine around that.

**3.4 [HOLDS] "One spotted member springs the whole group."** Already true via `TriggerNearbyAmbushAllies` (`AutoTarget.cs:563-580`) at 10-cell radius. Widening it to moving/attack-move groups is the new work, but the coordination primitive is done.

**3.5 [HOLDS, with a caveat] "Undetected units should behave differently across moving / attack-moving / idle."** Idle is built; moving/attack-move is not (§1.2, `AttackFollow.cs:156`). The caveat is **human intent**: silently halting a unit a human explicitly `Move`-ordered will read as disobedience. Scope the halt-and-conceal behavior to **attack-move and auto-movement**, where "stop when you find a fight" is the expected contract — not to plain `Move` (Open Fork B).

**3.6 [UNDERSPECIFIED] The trend-check itself** has degenerate cases the naive "N decreasing" rule mishandles — enumerated and fixed in §5.2.

---

## 4. Interaction with the existing stack (must-handle list)

**4.1 Positioning executor un-ambushes the ambusher (the Phase-3 S4 gap).** `StancePositioningExecutor` keys opt-out on EngagementStance/`deployed`, not fire-stance, so it can walk a human's Ambush unit off its chosen cell (`260722_phase3_redteam.md` S4). **Fix = one clause:** add `!stance-ambush` (and, per the red-team's recommendation, `!stance-holdfire`) to the executor's `RequiresCondition` at `defaults.yaml:28`, using the already-present `AmbushCondition` grant. This is Stage 1 — near-free and independently valuable.

**4.2 Suppression ↔ return fire.** Springing does not pin most units (normal rifles keep firing suppressed, §1.4), so a sprung infantry ambush stays lethal. The exception — AT Specialist / SF / repair arms silenced at suppression ≥10 — is exactly why late-spring is bad (§3.3) and why the alpha volley should fire *before* the target's escort can return fire.

**4.3 AutoTarget hold-fire is already the substrate.** The new behavior extends `AmbushTickIdle` and adds a moving path; hold-fire is expressed by simply not calling `Attack` until a spring trigger fires. No new fire-gating primitive needed.

**4.4 Bots setting Ambush (Stage-D/F consumer).** An `@experimental` bot placing Ambush units on a reinforcement lane / chokepoint is the natural strategic consumer (game model: lanes are ambushable both ways). That is downstream of the influence-stack strategy layer and should be a *later* stage, benchmark-gated, not part of the core behavior stages.

**4.5 Order arbitration for bots.** If a bot sets Ambush and the squad FSM still owns the unit, the 75-tick `queued:false` re-issue will stomp the hold. A bot-driven ambusher must be committed in `PoiGoalGuard.Ledger` (survey Q5 §3) so offense/capture modules skip it. Human ambushers are protected by the idle-gate.

**4.6 AI-realism doctrine grounding (one pass).** Real small-unit ambush doctrine: **near vs far ambush** (near = inside grenade range, immediate assault; far = stand-off, fires only), **L-shape** (one leg down the axis of movement for enfilade, the short leg across it — yields flank/rear arcs at peak, no waiting required), **initiation discipline** (ambush is sprung on a single signal, ideally the highest-casualty weapon, so the whole volley lands before the enemy reacts), and **kill-zone geometry** (spring when the enemy is maximally inside, not as they leave). The recommended state machine (§5) encodes initiation-on-signal + peak-timing + group-simultaneity, which is doctrinally sound; the user's "shoot as it leaves" is the one part that diverges from doctrine.

---

## 5. Recommended design

Two sub-behaviors, both gated behind one default-off condition (`enable-ambush-tactics`, §6). Both extend `AutoTarget` (the survey's "one host trait" constraint) rather than inventing a parallel system. State lives either on `AutoTarget` (simplest) or a small sibling controller trait it delegates to.

### 5.1 Sub-behavior A — moving / attack-move concealment ("halt before contact")
When a unit is in Ambush stance and **auto-moving or attack-moving** (not plain player `Move`):
- Run the same full-range scan as idle. If it finds a valid enemy **and no group member is yet visible to that enemy** (`!CanBeViewedByPlayer` for each of self + allies within coordination radius), **halt** (stop the move activity / do not advance) and pre-aim. Do **not** advance into the enemy's vision.
- If any group member becomes visible to the target owner, or is damaged → **spring** (reuse `TriggerNearbyAmbushAllies` + `Attack`).
- Prone is optional cosmetic for infantry (no detection value; be honest — §3.1).
- **Human-intent guard:** only intercept **attack-move / auto-movement**. A plain `Move` is an explicit order to be somewhere; leave it alone (Open Fork B).

### 5.2 Sub-behavior B — stationary literal-ambush state machine (replaces "N decreasing")
States: **DORMANT** → **TRACKING** → **SPRUNG** (terminal until stance reset).

- **DORMANT → TRACKING:** scan finds ≥1 engageable enemy (valid weapon, in range, fog-visible-to-me) while the group is undetected. Begin pre-aiming the best target.
- **TRACKING (hold fire) → SPRUNG** on the *first* of these triggers:
  1. **Detected** — any group member `CanBeViewedByPlayer(enemy)` (existing spot trigger).
  2. **Damaged** — took fire (existing).
  3. **Best-strike degrading** — the highest-weighted engageable target currently in range is predicted to **exit weapon range within K ticks** (predict from its velocity/heading vs the unit's max range) AND the current worthwhile score ≥ `MinSpringThreshold`. *This is the reframed "spring near the optimal point": fire when the best available shot is about to be lost, not when an aggregate starts falling.*
  4. **Saturation** — worthwhile score ≥ `HighSpringThreshold` sustained for `T` ticks (column fully in zone, or enemy stopped in the kill zone). Handles the "enemy never decreases" degenerate case.
  5. **Overrun** — any engageable enemy breaches `MinRange` (about to walk on top of the ambush).
- **Worthwhile score** = local fog-filtered `FindActorsInCircle(killZoneRadius)` summing `w_threat·threatValue + w_value·cellValue` over enemies visible to my player, at a 25-tick staggered cadence. Optionally add `DangerFieldLayer.GroundDanger(self.Owner, cell)` as an O(1) corroborator where the layer exists. Track the running **peak** and the **best single engageable target** (for triggers 3/5).
- **Degenerate-case coverage** (§3.6): enemy-stops → trigger 4; oscillation/noise → require hysteresis (net change beyond an epsilon band; count consecutive degrade ticks, not single-sample) on trigger 3; fast convoy passing before checks complete → trigger 3 keys on *exit prediction* not sample count, so it fires regardless of cadence; multiple groups → the aggregate can mask a peak, accepted for v1 (trigger 5 + the best-target track bound the worst case); threshold units → threat in `DangerKernelMath`-comparable units, value in cost units, weights tuned in autotest.

### 5.3 Data sources & traits touched
- **Detection:** `Actor.CanBeViewedByPlayer` (`Actor.cs:591-599`) — sim-legal, already used.
- **Targets / range:** `AutoTarget.ScanForTarget` (`:598-622`), armament range for exit prediction.
- **Local aggregate:** `World.FindActorsInCircle` + fog filter (the cheap primitive, §2).
- **Optional corroborator:** `DangerFieldLayer` / `ControlField` reads (humans + @experimental only).
- **Traits touched:** `AutoTarget` (extend `AmbushTickIdle`, add moving path + state machine, all behind the condition gate); `StancePositioningExecutor` `RequiresCondition` (the §4.1 opt-out clause); `defaults.yaml` (condition + grant wiring). No new UI (reuse the existing Ambush button / `UnitDefaultsManager` plumbing).
- **Determinism:** integer math; order the `FindActorsInCircle` result by ActorID before it gates the spring; no `RenderPlayer`; if any `SharedRandom` is ever drawn, it must be drawn identically for @stable (it won't be — the behavior is condition-gated off there).

---

## 6. Staged implementation plan

Each stage: gated behind `enable-ambush-tactics` (default-off) OR `enable-ai-experimental`; **@stable + control bots byte-identical** (they never grant the condition); autotest per `DOCS/recipes/AUTOTEST.md`; benchmark gate before any thought of default-on; NUnit pin where logic is table-like. Worktrees under `C:\Users\fredr\worktrees\ww3mod\`.

| Stage | Content | Depends on | Verify |
|---|---|---|---|
| **1** | **Ambush positioning opt-out** (§4.1): add `!stance-ambush` (+`!stance-holdfire`) to the executor's `RequiresCondition`. Fixes Phase-3 S4. | nothing | autotest: place Ambush unit behind cover, march an enemy at it, assert the unit does **not** relocate; visual check |
| **2** | **Halt-before-contact** for attack-move/auto-move Ambush units (§5.1): stop when an undetected enemy is scanned, spring on detection/damage. | 1 | autotest: Ambush squad attack-moves toward an enemy patrol; assert halt-before-detection, then simultaneous fire-on-contact |
| **3** | **Stationary state machine** (§5.2): local actor-scan worthwhile metric + triggers 1-5, behind the condition. | 2 | autotest: convoy passes a stationary AT ambush — assert spring near optimal (before the column clears), plus the enemy-stops and fast-convoy cases both fire; NUnit for the trigger table |
| **4** | **Consumers & corroboration** (optional, later): `@experimental` bot sets Ambush on reinforcement-lane guards (register in `PoiGoalGuard.Ledger`, §4.5); optional `DangerFieldLayer` corroborator read. | 3, influence stack | benchmark-priced; autotest: bot ambush on a lane kills passing reinforcements without stomping by the squad FSM |

**Ordering rationale:** Stage 1 is a near-free, independently valuable bug fix (a human Ambush placement stops being silently defeated) and de-risks everything after. Stage 2 delivers the visible "moving ambush" payoff with the smallest new surface. Stage 3 is the heart (the reframed trend logic). Stage 4 is the bot/strategic consumer and carries the benchmark cost, so it goes last.

**Governance (per the split SPEC):** this is **shared human+bot unit behavior**, so it ships **default-off behind the condition**, is **benchmark-priced before any default-on**, keeps **byte-identity for @stable / control bots** (they never grant `enable-ambush-tactics`), and if it is ever promoted to default-on for everyone, that is a **declared re-baseline** of the ladder. The existing Ambush stance is already live for all players, so the new logic must be strictly additive behind the gate — do not alter the current `AmbushTickIdle` behavior on the ungated path.

---

## 7. Open forks for the user (genuine taste/scope decisions)

**A. Prone semantics.** Since prone gives no concealment today (§3.1): (a) **skip prone in v1** (honest, cheapest — recommended), (b) add a **new prone/stationary detection-reduction modifier** (a real concealment mechanic, larger scope, affects balance and the detection substrate), or (c) prone as **pure cosmetic** on infantry ambushers. Which?

**B. Moving-ambush scope.** Apply halt-before-contact to **attack-move + auto-move only** (recommended — matches player intent), or **also to plain `Move`** (stronger "never be seen first" guarantee, but silently disobeys an explicit move order)?

**C. Spring-timing doctrine.** Default to **peak-density initiation** (doctrinally sound; alpha volley before return fire; avoids the AT-suppression trap — recommended), or honor the user's **"spring past the peak for rear shots"** intuition (rear armor is real, but it invites the suppression-silence failure for AT units)? A middle option: peak-timing by default, with rear-arc opportunism only when it costs no delay (L-shape geometry).

**D. First-shipment audience.** **Bot-only first** (no human-facing disobedience risk, cleanly benchmark-priced, then extend to humans), or **human + bot behind the opt-in condition from day one** (the user gets to feel it immediately in skirmish)?
